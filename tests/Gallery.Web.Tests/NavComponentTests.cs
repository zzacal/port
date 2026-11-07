using System.Net;
using System.Text.RegularExpressions;
using Gallery.Web.Components.Atoms;
using Gallery.Web.Components.Molecules;
using Gallery.Web.Components.Organisms;
using Gallery.Web.Site.Pages;
using Gallery.Core.Images;
using Microsoft.AspNetCore.Components;

namespace Gallery.Web.Tests;

/// <summary>
/// Feature 010 components (contracts/components.md K1–K6, K8): exact markup and copy, the
/// unchanged output with every new parameter unset, and the one tap rule (T021).
/// </summary>
public partial class NavComponentTests
{
    // The 006 specimens as they were BEFORE 010 (frozen copies; T025 regenerates the live 006 baseline).
    private static readonly string UiBaseline = Path.Combine(
        Render.RepoRoot, "specs", "010-navigation-improvements", "evidence", "pre-010-specimens");

    private static RenderFragment Frag(string text) => b => b.AddContent(0, text);

    private static RenderFragment Markup(string html) => b => b.AddMarkupContent(0, html);

    /// <summary>Drops scoped-CSS attributes and decodes entities, so copy and markup compare plainly.</summary>
    private static string Bare(string html) => WebUtility.HtmlDecode(ScopeAttr().Replace(html, ""));

    [GeneratedRegex(@" b-[a-z0-9]{10}")]
    private static partial Regex ScopeAttr();

    private static List<string> Blocks(string html, string pattern)
        => Regex.Matches(html, pattern, RegexOptions.Singleline).Select(m => m.Value).ToList();

    private static ImageView MakeImage(string id = "img001") => new(
        Id: id,
        Title: "Test Image",
        Width: 1920,
        Height: 1080,
        Variants: [new Variant(640, $"https://img.test/{id}.avif", $"https://img.test/{id}.webp")],
        PlaceholderDataUri: "data:image/webp;base64,AAAA",
        DominantColor: "#aabbcc",
        Exif: null
    );

    // ── K1 TextLink ──────────────────────────────────────────────────────────

    [Fact]
    public async Task TextLink_without_tap_renders_class_text_link_exactly()
    {
        var html = await Render.Html<TextLink>(new { Href = "/x", ChildContent = Frag("Edit") });
        Assert.Equal("<a class=\"text-link\" href=\"/x\">Edit</a>", Bare(html));
    }

    [Fact]
    public async Task TextLink_with_tap_renders_class_text_link_tap()
    {
        var html = await Render.Html<TextLink>(new { Href = "/x", ChildContent = Frag("Next"), Rel = "next", Tap = true });
        Assert.Equal("<a class=\"text-link tap\" href=\"/x\" rel=\"next\">Next</a>", Bare(html));
    }

