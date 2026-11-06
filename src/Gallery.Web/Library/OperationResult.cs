using Gallery.Web.Data;
using Gallery.Web.Edge;
using Gallery.Web.Observability;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Gallery.Web.Library;

/// <summary>The outcome kind of one owner operation (data-model "Value objects").</summary>
public enum OperationStatus
{
    Ok,
    /// <summary>A gate refused the request; nothing was stored. <see cref="OperationResult.StatusCode"/> and <see cref="OperationResult.Reason"/> say why.</summary>
    Refused,
    /// <summary>A reorder named a set that changed since the page loaded; nothing was stored (409).</summary>
    Stale,
    /// <summary>The gallery or image does not exist (404, or 303 with a zero count for a repeat delete).</summary>
    NotFound,
    /// <summary>A publish without confirmation: the page re-renders the warning; nothing was stored (200).</summary>
    NeedsConfirm,
}

/// <summary>
/// The result of one owner operation (data-model "Value objects"): <c>Ok</c>, <c>Refused(reason,
/// status)</c>, <c>Stale</c>, <c>NotFound</c> or <c>NeedsConfirm</c>, plus the counts the PRG status
/// query carries.
/// </summary>
public sealed record OperationResult
{
    public OperationStatus Status { get; init; }
    public int StatusCode { get; init; } = 200;
    public string? Reason { get; init; }

    public int Added { get; init; }
    public int Skipped { get; init; }
    public int Removed { get; init; }
    public int Deleted { get; init; }
    public int Leftovers { get; init; }
    public int Failed { get; init; }

    /// <summary>The gallery the operation created or wrote to, when there is one.</summary>
    public string? GalleryId { get; init; }
    public string? GalleryTitle { get; init; }

    public bool IsOk => Status == OperationStatus.Ok;

    public static OperationResult Ok() => new() { Status = OperationStatus.Ok };
    public static OperationResult Refused(int statusCode, string reason) =>
        new() { Status = OperationStatus.Refused, StatusCode = statusCode, Reason = reason };
    public static OperationResult Stale(string reason) =>
        new() { Status = OperationStatus.Stale, StatusCode = 409, Reason = reason };
    public static OperationResult NotFound() => new() { Status = OperationStatus.NotFound, StatusCode = 404 };
    public static OperationResult NeedsConfirm() => new() { Status = OperationStatus.NeedsConfirm };
}

/// <summary>What changed about one gallery, which picks its purge set (contracts/edge.md).</summary>
public enum PurgeKind
{
    /// <summary>P-MEMBERS: add, remove, reorder, cover pin/unpin; per gallery of an image delete (P-DELETE).</summary>
    Members,
    /// <summary>P-VIS: publish / unpublish.</summary>
    Visibility,
    /// <summary>P-RENAME: the title changed (both slugs when the slug changed, else one).</summary>
    Rename,
    /// <summary>P-DESC: the description only.</summary>
    Description,
    /// <summary>P-GDELETE: the gallery was deleted.</summary>
    GalleryDelete,
    /// <summary>P-TEXT: a member image's title or caption changed.</summary>
    Text,
}

/// <summary>
/// One owner request's write bookkeeping (tasks.md T021). It collects the BEFORE snapshot (first
/// touch) of every gallery the operation touches, what changed about it, the public keys deleted
/// and the image ids whose text changed. <see cref="CompleteAsync"/> then runs, exactly once:
/// card rebuilds (<see cref="CardBuilder.Build"/>), <see cref="SiteProjector.ProjectWithNavAsync"/>, the
/// AFTER snapshots, the purge set (<see cref="PurgeSets"/>, including P-NAV), local eviction
/// (<see cref="PageCacheEvictor"/>) and ONE <see cref="IEdgeCache.Enqueue"/> carrying
/// <see cref="PurgeSets.WarmFiles"/>. A bulk loop of single-item calls shares one scope, so it
/// flushes once (contracts/edge.md "A bulk operation enqueues ONE EdgeWork"; decisions row 11).
/// Created by <see cref="GalleryService.Begin"/>.
/// </summary>
public sealed class OperationScope
{
    private sealed class Entry(GallerySnapshot? before)
    {
        public GallerySnapshot? Before { get; } = before;
        public HashSet<PurgeKind> Kinds { get; } = [];
        public HashSet<string> TextImages { get; } = new(StringComparer.Ordinal);
    }

    private readonly IMongoCollection<GalleryDoc> _galleries;
    private readonly IMongoCollection<ImageDoc> _images;
    private readonly OpTimer _timer;
    private readonly SiteProjector _projector;
    private readonly PageCacheEvictor _evictor;
    private readonly IEdgeCache _edge;
    private readonly EdgeBases _bases;
    private readonly TimeProvider _clock;

