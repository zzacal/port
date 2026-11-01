using Gallery.Web.Data;
using Gallery.Web.Library;
using Gallery.Web.Observability;
using Gallery.Web.Tests.Fixtures;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Gallery.Web.Tests;

/// <summary>
/// 009 T010: <see cref="MembershipRepair.RepairAsync"/> rebuilds <c>images.galleryIds</c> from
/// <c>galleries.items</c> (research R-01). Also T011's proof that <see cref="InvariantChecker"/>
/// can fail: each invariant is broken once on a fresh database, and the checker must throw naming
/// the broken id.
/// </summary>
[Collection("mongo")]
public class MembershipRepairTests(MongoFixture mongo)
{
    private static MembershipRepair Repair(IMongoDatabase db) => new(db, new OpTimer(NullLogger<OpTimer>.Instance));

    private static IMongoCollection<ImageDoc> Images(IMongoDatabase db) => db.GetCollection<ImageDoc>("images");
    private static IMongoCollection<GalleryDoc> Galleries(IMongoDatabase db) => db.GetCollection<GalleryDoc>("galleries");

    private static async Task<List<string>> GalleryIdsOf(IMongoDatabase db, string imageId) =>
        (await Images(db).Find(i => i.Id == imageId).FirstAsync()).GalleryIds;

    private const string G1 = "g1g1g1g1g1g1g1g1g1g1g1g1g2";
    private const string G2 = "g2g2g2g2g2g2g2g2g2g2g2g2g2";
    private const string G3 = "g3g3g3g3g3g3g3g3g3g3g3g3g3";

    [Fact]
    public async Task Stale_ids_are_removed_missing_ids_added_and_orphans_get_empty()
    {
        var db = mongo.NewDatabase();
        var a = SeedShapes.UploadedImage(galleryIds: [G1, G3]);   // G3 holds it: G1 stale, G2 missing
        var b = SeedShapes.UploadedImage(galleryIds: []);         // G2 holds it: missing
        var c = SeedShapes.UploadedImage(galleryIds: [G1, G2]);   // in no gallery: both stale
        await Images(db).InsertManyAsync([a, b, c]);
        await Galleries(db).InsertManyAsync(
        [
            SeedShapes.Gallery([a, b], id: G2, title: "Two", visibility: "private"),
            SeedShapes.Gallery([a], id: G3, title: "Three", visibility: "private"),
            SeedShapes.Gallery([], id: G1, title: "One", visibility: "private"),
        ]);

        var changed = await Repair(db).RepairAsync([a.Id, b.Id, c.Id]);

        Assert.Equal(3, changed);
        Assert.Equal(new[] { G2, G3 }, (await GalleryIdsOf(db, a.Id)).ToArray());
        Assert.Equal(new[] { G2 }, (await GalleryIdsOf(db, b.Id)).ToArray());
        Assert.Empty(await GalleryIdsOf(db, c.Id));
        await InvariantChecker.AssertAsync(db);
    }

    [Fact]
    public async Task A_second_run_changes_nothing()
    {
        var db = mongo.NewDatabase();
        var a = SeedShapes.UploadedImage(galleryIds: [G3]);
        var b = SeedShapes.UploadedImage();
        await Images(db).InsertManyAsync([a, b]);
        await Galleries(db).InsertOneAsync(SeedShapes.Gallery([a, b], id: G1, title: "One", visibility: "private"));

        Assert.Equal(2, await Repair(db).RepairAsync([a.Id, b.Id]));
        var before = await Images(db).Find(FilterDefinition<ImageDoc>.Empty).SortBy(i => i.Id).ToListAsync();

        Assert.Equal(0, await Repair(db).RepairAsync([a.Id, b.Id]));

        var after = await Images(db).Find(FilterDefinition<ImageDoc>.Empty).SortBy(i => i.Id).ToListAsync();
        Assert.Equal(
            before.Select(i => i.ToBsonDocument().ToJson()).ToArray(),
            after.Select(i => i.ToBsonDocument().ToJson()).ToArray());
    }

    [Fact]
    public async Task Only_the_named_images_are_repaired()
    {
        var db = mongo.NewDatabase();
        var a = SeedShapes.UploadedImage(galleryIds: []);
        var other = SeedShapes.UploadedImage(galleryIds: [G3]);
        await Images(db).InsertManyAsync([a, other]);
        await Galleries(db).InsertOneAsync(SeedShapes.Gallery([a, other], id: G1, title: "One", visibility: "private"));

        Assert.Equal(1, await Repair(db).RepairAsync([a.Id]));

        Assert.Equal(new[] { G1 }, (await GalleryIdsOf(db, a.Id)).ToArray());
        Assert.Equal(new[] { G3 }, (await GalleryIdsOf(db, other.Id)).ToArray());
    }

    // ── T011: InvariantChecker can fail, once per invariant ─────────────────────

