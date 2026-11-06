using System.Net;
using System.Text.RegularExpressions;
using Gallery.Web.Data;
using Gallery.Web.Library;
using Gallery.Web.Site;
using Gallery.Web.Tests.Fixtures;
using MongoDB.Driver;

namespace Gallery.Web.Tests;

/// <summary>
/// 013 T027 [SAFETY] (contracts/routes.md C3, components.md K3; research R-07, R-09): the camera
/// photo page <c>/c/{slug}/{iid}</c> over HTTP on a <see cref="LibraryHost"/>. Camera A (Canon,
/// three visible photos, one of them in both public galleries) and B (Nikon, one photo) sit in two
/// public galleries; camera C (Sony) and one more Canon photo sit only in a private gallery.
/// Sequences compare for equality, read counts use <c>==</c>, every 404 is compared on status,
/// Cache-Control, X-Robots-Tag and body bytes, and "absent" checks look for the attribute.
/// </summary>
[Collection("s3")]
public partial class CameraPhotoRoutesTests(S3ProxyFixture s3, MongoFixture mongo) : IClassFixture<MongoFixture>
{
    private const string SlugA = "canon-eos-r5", SlugB = "nikon-z-8", SlugC = "sony-ilce-7m4";
    private const string NameA = "Canon EOS R5";

    private static readonly DateTime T0 = new(2026, 3, 1, 9, 0, 0, DateTimeKind.Utc);

    [GeneratedRegex("<nav class=\"breadcrumb\"[^>]*>")]
    private static partial Regex BreadcrumbRx();

    [GeneratedRegex("<li\\b[^>]*>(.*?)</li>", RegexOptions.Singleline)]
    private static partial Regex LiRx();

    [GeneratedRegex("<a\\b[^>]*\\bhref=\"([^\"]*)\"[^>]*>(.*?)</a>", RegexOptions.Singleline)]
    private static partial Regex AnchorRx();

    [GeneratedRegex("<span\\b[^>]*>(.*?)</span>", RegexOptions.Singleline)]
    private static partial Regex SpanRx();

    [GeneratedRegex("<p class=\"photo-count\"[^>]*>(.*?)</p>", RegexOptions.Singleline)]
    private static partial Regex PhotoCountRx();

    [GeneratedRegex("<a\\b[^>]*\\bhref=\"([^\"]*)\"[^>]*\\brel=\"(prev|next)\"")]
    private static partial Regex PagerRx();

    [GeneratedRegex("<p class=\"metadata-bar\"[^>]*>")]
    private static partial Regex MetadataRx();

    [GeneratedRegex("<title[^>]*>(.*?)</title>", RegexOptions.Singleline)]
    private static partial Regex TitleRx();

    /// <summary>The solo article's prefetch attribute (the element, not a script's mention of it).</summary>
    [GeneratedRegex("<article class=\"solo\"[^>]*\\bdata-prefetch=\"[^\"]+\"")]
    private static partial Regex SoloPrefetchRx();

    // ── the library ──────────────────────────────────────────────────────────

    private sealed record Lib(
        IReadOnlyList<string> A, IReadOnlyList<string> B, string C, string PrivateCanon,
        string HarbourId, IReadOnlyDictionary<string, string> Titles);

    private static Task<OperationResult> Gs(LibraryHost h, Func<GalleryService, Task<OperationResult>> call) =>
        h.RunAsync(call);

    private static async Task<string> PhotoAsync(LibraryHost h, string make, string model, int minutes, string title) =>
        (await h.InsertUploadedImageAsync(T0.AddMinutes(minutes), title: title, files: false,
            shape: i => i.Camera = new CameraDoc
            {
                Make = make, Model = model, Lens = "RF 24-70mm F2.8", FocalMm = 35m, FNumber = 4m, Exposure = 0.004m, Iso = 200,
            })).Id;

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

        var harbour = await GalleryAsync(h, "Harbour", [a1, a2], publish: true);
        await GalleryAsync(h, "Meadow", [a2, a3, b1], publish: true);   // a2 sits in both
        await GalleryAsync(h, "Secret", [c1, aPriv, a3], publish: false); // a3 is also public
        await h.SettleAsync();