    private readonly Dictionary<string, Entry> _touched = new(StringComparer.Ordinal);
    private readonly HashSet<string> _deletedKeys = new(StringComparer.Ordinal);
    // 010 P-NAV (contracts/edge.md E2): every gallery whose nav any projection in this scope changed,
    // id → slug after the change (the latest projection's slug wins).
    private readonly Dictionary<string, string> _navChanged = new(StringComparer.Ordinal);
    // 013 E3: per camera key, its BEFORE from the first projection result in this scope that names
    // it, and its AFTER from the last (null where the key has no document).
    private readonly Dictionary<string, CameraGroupDoc?> _camBefore = new(StringComparer.Ordinal);
    private readonly Dictionary<string, CameraGroupDoc?> _camAfter = new(StringComparer.Ordinal);
    private bool _completed;

    public string Op { get; }

    internal OperationScope(
        string op, IMongoDatabase db, OpTimer timer, SiteProjector projector, PageCacheEvictor evictor,
        IEdgeCache edge, EdgeBases bases, TimeProvider clock)
    {
        Op = op;
        _galleries = db.GetCollection<GalleryDoc>("galleries");
        _images = db.GetCollection<ImageDoc>("images");
        _timer = timer;
        _projector = projector;
        _evictor = evictor;
        _edge = edge;
        _bases = bases;
        _clock = clock;
    }

    /// <summary>True when <paramref name="galleryId"/> already has its BEFORE snapshot here.</summary>
    public bool HasBefore(string galleryId) => _touched.ContainsKey(galleryId);

    /// <summary>
    /// Records that <paramref name="before"/>'s gallery changed by <paramref name="kind"/>. The first
    /// call per gallery keeps its BEFORE snapshot; later calls only add the kind.
    /// </summary>
    public void Touch(GallerySnapshot before, PurgeKind kind)
    {
        if (!_touched.TryGetValue(before.Id, out var e))
            _touched[before.Id] = e = new Entry(before);
        e.Kinds.Add(kind);
    }

    /// <summary>P-TEXT: <paramref name="imageId"/>'s text changed and <paramref name="holder"/> holds it.</summary>
    public void TouchText(GallerySnapshot holder, string imageId)
    {
        Touch(holder, PurgeKind.Text);
        _touched[holder.Id].TextImages.Add(imageId);
    }

    /// <summary>Public object keys actually deleted (P-DELETE files).</summary>
    public void AddDeletedKeys(IEnumerable<string> keys) => _deletedKeys.UnionWith(keys);

    private (IReadOnlyList<string> Before, IReadOnlyList<string> After, IReadOnlyList<GallerySnapshot> Galleries)? _order;

    /// <summary>P-ORDER (G3): the home order changed from <paramref name="before"/> to <paramref name="after"/>.</summary>
    public void TouchOrder(IReadOnlyList<string> before, IReadOnlyList<string> after, IReadOnlyList<GallerySnapshot> galleries) =>
        _order = (before, after, galleries);

    /// <summary>
    /// Rebuilds the card of each existing gallery in <paramref name="galleryIds"/> with
    /// <see cref="CardBuilder.Build"/> (one read of the galleries, one of their covers, one write per
    /// changed card), then projects the site. Image delete step 2 calls it (research R-02);
    /// <see cref="CompleteAsync"/> calls the card part for every touched gallery. The projection's
    /// <c>NavChanged</c> is kept in the scope, so <see cref="CompleteAsync"/> purges those pages too.
    /// </summary>
    public async Task<IReadOnlyList<GalleryCardDoc>> RefreshAsync(IEnumerable<string> galleryIds)
    {
        await RebuildCardsAsync(galleryIds);
        return await ProjectAsync();
    }

    private async Task<IReadOnlyList<GalleryCardDoc>> ProjectAsync()
    {
        var result = await _projector.ProjectWithNavAsync();
        foreach (var g in result.NavChanged)
            _navChanged[g.Id] = g.Slug;
        foreach (var (before, after) in result.Cameras)
        {
            var key = (before ?? after)!.Id;
            _camBefore.TryAdd(key, before);
            _camAfter[key] = after;
        }
        return result.Cards;
    }

