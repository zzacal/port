using Gallery.Web.Data;
using Gallery.Web.Edge;
using Gallery.Web.Observability;
using Gallery.Web.Storage;
using Gallery.Web.Upload;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Gallery.Web.Library;

/// <summary>
/// Image delete, exactly per research R-02 (tasks.md T024, FR-014–FR-017, decisions row 16):
/// <list type="number">
/// <item>tombstone: <c>$set deletingAt</c> (<c>db.image.tombstone</c>); a missing image → NotFound;</item>
/// <item>for each gallery whose <c>items</c> holds it: <c>$pull</c>, unset a matching
///   <c>pinnedCover</c>, rebuild the card; then <see cref="SiteProjector.ProjectAsync"/>;</item>
/// <item>storage: <c>masters/{id}.jpg</c> from the private bucket, and each derivative key (AVIF
///   and WebP, every width) from the public bucket UNLESS another image without <c>deletingAt</c>
///   references it (<c>db.images.shared</c>, [SAFETY] shared-file rule); at most 8 in flight; a
///   missing object counts as deleted; a failure logs <c>library.delete_leftover</c>, counts as a
///   leftover and never stops the sequence;</item>
/// <item>delete the document (<c>db.image.delete</c>);</item>
/// <item>P-DELETE with exactly the public keys actually deleted; <c>owner.action image.delete</c>.</item>
/// </list>
/// A repeat on a tombstoned image resumes at step 2. Scoped.
/// </summary>
public sealed class ImageDeleteService
{
    public const int MaxDeletesInFlight = ConfirmService.MaxStorageOpsInFlight;

    private readonly IMongoDatabase _db;
    private readonly IMongoCollection<ImageDoc> _images;
    private readonly IMongoCollection<GalleryDoc> _galleries;
    private readonly OpTimer _timer;
    private readonly GalleryService _galleryService;
    private readonly MembershipRepair _repair;
    private readonly IObjectDeleter _deleter;
    private readonly LibraryOptions _options;
    private readonly ILogger<ImageDeleteService> _log;
    private readonly string _pub;
    private readonly string _priv;
    private readonly Func<string, Task>? _afterGalleries;

    /// <param name="afterGalleries">
    /// Crash seam for tests only: runs with the image id after step 2. DI never sets it (default null).
    /// </param>
    public ImageDeleteService(
        IMongoDatabase db, OpTimer timer, GalleryService galleryService, MembershipRepair repair, IObjectDeleter deleter,
        StorageOptions storage, LibraryOptions options, ILogger<ImageDeleteService> log,
        Func<string, Task>? afterGalleries = null)
    {
        _db = db;
        _images = db.GetCollection<ImageDoc>("images");
        _galleries = db.GetCollection<GalleryDoc>("galleries");
        _timer = timer;
        _galleryService = galleryService;
        _repair = repair;
        _deleter = deleter;
        _options = options;
        _log = log;
        _pub = storage.PublicBucket ?? string.Empty;
        _priv = storage.PrivateBucket ?? string.Empty;
        _afterGalleries = afterGalleries;
    }

    /// <summary>
    /// Deletes one image (L5). With <paramref name="scope"/> null it runs in its own scope and
    /// enqueues its P-DELETE; a bulk caller passes its shared scope and flushes once.
    /// </summary>
    public Task<OperationResult> DeleteAsync(string iid, OperationScope? scope = null) =>
        _timer.Time("library.image.delete", async () =>
        {
            var own = scope is null;
            scope ??= _galleryService.Begin("image.delete");
            var result = await DeleteCoreAsync(iid, scope);
            if (own) await scope.CompleteAsync();
            return result;
        });

    /// <summary>
    /// L4: <paramref name="ids"/> must hold 1..<c>PageSize</c> ids (else 422). Calls
    /// <see cref="DeleteAsync"/> once per id, sequentially, inside one scope, then enqueues ONE
    /// <c>EdgeWork</c>. An id that throws is counted as failed and the loop continues; an unknown id
    /// counts as neither deleted nor failed (it is already gone). Timed as <c>library.bulk.delete</c>.
    /// </summary>
    public Task<OperationResult> DeleteManyAsync(IReadOnlyList<string> ids) =>
        _timer.Time("library.bulk.delete", async () =>
        {
            if (ids.Count == 0 || ids.Count > _options.PageSize)
            {
                OpTiming.Log(_log, "db.image.tombstone", 0, "refused");
                return OperationResult.Refused(422, ids.Count == 0
                    ? "Select at least one photo."
                    : $"Select at most {_options.PageSize} photos.");
            }

            var scope = _galleryService.Begin("image.delete.bulk");
            int deleted = 0, leftovers = 0, failed = 0;
            foreach (var iid in ids.Distinct(StringComparer.Ordinal))
            {
                try
                {
                    var r = await DeleteAsync(iid, scope);
                    deleted += r.Deleted;
                    leftovers += r.Leftovers;
                }
                catch (Exception ex) when (ex is MongoException or TimeoutException)
                {
                    // The image stays tombstoned (or untouched); a repeat finishes it (FR-017).
                    failed++;
                }
            }
            await scope.CompleteAsync();
            return OperationResult.Ok() with { Deleted = deleted, Leftovers = leftovers, Failed = failed };
        });