        var titles = new Dictionary<string, string>
        {
            [a1] = "Pier at dawn", [a2] = "Gull", [a3] = "Breakwater", [b1] = "Fern", [c1] = "Hidden sony", [aPriv] = "Hidden canon",
        };
        // Newest first (R-03); the stored camera must agree (non-vacuity of the sequence below).
        var stored = await h.Db.GetCollection<CameraGroupDoc>("cameras").Find(c => c.Slug == SlugA).FirstAsync();
        Assert.Equal([a3, a2, a1], stored.Items);
        return new Lib([a3, a2, a1], [b1], c1, aPriv, harbour, titles);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static async Task<string> HtmlAsync(LibraryHost h, string path)
    {
        var resp = await h.GetAsync(path);
        Assert.True(resp.StatusCode == HttpStatusCode.OK, $"GET {path} answered {(int)resp.StatusCode}");
        return await resp.Content.ReadAsStringAsync();
    }

    private static List<(string Label, string? Href)> Crumbs(string html)
    {
        var navs = BreadcrumbRx().Matches(html);
        Assert.Single(navs);
        var nav = LibraryHtml.Element(html, "nav", navs[0].Index);
        return LiRx().Matches(nav).Select(li =>
        {
            var inner = li.Groups[1].Value;
            var a = AnchorRx().Match(inner);
            return a.Success
                ? (WebUtility.HtmlDecode(a.Groups[2].Value), (string?)a.Groups[1].Value)
                : (WebUtility.HtmlDecode(SpanRx().Match(inner).Groups[1].Value), (string?)null);
        }).ToList();
    }

    private static string? Pager(string html, string rel) =>
        PagerRx().Matches(html).Where(m => m.Groups[2].Value == rel).Select(m => m.Groups[1].Value).SingleOrDefault();

    private static string Metadata(string html)
    {
        var m = MetadataRx().Matches(html);
        Assert.Single(m);
        return LibraryHtml.Element(html, "p", m[0].Index);
    }

    private static string? Header(HttpResponseMessage resp, string name) =>
        resp.Headers.TryGetValues(name, out var v) ? string.Join(",", v)
        : resp.Content.Headers.TryGetValues(name, out var c) ? string.Join(",", c) : null;

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

    private static int CountOp(LibraryHost h, string op)
    {
        lock (h.LogLines)
            return h.LogLines.SelectMany(l => l.Split('\n'))
                .Count(l => l.StartsWith($"op.timing {op} ", StringComparison.Ordinal));
    }

    // ── crumbs, count, stepping ──────────────────────────────────────────────

