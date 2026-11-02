using Gallery.Web.Tests.Fixtures;
using Microsoft.Playwright;

namespace Gallery.Web.Tests;

/// <summary>
/// T038: what only a browser can show, run with `dotnet test`. These replace the one-off manual
/// pass in evidence/browser.md (T034). Each test asserts the outcome a visitor feels, not just
/// that a mechanism fired. The hover check that once passed while every click still downloaded
/// the image again is the reason: see Hover_then_click_does_not_download_the_photo_twice.
/// </summary>
[Collection("browser")]
[Trait("Category", "Browser")]
public sealed class BrowserTests(BrowserSiteFixture site)
{
    // The image host's request counter is shared by every test in the collection (tests run one at a
    // time), so each page load starts from zero: earlier tests' fetches can't be counted as this one's.
    private async Task<(IBrowserContext Ctx, IPage Page)> Open(string path, int width = 1440, int height = 900, bool js = true)
    {
        site.ResetHits();
        var ctx = await site.NewContextAsync(width, height, js);
        var page = await ctx.NewPageAsync();
        await page.GotoAsync(site.BaseUrl + path, new() { WaitUntil = WaitUntilState.NetworkIdle });
        return (ctx, page);
    }

    private static Task<string> MainImageFile(IPage page) =>
        page.EvaluateAsync<string>("document.querySelector('article.solo picture img').currentSrc.split('/').pop()");

    // ── Fixture guard: the browser sees the real, styled site ────────────────────────────────────
    // The inlined stylesheet is loaded from the scoped-CSS bundle via the static web assets
    // manifest. A host that misses it renders unstyled pages, and every geometry test below then
    // measures the wrong thing (first fixture run: 1288 px images overflowing a 390 px viewport).

    [Fact]
    public async Task Pages_are_styled_by_the_inlined_stylesheet()
    {
        var (ctx, page) = await Open("/g");
        await using var _ = ctx;
        Assert.True(await page.EvaluateAsync<int>("document.querySelector('style').textContent.length") > 10_000);
        Assert.Equal("grid", await page.EvaluateAsync<string>("getComputedStyle(document.querySelector('.cs')).display"));
    }

    // ── IV.12: hover warms the exact file the photo page shows (owner-reported defect) ──────────

    [Fact]
    public async Task Hover_then_click_does_not_download_the_photo_twice()
    {
        var (ctx, page) = await Open(site.GalleryPath);
        await using var _ = ctx;
        var tile = page.Locator($"a.tile[href='{site.PhotoPath(4)}']");
        await tile.ScrollIntoViewIfNeededAsync();
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        site.ResetHits();

        await tile.HoverAsync();
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await page.WaitForTimeoutAsync(500);
        var warmed = site.HitFiles();
        await tile.ClickAsync();
        await page.WaitForURLAsync("**" + site.PhotoPath(4));
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        var main = await MainImageFile(page);
        Assert.EndsWith(".avif", main);
        // The file the photo page shows was fetched during the hover, BEFORE the click...
        Assert.True(warmed.Contains(main), $"hover warmed [{string.Join(", ", warmed)}], page shows {main}");
        // ...and the click did not fetch it again.
        Assert.Equal(1, site.Hits(main));
    }

    // Guards the guard: with no hover, the photo page's file is NOT already fetched, so the
    // "warmed before the click" assertion above can fail.
    [Fact]
    public async Task Without_a_hover_the_photo_is_not_fetched_before_the_click()
    {
        var (ctx, page) = await Open(site.GalleryPath);
        await using var _ = ctx;
        await page.WaitForTimeoutAsync(500);
        var before = site.HitFiles();
        site.ResetHits();
        await page.Locator($"a.tile[href='{site.PhotoPath(5)}']").ClickAsync();
        await page.WaitForURLAsync("**" + site.PhotoPath(5));
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        var main = await MainImageFile(page);
        Assert.DoesNotContain(main, before);
        Assert.Equal(1, site.Hits(main));
    }

    // ── IV.13: the photo page warms previous/next, and they are reused ───────────────────────────

    [Fact]
    public async Task Next_photo_is_served_from_the_prefetch()
    {
        var (ctx, page) = await Open(site.PhotoPath(2));
        await using var _ = ctx;
        await page.WaitForTimeoutAsync(500);
        var warmed = site.HitFiles();

        await page.Locator($"a[href='{site.PhotoPath(3)}']").First.ClickAsync();
        await page.WaitForURLAsync("**" + site.PhotoPath(3));
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        var main = await MainImageFile(page);
        Assert.True(warmed.Contains(main), $"photo 2's page warmed [{string.Join(", ", warmed)}], photo 3 shows {main}");
        Assert.Equal(1, site.Hits(main)); // not fetched again on arrival
    }

    // ── IV.14: Save-Data suppresses every predictive fetch ───────────────────────────────────────

