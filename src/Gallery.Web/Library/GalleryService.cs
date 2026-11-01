using Gallery.Core.Ids;
using Gallery.Core.Text;
using Gallery.Web.Data;
using Gallery.Web.Edge;
using Gallery.Web.Observability;
using Gallery.Web.Storage;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Gallery.Web.Library;

/// <summary>Outcome of THE single-item add gate (<see cref="GalleryService.AddOneAsync"/>).</summary>
public enum AddOutcome
{
    Added,
    /// <summary>Unknown or tombstoned image, or already a member.</summary>
    Skipped,
    /// <summary>The gallery does not exist.</summary>
    NoGallery,
}

/// <summary>
/// Every owner write to a gallery (tasks.md T021, T022; contracts/http.md "Every write"). Each
/// operation is the ordered single-document updates of research R-01 (no transactions, decisions
/// row 15), then an <see cref="OperationScope"/> flush: <see cref="CardBuilder.Build"/> per affected
/// gallery, <see cref="SiteProjector.ProjectAsync"/>, <see cref="PageCacheEvictor.EvictAsync"/> and
/// ONE <see cref="IEdgeCache.Enqueue"/>. [SAFETY] II.5/II.6: no method other than
/// <see cref="PublishAsync"/> sets a gallery public, and that write is conditional on
/// <c>items.0</c> existing. Scoped.
/// </summary>
public sealed class GalleryService
{
    public const string EmptyPublishReason = "Add a photo before making this gallery public";
    public const string StaleOrderReason = "This gallery changed in another tab. Reload to see the current order.";
    public const string StaleHomeOrderReason = "The gallery list changed in another tab. Reload to see the current order.";
    public const string NotAMemberReason = "That photo is not in this gallery.";

    private readonly IMongoDatabase _db;
    private readonly IMongoCollection<GalleryDoc> _galleries;
    private readonly IMongoCollection<ImageDoc> _images;
    private readonly IMongoCollection<BsonDocument> _site;
    private readonly OpTimer _timer;
    private readonly SiteProjector _projector;
    private readonly PageCacheEvictor _evictor;
    private readonly IEdgeCache _edge;
    private readonly EdgeBases _bases;
    private readonly LibraryOptions _options;
    private readonly TimeProvider _clock;
    private readonly ILogger<GalleryService> _log;
    private readonly Func<string, Task>? _beforePublishWrite;

    /// <param name="beforePublishWrite">
    /// Race seam for tests only: runs with the gallery id just before the conditional publish write.
    /// DI never sets it (default null).
    /// </param>
    public GalleryService(
        IMongoDatabase db, OpTimer timer, SiteProjector projector, PageCacheEvictor evictor, IEdgeCache edge,
        EdgeOptions edgeOptions, StorageOptions storage, LibraryOptions options, TimeProvider clock,
        ILogger<GalleryService> log, Func<string, Task>? beforePublishWrite = null)
    {
        _db = db;
        _galleries = db.GetCollection<GalleryDoc>("galleries");
        _images = db.GetCollection<ImageDoc>("images");
        _site = db.GetCollection<BsonDocument>("site");
        _timer = timer;
        _projector = projector;
        _evictor = evictor;
        _edge = edge;
        _bases = new EdgeBases(edgeOptions.SiteBaseUrl, storage.ImageBaseUrl ?? string.Empty);
        _options = options;
        _clock = clock;
        _log = log;
        _beforePublishWrite = beforePublishWrite;
    }

    /// <summary>A new per-request scope; call <see cref="OperationScope.CompleteAsync"/> once at the end.</summary>
    public OperationScope Begin(string op) =>
        new(op, _db, _timer, _projector, _evictor, _edge, _bases, _clock);

    /// <summary>The owner read of one gallery (<c>db.gallery.load</c>), or null.</summary>
    public Task<GalleryDoc?> LoadAsync(string gid) =>
        _timer.Time("db.gallery.load", async () =>
            (GalleryDoc?)await _galleries.Find(Builders<GalleryDoc>.Filter.Eq(g => g.Id, gid)).FirstOrDefaultAsync());

