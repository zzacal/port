using Gallery.Web.Data;
using Gallery.Web.Library;
using Gallery.Web.Tests.Fixtures;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Gallery.Web.Tests;

/// <summary>
/// 010 owner-built library state shared by <see cref="NavProjectionTests"/> and
/// <see cref="NavPurgeTests"/>: galleries A, B, C, D (public, 2 photos each) and P (private, 2
/// photos), created, filled and published through <see cref="GalleryService"/>, never by writing
/// gate-satisfying state or a <c>nav</c> into the database. Home order A, B, C, D, P.
/// </summary>
internal sealed record NavLibrary(string A, string B, string C, string D, string P)
{
    public static async Task<NavLibrary> BuildAsync(LibraryHost h)
    {
        var ids = new List<string>();
        foreach (var (title, pub) in new[] { ("Alpha", true), ("Bravo", true), ("Charlie", true), ("Delta", true), ("Papa", false) })
        {
            var created = await Gs(h, s => s.CreateAsync(title));
            Assert.True(created.IsOk);
            var gid = created.GalleryId!;
            var imgs = new List<string>();
            for (var i = 0; i < 2; i++)
                imgs.Add((await h.InsertUploadedImageAsync(DateTime.UtcNow.AddSeconds(i), files: false)).Id);
            Assert.True((await Gs(h, s => s.AddAsync(gid, imgs))).IsOk);
            if (pub) Assert.True((await Gs(h, s => s.PublishAsync(gid, confirmed: true))).IsOk);
            ids.Add(gid);
        }
        await h.SettleAsync();
        return new NavLibrary(ids[0], ids[1], ids[2], ids[3], ids[4]);
    }

    public static Task<OperationResult> Gs(LibraryHost h, Func<GalleryService, Task<OperationResult>> call) =>
        h.RunAsync(call);

    public static async Task<List<GalleryDoc>> GalleriesAsync(LibraryHost h) =>
        await h.Galleries.Find(FilterDefinition<GalleryDoc>.Empty).ToListAsync();

    public static async Task<List<string>> OrderAsync(LibraryHost h) => [.. (await h.SiteAsync()).HomeOrder];

    /// <summary>Each gallery's raw stored <c>nav</c> (BsonNull when absent).</summary>
    public static async Task<Dictionary<string, BsonValue>> StoredNavAsync(LibraryHost h)
    {
        var raw = await h.Db.GetCollection<BsonDocument>("galleries").Find(FilterDefinition<BsonDocument>.Empty).ToListAsync();
        return raw.ToDictionary(d => d["_id"].AsString, d => d.GetValue("nav", BsonNull.Value), StringComparer.Ordinal);
    }
}

/// <summary>
/// 010 T008 [SAFETY] II.2 (data-model "Gallery.nav" rules 1–4, contracts/edge.md E2): the pure
/// <see cref="SiteProjector.Nav"/> lists only listed galleries as neighbours; the stored <c>nav</c>
/// after every owner action equals <c>Nav</c> of the stored state; <c>NavChanged</c> names exactly
/// the galleries whose stored <c>nav</c> changed; a nav-only write leaves <c>updatedAt</c> alone.
/// </summary>
[Collection("s3")]
public class NavProjectionTests(S3ProxyFixture s3, MongoFixture mongo) : IClassFixture<MongoFixture>
{
    private static GalleryDoc Doc(string id, string vis, int count) => new()
    {
        Id = id,
        Slug = id.ToLowerInvariant() + "-slug",
        Title = "Title " + id,
        Visibility = vis,
        Items = [.. Enumerable.Range(0, count).Select(i => $"{id}{i}")],
        Card = new GalleryCardDoc { GalleryId = id, Slug = id.ToLowerInvariant() + "-slug", Title = "Title " + id, ImageCount = count },
    };

    private static BsonValue Bson(GalleryNavDoc? nav) => nav is null ? BsonNull.Value : nav.ToBsonDocument();

    private static BsonDocument Link(GalleryDoc g) =>
        new() { { "id", g.Id }, { "slug", g.Slug }, { "title", g.Title } };

    // ── (a), (b) pure ────────────────────────────────────────────────────────

