using System.Globalization;
using Gallery.Web.Tests.Fixtures;
using Microsoft.Playwright;

namespace Gallery.Web.Tests;

/// <summary>
/// 011 T020 (SC-003, contracts C2/C4/C5): the home page's single link measured in a real browser.
/// At every width it is centred horizontally and in the space under the header, the page does not
/// scroll, the link is the largest text on the public site (48px) and a full tap target, and the
/// link reaches the listing at <c>/g</c> with JavaScript off. Each test collects every failure with
/// the width and measured values, and asserts once.
/// </summary>
[Collection("nav-browser")]
[Trait("Category", "Browser")]
public sealed class HomeBrowserTests(NavBrowserFixture site)
{
    public static TheoryData<int, int> Viewports() => new()
    {
        { 320, 900 },
        { 390, 900 },
        { 768, 900 },
        { 1280, 900 },
        { 2560, 900 },
        { 390, 640 },
    };

    private static string F(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary>Parks the pointer at (0,0) so no link is hovered (moderate speculation rule), then loads.</summary>
    private async Task Go(IPage page, string path)
    {
        await page.Mouse.MoveAsync(0, 0);
        await page.GotoAsync(site.BaseUrl + path, new() { WaitUntil = WaitUntilState.NetworkIdle });
    }

    /// <summary>The largest computed font size (px) of any element on the page, and one element that has it.</summary>
    private static async Task<(double Max, string Where)> MaxFontSize(IPage page)
    {
        var r = await page.EvaluateAsync<string[]>("""
            (() => {
                let max = 0, where = '';
                for (const e of document.querySelectorAll('*')) {
                    const s = parseFloat(getComputedStyle(e).fontSize);
                    if (s > max) { max = s; where = e.tagName.toLowerCase() + (e.className && typeof e.className === 'string' ? '.' + e.className.trim().split(/\s+/).join('.') : ''); }
                }
                return [String(max), where];
            })()
            """);
        return (double.Parse(r[0], CultureInfo.InvariantCulture), r[1]);
    }

    [Theory]
    [MemberData(nameof(Viewports))]
    public async Task Home_link_is_centred_unscrolled_largest_and_tappable(int width, int height)
    {
        var ctx = await site.NewContextAsync(width, height);
        await using var _ = ctx;
        var page = await ctx.NewPageAsync();
        var failures = new List<string>();
        var at = $"{width}x{height}";

        await Go(page, "/");
        // 013 stated change (test-impact A): two links. Centring (a, b) is measured on the link
        // list as a whole; (d) and (e) on each link; (c) unchanged.
        var count = await page.EvaluateAsync<int>("document.querySelectorAll('nav.nav--home a').length");
        if (count != 2)
        {
            failures.Add($"{at}: expected two 'nav.nav--home a', found {count}");
        }
        else
        {
            foreach (var i in new[] { 0, 1 })
            {
                var l = await page.EvaluateAsync<double[]>($$"""
                    (() => {
                        const a = document.querySelectorAll('nav.nav--home a')[{{i}}], r = a.getBoundingClientRect();
                        return [r.top, r.bottom, parseFloat(getComputedStyle(a).fontSize)];
                    })()
                    """);
                if (l[2] != 48)
                    failures.Add($"{at}: (d) link {i} font-size {F(l[2])}px, expected 48px");
                if (l[1] - l[0] < 44)
                    failures.Add($"{at}: (e) link {i} height {F(l[1] - l[0])} < 44");
            }

            var m = await page.EvaluateAsync<double[]>("""
                (() => {
                    const a = document.querySelector('nav.nav--home ul'), r = a.getBoundingClientRect();
                    const h = document.querySelector('header.site-header').getBoundingClientRect();
                    const d = document.documentElement;
                    return [r.left, r.right, r.top, r.bottom, h.bottom, innerWidth, innerHeight,
                            d.scrollHeight, d.scrollWidth, parseFloat(getComputedStyle(a).fontSize)];
                })()
                """);
            var fontSize = await page.EvaluateAsync<string>("getComputedStyle(document.querySelector('nav.nav--home a')).fontSize");
            double left = m[0], right = m[1], top = m[2], bottom = m[3], headerBottom = m[4];
            double iw = m[5], ih = m[6], sh = m[7], sw = m[8];

            // (a) horizontal centre
            var cx = (left + right) / 2;
            if (Math.Abs(cx - iw / 2) > 2)
                failures.Add($"{at}: (a) link centre-x {F(cx)} vs innerWidth/2 {F(iw / 2)} (off {F(cx - iw / 2)}; rect {F(left)}-{F(right)})");

            // (b) vertical centre of the area below the header
            var cy = (top + bottom) / 2;
            var mid = (headerBottom + ih) / 2;
            if (Math.Abs(cy - mid) > 2)
                failures.Add($"{at}: (b) link centre-y {F(cy)} vs midpoint {F(mid)} of header bottom {F(headerBottom)} and innerHeight {F(ih)} (off {F(cy - mid)}; rect {F(top)}-{F(bottom)})");

            // (c) no scroll either way
            if (sh > ih)
                failures.Add($"{at}: (c) scrollHeight {F(sh)} > innerHeight {F(ih)}");
            if (sw > iw)
                failures.Add($"{at}: (c) scrollWidth {F(sw)} > innerWidth {F(iw)}");

            // (d) the link is 48px
            if (fontSize != "48px")
                failures.Add($"{at}: (d) link font-size {fontSize}, expected 48px");

            // (e) tap height
            if (bottom - top < 44)
                failures.Add($"{at}: (e) link height {F(bottom - top)} < 44");
        }

        // (d) 48px is the max computed font size over every element of /, /g, a gallery and a photo
        // page: no element on any of the four exceeds 48px, and the max across them is exactly 48px
        // (the home link; the other pages carry no 48px text, so a per-page "== 48" cannot hold).
        var overall = 0.0;
        var seen = new List<string>();
        foreach (var path in new[] { "/", "/g", await site.GalleryPathAsync(NavBrowserFixture.SeptemberId), await site.PhotoPathAsync(0) })
        {
            await Go(page, path);
            var (max, where) = await MaxFontSize(page);
            seen.Add($"{path}={F(max)}px ({where})");
            overall = Math.Max(overall, max);
            if (max > 48)
                failures.Add($"{at}: (d) {where} on {path} has computed font size {F(max)}px > 48px");
        }
        if (overall != 48)
            failures.Add($"{at}: (d) max computed font size over the four pages is {F(overall)}px, expected 48px [{string.Join("; ", seen)}]");

        Assert.True(failures.Count == 0, $"{failures.Count} failure(s):\n{string.Join("\n", failures)}");
    }

    [Theory]
    [MemberData(nameof(Viewports))]
    public async Task Home_link_reaches_the_listing_with_javascript_off(int width, int height)
    {
        var ctx = await site.NewContextAsync(width, height, javaScript: false);
        await using var _ = ctx;
        var page = await ctx.NewPageAsync();
        var failures = new List<string>();
        var at = $"{width}x{height} JS off";

        await Go(page, "/");
        var link = page.Locator("nav.nav--home a", new() { HasTextString = "Galleries" });
        var links = await link.CountAsync();
        if (links != 1)
        {
            failures.Add($"{at}: expected one Galleries link in nav.nav--home, found {links}");
        }
        else
        {
            await link.ClickAsync();
            var deadline = DateTime.UtcNow.AddSeconds(15);
            while (new Uri(page.Url).AbsolutePath != "/g" && DateTime.UtcNow < deadline)
                await Task.Delay(50);
            await BrowserWait.LoadedAsync(page);
            var path = new Uri(page.Url).AbsolutePath;
            if (path != "/g")
                failures.Add($"{at}: after clicking Galleries the path is {path}, expected /g");
            var grids = await page.Locator(".cs").CountAsync();
            if (grids < 1)
                failures.Add($"{at}: no .cs grid on {page.Url}");
        }

        Assert.True(failures.Count == 0, $"{failures.Count} failure(s):\n{string.Join("\n", failures)}");
    }
}
