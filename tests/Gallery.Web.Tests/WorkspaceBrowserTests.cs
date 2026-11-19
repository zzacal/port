using System.Text.Json;
using Gallery.Web.Library;
using Gallery.Web.Tests.Fixtures;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using MongoDB.Driver;

namespace Gallery.Web.Tests;

/// <summary>
/// 020 T039 (Batch C2a: workspace.js parts 1 and 2, T036/T037; contracts/components.md C12): the
/// workspace at /admin in a real browser at 1440 × 900. Navigation swaps the main fragment and keeps
/// the collection over back, forward and reload; Show more and the sentinel append chunks;
/// selection (click, Ctrl/⌘, select mark, Shift range, Ctrl/⌘+A to the cap, Escape, empty space,
/// arrow keys and Space); SC-004 (click → filled panel ≤ 100 ms, no request); save on leave (A1, A5)
/// and its refusal; a swapped-in tile is styled like a full load (R-01); only the select mark is
/// drawn on a photo; public pages load no admin asset. Part 3's facts (checklist, drag, move,
/// cover, Public, delete, new gallery) arrive with T038.
/// </summary>
[Collection("library-browser")]
[Trait("Category", "Browser")]
public sealed class WorkspaceBrowserTests(LibraryBrowserFixture fx)
{
    private const float WaitMs = 15_000;

    // ── shared helpers ─────────────────────────────────────────────────────────────────────

    /// <summary>A new owner page at <paramref name="path"/>, loaded, with workspace.js upgraded.</summary>
    private static async Task<IPage> OpenAsync(LibrarySite site, string path, string? initScript = null)
    {
        var ctx = await site.NewContextAsync();
        if (initScript is not null) await ctx.AddInitScriptAsync(initScript);
        var page = await ctx.NewPageAsync();
        await page.Mouse.MoveAsync(0, 0);
        var resp = await page.GotoAsync(site.BaseUrl + path);
        Assert.Equal(200, resp!.Status);
        await BrowserWait.LoadedAsync(page, WaitMs);
        Assert.Equal(path, new Uri(page.Url).AbsolutePath);
        await ReadyAsync(page);
        return page;
    }

    private static Task ReadyAsync(IPage page) =>
        page.Locator("admin-workspace[data-script='ready']").WaitForAsync(new() { State = WaitForSelectorState.Attached, Timeout = WaitMs });

    private static ILocator Cells(IPage page) => page.Locator(".ws-main .ws-cell");

    private static Task<string[]> CellIdsAsync(IPage page) =>
        page.EvaluateAsync<string[]>("() => [...document.querySelectorAll('.ws-main .ws-cell')].map(c => c.dataset.id)");

    private static Task<string[]> SelectedAsync(IPage page) =>
        page.EvaluateAsync<string[]>("() => [...document.querySelectorAll('.ws-main .ws-cell[aria-selected=\"true\"]')].map(c => c.dataset.id)");

    private static Task<string> PanelHeadingAsync(IPage page) =>
        page.EvaluateAsync<string>("() => document.querySelector('.ws-panel > .side-panel > h2')?.textContent.trim() ?? '(no panel)'");

    private static Task<string> HeadingAsync(IPage page) =>
        page.EvaluateAsync<string>("() => document.querySelector('.ws-main .ws-head h1')?.textContent.trim() ?? '(no heading)'");

    private static Task<string> ToastAsync(IPage page) =>
        page.EvaluateAsync<string>("() => document.querySelector('.ws-panel .ws-toast').textContent");

    private static Task<string> CurrentRowAsync(IPage page) =>
        page.EvaluateAsync<string>("() => [...document.querySelectorAll('.ws-list a[aria-current=\"page\"]')].map(a => a.getAttribute('href')).join(',')");

    /// <summary>The number of document loads in this tab: 1 means every change so far was a swap.</summary>
    private static Task<int> DocumentLoadsAsync(IPage page) =>
        page.EvaluateAsync<int>("() => performance.getEntriesByType('navigation').length + (window.__wsMark === 1 ? 0 : 100)");

    private static Task MarkDocumentAsync(IPage page) => page.EvaluateAsync("() => { window.__wsMark = 1; }");

    private static async Task WaitForAsync(IPage page, string predicateJs, object? arg = null, string? what = null)
    {
        try
        {
            await page.WaitForFunctionAsync(predicateJs, arg, new() { Timeout = WaitMs, PollingInterval = 25 });
        }
        catch (TimeoutException)
        {
            throw new TimeoutException($"waited {WaitMs} ms for {what ?? predicateJs} at {page.Url}");
        }
    }

