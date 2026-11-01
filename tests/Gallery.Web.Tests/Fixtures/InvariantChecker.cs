using Gallery.Web.Data;
using Gallery.Web.Library;
using MongoDB.Bson;
using MongoDB.Driver;
using Xunit.Sdk;

namespace Gallery.Web.Tests.Fixtures;

/// <summary>
/// The data-model invariants (009 data-model "Invariants", SC-009), checked over a whole database
/// after an operation. <see cref="AssertAsync"/> throws an <see cref="InvariantViolation"/> whose
/// message lists every broken invariant with the gallery or image id it concerns:
/// <list type="number">
/// <item>a gallery's <c>items</c> has no duplicates;</item>
/// <item>every item names an existing, non-tombstoned image;</item>
/// <item><c>pinnedCover</c> is absent or an element of <c>items</c>;</item>
/// <item>every image's <c>galleryIds</c> equals the set of galleries whose <c>items</c> hold it;</item>
/// <item>every gallery's <c>card</c> equals <see cref="CardBuilder.Build"/> of it and its cover image;</item>
/// <item><c>site.homeCards</c> equals what <see cref="SiteProjector"/> would write
///   (<see cref="SiteProjector.Cards"/> over <c>homeOrder</c> and the stored galleries).</item>
/// </list>
/// </summary>
public static class InvariantChecker
{
    public sealed class InvariantViolation(string message) : XunitException(message);

    public static async Task AssertAsync(IMongoDatabase db)
    {
        var galleries = await db.GetCollection<GalleryDoc>("galleries").Find(FilterDefinition<GalleryDoc>.Empty).ToListAsync();
        var images = await db.GetCollection<ImageDoc>("images").Find(FilterDefinition<ImageDoc>.Empty).ToListAsync();
        var site = await db.GetCollection<SiteDoc>("site").Find(s => s.Id == "site").FirstOrDefaultAsync();

        var imagesById = images.ToDictionary(i => i.Id, StringComparer.Ordinal);
        var violations = new List<string>();

        foreach (var g in galleries)
        {
            var dupes = g.Items.GroupBy(i => i, StringComparer.Ordinal).Where(x => x.Count() > 1).Select(x => x.Key).ToList();
            foreach (var d in dupes)
                violations.Add($"gallery {g.Id}: items holds image {d} more than once");

            foreach (var iid in g.Items.Distinct(StringComparer.Ordinal))
            {
                if (!imagesById.TryGetValue(iid, out var img))
                    violations.Add($"gallery {g.Id}: item {iid} names no image");
                else if (img.DeletingAt is not null)
                    violations.Add($"gallery {g.Id}: item {iid} names a tombstoned image");
            }

            if (g.PinnedCover is not null && !g.Items.Contains(g.PinnedCover))
                violations.Add($"gallery {g.Id}: pinnedCover {g.PinnedCover} is not a member");

            var coverId = CardBuilder.CoverId(g);
            var cover = coverId is not null && imagesById.TryGetValue(coverId, out var c) ? c : null;
            var expected = CardBuilder.Build(g, cover).ToBsonDocument();
            var actual = g.Card?.ToBsonDocument();
            if (actual is null || actual != expected)
                violations.Add($"gallery {g.Id}: card {actual?.ToJson() ?? "null"} differs from CardBuilder.Build {expected.ToJson()}");
        }

        foreach (var img in images)
        {
            var expected = galleries.Where(g => g.Items.Contains(img.Id)).Select(g => g.Id)
                .Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToList();
            var actual = img.GalleryIds.Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToList();
            if (!expected.SequenceEqual(actual, StringComparer.Ordinal) || img.GalleryIds.Count != actual.Count)
                violations.Add($"image {img.Id}: galleryIds [{string.Join(",", img.GalleryIds)}] differs from membership [{string.Join(",", expected)}]");
        }

        if (site is not null)
        {
            var expected = new BsonArray(SiteProjector.Cards(site.HomeOrder, galleries).Select(x => x.ToBsonDocument()));
            var actual = new BsonArray(site.HomeCards.Select(x => x.ToBsonDocument()));
            if (actual != expected)
                violations.Add($"site site: homeCards [{string.Join(",", site.HomeCards.Select(x => x.GalleryId))}] differs from the projection [{string.Join(",", expected.Select(x => x["galleryId"].AsString))}]");
        }
        else if (galleries.Any(g => g.Visibility == "public" && g.Items.Count > 0))
        {
            violations.Add("site site: missing while a public, non-empty gallery exists");
        }

        if (violations.Count > 0)
            throw new InvariantViolation("Data-model invariants broken:\n" + string.Join("\n", violations));
    }
}
