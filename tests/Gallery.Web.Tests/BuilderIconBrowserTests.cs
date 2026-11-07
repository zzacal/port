using System.Text.Json;
using Gallery.Web.Tests.Fixtures;
using Microsoft.Playwright;

namespace Gallery.Web.Tests;

/// <summary>
/// Feature 015 K6 (T023, US4): the gallery builder's icon row in a real browser. Geometry is
/// MEASURED (the row height, one row of four icons, the title band inside the photo), and every
/// action is checked by its outcome: the stored <c>items</c> and <c>pinnedCover</c>, the announced
/// position, and the one filled button at rest. The mouse is parked at (0,0) before probing, so no
/// hover background is counted.
/// </summary>
[Collection("library-browser")]
[Trait("Category", "Browser")]
public sealed class BuilderIconBrowserTests(LibraryBrowserFixture fx)
{
    private const float WaitMs = 15_000;

    private const string Tiles = ".builder reorder-grid .cs > .tile";

    private static ILocator TileAt(IPage page, int i) => page.Locator(Tiles).Nth(i);

    private static ILocator Handle(IPage page, int i) => TileAt(page, i).Locator(".b-ctl .handle");

    private static ILocator Star(IPage page, int i) => TileAt(page, i).Locator(".b-ctl .cover-marker > .icon-button");

    private static ILocator RemoveOf(IPage page, int i) => TileAt(page, i).Locator(".b-ctl > button.icon-button[name='image'][form]");

    private static async Task<IPage> OpenAsync(LibrarySite site, string path, int width = 1440)
    {
        var page = await site.OpenAsync(path, width);
        await page.Mouse.MoveAsync(0, 0);
        return page;
    }

    /// <summary>Waits until the main frame commits a new document (a form submit and its 303) and it has loaded.</summary>
    private static async Task NavigatesAsync(IPage page, Func<Task> action)
    {
        var committed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnNavigated(object? _, IFrame f)
        {
            if (f == page.MainFrame) committed.TrySetResult();
        }
        page.FrameNavigated += OnNavigated;
        try
        {
            await action();
            var done = await Task.WhenAny(committed.Task, Task.Delay(TimeSpan.FromMilliseconds(WaitMs)));
            Assert.True(done == committed.Task, $"no form was submitted (the page did not navigate away from {page.Url})");
        }
        finally
        {
            page.FrameNavigated -= OnNavigated;
        }
        await page.WaitForLoadStateAsync(LoadState.Load, new() { Timeout = WaitMs });
        await page.Mouse.MoveAsync(0, 0);
    }