    [Fact]
    public async Task Save_data_suppresses_neighbour_and_hover_fetches()
    {
        var ctx = await site.NewContextAsync();
        await using var _ = ctx;
        await ctx.AddInitScriptAsync("Object.defineProperty(Navigator.prototype, 'connection', { get: () => ({ saveData: true }) })");
        var page = await ctx.NewPageAsync();
        site.ResetHits();

        await page.GotoAsync(site.BaseUrl + site.PhotoPath(2), new() { WaitUntil = WaitUntilState.NetworkIdle });
        await page.WaitForTimeoutAsync(500);
        var neighbours = await page.EvaluateAsync<string>("document.querySelector('article.solo').dataset.prefetch");
        foreach (var srcset in neighbours.Split('|'))
            foreach (var file in srcset.Split(',').Select(c => c.Trim().Split(' ')[0].Split('/').Last()))
                Assert.Equal(0, site.Hits(file));

        await page.GotoAsync(site.BaseUrl + site.GalleryPath, new() { WaitUntil = WaitUntilState.NetworkIdle });
        var tile = page.Locator($"a.tile[href='{site.PhotoPath(5)}']");
        await tile.ScrollIntoViewIfNeededAsync();
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        var before = await page.EvaluateAsync<int>("performance.getEntriesByType('resource').length");
        await tile.HoverAsync();
        await page.WaitForTimeoutAsync(500);
        Assert.Equal(before, await page.EvaluateAsync<int>("performance.getEntriesByType('resource').length"));
    }

    // ── SC-005: zero layout shift ────────────────────────────────────────────────────────────────

    public static TheoryData<int, string> Widths()
    {
        var d = new TheoryData<int, string>();
        foreach (var w in new[] { 1440, 1024, 390 })
            foreach (var p in new[] { "home", "gallery", "photo" })
                d.Add(w, p);
        return d;
    }

    [Theory]
    [MemberData(nameof(Widths))]
    public async Task No_layout_shift_and_no_horizontal_scroll(int width, string which)
    {
        var path = which switch { "home" => "/g", "gallery" => site.GalleryPath, _ => site.PhotoPath(0) };
        var ctx = await site.NewContextAsync(width, width < 600 ? 844 : 900);
        await using var _ = ctx;
        var page = await ctx.NewPageAsync();
        await page.AddInitScriptAsync("""
            window.__cls = 0;
            new PerformanceObserver(l => { for (const e of l.getEntries()) if (!e.hadRecentInput) window.__cls += e.value; })
              .observe({ type: 'layout-shift', buffered: true });
            """);
        await page.GotoAsync(site.BaseUrl + path, new() { WaitUntil = WaitUntilState.NetworkIdle });
        await page.EvaluateAsync("window.scrollTo(0, document.body.scrollHeight)");
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await page.WaitForTimeoutAsync(300);

        Assert.Equal(0d, await page.EvaluateAsync<double>("window.__cls"));
        Assert.False(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth > innerWidth"));
    }

    // ── IV.16: the tile and the solo picture share one view-transition name ────────────────────

    [Fact]
    public async Task Tile_and_solo_picture_share_a_view_transition_name()
    {
        var id = site.PhotoIds[3];
        var (ctx, page) = await Open(site.GalleryPath);
        await using var _ = ctx;
        var onTile = await page.EvaluateAsync<string>($"getComputedStyle(document.getElementById('p-{id}')).viewTransitionName");
        await page.GotoAsync(site.BaseUrl + site.PhotoPath(3));
        var onSolo = await page.EvaluateAsync<string>($"getComputedStyle(document.getElementById('p-{id}')).viewTransitionName");

        Assert.Equal($"img-{id}", onTile);
        Assert.Equal(onTile, onSolo);
    }

    // ── SC-009 / V.3: the whole walk works with JavaScript off ──────────────────────────────────

    [Fact]
    public async Task Walk_home_gallery_photo_next_back_with_javascript_off()
    {
        var (ctx, page) = await Open("/g", js: false);
        await using var _ = ctx;

        await page.Locator($"a.tile[href='{site.GalleryPath}']").ClickAsync();
        await page.WaitForURLAsync("**" + site.GalleryPath);
        await page.Locator($"a.tile[href='{site.PhotoPath(0)}']").ClickAsync();
        await page.WaitForURLAsync("**" + site.PhotoPath(0));
        Assert.Equal("First caption.", await page.Locator(".solo-caption").InnerTextAsync());
        Assert.Contains("TESTCAM T1", await page.Locator(".metadata-bar").InnerTextAsync());

        await page.Locator($"a[href='{site.PhotoPath(1)}']").First.ClickAsync();
        await page.WaitForURLAsync("**" + site.PhotoPath(1));
        await page.GoBackAsync();
        Assert.EndsWith(site.PhotoPath(0), page.Url);

        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        var full = await page.EvaluateAsync<int>("document.querySelector('article.solo picture img').naturalWidth");
        Assert.True(full >= 320, $"full image not loaded without JS (naturalWidth {full})");
    }
}
