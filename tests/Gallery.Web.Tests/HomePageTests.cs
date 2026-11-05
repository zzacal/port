using System.Net;
using System.Text.RegularExpressions;
using Gallery.Web.Components.Molecules;
using Gallery.Web.Edge;
using Gallery.Web.Site;
using Gallery.Web.Site.Pages;
using Gallery.Web.Tests.Fixtures;

namespace Gallery.Web.Tests;

/// <summary>
/// 011 T014 (contracts/routes.md, components.md C2/C4/C5, edge.md E2; SC-004, SC-005): the home
/// page at <c>/</c> (links only, no read) and the galleries listing moved to <c>/g</c>. The library
/// is <see cref="HomeCaptureLibrary"/>: three listed galleries, one private and one empty, built
/// through the services. "No X" assertions check the ELEMENT; read counts use equality.
/// </summary>
[Collection("s3")]
public partial class HomePageTests(S3ProxyFixture s3, MongoFixture mongo) : IClassFixture<MongoFixture>
{
    private const string SpeculationRule =
        "{\"prerender\":[{\"where\":{\"or\":[{\"href_matches\":\"/g\"},{\"href_matches\":\"/g/*\"},{\"href_matches\":\"/c\"},{\"href_matches\":\"/c/*\"}]},\"eagerness\":\"moderate\"}]}";

    // Scoped CSS adds a b-xxxx attribute to the layout's elements.
    [GeneratedRegex("<script type=\"speculationrules\"(?: b-[a-z0-9]{10})?>(.*?)</script>", RegexOptions.Singleline)]
    private static partial Regex SpeculationRx();

    [GeneratedRegex("<nav class=\"nav nav--home\"[^>]*>")]
    private static partial Regex HomeNavRx();

    [GeneratedRegex("<a\\b[^>]*>")]
    private static partial Regex AnchorRx();

    [GeneratedRegex("<a href=\"/g\"(?: b-[a-z0-9]{10})?>Galleries</a>")]
    private static partial Regex GalleriesLinkRx();

    [GeneratedRegex("<a href=\"/c\"(?: b-[a-z0-9]{10})?>By Camera</a>")]
    private static partial Regex CamerasLinkRx();

    [GeneratedRegex("<div class=\"cs[ \"]")]
    private static partial Regex ContactSheetRx();

    [GeneratedRegex("<body\\b[^>]*>")]
    private static partial Regex BodyRx();

    [GeneratedRegex("<a class=\"tile\" href=\"([^\"]*)\"")]
    private static partial Regex TileHrefRx();

    [GeneratedRegex("<title[^>]*>(.*?)</title>", RegexOptions.Singleline)]
    private static partial Regex TitleRx();

    // ── helpers ──────────────────────────────────────────────────────────────

    private static async Task<string> RawOkAsync(LibraryHost h, string path)
    {
        var resp = await h.GetAsync(path);
        Assert.True(resp.StatusCode == HttpStatusCode.OK, $"GET {path} answered {(int)resp.StatusCode}");
        return await resp.Content.ReadAsStringAsync();
    }

    /// <summary>
    /// GETs <paramref name="path"/> and returns every log line written for it, through its single
    /// <c>http.request</c> line (which is written last, in the request's finally).
    /// </summary>
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

    private static async Task<string> SlugAsync(LibraryHost h, string gid) => (await GalleryHtml.LoadAsync(h, gid)).Slug;

    // ── (a) the home page ────────────────────────────────────────────────────