    // ── geometry ────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(1440)]
    [InlineData(390)]
    public async Task Each_row_is_44_high_with_the_four_icons_on_one_row_and_the_title_band_inside_the_photo(int width)
    {
        await using var site = await fx.NewSiteAsync();
        var ids = await site.ImagesAsync(3);
        var gid = await site.GalleryAsync("Coast", ids, publish: true);
        var page = await OpenAsync(site, $"/admin/galleries/{gid}", width);

        var tiles = await page.EvaluateAsync<JsonElement>("""
            sel => [...document.querySelectorAll(sel)].map(tile => {
              const box = el => { const b = el.getBoundingClientRect(); return { t: b.top, b: b.bottom, l: b.left, r: b.right, h: b.height, w: b.width }; };
              const row = tile.querySelector('.b-ctl');
              const kids = [...row.children].map(k => ({ tag: k.tagName.toLowerCase(), cls: k.className, ...box(k) }));
              const icons = [...row.querySelectorAll('svg.icon')].map(s => ({ name: s.dataset.icon, ...box(s) }));
              const band = tile.querySelector('.cell .on-photo');
              const frame = tile.querySelector('.cell .frame');
              return { row: box(row), kids, icons, band: band ? box(band) : null, frame: frame ? box(frame) : null,
                       cell: box(tile.querySelector(':scope > .cell')), tile: box(tile),
                       wrap: getComputedStyle(row).flexWrap, title: band ? band.textContent.trim() : null };
            })
            """, Tiles);
        var list = tiles.EnumerateArray().ToList();
        Assert.Equal(3, list.Count);
        var i = 0;
        foreach (var t in list)
        {
            i++;
            var where = $"@{width} tile {i}";
            double B(JsonElement e, string side) => e.GetProperty(side).GetDouble();
            var row = t.GetProperty("row");
            Assert.True(Math.Abs(B(row, "h") - 44) < 0.5, $"{where}: b-ctl height {B(row, "h")}");
            Assert.Equal("nowrap", t.GetProperty("wrap").GetString());

            var icons = t.GetProperty("icons").EnumerateArray().ToList();
            Assert.Equal(["grip-vertical", "star", "image-minus", "square-arrow-out-up-right"], icons.Select(x => x.GetProperty("name").GetString()));
            var kids = t.GetProperty("kids").EnumerateArray().ToList();
            Assert.Equal(4, kids.Count);
            foreach (var k in kids)
            {
                Assert.True(Math.Abs(B(k, "t") - B(row, "t")) < 0.5 && Math.Abs(B(k, "h") - 44) < 0.5,
                    $"{where}: {k.GetProperty("tag").GetString()}.{k.GetProperty("cls").GetString()} at top {B(k, "t")} height {B(k, "h")}, row top {B(row, "t")}");
                Assert.True(B(k, "l") >= B(t.GetProperty("tile"), "l") - 0.5 && B(k, "r") <= B(t.GetProperty("tile"), "r") + 0.5, $"{where}: a control leaves the tile");
            }
            for (var k = 1; k < kids.Count; k++)
                Assert.True(B(kids[k], "l") >= B(kids[k - 1], "r") - 0.5, $"{where}: control {k + 1} overlaps control {k}");
            Assert.True(B(row, "t") >= B(t.GetProperty("cell"), "b") - 0.5, $"{where}: the row is over the photo");

            Assert.Equal(JsonValueKind.Object, t.GetProperty("band").ValueKind);
            var band = t.GetProperty("band");
            var frame = t.GetProperty("frame");
            Assert.True(B(band, "h") > 0, $"{where}: the title band has no height");
            Assert.True(B(band, "t") >= B(frame, "t") - 0.5 && B(band, "b") <= B(frame, "b") + 0.5
                        && B(band, "l") >= B(frame, "l") - 0.5 && B(band, "r") <= B(frame, "r") + 0.5,
                $"{where}: the title band [{B(band, "l")},{B(band, "t")},{B(band, "r")},{B(band, "b")}] is outside the photo [{B(frame, "l")},{B(frame, "t")},{B(frame, "r")},{B(frame, "b")}]");
            Assert.Equal($"Photo {i}", t.GetProperty("title").GetString());
        }
    }

    // ── keyboard reorder with the icon handle ───────────────────────────────────────────────

    [Fact]
    public async Task Keyboard_lift_and_move_with_the_icon_handle_announces_and_stores_the_order()
    {
        await using var site = await fx.NewSiteAsync();
        var ids = await site.ImagesAsync(3);
        var gid = await site.GalleryAsync("Coast", ids);
        var page = await OpenAsync(site, $"/admin/galleries/{gid}");

        var handle = Handle(page, 0);
        Assert.Equal(1, await handle.Locator("svg.icon[data-icon='grip-vertical']").CountAsync());
        await handle.FocusAsync();
        await page.Keyboard.PressAsync("Space");
        Assert.Equal("true", await handle.GetAttributeAsync("aria-pressed"));
        var live = page.Locator($"{Tiles} .b-ctl [role='status']");
        Assert.Equal(1, await live.CountAsync());
        Assert.StartsWith("Lifted. Position 1 of 3.", (await live.InnerTextAsync()).Trim());

        await page.Keyboard.PressAsync("ArrowRight");
        Assert.Equal("Position 2 of 3", (await live.InnerTextAsync()).Trim());

        await NavigatesAsync(page, () => page.Keyboard.PressAsync("Space"));
        Assert.Equal([ids[1], ids[0], ids[2]], (await site.GalleryDocAsync(gid)).Items);
    }

