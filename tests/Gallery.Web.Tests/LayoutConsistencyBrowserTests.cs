using Gallery.Web.Tests.Fixtures;
using Microsoft.Playwright;

namespace Gallery.Web.Tests;

/// <summary>
/// Constitution IX.4: every page type of a site draws the same chrome at the same place. Renders each
/// public page type (home, gallery, photo, 404) and each admin page type (upload, library, gallery
/// list, builder) at narrow and wide widths and compares the header/bar box, the breadcrumb row, the
/// first crumb's text position, and the gap between the chrome and the content's first box.
/// </summary>
public static class ChromeProbe
{
    // [header top, header bottom, crumb row top, crumb row height, first crumb text left, first crumb
    //  text top, gap from the chrome to main's first box]
    public const string Script = """
        (() => {
          const crumb = document.querySelector('nav.breadcrumb'), cr = crumb.getBoundingClientRect();
          const chrome = document.querySelector('header.site-header, header.owner-bar').getBoundingClientRect();
          const li = crumb.querySelector('li');
          const w = document.createTreeWalker(li, NodeFilter.SHOW_TEXT, { acceptNode: n => n.textContent.trim() ? 1 : 3 });
          const r = document.createRange(); r.selectNodeContents(w.nextNode()); const t = r.getBoundingClientRect();
          const main = document.querySelector('main'), first = main.firstElementChild ?? main;
          const bottom = Math.max(chrome.bottom, cr.bottom);
          return [chrome.top, chrome.bottom, cr.top, cr.height, t.left, t.top, first.getBoundingClientRect().top - bottom];
        })()
        """;

    public static readonly string[] Names = ["header top", "header bottom", "crumb top", "crumb height", "crumb text left", "crumb text top", "gap to content"];

    /// <summary>Compares every page's geometry to the first page's, to the half pixel.</summary>
    public static void Compare(int width, IReadOnlyList<(string Page, double[] M)> rows, List<string> failures)
    {
        var (basePage, b) = rows[0];
        foreach (var (page, m) in rows.Skip(1))
            for (var k = 0; k < b.Length; k++)
                if (Math.Abs(m[k] - b[k]) > 0.5)
                    failures.Add($"{width}px {Names[k]}: {page} {m[k]:0.#} vs {basePage} {b[k]:0.#}");
    }
}

[Collection("nav-browser")]
[Trait("Category", "Browser")]
public sealed class PublicLayoutConsistencyBrowserTests(NavBrowserFixture site)
{
    [Fact]
    public async Task Every_public_page_type_draws_the_same_chrome()
    {
        var pages = new (string Name, string Path)[]
        {
            ("home", "/"),
            ("gallery", await site.GalleryPathAsync(NavBrowserFixture.SeptemberId)),
            ("photo", await site.PhotoPathAsync(4)),
            ("404", "/g/zzzzzzzzzzzzzzzzzzzzzzzzzz/none"),
        };
        var failures = new List<string>();
        foreach (var width in new[] { 320, 390, 600, 1024, 1440 })
        {
            var ctx = await site.NewContextAsync(width, 900);
            await using var _ = ctx;
            var page = await ctx.NewPageAsync();
            var rows = new List<(string, double[])>();
            foreach (var (name, path) in pages)
            {
                await page.GotoAsync(site.BaseUrl + path, new() { WaitUntil = WaitUntilState.Load });
                rows.Add((name, await page.EvaluateAsync<double[]>(ChromeProbe.Script)));
            }
            ChromeProbe.Compare(width, rows, failures);
        }
        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }
}

[Collection("library-browser")]
[Trait("Category", "Browser")]
public sealed class AdminLayoutConsistencyBrowserTests(LibraryBrowserFixture fx)
{
    [Fact]
    public async Task Every_admin_page_type_draws_the_same_chrome()
    {
        await using var site = await fx.NewSiteAsync();
        var ids = await site.ImagesAsync(2);
        var gid = await site.GalleryAsync("Coast", ids);
        var pages = new[] { ("upload", "/admin/upload"), ("library", "/admin/library"), ("galleries", "/admin/galleries"), ("builder", $"/admin/galleries/{gid}") };
        var failures = new List<string>();
        foreach (var width in new[] { 390, 1440 })
        {
            var rows = new List<(string, double[])>();
            foreach (var (name, path) in pages)
            {
                var page = await site.OpenAsync(path, width);
                rows.Add((name, await page.EvaluateAsync<double[]>(ChromeProbe.Script)));
                await page.Context.CloseAsync();
            }
            ChromeProbe.Compare(width, rows, failures);
        }
        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }
}
