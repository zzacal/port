using Gallery.Web.Data;
using Gallery.Web.Observability;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Gallery.Web.Library;

/// <summary>
/// Recomputes <c>site.homeCards</c> (research R-01 step 4, data-model "Site"): the cards of the
/// galleries in <c>homeOrder</c> order that are public AND hold at least one image (II.6). One read
/// of the site's <c>homeOrder</c>, one read of those galleries, one <c>$set</c> of ONLY
/// <c>homeCards</c> and <c>updatedAt</c> (upsert on <c>_id:"site"</c>), all inside
/// <c>db.site.project</c>. It never touches the owner claim fields (I.4) or <c>homeOrder</c>.
/// The home page stays one read of <c>homeCards</c> (IV.9, SC-007). Scoped.
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

    /// <summary>Writes and returns the projected <c>homeCards</c>.</summary>
    public Task<IReadOnlyList<GalleryCardDoc>> ProjectAsync() =>
        timer.Time<IReadOnlyList<GalleryCardDoc>>("db.site.project", async () =>
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

            return cards;
        });
}