    [Fact]
    public async Task Every_camera_photo_has_the_camera_trail_its_count_and_neighbours_within_the_camera()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3);
        var lib = await BuildAsync(h);
        var failures = new List<string>();

        for (var i = 0; i < lib.A.Count; i++)
        {
            var iid = lib.A[i];
            var path = $"/c/{SlugA}/{iid}";
            var resp = await h.GetAsync(path);
            Assert.True(resp.StatusCode == HttpStatusCode.OK, $"GET {path} answered {(int)resp.StatusCode}");
            Assert.Equal(PublicCache.HtmlPolicy, resp.Headers.CacheControl?.ToString());
            Assert.Contains("rel=preload", Header(resp, "Link") ?? "", StringComparison.Ordinal);
            var html = await resp.Content.ReadAsStringAsync();

            List<(string, string?)> expected =
                [("Home", "/"), ("By Camera", "/c"), (NameA, $"/c/{SlugA}#p-{iid}"), (lib.Titles[iid], null)];
            var crumbs = Crumbs(html);
            if (!crumbs.SequenceEqual(expected))
                failures.Add($"{path}: crumbs [{string.Join(", ", crumbs)}], expected [{string.Join(", ", expected)}]");

            var count = PhotoCountRx().Matches(html).Select(m => m.Groups[1].Value).ToList();
            if (!count.SequenceEqual([$"{i + 1} / {lib.A.Count}"]))
                failures.Add($"{path}: photo-count [{string.Join(", ", count)}], expected {i + 1} / {lib.A.Count}");

            var prev = Pager(html, "prev");
            var next = Pager(html, "next");
            var wantPrev = i > 0 ? $"/c/{SlugA}/{lib.A[i - 1]}" : null;
            var wantNext = i < lib.A.Count - 1 ? $"/c/{SlugA}/{lib.A[i + 1]}" : null;
            if (prev != wantPrev) failures.Add($"{path}: prev {prev ?? "(none)"}, expected {wantPrev ?? "(none)"}");
            if (next != wantNext) failures.Add($"{path}: next {next ?? "(none)"}, expected {wantNext ?? "(none)"}");

            var hashLinks = Regex.Matches(LibraryHtml.Element(html, "nav", BreadcrumbRx().Match(html).Index), "<a\\b[^>]*\\bhref=\"[^\"]*#p-[^\"]*\"").Count;
            if (hashLinks != 1) failures.Add($"{path}: {hashLinks} breadcrumb links with #p-, expected 1");

            var title = WebUtility.HtmlDecode(TitleRx().Match(html).Groups[1].Value);
            if (title != $"{lib.Titles[iid]} — {NameA}") failures.Add($"{path}: <title> {title}");
        }

        // A one-photo camera: N / total is 1 / 1 and neither neighbour exists.
        var b = await HtmlAsync(h, $"/c/{SlugB}/{lib.B[0]}");
        if (Pager(b, "prev") is not null || Pager(b, "next") is not null) failures.Add("camera B: a pager link on a one-photo camera");
        if (!PhotoCountRx().Matches(b).Select(m => m.Groups[1].Value).SequenceEqual(["1 / 1"])) failures.Add("camera B: count is not 1 / 1");

        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    [Fact]
    public async Task Metadata_bar_is_the_same_as_on_the_photos_gallery_photo_page()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3);
        var lib = await BuildAsync(h);
        var g = await h.Galleries.Find(x => x.Id == lib.HarbourId).FirstAsync();
        var iid = lib.A[2]; // a1, in Harbour

        var gallery = Metadata(await HtmlAsync(h, $"/g/{g.Id}/{g.Slug}/{iid}"));
        var camera = Metadata(await HtmlAsync(h, $"/c/{SlugA}/{iid}"));
        // Non-vacuity: the bar carries the camera and the exposure fields.
        Assert.Contains("Canon EOS R5", WebUtility.HtmlDecode(gallery), StringComparison.Ordinal);
        Assert.Contains("ISO", gallery, StringComparison.Ordinal);
        Assert.Equal(System.Text.Encoding.UTF8.GetBytes(gallery), System.Text.Encoding.UTF8.GetBytes(camera));
    }

    // ── [SAFETY] read budget ─────────────────────────────────────────────────

    [Fact]
    public async Task Camera_photo_page_makes_exactly_two_reads()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3);
        var lib = await BuildAsync(h);

        foreach (var iid in lib.A)
        {
            var (lines, req) = await LinesForAsync(h, $"/c/{SlugA}/{iid}", "/c/{slug}/{iid}");
            var f = req.Split(' ');
            Assert.Equal("200", f[3]);
            Assert.True(int.Parse(f[^1], System.Globalization.CultureInfo.InvariantCulture) == 2, $"{iid}: {req}");
            Assert.True(lines.Count(l => l.StartsWith("op.timing db.camera.read ", StringComparison.Ordinal)) == 1, $"{iid}: camera reads");
            Assert.True(lines.Count(l => l.StartsWith("op.timing db.images.read ", StringComparison.Ordinal)) == 1, $"{iid}: image reads");
            Assert.True(lines.Count(l => l.StartsWith("op.timing db.", StringComparison.Ordinal)) == 2, $"{iid}: db timers");
        }

        var resp = await h.GetAsync($"/c/{SlugA}/{lib.A[1]}");
        Assert.True(resp.Headers.TryGetValues("Server-Timing", out var st) && st.Any(v => v.Length > 0), "no Server-Timing");
    }

    [Fact]
    public async Task With_the_output_cache_on_a_second_GET_adds_no_read()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3,
            extraOverrides: new() { ["Site:OutputCache"] = "true" });
        var lib = await BuildAsync(h);

        var cam0 = CountOp(h, "db.camera.read");
        var img0 = CountOp(h, "db.images.read");
        await HtmlAsync(h, $"/c/{SlugA}/{lib.A[1]}");
        await HtmlAsync(h, $"/c/{SlugA}/{lib.A[1]}");
        Assert.Equal(cam0 + 1, CountOp(h, "db.camera.read"));
        Assert.Equal(img0 + 1, CountOp(h, "db.images.read"));
    }

    // ── [SAFETY] FR-009: every refusal is the same 404 ───────────────────────

    [Fact]
    public async Task Every_refusal_answers_the_same_404()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3);
        var lib = await BuildAsync(h);
        // Non-vacuity: the refused ids are stored, valid ids; C's camera simply is not listed.
        Assert.Equal(1, await h.Images.CountDocumentsAsync(i => i.Id == lib.PrivateCanon));
        Assert.Equal(1, await h.Images.CountDocumentsAsync(i => i.Id == lib.C));
        Assert.True(Gallery.Core.Ids.OpaqueId.IsValid(lib.PrivateCanon));

        var unknownValid = Gallery.Core.Ids.OpaqueId.NewId();
        var paths = new[]
        {
            $"/c/{SlugA}/not-an-id",            // invalid iid
            $"/c/{SlugA}/{unknownValid}",       // valid iid, not in the camera (and not stored)
            $"/c/{SlugA}/{lib.PrivateCanon}",   // a private-only photo of this camera
            $"/c/{SlugA}/{lib.B[0]}",           // a visible photo of another camera
            $"/c/{SlugC}/{lib.C}",              // a private-only camera's photo
            $"/c/no-such-camera/{lib.A[0]}",    // unknown slug
            $"/c/BAD!/{lib.A[0]}",              // junk slug
            "/c/no-such-camera",                // the C2 refusal, as the reference
        };

        var answers = new List<(string Path, int Status, string? Cache, string? Robots, byte[] Body)>();
        foreach (var path in paths)
        {
            var resp = await h.GetAsync(path);
            answers.Add((path, (int)resp.StatusCode, resp.Headers.CacheControl?.ToString(), Header(resp, "X-Robots-Tag"),
                await resp.Content.ReadAsByteArrayAsync()));
        }

        var first = answers[^1];
        Assert.Equal(404, first.Status);
        Assert.Equal(PublicCache.ShortPolicy, first.Cache);
        Assert.Equal("noindex", first.Robots);
        Assert.NotEmpty(first.Body);
        var failures = new List<string>();
        foreach (var a in answers)
        {
            if (a.Status != first.Status) failures.Add($"{a.Path}: status {a.Status}");
            if (a.Cache != first.Cache) failures.Add($"{a.Path}: Cache-Control {a.Cache}");
            if (a.Robots != first.Robots) failures.Add($"{a.Path}: X-Robots-Tag {a.Robots}");
            if (!a.Body.AsSpan().SequenceEqual(first.Body)) failures.Add($"{a.Path}: body differs");
        }
        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    [Fact]
    public async Task A_tombstoned_photo_answers_the_same_404()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3);
        var lib = await BuildAsync(h);
        var reference = await h.GetAsync("/c/no-such-camera");
        var refBody = await reference.Content.ReadAsByteArrayAsync();
        // Non-vacuity: before the tombstone, the first photo prefetches its only neighbour, A[1].
        Assert.Matches(SoloPrefetchRx(), await HtmlAsync(h, $"/c/{SlugA}/{lib.A[0]}"));

        // Deleting between projection and request: the stored camera still lists it.
        await h.Images.UpdateOneAsync(i => i.Id == lib.A[1], Builders<ImageDoc>.Update.Set(i => i.DeletingAt, DateTime.UtcNow));
        var stored = await h.Db.GetCollection<CameraGroupDoc>("cameras").Find(c => c.Slug == SlugA).FirstAsync();
        Assert.Contains(lib.A[1], stored.Items);

        var resp = await h.GetAsync($"/c/{SlugA}/{lib.A[1]}");
        Assert.Equal(404, (int)resp.StatusCode);
        Assert.Equal(reference.Headers.CacheControl?.ToString(), resp.Headers.CacheControl?.ToString());
        Assert.Equal(Header(reference, "X-Robots-Tag"), Header(resp, "X-Robots-Tag"));
        Assert.True((await resp.Content.ReadAsByteArrayAsync()).AsSpan().SequenceEqual(refBody), "body differs");

        // The first photo no longer prefetches it (the guard drops the doc; A[1] was its only neighbour).
        Assert.DoesNotMatch(SoloPrefetchRx(), await HtmlAsync(h, $"/c/{SlugA}/{lib.A[0]}"));
    }

    // ── [SAFETY] FR-010: no gallery link, indexable ──────────────────────────

    [Fact]
    public async Task Camera_photo_pages_link_and_name_no_gallery_and_carry_no_robots_header()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3);
        var lib = await BuildAsync(h);

        foreach (var iid in lib.A.Concat(lib.B))
        {
            var slug = lib.A.Contains(iid) ? SlugA : SlugB;
            var path = $"/c/{slug}/{iid}";
            var resp = await h.GetAsync(path);
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
            Assert.False(resp.Headers.Contains("X-Robots-Tag"), $"{path} sends X-Robots-Tag");
            var html = WebUtility.HtmlDecode(await resp.Content.ReadAsStringAsync());
            Assert.DoesNotContain("href=\"/g/", html, StringComparison.Ordinal);
            Assert.DoesNotContain("href=\"/g\"", html, StringComparison.Ordinal);
            Assert.DoesNotContain("<meta name=\"robots\"", html, StringComparison.Ordinal);
            foreach (var gallery in new[] { "Harbour", "Meadow", "Secret" })
                Assert.False(html.Contains(gallery, StringComparison.Ordinal), $"{path} names the gallery {gallery}");
            Guards.AssertNoInlineStyle(html);
        }
    }
}
