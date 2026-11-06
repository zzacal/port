using System.Net;
using System.Text.RegularExpressions;
using Gallery.Web.Data;
using Gallery.Web.Edge;
using Gallery.Web.Library;
using Gallery.Web.Tests.Fixtures;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Gallery.Web.Tests;

/// <summary>
/// 013 T023 [SAFETY] (FR-011, II.2; contracts/edge.md E1-E3; US2): every owner action of FR-011 on
/// a <see cref="LibraryHost"/> whose photos carry cameras. For each action three things hold:
/// (1) the stored <c>cameras</c> are right (<see cref="InvariantChecker"/>, run by
/// <see cref="LibraryHost.SettleAsync"/>, plus an explicit slug/items check); (2) the purged set
/// EQUALS the existing gallery set for the action (computed with <see cref="PurgeSets"/> over the
/// before/after gallery snapshots, plus P-NAV for the galleries whose stored nav changed) united with
/// the camera part, written out by hand from edge.md; (3) the next anonymous GET of <c>/c</c> and of
/// the affected <c>/c/{slug}</c> shows the change.
/// Library: Canon photos k1 &lt; k2 &lt; k3 (by <c>createdAt</c>) and a Nikon photo n1 that sits in
/// its own public gallery "Nook" throughout, so the Nikon camera is there in every test.
/// </summary>
[Collection("s3")]
public partial class CameraFreshnessTests(S3ProxyFixture s3, MongoFixture mongo) : IClassFixture<MongoFixture>
{
    private const string Canon = "canon-eos-r5", Nikon = "nikon-z-8";
    private static readonly EdgeBases Bases = new(LibraryHost.SiteBaseUrl, LibraryHost.ImageBaseUrl);
    private static readonly DateTime T0 = new(2026, 5, 1, 8, 0, 0, DateTimeKind.Utc);

    [GeneratedRegex("<a class=\"tile\"[^>]*href=\"([^\"]*)\"")]
    private static partial Regex TileHrefRx();

    private sealed record Lib(string K1, string K2, string K3, string N1, string Nook);

    // ── setup ────────────────────────────────────────────────────────────────

    private static Task<OperationResult> Gs(LibraryHost h, Func<GalleryService, Task<OperationResult>> call) => h.RunAsync(call);

    private static async Task<string> PhotoAsync(LibraryHost h, string make, string model, int minutes, string title, bool files = false) =>
        (await h.InsertUploadedImageAsync(T0.AddMinutes(minutes), title: title, files: files,
            shape: i => i.Camera = new CameraDoc { Make = make, Model = model })).Id;

    private static async Task<string> GalleryAsync(LibraryHost h, string title, IReadOnlyList<string> ids, bool publish)
    {
        var created = await Gs(h, s => s.CreateAsync(title));
        Assert.True(created.IsOk);
        var gid = created.GalleryId!;
        if (ids.Count > 0) Assert.True((await Gs(h, s => s.AddAsync(gid, ids))).IsOk);
        if (publish) Assert.True((await Gs(h, s => s.PublishAsync(gid, confirmed: true))).IsOk);
        return gid;
    }

    private static async Task<Lib> PhotosAsync(LibraryHost h, bool k1Files = false)
    {
        var k1 = await PhotoAsync(h, "Canon", "Canon EOS R5", 1, "Pier at dawn", k1Files);
        var k2 = await PhotoAsync(h, "Canon", "Canon EOS R5", 2, "Gull");
        var k3 = await PhotoAsync(h, "CANON ", "Canon  EOS R5", 3, "Breakwater");
        var n1 = await PhotoAsync(h, "NIKON CORPORATION", "NIKON Z 8", 4, "Fern");
        var nook = await GalleryAsync(h, "Nook", [n1], publish: true);
        return new Lib(k1, k2, k3, n1, nook);
    }

    // ── observation helpers ──────────────────────────────────────────────────

    private static async Task<Dictionary<string, GalleryDoc>> GalleriesAsync(LibraryHost h) =>
        (await h.Galleries.Find(FilterDefinition<GalleryDoc>.Empty).ToListAsync()).ToDictionary(g => g.Id, StringComparer.Ordinal);

    private static GallerySnapshot Snap(Dictionary<string, GalleryDoc> all, string id) => GallerySnapshot.From(all[id]);

