using Gallery.Core.Content;
using Gallery.Core.Images;
using Gallery.Core.Text;
using Gallery.Web.Data;
using Gallery.Web.Observability;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Gallery.Web.Library;

/// <summary>A gallery whose stored <c>nav</c> a projection changed, with its slug after the change (010 contracts/edge.md E2).</summary>
public sealed record GalleryLinkTarget(string Id, string Slug);

/// <summary>
/// What one projection wrote: the home cards, every gallery whose <c>nav</c> it set or unset, and
/// (013 data-model "Write" step 4) one (stored, new) pair per camera key that was added, removed or
/// whose document changed; null on the side where the key has no document.
/// </summary>
public sealed record ProjectionResult(
    IReadOnlyList<GalleryCardDoc> Cards,
    IReadOnlyList<GalleryLinkTarget> NavChanged,
    IReadOnlyList<(CameraGroupDoc? Before, CameraGroupDoc? After)> Cameras);

/// <summary>
/// Recomputes <c>site.homeCards</c> (research R-01 step 4, data-model "Site"): the cards of the
/// galleries in <c>homeOrder</c> order that are public AND hold at least one image (II.6). One read
/// of the site's <c>homeOrder</c>, one read of those galleries, one <c>$set</c> of ONLY
/// <c>homeCards</c> and <c>updatedAt</c> (upsert on <c>_id:"site"</c>), all inside
/// <c>db.site.project</c>. It never touches the owner claim fields (I.4) or <c>homeOrder</c>.
/// The home page stays one read of <c>homeCards</c> (IV.9, SC-007).
/// 010 (research R-01, data-model "Gallery.nav"): in the same timer it is the SINGLE writer of each
/// gallery's <c>nav</c> (<see cref="Nav"/>), writing only the galleries whose stored value differs,
/// without touching their <c>updatedAt</c>.
/// 013 (data-model "Write"): in the same timer it is the SINGLE writer of the derived
/// <c>cameras</c> collection (<see cref="Cameras"/>), writing only the documents that differ.
/// Scoped.
/// </summary>
public sealed class SiteProjector(IMongoDatabase db, OpTimer timer)
{
    public const string SiteId = "site";

    private readonly IMongoCollection<BsonDocument> _site = db.GetCollection<BsonDocument>("site");
    private readonly IMongoCollection<GalleryDoc> _galleries = db.GetCollection<GalleryDoc>("galleries");
    private readonly IMongoCollection<ImageDoc> _images = db.GetCollection<ImageDoc>("images");
    private readonly IMongoCollection<CameraGroupDoc> _cameras = db.GetCollection<CameraGroupDoc>("cameras");

    /// <summary>
    /// [SAFETY] II.2: the LISTED galleries: those named in <paramref name="homeOrder"/>, in that
    /// order (first occurrence), that are public and whose card has <c>imageCount ≥ 1</c>. Ids with
    /// no gallery are skipped. The one rule behind <see cref="Cards"/> and <see cref="Cameras"/>.
    /// </summary>
    public static IReadOnlyList<GalleryDoc> Listed(IEnumerable<string> homeOrder, IEnumerable<GalleryDoc> galleries)
    {
        var byId = galleries.ToDictionary(g => g.Id, StringComparer.Ordinal);
        var listed = new List<GalleryDoc>();
        foreach (var id in homeOrder.Distinct(StringComparer.Ordinal))
        {
            if (!byId.TryGetValue(id, out var g)) continue;
            if (g.Visibility != "public") continue;
            if (g.Card is null || g.Card.ImageCount < 1) continue;
            listed.Add(g);
        }
        return listed;
    }

    /// <summary>
    /// The pure projection: the stored cards of the <see cref="Listed"/> galleries, in
    /// <paramref name="homeOrder"/> order.
    /// </summary>
    public static List<GalleryCardDoc> Cards(IEnumerable<string> homeOrder, IEnumerable<GalleryDoc> galleries) =>
        Listed(homeOrder, galleries).Select(g => g.Card!).ToList();

