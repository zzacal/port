using System.Net;
using System.Text.RegularExpressions;
using Gallery.Core.Content;
using Gallery.Web.Library;
using Gallery.Web.Tests.Fixtures;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Gallery.Web.Tests;

/// <summary>
/// 010 T030/T031 [SAFETY] (contracts/components.md P1, P2, K7; data-model "Derived"; research R-08,
/// R-09): the public routes over HTTP on a <see cref="LibraryHost"/>. Galleries are created,
/// filled and published through <see cref="GalleryService"/>; the ONLY direct <c>nav</c> write is
/// the stale nav planted on a private gallery in case (e). Copy is checked after
/// <see cref="WebUtility.HtmlDecode"/>; "no link" asserts the element is absent.
/// </summary>
[Collection("s3")]
public partial class NavRoutesTests(S3ProxyFixture s3, MongoFixture mongo) : IClassFixture<MongoFixture>
{
    // Scoped CSS adds b-xxxx attributes to elements, so every tag pattern allows extra attributes.
    [GeneratedRegex("<p class=\"gallery-link\"[^>]*><a class=\"text-link tap\" href=\"([^\"]*)\"[^>]*>← (.*?)</a><span class=\"count\"[^>]*>( · \\d+ / \\d+)</span></p>", RegexOptions.Singleline)]
    private static partial Regex GalleryLinkRx();

    [GeneratedRegex("<nav class=\"gallery-neighbours\"[^>]*>(.*?)</nav>", RegexOptions.Singleline)]
    private static partial Regex NeighboursRx();

    [GeneratedRegex("<a [^>]*href=\"([^\"]*)\"[^>]*rel=\"(prev|next)\"[^>]*>(.*?)</a>", RegexOptions.Singleline)]
    private static partial Regex RelLinkRx();

    [GeneratedRegex("<nav class=\"pager[^\"]*\"[^>]*>(.*?)</nav>", RegexOptions.Singleline)]
    private static partial Regex PagerRx();

    [GeneratedRegex("<header class=\"site-header\"[^>]*>(.*?)</header>", RegexOptions.Singleline)]
    private static partial Regex HeaderRx();

    [GeneratedRegex("href=\"([^\"]*)\"")]
    private static partial Regex HrefRx();

    // ── helpers ──────────────────────────────────────────────────────────────

    private static Task<OperationResult> Gs(LibraryHost h, Func<GalleryService, Task<OperationResult>> call) =>
        h.RunAsync(call);