    private static Task WaitForToastAsync(IPage page) =>
        WaitForAsync(page, "() => document.querySelector('.ws-panel .ws-toast').textContent.length > 0", what: "a toast");

    private static Task WaitForCellsAsync(IPage page, int n) =>
        WaitForAsync(page, "n => document.querySelectorAll('.ws-main .ws-cell').length === n", n, $"{n} cells");

    /// <summary>Requests to the app (not the image host) made while <paramref name="action"/> runs.</summary>
    private static async Task<List<string>> AppRequestsDuring(LibrarySite site, IPage page, Func<Task> action)
    {
        var seen = new List<string>();
        void OnRequest(object? _, IRequest r)
        {
            if (r.Url.StartsWith(site.BaseUrl, StringComparison.Ordinal)) lock (seen) seen.Add($"{r.Method} {r.Url}");
        }
        page.Request += OnRequest;
        try
        {
            await action();
        }
        finally
        {
            page.Request -= OnRequest;
        }
        lock (seen) return seen.ToList();
    }

    /// <summary>The server's own refusal sentence for <paramref name="title"/> (GalleryService.TitleProblem).</summary>
    private static string TitleProblem(LibrarySite site, string title) =>
        GalleryService.TitleProblem(title, site.Services.GetRequiredService<LibraryOptions>())
        ?? throw new InvalidOperationException($"'{title}' is a valid title");

    // ── fixture guard ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Fixture_guard_workspace_is_styled_and_workspace_js_is_ready()
    {
        await using var site = await fx.NewSiteAsync();
        await site.ImagesAsync(2);
        var page = await OpenAsync(site, "/admin");

        var css = await page.EvaluateAsync<int>("() => (document.head.querySelector('style')?.textContent || '').length");
        Assert.True(css > 2000, $"the inlined stylesheet is {css} characters; the fixture lost the scoped CSS");
        Assert.Equal("grid", await page.Locator(".ws-main .cs").EvaluateAsync<string>("el => getComputedStyle(el).display"));
        Assert.Equal("grid", await page.Locator("admin-workspace").EvaluateAsync<string>("el => getComputedStyle(el).display"));
        Assert.Equal("Library", await PanelHeadingAsync(page));
    }

    // ── T036: navigation, back/forward/reload ──────────────────────────────────────────────

    [Fact]
    public async Task Collection_links_swap_main_and_back_forward_reload_keep_the_collection()
    {
        await using var site = await fx.NewSiteAsync();
        var ids = await site.ImagesAsync(3);
        var gid = await site.GalleryAsync("Coast", [ids[0]]);
        var page = await OpenAsync(site, "/admin");
        await MarkDocumentAsync(page);
        Assert.Equal("/admin", await CurrentRowAsync(page));

        await page.Locator($".ws-list a[data-gid='{gid}']").ClickAsync();
        await WaitForAsync(page, "() => location.pathname.startsWith('/admin/g/') && document.querySelector('.ws-main .ws-head h1')?.textContent === 'Coast'", what: "the gallery swapped in");
        Assert.Equal($"/admin/g/{gid}", new Uri(page.Url).AbsolutePath);
        Assert.Equal("Coast", await page.TitleAsync());
        Assert.Equal($"/admin/g/{gid}", await CurrentRowAsync(page));
        Assert.Equal([ids[0]], await CellIdsAsync(page));
        Assert.Equal("Gallery", await PanelHeadingAsync(page));

        await page.Locator(".ws-list a[href='/admin/unplaced']").ClickAsync();
        await WaitForAsync(page, "() => location.pathname === '/admin/unplaced' && document.querySelector('.ws-main .ws-head h1')?.textContent === 'Not in a gallery'", what: "Not in a gallery swapped in");
        Assert.Equal([ids[2], ids[1]], await CellIdsAsync(page));
        Assert.Equal("/admin/unplaced", await CurrentRowAsync(page));
        Assert.Equal(1, await DocumentLoadsAsync(page));

        await page.GoBackAsync();
        await WaitForAsync(page, "() => document.querySelector('.ws-main .ws-head h1')?.textContent === 'Coast'", what: "back to the gallery");
        Assert.Equal($"/admin/g/{gid}", new Uri(page.Url).AbsolutePath);
        Assert.Equal($"/admin/g/{gid}", await CurrentRowAsync(page));
        Assert.Equal([ids[0]], await CellIdsAsync(page));
        Assert.Equal(1, await DocumentLoadsAsync(page));

        await page.GoForwardAsync();
        await WaitForAsync(page, "() => document.querySelector('.ws-main .ws-head h1')?.textContent === 'Not in a gallery'", what: "forward to Not in a gallery");
        Assert.Equal("/admin/unplaced", new Uri(page.Url).AbsolutePath);
        Assert.Equal(1, await DocumentLoadsAsync(page));

        await page.ReloadAsync();
        await BrowserWait.LoadedAsync(page, WaitMs);
        await ReadyAsync(page);
        Assert.Equal("/admin/unplaced", new Uri(page.Url).AbsolutePath);
        Assert.Equal("Not in a gallery", await HeadingAsync(page));
        Assert.Equal("/admin/unplaced", await CurrentRowAsync(page));
        Assert.Equal([ids[2], ids[1]], await CellIdsAsync(page));
    }

