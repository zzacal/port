using System.Text.Json;
using Gallery.Web.Data;
using Gallery.Web.Tests.Fixtures;
using Microsoft.Playwright;
using MongoDB.Driver;

namespace Gallery.Web.Tests;

/// <summary>
/// Feature 015 T018 (US1, US2; SC-001, SC-003, SC-004; FR-007, FR-010, FR-012, FR-014): the library
/// tile's title band and icon row in a real browser. Geometry and colours are MEASURED on the
/// rendered page (a markup assertion cannot see where a band is drawn or what colour it paints).
/// The mouse is parked at (0,0) before every navigation so a hover never colours a control.
/// </summary>
[Collection("library-browser")]
[Trait("Category", "Browser")]
public sealed class TileIconBrowserTests(LibraryBrowserFixture fx)
{
    private const float WaitMs = 15_000;

    /// <summary>Landscape, portrait, square and 3:1 panorama.</summary>
    private static readonly (int W, int H)[] Shapes = [(7943, 5411), (3000, 4500), (4000, 4000), (6000, 2000)];

    // 1x1 PNGs of one colour (SC-004).
    private const string White = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAIAAACQd1PeAAAADElEQVR4nGP4//8/AAX+Av4N70a4AAAAAElFTkSuQmCC";
    private const string Black = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAIAAACQd1PeAAAADElEQVR4nGNgYGAAAAAEAAH2FzhVAAAAAElFTkSuQmCC";
    private const string Grey = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAIAAACQd1PeAAAADElEQVR4nGNoaGgAAAMEAYFL09IQAAAAAElFTkSuQmCC";

    private const string LongTitle = "A slow walk along the northern ridge above the lake at dusk.";

    public static TheoryData<int, int> Widths() => new() { { 1440, 900 }, { 800, 900 }, { 390, 844 } };

    // ── helpers ────────────────────────────────────────────────────────────────────────────

    private static async Task<IPage> OpenAsync(LibrarySite site, string path, int width = 1440, int height = 900)
    {
        var ctx = await site.NewContextAsync(width, height);
        return await GotoAsync(await ctx.NewPageAsync(), site, path);
    }

    private static async Task<IPage> GotoAsync(IPage page, LibrarySite site, string path)
    {
        await page.Mouse.MoveAsync(0, 0);
        var resp = await page.GotoAsync(site.BaseUrl + path, new() { WaitUntil = WaitUntilState.NetworkIdle });
        Assert.True(resp is not null && resp.Status == 200 && new Uri(page.Url).AbsolutePath == path.Split('?')[0],
            $"GET {path} answered {resp?.Status} at {page.Url}");
        return page;
    }

    /// <summary>A context with JavaScript off, carrying the owner session copied from a JS-on context.</summary>
    private static async Task<(IBrowserContext Ctx, IPage Page)> NoScriptAsync(LibraryBrowserFixture fx, LibrarySite site)
    {
        var donor = await site.NewContextAsync();
        var cookies = await donor.CookiesAsync();
        var ctx = await fx.Browser.NewContextAsync(new()
        {
            ViewportSize = new() { Width = 1440, Height = 900 },
            JavaScriptEnabled = false,
        });
        await ctx.RouteAsync(LibraryBrowserFixture.ImageBaseUrl + "/**", r => r.FulfillAsync(new() { Status = 404 }));
        await ctx.AddCookiesAsync(cookies.Select(c => new Cookie
        {
            Name = c.Name, Value = c.Value, Domain = c.Domain, Path = c.Path,
            Secure = c.Secure, HttpOnly = c.HttpOnly, SameSite = c.SameSite,
        }));
        return (ctx, await ctx.NewPageAsync());
    }

    /// <summary>Clicks <paramref name="sel"/> and waits for the new document to load.</summary>
    private static async Task ClickNavigatesAsync(IPage page, string sel)
    {
        var committed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnNavigated(object? _, IFrame f)
        {
            if (f == page.MainFrame) committed.TrySetResult();
        }
        page.FrameNavigated += OnNavigated;
        try
        {
            await page.Locator(sel).ClickAsync();
            var done = await Task.WhenAny(committed.Task, Task.Delay(TimeSpan.FromMilliseconds(WaitMs)));
            Assert.True(done == committed.Task, $"clicking {sel} did not navigate (at {page.Url})");
        }
        finally
        {
            page.FrameNavigated -= OnNavigated;
        }
        await page.WaitForLoadStateAsync(LoadState.Load, new() { Timeout = WaitMs });
        await page.Mouse.MoveAsync(0, 0);
    }

