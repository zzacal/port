using System.Text.RegularExpressions;
using Gallery.Web.Data;
using Gallery.Web.Tests.Fixtures;
using Microsoft.Playwright;
using MongoDB.Driver;

namespace Gallery.Web.Tests;

/// <summary>
/// 013 T028 (US3; research R-07; test-impact B): the camera pages in a real browser on
/// <see cref="NavBrowserFixture"/>, whose photos are all TESTCAM T1 (slug <c>testcam-t1</c>). The
/// walk /c → camera → first photo → ArrowRight to the last → ArrowLeft → Escape, at 320 and 1440
/// px; the same walk by Next links with JavaScript off; and, on /c, the camera page and one camera
/// photo page: no horizontal scroll, every image decoded, zero image layout shift, no
/// /admin-assets/ request, no key hint and no duplicate header destination. Each test parks the
/// pointer at (0,0) before every <c>GotoAsync</c>, collects every failure and asserts once.
/// </summary>
[Collection("nav-browser")]
[Trait("Category", "Browser")]
public sealed class CameraBrowserTests(NavBrowserFixture site)
{
    private const string Slug = NavBrowserFixture.CameraSlug;

    private static int HeightFor(int width) => width < 600 ? 844 : 900;

    private static string PathOf(IPage page) => new Uri(page.Url).PathAndQuery + new Uri(page.Url).Fragment;

    private async Task<IReadOnlyList<string>> ItemsAsync() =>
        (await site.Db.GetCollection<CameraGroupDoc>("cameras").Find(c => c.Slug == Slug).FirstAsync()).Items;

    /// <summary>Parks the pointer at (0,0) so no link is hovered (moderate speculation rule), then loads.</summary>
    private async Task Go(IPage page, string path, WaitUntilState until = WaitUntilState.NetworkIdle)
    {
        await page.Mouse.MoveAsync(0, 0);
        await page.GotoAsync(site.BaseUrl + path, new() { WaitUntil = until });
    }

    /// <summary>
    /// Waits up to <paramref name="ms"/> for the URL to satisfy <paramref name="until"/> and the
    /// document to load, and returns the path either way, so a key that did nothing fails on the
    /// caller's comparison rather than on a timeout.
    /// </summary>
    private static async Task<string> PathAfter(IPage page, Func<string, bool> until, int ms = 15000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(ms);
        while (!until(PathOf(page)))
        {
            if (DateTime.UtcNow > deadline) return $"{PathOf(page)} (timed out after {ms} ms)";
            await Task.Delay(50);
        }
        try { await page.WaitForFunctionAsync("document.readyState === 'complete'", null, new() { Timeout = ms }); }
        catch (Exception e) when (e is PlaywrightException or TimeoutException) { return $"{PathOf(page)} (not loaded after {ms} ms)"; }
        return PathOf(page);
    }