    private async Task<List<GalleryDoc>> RebuildCardsAsync(IEnumerable<string> galleryIds)
    {
        var ids = galleryIds.Distinct(StringComparer.Ordinal).ToList();
        if (ids.Count == 0) return [];

        var galleries = await _timer.Time("db.gallery.load", () =>
            _galleries.Find(Builders<GalleryDoc>.Filter.In(g => g.Id, ids)).ToListAsync());
        var coverIds = galleries.Select(CardBuilder.CoverId).OfType<string>().Distinct(StringComparer.Ordinal).ToList();
        var covers = (await ReadImagesAsync(coverIds)).ToDictionary(i => i.Id, StringComparer.Ordinal);

        foreach (var g in galleries)
        {
            var coverId = CardBuilder.CoverId(g);
            var cover = coverId is not null && covers.TryGetValue(coverId, out var c) ? c : null;
            var card = CardBuilder.Build(g, cover);
            if (g.Card is not null && card.ToBsonDocumentEquals(g.Card)) continue;
            g.Card = card;
            await _timer.Time("db.gallery.update", () => _galleries.UpdateOneAsync(
                Builders<GalleryDoc>.Filter.Eq(x => x.Id, g.Id),
                Builders<GalleryDoc>.Update.Set(x => x.Card, card).Set(x => x.UpdatedAt, DateTime.UtcNow)));
        }
        return galleries;
    }

    private async Task<List<ImageDoc>> ReadImagesAsync(IReadOnlyCollection<string> ids) =>
        ids.Count == 0
            ? []
            : await _timer.Time("db.gallery.load", () =>
                _images.Find(Builders<ImageDoc>.Filter.In(i => i.Id, ids)).ToListAsync());