    private static string? OpenParam(IPage page)
    {
        var q = System.Web.HttpUtility.ParseQueryString(new Uri(page.Url).Query);
        return q["open"];
    }

    /// <summary>The tile ids whose edit icon is current, in page order.</summary>
    private static Task<string[]> ActiveEditsAsync(IPage page) =>
        page.EvaluateAsync<string[]>("() => [...document.querySelectorAll('[aria-current=\"true\"].pa-edit')].map(a => a.closest('.lib-tile').id)");

    private static Task<string[]> ActiveGroupsAsync(IPage page) =>
        page.EvaluateAsync<string[]>("() => [...document.querySelectorAll('[aria-current=\"true\"].pa-group')].map(a => a.closest('.lib-tile').id)");

    private static Task<bool> InViewportAsync(IPage page, string sel) =>
        page.EvaluateAsync<bool>("(sel) => { const el = document.querySelector(sel); if (!el) return false; const r = el.getBoundingClientRect(); return r.top >= 0 && r.top < innerHeight; }", sel);

    private async Task<List<string>> ShapedAsync(LibrarySite site)
    {
        var ids = await site.ImagesAsync(Shapes.Length, "Shape");
        for (var i = 0; i < ids.Count; i++)
            await site.Images.UpdateOneAsync(d => d.Id == ids[i],
                Builders<ImageDoc>.Update.Set(d => d.Width, Shapes[i].W).Set(d => d.Height, Shapes[i].H));
        // The portrait (narrowest band) carries the long title.
        await site.Images.UpdateOneAsync(d => d.Id == ids[1], Builders<ImageDoc>.Update.Set(d => d.Title, LongTitle));
        return ids;
    }

    // ── title band geometry (K3) ─────────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(Widths))]
    public async Task Title_band_sits_on_the_photos_bottom_edge_clear_of_the_select_mark(int width, int height)
    {
        await using var site = await fx.NewSiteAsync();
        Assert.Equal(60, LongTitle.Length);
        await ShapedAsync(site);
        var page = await OpenAsync(site, "/admin/library", width, height);

        var tiles = (await page.EvaluateAsync<JsonElement>("""
            () => [...document.querySelectorAll('section.lib .lib-tile')].map(tile => {
              const box = el => { const r = el.getBoundingClientRect(); return { l: r.left, t: r.top, r: r.right, b: r.bottom }; };
              const t = tile.querySelector('.on-photo .title');
              return {
                band: box(tile.querySelector('.on-photo')),
                img: box(tile.querySelector('.pic .main img')),
                mark: box(tile.querySelector('.select-mark')),
                text: t.textContent.trim(), tip: t.getAttribute('title'),
                overflow: t.scrollWidth > t.clientWidth,
                under: tile.querySelectorAll(':scope > .title').length,
              };
            })
            """)).EnumerateArray().ToList();
        Assert.Equal(Shapes.Length, tiles.Count);

        var errors = new List<string>();
        foreach (var t in tiles)
        {
            var text = t.GetProperty("text").GetString()!;
            var (band, img, mark) = (Box(t.GetProperty("band")), Box(t.GetProperty("img")), Box(t.GetProperty("mark")));
            var what = $"{text} @{width}px";
            if (band.L < img.L - 1 || band.R > img.R + 1 || band.T < img.T - 1 || band.B > img.B + 1)
                errors.Add($"{what}: band {band} is not inside the photo {img}");
            if (Math.Abs(band.B - img.B) > 1)
                errors.Add($"{what}: band bottom {band.B:F1} is not the photo's bottom {img.B:F1}");
            if (band.L < mark.R && mark.L < band.R && band.T < mark.B && mark.T < band.B)
                errors.Add($"{what}: band {band} overlaps the select mark {mark}");
            if (t.GetProperty("tip").GetString() != text)
                errors.Add($"{what}: title attribute '{t.GetProperty("tip").GetString()}' is not the text");
            if (t.GetProperty("under").GetInt32() != 0)
                errors.Add($"{what}: a title line is still under the cell");
            if (text == LongTitle && !t.GetProperty("overflow").GetBoolean())
                errors.Add($"{what}: the 60-character title does not overflow (scrollWidth <= clientWidth)");
        }
        Assert.Contains(tiles, t => t.GetProperty("text").GetString() == LongTitle);
        Assert.True(errors.Count == 0, string.Join("\n", errors));
    }