    /// <summary>P-NAV: the gallery page (after slug) of every gallery whose stored nav changed.</summary>
    private static PurgeSet NavChanged(Dictionary<string, GalleryDoc> before, Dictionary<string, GalleryDoc> after) =>
        PurgeSets.Nav(after.Values
            .Where(a => before.TryGetValue(a.Id, out var b) && NavBson(b) != NavBson(a))
            .Select(a => new GalleryLinkTarget(a.Id, a.Slug)), Bases);

    private static BsonValue NavBson(GalleryDoc g) => g.Nav is null ? BsonNull.Value : g.Nav.ToBsonDocument();

    private static PurgeSet Cam(params string[] paths) => new(Bases, paths, []);

    private static void SetEq(IEnumerable<PurgeSet> expected, IReadOnlyList<string> purged)
    {
        var e = PurgeSet.Union(Bases, expected);
        Assert.Equal(e.Pages.Concat(e.Files).Order(StringComparer.Ordinal).ToArray(),
            purged.Order(StringComparer.Ordinal).ToArray());
    }

    private static async Task<List<CameraGroupDoc>> StoredAsync(LibraryHost h) =>
        await h.Db.GetCollection<CameraGroupDoc>("cameras").Find(FilterDefinition<CameraGroupDoc>.Empty)
            .SortBy(c => c.Rank).ToListAsync();

    private static async Task AssertStoredAsync(LibraryHost h, params (string Slug, string[] Items)[] expected)
    {
        var stored = await StoredAsync(h);
        Assert.Equal(expected.Select(e => (e.Slug, string.Join(",", e.Items))).ToArray(),
            stored.Select(c => (c.Slug, string.Join(",", c.Items))).ToArray());
    }

    /// <summary>The tiles of an anonymous GET; null when the page answers 404.</summary>
    private static async Task<List<string>?> TilesAsync(LibraryHost h, string path)
    {
        h.Host.ClearCookies();
        var resp = await h.GetAsync(path);
        if (resp.StatusCode == HttpStatusCode.NotFound) return null;
        Assert.True(resp.StatusCode == HttpStatusCode.OK, $"GET {path} answered {(int)resp.StatusCode}");
        return TileHrefRx().Matches(await resp.Content.ReadAsStringAsync()).Select(m => m.Groups[1].Value).ToList();
    }

    private static async Task<string> HtmlAsync(LibraryHost h, string path)
    {
        h.Host.ClearCookies();
        var resp = await h.GetAsync(path);
        Assert.True(resp.StatusCode == HttpStatusCode.OK, $"GET {path} answered {(int)resp.StatusCode}");
        return WebUtility.HtmlDecode(await resp.Content.ReadAsStringAsync());
    }

    private static List<string> Photos(string slug, params string[] ids) => ids.Select(i => $"/c/{slug}/{i}").ToList();

    private static void AssertListed(string html, string slug) =>
        Assert.Contains($"href=\"/c/{slug}\"", html, StringComparison.Ordinal);

    // ── the actions ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Publish_adds_the_camera_and_purges_its_pages_and_the_listing()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3);
        var lib = await PhotosAsync(h);
        var gid = await GalleryAsync(h, "Harbour", [lib.K1, lib.K2], publish: false);
        await h.SettleAsync();
        Assert.Equal([$"/c/{Nikon}"], await TilesAsync(h, "/c"));
        Assert.Null(await TilesAsync(h, $"/c/{Canon}"));
        var g0 = await GalleriesAsync(h);

        Assert.True((await Gs(h, s => s.PublishAsync(gid, confirmed: true))).IsOk);
        var purged = await h.SettleAsync();

        await AssertStoredAsync(h, (Canon, [lib.K2, lib.K1]), (Nikon, [lib.N1]));
        var g1 = await GalleriesAsync(h);
        SetEq([
            PurgeSets.Visibility(Snap(g0, gid), Snap(g1, gid), Bases),
            NavChanged(g0, g1),
            // Canon appears; Nikon's rank moves 0 → 1 (its document changed, no photo did).
            Cam("/c", $"/c/{Canon}", $"/c/{Canon}/{lib.K1}", $"/c/{Canon}/{lib.K2}", $"/c/{Nikon}"),
        ], purged);