    [Fact]
    public async Task Home_has_one_Galleries_link_in_nav_home_and_no_grid()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3);
        await HomeCaptureLibrary.BuildAsync(h);

        var resp = await h.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal(PublicCache.HtmlPolicy, resp.Headers.CacheControl?.ToString());
        Assert.False(resp.Headers.Contains("Link"));
        var html = await resp.Content.ReadAsStringAsync();

        var navs = HomeNavRx().Matches(html);
        Assert.Single(navs);
        var nav = LibraryHtml.Element(html, "nav", navs[0].Index);
        // 013 stated change (test-impact A): exactly two links, Galleries then By Camera.
        var anchors = AnchorRx().Matches(nav);
        Assert.Equal(2, anchors.Count);
        Assert.Single(GalleriesLinkRx().Matches(nav));
        Assert.Single(CamerasLinkRx().Matches(nav));
        Assert.True(GalleriesLinkRx().Match(nav).Index < CamerasLinkRx().Match(nav).Index, "Galleries must come before By Camera");

        Assert.Empty(ContactSheetRx().Matches(html));
        Assert.DoesNotContain("<a class=\"tile\"", html, StringComparison.Ordinal);

        var bodies = BodyRx().Matches(html);
        Assert.Single(bodies);
        Assert.Matches("^<body class=\"public fill\"(?: b-[a-z0-9]{10})?>$", bodies[0].Value);

        var titles = TitleRx().Matches(html);
        Assert.Single(titles);
        Assert.Equal("Gallery", titles[0].Groups[1].Value);
    }

    [Fact]
    public async Task HomePage_component_takes_no_data_and_renders_the_home_links()
    {
        var html = await Render.Html<HomePage>();
        Assert.Single(HomeNavRx().Matches(html));
        Assert.Matches("<body class=\"public fill\"(?: b-[a-z0-9]{10})?>", html);
        Assert.Empty(ContactSheetRx().Matches(html));
        Assert.DoesNotContain(typeof(HomePage).GetProperties(),
            p => p.IsDefined(typeof(Microsoft.AspNetCore.Components.ParameterAttribute), true));
    }

    // ── (b) [SAFETY] IV.9 read budget ────────────────────────────────────────

    [Fact]
    public async Task Home_reads_nothing_and_listing_reads_the_site_once()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3);
        await HomeCaptureLibrary.BuildAsync(h);

        var (homeLines, homeRequest) = await LinesForAsync(h, "/", "/");
        Assert.Equal(0, DbReads(homeRequest));
        Assert.Equal(0, homeLines.Count(l => l.StartsWith("op.timing db.", StringComparison.Ordinal)));

        var (listLines, listRequest) = await LinesForAsync(h, "/g", "/g");
        Assert.Equal(1, DbReads(listRequest));
        Assert.Equal(1, listLines.Count(l => l.StartsWith("op.timing db.site.read ", StringComparison.Ordinal)));
        Assert.Equal(1, listLines.Count(l => l.StartsWith("op.timing db.", StringComparison.Ordinal)));
    }

    // ── (c) [SAFETY] II.2 / II.6 listing ─────────────────────────────────────

    [Fact]
    public async Task Listing_shows_exactly_the_listed_galleries_in_home_order()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3);
        var built = await HomeCaptureLibrary.BuildAsync(h);

        var listed = built.Listed.ToHashSet(StringComparer.Ordinal);
        var expected = new List<string>();
        foreach (var gid in (await GalleryHtml.HomeOrderAsync(h)).Where(listed.Contains))
            expected.Add($"/g/{gid}/{await SlugAsync(h, gid)}");
        Assert.Equal(3, expected.Count);

        var html = await RawOkAsync(h, "/g");
        var tiles = TileHrefRx().Matches(html).Select(m => m.Groups[1].Value).ToList();
        Assert.Equal(expected, tiles);

        foreach (var hidden in new[] { built.Private, built.Empty })
            Assert.DoesNotContain(tiles, t => t.Contains(hidden, StringComparison.Ordinal));
        Assert.DoesNotContain($"href=\"/g/{built.Private}/", html, StringComparison.Ordinal);
        Assert.DoesNotContain($"href=\"/g/{built.Empty}/", html, StringComparison.Ordinal);

        var titles = TitleRx().Matches(html);
        Assert.Single(titles);
        Assert.Equal("Galleries", titles[0].Groups[1].Value);
        Assert.Empty(HomeNavRx().Matches(html));

        // [SAFETY] II.6 at render time (parent, after the T025 non-vacuity run showed the services
        // alone keep the case above green): a stale coverless card planted in homeCards is not listed.
        var empty = await GalleryHtml.LoadAsync(h, built.Empty);
        await h.Site.UpdateOneAsync(MongoDB.Driver.Builders<Gallery.Web.Data.SiteDoc>.Filter.Eq(s => s.Id, "site"),
            MongoDB.Driver.Builders<Gallery.Web.Data.SiteDoc>.Update.Push(s => s.HomeCards,
                new Gallery.Web.Data.GalleryCardDoc { GalleryId = empty.Id, Slug = empty.Slug, Title = empty.Title, ImageCount = 0, Cover = null }));
        Assert.Contains((await h.SiteAsync()).HomeCards, c => c.GalleryId == empty.Id);
        var stale = await RawOkAsync(h, "/g");
        Assert.Equal(expected, TileHrefRx().Matches(stale).Select(m => m.Groups[1].Value).ToList());
        Assert.DoesNotContain($"href=\"/g/{empty.Id}/", stale, StringComparison.Ordinal);
        Assert.DoesNotContain("tile-empty", stale, StringComparison.Ordinal);
    }

    // ── (d) /g/ redirect ─────────────────────────────────────────────────────

    [Fact]
    public async Task Listing_trailing_slash_redirects()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3);
        foreach (var verb in new[] { HttpMethod.Get, HttpMethod.Head })
        {
            var resp = await h.Host.SendAsync(new HttpRequestMessage(verb, "/g/"));
            Assert.Equal(HttpStatusCode.MovedPermanently, resp.StatusCode);
            Assert.Equal("/g", resp.Headers.Location?.OriginalString);
            Assert.Equal(PublicCache.ShortPolicy, resp.Headers.CacheControl?.ToString());
        }
    }

    // ── (e) output cache ─────────────────────────────────────────────────────

    [Fact]
    public async Task Second_GETs_of_home_and_listing_are_served_from_the_output_cache()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3,
            extraOverrides: new() { ["Site:OutputCache"] = "true" });
        await HomeCaptureLibrary.BuildAsync(h);
        var baseline = CountOp(h, "db.site.read");

        await RawOkAsync(h, "/g");
        await RawOkAsync(h, "/g");
        Assert.Equal(baseline + 1, CountOp(h, "db.site.read"));

        await RawOkAsync(h, "/");
        await RawOkAsync(h, "/");
        Assert.Equal(baseline + 1, CountOp(h, "db.site.read"));
    }

    // ── (f) [SAFETY] E2: the home page is never purged ───────────────────────

    private static readonly EdgeBases Bases = new("https://site.test", "https://img.test");

    private static GallerySnapshot G(string id, string slug, string vis, params string[] items) => new(id, slug, vis, items);

    /// <summary>Every PurgeSets method over the PurgeSetTests fixtures.</summary>
    private static IEnumerable<(string Name, IReadOnlyList<string> Paths)> AllSets()
    {
        var pubs = new (GallerySnapshot? B, GallerySnapshot? A)[]
        {
            (G("g1", "s", "public", "a", "b", "c"), G("g1", "s", "public", "a", "b", "c", "e")),
            (G("g1", "s", "public", "a", "b", "c"), G("g1", "s", "public", "a", "c")),
            (G("g1", "s", "public", "a", "b", "c", "d"), G("g1", "s", "public", "b", "a", "c", "d")),
            (new GallerySnapshot("g1", "s", "public", ["a", "b"]), new GallerySnapshot("g1", "s", "public", ["a", "b"], PinnedCover: "b")),
            (G("g1", "s", "public", "a"), G("g1", "s", "public")),
            (G("g1", "s", "private", "a"), G("g1", "s", "private")),
            (G("g1", "s", "private", "a"), G("g1", "s", "private", "a", "b")),
            (G("g1", "s", "public"), G("g1", "s", "public", "a")),
            (G("g1", "s", "private", "a", "b"), G("g1", "s", "public", "a", "b")),
            (G("g1", "s", "public", "a", "b"), G("g1", "s", "private", "a", "b")),
            (G("g1", "old", "public", "a", "b"), G("g1", "new", "public", "a", "b")),
            (G("g1", "old", "private", "a"), G("g1", "new", "private", "a")),
            (null, G("g1", "s", "public", "a")),
            (G("g1", "s", "public", "a"), null),
        };
        foreach (var (b, a) in pubs)
        {
            yield return ("Listing", PurgeSets.Listing(b, a));
            yield return ("Members", PurgeSets.Members(b, a, Bases).PagePaths);
            yield return ("Visibility", PurgeSets.Visibility(b, a, Bases).PagePaths);
            yield return ("Rename", PurgeSets.Rename(b, a, Bases).PagePaths);
            yield return ("Description", PurgeSets.Description(b, a, Bases).PagePaths);
            yield return ("Pages", PurgeSets.Pages(b, a, ["a", "b", "c", "d", "e"]));
            if (b is not null) yield return ("GalleryDelete", PurgeSets.GalleryDelete(b, Bases).PagePaths);
            if (b is not null) yield return ("Delete", PurgeSets.Delete([(b, a)], ["a-320.avif"], Bases).PagePaths);
        }

        GallerySnapshot[] three =
        [
            G("g1", "a", "public", "x"), G("g2", "b", "public", "y"), G("g3", "c", "private", "z"), G("g4", "d", "public"),
        ];
        yield return ("Order", PurgeSets.Order(["g1", "g2", "g3", "g4"], ["g2", "g1", "g3", "g4"], three, Bases).PagePaths);
        yield return ("Order", PurgeSets.Order(["g3", "g1", "g2"], ["g1", "g2", "g3"], three, Bases).PagePaths);
        yield return ("Order", PurgeSets.Order(["g4", "g1", "g2"], ["g1", "g4", "g2"], three, Bases).PagePaths);

        yield return ("Text", PurgeSets.Text("a", [G("g1", "s", "public", "a", "b"), G("g2", "t", "private", "c", "a")], Bases).PagePaths);
        yield return ("Text", PurgeSets.Text("b", [G("g1", "s", "public", "a", "b")], Bases).PagePaths);
        yield return ("Text", PurgeSets.Text("b", [new GallerySnapshot("g1", "s", "public", ["a", "b"], PinnedCover: "b")], Bases).PagePaths);
        yield return ("Text", PurgeSets.Text("a", [G("g2", "t", "private", "a")], Bases).PagePaths);

        yield return ("Delete", PurgeSets.Delete(
            [(G("g1", "s", "public", "a", "b"), G("g1", "s", "public", "b")), (G("g2", "t", "private", "a"), G("g2", "t", "private"))],
            ["a-320.avif"], Bases).PagePaths);

        yield return ("Nav", PurgeSets.Nav([new Gallery.Web.Library.GalleryLinkTarget("g1", "s")], Bases).PagePaths);

        var one = PurgeSets.Members(G("g1", "s", "public", "a"), G("g1", "s", "public", "a", "b"), Bases);
        var two = PurgeSets.Members(G("g1", "s", "public", "a", "b"), G("g1", "s", "public", "a", "b", "c"), Bases);
        yield return ("Union", PurgeSet.Union(Bases, [one, two]).PagePaths);
    }

    [Fact]
    public void No_purge_set_ever_contains_the_home_page()
    {
        Assert.Equal("/g", PurgeSets.ListingPath);
        var sets = AllSets().ToList();
        // Non-vacuity: the fixtures do produce listing purges, so "/" would have shown up before 011.
        Assert.Contains(sets, s => s.Paths.Contains(PurgeSets.ListingPath));
        var offenders = sets.Where(s => s.Paths.Contains("/")).Select(s => s.Name).ToList();
        Assert.Empty(offenders);
    }

    // ── (g) source guard ─────────────────────────────────────────────────────

    [Fact]
    public void Home_handler_takes_no_ContentReader()
    {
        var src = File.ReadAllText(Path.Combine(Render.RepoRoot, "src", "Gallery.Web", "Site", "PublicEndpoints.cs"));
        var maps = Regex.Matches(src, "MapMethods\\(\"/\", verbs, ");
        Assert.Single(maps);
        var start = maps[0].Index + maps[0].Length;
        var arrow = src.IndexOf("=>", start, StringComparison.Ordinal);
        Assert.True(arrow > start, "no lambda on the / route");
        var parameters = src[start..arrow];
        Assert.DoesNotContain("ContentReader", parameters, StringComparison.Ordinal);
        Assert.DoesNotContain("StorageOptions", parameters, StringComparison.Ordinal);
        Assert.Equal("()", parameters.Trim());
    }

    // ── (h) speculation rule ─────────────────────────────────────────────────

    [Fact]
    public async Task Speculation_rule_is_exact_on_every_public_page_type()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3);
        var built = await HomeCaptureLibrary.BuildAsync(h);
        var gid = built.Listed[0];
        var slug = await SlugAsync(h, gid);
        var photo = (await GalleryHtml.LoadAsync(h, gid)).Items[0];

        foreach (var path in new[] { "/", "/g", $"/g/{gid}/{slug}", $"/g/{gid}/{slug}/{photo}" })
        {
            var html = await RawOkAsync(h, path);
            var rules = SpeculationRx().Matches(html);
            Assert.True(rules.Count == 1, $"{path}: {rules.Count} speculation rules");
            Assert.Equal(SpeculationRule, rules[0].Groups[1].Value);
        }
    }

    // ── (i) Nav with Home unset is unchanged ─────────────────────────────────

    [Fact]
    public async Task Nav_default_renders_the_pre_011_bytes()
    {
        var specimen = await File.ReadAllTextAsync(Path.Combine(Render.RepoRoot,
            "specs", "011-home-page", "evidence", "pre-011-specimens", "006", "molecules", "nav.html"));
        specimen = Guards.StripStyle(specimen);
        var section = specimen.IndexOf("<section class=\"specimen\"", StringComparison.Ordinal);
        Assert.True(section >= 0);
        var navAt = specimen.IndexOf("<nav class=\"nav\"", section, StringComparison.Ordinal);
        Assert.True(navAt >= 0);
        var expected = LibraryHtml.Element(specimen, "nav", navAt);

        var html = Guards.StripStyle(await Render.Html<Nav>(new
        {
            Items = (IReadOnlyList<NavItem>)[new("Galleries", "/", Current: true)],
        }));
        Assert.Equal(expected, html);
    }

    // ── (j) SC-004 listing unchanged ─────────────────────────────────────────

    [Fact]
    public async Task Listing_region_is_byte_equal_to_the_pre_011_home()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3);
        var built = await HomeCaptureLibrary.BuildAsync(h);
        var raw = await RawOkAsync(h, "/g");
        var expected = await File.ReadAllTextAsync(Path.Combine(Render.RepoRoot,
            "specs", "011-home-page", "evidence", "pre-011-listing.html"));
        Assert.Equal(expected, HomeCaptureLibrary.ListingRegion(raw, built));
    }
}