    [Fact]
    public void Tap_rule_lives_only_in_TextLink_css()
    {
        var src = Path.Combine(Render.RepoRoot, "src");
        var css = File.ReadAllText(Path.Combine(src, "Gallery.Web", "Components", "Atoms", "TextLink.razor.css"));
        var rule = Regex.Match(css, @"\.text-link\.tap\s*\{([^}]*)\}");
        Assert.True(rule.Success, "the .text-link.tap rule is missing from TextLink.razor.css");
        // Constitution IX.3 / decisions row 57: the size is the one Tokens.Tap value (--tap, 44 px).
        Assert.Contains("min-height: var(--tap);", rule.Groups[1].Value);
        Assert.Contains("min-width: var(--tap);", rule.Groups[1].Value);
        Assert.Contains("line-height: var(--tap);", rule.Groups[1].Value);
        Assert.Contains("display: inline-block;", rule.Groups[1].Value);
        Assert.Contains("text-overflow: ellipsis;", rule.Groups[1].Value);

        var violations = new List<string>();
        var files = Directory.EnumerateFiles(src, "*.razor.css", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && Path.GetFileName(f) != "TextLink.razor.css");
        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            if (text.Contains("44px")) violations.Add($"{file}: a literal 44px (use var(--tap))");
            if (text.Contains("line-height: var(--tap)")) violations.Add($"{file}: line-height: var(--tap)");
            foreach (Match m in Regex.Matches(text, @"([^{}]*)\{([^{}]*)\}"))
            {
                if (!m.Groups[1].Value.Contains(".text-link")) continue;
                var body = m.Groups[2].Value;
                if (Regex.IsMatch(body, @"(^|[\s;])min-height\s*:")) violations.Add($"{file}: min-height on .text-link");
                if (Regex.IsMatch(body, @"(^|[\s;])display\s*:")) violations.Add($"{file}: display on .text-link");
            }
        }
        Assert.True(violations.Count == 0, string.Join("\n", violations));
    }

    // ── K2 Pager ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Pager_without_here_or_stack_differs_from_the_pre_010_specimens_only_by_tap()
    {
        var baseline = Blocks(await File.ReadAllTextAsync(Path.Combine(UiBaseline, "molecules", "pager.html")),
            "<nav class=\"pager\".*?</nav>");
        Assert.Equal(3, baseline.Count);

        var first = await Render.Html<Pager>(new { NextHref = "#" });
        var middle = await Render.Html<Pager>(new { PrevHref = "#", NextHref = "#" });
        var last = await Render.Html<Pager>(new { PrevHref = "#" });

        Assert.Equal(baseline, new[] { first, middle, last }.Select(h => h.Replace(" tap", "")).ToList());
        Assert.Equal(1, Guards.Count(first, "class=\"text-link tap\""));
        Assert.Equal(2, Guards.Count(middle, "class=\"text-link tap\""));
        Assert.Equal(1, Guards.Count(last, "class=\"text-link tap\""));
    }

    [Fact]
    public async Task Pager_first_photo_renders_no_previous_link()
    {
        var html = await Render.Html<Pager>(new { NextHref = "/n" });
        Assert.DoesNotContain("rel=\"prev\"", html);
        Assert.DoesNotContain("Previous", html);
        Assert.Contains("<span class=\"prev\"></span>", Bare(html));
    }

    [Fact]
    public async Task Pager_last_photo_renders_no_next_link()
    {
        var html = await Render.Html<Pager>(new { PrevHref = "/p" });
        Assert.DoesNotContain("rel=\"next\"", html);
        Assert.DoesNotContain(">Next<", html);
        Assert.Contains("<span class=\"next\"></span>", Bare(html));
    }

    [Fact]
    public async Task Pager_one_photo_renders_no_links_at_all()
    {
        var html = await Render.Html<Pager>(new { Here = Markup("<b>here</b>") });
        Assert.DoesNotContain("<a ", html);
        Assert.Equal(
            "<nav class=\"pager\" aria-label=\"Image navigation\"><span class=\"prev\"></span><div class=\"here\"><b>here</b></div>\n    <span class=\"next\"></span></nav>",
            Bare(html));
    }

    [Fact]
    public async Task Pager_with_here_renders_the_here_div_between_the_spans()
    {
        var html = await Render.Html<Pager>(new { PrevHref = "/p", NextHref = "/n", Here = Markup("<i>centre</i>") });
        Assert.Equal(
            "<nav class=\"pager\" aria-label=\"Image navigation\"><span class=\"prev\"><a class=\"text-link tap\" href=\"/p\" rel=\"prev\">Previous</a></span><div class=\"here\"><i>centre</i></div>\n" +
            "    <span class=\"next\"><a class=\"text-link tap\" href=\"/n\" rel=\"next\">Next</a></span></nav>",
            Bare(html));
    }

    [Fact]
    public void Pager_css_keeps_both_words_whole()
    {
        // Owner review 1 (B4): side tracks are at least max-content, so a centre item can never
        // squeeze "Previous" into the tap rule's ellipsis; no stacking rules any more.
        var css = File.ReadAllText(Path.Combine(Render.RepoRoot, "src", "Gallery.Web", "Components", "Molecules", "Pager.razor.css"));
        Assert.Contains("grid-template-columns: minmax(max-content, 1fr) auto minmax(max-content, 1fr);", css);
        Assert.DoesNotContain("minmax(44px", css);
        Assert.DoesNotContain("pager-stack", css);
        Assert.DoesNotContain("min-height", css);
        Assert.DoesNotContain("line-height", css);
    }

    // ── B3 PhotoCount ────────────────────────────────────────────────────────

    [Fact]
    public async Task PhotoCount_renders_index_slash_total()
    {
        var html = await Render.Html<PhotoCount>(new { Index = 3, Total = 23 });
        Assert.Equal("<p class=\"photo-count\">3 / 23</p>", Bare(html));
        Assert.DoesNotContain("<a ", html);
    }

    // ── B1 Breadcrumb ────────────────────────────────────────────────────────

    [Fact]
    public async Task Breadcrumb_links_ancestors_and_marks_the_current_page()
    {
        var html = await Render.Html<Breadcrumb>(new { Items = (IReadOnlyList<Crumb>)[new("Galleries", "/"), new("Coast", "/g/a/coast#p-i"), new("Dusk")] });
        Assert.Equal(
            "<nav class=\"breadcrumb\" aria-label=\"Breadcrumb\"><ol><li><a class=\"text-link tap\" href=\"/\">Galleries</a></li>" +
            "<li><a class=\"text-link tap\" href=\"/g/a/coast#p-i\">Coast</a></li><li aria-current=\"page\"><span>Dusk</span></li></ol></nav>",
            Regex.Replace(Bare(html), @">\s+<", "><"));
        Assert.Equal(2, Guards.Count(html, "<a "));
        Guards.AssertNoInlineStyle(html);
    }

    [Fact]
    public async Task Breadcrumb_with_no_items_renders_nothing()
    {
        Assert.Equal("", (await Render.Html<Breadcrumb>(new { Items = (IReadOnlyList<Crumb>)[] })).Trim());
    }

    [Fact]
    public async Task Breadcrumb_encodes_labels()
    {
        var html = await Render.Html<Breadcrumb>(new { Items = (IReadOnlyList<Crumb>)[new("Galleries", "/"), new("<b>&")] });
        Assert.DoesNotContain("<b>", html);
        Assert.Contains("&lt;b&gt;&amp;", html);
    }

    // ── B6 PageHeading ───────────────────────────────────────────────────────

    [Fact]
    public async Task PageHeading_renders_title_and_actions_only_when_given()
    {
        var bare = Bare(await Render.Html<PageHeading>(new { Text = "Library" }));
        Assert.Equal("<div class=\"page-heading\"><h1>Library</h1></div>", bare);
        var with = Bare(await Render.Html<PageHeading>(new { Text = "Galleries", Actions = Markup("<b>x</b>") }));
        Assert.Equal("<div class=\"page-heading\"><h1>Galleries</h1><div class=\"page-actions\"><b>x</b></div></div>", with);
    }

    // ── K4 SoloViewer ────────────────────────────────────────────────────────

    [Fact]
    public async Task SoloViewer_without_here_or_stack_differs_from_the_pre_010_specimens_only_by_tap()
    {
        var baseline = Blocks(await File.ReadAllTextAsync(Path.Combine(UiBaseline, "organisms", "solo-viewer.html")),
            "<article class=\"solo\".*?</article>");
        Assert.Equal(2, baseline.Count);

        var landscape = await Render.Html<SoloViewer>(new { Image = Render.Samples.Landscape[0], PrevHref = "#", NextHref = "#" });
        var portrait = await Render.Html<SoloViewer>(new { Image = Render.Samples.PartialExifPortrait, PrevHref = "#" });

        Assert.Equal(baseline, new[] { landscape, portrait }.Select(h => h.Replace(" tap", "")).ToList());
        Assert.Equal(2, Guards.Count(landscape, "class=\"text-link tap\""));
    }

    [Fact]
    public async Task SoloViewer_passes_here_through_to_the_pager()
    {
        var html = await Render.Html<SoloViewer>(new
        {
            Image = MakeImage(),
            PrevHref = "/p",
            NextHref = "/n",
            Here = Markup("<i>centre</i>"),
        });
        var bare = Bare(html);
        Assert.Contains("<nav class=\"pager\" aria-label=\"Image navigation\">", bare);
        Assert.Contains("</span><div class=\"here\"><i>centre</i></div>", bare);
        Guards.AssertNoInlineStyle(html);
    }

    [Fact]
    public async Task PhotoPage_without_gallery_href_renders_the_home_trail_and_no_count_and_does_not_throw()
    {
        var html = await Render.Html<PhotoPage>(new { Image = MakeImage(), GalleryTitle = "G", NoIndex = false, PrevHref = "/p" });
        // Owner review 2 (R2): the header is always a trail; with no gallery link it is just home.
        var crumb = Regex.Match(Bare(html), "<nav class=\"breadcrumb\".*?</nav>", RegexOptions.Singleline).Value;
        Assert.Equal(1, Guards.Count(crumb, "<li"));
        Assert.Contains("href=\"/\"", crumb);
        Assert.DoesNotContain("class=\"photo-count\"", html);
        Assert.DoesNotContain("class=\"here\"", html);
        Assert.Contains("<nav class=\"pager\"", html);
        Guards.AssertNoInlineStyle(html);
    }

    // ── K5 GalleryNeighbours ─────────────────────────────────────────────────

    [Fact]
    public async Task GalleryNeighbours_with_both_null_renders_nothing()
    {
        var html = await Render.Html<GalleryNeighbours>(new { Prev = (GalleryNeighbour?)null, Next = (GalleryNeighbour?)null });
        Assert.Equal("", html);
    }

    [Fact]
    public async Task GalleryNeighbours_with_both_renders_arrows_titles_and_rel()
    {
        var html = await Render.Html<GalleryNeighbours>(new
        {
            Prev = new GalleryNeighbour("/g/a/alpine", "alpine"),
            Next = new GalleryNeighbour("/g/b/birds", "Birds"),
        });
        Assert.Equal(
            "<nav class=\"gallery-neighbours\" aria-label=\"Galleries\"><span class=\"prev\"><a class=\"text-link tap\" href=\"/g/a/alpine\" rel=\"prev\">← alpine</a></span>\n" +
            "    <span class=\"next\"><a class=\"text-link tap\" href=\"/g/b/birds\" rel=\"next\">Birds →</a></span></nav>",
            Bare(html));
        Guards.AssertNoInlineStyle(html);
    }

    [Fact]
    public async Task GalleryNeighbours_first_gallery_renders_an_empty_prev_span()
    {
        var html = await Render.Html<GalleryNeighbours>(new { Next = new GalleryNeighbour("/g/b/birds", "Birds") });
        var bare = Bare(html);
        Assert.Contains("<span class=\"prev\"></span>", bare);
        Assert.DoesNotContain("rel=\"prev\"", bare);
        Assert.DoesNotContain("←", bare);
        Assert.Contains("<a class=\"text-link tap\" href=\"/g/b/birds\" rel=\"next\">Birds →</a>", bare);
    }

    [Fact]
    public async Task GalleryNeighbours_last_gallery_renders_an_empty_next_span()
    {
        var html = await Render.Html<GalleryNeighbours>(new { Prev = new GalleryNeighbour("/g/a/alpine", "alpine") });
        var bare = Bare(html);
        Assert.Contains("<span class=\"next\"></span>", bare);
        Assert.DoesNotContain("rel=\"next\"", bare);
        Assert.DoesNotContain("→", bare);
        Assert.Contains("<a class=\"text-link tap\" href=\"/g/a/alpine\" rel=\"prev\">← alpine</a>", bare);
    }

    // 015 A12 (requirement change): the K8 MembershipStatus facts were deleted with the component;
    // PhotoActionsTests covers its replacement.

    // ── K6 SiteHeader ────────────────────────────────────────────────────────

    // Owner review 2 (R1): the header holds only the breadcrumb.
    [Fact]
    public async Task SiteHeader_is_the_breadcrumb_and_nothing_else()
    {
        var html = Bare(await Render.Html<SiteHeader>(new { Crumbs = (IReadOnlyList<Crumb>)[new("Galleries", "/"), new("Coast")] }));
        Assert.Matches("^<header class=\"site-header\"><nav class=\"breadcrumb\" aria-label=\"Breadcrumb\"><ol>.*</ol></nav></header>$", html);
        Assert.DoesNotContain("brand", html);
        Assert.Equal(1, Guards.Count(html, "<a "));
    }

    [Fact]
    public async Task SiteHeader_on_home_has_no_link_to_itself()
    {
        var html = await Render.Html<SiteHeader>(new { Crumbs = (IReadOnlyList<Crumb>)[new("Galleries")] });
        Assert.DoesNotContain("<a ", html);
        Assert.Contains("aria-current=\"page\"", html);
    }

    // Owner review 2 (R3): "/" separator, left-aligned.
    [Fact]
    public void Breadcrumb_css_uses_a_slash_and_aligns_left()
    {
        var css = File.ReadAllText(Path.Combine(Render.RepoRoot, "src", "Gallery.Web", "Components", "Molecules", "Breadcrumb.razor.css"));
        Assert.Contains("li + li::before { content: \"/\";", css);
        Assert.Contains("justify-content: flex-start;", css);
        Assert.DoesNotContain("›", css);
    }

    // ── UI-4: no key hints ───────────────────────────────────────────────────

    [Fact]
    public async Task No_component_mentions_keys()
    {
        var htmls = new[]
        {
            await Render.Html<Pager>(new { PrevHref = "/p", NextHref = "/n", Here = Markup("x") }),
            await Render.Html<PhotoCount>(new { Index = 1, Total = 2 }),
            await Render.Html<Breadcrumb>(new { Items = (IReadOnlyList<Crumb>)[new("Galleries", "/"), new("T", "/g#p-1"), new("P")] }),
            await Render.Html<GalleryNeighbours>(new { Prev = new GalleryNeighbour("/a", "A"), Next = new GalleryNeighbour("/b", "B") }),
        };
        foreach (var html in htmls)
        {
            Assert.DoesNotContain("aria-keyshortcuts", html);
            Assert.DoesNotContain("title=", html);
            Assert.DoesNotMatch("(?i)arrow|escape|\\bkey", html);
        }
    }
}