    /// <summary>A gallery of <paramref name="photos"/> photos, created, filled (and published) through GalleryService.</summary>
    private static async Task<(string Id, string Slug, List<string> Items)> GalleryAsync(
        LibraryHost h, string title, int photos, bool publish = true)
    {
        var created = await Gs(h, s => s.CreateAsync(title));
        Assert.True(created.IsOk);
        var gid = created.GalleryId!;
        var t0 = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc);
        var ids = new List<string>();
        for (var i = 0; i < photos; i++)
            ids.Add((await h.InsertUploadedImageAsync(t0.AddMinutes(i), title: $"{title} {i:00}", files: false)).Id);
        Assert.True((await Gs(h, s => s.AddAsync(gid, ids))).IsOk);
        if (publish)
            Assert.True((await Gs(h, s => s.PublishAsync(gid, confirmed: true))).IsOk);
        var g = await h.Galleries.Find(x => x.Id == gid).FirstAsync();
        return (gid, g.Slug, g.Items.ToList());
    }

    private static async Task<string> SlugAsync(LibraryHost h, string gid) =>
        (await h.Galleries.Find(g => g.Id == gid).FirstAsync()).Slug;

    private static async Task<string> HtmlAsync(LibraryHost h, string path, HttpStatusCode status = HttpStatusCode.OK)
    {
        var resp = await h.GetAsync(path);
        Assert.True(resp.StatusCode == status, $"GET {path} answered {(int)resp.StatusCode}, expected {(int)status}");
        return WebUtility.HtmlDecode(await resp.Content.ReadAsStringAsync());
    }

    /// <summary>The gallery-neighbours links in document order: (rel, href, text). Empty when the element is absent.</summary>
    private static List<(string Rel, string Href, string Text)> Neighbours(string html)
    {
        var nav = NeighboursRx().Matches(html);
        Assert.True(nav.Count <= 1, "more than one gallery-neighbours element");
        if (nav.Count == 0) return [];
        return [.. RelLinkRx().Matches(nav[0].Groups[1].Value).Select(m => (m.Groups[2].Value, m.Groups[1].Value, m.Groups[3].Value))];
    }

    private static string Pager(string html)
    {
        var m = PagerRx().Match(html);
        Assert.True(m.Success, "no pager");
        return m.Groups[1].Value;
    }

    private static List<string> HeaderHrefs(string html)
    {
        var headers = HeaderRx().Matches(html);
        Assert.Single(headers);
        return [.. HrefRx().Matches(headers[0].Groups[1].Value).Select(m => m.Groups[1].Value)];
    }

    /// <summary>GETs <paramref name="path"/> and returns its single <c>http.request</c> log line.</summary>
    private static async Task<string> RequestLineAsync(LibraryHost h, string path, string route)
    {
        int before;
        lock (h.LogLines) before = h.LogLines.Count;
        Assert.Equal(HttpStatusCode.OK, (await h.GetAsync(path)).StatusCode);
        var prefix = $"http.request GET {route} ";
        List<string> lines = [];
        for (var i = 0; i < 100; i++)
        {
            lock (h.LogLines)
                lines = h.LogLines.Skip(before).SelectMany(l => l.Split('\n'))
                    .Where(l => l.StartsWith(prefix, StringComparison.Ordinal)).ToList();
            if (lines.Count > 0) break;
            await Task.Delay(20);
        }
        Assert.Single(lines);
        return lines[0];
    }

    private static int DbReads(string line)
    {
        var f = line.Split(' ');
        Assert.Equal("200", f[3]);
        return int.Parse(f[^1], System.Globalization.CultureInfo.InvariantCulture);
    }

    // ── (a) photo page: gallery link and counter ─────────────────────────────

    [Fact]
    public async Task Photo_page_names_its_gallery_and_position_and_links_back_to_the_tile()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3);
        var big = await GalleryAsync(h, "September 6", 23);
        var one = await GalleryAsync(h, "Solo", 1);
        await h.SettleAsync();

        // Photo 4 of 23: the way back is the tile anchor, then the counter.
        var html = await HtmlAsync(h, $"/g/{big.Id}/{big.Slug}/{big.Items[3]}");
        var link = GalleryLinkRx().Match(html);
        Assert.True(link.Success, "no gallery link");
        Assert.Equal($"/g/{big.Id}/{big.Slug}#p-{big.Items[3]}", link.Groups[1].Value);
        Assert.Equal("September 6", link.Groups[2].Value);
        Assert.Equal(" · 4 / 23", link.Groups[3].Value);
        Assert.Contains("← September 6", html);
        Assert.Contains(" · 4 / 23", html);
        var pager = Pager(html);
        Assert.Contains($"href=\"/g/{big.Id}/{big.Slug}/{big.Items[2]}\"", pager);
        Assert.Contains($"href=\"/g/{big.Id}/{big.Slug}/{big.Items[4]}\"", pager);
        // UI-3: nothing added before the photo's stage.
        Assert.True(html.IndexOf("class=\"stage\"", StringComparison.Ordinal) < html.IndexOf("class=\"gallery-link\"", StringComparison.Ordinal));

        // First: 1 / 23, no Previous link.
        html = await HtmlAsync(h, $"/g/{big.Id}/{big.Slug}/{big.Items[0]}");
        link = GalleryLinkRx().Match(html);
        Assert.Equal(" · 1 / 23", link.Groups[3].Value);
        Assert.Equal($"/g/{big.Id}/{big.Slug}#p-{big.Items[0]}", link.Groups[1].Value);
        pager = Pager(html);
        Assert.DoesNotContain("rel=\"prev\"", pager);
        Assert.Contains("rel=\"next\"", pager);

        // Last: 23 / 23, no Next link.
        html = await HtmlAsync(h, $"/g/{big.Id}/{big.Slug}/{big.Items[22]}");
        Assert.Equal(" · 23 / 23", GalleryLinkRx().Match(html).Groups[3].Value);
        pager = Pager(html);
        Assert.Contains("rel=\"prev\"", pager);
        Assert.DoesNotContain("rel=\"next\"", pager);

        // One-photo gallery: 1 / 1, neither Previous nor Next.
        html = await HtmlAsync(h, $"/g/{one.Id}/{one.Slug}/{one.Items[0]}");
        link = GalleryLinkRx().Match(html);
        Assert.Equal(" · 1 / 1", link.Groups[3].Value);
        Assert.Equal($"/g/{one.Id}/{one.Slug}#p-{one.Items[0]}", link.Groups[1].Value);
        pager = Pager(html);
        Assert.DoesNotContain("rel=\"prev\"", pager);
        Assert.DoesNotContain("rel=\"next\"", pager);
        Assert.DoesNotContain("Previous", pager);
        Assert.DoesNotContain("Next", pager);

        // UI-4: no key hints on the new controls.
        Assert.DoesNotContain("aria-keyshortcuts", html);
        Assert.DoesNotContain(" title=", link.Value);
        Assert.DoesNotContain(" title=", pager);
        Guards.AssertNoInlineStyle(html);
    }

    // ── (b) reads, (g) Server-Timing ─────────────────────────────────────────

    [Fact]
    public async Task Routes_keep_their_read_budget_and_Server_Timing()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3);
        var lib = await NavLibrary.BuildAsync(h);
        var b = await h.Galleries.Find(g => g.Id == lib.B).FirstAsync();

        Assert.Equal(1, DbReads(await RequestLineAsync(h, "/", "/")));
        Assert.True(DbReads(await RequestLineAsync(h, $"/g/{b.Id}/{b.Slug}", "/g/{id}/{slug}")) <= 2);
        Assert.True(DbReads(await RequestLineAsync(h, $"/g/{b.Id}/{b.Slug}/{b.Items[0]}", "/g/{id}/{slug}/{imageId}")) <= 2);

        foreach (var path in new[] { "/", $"/g/{b.Id}/{b.Slug}", $"/g/{b.Id}/{b.Slug}/{b.Items[0]}" })
        {
            var resp = await h.GetAsync(path);
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
            Assert.True(resp.Headers.TryGetValues("Server-Timing", out var st) && st.Any(v => v.Length > 0), $"{path}: no Server-Timing");
        }
    }

    // ── (c) gallery neighbours in home order ─────────────────────────────────

    [Fact]
    public async Task Gallery_pages_link_their_listed_neighbours_in_home_order()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3);
        var a = await GalleryAsync(h, "Alpine", 2);
        var b = await GalleryAsync(h, "Birds", 2);
        var c = await GalleryAsync(h, "Coast", 2);
        await h.SettleAsync();

        var onB = Neighbours(await HtmlAsync(h, $"/g/{b.Id}/{b.Slug}"));
        Assert.Equal([("prev", $"/g/{a.Id}/{a.Slug}", "← Alpine"), ("next", $"/g/{c.Id}/{c.Slug}", "Coast →")], onB);

        var aHtml = await HtmlAsync(h, $"/g/{a.Id}/{a.Slug}");
        Assert.Equal([("next", $"/g/{b.Id}/{b.Slug}", "Birds →")], Neighbours(aHtml));
        Assert.Contains("aria-label=\"Galleries\"", aHtml);

        Assert.Equal([("prev", $"/g/{b.Id}/{b.Slug}", "← Birds")], Neighbours(await HtmlAsync(h, $"/g/{c.Id}/{c.Slug}")));
        Guards.AssertNoInlineStyle(aHtml);
    }

    // ── (d), (e) [SAFETY] private galleries ──────────────────────────────────

    [Fact]
    public async Task A_private_gallery_links_to_no_gallery_and_no_listed_page_links_to_it()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3);
        var lib = await NavLibrary.BuildAsync(h);
        var p = await h.Galleries.Find(g => g.Id == lib.P).FirstAsync();

        var pHtml = await HtmlAsync(h, $"/g/{p.Id}/{p.Slug}");
        Assert.DoesNotMatch(NeighboursRx(), pHtml);
        Assert.DoesNotContain("gallery-neighbours", pHtml);
        var tiles = p.Items.Select(i => $"/g/{p.Id}/{p.Slug}/{i}").ToHashSet();
        var galleryLinks = HrefRx().Matches(pHtml).Select(m => m.Groups[1].Value).Where(x => x.StartsWith("/g/", StringComparison.Ordinal)).ToList();
        Assert.NotEmpty(galleryLinks);
        Assert.All(galleryLinks, x => Assert.Contains(x, tiles));

        foreach (var id in new[] { lib.A, lib.B, lib.C, lib.D })
        {
            var html = await HtmlAsync(h, $"/g/{id}/{await SlugAsync(h, id)}");
            Assert.NotEmpty(Neighbours(html));
            Assert.DoesNotContain($"/g/{lib.P}", html);
            Assert.DoesNotContain("Papa", html);
        }
    }

    [Fact]
    public async Task A_stale_nav_planted_on_a_private_gallery_renders_no_neighbours()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3);
        var lib = await NavLibrary.BuildAsync(h);
        var p = await h.Galleries.Find(g => g.Id == lib.P).FirstAsync();
        var a = await h.Galleries.Find(g => g.Id == lib.A).FirstAsync();
        var c = await h.Galleries.Find(g => g.Id == lib.C).FirstAsync();

        // The ONLY direct nav write in this file: a stale nav naming A and C on the private gallery.
        static BsonDocument Link(Data.GalleryDoc g) => new() { { "id", g.Id }, { "slug", g.Slug }, { "title", g.Title } };
        var raw = h.Db.GetCollection<BsonDocument>("galleries");
        await raw.UpdateOneAsync(new BsonDocument("_id", lib.P),
            Builders<BsonDocument>.Update.Set("nav", new BsonDocument { { "prev", Link(a) }, { "next", Link(c) } }));
        Assert.NotNull((await h.Galleries.Find(g => g.Id == lib.P).FirstAsync()).Nav?.Prev);

        var html = await HtmlAsync(h, $"/g/{p.Id}/{p.Slug}");
        Assert.DoesNotMatch(NeighboursRx(), html);
        Assert.DoesNotContain("gallery-neighbours", html);
        Assert.DoesNotContain($"/g/{a.Id}", html);
        Assert.DoesNotContain($"/g/{c.Id}", html);
    }

    // ── (f) brand-only header ────────────────────────────────────────────────

    [Fact]
    public async Task Public_header_has_one_link_to_home_on_every_page()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3);
        var lib = await NavLibrary.BuildAsync(h);
        var b = await h.Galleries.Find(g => g.Id == lib.B).FirstAsync();

        var pages = new[]
        {
            await HtmlAsync(h, "/"),
            await HtmlAsync(h, $"/g/{b.Id}/{b.Slug}"),
            await HtmlAsync(h, $"/g/{b.Id}/{b.Slug}/{b.Items[0]}"),
            await HtmlAsync(h, "/g/zzzzzzzzzzzz/none", HttpStatusCode.NotFound),
        };
        foreach (var html in pages)
        {
            var hrefs = HeaderHrefs(html);
            Assert.Equal(hrefs.Count, hrefs.Distinct(StringComparer.Ordinal).Count());
            Assert.Equal(["/"], hrefs);
            Assert.DoesNotContain("aria-label=\"Site\"", html);
        }
    }

    // ── (h) SC-005: zero stale neighbour links across owner actions ──────────

    /// <summary>
    /// Every existing non-empty gallery's page shows exactly the neighbours of the CURRENT listed
    /// home order (<see cref="SiteProjector.Cards"/> then <see cref="GalleryNav.Neighbours"/>); a
    /// gallery that is not listed shows none.
    /// </summary>
    private static async Task AssertNoStaleLinksAsync(LibraryHost h, string step)
    {
        var galleries = await NavLibrary.GalleriesAsync(h);
        var byId = galleries.ToDictionary(g => g.Id, StringComparer.Ordinal);
        var listed = SiteProjector.Cards(await NavLibrary.OrderAsync(h), galleries).Select(c => c.GalleryId).ToList();

        foreach (var g in galleries.Where(g => g.Items.Count > 0))
        {
            var expected = new List<(string, string, string)>();
            if (listed.Contains(g.Id))
            {
                var (prev, next) = GalleryNav.Neighbours(listed, g.Id);
                if (prev is not null) expected.Add(("prev", $"/g/{prev}/{byId[prev].Slug}", $"← {byId[prev].Title}"));
                if (next is not null) expected.Add(("next", $"/g/{next}/{byId[next].Slug}", $"{byId[next].Title} →"));
            }
            var html = await HtmlAsync(h, $"/g/{g.Id}/{g.Slug}");
            var actual = Neighbours(html);
            Assert.True(expected.SequenceEqual(actual),
                $"after {step}: {g.Title} shows [{string.Join(", ", actual)}], expected [{string.Join(", ", expected)}]");
            if (expected.Count == 0)
                Assert.DoesNotContain("gallery-neighbours", html);
        }
    }

    [Fact]
    public async Task Neighbour_links_follow_every_owner_action_with_no_stale_link()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3);
        var lib = await NavLibrary.BuildAsync(h);
        var (a, b, c, d, p) = (lib.A, lib.B, lib.C, lib.D, lib.P);
        await AssertNoStaleLinksAsync(h, "build");

        async Task Step(string name, Func<GalleryService, Task<OperationResult>> call)
        {
            Assert.True((await Gs(h, call)).IsOk, $"{name} failed");
            await h.SettleAsync();
            await AssertNoStaleLinksAsync(h, name);
        }

        // contracts/edge.md "Expected sets": P is placed between B and C so publishing it lands there.
        await Step("reorder home", s => s.ReorderHomeAsync([b, a, c, d, p]));
        await Step("move P between B and C", s => s.ReorderHomeAsync([b, a, p, c, d]));
        await Step("unpublish B", s => s.UnpublishAsync(b));
        await Step("publish P", s => s.PublishAsync(p, confirmed: true));
        await Step("republish B", s => s.PublishAsync(b, confirmed: true));
        await Step("rename B", s => s.UpdateTextAsync(b, "Brook", null));
        await Step("delete C", s => s.DeleteAsync(c));
        var items = (await h.Galleries.Find(g => g.Id == b).FirstAsync()).Items.ToList();
        await Step("remove B's first photo", s => s.RemoveAsync(b, items[0]));
        await Step("remove B's last photo", s => s.RemoveAsync(b, items[1]));

        // Final listed sequence A, P, D: B is empty (404), C is gone.
        var aHtml = await HtmlAsync(h, $"/g/{a}/{await SlugAsync(h, a)}");
        Assert.Equal([("next", $"/g/{p}/{await SlugAsync(h, p)}", "Papa →")], Neighbours(aHtml));
    }

    // ── T031 rename: old slug redirects, new page links with the new slug ────

    [Fact]
    public async Task After_rename_the_old_photo_address_redirects_and_the_gallery_link_uses_the_new_slug()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3);
        var g = await GalleryAsync(h, "Coast", 3);
        await h.SettleAsync();

        Assert.True((await Gs(h, s => s.UpdateTextAsync(g.Id, "Harbour Lights", null))).IsOk);
        await h.SettleAsync();
        var newSlug = await SlugAsync(h, g.Id);
        Assert.NotEqual(g.Slug, newSlug);

        var old = await h.GetAsync($"/g/{g.Id}/{g.Slug}/{g.Items[1]}");
        Assert.Equal(HttpStatusCode.MovedPermanently, old.StatusCode);
        var location = old.Headers.Location!.OriginalString;
        Assert.Equal($"/g/{g.Id}/{newSlug}/{g.Items[1]}", location);

        var html = await HtmlAsync(h, location);
        var link = GalleryLinkRx().Match(html);
        Assert.True(link.Success, "no gallery link");
        Assert.Equal($"/g/{g.Id}/{newSlug}#p-{g.Items[1]}", link.Groups[1].Value);
        Assert.Equal("Harbour Lights", link.Groups[2].Value);
        Assert.Equal(" · 2 / 3", link.Groups[3].Value);
    }
}