    /// <summary>
    /// [SAFETY] II.2 / II.5 (013 data-model "Visibility", research R-02, R-03): the camera
    /// documents. An image counts only when its id is in some <paramref name="listed"/> gallery's
    /// <c>items</c>, it has no <c>deletingAt</c> and <see cref="CameraKey.Key"/> is not null; each id
    /// counts once. Items are <c>createdAt</c> desc then <c>_id</c> ordinal; the name comes from the
    /// first item; the result is in rank order (count desc, name ordinal ignore-case, key ordinal).
    /// Slugs: a key keeps its <paramref name="storedSlugs"/> slug while it is unique; every other key
    /// (in ordinal key order) takes <see cref="Slug.From"/> of its name, or the first free
    /// <c>-2</c>, <c>-3</c>…. Deterministic: equal input gives BSON-equal output.
    /// </summary>
    public static List<CameraGroupDoc> Cameras(
        IReadOnlyList<GalleryDoc> listed,
        IReadOnlyList<ImageDoc> images,
        IReadOnlyDictionary<string, string> storedSlugs)
    {
        var visible = new HashSet<string>(listed.SelectMany(g => g.Items), StringComparer.Ordinal);

        var groups = new Dictionary<string, Dictionary<string, ImageDoc>>(StringComparer.Ordinal);
        foreach (var image in images)
        {
            if (!visible.Contains(image.Id)) continue;
            if (image.DeletingAt is not null) continue;
            var key = CameraKey.Key(image.Camera?.Make, image.Camera?.Model);
            if (key is null) continue;
            if (!groups.TryGetValue(key, out var members))
                groups[key] = members = new Dictionary<string, ImageDoc>(StringComparer.Ordinal);
            members.TryAdd(image.Id, image);
        }

        var keys = groups.Keys.Order(StringComparer.Ordinal).ToList();
        var ordered = keys.ToDictionary(k => k, k => groups[k].Values
            .OrderByDescending(i => i.CreatedAt)
            .ThenBy(i => i.Id, StringComparer.Ordinal)
            .ToList(), StringComparer.Ordinal);
        var names = keys.ToDictionary(k => k, k =>
        {
            var first = ordered[k][0];
            return CameraKey.DisplayName(first.Camera!.Make, first.Camera.Model)!;
        }, StringComparer.Ordinal);

        // Slugs: stored slugs first (stability, FR-007), then new ones, both in ordinal key order.
        var slugs = new Dictionary<string, string>(StringComparer.Ordinal);
        var taken = new HashSet<string>(StringComparer.Ordinal);
        foreach (var k in keys)
            if (storedSlugs.TryGetValue(k, out var stored) && !string.IsNullOrEmpty(stored) && taken.Add(stored))
                slugs[k] = stored;
        foreach (var k in keys)
        {
            if (slugs.ContainsKey(k)) continue;
            var baseSlug = Slug.From(names[k]);
            var slug = baseSlug;
            for (var n = 2; !taken.Add(slug); n++) slug = $"{baseSlug}-{n}";
            slugs[k] = slug;
        }

        var ranked = keys
            .OrderByDescending(k => ordered[k].Count)
            .ThenBy(k => names[k], StringComparer.OrdinalIgnoreCase)
            .ThenBy(k => k, StringComparer.Ordinal)
            .ToList();

        var result = new List<CameraGroupDoc>(ranked.Count);
        for (var rank = 0; rank < ranked.Count; rank++)
        {
            var k = ranked[rank];
            var items = ordered[k];
            var cover = items[0];
            result.Add(new CameraGroupDoc
            {
                Id = k,
                Slug = slugs[k],
                Name = names[k],
                Items = items.Select(i => i.Id).ToList(),
                Rank = rank,
                Card = new CameraCardDoc
                {
                    Slug = slugs[k],
                    Name = names[k],
                    ImageCount = items.Count,
                    // The same five fields CardBuilder.Build maps for a gallery cover.
                    Cover = new CoverImageDoc
                    {
                        ImageId = cover.Id,
                        Width = cover.Width,
                        Height = cover.Height,
                        Placeholder = cover.Placeholder,
                        Derivatives = [.. cover.Derivatives],
                    },
                },
            });
        }
        return result;
    }