    /// <summary>The title rule (FR-010, R-09): trimmed, 1..<c>TitleMax</c>. Null when valid.</summary>
    public static string? TitleProblem(string? title, LibraryOptions options)
    {
        var t = title?.Trim() ?? string.Empty;
        if (t.Length == 0) return "Enter a title.";
        if (t.Length > options.TitleMax) return $"The title can be at most {options.TitleMax} characters.";
        return null;
    }

    // ── create (L-10: no purge) ──────────────────────────────────────────────

    /// <summary>
    /// Creates a private, empty gallery with no <c>seedKey</c>, appended to <c>homeOrder</c>.
    /// Refused 422 for an empty, blank or over-long title. Result carries the new id.
    /// </summary>
    public async Task<OperationResult> CreateAsync(string? title)
    {
        var problem = TitleProblem(title, _options);
        if (problem is not null) return Refuse("db.gallery.create", 422, problem);

        await Indexes.EnsureOnceAsync(_db);
        var t = title!.Trim();
        var g = new GalleryDoc
        {
            Id = OpaqueId.NewId(),
            Slug = Slug.From(t),
            Title = t,
            Visibility = "private",
            Items = [],
            UpdatedAt = DateTime.UtcNow,
        };
        g.Card = CardBuilder.Build(g, null);

        await _timer.Time("db.gallery.create", async () =>
        {
            await _galleries.InsertOneAsync(g);
            await _site.UpdateOneAsync(
                Builders<BsonDocument>.Filter.Eq("_id", SiteProjector.SiteId),
                Builders<BsonDocument>.Update.AddToSet("homeOrder", g.Id),
                new UpdateOptions { IsUpsert = true });
            return true;
        });

        var scope = Begin("gallery.create");
        await scope.CompleteAsync();
        return OperationResult.Ok() with { GalleryId = g.Id, GalleryTitle = g.Title };
    }

    // ── membership (P-MEMBERS) ───────────────────────────────────────────────

    /// <summary>
    /// THE single-item add gate (FR-012, decisions row 11): an unknown or tombstoned image is
    /// skipped, an image already in the gallery is skipped, otherwise it is appended to
    /// <c>items</c> and the gallery id is added to the image's <c>galleryIds</c>. Every add path
    /// (bulk included) goes through here.
    /// </summary>
    public async Task<AddOutcome> AddOneAsync(string gid, string iid, OperationScope scope)
    {
        await Indexes.EnsureOnceAsync(_db);
        if (!scope.HasBefore(gid))
        {
            var before = await LoadAsync(gid);
            if (before is null) return AddOutcome.NoGallery;
            scope.Touch(GallerySnapshot.From(before), PurgeKind.Members);
        }

        var image = await _timer.Time("db.gallery.load", () =>
            _images.Find(Builders<ImageDoc>.Filter.Eq(i => i.Id, iid)).Project(i => new { i.Id, i.DeletingAt }).FirstOrDefaultAsync());
        if (image is null || image.DeletingAt is not null) return AddOutcome.Skipped;

        var pushed = await _timer.Time("db.gallery.update", () => _galleries.UpdateOneAsync(
            // {items: {$ne: iid}}: matches only when NO element equals iid (not a member yet).
            Builders<GalleryDoc>.Filter.Eq(g => g.Id, gid) & new BsonDocument("items", new BsonDocument("$ne", iid)),
            Builders<GalleryDoc>.Update.Push(g => g.Items, iid).Set(g => g.UpdatedAt, DateTime.UtcNow)));
        if (pushed.MatchedCount == 0) return AddOutcome.Skipped;

        await _timer.Time("db.image.update", () => _images.UpdateOneAsync(
            Builders<ImageDoc>.Filter.Eq(i => i.Id, iid),
            Builders<ImageDoc>.Update.AddToSet(i => i.GalleryIds, gid)));
        return AddOutcome.Added;
    }