    /// <summary>
    /// Card rebuilds, site projection, purge set, eviction and ONE enqueue. Runs once; a second call
    /// is a no-op. Enqueues nothing when the purge set is empty (e.g. creating a gallery, L-10).
    /// </summary>
    public async Task CompleteAsync()
    {
        if (_completed) return;
        _completed = true;

        var after = (await RebuildCardsAsync(_touched.Keys))
            .ToDictionary(g => g.Id, GallerySnapshot.From, StringComparer.Ordinal);
        var homeCards = await ProjectAsync();

        var sets = new List<PurgeSet>();
        // P-MEMBERS of every gallery, plus the deleted public keys: PurgeSets.Delete is exactly that
        // union (with no keys it is the union of PurgeSets.Members).
        var members = new List<(GallerySnapshot Before, GallerySnapshot? After)>();
        foreach (var (gid, e) in _touched)
        {
            var b = e.Before;
            var a = after.GetValueOrDefault(gid);
            foreach (var kind in e.Kinds)
            {
                switch (kind)
                {
                    case PurgeKind.Members:
                        if (b is not null) members.Add((b, a));
                        break;
                    case PurgeKind.Visibility: sets.Add(PurgeSets.Visibility(b, a, _bases)); break;
                    case PurgeKind.Rename: sets.Add(PurgeSets.Rename(b, a, _bases)); break;
                    case PurgeKind.Description: sets.Add(PurgeSets.Description(b, a, _bases)); break;
                    case PurgeKind.GalleryDelete:
                        if (b is not null) sets.Add(PurgeSets.GalleryDelete(b, _bases));
                        break;
                    case PurgeKind.Text:
                        var holder = a ?? b;
                        if (holder is not null)
                            foreach (var iid in e.TextImages)
                                sets.Add(PurgeSets.Text(iid, [holder], _bases));
                        break;
                }
            }
        }
        if (members.Count > 0 || _deletedKeys.Count > 0)
            sets.Add(PurgeSets.Delete(members, _deletedKeys, _bases));
        if (_order is { } o)
            sets.Add(PurgeSets.Order(o.Before, o.After, o.Galleries, _bases));
        if (_navChanged.Count > 0)
            sets.Add(PurgeSets.Nav(_navChanged.Select(kv => new GalleryLinkTarget(kv.Key, kv.Value)), _bases));

        // 013 E3: P-CAM over every key whose first BEFORE and last AFTER differ, and P-CAMTEXT for
        // every text-edited image over the latest AFTER cameras the scope holds.
        var camChanges = CameraChanges();
        if (camChanges.Count > 0)
            sets.Add(PurgeSets.Cameras(
                camChanges.Select(c => (Snap(c.Before), Snap(c.After))),
                camChanges.Any(c => CardChanged(c.Before, c.After)),
                _bases));
        var camAfter = _camAfter.Values.OfType<CameraGroupDoc>().ToList();
        var textImages = _touched.Values.SelectMany(e => e.TextImages).Distinct(StringComparer.Ordinal).ToList();
        // A text edit changes no camera document, so its camera is usually not in any projection
        // result: read the cameras holding the remaining ids (one db.cameras.load find, only then).
        var unheld = textImages.Where(iid => !camAfter.Any(c => c.Items.Contains(iid))).ToList();
        if (unheld.Count > 0)
        {
            var held = camAfter.Select(c => c.Id).ToHashSet(StringComparer.Ordinal);
            var removed = _camAfter.Where(kv => kv.Value is null).Select(kv => kv.Key).ToHashSet(StringComparer.Ordinal);
            camAfter.AddRange((await _projector.CamerasHoldingAsync(unheld))
                .Where(c => !held.Contains(c.Id) && !removed.Contains(c.Id)));
        }
        if (textImages.Count > 0 && camAfter.Count > 0)
        {
            var snaps = camAfter.Select(CameraSnapshot.From).ToList();
            foreach (var iid in textImages)
                sets.Add(PurgeSets.CameraText(iid, snaps, _bases));
        }

        var set = PurgeSet.Union(_bases, sets);
        if (set.IsEmpty) return;

        await _evictor.EvictAsync(set.PagePaths);

        // P-NAV pages of galleries this operation did not touch need their AFTER state for warm:
        // one extra read, only when there are any (contracts/edge.md E2).
        var missing = _navChanged.Keys.Where(id => !after.ContainsKey(id)).ToList();
        if (missing.Count > 0)
        {
            var extra = await _timer.Time("db.gallery.load", () =>
                _galleries.Find(Builders<GalleryDoc>.Filter.In(g => g.Id, missing)).ToListAsync());
            foreach (var g in extra)
                after[g.Id] = GallerySnapshot.From(g);
        }

        // Warm files come from the AFTER state already read (decisions row 45): the first-row images
        // of each purged page that still exists.
        var warmIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var path in set.PagePaths)
        {
            var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3 || parts[0] != "g" || !after.TryGetValue(parts[1], out var g)) continue;
            if (parts.Length == 3) warmIds.UnionWith(g.Items.Take(4));
            else if (parts.Length == 4) warmIds.Add(parts[3]);
        }
        // 013 E4: /c/{slug} warms its first 4 items, /c/{slug}/{iid} that photo, from the AFTER
        // cameras the scope already holds (no read).
        var camBySlug = camAfter.ToDictionary(c => c.Slug, StringComparer.Ordinal);
        foreach (var path in set.PagePaths)
        {
            var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2 || parts[0] != "c" || !camBySlug.TryGetValue(parts[1], out var cam)) continue;
            if (parts.Length == 2) warmIds.UnionWith(cam.Items.Take(4));
            else if (parts.Length == 3 && cam.Items.Contains(parts[2])) warmIds.Add(parts[2]);
        }
        var avif = (await ReadImagesAsync(warmIds))
            .Where(i => i.DeletingAt is null)
            .ToDictionary(i => i.Id, i => (IReadOnlyList<string>)i.Derivatives.Select(d => d.Avif).ToList(), StringComparer.Ordinal);
        var cameraCards = camAfter.OrderBy(c => c.Rank).Select(c => c.Card).ToList();
        var warm = PurgeSets.WarmFiles(set, after.Values, homeCards, iid => avif.GetValueOrDefault(iid), cameraCards,
            camAfter.Select(CameraSnapshot.From).ToList());

        _edge.Enqueue(set.ToWork(Op, warm, _clock.GetUtcNow()));
    }

    /// <summary>013 E3: (first BEFORE, last AFTER) of every camera key whose two sides differ.</summary>
    private List<(CameraGroupDoc? Before, CameraGroupDoc? After)> CameraChanges()
    {
        var changes = new List<(CameraGroupDoc? Before, CameraGroupDoc? After)>();
        foreach (var key in _camBefore.Keys.Order(StringComparer.Ordinal))
        {
            var b = _camBefore[key];
            var a = _camAfter[key];
            if (b is null && a is null) continue;
            if (b is not null && a is not null && b.ToBsonDocument() == a.ToBsonDocument()) continue;
            changes.Add((b, a));
        }
        return changes;
    }

    private static CameraSnapshot? Snap(CameraGroupDoc? c) => c is null ? null : CameraSnapshot.From(c);

    /// <summary>The <c>/c</c> listing changed: a side is missing, or the card or rank differs.</summary>
    private static bool CardChanged(CameraGroupDoc? b, CameraGroupDoc? a) =>
        b is null || a is null || b.Rank != a.Rank || b.Card.ToBsonDocument() != a.Card.ToBsonDocument();
}

internal static class CardCompare
{
    public static bool ToBsonDocumentEquals(this GalleryCardDoc a, GalleryCardDoc b) =>
        MongoDB.Bson.BsonExtensionMethods.ToBsonDocument(a) == MongoDB.Bson.BsonExtensionMethods.ToBsonDocument(b);
}
