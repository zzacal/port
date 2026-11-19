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

    // ── T038 (Batch C2b): part 3 helpers ───────────────────────────────────────────────────

    private static ILocator Cell(IPage page, string id) => page.Locator($".ws-main .ws-cell[data-id='{id}']");

    private static ILocator ListRow(IPage page, string gid) => page.Locator($".ws-list a[data-gid='{gid}']");

    /// <summary>The checklist row (its label) for <paramref name="gid"/> in the open panel.</summary>
    private static ILocator CheckRow(IPage page, string gid) =>
        page.Locator($".ws-panel > .side-panel label.ws-checks-row:has(input[data-member='{gid}'])");

    private static ILocator CheckBox(IPage page, string gid) =>
        page.Locator($".ws-panel > .side-panel input[data-member='{gid}']");

    private static ILocator FindField(IPage page) => page.Locator(".ws-panel > .side-panel [data-gq]");

    private static Task WaitForToastTextAsync(IPage page, string text) =>
        WaitForAsync(page, "t => document.querySelector('.ws-panel .ws-toast').textContent === t", text, $"the toast \"{text}\"");

    private static Task ClearToastAsync(IPage page) =>
        page.EvaluateAsync("() => { document.querySelector('.ws-panel .ws-toast').textContent = ''; }");

    private static Task<string[]> ListOrderAsync(IPage page) =>
        page.EvaluateAsync<string[]>("() => [...document.querySelectorAll('.ws-list a[data-gid]')].map(a => a.dataset.gid)");

    private static Task<string> ListRowTextAsync(IPage page, string gid) =>
        ListRow(page, gid).EvaluateAsync<string>("a => [...a.children].map(c => c.textContent.trim()).join(' ')");

    private static async Task<List<string>> ItemsAsync(LibrarySite site, string gid) => (await site.GalleryDocAsync(gid)).Items;

    private static List<string> Sorted(IEnumerable<string> ids) => ids.Order(StringComparer.Ordinal).ToList();

    /// <summary>The galleries of a cell as the page holds them (data-galleries), sorted.</summary>
    private static async Task<List<string>> CellGalleriesAsync(IPage page, string id) =>
        Sorted((await Cell(page, id).GetAttributeAsync("data-galleries"))!.Split(' ', StringSplitOptions.RemoveEmptyEntries));

    /// <summary>An anonymous visitor's page at <paramref name="path"/> (no owner cookie).</summary>
    private static async Task<(IPage Page, int Status)> VisitorAsync(LibrarySite site, string path)
    {
        var ctx = await site.NewContextAsync(owner: false);
        var page = await ctx.NewPageAsync();
        await page.Mouse.MoveAsync(0, 0);
        var resp = await page.GotoAsync(site.BaseUrl + path);
        await BrowserWait.LoadedAsync(page, WaitMs);
        return (page, resp!.Status);
    }

    // ── US1 independent test (SC-001, SC-003) ──────────────────────────────────────────────

    [Fact]
    public async Task Us1_retitle_three_add_four_to_an_existing_and_a_typed_gallery_drag_two_in_one_document()
    {
        await using var site = await fx.NewSiteAsync();
        var ids = await site.ImagesAsync(12);
        var coast = await site.GalleryAsync("Coast", [ids[0]]);
        var birds = await site.GalleryAsync("Birds", [ids[1]]);
        var desert = await site.GalleryAsync("Desert", [ids[2]]);
        var page = await OpenAsync(site, "/admin");
        await MarkDocumentAsync(page);
        var order = await CellIdsAsync(page);
        Assert.Equal(Enumerable.Reverse(ids).ToArray(), order);

        // Retitle three photos (A1 on leaving the field).
        string[] titles = ["Harbour at dawn", "Low tide", "Gulls"];
        for (var i = 0; i < 3; i++)
        {
            await Cell(page, order[i]).ClickAsync();
            await page.Locator(".ws-panel input[name='title']").FillAsync(titles[i]);
            await page.Keyboard.PressAsync("Tab");
            await WaitForAsync(page, "({ id, t }) => document.querySelector(`.ws-main .ws-cell[data-id=\"${id}\"]`).dataset.title === t",
                new { id = order[i], t = titles[i] }, $"title {i} saved");
        }

        // Four onto an existing gallery by ticking it.
        await Cell(page, order[3]).ClickAsync();
        await Cell(page, order[6]).ClickAsync(new() { Modifiers = [KeyboardModifier.Shift] });
        Assert.Equal(order[3..7], await SelectedAsync(page));
        await CheckRow(page, birds).ClickAsync();
        await WaitForToastTextAsync(page, Gallery.Web.Workspace.Messages.Added(4, 0, "Birds", false));

        // Four into a new gallery typed in the find field.
        await Cell(page, order[7]).ClickAsync();
        await Cell(page, order[10]).ClickAsync(new() { Modifiers = [KeyboardModifier.Shift] });
        Assert.Equal(order[7..11], await SelectedAsync(page));
        await FindField(page).FillAsync("Harbour");
        await page.Keyboard.PressAsync("Enter");
        await WaitForToastTextAsync(page, Gallery.Web.Workspace.Messages.Created("Harbour", 4));

        // Two dragged onto a gallery's name.
        await Cell(page, order[0]).ClickAsync();
        await Cell(page, order[1]).ClickAsync(new() { Modifiers = [KeyboardModifier.ControlOrMeta] });
        await Cell(page, order[0]).DragToAsync(ListRow(page, desert));
        await WaitForToastTextAsync(page, Gallery.Web.Workspace.Messages.Added(2, 0, "Desert", false));
        await WaitForAsync(page, "gid => document.querySelector(`.ws-list a[data-gid=\"${gid}\"] .ws-coll-count`)?.textContent === '3'", desert, "Desert's count 3");

        Assert.Equal(1, await DocumentLoadsAsync(page));

        // Reload: every change was stored.
        await page.ReloadAsync();
        await BrowserWait.LoadedAsync(page, WaitMs);
        await ReadyAsync(page);
        Assert.Equal("All photos", await HeadingAsync(page));
        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(titles[i], await Cell(page, order[i]).GetAttributeAsync("data-title"));
            Assert.Equal(titles[i], (await site.Images.Find(x => x.Id == order[i]).FirstAsync()).Title);
        }
        var harbour = (await site.Galleries.Find(g => g.Title == "Harbour").ToListAsync()).Single();
        Assert.Equal("private", harbour.Visibility);
        Assert.Equal(Sorted(order[7..11]), Sorted(harbour.Items));
        Assert.Equal(Sorted([ids[1], .. order[3..7]]), Sorted(await ItemsAsync(site, birds)));
        Assert.Equal(Sorted([ids[2], order[0], order[1]]), Sorted(await ItemsAsync(site, desert)));
        Assert.Equal([ids[0]], await ItemsAsync(site, coast));
        Assert.Equal(4, await site.Galleries.CountDocumentsAsync(FilterDefinition<Gallery.Web.Data.GalleryDoc>.Empty));
        Assert.Equal([coast, birds, desert, harbour.Id], await ListOrderAsync(page));
        Assert.Equal("Harbour Private 4", await ListRowTextAsync(page, harbour.Id));
        Assert.Equal("Birds Private 5", await ListRowTextAsync(page, birds));
        Assert.Equal(Sorted([desert]), await CellGalleriesAsync(page, order[0]));
        Assert.Equal(Sorted([harbour.Id]), await CellGalleriesAsync(page, order[7]));
    }

    // ── the checklist (K6) ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_gallery_holding_some_of_the_selection_is_partly_ticked_and_ticking_adds_the_rest()
    {
        await using var site = await fx.NewSiteAsync();
        var ids = await site.ImagesAsync(3);
        var coast = await site.GalleryAsync("Coast", [ids[0]]);
        var birds = await site.GalleryAsync("Birds", []);
        var page = await OpenAsync(site, "/admin");
        const string State = "b => [b.getAttribute('aria-checked') ?? '-', String(b.indeterminate), String(b.checked)]";

        await Cell(page, ids[0]).ClickAsync();
        await Cell(page, ids[1]).ClickAsync(new() { Modifiers = [KeyboardModifier.ControlOrMeta] });
        Assert.Equal("2 photos selected", await PanelHeadingAsync(page));
        Assert.Equal(["mixed", "true", "false"], await CheckBox(page, coast).EvaluateAsync<string[]>(State));
        Assert.Equal(["-", "false", "false"], await CheckBox(page, birds).EvaluateAsync<string[]>(State));

        await CheckRow(page, coast).ClickAsync();
        await WaitForToastTextAsync(page, Gallery.Web.Workspace.Messages.Added(1, 1, "Coast", false));
        await WaitForAsync(page, "gid => document.querySelector(`.ws-panel > .side-panel input[data-member=\"${gid}\"]`)?.checked === true", coast, "Coast ticked");
        Assert.Equal(["-", "false", "true"], await CheckBox(page, coast).EvaluateAsync<string[]>(State));
        Assert.Equal(Sorted([ids[0], ids[1]]), Sorted(await ItemsAsync(site, coast)));
        Assert.Equal([coast], await CellGalleriesAsync(page, ids[1]));
        await WaitForAsync(page, "gid => document.querySelector(`.ws-list a[data-gid=\"${gid}\"] .ws-coll-count`)?.textContent === '2'", coast, "Coast's count 2");
        Assert.Equal(["2 photos selected"], [await PanelHeadingAsync(page)]);
    }

    [Fact]
    public async Task Typing_a_new_name_and_Enter_creates_a_private_gallery_with_the_selection_and_keeps_focus()
    {
        await using var site = await fx.NewSiteAsync();
        var ids = await site.ImagesAsync(3);
        var coast = await site.GalleryAsync("Coast", []);
        var page = await OpenAsync(site, "/admin");

        await Cell(page, ids[0]).ClickAsync();
        await Cell(page, ids[2]).ClickAsync(new() { Modifiers = [KeyboardModifier.ControlOrMeta] });
        await FindField(page).FillAsync("Harbour");
        var offer = page.Locator(".ws-panel > .side-panel [data-mk]");
        Assert.False(await offer.EvaluateAsync<bool>("b => b.hidden"));
        Assert.Equal("New gallery \u201CHarbour\u201D", await offer.TextContentAsync());
        await page.Keyboard.PressAsync("Enter");

        await WaitForToastTextAsync(page, Gallery.Web.Workspace.Messages.Created("Harbour", 2));
        var harbour = (await site.Galleries.Find(g => g.Title == "Harbour").ToListAsync()).Single();
        await WaitForAsync(page, "gid => document.querySelector(`.ws-panel > .side-panel input[data-member=\"${gid}\"]`)?.checked === true", harbour.Id, "Harbour ticked");
        Assert.True(await page.EvaluateAsync<bool>("() => document.activeElement === document.querySelector('.ws-panel > .side-panel [data-gq]')"),
            "focus is not back in the find field: " + await page.EvaluateAsync<string>("() => document.activeElement?.outerHTML.slice(0, 120) ?? 'none'"));
        Assert.Equal("", await FindField(page).InputValueAsync());
        Assert.Equal("private", harbour.Visibility);
        Assert.Equal(Sorted([ids[0], ids[2]]), Sorted(harbour.Items));
        Assert.Equal([coast, harbour.Id], await ListOrderAsync(page));
        Assert.Equal("Harbour Private 2", await ListRowTextAsync(page, harbour.Id));
        Assert.Equal([ids[2], ids[0]], await SelectedAsync(page));
    }

    [Fact]
    public async Task Typing_an_existing_name_and_Enter_adds_without_a_duplicate()
    {
        await using var site = await fx.NewSiteAsync();
        var ids = await site.ImagesAsync(3);
        var coast = await site.GalleryAsync("Coast", []);
        var page = await OpenAsync(site, "/admin");

        await Cell(page, ids[1]).ClickAsync();
        await FindField(page).FillAsync("  coast ");
        Assert.True(await page.Locator(".ws-panel > .side-panel [data-mk]").EvaluateAsync<bool>("b => b.hidden"));
        await page.Keyboard.PressAsync("Enter");

        await WaitForToastTextAsync(page, Gallery.Web.Workspace.Messages.Added(1, 0, "Coast", false));
        await WaitForAsync(page, "gid => document.querySelector(`.ws-panel > .side-panel input[data-member=\"${gid}\"]`)?.checked === true", coast, "Coast ticked");
        Assert.True(await page.EvaluateAsync<bool>("() => document.activeElement === document.querySelector('.ws-panel > .side-panel [data-gq]')"));
        Assert.Equal(1, await site.Galleries.CountDocumentsAsync(FilterDefinition<Gallery.Web.Data.GalleryDoc>.Empty));
        Assert.Equal([ids[1]], await ItemsAsync(site, coast));
        Assert.Equal([coast], await ListOrderAsync(page));
    }

    [Fact]
    public async Task Checklist_rows_equal_GalleryChecklist_Match_for_each_query()
    {
        await using var site = await fx.NewSiteAsync();
        var ids = await site.ImagesAsync(1);
        string[] titles = ["Sea birds", "Birds", "birdsong", "Coast", "Big Sur", "\u00C9b\u00E8ne", "Desert", "Bird"];
        foreach (var t in titles) await site.GalleryAsync(t, []);
        var max = site.Services.GetRequiredService<LibraryOptions>().TitleMax;
        var page = await OpenAsync(site, "/admin");
        await Cell(page, ids[0]).ClickAsync();
        Assert.Equal("Photo", await PanelHeadingAsync(page));

        string[] queries = ["", "b", "BIRDS", " bird ", "bird", "sur", "x", "\u00E9B", "coast", "coas", "   ", new string('x', max), new string('x', max + 1), "s"];
        foreach (var q in queries)
        {
            await FindField(page).FillAsync(q);
            var m = Gallery.Web.Components.Molecules.GalleryChecklist.Match(q, titles, max);
            var shown = m.Shown.Select(i => titles[i]).ToList();
            var hidden = titles.Where((_, i) => !m.Shown.Contains(i)).ToList();
            var actual = await page.EvaluateAsync<string[]>("""
                () => [...document.querySelectorAll('.ws-panel > .side-panel label.ws-checks-row')]
                  .map(r => (r.hidden ? '-' : '+') + r.querySelector('.ws-checks-name').textContent)
                """);
            Assert.Equal([.. shown.Select(t => "+" + t), .. hidden.Select(t => "-" + t)], actual);
            var offer = await page.EvaluateAsync<string?>("() => { const b = document.querySelector('.ws-panel > .side-panel [data-mk]'); return b.hidden ? null : b.textContent; }");
            Assert.Equal(m.Offer is null ? null : $"New gallery \u201C{m.Offer}\u201D", offer);
        }
    }

    [Fact]
    public async Task Ticking_a_public_gallery_says_visitors_can_see_them_now()
    {
        await using var site = await fx.NewSiteAsync();
        var ids = await site.ImagesAsync(3);
        var coast = await site.GalleryAsync("Coast", [ids[0]], publish: true);
        var page = await OpenAsync(site, "/admin");

        await Cell(page, ids[1]).ClickAsync();
        await Cell(page, ids[2]).ClickAsync(new() { Modifiers = [KeyboardModifier.ControlOrMeta] });
        await CheckRow(page, coast).ClickAsync();
        await WaitForToastTextAsync(page, Gallery.Web.Workspace.Messages.Added(2, 0, "Coast", true));
        Assert.Equal(Sorted(ids), Sorted(await ItemsAsync(site, coast)));
    }

    [Fact]
    public async Task Unticking_the_open_gallery_removes_the_cell()
    {
        await using var site = await fx.NewSiteAsync();
        var ids = await site.ImagesAsync(3);
        var coast = await site.GalleryAsync("Coast", ids);
        var page = await OpenAsync(site, $"/admin/g/{coast}");
        Assert.Equal(ids.ToArray(), await CellIdsAsync(page));

        await Cell(page, ids[1]).ClickAsync();
        await CheckRow(page, coast).ClickAsync();
        await WaitForToastTextAsync(page, Gallery.Web.Workspace.Messages.Removed(1, "Coast", false));
        await WaitForCellsAsync(page, 2);
        Assert.Equal([ids[0], ids[2]], await CellIdsAsync(page));
        Assert.Equal([ids[0], ids[2]], await ItemsAsync(site, coast));
        Assert.Equal($"/admin/g/{coast}", new Uri(page.Url).AbsolutePath);
        await WaitForAsync(page, "gid => document.querySelector(`.ws-list a[data-gid=\"${gid}\"] .ws-coll-count`)?.textContent === '2'", coast, "Coast's count 2");
    }

    [Fact]
    public async Task Removing_the_last_photo_of_a_public_gallery_says_visitors_can_no_longer_see_it_and_its_page_answers_404()
    {
        await using var site = await fx.NewSiteAsync();
        var ids = await site.ImagesAsync(2);
        var coast = await site.GalleryAsync("Coast", [ids[0]], publish: true);
        var slug = (await site.GalleryDocAsync(coast)).Slug;
        Assert.Equal(200, (await VisitorAsync(site, $"/g/{coast}/{slug}")).Status);
        var page = await OpenAsync(site, $"/admin/g/{coast}");

        await Cell(page, ids[0]).ClickAsync();
        await CheckRow(page, coast).ClickAsync();
        await WaitForToastTextAsync(page, Gallery.Web.Workspace.Messages.Removed(1, "Coast", true));
        await WaitForCellsAsync(page, 0);
        Assert.Empty(await ItemsAsync(site, coast));
        Assert.Equal(404, (await VisitorAsync(site, $"/g/{coast}/{slug}")).Status);
    }

    // ── drag and the Move buttons (R-07) ───────────────────────────────────────────────────

    [Fact]
    public async Task Drag_a_gallery_name_to_the_top_stores_the_home_order()
    {
        await using var site = await fx.NewSiteAsync();
        var ids = await site.ImagesAsync(3);
        var a = await site.GalleryAsync("Coast", [ids[0]]);
        var b = await site.GalleryAsync("Birds", [ids[1]]);
        var c = await site.GalleryAsync("Desert", [ids[2]]);
        var page = await OpenAsync(site, "/admin");
        Assert.Equal([a, b, c], await ListOrderAsync(page));

        await ListRow(page, c).DragToAsync(ListRow(page, a), new() { TargetPosition = new() { X = 20, Y = 2 } });
        await WaitForToastTextAsync(page, Gallery.Web.Workspace.Messages.GalleryOrderSaved);
        Assert.Equal([c, a, b], await site.HomeOrderAsync());
        await WaitForAsync(page, "first => document.querySelector('.ws-list a[data-gid]').dataset.gid === first", c, "the list re-read");
        Assert.Equal([c, a, b], await ListOrderAsync(page));
        Assert.Equal(0, await page.Locator(".ws-list .ws-before, .ws-list .ws-over").CountAsync());
    }

    [Fact]
    public async Task Move_earlier_and_later_buttons_store_the_order()
    {
        await using var site = await fx.NewSiteAsync();
        var ids = await site.ImagesAsync(3);
        var coast = await site.GalleryAsync("Coast", ids);
        var page = await OpenAsync(site, $"/admin/g/{coast}");

        await Cell(page, ids[0]).ClickAsync();
        await page.Locator(".ws-panel .ws-later").ClickAsync();
        await WaitForToastTextAsync(page, Gallery.Web.Workspace.Messages.OrderSaved);
        Assert.Equal([ids[1], ids[0], ids[2]], await ItemsAsync(site, coast));
        Assert.Equal([ids[1], ids[0], ids[2]], await CellIdsAsync(page));
        Assert.Equal([ids[0]], await SelectedAsync(page));

        await ClearToastAsync(page);
        await Cell(page, ids[2]).ClickAsync();
        await page.Locator(".ws-panel .ws-earlier").ClickAsync();
        await WaitForToastTextAsync(page, Gallery.Web.Workspace.Messages.OrderSaved);
        Assert.Equal([ids[1], ids[2], ids[0]], await ItemsAsync(site, coast));
        Assert.Equal([ids[1], ids[2], ids[0]], await CellIdsAsync(page));
    }

    // ── US3 independent test, with the visitor's view ──────────────────────────────────────

    [Fact]
    public async Task Us3_create_add_reorder_cover_publish_and_move_up_then_the_visitor_sees_that_order_cover_and_place()
    {
        await using var site = await fx.NewSiteAsync();
        var ids = await site.ImagesAsync(4);
        var coast = await site.GalleryAsync("Coast", [ids[0]], publish: true);
        var birds = await site.GalleryAsync("Birds", [ids[0]], publish: true);
        var page = await OpenAsync(site, "/admin");
        await MarkDocumentAsync(page);

        // Create by name in the list.
        await page.Locator(".ws-list button[name='collection'][value='new']").ClickAsync();
        Assert.True(await page.EvaluateAsync<bool>("() => document.activeElement?.matches('.ws-list .ws-naming-input') === true"));
        await page.Keyboard.TypeAsync("Harbour");
        await page.Keyboard.PressAsync("Enter");
        await WaitForAsync(page, "() => location.pathname.startsWith('/admin/g/') && document.querySelector('.ws-main .ws-head h1')?.textContent === 'Harbour'", what: "the new gallery opened");
        Assert.Equal(Gallery.Web.Workspace.Messages.Created("Harbour", null), await ToastAsync(page));
        var gid = new Uri(page.Url).AbsolutePath["/admin/g/".Length..];
        await WaitForAsync(page, "gid => document.querySelector(`.ws-list a[data-gid=\"${gid}\"]`)?.getAttribute('aria-current') === 'page'", gid, "the new row current");
        Assert.Equal([coast, birds, gid], await site.HomeOrderAsync());
        Assert.Equal(0, await page.Locator(".ws-list .ws-naming-input").CountAsync());

        // Add three photos (drag them onto its name from All photos).
        await page.Locator(".ws-list a[href='/admin']").First.ClickAsync();
        await WaitForAsync(page, "() => document.querySelector('.ws-main .ws-head h1')?.textContent === 'All photos'", what: "All photos");
        await Cell(page, ids[3]).ClickAsync();
        await Cell(page, ids[1]).ClickAsync(new() { Modifiers = [KeyboardModifier.Shift] });
        await Cell(page, ids[2]).DragToAsync(ListRow(page, gid));
        await WaitForToastTextAsync(page, Gallery.Web.Workspace.Messages.Added(3, 0, "Harbour", false));
        var added = await ItemsAsync(site, gid);
        Assert.Equal(Sorted([ids[1], ids[2], ids[3]]), Sorted(added));

        // Reorder by drag: the last photo to the front.
        await ListRow(page, gid).ClickAsync();
        await WaitForAsync(page, "() => document.querySelector('.ws-main .ws-head h1')?.textContent === 'Harbour'", what: "Harbour");
        Assert.Equal(added.ToArray(), await CellIdsAsync(page));
        var (p, q, r) = (added[0], added[1], added[2]);
        await Cell(page, r).DragToAsync(Cell(page, p), new() { TargetPosition = new() { X = 4, Y = 40 } });
        await WaitForToastTextAsync(page, Gallery.Web.Workspace.Messages.OrderSaved);
        Assert.Equal([r, p, q], await ItemsAsync(site, gid));
        Assert.Equal([r, p, q], await CellIdsAsync(page));
        Assert.Equal(0, await page.Locator(".ws-main .ws-drop").CountAsync());

        // Move later: p goes after q.
        await ClearToastAsync(page);
        await Cell(page, p).ClickAsync();
        await page.Locator(".ws-panel .ws-later").ClickAsync();
        await WaitForToastTextAsync(page, Gallery.Web.Workspace.Messages.OrderSaved);
        Assert.Equal([r, q, p], await ItemsAsync(site, gid));
        Assert.Equal([r, q, p], await CellIdsAsync(page));

        // Cover: q.
        await Cell(page, q).ClickAsync();
        await page.Locator(".ws-panel .ws-cover").ClickAsync();
        var qTitle = (await site.Images.Find(x => x.Id == q).FirstAsync()).Title;
        await WaitForToastTextAsync(page, Gallery.Web.Workspace.Messages.Cover(qTitle, "Harbour"));
        Assert.Equal(q, (await site.GalleryDocAsync(gid)).PinnedCover);
        Assert.Equal([q], await page.EvaluateAsync<string[]>("() => [...document.querySelectorAll('.ws-main .ws-cell')].filter(c => [...c.querySelectorAll('.ws-mark')].some(m => m.textContent === 'Cover')).map(c => c.dataset.id)"));
        // The script-placed mark is styled by PhotoCell's own rule (absolute, top right).
        Assert.Equal("absolute", await Cell(page, q).Locator(".ws-cell-marks").EvaluateAsync<string>("el => getComputedStyle(el).position"));

        // Public on.
        await page.Keyboard.PressAsync("Escape");
        Assert.Equal("Gallery", await PanelHeadingAsync(page));
        await page.Locator(".ws-panel label.switch").ClickAsync();
        await WaitForToastTextAsync(page, Gallery.Web.Workspace.Messages.NowPublic("Harbour"));
        Assert.Equal("public", (await site.GalleryDocAsync(gid)).Visibility);
        await WaitForAsync(page, "() => document.querySelector('.ws-main .ws-meta')?.textContent.includes('· Public') && document.querySelector('.ws-panel input[role=\"switch\"]')?.checked === true", what: "the heading says Public");
        await WaitForAsync(page, "gid => document.querySelector(`.ws-list a[data-gid=\"${gid}\"]`)?.dataset.public === 'true'", gid, "the list row public");

        // Move up: Harbour before Birds.
        await ClearToastAsync(page);
        await page.Locator(".ws-panel .ws-move-up").ClickAsync();
        await WaitForToastTextAsync(page, Gallery.Web.Workspace.Messages.GalleryOrderSaved);
        Assert.Equal([coast, gid, birds], await site.HomeOrderAsync());
        await WaitForAsync(page, "gid => document.querySelectorAll('.ws-list a[data-gid]')[1]?.dataset.gid === gid", gid, "Harbour second in the list");
        Assert.Equal(1, await DocumentLoadsAsync(page));

        // The visitor's view.
        var slug = (await site.GalleryDocAsync(gid)).Slug;
        var (listing, listingStatus) = await VisitorAsync(site, "/g");
        Assert.Equal(200, listingStatus);
        var cards = await listing.EvaluateAsync<string[][]>("""
            () => {
              const seen = new Map();
              for (const a of document.querySelectorAll('a[href^="/g/"]')) {
                const m = /^\/g\/([^/]+)\/[^/]+$/.exec(a.getAttribute('href'));
                if (!m || seen.has(m[1])) continue;
                const box = a.closest('.tile') ?? a;
                seen.set(m[1], [...box.querySelectorAll('img, source')].map(e => (e.getAttribute('srcset') ?? '') + ' ' + (e.getAttribute('src') ?? '')).join(' '));
              }
              return [...seen].map(([gid, files]) => [gid, files]);
            }
            """);
        Assert.Equal([coast, gid, birds], cards.Select(c => c[0]).ToList());
        var harbourFiles = cards.Single(c => c[0] == gid)[1];
        string[] Keys(string id) => [.. Gallery.Web.Tests.Fixtures.SeedShapes.Derivatives(id).SelectMany(d => new[] { d.Avif, d.Webp })];
        Assert.Contains(Keys(q), k => harbourFiles.Contains(k, StringComparison.Ordinal));
        Assert.DoesNotContain(Keys(r).Concat(Keys(p)), k => harbourFiles.Contains(k, StringComparison.Ordinal));

        var (gallery, galleryStatus) = await VisitorAsync(site, $"/g/{gid}/{slug}");
        Assert.Equal(200, galleryStatus);
        var photos = await gallery.EvaluateAsync<string[]>("""
            prefix => [...new Set([...document.querySelectorAll('a[href]')].map(a => a.getAttribute('href')).filter(h => h.startsWith(prefix)).map(h => h.slice(prefix.length)).filter(h => h.length > 0 && !h.includes('/')))]
            """, $"/g/{gid}/{slug}/");
        Assert.Equal([r, q, p], photos);
    }

    // ── Public switch refusal, delete, new gallery cancel ──────────────────────────────────

    [Fact]
    public async Task Public_switch_on_an_emptied_gallery_is_refused_and_put_back_with_the_reason_beside_it()
    {
        await using var site = await fx.NewSiteAsync();
        var ids = await site.ImagesAsync(1);
        var coast = await site.GalleryAsync("Coast", ids);
        var page = await OpenAsync(site, $"/admin/g/{coast}");
        var sw = page.Locator(".ws-panel input[role='switch']");
        Assert.False(await sw.IsDisabledAsync());
        Assert.False(await sw.IsCheckedAsync());

        // The gallery is emptied behind the page's back (another tab).
        using (var scope = site.Services.CreateScope())
            Assert.True((await scope.ServiceProvider.GetRequiredService<GalleryService>().RemoveManyAsync(coast, ids)).IsOk);

        await page.Locator(".ws-panel label.switch").ClickAsync();
        await WaitForToastTextAsync(page, GalleryService.EmptyPublishReason);
        Assert.True(await page.Locator(".ws-panel .ws-toast").EvaluateAsync<bool>("el => el.classList.contains('ws-refusal')"));
        Assert.False(await sw.IsCheckedAsync());
        Assert.Equal(GalleryService.EmptyPublishReason, await sw.EvaluateAsync<string>("el => el.closest('label.switch').nextElementSibling.textContent"));
        Assert.Equal("hint", await sw.EvaluateAsync<string>("el => el.closest('label.switch').nextElementSibling.className.split(' ')[0]"));
        Assert.Equal("private", (await site.GalleryDocAsync(coast)).Visibility);
    }

    [Fact]
    public async Task Deleting_a_gallery_lands_on_All_photos_with_its_photos_kept()
    {
        await using var site = await fx.NewSiteAsync();
        var ids = await site.ImagesAsync(2);
        var coast = await site.GalleryAsync("Coast", ids, publish: true);
        var page = await OpenAsync(site, $"/admin/g/{coast}");
        await MarkDocumentAsync(page);

        await page.Locator(".ws-panel .danger-trigger").ClickAsync();
        var dialog = page.Locator(".ws-panel dialog#ws-del-gallery");
        await dialog.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = WaitMs });
        Assert.Equal("Delete gallery \u201CCoast\u201D?", (await dialog.Locator(".danger-title").TextContentAsync())!.Trim());
        await dialog.Locator(".danger-actions button:not(.danger-cancel)").ClickAsync();

        await WaitForAsync(page, "() => location.pathname === '/admin' && document.querySelector('.ws-main .ws-head h1')?.textContent === 'All photos'", what: "All photos");
        Assert.Equal(Gallery.Web.Workspace.Messages.GalleryDeleted("Coast"), await ToastAsync(page));
        await WaitForAsync(page, "gid => !document.querySelector(`.ws-list a[data-gid=\"${gid}\"]`)", coast, "the row gone");
        Assert.Equal("/admin", await CurrentRowAsync(page));
        Assert.Equal(Enumerable.Reverse(ids).ToArray(), await CellIdsAsync(page));
        Assert.Equal(0, await site.Galleries.CountDocumentsAsync(g => g.Id == coast));
        Assert.Equal(2, await site.Images.CountDocumentsAsync(i => ids.Contains(i.Id) && i.DeletingAt == null));
        Assert.Equal(1, await DocumentLoadsAsync(page));
    }

    [Fact]
    public async Task Delete_confirms_name_what_is_deleted_and_Cancel_deletes_nothing()
    {
        await using var site = await fx.NewSiteAsync();
        var ids = await site.ImagesAsync(3);
        await site.GalleryAsync("Coast", [ids[0]]);
        var page = await OpenAsync(site, "/admin");

        // One photo: the confirm says the server's sentence for its gallery count; Cancel deletes nothing.
        await Cell(page, ids[0]).ClickAsync();
        await page.Locator(".ws-panel .danger-trigger").ClickAsync();
        var one = page.Locator(".ws-panel dialog#ws-del-photo");
        await one.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = WaitMs });
        Assert.Equal(Gallery.Web.Workspace.Messages.DeleteConsequence(1), await one.Locator(".danger-consequence").TextContentAsync());
        await one.Locator(".danger-cancel").ClickAsync();
        await one.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = WaitMs });
        Assert.Equal(3, await site.Images.CountDocumentsAsync(i => ids.Contains(i.Id) && i.DeletingAt == null));
        Assert.Equal(3, await Cells(page).CountAsync());

        // Two photos: the count and the bulk sentence; confirm deletes exactly them.
        await Cell(page, ids[1]).ClickAsync();
        await Cell(page, ids[2]).ClickAsync(new() { Modifiers = [KeyboardModifier.ControlOrMeta] });
        Assert.Equal("Delete 2 photos\u2026", (await page.Locator(".ws-panel .danger-trigger").TextContentAsync())!.Trim());
        await page.Locator(".ws-panel .danger-trigger").ClickAsync();
        var many = page.Locator(".ws-panel dialog#ws-del-selection");
        await many.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = WaitMs });
        Assert.Equal("Delete 2 photos?", (await many.Locator(".danger-title").TextContentAsync())!.Trim());
        Assert.Equal(Gallery.Web.Admin.Pages.WorkspacePage.BulkDeleteConsequence, await many.Locator(".danger-consequence").TextContentAsync());
        var answer = page.WaitForResponseAsync(r => r.Url.EndsWith("/admin/api/photos/delete", StringComparison.Ordinal));
        await many.Locator(".danger-actions button:not(.danger-cancel)").ClickAsync();
        using var json = JsonDocument.Parse(await (await answer).TextAsync());
        Assert.Equal(2, json.RootElement.GetProperty("deleted").GetInt32());
        await WaitForCellsAsync(page, 1);
        Assert.Equal(json.RootElement.GetProperty("message").GetString(), await ToastAsync(page));
        Assert.StartsWith(Gallery.Web.Workspace.Messages.Deleted(2, 0, 0), await ToastAsync(page));
        Assert.Equal([ids[0]], await CellIdsAsync(page));
        Assert.Equal(1, await site.Images.CountDocumentsAsync(i => ids.Contains(i.Id) && i.DeletingAt == null));
    }

    [Fact]
    public async Task New_gallery_naming_is_cancelled_by_Escape_and_by_leaving_the_field()
    {
        await using var site = await fx.NewSiteAsync();
        await site.ImagesAsync(1);
        var page = await OpenAsync(site, "/admin");
        var button = page.Locator(".ws-list button[name='collection'][value='new']");
        var input = page.Locator(".ws-list .ws-naming-input");

        var requests = await AppRequestsDuring(site, page, async () =>
        {
            await button.ClickAsync();
            Assert.Equal("New gallery name", (await page.Locator(".ws-list .ws-naming-label").TextContentAsync())!.Trim());
            await page.Keyboard.TypeAsync("Harbour");
            await page.Keyboard.PressAsync("Escape");
            Assert.Equal(0, await input.CountAsync());
            Assert.Equal(1, await button.CountAsync());

            await button.ClickAsync();
            await page.Keyboard.TypeAsync("Harbour");
            await page.Locator(".ws-main .ws-head h1").ClickAsync();
            Assert.Equal(0, await input.CountAsync());
            Assert.Equal(1, await button.CountAsync());
        });
        Assert.Empty(requests);
        Assert.Equal(0, await site.Galleries.CountDocumentsAsync(FilterDefinition<Gallery.Web.Data.GalleryDoc>.Empty));
        // The cloned field carries CollectionList's scope, so its own rule styles it.
        await button.ClickAsync();
        Assert.Equal("flex", await page.Locator(".ws-list .ws-naming").EvaluateAsync<string>("el => getComputedStyle(el).display"));
    }

    // ── one filled button per context; deletes outlined and last ───────────────────────────

    [Fact]
    public async Task One_primary_button_per_context_and_every_delete_is_outlined_and_last()
    {
        await using var site = await fx.NewSiteAsync();
        var ids = await site.ImagesAsync(3);
        var coast = await site.GalleryAsync("Coast", ids);
        const string Primaries = """
            () => [...document.querySelectorAll('admin-workspace .btn-primary')].filter(b => b.getClientRects().length > 0).map(b => b.textContent.trim())
            """;
        const string LastControl = """
            () => {
              const p = document.querySelector('.ws-panel > .side-panel');
              const all = [...p.querySelectorAll('a, button, input, textarea, select')].filter(e => !e.closest('dialog') && e.getClientRects().length > 0);
              const last = all[all.length - 1];
              const s = getComputedStyle(last);
              return [last.classList.contains('danger-trigger') ? 'danger-trigger' : last.outerHTML.slice(0, 80), s.backgroundColor, String(parseFloat(s.borderTopWidth) > 0), String(s.borderTopColor === s.color)];
            }
            """;
        string[] Outlined = ["danger-trigger", "rgba(0, 0, 0, 0)", "true", "true"];

        foreach (var (path, select, panel) in new[]
                 {
                     ("/admin", 0, "Library"), ($"/admin/g/{coast}", 0, "Gallery"),
                     ($"/admin/g/{coast}", 1, "Photo"), ("/admin", 2, "2 photos selected"),
                 })
        {
            var page = await OpenAsync(site, path);
            if (select >= 1) await Cells(page).Nth(0).ClickAsync();
            if (select >= 2) await Cells(page).Nth(1).ClickAsync(new() { Modifiers = [KeyboardModifier.ControlOrMeta] });
            Assert.Equal(panel, await PanelHeadingAsync(page));
            var primaries = await page.EvaluateAsync<string[]>(Primaries);
            Assert.True(primaries.Length <= 1, $"{panel}: {primaries.Length} filled buttons: {string.Join(", ", primaries)}");
            Assert.Equal(0, await page.Locator(".ws-panel .btn-primary").CountAsync());
            if (panel != "Library") Assert.Equal(Outlined, await page.EvaluateAsync<string[]>(LastControl));
            await page.Context.CloseAsync();
        }
    }

    // ── the panel's photo copies ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Panel_preview_is_inside_the_panel_and_visible()
    {
        await using var site = await fx.NewSiteAsync();
        await site.ImagesAsync(4);
        var page = await OpenAsync(site, "/admin");
        const string Boxes = """
            sel => {
              const p = document.querySelector('.ws-panel > .side-panel');
              const r = p.getBoundingClientRect();
              const s = getComputedStyle(p);
              const left = r.left + parseFloat(s.borderLeftWidth) + parseFloat(s.paddingLeft);
              const right = r.right - parseFloat(s.borderRightWidth) - parseFloat(s.paddingRight);
              return [...p.querySelectorAll(sel)].map(el => {
                const b = el.getBoundingClientRect();
                return [b.height > 0, b.width > 0, b.width <= right - left + 0.5, b.left >= left - 0.5, b.right <= right + 0.5];
              });
            }
            """;

        await Cells(page).Nth(0).ClickAsync();
        var one = await page.EvaluateAsync<bool[][]>(Boxes, ".ws-preview picture");
        Assert.Single(one);
        Assert.Equal([true, true, true, true, true], one[0]);

        await Cells(page).Nth(2).ClickAsync(new() { Modifiers = [KeyboardModifier.Shift] });
        Assert.Equal("3 photos selected", await PanelHeadingAsync(page));
        var three = await page.EvaluateAsync<bool[][]>(Boxes, ".ws-previews picture");
        Assert.Equal(3, three.Length);
        Assert.All(three, b => Assert.Equal([true, true, true, true, true], b));
    }

    // ── T056 (US5, FR-036, R-08): the large view ───────────────────────────────────────────

    private static ILocator Stage(IPage page) => page.Locator(".ws-main .ws-largeview");
    private static ILocator StageButton(IPage page, string value) => page.Locator($".ws-main .ws-largeview button[name='stage'][value='{value}']");

    private static Task<string> StagePositionAsync(IPage page) =>
        page.EvaluateAsync<string>("() => document.querySelector('.ws-main .ws-largeview .ws-stage-pos')?.textContent.trim() ?? '(no stage)'");

    /// <summary>The photo the stage shows (its frame's data-id), or null when the stage is hidden.</summary>
    private static Task<string?> StagePhotoAsync(IPage page) =>
        page.EvaluateAsync<string?>("""
            () => {
              const v = document.querySelector('.ws-main .ws-largeview');
              return v && !v.hidden && v.getClientRects().length > 0 ? v.querySelector('.ws-stage-frame').dataset.id ?? '(empty frame)' : null;
            }
            """);

    private static Task WaitForStageAsync(IPage page, string id, string position) =>
        WaitForAsync(page, """
            ([id, pos]) => {
              const v = document.querySelector('.ws-main .ws-largeview');
              return v && !v.hidden && v.querySelector('.ws-stage-frame').dataset.id === id
                && v.querySelector('.ws-stage-pos').textContent.trim() === pos
                && new URLSearchParams(location.search).get('photo') === id;
            }
            """, new[] { id, position }, $"{id} large at {position}");

    private static Task WaitForGridAsync(IPage page) =>
        WaitForAsync(page, "() => document.querySelector('.ws-main .ws-largeview').hidden && location.search === ''", what: "the grid back");

    /// <summary>[previous disabled, next disabled].</summary>
    private static Task<bool[]> StageEndsAsync(IPage page) =>
        page.EvaluateAsync<bool[]>("""
            () => ['previous', 'next'].map(v => document.querySelector(`.ws-main .ws-largeview button[name="stage"][value="${v}"]`).disabled)
            """);

    /// <summary>True when the cell's box lies inside main's visible box.</summary>
    private static Task<bool> InViewAsync(IPage page, string id) =>
        page.EvaluateAsync<bool>("""
            id => {
              const c = document.querySelector(`.ws-main .ws-cell[data-id="${id}"]`).getBoundingClientRect();
              const m = document.querySelector('.ws-main').getBoundingClientRect();
              const top = Math.max(m.top, 0), bottom = Math.min(m.bottom, innerHeight);
              return c.height > 0 && c.top >= top - 0.5 && c.bottom <= bottom + 0.5;
            }
            """, id);

    [Fact]
    public async Task Us5_third_photo_large_right_right_left_with_an_edit_then_back_selects_the_last_viewed_with_its_new_title()
    {
        await using var site = await fx.NewSiteAsync();
        var ids = await site.ImagesAsync(12);
        var gid = await site.GalleryAsync("Coast", ids);
        var page = await OpenAsync(site, $"/admin/g/{gid}");
        Assert.Equal(ids.ToArray(), await CellIdsAsync(page));
        await MarkDocumentAsync(page);

        // The third photo, by double-click: shown large with its position, the panel shows it.
        await Cells(page).Nth(2).DblClickAsync();
        await WaitForStageAsync(page, ids[2], "3 of 12");
        Assert.Equal([ids[2]], await SelectedAsync(page));
        Assert.Equal("Photo", await PanelHeadingAsync(page));
        Assert.Equal("Photo 3", await page.Locator(".ws-panel .side-panel input[name='title']").InputValueAsync());
        Assert.Equal("← Coast", (await StageButton(page, "back").TextContentAsync())!.Trim());

        // → : the next photo and its details.
        await page.Keyboard.PressAsync("ArrowRight");
        await WaitForStageAsync(page, ids[3], "4 of 12");
        Assert.Equal([ids[3]], await SelectedAsync(page));
        Assert.Equal("Photo 4", await page.Locator(".ws-panel .side-panel input[name='title']").InputValueAsync());

        // An edit on the way: the details stay usable while the photo is large (FR-036).
        var title = page.Locator(".ws-panel .side-panel input[name='title']");
        await title.FillAsync("Pier at dusk");
        await title.PressAsync("Tab");
        await WaitForAsync(page, "id => document.querySelector(`.ws-main .ws-cell[data-id=\"${id}\"]`).dataset.title === 'Pier at dusk'", ids[3], "the saved title on the cell");
        await page.Locator(".ws-main .ws-largeview .ws-stage-frame").ClickAsync();
        Assert.Equal([ids[3]], await SelectedAsync(page));

        await page.Keyboard.PressAsync("ArrowRight");
        await WaitForStageAsync(page, ids[4], "5 of 12");
        await page.Keyboard.PressAsync("ArrowLeft");
        await WaitForStageAsync(page, ids[3], "4 of 12");
        Assert.Equal("Pier at dusk", await page.Locator(".ws-panel .side-panel input[name='title']").InputValueAsync());

        // Back: the grid, the last photo viewed selected, with its new title, in view; no document load.
        await page.GoBackAsync();
        await WaitForGridAsync(page);
        Assert.Equal($"/admin/g/{gid}", new Uri(page.Url).AbsolutePath);
        Assert.Equal([ids[3]], await SelectedAsync(page));
        Assert.Equal("Pier at dusk", (await Cells(page).Nth(3).Locator(".title").TextContentAsync())!.Trim());
        Assert.True(await InViewAsync(page, ids[3]), "the last viewed photo is not in view");
        Assert.Equal("Photo", await PanelHeadingAsync(page));
        Assert.Equal(1, await DocumentLoadsAsync(page));
        Assert.Equal("Pier at dusk", (await site.Images.Find(i => i.Id == ids[3]).SingleAsync()).Title);
        Assert.True(await page.Locator(".ws-main .cs").IsVisibleAsync());
        Assert.True(await page.Locator(".ws-main .ws-head").IsVisibleAsync());
    }

    [Fact]
    public async Task Large_view_shows_the_whole_photo_as_large_as_main_allows()
    {
        await using var site = await fx.NewSiteAsync();
        var ids = await site.ImagesAsync(3);
        // Stored sizes: landscape 4:3 (as seeded), portrait 3:5, panorama 3:1.
        var stored = new Dictionary<string, (int W, int H)> { [ids[0]] = (1280, 960), [ids[1]] = (960, 1600), [ids[2]] = (3000, 1000) };
        foreach (var (id, (w, h)) in stored)
            await site.Images.UpdateOneAsync(i => i.Id == id, Builders<Gallery.Web.Data.ImageDoc>.Update.Set(i => i.Width, w).Set(i => i.Height, h));
        var page = await OpenAsync(site, "/admin");
        const string Measure = """
            () => {
              const box = (el) => { const r = el.getBoundingClientRect(); return [r.left, r.top, r.right, r.bottom, r.width, r.height]; };
              const main = document.querySelector('.ws-main');
              const stage = document.querySelector('.ws-main .ws-largeview .ws-stage-frame');
              const frame = stage.querySelector('.pic .frame');
              const img = stage.querySelector('.pic picture.main img');
              const sizes = [...stage.querySelectorAll('.pic picture.main source')].map(s => s.getAttribute('sizes'));
              return {
                main: box(main), stage: box(stage), frame: box(frame), img: box(img), sizes,
                position: getComputedStyle(frame).position, fit: getComputedStyle(img).objectFit,
                overlay: stage.querySelectorAll('.overlay, .select-mark').length,
                scrolls: main.scrollHeight > main.clientHeight + 1 || main.scrollWidth > main.clientWidth + 1,
              };
            }
            """;

        var problems = new List<string>();
        foreach (var (id, (w, h)) in stored)
        {
            // Enter on the focused cell opens it.
            var cell = page.Locator($".ws-main .ws-cell[data-id='{id}']");
            await cell.ClickAsync();
            await cell.FocusAsync();
            await page.Keyboard.PressAsync("Enter");
            await WaitForAsync(page, "id => document.querySelector('.ws-main .ws-largeview .ws-stage-frame').dataset.id === id", id, $"{id} large");
            var m = await page.EvaluateAsync<JsonElement>(Measure);
            double[] B(string k) => m.GetProperty(k).EnumerateArray().Select(v => v.GetDouble()).ToArray();
            var (main, stage, frame, img) = (B("main"), B("stage"), B("frame"), B("img"));
            var name = $"{w}x{h}";
            // The Picture's own contain-fit rules apply to the copy (its scope came with it).
            if (m.GetProperty("position").GetString() != "absolute") problems.Add($"{name}: the frame is not laid out by Picture's rules ({m.GetProperty("position")})");
            if (m.GetProperty("fit").GetString() != "contain") problems.Add($"{name}: the photo is not object-fit contain");
            if (m.GetProperty("overlay").GetInt32() != 0) problems.Add($"{name}: the select mark came along");
            // Inside main and inside the stage, main not scrolling.
            if (frame[0] < main[0] - 0.5 || frame[1] < main[1] - 0.5 || frame[2] > main[2] + 0.5 || frame[3] > main[3] + 0.5)
                problems.Add($"{name}: frame [{string.Join(", ", frame[..4])}] is not inside main [{string.Join(", ", main[..4])}]");
            if (frame[0] < stage[0] - 0.5 || frame[1] < stage[1] - 0.5 || frame[2] > stage[2] + 0.5 || frame[3] > stage[3] + 0.5)
                problems.Add($"{name}: frame is not inside the stage");
            if (m.GetProperty("scrolls").GetBoolean()) problems.Add($"{name}: main scrolls with the stage open");
            // The stored ratio within 1%, and as large as the stage allows (touching its width or its height).
            var ratio = frame[4] / frame[5];
            if (Math.Abs(ratio / ((double)w / h) - 1) > 0.01) problems.Add($"{name}: frame ratio {ratio:0.###}, stored {(double)w / h:0.###}");
            if (Math.Abs(frame[4] - stage[4]) > 1 && Math.Abs(frame[5] - stage[5]) > 1)
                problems.Add($"{name}: frame {frame[4]:0.#}x{frame[5]:0.#} fills neither side of the stage {stage[4]:0.#}x{stage[5]:0.#}");
            if (Math.Abs(img[4] - frame[4]) > 0.5 || Math.Abs(img[5] - frame[5]) > 0.5) problems.Add($"{name}: the image box is not the frame's");
            // The browser picks the file for the width it is shown at.
            var sizes = m.GetProperty("sizes").EnumerateArray().Select(s => s.GetString()).Distinct().ToList();
            if (sizes.Count != 1 || sizes[0] != $"{Math.Ceiling(frame[4] - 0.01)}px") problems.Add($"{name}: sizes [{string.Join(", ", sizes)}], shown {frame[4]:0.##} px wide");
            if (stage[5] < 300) problems.Add($"{name}: the stage is only {stage[5]:0} px high");

            await page.Keyboard.PressAsync("Escape");
            await WaitForGridAsync(page);
        }
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Fact]
    public async Task Large_view_reload_reopens_the_same_photo()
    {
        await using var site = await fx.NewSiteAsync(new() { ["Library:PageSize"] = "2" });
        var ids = await site.ImagesAsync(5);
        var newestFirst = Enumerable.Reverse(ids).ToArray();
        // No IntersectionObserver: only the large view (and Show more) loads chunks.
        const string NoObserver = "delete window.IntersectionObserver;";
        var page = await OpenAsync(site, "/admin", NoObserver);
        Assert.Equal(newestFirst[..2], await CellIdsAsync(page));

        // Next past the last loaded cell loads the next chunk.
        await Cells(page).Nth(1).DblClickAsync();
        await WaitForStageAsync(page, newestFirst[1], "2 of 5");
        Assert.Equal(new[] { false, false }, await StageEndsAsync(page));
        await StageButton(page, "next").ClickAsync();
        await WaitForStageAsync(page, newestFirst[2], "3 of 5");
        Assert.Equal(newestFirst[..4], await CellIdsAsync(page));

        // Reload: the same photo, large, at the same position.
        await page.ReloadAsync();
        await BrowserWait.LoadedAsync(page, WaitMs);
        await ReadyAsync(page);
        await WaitForStageAsync(page, newestFirst[2], "3 of 5");
        Assert.Equal([newestFirst[2]], await SelectedAsync(page));
        Assert.Equal("Photo", await PanelHeadingAsync(page));

        // A direct load of the oldest photo loads chunks until it is found; it is the last, so Next is unavailable.
        var ctx = await site.NewContextAsync();
        await ctx.AddInitScriptAsync(NoObserver);
        var direct = await ctx.NewPageAsync();
        await direct.Mouse.MoveAsync(0, 0);
        Assert.Equal(200, (await direct.GotoAsync(site.BaseUrl + $"/admin?photo={newestFirst[4]}"))!.Status);
        await BrowserWait.LoadedAsync(direct, WaitMs);
        await ReadyAsync(direct);
        await WaitForStageAsync(direct, newestFirst[4], "5 of 5");
        Assert.Equal(newestFirst, await CellIdsAsync(direct));
        Assert.Equal(new[] { false, true }, await StageEndsAsync(direct));

        // Its Back (nothing was pushed) goes to the grid in place, with the photo selected and in view.
        await StageButton(direct, "back").ClickAsync();
        await WaitForGridAsync(direct);
        Assert.Equal("/admin", new Uri(direct.Url).PathAndQuery);
        Assert.Equal([newestFirst[4]], await SelectedAsync(direct));
        Assert.True(await InViewAsync(direct, newestFirst[4]), "the photo is not in view");

        // An unknown id leaves the grid as it is and drops the address's photo.
        var other = await ctx.NewPageAsync();
        Assert.Equal(200, (await other.GotoAsync(site.BaseUrl + $"/admin?photo={Gallery.Core.Ids.OpaqueId.NewId()}"))!.Status);
        await BrowserWait.LoadedAsync(other, WaitMs);
        await ReadyAsync(other);
        Assert.Null(await StagePhotoAsync(other));
        Assert.Equal("/admin", new Uri(other.Url).PathAndQuery);
    }

    [Fact]
    public async Task Large_view_ends_are_unavailable_and_Escape_returns_to_the_grid()
    {
        await using var site = await fx.NewSiteAsync();
        var ids = await site.ImagesAsync(3);
        var gid = await site.GalleryAsync("Coast", ids);
        var page = await OpenAsync(site, $"/admin/g/{gid}");

        // Open large from the panel.
        await Cells(page).Nth(0).ClickAsync();
        await page.Locator(".ws-panel .side-panel .ws-large").ClickAsync();
        await WaitForStageAsync(page, ids[0], "1 of 3");
        Assert.Equal(new[] { true, false }, await StageEndsAsync(page));
        await page.Keyboard.PressAsync("ArrowLeft");
        await StageButton(page, "previous").ClickAsync(new() { Force = true });
        Assert.Equal(ids[0], await StagePhotoAsync(page));
        Assert.Equal("1 of 3", await StagePositionAsync(page));

        await StageButton(page, "next").ClickAsync();
        await WaitForStageAsync(page, ids[1], "2 of 3");
        Assert.Equal(new[] { false, false }, await StageEndsAsync(page));
        await page.Keyboard.PressAsync("ArrowRight");
        await WaitForStageAsync(page, ids[2], "3 of 3");
        Assert.Equal(new[] { false, true }, await StageEndsAsync(page));
        await page.Keyboard.PressAsync("ArrowRight");
        await StageButton(page, "next").ClickAsync(new() { Force = true });
        Assert.Equal(ids[2], await StagePhotoAsync(page));
        Assert.Equal("3 of 3", await StagePositionAsync(page));

        // The rest of main is hidden while the photo is large; nothing else in main is shown.
        Assert.False(await page.Locator(".ws-main .ws-head").IsVisibleAsync());
        Assert.False(await page.Locator(".ws-main .cs").IsVisibleAsync());

        // Escape: back to the grid (the pushed entry is popped), the photo selected.
        await page.Keyboard.PressAsync("Escape");
        await WaitForGridAsync(page);
        Assert.Equal([ids[2]], await SelectedAsync(page));
        Assert.Equal($"/admin/g/{gid}", new Uri(page.Url).PathAndQuery);
        // Forward reopens it.
        await page.GoForwardAsync();
        await WaitForStageAsync(page, ids[2], "3 of 3");
    }
}
