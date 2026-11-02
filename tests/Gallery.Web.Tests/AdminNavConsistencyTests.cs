using System.Text.RegularExpressions;
using Gallery.Web.Tests.Fixtures;

namespace Gallery.Web.Tests;

/// <summary>
/// 010 owner review 1 (contracts/owner-review-1.md B6; decisions rows 42-45). The owner bar is the
/// same on every admin page (only <c>aria-current</c> differs) and holds no page action; a page's
/// create action sits in its heading row beside the title; the builder has a breadcrumb back to the
/// gallery list and no "Back to galleries" link; section roots show their own name (constitution IX).
/// </summary>
[Collection("s3")]
public class AdminNavConsistencyTests(S3ProxyFixture s3, MongoFixture mongo) : IClassFixture<MongoFixture>
{
    private static string Bar(string html)
    {
        var bars = Regex.Matches(html, "<header class=\"owner-bar\".*?</header>", RegexOptions.Singleline);
        Assert.Single(bars);
        return bars[0].Value;
    }

    /// <summary>The bar with scoped-CSS attributes, the CSRF value and aria-current removed.</summary>
    private static string Normalised(string bar) =>
        Regex.Replace(Regex.Replace(Regex.Replace(bar, " b-[a-z0-9]{10}", ""), "value=\"[^\"]*\"", "value=\"\""), " aria-current=\"page\"", "");

    [Fact]
    public async Task Every_admin_page_has_the_same_owner_bar_with_no_page_action()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3);
        var ids = await GalleryHtml.ImageIdsAsync(h, 2);
        var gid = await GalleryHtml.GalleryAsync(h, "Coast", ids, publish: true);
        var empty = await GalleryHtml.GalleryAsync(h, "Empty", []);
        await h.SettleAsync();

        var pages = new Dictionary<string, string>
        {
            ["library"] = await GalleryHtml.GetOkAsync(h, "/admin/library"),
            ["galleries"] = await GalleryHtml.GetOkAsync(h, "/admin/galleries"),
            ["builder"] = await GalleryHtml.GetOkAsync(h, $"/admin/galleries/{gid}"),
            ["empty builder"] = await GalleryHtml.GetOkAsync(h, $"/admin/galleries/{empty}"),
            ["upload"] = await GalleryHtml.GetOkAsync(h, "/admin/upload"),
        };

        var reference = Normalised(Bar(pages["library"]));
        foreach (var (name, html) in pages)
        {
            var bar = Bar(html);
            Assert.True(Normalised(bar) == reference, $"{name}'s owner bar differs:\n{Normalised(bar)}\nvs library:\n{reference}");
            Assert.DoesNotContain("btn-primary", bar);
            Assert.DoesNotContain("New gallery", bar);
            Assert.Contains(">View site ↗</a>", System.Net.WebUtility.HtmlDecode(bar));
            Assert.Contains("href=\"/\"", bar);
        }
        Assert.Matches("<a href=\"/admin/library\" aria-current=\"page\"[^>]*>Library</a>", pages["library"]);
        Assert.Matches("<a href=\"/admin/galleries\" aria-current=\"page\"[^>]*>Galleries</a>", pages["builder"]);
    }

    [Fact]
    public async Task New_gallery_sits_in_the_page_heading_beside_the_title()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3);
        var html = await GalleryHtml.GetOkAsync(h, "/admin/galleries");
        var heading = Regex.Match(html, "<div class=\"page-heading\".*?</h1>(.*?)</div></div>", RegexOptions.Singleline);
        Assert.True(heading.Success, "no page heading with actions");
        Assert.Matches("<h1[^>]*>Galleries</h1>", heading.Value);
        Assert.Matches("<button class=\"btn btn-primary\"[^>]*name=\"action\"[^>]*value=\"new-gallery\"[^>]*>\\s*New gallery\\s*</button>", heading.Groups[1].Value);
        Assert.Single(Regex.Matches(html, "<h1[ >]"));

        var lib = await GalleryHtml.GetOkAsync(h, "/admin/library");
        Assert.Matches("<div class=\"page-heading\"[^>]*><h1[^>]*>Library</h1></div>", lib);
        Assert.Single(Regex.Matches(lib, "<h1[ >]"));
    }

    [Fact]
    public async Task The_builder_has_a_breadcrumb_back_to_the_list_and_roots_name_themselves()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3);
        var ids = await GalleryHtml.ImageIdsAsync(h, 1);
        var gid = await GalleryHtml.GalleryAsync(h, "Coast", ids);
        var empty = await GalleryHtml.GalleryAsync(h, "Empty", []);
        await h.SettleAsync();

        foreach (var (id, title) in new[] { (gid, "Coast"), (empty, "Empty") })
        {
            var html = await GalleryHtml.GetOkAsync(h, $"/admin/galleries/{id}");
            var crumb = Regex.Match(html, "<nav class=\"breadcrumb\".*?</nav>", RegexOptions.Singleline).Value;
            Assert.Matches("<li[^>]*><a class=\"text-link tap\" href=\"/admin/galleries\"[^>]*>Galleries</a></li>", crumb);
            Assert.Matches($"<li aria-current=\"page\"[^>]*><span[^>]*>{Regex.Escape(title)}</span></li>", crumb);
            Assert.True(html.IndexOf("</header>", StringComparison.Ordinal) < html.IndexOf("<nav class=\"breadcrumb\"", StringComparison.Ordinal));
            Assert.DoesNotContain("Back to galleries", html);
        }
        // The empty gallery has no public page, so no View link in the panel either.
        Assert.DoesNotContain("b-view", await GalleryHtml.GetOkAsync(h, $"/admin/galleries/{empty}"));
        Assert.Contains("View gallery", await GalleryHtml.GetOkAsync(h, $"/admin/galleries/{gid}"));

        // Constitution IX: the layout draws the breadcrumb row on every page with the owner bar, so
        // content starts at one height; a section root's trail is just its own name, not a link.
        foreach (var (root, name) in new[] { ("/admin/library", "Library"), ("/admin/galleries", "Galleries"), ("/admin/upload", "Upload"), ("/admin", "Gallery admin") })
        {
            var crumb = Regex.Match(await GalleryHtml.GetOkAsync(h, root), "<nav class=\"breadcrumb\".*?</nav>", RegexOptions.Singleline).Value;
            Assert.DoesNotContain("<a ", crumb);
            Assert.Matches($"<li aria-current=\"page\"[^>]*><span[^>]*>{Regex.Escape(name)}</span></li>", crumb);
        }
    }
}