    // ── SC-004 contrast ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SC004_band_text_contrast_is_at_least_4_5_over_white_black_and_grey_photos()
    {
        await using var site = await fx.NewSiteAsync();
        var ids = await site.ImagesAsync(3, "Tone");
        var tones = new[] { White, Black, Grey };
        for (var i = 0; i < ids.Count; i++)
            await site.Images.UpdateOneAsync(d => d.Id == ids[i], Builders<ImageDoc>.Update.Set(d => d.Placeholder.Preview, tones[i]));

        var page = await OpenAsync(site, "/admin/library");
        var rows = (await page.EvaluateAsync<JsonElement>("""
            async () => {
              const rgba = s => { const n = (s.match(/[\d.]+/g) || []).map(Number); return [n[0], n[1], n[2], n.length > 3 ? n[3] : 1]; };
              const out = [];
              for (const tile of document.querySelectorAll('section.lib .lib-tile')) {
                const ph = tile.querySelector('.pic img.ph');
                await ph.decode();
                const c = document.createElement('canvas'); c.width = 1; c.height = 1;
                const g = c.getContext('2d'); g.drawImage(ph, 0, 0, 1, 1);
                const d = g.getImageData(0, 0, 1, 1).data;
                const main = tile.querySelector('.pic .main img');
                out.push({
                  photo: [d[0], d[1], d[2]],
                  mainLoaded: main.complete && main.naturalWidth > 0,
                  fg: rgba(getComputedStyle(tile.querySelector('.on-photo .title')).color),
                  band: rgba(getComputedStyle(tile.querySelector('.on-photo')).backgroundColor),
                });
              }
              return out;
            }
            """)).EnumerateArray().ToList();
        Assert.Equal(3, rows.Count);

        var seen = new List<string>();
        foreach (var r in rows)
        {
            Assert.False(r.GetProperty("mainLoaded").GetBoolean(), "a main image loaded; only the placeholder must show");
            var photo = r.GetProperty("photo").EnumerateArray().Select(e => e.GetDouble()).ToArray();
            var fg = r.GetProperty("fg").EnumerateArray().Select(e => e.GetDouble()).ToArray();
            var band = r.GetProperty("band").EnumerateArray().Select(e => e.GetDouble()).ToArray();
            Assert.True(band[3] > 0, "the band has no background");

            var bg = Composite(band, photo);
            var fgc = Composite(fg, bg);
            var ratio = Contrast(fgc, bg);
            seen.Add($"#{(int)photo[0]:x2}{(int)photo[1]:x2}{(int)photo[2]:x2}");
            Assert.True(ratio >= 4.5, $"over #{(int)photo[0]:x2}{(int)photo[1]:x2}{(int)photo[2]:x2} the band text contrast is {ratio:F2}");
        }
        Assert.Equal(["#808080", "#000000", "#ffffff"], seen); // newest first
    }

    private static double[] Composite(double[] top, double[] under)
    {
        var a = top.Length > 3 ? top[3] : 1;
        return [top[0] * a + under[0] * (1 - a), top[1] * a + under[1] * (1 - a), top[2] * a + under[2] * (1 - a)];
    }