        Assert.Equal([$"/c/{Canon}", $"/c/{Nikon}"], await TilesAsync(h, "/c"));
        Assert.Equal(Photos(Canon, lib.K2, lib.K1), await TilesAsync(h, $"/c/{Canon}"));
    }

    [Fact]
    public async Task Unpublish_of_the_only_listed_holder_removes_the_camera()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3);
        var lib = await PhotosAsync(h);
        var gid = await GalleryAsync(h, "Harbour", [lib.K1, lib.K2], publish: true);
        await h.SettleAsync();
        Assert.Equal([$"/c/{Canon}", $"/c/{Nikon}"], await TilesAsync(h, "/c"));
        var g0 = await GalleriesAsync(h);

        Assert.True((await Gs(h, s => s.UnpublishAsync(gid))).IsOk);
        var purged = await h.SettleAsync();

        await AssertStoredAsync(h, (Nikon, [lib.N1]));
        var g1 = await GalleriesAsync(h);
        SetEq([
            PurgeSets.Visibility(Snap(g0, gid), Snap(g1, gid), Bases),
            NavChanged(g0, g1),
            Cam("/c", $"/c/{Canon}", $"/c/{Canon}/{lib.K1}", $"/c/{Canon}/{lib.K2}", $"/c/{Nikon}"),
        ], purged);

        Assert.Equal([$"/c/{Nikon}"], await TilesAsync(h, "/c"));
        Assert.Null(await TilesAsync(h, $"/c/{Canon}"));
    }

    [Fact]
    public async Task Adding_a_newer_photo_to_a_public_gallery_purges_every_canon_photo_and_the_listing()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3);
        var lib = await PhotosAsync(h);
        var gid = await GalleryAsync(h, "Harbour", [lib.K1, lib.K2], publish: true);
        await h.SettleAsync();
        var g0 = await GalleriesAsync(h);
        var card0 = (await StoredAsync(h))[0].Card;
        Assert.Equal((2, lib.K2), (card0.ImageCount, card0.Cover.ImageId));

        Assert.True((await Gs(h, s => s.AddAsync(gid, [lib.K3]))).IsOk);
        var purged = await h.SettleAsync();

        await AssertStoredAsync(h, (Canon, [lib.K3, lib.K2, lib.K1]), (Nikon, [lib.N1]));
        var card1 = (await StoredAsync(h))[0].Card;
        Assert.Equal((3, lib.K3), (card1.ImageCount, card1.Cover.ImageId));
        var g1 = await GalleriesAsync(h);
        SetEq([
            PurgeSets.Members(Snap(g0, gid), Snap(g1, gid), Bases),
            NavChanged(g0, g1),
            Cam("/c", $"/c/{Canon}", $"/c/{Canon}/{lib.K1}", $"/c/{Canon}/{lib.K2}", $"/c/{Canon}/{lib.K3}"),
        ], purged);

        var listing = await HtmlAsync(h, "/c");
        AssertListed(listing, Canon);
        Assert.Contains(card1.Cover.Derivatives[0].Avif, listing, StringComparison.Ordinal);
        Assert.Equal(Photos(Canon, lib.K3, lib.K2, lib.K1), await TilesAsync(h, $"/c/{Canon}"));
    }

    [Fact]
    public async Task Removing_a_photo_purges_it_and_the_renumbered_ones()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3);
        var lib = await PhotosAsync(h);
        var gid = await GalleryAsync(h, "Harbour", [lib.K1, lib.K2, lib.K3], publish: true);
        await h.SettleAsync();
        var g0 = await GalleriesAsync(h);

        Assert.True((await Gs(h, s => s.RemoveAsync(gid, lib.K1))).IsOk);
        var purged = await h.SettleAsync();

        await AssertStoredAsync(h, (Canon, [lib.K3, lib.K2]), (Nikon, [lib.N1]));
        var g1 = await GalleriesAsync(h);
        SetEq([
            PurgeSets.Members(Snap(g0, gid), Snap(g1, gid), Bases),
            NavChanged(g0, g1),
            // k1 left (its page under the before slug); the count changed, so k2 and k3 too; the card count changed.
            Cam("/c", $"/c/{Canon}", $"/c/{Canon}/{lib.K1}", $"/c/{Canon}/{lib.K2}", $"/c/{Canon}/{lib.K3}"),
        ], purged);

        Assert.Equal(Photos(Canon, lib.K3, lib.K2), await TilesAsync(h, $"/c/{Canon}"));
        Assert.Equal([$"/c/{Canon}", $"/c/{Nikon}"], await TilesAsync(h, "/c"));
    }

    [Fact]
    public async Task Reordering_inside_a_gallery_purges_no_camera_page()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3);
        var lib = await PhotosAsync(h);
        var gid = await GalleryAsync(h, "Harbour", [lib.K1, lib.K2], publish: true);
        await h.SettleAsync();
        var g0 = await GalleriesAsync(h);

        Assert.True((await Gs(h, s => s.ReorderAsync(gid, [lib.K2, lib.K1]))).IsOk);
        var purged = await h.SettleAsync();

        await AssertStoredAsync(h, (Canon, [lib.K2, lib.K1]), (Nikon, [lib.N1]));
        var g1 = await GalleriesAsync(h);
        SetEq([PurgeSets.Members(Snap(g0, gid), Snap(g1, gid), Bases), NavChanged(g0, g1)], purged);
        Assert.DoesNotContain(purged, p => p.StartsWith(LibraryHost.Page("/c"), StringComparison.Ordinal));

        Assert.Equal(Photos(Canon, lib.K2, lib.K1), await TilesAsync(h, $"/c/{Canon}"));
    }

    [Fact]
    public async Task Setting_a_gallery_cover_purges_no_camera_page()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3);
        var lib = await PhotosAsync(h);
        var gid = await GalleryAsync(h, "Harbour", [lib.K1, lib.K2], publish: true);
        await h.SettleAsync();
        var g0 = await GalleriesAsync(h);

        Assert.True((await Gs(h, s => s.SetCoverAsync(gid, lib.K2))).IsOk);
        var purged = await h.SettleAsync();

        await AssertStoredAsync(h, (Canon, [lib.K2, lib.K1]), (Nikon, [lib.N1]));
        var g1 = await GalleriesAsync(h);
        Assert.Equal(lib.K2, g1[gid].PinnedCover);
        SetEq([PurgeSets.Members(Snap(g0, gid), Snap(g1, gid), Bases), NavChanged(g0, g1)], purged);
        Assert.DoesNotContain(purged, p => p.StartsWith(LibraryHost.Page("/c"), StringComparison.Ordinal));

        Assert.Equal([$"/c/{Canon}", $"/c/{Nikon}"], await TilesAsync(h, "/c"));
    }

    [Fact]
    public async Task Retitling_a_photo_purges_its_camera_page_and_its_camera_photo_page()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3);
        var lib = await PhotosAsync(h);
        var gid = await GalleryAsync(h, "Harbour", [lib.K1, lib.K2], publish: true);
        await h.SettleAsync();
        Assert.Contains("Pier at dawn", await HtmlAsync(h, $"/c/{Canon}"), StringComparison.Ordinal);
        var g0 = await GalleriesAsync(h);

        Assert.True((await h.RunAsync<ImageService, OperationResult>(s => s.UpdateTextAsync(lib.K1, "Pier at noon", null))).IsOk);
        var purged = await h.SettleAsync();

        await AssertStoredAsync(h, (Canon, [lib.K2, lib.K1]), (Nikon, [lib.N1]));
        SetEq([
            PurgeSets.Text(lib.K1, [Snap(g0, gid)], Bases),
            // P-CAMTEXT: never /c (the card shows the camera name).
            Cam($"/c/{Canon}", $"/c/{Canon}/{lib.K1}"),
        ], purged);

        var html = await HtmlAsync(h, $"/c/{Canon}");
        Assert.Contains("Pier at noon", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Pier at dawn", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Deleting_a_photo_removes_it_from_its_camera()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3);
        var lib = await PhotosAsync(h, k1Files: true);
        var gid = await GalleryAsync(h, "Harbour", [lib.K1, lib.K2], publish: true);
        await h.SettleAsync();
        var g0 = await GalleriesAsync(h);
        var k1 = await h.Images.Find(i => i.Id == lib.K1).FirstAsync();
        var keys = k1.Derivatives.SelectMany(d => new[] { d.Avif, d.Webp }).ToList();

        var r = await h.RunAsync<ImageDeleteService, OperationResult>(s => s.DeleteAsync(lib.K1));
        Assert.Equal((OperationStatus.Ok, 1, 0), (r.Status, r.Deleted, r.Leftovers));
        var purged = await h.SettleAsync();

        await AssertStoredAsync(h, (Canon, [lib.K2]), (Nikon, [lib.N1]));
        var g1 = await GalleriesAsync(h);
        SetEq([
            PurgeSets.Delete([(Snap(g0, gid), Snap(g1, gid))], keys, Bases),
            NavChanged(g0, g1),
            Cam("/c", $"/c/{Canon}", $"/c/{Canon}/{lib.K1}", $"/c/{Canon}/{lib.K2}"),
        ], purged);

        Assert.Equal(Photos(Canon, lib.K2), await TilesAsync(h, $"/c/{Canon}"));
        Assert.Equal([$"/c/{Canon}", $"/c/{Nikon}"], await TilesAsync(h, "/c"));
    }

    [Fact]
    public async Task Deleting_the_only_listed_gallery_of_a_camera_removes_the_camera()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3);
        var lib = await PhotosAsync(h);
        var gid = await GalleryAsync(h, "Harbour", [lib.K1, lib.K2], publish: true);
        await h.SettleAsync();
        var g0 = await GalleriesAsync(h);

        Assert.True((await Gs(h, s => s.DeleteAsync(gid))).IsOk);
        var purged = await h.SettleAsync();

        await AssertStoredAsync(h, (Nikon, [lib.N1]));
        var g1 = await GalleriesAsync(h);
        Assert.False(g1.ContainsKey(gid));
        SetEq([
            PurgeSets.GalleryDelete(Snap(g0, gid), Bases),
            NavChanged(g0, g1),
            Cam("/c", $"/c/{Canon}", $"/c/{Canon}/{lib.K1}", $"/c/{Canon}/{lib.K2}", $"/c/{Nikon}"),
        ], purged);

        Assert.Equal([$"/c/{Nikon}"], await TilesAsync(h, "/c"));
        Assert.Null(await TilesAsync(h, $"/c/{Canon}"));
    }

    [Fact]
    public async Task Making_private_the_only_listed_holder_of_a_photo_also_in_a_private_gallery_removes_it_from_its_camera()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3);
        var lib = await PhotosAsync(h);
        var harbour = await GalleryAsync(h, "Harbour", [lib.K1], publish: true);   // the only listed holder of k1
        await GalleryAsync(h, "Meadow", [lib.K2], publish: true);                    // keeps Canon alive
        await GalleryAsync(h, "Secret", [lib.K1], publish: false);                   // k1 also sits here
        await h.SettleAsync();
        await AssertStoredAsync(h, (Canon, [lib.K2, lib.K1]), (Nikon, [lib.N1]));
        Assert.Equal(Photos(Canon, lib.K2, lib.K1), await TilesAsync(h, $"/c/{Canon}"));
        var g0 = await GalleriesAsync(h);

        Assert.True((await Gs(h, s => s.UnpublishAsync(harbour))).IsOk);
        var purged = await h.SettleAsync();

        // II.2: k1 is now only in private galleries, so it is in no camera.
        await AssertStoredAsync(h, (Canon, [lib.K2]), (Nikon, [lib.N1]));
        var g1 = await GalleriesAsync(h);
        SetEq([
            PurgeSets.Visibility(Snap(g0, harbour), Snap(g1, harbour), Bases),
            NavChanged(g0, g1),
            Cam("/c", $"/c/{Canon}", $"/c/{Canon}/{lib.K1}", $"/c/{Canon}/{lib.K2}"),
        ], purged);

        Assert.Equal(Photos(Canon, lib.K2), await TilesAsync(h, $"/c/{Canon}"));
        var html = await HtmlAsync(h, $"/c/{Canon}");
        Assert.DoesNotContain(lib.K1, html, StringComparison.Ordinal);
        Assert.DoesNotContain("Pier at dawn", html, StringComparison.Ordinal);
        Assert.Equal([$"/c/{Canon}", $"/c/{Nikon}"], await TilesAsync(h, "/c"));
    }
}