    [Fact]
    public async Task A_refused_navigation_shows_a_refusal_and_leaves_the_page_as_it_was()
    {
        await using var site = await fx.NewSiteAsync();
        var ids = await site.ImagesAsync(2);
        await site.GalleryAsync("Coast", [ids[0]]);
        var page = await OpenAsync(site, "/admin");
        await page.RouteAsync(site.BaseUrl + "/admin/unplaced", r => r.FulfillAsync(new() { Status = 503, Body = "" }));

        await page.Locator(".ws-list a[href='/admin/unplaced']").ClickAsync();
        await WaitForToastAsync(page);

        Assert.True(await page.Locator(".ws-panel .ws-toast").EvaluateAsync<bool>("el => el.classList.contains('ws-refusal')"));
        Assert.Equal("/admin", new Uri(page.Url).AbsolutePath);
        Assert.Equal("All photos", await HeadingAsync(page));
        Assert.Equal("/admin", await CurrentRowAsync(page));
        Assert.Equal([ids[1], ids[0]], await CellIdsAsync(page));
    }

    // ── T036: Show more and the sentinel ───────────────────────────────────────────────────

    [Fact]
    public async Task Show_more_appends_the_next_chunk_until_the_last()
    {
        await using var site = await fx.NewSiteAsync(new() { ["Library:PageSize"] = "4" });
        var ids = await site.ImagesAsync(10);
        var newestFirst = Enumerable.Reverse(ids).ToArray();
        // No IntersectionObserver: only the button loads more, so each click is one chunk.
        var page = await OpenAsync(site, "/admin", "delete window.IntersectionObserver;");

        Assert.Equal(newestFirst[..4], await CellIdsAsync(page));
        await page.Locator(".ws-main .ws-more").ClickAsync();
        await WaitForCellsAsync(page, 8);
        Assert.Equal(newestFirst[..8], await CellIdsAsync(page));
        Assert.Equal(1, await page.Locator(".ws-main span[data-next]").CountAsync());

        await page.Locator(".ws-main .ws-more").ClickAsync();
        await WaitForCellsAsync(page, 10);
        Assert.Equal(newestFirst, await CellIdsAsync(page));
        Assert.Equal(0, await page.Locator(".ws-main .ws-more").CountAsync());
        Assert.Equal(0, await page.Locator(".ws-main span[data-next]").CountAsync());
    }

    [Fact]
    public async Task The_sentinel_in_view_loads_every_chunk()
    {
        await using var site = await fx.NewSiteAsync(new() { ["Library:PageSize"] = "4" });
        var ids = await site.ImagesAsync(10);
        var page = await OpenAsync(site, "/admin");

        await WaitForCellsAsync(page, 10);
        Assert.Equal(Enumerable.Reverse(ids).ToArray(), await CellIdsAsync(page));
        Assert.Equal(0, await page.Locator(".ws-main .ws-more").CountAsync());
        Assert.Equal(0, await page.Locator(".ws-main span[data-next]").CountAsync());
    }