    // ── cover: pin, then unpin ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Clicking_the_star_pins_a_filled_pressed_star_and_clicking_it_again_returns_to_automatic()
    {
        await using var site = await fx.NewSiteAsync();
        var ids = await site.ImagesAsync(3);
        var gid = await site.GalleryAsync("Coast", ids);
        var page = await OpenAsync(site, $"/admin/galleries/{gid}");
        Assert.Null((await site.GalleryDocAsync(gid)).PinnedCover);

        // At rest the first tile is the automatic cover (a static star), the others offer to pin.
        Assert.Equal("span", await Star(page, 0).EvaluateAsync<string>("e => e.tagName.toLowerCase()"));
        Assert.Equal("false", await Star(page, 2).GetAttributeAsync("aria-pressed"));
        Assert.Equal("none", await Star(page, 2).Locator("svg").GetAttributeAsync("fill"));

        await NavigatesAsync(page, () => Star(page, 2).ClickAsync());
        Assert.Equal(ids[2], (await site.GalleryDocAsync(gid)).PinnedCover);
        Assert.Equal("true", await Star(page, 2).GetAttributeAsync("aria-pressed"));
        Assert.Equal("Unpin cover", await Star(page, 2).GetAttributeAsync("aria-label"));
        Assert.Equal("currentColor", await Star(page, 2).Locator("svg").GetAttributeAsync("fill"));
        var drawn = await Star(page, 2).Locator("svg").EvaluateAsync<string>("s => getComputedStyle(s).fill");
        Assert.NotEqual("none", drawn);
        // No other tile is pressed, and the first tile is no longer the automatic cover.
        Assert.Equal(1, await page.Locator($"{Tiles} .cover-marker [aria-pressed='true']").CountAsync());
        Assert.Equal(0, await page.Locator($"{Tiles} .cover-marker .is-unavailable").CountAsync());

        await NavigatesAsync(page, () => Star(page, 2).ClickAsync());
        Assert.Null((await site.GalleryDocAsync(gid)).PinnedCover);
        Assert.Equal(0, await page.Locator($"{Tiles} .cover-marker [aria-pressed='true']").CountAsync());
        Assert.Equal("span", await Star(page, 0).EvaluateAsync<string>("e => e.tagName.toLowerCase()"));
        Assert.Equal("Cover: first photo", await Star(page, 0).GetAttributeAsync("aria-label"));
        Assert.Equal("false", await Star(page, 2).GetAttributeAsync("aria-pressed"));
    }

    // ── remove ──────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task The_remove_icon_takes_the_photo_out_of_the_stored_items()
    {
        await using var site = await fx.NewSiteAsync();
        var ids = await site.ImagesAsync(3);
        var gid = await site.GalleryAsync("Coast", ids);
        var page = await OpenAsync(site, $"/admin/galleries/{gid}");

        var remove = RemoveOf(page, 1);
        Assert.Equal(1, await remove.CountAsync());
        Assert.Equal(ids[1], await remove.GetAttributeAsync("value"));
        Assert.Equal("Remove Photo 2 from this gallery", await remove.GetAttributeAsync("aria-label"));
        Assert.Equal(1, await remove.Locator("svg.icon[data-icon='image-minus']").CountAsync());

        await NavigatesAsync(page, () => remove.ClickAsync());
        Assert.Equal([ids[0], ids[2]], (await site.GalleryDocAsync(gid)).Items);
        Assert.Equal(2, await page.Locator(Tiles).CountAsync());
    }

    // ── one filled button at rest ───────────────────────────────────────────────────────────

    /// <summary>The LibraryBrowserTests probe: visible buttons whose computed background is filled (not transparent, not the page colour).</summary>
    private const string FilledButtonsJs = """
        () => {
          const rgba = s => { const n = (s.match(/[\d.]+/g) || []).map(Number); return { r: n[0], g: n[1], b: n[2], a: n.length > 3 ? n[3] : 1 }; };
          const page = rgba(getComputedStyle(document.body).backgroundColor);
          const filled = el => { const c = rgba(getComputedStyle(el).backgroundColor); return c.a > 0 && !(c.r === page.r && c.g === page.g && c.b === page.b); };
          const visible = el => el.getClientRects().length > 0 && getComputedStyle(el).visibility !== 'hidden';
          return [...document.querySelectorAll('button, summary, a.btn')].filter(visible).filter(filled)
            .map(el => el.textContent.trim().replace(/\s+/g, ' '));
        }
        """;

    [Fact]
    public async Task The_builder_at_rest_has_one_filled_button_Save()
    {
        await using var site = await fx.NewSiteAsync();
        var ids = await site.ImagesAsync(3);
        var gid = await site.GalleryAsync("Coast", ids);
        await site.GalleryAsync("Harbour", [ids[0]]);
        // A pinned cover too, so the active star is part of the probe.
        var page = await OpenAsync(site, $"/admin/galleries/{gid}");
        await NavigatesAsync(page, () => Star(page, 1).ClickAsync());
        Assert.Equal(ids[1], (await site.GalleryDocAsync(gid)).PinnedCover);

        var rest = await OpenAsync(site, $"/admin/galleries/{gid}");
        Assert.Equal(["Save"], await rest.EvaluateAsync<string[]>(FilledButtonsJs));
    }
}
