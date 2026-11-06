using System.Net;
using System.Text.RegularExpressions;
using Gallery.Web.Data;
using Gallery.Web.Library;
using Gallery.Web.Site;
using Gallery.Web.Tests.Fixtures;
using MongoDB.Driver;

namespace Gallery.Web.Tests;

/// <summary>
/// 013 T016-T018 [SAFETY] (contracts/routes.md C1, C2; research R-04, R-07, R-09; test-impact B):
/// the camera routes over HTTP on a <see cref="LibraryHost"/>. Two cameras A (Canon, three visible
/// photos, one of them in both public galleries) and B (Nikon, one photo) sit in two public
/// galleries; camera C (Sony) and one more Canon photo sit only in a private gallery. Galleries are
/// built and published through <see cref="GalleryService"/>; the projector writes <c>cameras</c>.
/// Sets compare for equality, read counts use <c>==</c>, and "absent" checks look for the element
/// or attribute.
/// </summary>
[Collection("s3")]
public partial class CameraRoutesTests(S3ProxyFixture s3, MongoFixture mongo) : IClassFixture<MongoFixture>
{
    private const string SpeculationRule =
        "{\"prerender\":[{\"where\":{\"or\":[{\"href_matches\":\"/g\"},{\"href_matches\":\"/g/*\"},{\"href_matches\":\"/c\"},{\"href_matches\":\"/c/*\"}]},\"eagerness\":\"moderate\"}]}";

    private const string SlugA = "canon-eos-r5", SlugB = "nikon-z-8", SlugC = "sony-ilce-7m4";

    private static readonly DateTime T0 = new(2026, 3, 1, 9, 0, 0, DateTimeKind.Utc);

    [GeneratedRegex("<script type=\"speculationrules\"(?: b-[a-z0-9]{10})?>(.*?)</script>", RegexOptions.Singleline)]
    private static partial Regex SpeculationRx();

    [GeneratedRegex("<nav class=\"nav nav--home\"[^>]*>")]
    private static partial Regex HomeNavRx();

    [GeneratedRegex("<a\\b[^>]*\\bhref=\"([^\"]*)\"[^>]*>(.*?)</a>", RegexOptions.Singleline)]
    private static partial Regex AnchorRx();

    [GeneratedRegex("<a class=\"tile\"[^>]*href=\"([^\"]*)\"")]
    private static partial Regex TileHrefRx();

    [GeneratedRegex("\\bclass\\s*=\\s*\"([^\"]*)\"")]
    private static partial Regex ClassAttr();

    // ── the library ──────────────────────────────────────────────────────────

    private sealed record Lib(
        IReadOnlyList<string> A, IReadOnlyList<string> B, string C, string PrivateCanon, string Shared);

    private static Task<OperationResult> Gs(LibraryHost h, Func<GalleryService, Task<OperationResult>> call) =>
        h.RunAsync(call);

    private static async Task<string> PhotoAsync(LibraryHost h, string make, string model, int minutes, string title) =>
        (await h.InsertUploadedImageAsync(T0.AddMinutes(minutes), title: title, files: false,
            shape: i => i.Camera = new CameraDoc { Make = make, Model = model })).Id;

    private static async Task<string> GalleryAsync(LibraryHost h, string title, IReadOnlyList<string> ids, bool publish)
    {
        var created = await Gs(h, s => s.CreateAsync(title));
        Assert.True(created.IsOk);
        var gid = created.GalleryId!;
        Assert.True((await Gs(h, s => s.AddAsync(gid, ids))).IsOk);
        if (publish)
            Assert.True((await Gs(h, s => s.PublishAsync(gid, confirmed: true))).IsOk);
        return gid;
    }