    // ── T037: selection ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Click_selects_one_ctrl_and_the_mark_toggle_shift_selects_the_range()
    {
        await using var site = await fx.NewSiteAsync();
        var ids = await site.ImagesAsync(6);
        var page = await OpenAsync(site, "/admin");
        var order = await CellIdsAsync(page);
        Assert.Equal(Enumerable.Reverse(ids).ToArray(), order);

        await Cells(page).Nth(1).ClickAsync();
        Assert.Equal([order[1]], await SelectedAsync(page));
        Assert.Equal("Photo", await PanelHeadingAsync(page));
        Assert.False(await page.Locator(".ws-main .ws-clear").EvaluateAsync<bool>("el => el.hidden"));

        await Cells(page).Nth(3).ClickAsync(new() { Modifiers = [KeyboardModifier.ControlOrMeta] });
        Assert.Equal([order[1], order[3]], await SelectedAsync(page));
        Assert.Equal("2 photos selected", await PanelHeadingAsync(page));

        await Cells(page).Nth(5).Locator("label.select-mark").ClickAsync();
        Assert.Equal([order[1], order[3], order[5]], await SelectedAsync(page));
        Assert.True(await Cells(page).Nth(5).Locator("label.select-mark input").IsCheckedAsync());

        await Cells(page).Nth(3).Locator("label.select-mark").ClickAsync();
        Assert.Equal([order[1], order[5]], await SelectedAsync(page));
        Assert.False(await Cells(page).Nth(3).Locator("label.select-mark input").IsCheckedAsync());

        await Cells(page).Nth(1).ClickAsync();
        Assert.Equal([order[1]], await SelectedAsync(page));
        await Cells(page).Nth(4).ClickAsync(new() { Modifiers = [KeyboardModifier.Shift] });
        Assert.Equal(order[1..5], await SelectedAsync(page));
        Assert.Equal("4 photos selected", await PanelHeadingAsync(page));
    }

    [Fact]
    public async Task Ctrl_A_selects_up_to_the_cap_and_says_so()
    {
        await using var site = await fx.NewSiteAsync(new() { ["Library:PageSize"] = "4" });
        await site.ImagesAsync(6);
        var page = await OpenAsync(site, "/admin");
        await WaitForCellsAsync(page, 6);
        var order = await CellIdsAsync(page);

        await page.Keyboard.PressAsync("ControlOrMeta+A");
        Assert.Equal(order[..4], await SelectedAsync(page));
        Assert.Equal("You can select up to 4 photos at a time.", await ToastAsync(page));
        Assert.Equal("4 photos selected", await PanelHeadingAsync(page));

        await page.EvaluateAsync("() => { const t = document.querySelector('.ws-panel .ws-toast'); t.textContent = ''; }");
        await Cells(page).Nth(5).ClickAsync(new() { Modifiers = [KeyboardModifier.ControlOrMeta] });
        Assert.Equal(order[..4], await SelectedAsync(page));
        Assert.Equal("You can select up to 4 photos at a time.", await ToastAsync(page));
    }

    [Fact]
    public async Task Escape_and_a_click_on_empty_space_clear_the_selection()
    {
        await using var site = await fx.NewSiteAsync();
        await site.ImagesAsync(3);
        var page = await OpenAsync(site, "/admin");

        await Cells(page).Nth(0).ClickAsync();
        await Cells(page).Nth(2).ClickAsync(new() { Modifiers = [KeyboardModifier.ControlOrMeta] });
        Assert.Equal(2, (await SelectedAsync(page)).Length);
        await page.Keyboard.PressAsync("Escape");
        Assert.Empty(await SelectedAsync(page));
        Assert.Equal("Library", await PanelHeadingAsync(page));
        Assert.True(await page.Locator(".ws-main .ws-clear").EvaluateAsync<bool>("el => el.hidden"));

        await Cells(page).Nth(1).ClickAsync();
        Assert.Single(await SelectedAsync(page));
        var main = (await page.Locator(".ws-main").BoundingBoxAsync())!;
        var grid = (await page.Locator(".ws-main .cs").BoundingBoxAsync())!;
        Assert.True(main.Y + main.Height - (grid.Y + grid.Height) > 40, "no empty space under the grid to click");
        await page.Mouse.ClickAsync(main.X + main.Width / 2, main.Y + main.Height - 20);
        Assert.Empty(await SelectedAsync(page));
        Assert.Equal("Library", await PanelHeadingAsync(page));
    }