    private static double Luminance(double[] c)
    {
        static double Lin(double v) { v /= 255; return v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4); }
        return 0.2126 * Lin(c[0]) + 0.7152 * Lin(c[1]) + 0.0722 * Lin(c[2]);
    }

    private static double Contrast(double[] a, double[] b)
    {
        var (la, lb) = (Luminance(a), Luminance(b));
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    // ── SC-001 icon row ──────────────────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(Widths))]
    public async Task SC001_the_icon_row_is_one_tap_high_with_three_44px_buttons_on_one_row(int width, int height)
    {
        await using var site = await fx.NewSiteAsync();
        var ids = await ShapedAsync(site);
        await site.GalleryAsync("Coast", [ids[0], ids[2]], publish: true);
        var page = await OpenAsync(site, "/admin/library", width, height);

        var rows = (await page.EvaluateAsync<JsonElement>("""
            () => {
              const tap = parseFloat(getComputedStyle(document.documentElement).getPropertyValue('--tap'));
              return [...document.querySelectorAll('section.lib .lib-tile .photo-actions')].map(row => ({
                tap,
                h: row.getBoundingClientRect().height,
                kids: [...row.children].map(k => { const r = k.getBoundingClientRect(); return { cls: k.className, w: r.width, h: r.height, t: r.top }; }),
              }));
            }
            """)).EnumerateArray().ToList();
        Assert.Equal(Shapes.Length, rows.Count);
        var errors = new List<string>();
        foreach (var r in rows)
        {
            Assert.Equal(44, r.GetProperty("tap").GetDouble());
            if (Math.Abs(r.GetProperty("h").GetDouble() - 44) > 0.5)
                errors.Add($"@{width}px row height {r.GetProperty("h").GetDouble()}");
            var kids = r.GetProperty("kids").EnumerateArray().ToList();
            if (kids.Count != 3) errors.Add($"@{width}px row has {kids.Count} children");
            foreach (var k in kids)
                if (k.GetProperty("w").GetDouble() < 44 || k.GetProperty("h").GetDouble() < 44)
                    errors.Add($"@{width}px {k.GetProperty("cls").GetString()} is {k.GetProperty("w").GetDouble()}x{k.GetProperty("h").GetDouble()}");
            var tops = kids.Select(k => k.GetProperty("t").GetDouble()).ToList();
            if (tops.Max() - tops.Min() > 0.5) errors.Add($"@{width}px buttons wrap: tops {string.Join(", ", tops)}");
        }
        Assert.True(errors.Count == 0, string.Join("\n", errors));
    }

    // ── tab order ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Tab_goes_group_view_edit_and_skips_an_unavailable_view()
    {
        await using var site = await fx.NewSiteAsync();
        var ids = await site.ImagesAsync(2, "Tab");
        await site.GalleryAsync("Coast", [ids[0]], publish: true); // ids[0] has a public page, ids[1] none
        var page = await OpenAsync(site, "/admin/library");

        async Task<string[]> TabsFrom(string iid, int presses)
        {
            await page.Locator($"#t-{iid} .pa-group").FocusAsync();
            var seen = new List<string> { await FocusedAsync(page) };
            for (var i = 0; i < presses; i++)
            {
                await page.Keyboard.PressAsync("Tab");
                seen.Add(await FocusedAsync(page));
            }
            return seen.ToArray();
        }

        Assert.Equal([$"pa-group t-{ids[0]}", $"pa-view t-{ids[0]}", $"pa-edit t-{ids[0]}"], await TabsFrom(ids[0], 2));
        Assert.Equal([$"pa-group t-{ids[1]}", $"pa-edit t-{ids[1]}"], await TabsFrom(ids[1], 1));
    }

    private static Task<string> FocusedAsync(IPage page) => page.EvaluateAsync<string>("""
        () => { const a = document.activeElement; const c = ['pa-group', 'pa-view', 'pa-edit'].find(x => a.classList.contains(x)) ?? a.tagName;
                return c + ' ' + (a.closest('.lib-tile')?.id ?? ''); }
        """);

    // ── SC-003 the toggle ────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(1440, 900)]
    [InlineData(390, 844)]
    public async Task SC003_with_js_edit_and_gallery_toggle_one_panel_at_a_time(int width, int height)
    {
        await using var site = await fx.NewSiteAsync();
        var ids = await site.ImagesAsync(2, "Toggle");
        var (a, b) = (ids[0], ids[1]);
        var page = await OpenAsync(site, "/admin/library", width, height);
        await ToggleStepsAsync(page, a, b, checkViewport: true);
    }

    [Fact]
    public async Task SC003_without_js_the_same_three_steps_work()
    {
        await using var site = await fx.NewSiteAsync();
        var ids = await site.ImagesAsync(2, "Toggle");
        var (ctx, page) = await NoScriptAsync(fx, site);
        try
        {
            await GotoAsync(page, site, "/admin/library");
            Assert.False(await page.EvaluateAsync<bool>("() => document.querySelector('.selection-bar')?.dataset.script === 'ready'"));
            await ToggleStepsAsync(page, ids[0], ids[1], checkViewport: false);
        }
        finally
        {
            await ctx.DisposeAsync();
        }
    }

    private static async Task ToggleStepsAsync(IPage page, string a, string b, bool checkViewport)
    {
        // 1. A's pencil opens A.
        await ClickNavigatesAsync(page, $"#t-{a} .pa-edit");
        Assert.Equal(a, OpenParam(page));
        Assert.True(await page.Locator("#lib-edit").IsVisibleAsync());
        Assert.Equal([$"t-{a}"], await ActiveEditsAsync(page));
        Assert.Equal([$"t-{a}"], await ActiveGroupsAsync(page));

        // 2. B's folder opens B, with its Galleries field in view.
        await ClickNavigatesAsync(page, $"#t-{b} .pa-group");
        Assert.Equal(b, OpenParam(page));
        Assert.True(await page.Locator("#lib-edit").IsVisibleAsync());
        Assert.EndsWith($"/admin/library/images/{b}", await page.Locator("#lib-edit").GetAttributeAsync("action"));
        if (checkViewport)
            Assert.True(await InViewportAsync(page, "#lib-edit .combobox"), "B's Galleries field is not in the viewport");
        Assert.Equal([$"t-{b}"], await ActiveEditsAsync(page));
        Assert.Equal([$"t-{b}"], await ActiveGroupsAsync(page));

        // 3. B's pencil closes the editor.
        await ClickNavigatesAsync(page, $"#t-{b} .pa-edit");
        Assert.Null(OpenParam(page));
        Assert.Equal(0, await page.Locator("#lib-edit").CountAsync());
        Assert.Empty(await ActiveEditsAsync(page));
        Assert.Empty(await ActiveGroupsAsync(page));
    }

    // ── panel in view: sticky aside (K7) and J1 ──────────────────────────────────────────────

    [Fact]
    public async Task At_1440_the_sticky_panel_is_in_view_beside_the_40th_tile()
    {
        await using var site = await fx.NewSiteAsync();
        await site.ImagesAsync(45, "Many");
        var page = await OpenAsync(site, "/admin/library");
        var tile = page.Locator("section.lib .lib-tile").Nth(39);
        await tile.ScrollIntoViewIfNeededAsync();
        var id = await tile.GetAttributeAsync("id");
        await ClickNavigatesAsync(page, $"#{id} .pa-edit");

        Assert.True(await page.EvaluateAsync<double>("() => scrollY") > 1000, "the page did not land at the 40th tile");
        Assert.True(await InViewportAsync(page, $"#{id}"), "the 40th tile is not in view");
        Assert.True(await InViewportAsync(page, "#lib-edit"), "the panel's top is not in the viewport");
    }

    [Fact]
    public async Task At_390_with_js_the_stacked_panels_galleries_field_is_brought_into_view()
    {
        await using var site = await fx.NewSiteAsync();
        await site.ImagesAsync(45, "Many");
        var page = await OpenAsync(site, "/admin/library", 390, 844);
        var tile = page.Locator("section.lib .lib-tile").Nth(39);
        await tile.ScrollIntoViewIfNeededAsync();
        var id = await tile.GetAttributeAsync("id");
        await ClickNavigatesAsync(page, $"#{id} .pa-edit");

        Assert.Equal(id![2..], OpenParam(page));
        Assert.True(await InViewportAsync(page, "#lib-edit .combobox"), "the Galleries field is not in the viewport");
    }

    // ── FR-010 one filled action ─────────────────────────────────────────────────────────────

    // Copied from LibraryBrowserTests.FilledButtonsJs (009 UI-3).
    private const string FilledButtonsJs = """
        () => {
          const rgba = s => { const n = (s.match(/[\d.]+/g) || []).map(Number); return { r: n[0], g: n[1], b: n[2], a: n.length > 3 ? n[3] : 1 }; };
          const page = rgba(getComputedStyle(document.body).backgroundColor);
          const filled = el => { const c = rgba(getComputedStyle(el).backgroundColor); return c.a > 0 && !(c.r === page.r && c.g === page.g && c.b === page.b); };
          const visible = el => el.getClientRects().length > 0 && getComputedStyle(el).visibility !== 'hidden';
          const modal = [...document.querySelectorAll('dialog')].find(d => d.open && d.matches(':modal')) || null;
          const all = [...document.querySelectorAll('button, summary, a.btn')].filter(visible).filter(filled);
          const label = el => el.textContent.trim().replace(/\s+/g, ' ');
          return {
            outside: all.filter(el => !modal || !modal.contains(el)).map(label),
            inside: modal ? all.filter(el => modal.contains(el)).map(label) : [],
            modal: modal !== null,
          };
        }
        """;

    private static async Task<string[]> FilledOutsideAsync(IPage page) =>
        (await page.EvaluateAsync<JsonElement>(FilledButtonsJs)).GetProperty("outside")
            .EnumerateArray().Select(e => e.GetString()!).ToArray();

    [Fact]
    public async Task FR010_no_icon_is_filled_at_rest_and_only_Save_with_a_panel_open_and_A_active()
    {
        await using var site = await fx.NewSiteAsync();
        var ids = await site.ImagesAsync(3);
        await site.GalleryAsync("Coast", [ids[0]], publish: true);

        var rest = await OpenAsync(site, "/admin/library");
        Assert.Empty(await FilledOutsideAsync(rest));

        var open = await OpenAsync(site, $"/admin/library?open={ids[0]}");
        Assert.Equal([$"t-{ids[0]}"], await ActiveEditsAsync(open));
        Assert.Equal(["Save"], await FilledOutsideAsync(open));
    }

    private sealed record Rect(double L, double T, double R, double B);

    private static Rect Box(JsonElement e) =>
        new(e.GetProperty("l").GetDouble(), e.GetProperty("t").GetDouble(), e.GetProperty("r").GetDouble(), e.GetProperty("b").GetDouble());
}