    /// <summary>
    /// Adds <paramref name="ids"/> in the given order: <see cref="AddOneAsync"/> once per id inside
    /// one scope, then one card, projection, eviction and ONE <c>EdgeWork</c> (P-MEMBERS).
    /// </summary>
    public async Task<OperationResult> AddAsync(string gid, IReadOnlyList<string> ids)
    {
        var gallery = await LoadAsync(gid);
        if (gallery is null) return OperationResult.NotFound();

        var scope = Begin("gallery.add");
        scope.Touch(GallerySnapshot.From(gallery), PurgeKind.Members);
        int added = 0, skipped = 0;
        foreach (var iid in ids)
        {
            var outcome = await AddOneAsync(gid, iid, scope);
            if (outcome == AddOutcome.Added) added++;
            else skipped++;
        }
        await scope.CompleteAsync();
        return OperationResult.Ok() with { Added = added, Skipped = skipped, GalleryId = gid, GalleryTitle = gallery.Title };
    }

    /// <summary>
    /// Removes one image from the gallery (B5), clearing <c>pinnedCover</c> when it was that image.
    /// Idempotent: a non-member answers Ok with <c>Removed = 0</c>.
    /// </summary>
    public async Task<OperationResult> RemoveAsync(string gid, string iid)
    {
        var gallery = await LoadAsync(gid);
        if (gallery is null) return OperationResult.NotFound();
        if (!gallery.Items.Contains(iid))
            return OperationResult.Ok() with { Removed = 0, GalleryId = gid };

        await Indexes.EnsureOnceAsync(_db);
        var scope = Begin("gallery.remove");
        scope.Touch(GallerySnapshot.From(gallery), PurgeKind.Members);
        await PullAsync(gid, iid);
        await _timer.Time("db.image.update", () => _images.UpdateOneAsync(
            Builders<ImageDoc>.Filter.Eq(i => i.Id, iid),
            Builders<ImageDoc>.Update.Pull(i => i.GalleryIds, gid)));
        await scope.CompleteAsync();
        return OperationResult.Ok() with { Removed = 1, GalleryId = gid };
    }

    /// <summary>
    /// <c>$pull</c>s <paramref name="iid"/> from the gallery's <c>items</c>, then unsets
    /// <c>pinnedCover</c> when it named that image. Two single-document updates, each repeatable.
    /// Shared by remove (B5) and image delete step 2 (R-02).
    /// </summary>
    public async Task PullAsync(string gid, string iid)
    {
        await _timer.Time("db.gallery.update", () => _galleries.UpdateOneAsync(
            Builders<GalleryDoc>.Filter.Eq(g => g.Id, gid) & Builders<GalleryDoc>.Filter.AnyEq(g => g.Items, iid),
            Builders<GalleryDoc>.Update.Pull(g => g.Items, iid).Set(g => g.UpdatedAt, DateTime.UtcNow)));
        await _timer.Time("db.gallery.update", () => _galleries.UpdateOneAsync(
            Builders<GalleryDoc>.Filter.Eq(g => g.Id, gid) & Builders<GalleryDoc>.Filter.Eq(g => g.PinnedCover, iid),
            Builders<GalleryDoc>.Update.Unset(g => g.PinnedCover)));
    }

    /// <summary>
    /// B3 (research R-03, decisions row 5): <paramref name="ids"/> must be exactly the current set
    /// (same members, no duplicates), and the write is filtered on the array read, so a change in
    /// another tab answers <see cref="OperationStatus.Stale"/> and stores nothing.
    /// </summary>
    public async Task<OperationResult> ReorderAsync(string gid, IReadOnlyList<string> ids)
    {
        var gallery = await LoadAsync(gid);
        if (gallery is null) return OperationResult.NotFound();

        var current = gallery.Items;
        var exact = ids.Count == current.Count
                    && ids.Distinct(StringComparer.Ordinal).Count() == ids.Count
                    && ids.ToHashSet(StringComparer.Ordinal).SetEquals(current);
        if (!exact) return Stale();
        if (ids.SequenceEqual(current, StringComparer.Ordinal))
            return OperationResult.Ok() with { GalleryId = gid };

        await Indexes.EnsureOnceAsync(_db);
        var scope = Begin("gallery.reorder");
        scope.Touch(GallerySnapshot.From(gallery), PurgeKind.Members);
        var r = await _timer.Time("db.gallery.update", () => _galleries.UpdateOneAsync(
            Builders<GalleryDoc>.Filter.Eq(g => g.Id, gid) & Builders<GalleryDoc>.Filter.Eq(g => g.Items, current),
            Builders<GalleryDoc>.Update.Set(g => g.Items, ids.ToList()).Set(g => g.UpdatedAt, DateTime.UtcNow)));
        if (r.MatchedCount == 0) return Stale();

        await scope.CompleteAsync();
        return OperationResult.Ok() with { GalleryId = gid };

        OperationResult Stale()
        {
            OpTiming.Log(_log, "db.gallery.update", 0, "stale");
            return OperationResult.Stale(StaleOrderReason);
        }
    }