    [Fact]
    public async Task Arrow_keys_move_the_one_tab_stop_and_Space_toggles()
    {
        await using var site = await fx.NewSiteAsync();
        await site.ImagesAsync(3);
        var page = await OpenAsync(site, "/admin");
        var order = await CellIdsAsync(page);
        var tabStops = "() => [...document.querySelectorAll('.ws-main .ws-cell')].filter(c => c.tabIndex === 0).map(c => c.dataset.id)";

        Assert.Equal([order[0]], await page.EvaluateAsync<string[]>(tabStops));
        await Cells(page).Nth(0).FocusAsync();
        await page.Keyboard.PressAsync("ArrowRight");
        Assert.Equal(order[1], await page.EvaluateAsync<string>("() => document.activeElement.dataset.id"));
        Assert.Equal([order[1]], await page.EvaluateAsync<string[]>(tabStops));
        await page.Keyboard.PressAsync("Space");
        Assert.Equal([order[1]], await SelectedAsync(page));
        await page.Keyboard.PressAsync("ArrowRight");
        await page.Keyboard.PressAsync("Space");
        Assert.Equal([order[1], order[2]], await SelectedAsync(page));
    }

    // ── SC-004: click → filled panel ≤ 100 ms, no request ──────────────────────────────────

    [Fact]
    public async Task Selecting_a_photo_fills_the_panel_within_100_ms_with_no_request()
    {
        await using var site = await fx.NewSiteAsync();
        var ids = await site.ImagesAsync(6);
        await site.Images.UpdateOneAsync(i => i.Id == ids[3], Builders<Gallery.Web.Data.ImageDoc>.Update.Set(i => i.Caption, "Low tide"));
        var page = await OpenAsync(site, "/admin");
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        var times = new List<double>();
        var requests = await AppRequestsDuring(site, page, async () =>
        {
            for (var i = 0; i < 5; i++)
            {
                var ms = await page.EvaluateAsync<double>("""
                    async (i) => {
                      const cell = document.querySelectorAll('.ws-main .ws-cell')[i];
                      const filled = () => {
                        const p = document.querySelector('.ws-panel > .side-panel');
                        return p && p.querySelector('h2').textContent === 'Photo'
                          && p.querySelector('input[name="title"]').value === cell.dataset.title
                          && p.querySelector('textarea[name="caption"]').value === cell.dataset.caption;
                      };
                      const t0 = performance.now();
                      cell.querySelector('.cell').dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true, button: 0 }));
                      while (!filled()) await new Promise(r => requestAnimationFrame(r));
                      await new Promise(r => requestAnimationFrame(r));
                      return performance.now() - t0;
                    }
                    """, i);
                times.Add(ms);
            }
        });

        var median = times.Order().ElementAt(2);
        Assert.True(median <= 100, $"median click → filled panel {median:F1} ms (all: {string.Join(", ", times.Select(t => t.ToString("F1")))})");
        Assert.Empty(requests);
        // Newest first: the fifth click (index 4) opened ids[1]; index 2 is ids[3], whose caption was seeded.
        Assert.Equal([ids[1]], await SelectedAsync(page));
        await Cells(page).Nth(2).ClickAsync();
        Assert.Equal("Low tide", await page.Locator(".ws-panel textarea[name='caption']").InputValueAsync());
    }

    // ── T037: save on leave (FR-013) and its refusal ───────────────────────────────────────

    [Fact]
    public async Task Leaving_a_changed_title_or_caption_saves_it_with_A1()
    {
        await using var site = await fx.NewSiteAsync();
        await site.ImagesAsync(3);
        var page = await OpenAsync(site, "/admin");
        var order = await CellIdsAsync(page);

        await Cells(page).Nth(0).ClickAsync();
        await page.Locator(".ws-panel input[name='title']").FillAsync("Harbour at dawn");
        await Cells(page).Nth(1).ClickAsync();
        await WaitForAsync(page, "id => document.querySelector(`.ws-main .ws-cell[data-id=\"${id}\"]`).dataset.title === 'Harbour at dawn'", order[0], "the cell's new title");

        Assert.Equal("Saved", await ToastAsync(page));
        Assert.Equal("Harbour at dawn", (await site.Images.Find(i => i.Id == order[0]).FirstAsync()).Title);
        Assert.Equal("Harbour at dawn", await Cells(page).Nth(0).Locator(".title").TextContentAsync());
        Assert.Equal([order[1]], await SelectedAsync(page));

        await page.Locator(".ws-panel textarea[name='caption']").FillAsync("Low tide, north end");
        await page.Keyboard.PressAsync("Tab");
        await WaitForAsync(page, "() => document.querySelector('.ws-panel textarea[name=\"caption\"]').closest('.field').querySelector('.msg').textContent === 'Saved'", what: "the caption's Saved");
        Assert.Equal("Low tide, north end", (await site.Images.Find(i => i.Id == order[1]).FirstAsync()).Caption);
        Assert.Equal("Low tide, north end", await Cells(page).Nth(1).GetAttributeAsync("data-caption"));

        // The panel re-opens from the cell with the stored values (no request).
        await Cells(page).Nth(0).ClickAsync();
        Assert.Equal("Harbour at dawn", await page.Locator(".ws-panel input[name='title']").InputValueAsync());
    }