    [Fact]
    public void Nav_links_only_listed_galleries_in_home_order()
    {
        var a = Doc("A", "public", 2);
        var b = Doc("B", "public", 1);
        var c = Doc("C", "public", 3);
        var p = Doc("P", "private", 2);
        var e = Doc("E", "public", 0);

        var nav = SiteProjector.Nav(["A", "P", "B", "E", "C"], [a, b, c, p, e]);

        Assert.Equal(new[] { "A", "B", "C", "E", "P" }, nav.Keys.Order(StringComparer.Ordinal).ToArray());
        Assert.Equal(new BsonDocument { { "next", Link(b) } }, Bson(nav["A"]));
        Assert.Equal(new BsonDocument { { "prev", Link(a) }, { "next", Link(c) } }, Bson(nav["B"]));
        Assert.Equal(new BsonDocument { { "prev", Link(b) } }, Bson(nav["C"]));
        Assert.Null(nav["P"]);
        Assert.Null(nav["E"]);

        var named = nav.Values.OfType<GalleryNavDoc>()
            .SelectMany(n => new[] { n.Prev?.Id, n.Next?.Id }).OfType<string>().ToHashSet();
        Assert.DoesNotContain("P", named);
        Assert.DoesNotContain("E", named);
    }

    [Fact]
    public void Nav_of_the_only_listed_gallery_is_null()
    {
        var a = Doc("A", "public", 2);
        var p = Doc("P", "private", 2);
        var e = Doc("E", "public", 0);

        var nav = SiteProjector.Nav(["P", "A", "E"], [a, p, e]);

        Assert.Equal(3, nav.Count);
        Assert.All(nav.Values, Assert.Null);
    }

    // ── (c) through GalleryService ───────────────────────────────────────────

    /// <summary>
    /// After an owner action: the stored <c>nav</c> of every gallery equals <c>Nav</c> of the stored
    /// state, and <c>NavChanged</c> names exactly the galleries whose stored <c>nav</c> changed. The
    /// action's own projection ran inside the service, so <c>NavChanged</c> is observed by restoring
    /// every gallery's pre-action <c>nav</c> (test-side, raw) and projecting again: that projection
    /// sees exactly the state the action's projection saw.
    /// </summary>
    private static async Task AssertProjectedAsync(LibraryHost h, Dictionary<string, BsonValue> before, Func<Task> action)
    {
        await action();
        await h.SettleAsync();

        var stored = await NavLibrary.StoredNavAsync(h);
        var galleries = await NavLibrary.GalleriesAsync(h);
        var expected = SiteProjector.Nav(await NavLibrary.OrderAsync(h), galleries);
        foreach (var g in galleries)
            Assert.Equal(Bson(expected.GetValueOrDefault(g.Id)), stored[g.Id]);

        var changedIds = stored.Keys
            .Where(id => stored[id] != before.GetValueOrDefault(id, BsonNull.Value))
            .Order(StringComparer.Ordinal).ToArray();

        var raw = h.Db.GetCollection<BsonDocument>("galleries");
        foreach (var id in stored.Keys)
        {
            var old = before.GetValueOrDefault(id, BsonNull.Value);
            var update = old.IsBsonNull
                ? Builders<BsonDocument>.Update.Unset("nav")
                : Builders<BsonDocument>.Update.Set("nav", old);
            await raw.UpdateOneAsync(new BsonDocument("_id", id), update);
        }
        using var scope = h.Scope();
        var result = await scope.ServiceProvider.GetRequiredService<SiteProjector>().ProjectWithNavAsync();

        Assert.Equal(changedIds, result.NavChanged.Select(t => t.Id).Order(StringComparer.Ordinal).ToArray());
        var slugs = galleries.ToDictionary(g => g.Id, g => g.Slug);
        Assert.All(result.NavChanged, t => Assert.Equal(slugs[t.Id], t.Slug));
        Assert.Equal(stored, await NavLibrary.StoredNavAsync(h));
    }

