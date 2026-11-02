using System.Net;
using System.Text.RegularExpressions;
using Gallery.Web.Tests.Fixtures;
using MongoDB.Driver;

namespace Gallery.Web.Tests;

/// <summary>
/// 011 T018 (FR-011 to FR-015, data-model.md trail table): the breadcrumb every public page type
/// renders, over HTTP. [SAFETY] II.2: a private gallery's page and photo page link only to "/",
/// "/g" and the photo's own gallery tile.
/// </summary>
[Collection("s3")]
public partial class TrailRoutesTests(S3ProxyFixture s3, MongoFixture mongo) : IClassFixture<MongoFixture>
{
    [GeneratedRegex("<nav class=\"breadcrumb\"[^>]*>(.*?)</nav>", RegexOptions.Singleline)]
    private static partial Regex BreadcrumbRx();

    [GeneratedRegex("<li[^>]*>(?:<a [^>]*href=\"([^\"]*)\"[^>]*>(.*?)</a>|<span[^>]*>(.*?)</span>)</li>", RegexOptions.Singleline)]
    private static partial Regex CrumbRx();

    [GeneratedRegex("<nav class=\"breadcrumb\"[^>]*>.*?</nav>", RegexOptions.Singleline)]
    private static partial Regex BreadcrumbBlockRx();

    private static List<(string? Href, string Label)> Crumbs(string html)
    {
        var nav = BreadcrumbRx().Matches(html);
        Assert.True(nav.Count == 1, $"{nav.Count} breadcrumbs");
        return CrumbRx().Matches(nav[0].Groups[1].Value)
            .Select(m => m.Groups[1].Success
                ? ((string?)WebUtility.HtmlDecode(m.Groups[1].Value), WebUtility.HtmlDecode(m.Groups[2].Value))
                : (null, WebUtility.HtmlDecode(m.Groups[3].Value)))
            .ToList();
    }

    private static async Task<string> HtmlAsync(LibraryHost h, string path, HttpStatusCode status = HttpStatusCode.OK)
    {
        var resp = await h.GetAsync(path);
        Assert.True(resp.StatusCode == status, $"GET {path} answered {(int)resp.StatusCode}, expected {(int)status}");
        return await resp.Content.ReadAsStringAsync();
    }

    [Fact]
    public async Task Every_public_page_type_shows_the_Home_rooted_trail()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3);
        var built = await HomeCaptureLibrary.BuildAsync(h);
        var g = await GalleryHtml.LoadAsync(h, built.Listed[0]);
        var photo = g.Items[1];
        var photoTitle = (await h.Images.Find(i => i.Id == photo).FirstAsync()).Title;

        Assert.Equal([((string?)null, "Home")], Crumbs(await HtmlAsync(h, "/")));
        Assert.Equal([("/", "Home"), (null, "Galleries")], Crumbs(await HtmlAsync(h, "/g")));
        Assert.Equal([("/", "Home"), ("/g", "Galleries"), (null, g.Title)], Crumbs(await HtmlAsync(h, $"/g/{g.Id}/{g.Slug}")));
        Assert.Equal([("/", "Home"), ("/g", "Galleries"), ($"/g/{g.Id}/{g.Slug}#p-{photo}", g.Title), (null, photoTitle)],
            Crumbs(await HtmlAsync(h, $"/g/{g.Id}/{g.Slug}/{photo}")));
        Assert.Equal([("/", "Home"), (null, "Not found")],
            Crumbs(await HtmlAsync(h, "/g/zzzzzzzzzzzzzzzzzzzzzzzzzz/none", HttpStatusCode.NotFound)));
    }

    // [SAFETY] II.2 (FR-014)
    [Fact]
    public async Task A_private_gallery_trail_links_no_other_gallery()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3);
        var built = await HomeCaptureLibrary.BuildAsync(h);
        var p = await GalleryHtml.LoadAsync(h, built.Private);
        Assert.Equal("private", p.Visibility);
        var photo = p.Items[0];

        var failures = new List<string>();
        foreach (var path in new[] { $"/g/{p.Id}/{p.Slug}", $"/g/{p.Id}/{p.Slug}/{photo}" })
        {
            var allowed = new HashSet<string>(["/", "/g", $"/g/{p.Id}/{p.Slug}#p-{photo}"], StringComparer.Ordinal);
            var crumbs = Crumbs(await HtmlAsync(h, path));
            var hrefs = crumbs.Where(c => c.Href is not null).Select(c => c.Href!).ToList();
            if (hrefs.Count < 2) failures.Add($"{path}: only {hrefs.Count} crumb links (vacuous)");
            foreach (var href in hrefs.Where(x => !allowed.Contains(x)))
                failures.Add($"{path}: crumb links {href}");
        }
        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    [Fact]
    public async Task Escape_still_targets_the_gallery_crumb()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3);
        var built = await HomeCaptureLibrary.BuildAsync(h);
        var g = await GalleryHtml.LoadAsync(h, built.Listed[0]);

        var html = await HtmlAsync(h, $"/g/{g.Id}/{g.Slug}/{g.Items[0]}");
        var crumb = BreadcrumbBlockRx().Match(html).Value;
        var targets = Regex.Matches(crumb, "<a [^>]*href=\"[^\"]*#p-[^\"]*\"");
        Assert.Single(targets);
        Assert.Contains($"href=\"/g/{g.Id}/{g.Slug}#p-{g.Items[0]}\"", targets[0].Value);
    }
}