    [Fact]
    public async Task A_blank_title_is_refused_keeping_the_typed_text_and_the_cell_title()
    {
        await using var site = await fx.NewSiteAsync();
        var ids = await site.ImagesAsync(2);
        var page = await OpenAsync(site, "/admin");
        var order = await CellIdsAsync(page);
        var before = (await site.Images.Find(i => i.Id == order[0]).FirstAsync()).Title;
        var reason = TitleProblem(site, "   ");

        await Cells(page).Nth(0).ClickAsync();
        var title = page.Locator(".ws-panel input[name='title']");
        await title.FillAsync("   ");
        await page.Keyboard.PressAsync("Tab");
        await WaitForAsync(page, "() => document.querySelector('.ws-panel input[name=\"title\"]').closest('.field').querySelector('.msg').classList.contains('msg-invalid')", what: "the refusal under the field");

        Assert.Equal(reason, await title.EvaluateAsync<string>("el => el.closest('.field').querySelector('.msg').textContent"));
        Assert.Equal(reason, await ToastAsync(page));
        Assert.Equal("   ", await title.InputValueAsync());
        Assert.Equal(before, await Cells(page).Nth(0).GetAttributeAsync("data-title"));
        Assert.Equal(before, (await site.Images.Find(i => i.Id == order[0]).FirstAsync()).Title);
        Assert.Equal(ids[1], order[0]);
    }

    [Fact]
    public async Task Leaving_the_gallery_title_or_description_saves_it_with_A5_and_refreshes_the_list()
    {
        await using var site = await fx.NewSiteAsync();
        var ids = await site.ImagesAsync(2);
        var gid = await site.GalleryAsync("Coast", ids);
        var page = await OpenAsync(site, $"/admin/g/{gid}");
        Assert.Equal("Gallery", await PanelHeadingAsync(page));

        await page.Locator(".ws-panel input[name='title']").FillAsync("Coast north");
        await page.Keyboard.PressAsync("Tab");
        await WaitForAsync(page, "gid => document.querySelector(`.ws-list a[data-gid=\"${gid}\"] .ws-coll-name`)?.textContent === 'Coast north'", gid, "the list row's new title");
        Assert.Equal("Saved", await ToastAsync(page));
        Assert.Equal("Coast north", (await site.GalleryDocAsync(gid)).Title);
        Assert.Equal("Coast north", await HeadingAsync(page));
        Assert.Equal($"/admin/g/{gid}", await CurrentRowAsync(page));

        await page.Locator(".ws-panel textarea[name='description']").FillAsync("Rocks and gulls.");
        await page.Keyboard.PressAsync("Tab");
        await WaitForAsync(page, "() => document.querySelector('.ws-panel textarea[name=\"description\"]').closest('.field').querySelector('.msg').textContent === 'Saved'", what: "the description's Saved");
        Assert.Equal("Rocks and gulls.", (await site.GalleryDocAsync(gid)).Description);

        // Refusal: a blank gallery title keeps the typed text; the list and heading keep the stored title.
        var reason = TitleProblem(site, "");
        var title = page.Locator(".ws-panel input[name='title']");
        await title.FillAsync("");
        await page.Keyboard.PressAsync("Tab");
        await WaitForAsync(page, "() => document.querySelector('.ws-panel input[name=\"title\"]').closest('.field').querySelector('.msg').classList.contains('msg-invalid')", what: "the refusal under the field");
        Assert.Equal(reason, await title.EvaluateAsync<string>("el => el.closest('.field').querySelector('.msg').textContent"));
        Assert.Equal("", await title.InputValueAsync());
        Assert.Equal("Coast north", (await site.GalleryDocAsync(gid)).Title);
        Assert.Equal("Coast north", await HeadingAsync(page));
    }

    // ── R-01: a swapped-in tile is styled ──────────────────────────────────────────────────

