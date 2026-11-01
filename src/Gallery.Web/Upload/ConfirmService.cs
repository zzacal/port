using System.Diagnostics;
using Gallery.Web.Data;
using Gallery.Web.Library;
using Gallery.Web.Observability;
using Gallery.Web.Storage;
using MongoDB.Driver;

namespace Gallery.Web.Upload;

/// <summary>
/// U10 confirm (research R-09, contracts/http.md U10, FR-030–FR-033, plan item 6). Scoped.
/// <list type="number">
/// <item>Gate: <see cref="BatchStore.TryStartConfirmAsync"/> (open, ≥1 item, all succeeded).</item>
/// <item>Per item, inside <c>upload.confirm.item</c>, with at most 8 storage operations in
///   flight: every staged file is copied with <c>x-amz-copy-source-if-match</c> on the ETag
///   recorded at validation (<see cref="ImageStore.CopyIfMatchAsync"/>, which sets
///   <c>ETagToMatch</c>): the master to <c>masters/{itemId}.jpg</c> in the private bucket, each
///   size to <c>{sha256-16hex}.{ext}</c> in the public bucket with the immutable
///   <c>Cache-Control</c>. Then <c>ImageDoc{_id=itemId}</c> is upserted with <c>$setOnInsert</c>
///   (no seed fields, <c>galleryIds: []</c>), then the item prefix is deleted.</item>
/// <item><see cref="BatchStore.MarkConfirmedAsync"/> and <c>upload.confirmed</c>, all inside
///   <c>upload.confirm</c>.</item>
/// </list>
/// A copy refused by if-match fails ONLY that item ("changed after validation"): its prefix and
/// master copy are deleted and it gets no image; the other items are added and pulled from the
/// batch, <c>confirmStartedAt</c> is cleared and the answer is 409 (decisions row 45), so the
/// batch is never locked. No purge or warm runs anywhere (FR-033).
/// </summary>
public sealed class ConfirmService
{
    public const string ChangedAfterValidation = "changed after validation";
    public const string ImmutableCacheControl = "public, max-age=31536000, immutable";
    public const string MasterCacheControl = "private, no-store";
    public const int MaxStorageOpsInFlight = 8;
    /// <summary>The documented default of <see cref="LibraryOptions.TitleMax"/>, which is the cap
    /// confirm applies (009 T007).</summary>
    public const int MaxTitleChars = 200;

    private readonly BatchStore _store;
    private readonly ImageStore _images;
    private readonly IMongoCollection<ImageDoc> _imageDocs;
    private readonly OpTimer _timer;
    private readonly TimeProvider _clock;
    private readonly UploadOptions _options;
    private readonly LibraryOptions _library;
    private readonly ILogger _log;
    private readonly string _pub;
    private readonly string _priv;
    private readonly Func<string, Task>? _afterItem;

    /// <param name="afterItem">
    /// Crash seam for tests only: runs after each item's image upsert with the item id. DI never
    /// sets it (default null).
    /// </param>
    public ConfirmService(
        BatchStore store, ImageStore images, StorageOptions storage, IMongoDatabase db, OpTimer timer,
        TimeProvider clock, UploadOptions options, LibraryOptions library, ILoggerFactory loggers,
        Func<string, Task>? afterItem = null)
    {
        _store = store;
        _images = images;
        _imageDocs = db.GetCollection<ImageDoc>("images");
        _timer = timer;
        _clock = clock;
        _options = options;
        _library = library;
        _log = loggers.CreateLogger(UploadEndpoints.LogCategory);
        _pub = storage.PublicBucket ?? string.Empty;
        _priv = storage.PrivateBucket ?? string.Empty;
        _afterItem = afterItem;
    }

    public static string MasterKey(string itemId) => $"masters/{itemId}.jpg";

    /// <summary>data-model "Library image" title: file name without its last extension, trimmed,
    /// at most <paramref name="maxChars"/> characters (confirm passes <c>Library:TitleMax</c>),
    /// "Untitled" when empty (D-13).</summary>
    public static string TitleFrom(string fileName, int maxChars = MaxTitleChars)
    {
        var dot = fileName.LastIndexOf('.');
        var t = (dot >= 0 ? fileName[..dot] : fileName).Trim();
        if (t.Length > maxChars) t = t[..maxChars].TrimEnd();
        return t.Length == 0 ? "Untitled" : t;
    }

    public async Task<ConfirmResult> ConfirmAsync(string batchId)
    {
        var before = await _store.GetByIdAsync(batchId);
        if (before is null) return ConfirmResult.NotFound();
        if (before.State == BatchStates.Confirmed) return ConfirmResult.Confirmed(before.ConfirmedCount ?? 0);
        if (before.State != BatchStates.Open) return ConfirmResult.NotFound();

        // A dev database seeded before 008 has a non-partial unique seedKey_1 index, which refuses
        // a second image with no seedKey (decisions row 60): upgrade it once per process first.
        await Indexes.EnsureOnceAsync(_imageDocs.Database);

        // A run that finds confirmStartedAt already set resumes an earlier, interrupted run.
        var resumed = before.ConfirmStartedAt is not null;

        if (!await _store.TryStartConfirmAsync(batchId))
        {
            // An interrupted run that had already failed an item left the batch locked with a
            // non-succeeded item, so the gate can never pass again. Finish that run instead: the
            // succeeded items are added and the lock is released (no lock-out; decisions row 45).
            if (resumed && before.Items.Count > 0)
                return await _timer.Time("upload.confirm", () => RunAsync(batchId, resumed: true));

            var now = _clock.GetUtcNow();
            var notReady = before.Items.Count(i =>
                EffectiveState.Of(i, now, _options.HeartbeatTimeout) != ItemStates.Succeeded);
            return ConfirmResult.NotReady(notReady);
        }

        return await _timer.Time("upload.confirm", () => RunAsync(batchId, resumed));
    }

