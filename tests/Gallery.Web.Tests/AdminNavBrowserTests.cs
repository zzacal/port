using Gallery.Web.Tests.Fixtures;
using Microsoft.Playwright;

namespace Gallery.Web.Tests;

/// <summary>
/// 010 owner review 1 in a real browser (contracts/owner-review-1.md B4, B6): the admin library's
/// Pager shows Previous and Next as whole words at every width, the page heading's New gallery opens
/// its dialog, and the builder's breadcrumb sits centred under the owner bar on one line. Measured,
/// because truncation and centring are rendered geometry no markup assertion can see.
/// </summary>
[Collection("library-browser")]
[Trait("Category", "Browser")]
public sealed class AdminNavBrowserTests(LibraryBrowserFixture fx)
{
    private static readonly int[] Widths = [320, 390, 600, 1024, 1440];

    [Fact]
    public async Task Library_pager_shows_previous_and_next_as_whole_words_at_every_width()
    {
        await using var site = await fx.NewSiteAsync(new() { ["Library:PageSize"] = "2" });
        await site.ImagesAsync(5);
        var failures = new List<string>();
        foreach (var width in Widths)
        {
            var page = await site.OpenAsync("/admin/library?page=2", width);
            var words = await page.EvaluateAsync<string[]>("""
                [...document.querySelectorAll('section.lib .pager a')]
                  .map(a => a.textContent + (a.scrollWidth > a.clientWidth + 0.5 ? ' (truncated)' : '') + ' ' + Math.round(a.getBoundingClientRect().width) + 'x' + Math.round(a.getBoundingClientRect().height))
                """);
            if (words.Length != 2 || !words[0].StartsWith("Previous ") || !words[1].StartsWith("Next ") || words.Any(w => w.Contains("(truncated)")))
                failures.Add($"{width}px: [{string.Join(", ", words)}]");
            await page.Context.CloseAsync();
        }
        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    [Fact]
    public async Task New_gallery_in_the_page_heading_opens_the_dialog()
    {
        await using var site = await fx.NewSiteAsync();
        var page = await site.OpenAsync("/admin/galleries");
        await page.Locator(".page-heading button[value='new-gallery']").ClickAsync();
        var dialog = page.Locator("dialog#gal-new");
        await dialog.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 5_000 });
        Assert.True(await dialog.EvaluateAsync<bool>("d => d.open && d.matches(':modal')"));
        Assert.Equal(0, await page.Locator(".owner-bar button[value='new-gallery']").CountAsync());
    }

    [Fact]
    public async Task Builder_breadcrumb_sits_centred_under_the_owner_bar_on_one_line()
    {
        await using var site = await fx.NewSiteAsync();
        var ids = await site.ImagesAsync(1);
        var gid = await site.GalleryAsync("Birds of the northern estuary, photographed through three long winters", ids);
        var failures = new List<string>();
        foreach (var width in Widths)
        {
            var page = await site.OpenAsync($"/admin/galleries/{gid}", width);
            var m = await page.EvaluateAsync<double[]>("""
                (() => {
                  const bar = document.querySelector('header.owner-bar').getBoundingClientRect();
                  const nav = document.querySelector('nav.breadcrumb'), ol = nav.querySelector('ol').getBoundingClientRect();
                  const tops = new Set([...nav.querySelectorAll('li')].map(li => { const r = li.getBoundingClientRect(); return Math.round((r.top + r.bottom) / 2); }));
                  const link = nav.querySelector('a').getBoundingClientRect();
                  return [bar.bottom, nav.getBoundingClientRect().top, ol.left, ol.right, document.documentElement.clientWidth,
                          tops.size, link.width, link.height, document.documentElement.scrollWidth];
                })()
                """);
            var where = $"{width}px";
            if (m[1] < m[0] - 0.5) failures.Add($"{where}: breadcrumb top {m[1]:0.#} above the bar bottom {m[0]:0.#}");
            if (Math.Abs((m[2] + m[3]) / 2 - m[4] / 2) > 2) failures.Add($"{where}: off centre ({m[2]:0.#}-{m[3]:0.#} in {m[4]})");
            if (m[5] != 1) failures.Add($"{where}: wraps onto {m[5]} lines");
            if (m[6] < 44 || m[7] < 44) failures.Add($"{where}: Galleries crumb is {m[6]:0.#}x{m[7]:0.#}");
            if (m[8] > m[4] + 0.5) failures.Add($"{where}: horizontal scroll {m[8]} > {m[4]}");
            await page.Context.CloseAsync();
        }
        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }
}