    /// <summary>
    /// G3 (decisions row 5; tasks.md Batch D blocker, parent): <paramref name="ids"/> must be exactly
    /// the current <c>homeOrder</c> set (same members, no duplicates), and the write is filtered on
    /// the array read, so a change in another tab answers <see cref="OperationStatus.Stale"/> and
    /// stores nothing. P-ORDER purges <c>/</c> only when the order of LISTED galleries changed.
    /// </summary>
    public async Task<OperationResult> ReorderHomeAsync(IReadOnlyList<string> ids)
    {
        var site = await _timer.Time("db.site.order", async () =>
            (BsonDocument?)await _site.Find(Builders<BsonDocument>.Filter.Eq("_id", SiteProjector.SiteId))
                .Project(Builders<BsonDocument>.Projection.Include("homeOrder")).FirstOrDefaultAsync());
        var current = site is not null && site.TryGetValue("homeOrder", out var ho) && ho.IsBsonArray
            ? ho.AsBsonArray.Select(v => v.AsString).ToList()
            : [];

        var exact = ids.Count == current.Count
                    && ids.Distinct(StringComparer.Ordinal).Count() == ids.Count
                    && ids.ToHashSet(StringComparer.Ordinal).SetEquals(current);
        if (!exact) return Stale();
        if (ids.SequenceEqual(current, StringComparer.Ordinal)) return OperationResult.Ok();

        await Indexes.EnsureOnceAsync(_db);
        var galleries = await _timer.Time("db.gallery.load", () =>
            _galleries.Find(Builders<GalleryDoc>.Filter.In(g => g.Id, current)).ToListAsync());
        var scope = Begin("gallery.order");
        scope.TouchOrder(current, ids, galleries.Select(GallerySnapshot.From).ToList());
        var r = await _timer.Time("db.site.order", () => _site.UpdateOneAsync(
            Builders<BsonDocument>.Filter.Eq("_id", SiteProjector.SiteId)
                & Builders<BsonDocument>.Filter.Eq("homeOrder", new BsonArray(current)),
            Builders<BsonDocument>.Update.Set("homeOrder", new BsonArray(ids))));
        if (r.MatchedCount == 0) return Stale();

        await scope.CompleteAsync();
        return OperationResult.Ok();

        OperationResult Stale()
        {
            OpTiming.Log(_log, "db.site.order", 0, "stale");
            return OperationResult.Stale(StaleHomeOrderReason);
        }
    }