    private async Task<ConfirmResult> RunAsync(string batchId, bool resumed)
    {
        var start = Stopwatch.GetTimestamp();

        // Re-read after the gate: while confirmStartedAt is set no other request can change the
        // items (every other mutation answers confirm-in-progress).
        var batch = await _store.GetByIdAsync(batchId)
                    ?? throw new InvalidOperationException("Batch vanished after the confirm gate.");
        var ready = batch.Items.Where(i => i.State == ItemStates.Succeeded).ToList();

        using var gate = new SemaphoreSlim(MaxStorageOpsInFlight);
        var outcomes = await Task.WhenAll(ready.Select(item =>
            _timer.Time("upload.confirm.item", () => ConfirmItemAsync(batchId, item, gate, resumed))));

        var added = ready.Where((_, i) => outcomes[i]).ToList();
        var failed = batch.Items.Count - added.Count;

        if (failed > 0)
        {
            // Copy refused by if-match (contracts/http.md U10, decisions row 45): the added items
            // leave the batch, the lock is released, and the batch stays open with only the
            // failed items, so the owner can Restart or Delete them.
            foreach (var item in added)
                await _store.PullItemAsync(batchId, item.ItemId);
            await _store.ClearConfirmStartedAsync(batchId);
            return ConfirmResult.Changed(added.Count, failed);
        }

        await _store.MarkConfirmedAsync(batchId, added.Count);
        var ms = Math.Round(Stopwatch.GetElapsedTime(start).TotalMilliseconds, 2);
        _log.LogInformation("upload.confirmed {batch} {count} {duration_ms}", batchId, added.Count, ms);
        return ConfirmResult.Confirmed(added.Count);
    }

    private sealed record Copy(string SrcKey, string DstBucket, string DstKey, string Etag, string ContentType, string CacheControl);

    /// <summary>Returns true when the item has its image, false when it failed.</summary>
    private async Task<bool> ConfirmItemAsync(string batchId, BatchItemDoc item, SemaphoreSlim gate, bool resumed)
    {
        var v = item.Validated
                ?? throw new InvalidOperationException("A succeeded item has no validated record.");
        var a = item.AttemptId;

        var master = new Copy(StagingKeys.Master(batchId, item.ItemId, a), _priv, MasterKey(item.ItemId),
            v.MasterEtag, "image/jpeg", MasterCacheControl);
        var sizes = v.Sizes.SelectMany(s => new[]
        {
            new Copy(StagingKeys.Size(batchId, item.ItemId, a, s.W, "avif"), _pub, s.AvifKey, s.AvifEtag, "image/avif", ImmutableCacheControl),
            new Copy(StagingKeys.Size(batchId, item.ItemId, a, s.W, "webp"), _pub, s.WebpKey, s.WebpEtag, "image/webp", ImmutableCacheControl),
        }).ToList();
        List<Copy> all = [master, .. sizes];

        // Existence only: the ETag check is the if-match on each copy below, never a HEAD
        // comparison, so the copy itself is the guard (III.6, decisions row 27).
        var heads = await Task.WhenAll(all.Select(c => Limited(gate, () => _images.HeadAsync(_priv, c.SrcKey))));

        // A staged file is deleted only AFTER its image upsert, and nothing else can delete it
        // while the batch is locked. So a missing one is acceptable only on a resumed run, and only
        // when its copy already exists (the interrupted run made it); otherwise the item fails.
        var pending = new List<Copy>();
        for (var i = 0; i < all.Count; i++)
        {
            if (heads[i].exists) { pending.Add(all[i]); continue; }
            if (!resumed) return await FailAsync(batchId, item, gate, []);
            var c = all[i];
            var (done, _, _) = await Limited(gate, () => _images.HeadAsync(c.DstBucket, c.DstKey));
            if (!done) return await FailAsync(batchId, item, gate, []);
        }

        // Copy-if-match with the validated ETag: the private master first, then every size.
        if (pending.Contains(master)
            && await Limited(gate, () => CopyAsync(master)) == CopyOutcome.PreconditionFailed)
            return await FailAsync(batchId, item, gate, []);

        var sizeCopies = pending.Where(c => c != master).ToList();
        var results = await Task.WhenAll(sizeCopies.Select(c => Limited(gate, async () =>
        {
            // Whether the public name existed before this copy: content-addressed names can be
            // shared with another image (the same file uploaded twice), which a failed item must
            // never delete.
            var (existed, _, _) = await _images.HeadAsync(c.DstBucket, c.DstKey);
            return (Copy: c, Existed: existed, Outcome: await CopyAsync(c));
        })));
        if (results.Any(r => r.Outcome == CopyOutcome.PreconditionFailed))
        {
            // Nothing of a failed item stays public: remove the names this run created.
            var created = results.Where(r => r.Outcome == CopyOutcome.Copied && !r.Existed).Select(r => r.Copy).ToList();
            return await FailAsync(batchId, item, gate, created);
        }

        await _timer.Time("db.image.upsert", () => UpsertImageAsync(item, v));

        if (_afterItem is not null)
            await _afterItem(item.ItemId);

        await DeleteItemPrefixAsync(batchId, item, gate);
        return true;
    }

