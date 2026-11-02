using Gallery.Web.Data;
using Gallery.Web.Library;
using Gallery.Web.Tests.Fixtures;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Gallery.Web.Tests;

/// <summary>
/// 009 T009: <see cref="SiteProjector.ProjectAsync"/> (data-model "Site", II.6, I.4, SC-007).
/// Runs over the database of an <see cref="AdminHost"/>, resolving the projector from DI.
/// </summary>
[Collection("mongo")]
public class SiteProjectorTests(MongoFixture mongo)
{
    private static async Task<(IMongoDatabase Db, List<GalleryDoc> Galleries)> InsertAsync(AdminHost host)
    {
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IMongoDatabase>();

        var a = SeedShapes.UploadedImage(title: "A");
        var b = SeedShapes.UploadedImage(title: "B");
        var c = SeedShapes.UploadedImage(title: "C");
        var pub1 = SeedShapes.Gallery([a, b], title: "Autumn Walk", visibility: "public");
        var priv = SeedShapes.Gallery([c], title: "Secret Garden", visibility: "private");
        var empty = SeedShapes.Gallery([], title: "Coming Soon", visibility: "public");
        var pub2 = SeedShapes.Gallery([c], title: "Spring Meadow", visibility: "public");
        List<GalleryDoc> galleries = [pub1, priv, empty, pub2];
        SeedShapes.Link([a, b, c], galleries);

        await db.GetCollection<ImageDoc>("images").InsertManyAsync([a, b, c]);
        await db.GetCollection<GalleryDoc>("galleries").InsertManyAsync(galleries);
        return (db, galleries);
    }

    private static async Task<List<GalleryCardDoc>> ProjectAsync(AdminHost host)
    {
        using var scope = host.Services.CreateScope();
        var projected = await scope.ServiceProvider.GetRequiredService<SiteProjector>().ProjectAsync();
        return [.. projected];
    }

    [Fact]
    public async Task Private_and_empty_galleries_are_excluded_and_homeOrder_order_is_kept()
    {
        await using var host = await AdminHost.CreateAsync(mongo);
        var (db, g) = await InsertAsync(host);
        var (pub1, priv, empty, pub2) = (g[0], g[1], g[2], g[3]);
        // homeOrder deliberately differs from insertion order.
        await db.GetCollection<SiteDoc>("site").InsertOneAsync(new SiteDoc
        {
            HomeOrder = [pub2.Id, empty.Id, priv.Id, pub1.Id],
            HomeCards = [],
        });

        var returned = await ProjectAsync(host);

        var site = await db.GetCollection<SiteDoc>("site").Find(s => s.Id == "site").FirstAsync();
        Assert.Equal(new[] { pub2.Id, pub1.Id }, site.HomeCards.Select(x => x.GalleryId).ToArray());
        Assert.Equal(pub2.Card.ToBsonDocument(), site.HomeCards[0].ToBsonDocument());
        Assert.Equal(pub1.Card.ToBsonDocument(), site.HomeCards[1].ToBsonDocument());
        Assert.Equal(new[] { pub2.Id, pub1.Id }, returned.Select(x => x.GalleryId).ToArray());
        // homeOrder itself is not touched.
        Assert.Equal(new[] { pub2.Id, empty.Id, priv.Id, pub1.Id }, site.HomeOrder.ToArray());

        Assert.Equal(1, host.LogLines.Count(l => l.StartsWith("op.timing db.site.project ", StringComparison.Ordinal) && l.EndsWith(" ok", StringComparison.Ordinal)));
        await InvariantChecker.AssertAsync(db);
    }

    [Fact]
    public async Task Without_a_site_document_it_upserts_an_empty_projection()
    {
        await using var host = await AdminHost.CreateAsync(mongo);
        await InsertAsync(host);

        var returned = await ProjectAsync(host);

        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IMongoDatabase>();
        var raw = await db.GetCollection<BsonDocument>("site").Find(new BsonDocument("_id", "site")).FirstAsync();
        Assert.Empty(returned);
        Assert.Equal(new[] { "_id", "homeCards", "updatedAt" }, raw.Names.Order(StringComparer.Ordinal).ToArray());
        Assert.Empty(raw["homeCards"].AsBsonArray);
    }

    [Fact]
    public async Task Claim_fields_stay_byte_identical()
    {
        await using var host = await AdminHost.CreateAsync(mongo);
        var (db, g) = await InsertAsync(host);
        var claimedAt = new DateTime(2026, 10, 1, 8, 30, 0, DateTimeKind.Utc);
        var siteCol = db.GetCollection<BsonDocument>("site");
        // The claim field names appear only here, in a test (OwnerWriteGuardTests scans src/).
        const string claimSub = "claimedSubject", claimAt = "claimedAt", claimIp = "claimedIp";
        await siteCol.InsertOneAsync(new BsonDocument
        {
            ["_id"] = "site",
            ["homeOrder"] = new BsonArray(g.Select(x => x.Id)),
            ["homeCards"] = new BsonArray(),
            [claimSub] = "owner-sub-123",
            [claimAt] = claimedAt,
            [claimIp] = "203.0.113.7",
            ["updatedAt"] = claimedAt,
        });
        var before = await siteCol.Find(new BsonDocument("_id", "site")).FirstAsync();

        await ProjectAsync(host);

        var after = await siteCol.Find(new BsonDocument("_id", "site")).FirstAsync();
        foreach (var f in new[] { claimSub, claimAt, claimIp })
            Assert.Equal(new BsonDocument(f, before[f]).ToBson(), new BsonDocument(f, after[f]).ToBson());
        Assert.Equal("owner-sub-123", after[claimSub].AsString);
        Assert.Equal(claimedAt, after[claimAt].ToUniversalTime());
        Assert.Equal("203.0.113.7", after[claimIp].AsString);
        Assert.Equal(new[] { g[0].Id, g[3].Id }, after["homeCards"].AsBsonArray.Select(x => x["galleryId"].AsString).ToArray());
        Assert.Equal(before.Names.Order(StringComparer.Ordinal).ToArray(), after.Names.Order(StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public async Task Home_after_projection_reads_the_datastore_once()
    {
        await using var host = await AdminHost.CreateAsync(mongo);
        var (db, g) = await InsertAsync(host);
        await db.GetCollection<SiteDoc>("site").InsertOneAsync(new SiteDoc { HomeOrder = g.Select(x => x.Id).ToList() });
        await ProjectAsync(host);

        var resp = await host.GetAsync("/g");
        Assert.Equal(System.Net.HttpStatusCode.OK, resp.StatusCode);
        var html = await resp.Content.ReadAsStringAsync();
        Assert.Contains("Autumn Walk", html);
        Assert.Contains("Spring Meadow", html);
        Assert.DoesNotContain("Secret Garden", html);
        Assert.DoesNotContain("Coming Soon", html);

        List<string> lines;
        lock (host.LogLines) lines = host.LogLines.Where(l => l.StartsWith("http.request GET /g ", StringComparison.Ordinal)).ToList();
        var line = Assert.Single(lines);
        Assert.Matches(@"^http\.request GET /g 200 [0-9.]+ 1$", line);
    }
}