    /// <summary>
    /// B4: pins <paramref name="iid"/> as the cover, or unpins when null. A non-member is refused
    /// 422 and nothing is stored.
    /// </summary>
    public async Task<OperationResult> SetCoverAsync(string gid, string? iid)
    {
        var gallery = await LoadAsync(gid);
        if (gallery is null) return OperationResult.NotFound();
        if (iid is not null && !gallery.Items.Contains(iid))
            return Refuse("db.gallery.update", 422, NotAMemberReason);

        await Indexes.EnsureOnceAsync(_db);
        var scope = Begin("gallery.cover");
        scope.Touch(GallerySnapshot.From(gallery), PurgeKind.Members);
        if (iid is null)
        {
            await _timer.Time("db.gallery.update", () => _galleries.UpdateOneAsync(
                Builders<GalleryDoc>.Filter.Eq(g => g.Id, gid),
                Builders<GalleryDoc>.Update.Unset(g => g.PinnedCover).Set(g => g.UpdatedAt, DateTime.UtcNow)));
        }
        else
        {
            var r = await _timer.Time("db.gallery.update", () => _galleries.UpdateOneAsync(
                Builders<GalleryDoc>.Filter.Eq(g => g.Id, gid) & Builders<GalleryDoc>.Filter.AnyEq(g => g.Items, iid),
                Builders<GalleryDoc>.Update.Set(g => g.PinnedCover, iid).Set(g => g.UpdatedAt, DateTime.UtcNow)));
            if (r.MatchedCount == 0) return Refuse("db.gallery.update", 422, NotAMemberReason);
        }
        await scope.CompleteAsync();
        return OperationResult.Ok() with { GalleryId = gid };
    }

    // ── visibility (P-VIS) [SAFETY] II.5, II.6 ───────────────────────────────

    /// <summary>
    /// G4 in the contracted order: load → empty <c>items</c> → refused 409 FIRST, whatever else was
    /// sent → not confirmed → <see cref="OperationStatus.NeedsConfirm"/> with no write → the
    /// conditional write <c>updateOne({_id, "items.0":{$exists:true}}, {$set:{visibility:"public"}})</c>
    /// → matched 0 (emptied in another tab) → the same refusal.
    /// </summary>
    public async Task<OperationResult> PublishAsync(string gid, bool confirmed)
    {
        var gallery = await LoadAsync(gid);
        if (gallery is null) return OperationResult.NotFound();
        if (gallery.Items.Count == 0) return Refuse("db.gallery.update", 409, EmptyPublishReason);
        if (!confirmed) return OperationResult.NeedsConfirm();

        await Indexes.EnsureOnceAsync(_db);
        if (_beforePublishWrite is not null)
            await _beforePublishWrite(gid);

        var scope = Begin("gallery.publish");
        var r = await _timer.Time("db.gallery.update", () => _galleries.UpdateOneAsync(
            Builders<GalleryDoc>.Filter.Eq(g => g.Id, gid) & Builders<GalleryDoc>.Filter.Exists("items.0"),
            Builders<GalleryDoc>.Update.Set(g => g.Visibility, "public").Set(g => g.UpdatedAt, DateTime.UtcNow)));
        if (r.MatchedCount == 0) return Refuse("db.gallery.update", 409, EmptyPublishReason);

        scope.Touch(GallerySnapshot.From(gallery), PurgeKind.Visibility);
        await scope.CompleteAsync();
        OwnerAction("gallery.publish", gid, $"photos={gallery.Items.Count}");
        return OperationResult.Ok() with { GalleryId = gid };
    }

    /// <summary>G4 unpublish: writes at once (hiding is safe and reversible, decisions row 4).</summary>
    public async Task<OperationResult> UnpublishAsync(string gid)
    {
        var gallery = await LoadAsync(gid);
        if (gallery is null) return OperationResult.NotFound();

        await Indexes.EnsureOnceAsync(_db);
        var scope = Begin("gallery.unpublish");
        scope.Touch(GallerySnapshot.From(gallery), PurgeKind.Visibility);
        await _timer.Time("db.gallery.update", () => _galleries.UpdateOneAsync(
            Builders<GalleryDoc>.Filter.Eq(g => g.Id, gid),
            Builders<GalleryDoc>.Update.Set(g => g.Visibility, "private").Set(g => g.UpdatedAt, DateTime.UtcNow)));
        await scope.CompleteAsync();
        OwnerAction("gallery.unpublish", gid, $"photos={gallery.Items.Count}");
        return OperationResult.Ok() with { GalleryId = gid };
    }

    // ── text (P-RENAME / P-DESC) ─────────────────────────────────────────────