    private async Task<OperationResult> DeleteCoreAsync(string iid, OperationScope scope)
    {
        await Indexes.EnsureOnceAsync(_db);

        // 1. tombstone (a repeat keeps the first tombstone time and resumes at step 2)
        var image = await _timer.Time("db.image.tombstone", async () =>
        {
            await _images.UpdateOneAsync(
                Builders<ImageDoc>.Filter.Eq(i => i.Id, iid) & Builders<ImageDoc>.Filter.Eq(i => i.DeletingAt, null),
                Builders<ImageDoc>.Update.Set(i => i.DeletingAt, DateTime.UtcNow));
            return (ImageDoc?)await _images.Find(Builders<ImageDoc>.Filter.Eq(i => i.Id, iid)).FirstOrDefaultAsync();
        });
        if (image is null) return OperationResult.NotFound();

        // 2. leave every gallery (query by items, not only galleryIds), rebuild cards, project
        var holders = await _timer.Time("db.gallery.load", () =>
            _galleries.Find(Builders<GalleryDoc>.Filter.AnyEq(g => g.Items, iid)).ToListAsync());
        foreach (var g in holders)
        {
            scope.Touch(GallerySnapshot.From(g), PurgeKind.Members);
            await _galleryService.PullAsync(g.Id, iid);
        }
        // galleryIds is the derived index (R-01): rebuilt from items, so it is [] now.
        await _repair.RepairAsync([iid]);
        await scope.RefreshAsync(holders.Select(g => g.Id));

        if (_afterGalleries is not null)
            await _afterGalleries(iid);

        // 3. storage: master always; public keys only when no live image shares them
        var keys = image.Derivatives
            .SelectMany(d => new[] { d.Avif, d.Webp })
            .Where(k => !string.IsNullOrEmpty(k))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var shared = await SharedKeysAsync(iid, keys);

        var targets = new List<(string Bucket, string Key)> { (_priv, ConfirmService.MasterKey(iid)) };
        targets.AddRange(keys.Where(k => !shared.Contains(k)).Select(k => (_pub, k)));

        var deletedPublic = new List<string>();
        var leftovers = 0;
        using (var gate = new SemaphoreSlim(MaxDeletesInFlight))
        {
            var outcomes = await Task.WhenAll(targets.Select(async t =>
            {
                await gate.WaitAsync();
                try
                {
                    await _deleter.DeleteAsync(t.Bucket, t.Key);
                    return (t.Bucket, t.Key, Ok: true);
                }
                catch (Exception ex)
                {
                    _log.LogWarning("library.delete_leftover {image} {bucket} {key} {error}", iid, t.Bucket, t.Key, ex.GetType().Name);
                    return (t.Bucket, t.Key, Ok: false);
                }
                finally
                {
                    gate.Release();
                }
            }));
            foreach (var o in outcomes)
            {
                if (!o.Ok) leftovers++;
                else if (o.Bucket == _pub) deletedPublic.Add(o.Key);
            }
        }

        // 4. the record goes last, so a crash before here leaves a findable tombstone
        await _timer.Time("db.image.delete", () => _images.DeleteOneAsync(Builders<ImageDoc>.Filter.Eq(i => i.Id, iid)));

        // 5. P-DELETE files: exactly the public keys actually deleted
        scope.AddDeletedKeys(deletedPublic);
        _log.LogInformation("owner.action {action} {target} {count} {detail}",
            "image.delete", iid, 1, $"leftovers={leftovers} galleries={holders.Count}");
        return OperationResult.Ok() with { Deleted = 1, Leftovers = leftovers };
    }

    /// <summary>
    /// [SAFETY] the shared-file rule (Clarification 3, E-17/E-18): the subset of
    /// <paramref name="keys"/> referenced by another image without <c>deletingAt</c>.
    /// </summary>
    private Task<HashSet<string>> SharedKeysAsync(string iid, IReadOnlyCollection<string> keys) =>
        _timer.Time("db.images.shared", async () =>
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            if (keys.Count == 0) return set;
            var f = Builders<ImageDoc>.Filter;
            var others = await _images.Find(
                    f.Ne(i => i.Id, iid)
                    & f.Eq(i => i.DeletingAt, null)
                    & new BsonDocument("$or", new BsonArray
                    {
                        new BsonDocument("derivatives.avif", new BsonDocument("$in", new BsonArray(keys))),
                        new BsonDocument("derivatives.webp", new BsonDocument("$in", new BsonArray(keys))),
                    }))
                .Project(i => i.Derivatives)
                .ToListAsync();
            foreach (var ds in others)
                foreach (var d in ds)
                {
                    if (keys.Contains(d.Avif)) set.Add(d.Avif);
                    if (keys.Contains(d.Webp)) set.Add(d.Webp);
                }
            return set;
        });
}