    /// <summary>A fresh, valid database: two images in one public gallery, the site projected.</summary>
    private async Task<(IMongoDatabase Db, ImageDoc A, ImageDoc B, GalleryDoc G)> ValidAsync()
    {
        var db = mongo.NewDatabase();
        var a = SeedShapes.UploadedImage(title: "A");
        var b = SeedShapes.UploadedImage(title: "B");
        var g = SeedShapes.Gallery([a, b], id: G1, title: "Harbour", visibility: "public");
        SeedShapes.Link([a, b], [g]);
        await Images(db).InsertManyAsync([a, b]);
        await Galleries(db).InsertOneAsync(g);
        await db.GetCollection<SiteDoc>("site").InsertOneAsync(SeedShapes.Site([g]));
        await InvariantChecker.AssertAsync(db); // the baseline is valid
        return (db, a, b, g);
    }

    private static async Task AssertBrokenAsync(IMongoDatabase db, string expectedFragment)
    {
        var ex = await Assert.ThrowsAsync<InvariantChecker.InvariantViolation>(() => InvariantChecker.AssertAsync(db));
        Assert.Contains(expectedFragment, ex.Message);
    }

    [Fact]
    public async Task Checker_fails_on_duplicate_items()
    {
        var (db, a, _, g) = await ValidAsync();
        await Galleries(db).UpdateOneAsync(x => x.Id == g.Id, Builders<GalleryDoc>.Update.Push(x => x.Items, a.Id));
        await AssertBrokenAsync(db, $"gallery {g.Id}: items holds image {a.Id} more than once");
    }

    [Fact]
    public async Task Checker_fails_on_an_item_naming_no_image()
    {
        var (db, _, b, g) = await ValidAsync();
        await Images(db).DeleteOneAsync(x => x.Id == b.Id);
        await AssertBrokenAsync(db, $"gallery {g.Id}: item {b.Id} names no image");
    }

    [Fact]
    public async Task Checker_fails_on_an_item_naming_a_tombstoned_image()
    {
        var (db, _, b, g) = await ValidAsync();
        await Images(db).UpdateOneAsync(x => x.Id == b.Id, Builders<ImageDoc>.Update.Set(x => x.DeletingAt, DateTime.UtcNow));
        await AssertBrokenAsync(db, $"gallery {g.Id}: item {b.Id} names a tombstoned image");
    }

    [Fact]
    public async Task Checker_fails_on_a_pinnedCover_that_is_not_a_member()
    {
        var (db, _, _, g) = await ValidAsync();
        const string stranger = "zzzzzzzzzzzzzzzzzzzzzzzzzz";
        await Galleries(db).UpdateOneAsync(x => x.Id == g.Id, Builders<GalleryDoc>.Update.Set(x => x.PinnedCover, stranger));
        await AssertBrokenAsync(db, $"gallery {g.Id}: pinnedCover {stranger} is not a member");
    }

    [Fact]
    public async Task Checker_fails_on_galleryIds_differing_from_membership()
    {
        var (db, a, _, _) = await ValidAsync();
        await Images(db).UpdateOneAsync(x => x.Id == a.Id, Builders<ImageDoc>.Update.Set(x => x.GalleryIds, [G1, G2]));
        await AssertBrokenAsync(db, $"image {a.Id}: galleryIds [{G1},{G2}] differs from membership [{G1}]");
    }

    [Fact]
    public async Task Checker_fails_on_a_card_differing_from_CardBuilder()
    {
        var (db, _, _, g) = await ValidAsync();
        await Galleries(db).UpdateOneAsync(x => x.Id == g.Id, Builders<GalleryDoc>.Update.Set("card.imageCount", 7));
        await AssertBrokenAsync(db, $"gallery {g.Id}: card ");
    }

    [Fact]
    public async Task Checker_fails_on_homeCards_differing_from_the_projection()
    {
        var (db, _, _, g) = await ValidAsync();
        await db.GetCollection<SiteDoc>("site").UpdateOneAsync(s => s.Id == "site",
            Builders<SiteDoc>.Update.Set(s => s.HomeCards, []));
        await AssertBrokenAsync(db, $"site site: homeCards [] differs from the projection [{g.Id}]");
    }

    [Fact]
    public async Task Checker_names_only_the_broken_invariant()
    {
        var (db, a, _, g) = await ValidAsync();
        await Galleries(db).UpdateOneAsync(x => x.Id == g.Id, Builders<GalleryDoc>.Update.Push(x => x.Items, a.Id));
        var ex = await Assert.ThrowsAsync<InvariantChecker.InvariantViolation>(() => InvariantChecker.AssertAsync(db));
        // A duplicate item also changes the card count; galleryIds, the pin and the site stay valid.
        var lines = ex.Message.Split('\n').Skip(1).ToArray();
        Assert.Equal(2, lines.Length);
        Assert.Equal($"gallery {g.Id}: items holds image {a.Id} more than once", lines[0]);
        Assert.StartsWith($"gallery {g.Id}: card ", lines[1]);
    }
}