    private const string TileStyleJs = """
        () => {
          const cell = document.querySelector('.ws-main .ws-cell');
          const img = cell.querySelector('img.ph');
          const s = getComputedStyle(img);
          const b = img.getBoundingClientRect();
          const c = cell.querySelector('.cell').getBoundingClientRect();
          return [s.objectFit, s.position, String(Math.round(b.width)), String(Math.round(b.height)),
                  String(Math.round(c.width)), String(Math.round(c.height)),
                  getComputedStyle(cell.parentElement).gridTemplateColumns,
                  getComputedStyle(cell.querySelector('.ws-cell-sel')).position];
        }
        """;

    [Fact]
    public async Task Swapped_tile_is_styled()
    {
        await using var site = await fx.NewSiteAsync();
        var ids = await site.ImagesAsync(3);
        var gid = await site.GalleryAsync("Coast", ids);
        var page = await OpenAsync(site, "/admin");
        var full = await page.EvaluateAsync<string[]>(TileStyleJs);
        Assert.Equal("absolute", full[1]);
        Assert.Equal("absolute", full[7]);

        await page.Locator($".ws-list a[data-gid='{gid}']").ClickAsync();
        await WaitForAsync(page, "() => document.querySelector('.ws-main .ws-head h1')?.textContent === 'Coast'", what: "the gallery swapped in");
        Assert.Equal(full, await page.EvaluateAsync<string[]>(TileStyleJs));

        await page.Locator(".ws-list a[href='/admin']").First.ClickAsync();
        await WaitForAsync(page, "() => document.querySelector('.ws-main .ws-head h1')?.textContent === 'All photos'", what: "All photos swapped in");
        Assert.Equal(full, await page.EvaluateAsync<string[]>(TileStyleJs));
    }

    // ── one glyph on a photo ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Only_the_select_mark_is_drawn_on_a_photo()
    {
        await using var site = await fx.NewSiteAsync();
        var ids = await site.ImagesAsync(3);
        await site.GalleryAsync("Coast", ids, publish: true);
        var page = await OpenAsync(site, "/admin");
        await Cells(page).Nth(0).ClickAsync();

        var hits = await page.EvaluateAsync<string[]>("""
            () => [...document.querySelectorAll('.ws-main .ws-cell')].flatMap((cell, i) => {
              const c = cell.querySelector('.cell').getBoundingClientRect();
              return [...document.querySelectorAll('a, button, input:not(.vh-input), textarea, select, label, [role="switch"], [tabindex="0"]')]
                .filter(el => el !== cell && el.getClientRects().length > 0)
                .filter(el => { const e = el.getBoundingClientRect(); return e.left < c.right && e.right > c.left && e.top < c.bottom && e.bottom > c.top; })
                .map(el => `${i}: ${el.matches('label.select-mark') ? 'select-mark' : (el.getAttribute('aria-label') || el.textContent.trim() || el.tagName)}`);
            })
            """);
        Assert.Equal(["0: select-mark", "1: select-mark", "2: select-mark"], hits);
    }

    // ── FR-003: no owner script on public pages ────────────────────────────────────────────

    [Fact]
    public async Task Public_pages_load_no_admin_assets_even_for_the_owner()
    {
        await using var site = await fx.NewSiteAsync();
        var ids = await site.ImagesAsync(2);
        var gid = await site.GalleryAsync("Coast", ids, publish: true);
        var slug = (await site.GalleryDocAsync(gid)).Slug;

        foreach (var path in new[] { "/", "/g", $"/g/{gid}/{slug}", $"/g/{gid}/{slug}/{ids[0]}" })
        {
            var ctx = await site.NewContextAsync();
            var page = await ctx.NewPageAsync();
            var requested = new List<string>();
            page.Request += (_, r) => { lock (requested) requested.Add(r.Url); };
            await page.Mouse.MoveAsync(0, 0);
            var resp = await page.GotoAsync(site.BaseUrl + path, new() { WaitUntil = WaitUntilState.NetworkIdle });
            Assert.Equal(200, resp!.Status);
            Assert.Equal(0, await page.Locator("script[src*='/admin-assets/']").CountAsync());
            Assert.Equal(0, await page.Locator("admin-workspace").CountAsync());
            lock (requested) Assert.DoesNotContain(requested, u => u.Contains("/admin-assets/", StringComparison.Ordinal));
        }
    }
}