    // ── the walk ─────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(320)]
    [InlineData(1440)]
    public async Task Walk_listing_camera_photo_then_keys_to_the_last_back_one_and_escape(int width)
    {
        var items = await ItemsAsync();
        Assert.Equal(NavBrowserFixture.PhotoCount, items.Count);
        var camera = await site.CameraPathAsync();
        var ctx = await site.NewContextAsync(width, HeightFor(width));
        await using var _ = ctx;
        var page = await ctx.NewPageAsync();
        var failures = new List<string>();
        var at = $"{width}px";

        await Go(page, "/c");
        await page.Locator($"a.tile[href='{camera}']").ClickAsync();
        var path = await PathAfter(page, p => p == camera);
        if (path != camera) failures.Add($"{at}: the /c tile went to {path}, expected {camera}");

        var first = await site.CameraPhotoPathAsync(0);
        await page.Locator($"a.tile[href='{first}']").ClickAsync();
        path = await PathAfter(page, p => p == first);
        if (path != first) failures.Add($"{at}: the first tile went to {path}, expected {first}");

        for (var i = 1; i < items.Count && failures.Count == 0; i++)
        {
            var want = $"{camera}/{items[i]}";
            await page.Keyboard.PressAsync("ArrowRight");
            path = await PathAfter(page, p => p == want);
            if (path != want) failures.Add($"{at}: ArrowRight to item {i} went to {path}, expected {want}");
        }

        if (failures.Count == 0)
        {
            var last = items.Count - 1;
            var count = await page.Locator("article.solo .photo-count").InnerTextAsync();
            if (count != $"{items.Count} / {items.Count}") failures.Add($"{at}: last photo count {count}");
            var next = await page.Locator("article.solo a[rel=next]").CountAsync();
            if (next != 0) failures.Add($"{at}: the last photo has {next} Next link(s)");

            var back = $"{camera}/{items[last - 1]}";
            await page.Keyboard.PressAsync("ArrowLeft");
            path = await PathAfter(page, p => p == back);
            if (path != back) failures.Add($"{at}: ArrowLeft went to {path}, expected {back}");

            var tile = $"{camera}#p-{items[last - 1]}";
            await page.Keyboard.PressAsync("Escape");
            path = await PathAfter(page, p => p == tile);
            if (path != tile) failures.Add($"{at}: Escape went to {path}, expected {tile}");
        }

        Assert.True(failures.Count == 0, $"{failures.Count} failure(s):\n{string.Join("\n", failures)}");
    }

    [Fact]
    public async Task Walk_by_Next_links_with_javascript_off()
    {
        var items = await ItemsAsync();
        var camera = await site.CameraPathAsync();
        var ctx = await site.NewContextAsync(1440, 900, javaScript: false);
        await using var _ = ctx;
        var page = await ctx.NewPageAsync();
        var failures = new List<string>();

        await Go(page, "/c", WaitUntilState.Load);
        await page.Locator($"a.tile[href='{camera}']").ClickAsync();
        var path = await PathAfter(page, p => p == camera);
        if (path != camera) failures.Add($"the /c tile went to {path}, expected {camera}");

        var first = await site.CameraPhotoPathAsync(0);
        await page.Locator($"a.tile[href='{first}']").ClickAsync();
        path = await PathAfter(page, p => p == first);
        if (path != first) failures.Add($"the first tile went to {path}, expected {first}");
        if (await page.Locator("article.solo a[rel=prev]").CountAsync() != 0) failures.Add("the first photo has a Previous link");

        for (var i = 1; i < items.Count && failures.Count == 0; i++)
        {
            var want = $"{camera}/{items[i]}";
            await page.Locator("article.solo a[rel=next]").ClickAsync();
            path = await PathAfter(page, p => p == want);
            if (path != want) failures.Add($"Next to item {i} went to {path}, expected {want}");
        }
        if (failures.Count == 0 && await page.Locator("article.solo a[rel=next]").CountAsync() != 0)
            failures.Add("the last photo has a Next link");

        if (failures.Count == 0)
        {
            var tile = $"{camera}#p-{items[^1]}";
            await page.Locator(".breadcrumb a[href*=\"#p-\"]").ClickAsync();
            path = await PathAfter(page, p => p == tile);
            if (path != tile) failures.Add($"the camera crumb went to {path}, expected {tile}");
        }

        Assert.True(failures.Count == 0, $"{failures.Count} failure(s):\n{string.Join("\n", failures)}");
    }

    // ── layout, images and the test-impact (B) scans ─────────────────────────