    /// <summary>
    /// [SAFETY] II.2 (data-model rules 2–3): the <c>nav</c> every existing gallery named in
    /// <paramref name="homeOrder"/> should hold. The sequence is <see cref="Cards"/> (the listed
    /// galleries); neighbours come from <see cref="GalleryNav.Neighbours"/> over its gallery ids. A
    /// listed gallery with at least one neighbour gets a <see cref="GalleryNavDoc"/>; every other
    /// gallery (private, empty, or the only listed one) maps to null, and is never anyone's neighbour.
    /// </summary>
    public static Dictionary<string, GalleryNavDoc?> Nav(IEnumerable<string> homeOrder, IEnumerable<GalleryDoc> galleries)
    {
        var order = homeOrder.ToList();
        var byId = galleries.ToDictionary(g => g.Id, StringComparer.Ordinal);
        var sequence = Cards(order, byId.Values).Select(c => c.GalleryId).ToList();

        var nav = new Dictionary<string, GalleryNavDoc?>(StringComparer.Ordinal);
        foreach (var id in order.Distinct(StringComparer.Ordinal))
            if (byId.ContainsKey(id)) nav[id] = null;

        foreach (var id in sequence)
        {
            var (prev, next) = GalleryNav.Neighbours(sequence, id);
            if (prev is null && next is null) continue;
            nav[id] = new GalleryNavDoc { Prev = Link(prev), Next = Link(next) };
        }
        return nav;

        GalleryLinkDoc? Link(string? id) =>
            id is null ? null : new GalleryLinkDoc { Id = id, Slug = byId[id].Slug, Title = byId[id].Title };
    }

    /// <summary>Writes and returns the projected <c>homeCards</c> (and each gallery's <c>nav</c>).</summary>
    public async Task<IReadOnlyList<GalleryCardDoc>> ProjectAsync() =>
        (await ProjectWithNavAsync()).Cards;

    /// <summary>
    /// Writes the projected <c>homeCards</c>, then <c>$set</c>s or <c>$unset</c>s <c>nav</c> on every
    /// gallery whose stored value differs from <see cref="Nav"/> (BSON equality; null and absent are
    /// equal), with no <c>updatedAt</c> change. Returns the cards and the galleries whose <c>nav</c>
    /// changed (contracts/edge.md E2).
    /// </summary>
    public Task<ProjectionResult> ProjectWithNavAsync() =>
        timer.Time("db.site.project", async () =>
        {
            var site = await _site
                .Find(Builders<BsonDocument>.Filter.Eq("_id", SiteId))
                .Project(Builders<BsonDocument>.Projection.Include("homeOrder"))
                .FirstOrDefaultAsync();

            var order = site is not null && site.TryGetValue("homeOrder", out var o) && o.IsBsonArray
                ? o.AsBsonArray.Where(v => v.IsString).Select(v => v.AsString).ToList()
                : [];

            var galleries = order.Count == 0
                ? []
                : await _galleries.Find(Builders<GalleryDoc>.Filter.In(g => g.Id, order)).ToListAsync();
            var cards = Cards(order, galleries);

            var cardsBson = new BsonArray(cards.Select(c => c.ToBsonDocument()));
            await _site.UpdateOneAsync(
                Builders<BsonDocument>.Filter.Eq("_id", SiteId),
                Builders<BsonDocument>.Update
                    .Set("homeCards", cardsBson)
                    .Set("updatedAt", DateTime.UtcNow),
                new UpdateOptions { IsUpsert = true });

            var byId = galleries.ToDictionary(g => g.Id, StringComparer.Ordinal);
            var changed = new List<GalleryLinkTarget>();
            foreach (var (gid, nav) in Nav(order, galleries))
            {
                var g = byId[gid];
                if (SameNav(g.Nav, nav)) continue;

                var filter = Builders<GalleryDoc>.Filter.Eq(x => x.Id, gid);
                var update = nav is null
                    ? Builders<GalleryDoc>.Update.Unset(x => x.Nav)
                    : Builders<GalleryDoc>.Update.Set(x => x.Nav, nav);
                await _galleries.UpdateOneAsync(filter, update);
                changed.Add(new GalleryLinkTarget(gid, g.Slug));
            }

            var cameras = await ProjectCamerasAsync(Listed(order, galleries));
            return new ProjectionResult(cards, changed, cameras);
        });