    private static async Task<Lib> BuildAsync(LibraryHost h)
    {
        var a1 = await PhotoAsync(h, "Canon", "Canon EOS R5", 1, "Pier at dawn");
        var a2 = await PhotoAsync(h, "Canon", "Canon EOS R5", 2, "Gull");
        var a3 = await PhotoAsync(h, "CANON", "Canon  EOS R5", 3, "Breakwater");
        var b1 = await PhotoAsync(h, "NIKON CORPORATION", "NIKON Z 8", 4, "Fern");
        var c1 = await PhotoAsync(h, "Sony", "ILCE-7M4", 5, "Hidden sony");
        var aPriv = await PhotoAsync(h, "Canon", "Canon EOS R5", 6, "Hidden canon");

        await GalleryAsync(h, "Harbour", [a1, a2], publish: true);
        await GalleryAsync(h, "Meadow", [a2, a3, b1], publish: true);   // a2 sits in both
        await GalleryAsync(h, "Secret", [c1, aPriv, a3], publish: false); // a3 is also public
        await h.SettleAsync();

        // Newest first (R-03).
        return new Lib([a3, a2, a1], [b1], c1, aPriv, a2);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static async Task<string> HtmlAsync(LibraryHost h, string path)
    {
        var resp = await h.GetAsync(path);
        Assert.True(resp.StatusCode == HttpStatusCode.OK, $"GET {path} answered {(int)resp.StatusCode}");
        return await resp.Content.ReadAsStringAsync();
    }

    private static List<string> Tiles(string html) => TileHrefRx().Matches(html).Select(m => m.Groups[1].Value).ToList();

    /// <summary>Every log line written for one GET of <paramref name="path"/>, through its http.request line.</summary>
    private static async Task<(List<string> Lines, string Request)> LinesForAsync(LibraryHost h, string path, string route)
    {
        int before;
        lock (h.LogLines) before = h.LogLines.Count;
        Assert.Equal(HttpStatusCode.OK, (await h.GetAsync(path)).StatusCode);
        var prefix = $"http.request GET {route} ";
        List<string> lines = [];
        for (var i = 0; i < 100; i++)
        {
            lock (h.LogLines)
                lines = h.LogLines.Skip(before).SelectMany(l => l.Split('\n')).ToList();
            if (lines.Any(l => l.StartsWith(prefix, StringComparison.Ordinal))) break;
            await Task.Delay(20);
        }
        var requests = lines.Where(l => l.StartsWith(prefix, StringComparison.Ordinal)).ToList();
        Assert.Single(requests);
        return (lines, requests[0]);
    }

    private static int DbReads(string requestLine)
    {
        var f = requestLine.Split(' ');
        Assert.Equal("200", f[3]);
        return int.Parse(f[^1], System.Globalization.CultureInfo.InvariantCulture);
    }

    private static int CountOp(LibraryHost h, string op)
    {
        lock (h.LogLines)
            return h.LogLines.SelectMany(l => l.Split('\n'))
                .Count(l => l.StartsWith($"op.timing {op} ", StringComparison.Ordinal));
    }

    private static int CountOpIn(List<string> lines, string op) =>
        lines.Count(l => l.StartsWith($"op.timing {op} ", StringComparison.Ordinal));

    private static string? Header(HttpResponseMessage resp, string name) =>
        resp.Headers.TryGetValues(name, out var v) ? string.Join(",", v)
        : resp.Content.Headers.TryGetValues(name, out var c) ? string.Join(",", c) : null;

    // ── T016 part 1: the pages ───────────────────────────────────────────────

    [Fact]
    public async Task Home_links_Galleries_then_By_Camera()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3);
        await BuildAsync(h);