    [Theory]
    [InlineData(320)]
    [InlineData(1440)]
    public async Task Camera_pages_decode_every_image_without_shift_or_horizontal_scroll(int width)
    {
        var failures = new List<string>();
        foreach (var path in new[] { "/c", await site.CameraPathAsync(), await site.CameraPhotoPathAsync(11) })
        {
            var ctx = await site.NewContextAsync(width, HeightFor(width));
            await using var _ = ctx;
            var page = await ctx.NewPageAsync();
            await page.AddInitScriptAsync("""
                window.__cls = 0;
                new PerformanceObserver(l => { for (const e of l.getEntries()) if (!e.hadRecentInput) window.__cls += e.value; })
                  .observe({ type: 'layout-shift', buffered: true });
                """);
            await Go(page, path);
            // Step through the page so every lazy image enters the viewport and loads.
            await page.EvaluateAsync("""
                (async () => {
                    for (let y = 0; y <= document.documentElement.scrollHeight; y += innerHeight / 2) {
                        window.scrollTo(0, y);
                        await new Promise(r => setTimeout(r, 60));
                    }
                })()
                """);
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
            await page.WaitForFunctionAsync("[...document.images].every(i => i.complete)", null, new() { Timeout = 15000 });
            await page.WaitForTimeoutAsync(300);

            var where = $"{path} at {width}px";
            var cls = await page.EvaluateAsync<double>("window.__cls");
            if (cls != 0) failures.Add($"{where}: CLS {cls}");
            var widths = await page.EvaluateAsync<double[]>("[document.documentElement.scrollWidth, innerWidth]");
            if (widths[0] > widths[1]) failures.Add($"{where}: scrollWidth {widths[0]} > innerWidth {widths[1]}");
            var images = await page.EvaluateAsync<int>("document.images.length");
            if (images == 0) failures.Add($"{where}: no img at all");
            var broken = await page.EvaluateAsync<string[]>("[...document.images].filter(i => !(i.naturalWidth > 0)).map(i => i.className + ' ' + (i.currentSrc || i.src).slice(-60))");
            if (broken.Length > 0) failures.Add($"{where}: {broken.Length} of {images} img with naturalWidth 0: [{string.Join(", ", broken)}]");
        }
        Assert.True(failures.Count == 0, $"{failures.Count} failure(s):\n{string.Join("\n", failures)}");
    }

    [Fact]
    public async Task Camera_pages_request_no_admin_assets_mention_no_keys_and_repeat_no_header_destination()
    {
        var hint = new Regex(@"arrow|escape|\besc\b|keyboard|shortcut|\bkeys?\b", RegexOptions.IgnoreCase);
        var failures = new List<string>();
        foreach (var path in new[] { "/c", await site.CameraPathAsync(), await site.CameraPhotoPathAsync(0), await site.CameraPhotoPathAsync(11), await site.CameraPhotoPathAsync(22) })
        {
            var ctx = await site.NewContextAsync();
            await using var _ = ctx;
            var page = await ctx.NewPageAsync();
            var requested = new List<string>();
            page.Request += (_, r) => { lock (requested) requested.Add(r.Url); };
            await Go(page, path);

            lock (requested)
                failures.AddRange(requested.Where(u => u.Contains("/admin-assets/", StringComparison.Ordinal)).Select(u => $"{path}: requested {u}"));
            if (await page.Locator("script[src*='/admin-assets/'], link[href*='/admin-assets/']").CountAsync() != 0)
                failures.Add($"{path}: references /admin-assets/");

            var texts = await page.EvaluateAsync<string[]>("""
                [document.body.innerText,
                 ...[...document.querySelectorAll('[title]')].map(e => 'title=' + e.getAttribute('title')),
                 ...[...document.querySelectorAll('[aria-label]')].map(e => 'aria-label=' + e.getAttribute('aria-label')),
                 ...[...document.querySelectorAll('[aria-keyshortcuts], [accesskey]')].map(e => 'shortcut attr on ' + e.outerHTML.slice(0, 80))]
                """);
            failures.AddRange(texts.Where(t => hint.IsMatch(t) || t.StartsWith("shortcut attr")).Select(t => $"{path}: {t[..Math.Min(t.Length, 200)]}"));

            var hrefs = await page.EvaluateAsync<string[]>("[...document.querySelectorAll('header.site-header a')].map(a => a.getAttribute('href'))");
            if (hrefs.Length == 0 || hrefs.Distinct().Count() != hrefs.Length)
                failures.Add($"{path}: header hrefs [{string.Join(", ", hrefs)}]");
        }
        Assert.True(failures.Count == 0, $"{failures.Count} failure(s):\n{string.Join("\n", failures)}");
    }
}