    [Fact]
    public async Task Stored_nav_follows_every_owner_action_and_NavChanged_is_exact()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3);
        var lib = await NavLibrary.BuildAsync(h);
        var (a, b, c, d, p) = (lib.A, lib.B, lib.C, lib.D, lib.P);

        // Built state: A-B-C-D chained, P none.
        var nav = await NavLibrary.StoredNavAsync(h);
        Assert.True(nav[a].IsBsonDocument && nav[d].IsBsonDocument);
        Assert.True(nav[p].IsBsonNull);

        async Task Step(Func<Task> action) => await AssertProjectedAsync(h, await NavLibrary.StoredNavAsync(h), action);

        await Step(async () => Assert.True((await NavLibrary.Gs(h, s => s.ReorderHomeAsync([b, a, c, d, p]))).IsOk));
        await Step(async () => Assert.True((await NavLibrary.Gs(h, s => s.ReorderHomeAsync([b, a, p, c, d]))).IsOk));
        await Step(async () => Assert.True((await NavLibrary.Gs(h, s => s.PublishAsync(p, confirmed: true))).IsOk));
        await Step(async () => Assert.True((await NavLibrary.Gs(h, s => s.UnpublishAsync(b))).IsOk));
        await Step(async () => Assert.True((await NavLibrary.Gs(h, s => s.PublishAsync(b, confirmed: true))).IsOk));
        await Step(async () => Assert.True((await NavLibrary.Gs(h, s => s.UpdateTextAsync(b, "Brook", null))).IsOk));
        await Step(async () => Assert.True((await NavLibrary.Gs(h, s => s.DeleteAsync(c))).IsOk));

        var items = (await h.Galleries.Find(g => g.Id == b).FirstAsync()).Items;
        await Step(async () => Assert.True((await NavLibrary.Gs(h, s => s.RemoveAsync(b, items[0]))).IsOk));
        await Step(async () => Assert.True((await NavLibrary.Gs(h, s => s.RemoveAsync(b, items[1]))).IsOk));

        // Final home order B, A, P, D with B now empty: the listed sequence is A, P, D.
        var final = await NavLibrary.StoredNavAsync(h);
        Assert.True(final[b].IsBsonNull);
        Assert.Equal(BsonNull.Value, final[a].AsBsonDocument.GetValue("prev", BsonNull.Value));
        Assert.Equal(p, final[a]["next"]["id"].AsString);
        Assert.Equal(d, final[p]["next"]["id"].AsString);
    }

    // ── (d), (e), (f) ────────────────────────────────────────────────────────

    [Fact]
    public async Task A_second_projection_with_no_change_writes_no_gallery()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3);
        await NavLibrary.BuildAsync(h);
        var raw = h.Db.GetCollection<BsonDocument>("galleries");
        var before = await raw.Find(FilterDefinition<BsonDocument>.Empty).SortBy(d => d["_id"]).ToListAsync();

        using var scope = h.Scope();
        var result = await scope.ServiceProvider.GetRequiredService<SiteProjector>().ProjectWithNavAsync();

        Assert.Empty(result.NavChanged);
        var after = await raw.Find(FilterDefinition<BsonDocument>.Empty).SortBy(d => d["_id"]).ToListAsync();
        Assert.Equal(before, after);
    }

    [Fact]
    public async Task A_nav_only_write_leaves_updatedAt_unchanged()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3);
        var lib = await NavLibrary.BuildAsync(h);
        var before = (await NavLibrary.GalleriesAsync(h)).ToDictionary(g => g.Id);
        var navBefore = await NavLibrary.StoredNavAsync(h);

        // A home reorder writes only the site document; every gallery write that follows is nav-only.
        Assert.True((await NavLibrary.Gs(h, s => s.ReorderHomeAsync([lib.B, lib.A, lib.C, lib.D, lib.P]))).IsOk);
        await h.SettleAsync();

        var navAfter = await NavLibrary.StoredNavAsync(h);
        var after = (await NavLibrary.GalleriesAsync(h)).ToDictionary(g => g.Id);
        var changed = navAfter.Keys.Where(id => navAfter[id] != navBefore[id]).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(new[] { lib.A, lib.B, lib.C }.Order(StringComparer.Ordinal).ToArray(), changed);
        foreach (var id in before.Keys)
            Assert.Equal(before[id].UpdatedAt, after[id].UpdatedAt);
    }

    [Fact]
    public async Task A_stale_nav_planted_on_a_private_gallery_is_unset_by_the_next_projection()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3);
        var lib = await NavLibrary.BuildAsync(h);
        var p = await h.Galleries.Find(g => g.Id == lib.P).FirstAsync();
        var a = await h.Galleries.Find(g => g.Id == lib.A).FirstAsync();
        var c = await h.Galleries.Find(g => g.Id == lib.C).FirstAsync();
        var raw = h.Db.GetCollection<BsonDocument>("galleries");
        await raw.UpdateOneAsync(new BsonDocument("_id", lib.P),
            Builders<BsonDocument>.Update.Set("nav", new BsonDocument { { "prev", Link(a) }, { "next", Link(c) } }));

        using var scope = h.Scope();
        var result = await scope.ServiceProvider.GetRequiredService<SiteProjector>().ProjectWithNavAsync();

        Assert.Equal(new[] { new GalleryLinkTarget(lib.P, p.Slug) }, result.NavChanged.ToArray());
        var stored = await raw.Find(new BsonDocument("_id", lib.P)).FirstAsync();
        Assert.False(stored.Contains("nav"));
        Assert.Equal(p.UpdatedAt, (await h.Galleries.Find(g => g.Id == lib.P).FirstAsync()).UpdatedAt);
    }
}