        var html = await HtmlAsync(h, "/");
        var navs = HomeNavRx().Matches(html);
        Assert.Single(navs);
        var nav = LibraryHtml.Element(html, "nav", navs[0].Index);
        var links = AnchorRx().Matches(nav).Select(m => (m.Groups[1].Value, WebUtility.HtmlDecode(m.Groups[2].Value))).ToList();
        Assert.Equal([("/g", "Galleries"), ("/c", "By Camera")], links);
    }

    [Fact]
    public async Task Camera_listing_shows_exactly_the_visible_cameras_in_rank_order()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3);
        await BuildAsync(h);

        var resp = await h.GetAsync("/c");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal(PublicCache.HtmlPolicy, resp.Headers.CacheControl?.ToString());
        var link = Header(resp, "Link");
        Assert.NotNull(link);
        Assert.Contains("rel=preload", link, StringComparison.Ordinal);
        var html = WebUtility.HtmlDecode(await resp.Content.ReadAsStringAsync());

        Assert.Equal([$"/c/{SlugA}", $"/c/{SlugB}"], Tiles(html));
        Assert.Contains("Canon EOS R5", html, StringComparison.Ordinal);
        Assert.Contains("NIKON Z 8", html, StringComparison.Ordinal);
        Assert.DoesNotContain($"href=\"/c/{SlugC}\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Sony", html, StringComparison.Ordinal);
        Guards.AssertNoInlineStyle(html);
    }

    [Fact]
    public async Task Camera_page_shows_exactly_its_visible_photos_each_once()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3);
        var lib = await BuildAsync(h);

        var resp = await h.GetAsync($"/c/{SlugA}");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal(PublicCache.HtmlPolicy, resp.Headers.CacheControl?.ToString());
        Assert.NotNull(Header(resp, "Link"));
        var html = WebUtility.HtmlDecode(await resp.Content.ReadAsStringAsync());

        // Exactly A's visible photos, newest first, the shared one once; the private Canon absent.
        Assert.Equal(lib.A.Select(id => $"/c/{SlugA}/{id}").ToList(), Tiles(html));
        Assert.Single(Regex.Matches(html, $"href=\"/c/{SlugA}/{lib.Shared}\""));
        Assert.DoesNotContain(lib.PrivateCanon, html, StringComparison.Ordinal);
        Assert.DoesNotContain("Hidden canon", html, StringComparison.Ordinal);
        Assert.Contains("Pier at dawn", html, StringComparison.Ordinal);

        var b = WebUtility.HtmlDecode(await HtmlAsync(h, $"/c/{SlugB}"));
        Assert.Equal(lib.B.Select(id => $"/c/{SlugB}/{id}").ToList(), Tiles(b));
        Guards.AssertNoInlineStyle(html);
    }

    [Fact]
    public async Task Trailing_slash_listing_redirects_to_c_with_the_short_policy()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3);
        var resp = await h.GetAsync("/c/");
        Assert.Equal(HttpStatusCode.MovedPermanently, resp.StatusCode);
        Assert.Equal("/c", resp.Headers.Location?.ToString());
        Assert.Equal(PublicCache.ShortPolicy, resp.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task Camera_routes_keep_their_exact_read_budget_and_Server_Timing()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3);
        await BuildAsync(h);

        var (listLines, listReq) = await LinesForAsync(h, "/c", "/c");
        Assert.Equal(1, DbReads(listReq));
        Assert.Equal(1, CountOpIn(listLines, "db.cameras.list"));
        Assert.Equal(1, listLines.Count(l => l.StartsWith("op.timing db.", StringComparison.Ordinal)));

        var (camLines, camReq) = await LinesForAsync(h, $"/c/{SlugA}", "/c/{slug}");
        Assert.Equal(2, DbReads(camReq));
        Assert.Equal(1, CountOpIn(camLines, "db.camera.read"));
        Assert.Equal(1, CountOpIn(camLines, "db.images.read"));
        Assert.Equal(2, camLines.Count(l => l.StartsWith("op.timing db.", StringComparison.Ordinal)));

        var (_, homeReq) = await LinesForAsync(h, "/", "/");
        Assert.Equal(0, DbReads(homeReq));

        foreach (var path in new[] { "/", "/c", $"/c/{SlugA}" })
        {
            var resp = await h.GetAsync(path);
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
            Assert.True(resp.Headers.TryGetValues("Server-Timing", out var st) && st.Any(v => v.Length > 0), $"{path}: no Server-Timing");
        }
    }

    [Fact]
    public async Task With_the_output_cache_on_a_second_GET_adds_no_read()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3,
            extraOverrides: new() { ["Site:OutputCache"] = "true" });
        await BuildAsync(h);

        var list0 = CountOp(h, "db.cameras.list");
        await HtmlAsync(h, "/c");
        await HtmlAsync(h, "/c");
        Assert.Equal(list0 + 1, CountOp(h, "db.cameras.list"));

        var cam0 = CountOp(h, "db.camera.read");
        var img0 = CountOp(h, "db.images.read");
        await HtmlAsync(h, $"/c/{SlugA}");
        await HtmlAsync(h, $"/c/{SlugA}");
        Assert.Equal(cam0 + 1, CountOp(h, "db.camera.read"));
        Assert.Equal(img0 + 1, CountOp(h, "db.images.read"));
    }

    // ── T017 part 2 [SAFETY] FR-009 / FR-010 ─────────────────────────────────

    [Fact]
    public async Task A_private_only_camera_an_unknown_slug_and_junk_answer_the_same_404()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3);
        // Non-vacuity: C's photo is stored, it is just in no listed gallery.
        var lib = await BuildAsync(h);
        Assert.Equal(1, await h.Images.CountDocumentsAsync(i => i.Id == lib.C));
        var stored = await h.Db.GetCollection<CameraGroupDoc>("cameras").Find(FilterDefinition<CameraGroupDoc>.Empty).ToListAsync();
        Assert.True(new HashSet<string>(stored.Select(c => c.Slug)).SetEquals([SlugA, SlugB]));

        var answers = new List<(string Path, int Status, string? Cache, string? Robots, byte[] Body)>();
        foreach (var path in new[] { $"/c/{SlugC}", "/c/no-such-camera", "/c/BAD!" })
        {
            var resp = await h.GetAsync(path);
            answers.Add((path, (int)resp.StatusCode, resp.Headers.CacheControl?.ToString(), Header(resp, "X-Robots-Tag"),
                await resp.Content.ReadAsByteArrayAsync()));
        }

        var first = answers[0];
        Assert.Equal(404, first.Status);
        Assert.Equal(PublicCache.ShortPolicy, first.Cache);
        Assert.Equal("noindex", first.Robots);
        Assert.NotEmpty(first.Body);
        foreach (var a in answers.Skip(1))
        {
            Assert.True(a.Status == first.Status, $"{a.Path}: status {a.Status}");
            Assert.True(a.Cache == first.Cache, $"{a.Path}: Cache-Control {a.Cache}");
            Assert.True(a.Robots == first.Robots, $"{a.Path}: X-Robots-Tag {a.Robots}");
            Assert.True(a.Body.AsSpan().SequenceEqual(first.Body), $"{a.Path}: body differs from {first.Path}");
        }
    }

    [Fact]
    public async Task Camera_pages_link_and_name_no_gallery_and_carry_no_robots_header()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3);
        await BuildAsync(h);

        foreach (var path in new[] { "/c", $"/c/{SlugA}" })
        {
            var resp = await h.GetAsync(path);
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
            Assert.False(resp.Headers.Contains("X-Robots-Tag"), $"{path} sends X-Robots-Tag");
            var html = WebUtility.HtmlDecode(await resp.Content.ReadAsStringAsync());
            Assert.DoesNotContain("href=\"/g/", html, StringComparison.Ordinal);
            Assert.DoesNotContain("href=\"/g\"", html, StringComparison.Ordinal);
            Assert.DoesNotContain("<meta name=\"robots\"", html, StringComparison.Ordinal);
            foreach (var gallery in new[] { "Harbour", "Meadow", "Secret" })
                Assert.False(html.Contains(gallery, StringComparison.Ordinal), $"{path} names the gallery {gallery}");
        }
    }

    // ── T018 part 3: test-impact (B) scans over the camera pages ─────────────

    private static string[] CameraPaths(Lib lib) =>
    [
        "/c",
        $"/c/{SlugA}",
        // T029 adds the photo route
        $"/c/{SlugA}/{lib.A[0]}",
    ];

    /// <summary>Copy of <c>LibrarySourceGuardTests.AssertNoOwnerMarkup</c> (that file is not ours to edit).</summary>
    private static void AssertNoOwnerMarkup(string label, string html)
    {
        Assert.False(html.Contains("/admin-assets/", StringComparison.Ordinal), $"{label} references /admin-assets/");
        Assert.False(html.Contains("/admin-assets", StringComparison.Ordinal), $"{label} references /admin-assets");
        Assert.False(html.Contains("owner-bar", StringComparison.Ordinal), $"{label} contains owner-bar");
        var owner = ClassAttr().Matches(Guards.StripStyle(html))
            .SelectMany(m => m.Groups[1].Value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .Where(c => c.StartsWith("lib-", StringComparison.Ordinal)
                        || c.StartsWith("gal-", StringComparison.Ordinal)
                        || c == "b-ctl")
            .Distinct(StringComparer.Ordinal)
            .ToList();
        Assert.True(owner.Count == 0, $"{label} carries owner classes: {string.Join(", ", owner)}");
    }

    [Fact]
    public async Task Camera_pages_carry_no_owner_markup_with_or_without_the_owner_session()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3);
        var built = await BuildAsync(h);

        // Non-vacuity: the owner session is live (an admin page renders the owner bar).
        var lib = await h.GetAsync("/admin/library");
        Assert.Equal(HttpStatusCode.OK, lib.StatusCode);
        Assert.Contains("owner-bar", await lib.Content.ReadAsStringAsync());

        foreach (var session in new[] { "with the owner session", "anonymous" })
        {
            if (session == "anonymous") h.Host.ClearCookies();
            foreach (var path in CameraPaths(built))
            {
                var resp = await h.GetAsync(path);
                Assert.True(resp.StatusCode == HttpStatusCode.OK, $"GET {path} ({session}) answered {(int)resp.StatusCode}");
                var html = await resp.Content.ReadAsStringAsync();
                Assert.True(WebUtility.HtmlDecode(html).Contains("Canon EOS R5", StringComparison.Ordinal),
                    $"GET {path} ({session}) does not show the camera");
                AssertNoOwnerMarkup($"GET {path} ({session})", html);
            }
        }

        var signedOut = await h.GetAsync("/admin/library");
        Assert.Equal(HttpStatusCode.Redirect, signedOut.StatusCode);
    }

    [Fact]
    public async Task Speculation_rule_is_exact_on_every_camera_page()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3);
        var built = await BuildAsync(h);

        foreach (var path in CameraPaths(built))
        {
            var html = await HtmlAsync(h, path);
            var rules = SpeculationRx().Matches(html);
            Assert.True(rules.Count == 1, $"{path}: {rules.Count} speculation rules");
            Assert.Equal(SpeculationRule, rules[0].Groups[1].Value);
        }
    }
}