    /// <summary>
    /// B2: a null field keeps the stored value (a bare POST changes nothing). Title trimmed,
    /// 1..<c>TitleMax</c>; description 0..<c>DescriptionMax</c> (empty stores null). A title change
    /// recomputes the slug and purges P-RENAME (both slugs when the slug changed, one otherwise);
    /// else a description change purges P-DESC; else nothing.
    /// </summary>
    public async Task<OperationResult> UpdateTextAsync(string gid, string? title, string? description)
    {
        if (title is not null && TitleProblem(title, _options) is { } problem)
            return Refuse("db.gallery.update", 422, problem);
        var desc = description?.Trim();
        if (desc is not null && desc.Length > _options.DescriptionMax)
            return Refuse("db.gallery.update", 422, $"The description can be at most {_options.DescriptionMax} characters.");

        var gallery = await LoadAsync(gid);
        if (gallery is null) return OperationResult.NotFound();

        var newTitle = title?.Trim() ?? gallery.Title;
        var newDesc = description is null ? gallery.Description : (desc!.Length == 0 ? null : desc);
        var titleChanged = newTitle != gallery.Title;
        var descChanged = newDesc != gallery.Description;
        if (!titleChanged && !descChanged)
            return OperationResult.Ok() with { GalleryId = gid };

        await Indexes.EnsureOnceAsync(_db);
        var scope = Begin("gallery.update");
        scope.Touch(GallerySnapshot.From(gallery), titleChanged ? PurgeKind.Rename : PurgeKind.Description);
        var update = Builders<GalleryDoc>.Update
            .Set(g => g.Title, newTitle)
            .Set(g => g.Slug, Slug.From(newTitle))
            .Set(g => g.UpdatedAt, DateTime.UtcNow);
        update = update.Set(g => g.Description, newDesc); // an empty description stores null
        await _timer.Time("db.gallery.update", () => _galleries.UpdateOneAsync(
            Builders<GalleryDoc>.Filter.Eq(g => g.Id, gid), update));
        await scope.CompleteAsync();
        return OperationResult.Ok() with { GalleryId = gid };
    }

    // ── delete (L-17, P-GDELETE) ─────────────────────────────────────────────

    /// <summary>
    /// B6 / L-17: removes the gallery from <c>homeOrder</c>, <c>$pull</c>s it from every member
    /// image's <c>galleryIds</c>, deletes the document, projects the site and purges P-GDELETE. The
    /// photos stay in the library. A missing gallery answers <see cref="OperationStatus.NotFound"/>.
    /// </summary>
    public async Task<OperationResult> DeleteAsync(string gid)
    {
        var gallery = await LoadAsync(gid);
        if (gallery is null) return OperationResult.NotFound();

        await Indexes.EnsureOnceAsync(_db);
        var scope = Begin("gallery.delete");
        scope.Touch(GallerySnapshot.From(gallery), PurgeKind.GalleryDelete);
        await _timer.Time("db.gallery.delete", async () =>
        {
            await _site.UpdateOneAsync(
                Builders<BsonDocument>.Filter.Eq("_id", SiteProjector.SiteId),
                Builders<BsonDocument>.Update.Pull("homeOrder", gid));
            await _images.UpdateManyAsync(
                Builders<ImageDoc>.Filter.In(i => i.Id, gallery.Items) | Builders<ImageDoc>.Filter.AnyEq(i => i.GalleryIds, gid),
                Builders<ImageDoc>.Update.Pull(i => i.GalleryIds, gid));
            await _galleries.DeleteOneAsync(Builders<GalleryDoc>.Filter.Eq(g => g.Id, gid));
            return true;
        });
        await scope.CompleteAsync();
        OwnerAction("gallery.delete", gid, $"photos={gallery.Items.Count}");
        return OperationResult.Ok() with { Deleted = 1, GalleryId = gid };
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private OperationResult Refuse(string op, int status, string reason)
    {
        OpTiming.Log(_log, op, 0, "refused");
        return OperationResult.Refused(status, reason);
    }

    private void OwnerAction(string action, string target, string detail) =>
        _log.LogInformation("owner.action {action} {target} {count} {detail}", action, target, 1, detail);
}
