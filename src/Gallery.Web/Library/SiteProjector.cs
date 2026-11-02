using Gallery.Core.Content;
using Gallery.Web.Data;
using Gallery.Web.Observability;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Gallery.Web.Library;

/// <summary>A gallery whose stored <c>nav</c> a projection changed, with its slug after the change (010 contracts/edge.md E2).</summary>
public sealed record GalleryLinkTarget(string Id, string Slug);

/// <summary>What one projection wrote: the home cards, and every gallery whose <c>nav</c> it set or unset.</summary>
public sealed record ProjectionResult(IReadOnlyList<GalleryCardDoc> Cards, IReadOnlyList<GalleryLinkTarget> NavChanged);

/// <summary>
/// Recomputes <c>site.homeCards</c> (research R-01 step 4, data-model "Site"): the cards of the
/// galleries in <c>homeOrder</c> order that are public AND hold at least one image (II.6). One read
/// of the site's <c>homeOrder</c>, one read of those galleries, one <c>$set</c> of ONLY
/// <c>homeCards</c> and <c>updatedAt</c> (upsert on <c>_id:"site"</c>), all inside
/// <c>db.site.project</c>. It never touches the owner claim fields (I.4) or <c>homeOrder</c>.
/// The home page stays one read of <c>homeCards</c> (IV.9, SC-007).
/// 010 (research R-01, data-model "Gallery.nav"): in the same timer it is the SINGLE writer of each
/// gallery's <c>nav</c> (<see cref="Nav"/>), writing only the galleries whose stored value differs,
/// without touching their <c>updatedAt</c>. Scoped.
/// </summary>
public sealed class SiteProjector(IMongoDatabase db, OpTimer timer)
{
    public const string SiteId = "site";

    private readonly IMongoCollection<BsonDocument> _site = db.GetCollection<BsonDocument>("site");
    private readonly IMongoCollection<GalleryDoc> _galleries = db.GetCollection<GalleryDoc>("galleries");

    /// <summary>
    /// The pure projection: the stored cards of the galleries named in <paramref name="homeOrder"/>,
    /// in that order (first occurrence), that are public and whose card has <c>imageCount ≥ 1</c>.
    /// Ids with no gallery are skipped.
    /// </summary>
    public static List<GalleryCardDoc> Cards(IEnumerable<string> homeOrder, IEnumerable<GalleryDoc> galleries)
    {
        var byId = galleries.ToDictionary(g => g.Id, StringComparer.Ordinal);
        var cards = new List<GalleryCardDoc>();
        foreach (var id in homeOrder.Distinct(StringComparer.Ordinal))
        {
            if (!byId.TryGetValue(id, out var g)) continue;
            if (g.Visibility != "public") continue;
            if (g.Card is null || g.Card.ImageCount < 1) continue;
            cards.Add(g.Card);
        }
        return cards;
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

            return new ProjectionResult(cards, changed);
        });

    private static bool SameNav(GalleryNavDoc? stored, GalleryNavDoc? computed) =>
        stored is null || computed is null
            ? stored is null && computed is null
            : stored.ToBsonDocument() == computed.ToBsonDocument();
}
