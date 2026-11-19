using System.Diagnostics;
using Gallery.Web.Data;
using Gallery.Web.Library;
using Gallery.Web.Observability;
using Gallery.Web.Storage;
using MongoDB.Driver;

namespace Gallery.Web.Upload;

/// <summary>
/// 020 U12 join (contracts/http.md U12 steps 3–5, research R-06, tasks.md C-02). Scoped. The
/// endpoint claims the item first (<see cref="BatchStore.TryClaimJoinAsync"/>); then
/// <see cref="JoinAsync"/>, inside <c>upload.join</c>, with at most 8 storage operations in flight:
/// <list type="number">
/// <item>Every staged file is copied with <c>x-amz-copy-source-if-match</c> on the ETag recorded
///   at validation (<see cref="ImageStore.CopyIfMatchAsync"/>, [SAFETY] III.6): the master to
///   <c>masters/{itemId}.jpg</c> in the private bucket, each size to <c>{sha256-16hex}.{ext}</c>
///   in the public bucket with the immutable <c>Cache-Control</c>. A staged file that is missing
///   is accepted only when its destination already exists (an earlier or overlapping join made
///   it). Then <c>ImageDoc{_id=itemId}</c> is upserted with <c>$setOnInsert</c> (no seed fields,
///   <c>galleryIds: []</c>), then the item prefix is deleted.</item>
/// <item><see cref="BatchStore.CompleteJoinAsync"/> pulls the item and records it in
///   <c>added</c>.</item>
/// <item>ONLY when the batch has a destination: <see cref="GalleryService.AddAsync"/> (gallery
///   gone → <see cref="BatchStore.UnsetDestinationAsync"/>). [SAFETY] II.5b: no destination → no
///   gallery call and no edge work; II.5c: no visibility write, ever.</item>
/// </list>
/// A copy refused by if-match fails the item ("changed after validation"): the public names this
/// run created and its master copy are removed, its prefix is deleted and it gets no image.
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
    private readonly LibraryOptions _library;
    private readonly GalleryService _galleries;
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
        TimeProvider clock, LibraryOptions library, GalleryService galleries, ILoggerFactory loggers,
        Func<string, Task>? afterItem = null)
    {
        _store = store;
        _images = images;
        _imageDocs = db.GetCollection<ImageDoc>("images");
        _timer = timer;
        _clock = clock;
        _library = library;
        _galleries = galleries;
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

    /// <summary>
    /// U12 steps 3–5 for one item the endpoint has claimed (stored <c>joining</c>, this attempt).
    /// The whole call is timed <c>upload.join</c>; success logs <c>upload.joined</c>.
    /// </summary>
    public Task<JoinResult> JoinAsync(string batchId, string itemId, string attemptId) =>
        _timer.Time("upload.join", () => RunJoinAsync(batchId, itemId, attemptId));

    private async Task<JoinResult> RunJoinAsync(string batchId, string itemId, string attemptId)
    {
        var start = Stopwatch.GetTimestamp();

        var batch = await _store.GetByIdAsync(batchId);
        var item = batch is { State: BatchStates.Open } ? batch.Items.FirstOrDefault(i => i.ItemId == itemId) : null;
        if (item is null) return JoinResult.NotFound();
        if (!string.Equals(item.AttemptId, attemptId, StringComparison.Ordinal)) return JoinResult.StaleAttempt();
        // [SAFETY] II.5d: only a claimed item is published; the claim accepts succeeded/joining only.
        if (item.State != ItemStates.Joining) return JoinResult.NotActive();

        // A dev database seeded before 008 has a non-partial unique seedKey_1 index, which refuses
        // a second image with no seedKey (decisions row 60): upgrade it once per process first.
        await Indexes.EnsureOnceAsync(_imageDocs.Database);

        using var gate = new SemaphoreSlim(MaxStorageOpsInFlight);
        var outcome = await JoinItemAsync(batchId, item, gate);
        if (outcome == ItemOutcome.Gone) return JoinResult.NotFound();
        if (outcome == ItemOutcome.Failed)
        {
            _log.LogInformation("upload.join-refused {batch} {item} {reason}", batchId, itemId, ChangedAfterValidation);
            return JoinResult.Changed();
        }

        var after = await _store.CompleteJoinAsync(batchId, itemId, itemId);
        // Another join of the same item finished first: it owns the rest (converge, FR-034).
        if (after is null) return JoinResult.NotFound();

        JoinedGallery? gallery = null;
        var galleryGone = false;
        if (after.Destination is { } gid)
        {
            var added = await _galleries.AddAsync(gid, [itemId]);
            var loaded = added.Status == OperationStatus.NotFound ? null : await _galleries.LoadAsync(gid);
            if (loaded is null)
            {
                await _store.UnsetDestinationAsync(batchId);
                galleryGone = true;
            }
            else
            {
                gallery = new JoinedGallery(loaded.Id, loaded.Title, loaded.Visibility == "public");
            }
        }

        var ms = Math.Round(Stopwatch.GetElapsedTime(start).TotalMilliseconds, 2);
        _log.LogInformation("upload.joined {batch} {item} {gallery} {public} {duration_ms}",
            batchId, itemId, gallery?.Id ?? "-", gallery is null ? "-" : gallery.Public ? "true" : "false", ms);
        return JoinResult.Joined(itemId, gallery, galleryGone);
    }

    private enum ItemOutcome { Published, Failed, Gone }

    private sealed record Copy(string SrcKey, string DstBucket, string DstKey, string Etag, string ContentType, string CacheControl);

    /// <summary>
    /// The per-item body (moved from 008's confirm): <c>Published</c> when the item has its image,
    /// <c>Failed</c> when a copy was refused, <c>Gone</c> when the item left the batch meanwhile.
    /// </summary>
    private async Task<ItemOutcome> JoinItemAsync(string batchId, BatchItemDoc item, SemaphoreSlim gate)
    {
        var v = item.Validated
                ?? throw new InvalidOperationException("A joining item has no validated record.");
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

        // A staged file is deleted only AFTER its image upsert, and U8/U9 refuse a joining item. So
        // a missing one is acceptable only when its copy already exists (an interrupted or
        // overlapping join made it); otherwise the item fails.
        var pending = new List<Copy>();
        for (var i = 0; i < all.Count; i++)
        {
            if (heads[i].exists) { pending.Add(all[i]); continue; }
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
        return ItemOutcome.Published;
    }

    /// <summary>
    /// Copy-if-match. A source that vanished between the HEAD and the copy (an overlapping join of
    /// the same item deleted its prefix after publishing) counts as copied only when the
    /// destination exists, the same rule as a missing staged file; otherwise the error stands.
    /// </summary>
    private async Task<CopyOutcome> CopyAsync(Copy c)
    {
        try
        {
            return await _images.CopyIfMatchAsync(_priv, c.SrcKey, c.DstBucket, c.DstKey, c.Etag, c.ContentType, c.CacheControl);
        }
        catch (Amazon.S3.AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            var (exists, _, _) = await _images.HeadAsync(c.DstBucket, c.DstKey);
            if (exists) return CopyOutcome.Copied;
            throw;
        }
    }

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
    /// deleted, and no image created. When the item is no longer in the batch with this attempt
    /// (an overlapping join already finished it), nothing is removed: <c>Gone</c>.
    /// </summary>
    private async Task<ItemOutcome> FailAsync(string batchId, BatchItemDoc item, SemaphoreSlim gate, IReadOnlyList<Copy> createdPublic)
    {
        var marked = await _store.UpdateItemAsync(batchId, item.ItemId, item.AttemptId, Builders<BatchDoc>.Update
            .Set("items.$.state", ItemStates.Failed)
            .Set("items.$.reason", ChangedAfterValidation)
            .Set("items.$.heartbeatAt", _clock.GetUtcNow().UtcDateTime));
        if (!marked) return ItemOutcome.Gone;
        var deletes = createdPublic.Select(c => (c.DstBucket, c.DstKey))
            .Append((_priv, MasterKey(item.ItemId)));
        await Task.WhenAll(deletes.Select(d => Limited(gate, async () =>
        {
            await ImageStore.TryDeleteAsync(() => _images.DeleteAsync(d.Item1, d.Item2), d.Item2, _log);
            return true;
        })));
        await DeleteItemPrefixAsync(batchId, item, gate);
        return ItemOutcome.Failed;
    }

    /// <summary>
    /// <c>$setOnInsert</c> only, so a repeated join never changes an existing image (FR-034).
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

/// <summary>The gallery a joined photo was added to (U12 answer <c>gallery</c>).</summary>
public sealed record JoinedGallery(string Id, string Title, bool Public);

public enum JoinStatus { Joined, NotFound, StaleAttempt, NotActive, ChangedAfterValidation }

/// <summary>Outcome of <see cref="ConfirmService.JoinAsync"/>.</summary>
public sealed record JoinResult(JoinStatus Status, string? ImageId = null, JoinedGallery? Gallery = null, bool GalleryGone = false)
{
    public static JoinResult Joined(string imageId, JoinedGallery? gallery, bool galleryGone) =>
        new(JoinStatus.Joined, imageId, gallery, galleryGone);
    public static JoinResult NotFound() => new(JoinStatus.NotFound);
    public static JoinResult StaleAttempt() => new(JoinStatus.StaleAttempt);
    public static JoinResult NotActive() => new(JoinStatus.NotActive);
    public static JoinResult Changed() => new(JoinStatus.ChangedAfterValidation);
}