    /// <summary>
    /// 013 data-model "Write": one <c>images</c> find over the listed items (research R-05
    /// projection) and one <c>cameras</c> find, both under <c>db.cameras.load</c>; then, only when
    /// something differs, every delete and replace-upsert inside ONE <c>db.cameras.write</c>.
    /// </summary>
    private async Task<IReadOnlyList<(CameraGroupDoc? Before, CameraGroupDoc? After)>> ProjectCamerasAsync(
        IReadOnlyList<GalleryDoc> listed)
    {
        var ids = listed.SelectMany(g => g.Items).Distinct(StringComparer.Ordinal).ToList();
        var (images, stored) = await timer.Time("db.cameras.load", async () =>
        {
            List<ImageDoc> found = ids.Count == 0
                ? []
                : await _images
                    .Find(Builders<ImageDoc>.Filter.In(i => i.Id, ids))
                    .Project<ImageDoc>(Builders<ImageDoc>.Projection
                        .Include("_id").Include("title").Include("width").Include("height")
                        .Include("placeholder").Include("derivatives")
                        .Include("camera.make").Include("camera.model")
                        .Include("createdAt").Include("deletingAt"))
                    .ToListAsync();
            var current = await _cameras.Find(FilterDefinition<CameraGroupDoc>.Empty).ToListAsync();
            return (found, current);
        });

        var before = stored.ToDictionary(c => c.Id, StringComparer.Ordinal);
        var after = Cameras(listed, images, before.ToDictionary(kv => kv.Key, kv => kv.Value.Slug, StringComparer.Ordinal))
            .ToDictionary(c => c.Id, StringComparer.Ordinal);

        var changes = new List<(CameraGroupDoc? Before, CameraGroupDoc? After)>();
        foreach (var key in before.Keys.Union(after.Keys).Order(StringComparer.Ordinal))
        {
            var b = before.GetValueOrDefault(key);
            var a = after.GetValueOrDefault(key);
            if (b is not null && a is not null && b.ToBsonDocument() == a.ToBsonDocument()) continue;
            changes.Add((b, a));
        }
        if (changes.Count == 0) return changes;

        await timer.Time("db.cameras.write", async () =>
        {
            // Deletes first, so a slug a leaving key held is free for the upserts.
            foreach (var (b, _) in changes.Where(c => c.After is null))
                await _cameras.DeleteOneAsync(Builders<CameraGroupDoc>.Filter.Eq(c => c.Id, b!.Id));
            foreach (var (_, a) in changes.Where(c => c.After is not null))
                await _cameras.ReplaceOneAsync(
                    Builders<CameraGroupDoc>.Filter.Eq(c => c.Id, a!.Id), a, new ReplaceOptions { IsUpsert = true });
            return true;
        });
        return changes;
    }

    private static bool SameNav(GalleryNavDoc? stored, GalleryNavDoc? computed) =>
        stored is null || computed is null
            ? stored is null && computed is null
            : stored.ToBsonDocument() == computed.ToBsonDocument();
}
