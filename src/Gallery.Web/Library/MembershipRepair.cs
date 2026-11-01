using Gallery.Web.Data;
using Gallery.Web.Observability;
using MongoDB.Driver;

namespace Gallery.Web.Library;

/// <summary>
/// Rebuilds the derived index <c>images.galleryIds</c> from the authoritative
/// <c>galleries.items</c> (research R-01): one <c>galleries.find({items:{$in:ids}})</c>, then one
/// update per image setting <c>galleryIds</c> to the ids of the galleries holding it (ordinal order),
/// inside <c>db.membership.repair</c>. Repeatable: a second run changes nothing. Scoped.
/// </summary>
public sealed class MembershipRepair(IMongoDatabase db, OpTimer timer)
{
    private readonly IMongoCollection<GalleryDoc> _galleries = db.GetCollection<GalleryDoc>("galleries");
    private readonly IMongoCollection<ImageDoc> _images = db.GetCollection<ImageDoc>("images");

    /// <summary>Returns the number of image documents whose <c>galleryIds</c> changed.</summary>
    public Task<int> RepairAsync(IReadOnlyCollection<string> imageIds) =>
        timer.Time("db.membership.repair", async () =>
        {
            var ids = imageIds.Distinct(StringComparer.Ordinal).ToList();
            if (ids.Count == 0) return 0;

            var holders = await _galleries
                .Find(Builders<GalleryDoc>.Filter.AnyIn(g => g.Items, ids))
                .Project(g => new { g.Id, g.Items })
                .ToListAsync();

            var changed = 0;
            foreach (var iid in ids)
            {
                var galleryIds = holders
                    .Where(g => g.Items.Contains(iid))
                    .Select(g => g.Id)
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(g => g, StringComparer.Ordinal)
                    .ToList();
                var r = await _images.UpdateOneAsync(
                    Builders<ImageDoc>.Filter.Eq(i => i.Id, iid),
                    Builders<ImageDoc>.Update.Set(i => i.GalleryIds, galleryIds));
                changed += (int)r.ModifiedCount;
            }
            return changed;
        });
}