    private Task<CopyOutcome> CopyAsync(Copy c) =>
        _images.CopyIfMatchAsync(_priv, c.SrcKey, c.DstBucket, c.DstKey, c.Etag, c.ContentType, c.CacheControl);

    private Task DeleteItemPrefixAsync(string batchId, BatchItemDoc item, SemaphoreSlim gate) =>
        Limited(gate, async () =>
        {
            await _images.DeletePrefixAsync(_priv, StagingKeys.ItemPrefix(batchId, item.ItemId), _log);
            return true;
        });

    private static async Task<T> Limited<T>(SemaphoreSlim gate, Func<Task<T>> op)
    {
        await gate.WaitAsync();
        try { return await op(); }
        finally { gate.Release(); }
    }

    /// <summary>
    /// The item fails "changed after validation": stored <c>failed</c> + reason, the public names
    /// this run created for it removed, its private master copy removed, its whole item prefix
    /// deleted, and no image created.
    /// </summary>
    private async Task<bool> FailAsync(string batchId, BatchItemDoc item, SemaphoreSlim gate, IReadOnlyList<Copy> createdPublic)
    {
        await _store.UpdateItemAsync(batchId, item.ItemId, item.AttemptId, Builders<BatchDoc>.Update
            .Set("items.$.state", ItemStates.Failed)
            .Set("items.$.reason", ChangedAfterValidation)
            .Set("items.$.heartbeatAt", _clock.GetUtcNow().UtcDateTime));
        var deletes = createdPublic.Select(c => (c.DstBucket, c.DstKey))
            .Append((_priv, MasterKey(item.ItemId)));
        await Task.WhenAll(deletes.Select(d => Limited(gate, async () =>
        {
            await ImageStore.TryDeleteAsync(() => _images.DeleteAsync(d.Item1, d.Item2), d.Item2, _log);
            return true;
        })));
        await DeleteItemPrefixAsync(batchId, item, gate);
        return false;
    }

    /// <summary>
    /// <c>$setOnInsert</c> only, so a repeat confirm never changes an existing image (FR-032).
    /// No <c>seedKey</c>/<c>sourceHash</c> and no caption are written (FR-035, R-10).
    /// </summary>
    private async Task<bool> UpsertImageAsync(BatchItemDoc item, ValidatedDoc v)
    {
        var u = Builders<ImageDoc>.Update;
        var updates = new List<UpdateDefinition<ImageDoc>>
        {
            u.SetOnInsert(i => i.Title, TitleFrom(item.FileName, _library.TitleMax)),
            u.SetOnInsert(i => i.Width, v.Width),
            u.SetOnInsert(i => i.Height, v.Height),
            u.SetOnInsert(i => i.Placeholder, v.Placeholder),
            u.SetOnInsert(i => i.Derivatives, v.Sizes.OrderBy(s => s.W).Select(s => new DerivativeDoc
            {
                W = s.W,
                Avif = s.AvifKey,
                Webp = s.WebpKey,
                BytesAvif = s.BytesAvif,
                BytesWebp = s.BytesWebp,
            }).ToList()),
            u.SetOnInsert(i => i.GalleryIds, new List<string>()),
            u.SetOnInsert(i => i.CreatedAt, _clock.GetUtcNow().UtcDateTime),
        };
        if (v.Camera is not null)
            updates.Add(u.SetOnInsert(i => i.Camera, v.Camera));

        await _imageDocs.UpdateOneAsync(
            Builders<ImageDoc>.Filter.Eq(i => i.Id, item.ItemId),
            u.Combine(updates),
            new UpdateOptions { IsUpsert = true });
        return true;
    }
}

public enum ConfirmStatus { Confirmed, NotFound, NotReady, ChangedAfterValidation }

public sealed record ConfirmResult(ConfirmStatus Status, int Count = 0, int NotReadyCount = 0, int Added = 0, int Failed = 0)
{
    public static ConfirmResult Confirmed(int count) => new(ConfirmStatus.Confirmed, Count: count);
    public static ConfirmResult NotFound() => new(ConfirmStatus.NotFound);
    public static ConfirmResult NotReady(int notReady) => new(ConfirmStatus.NotReady, NotReadyCount: notReady);
    public static ConfirmResult Changed(int added, int failed) => new(ConfirmStatus.ChangedAfterValidation, Added: added, Failed: failed);
}
