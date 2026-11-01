using System.Text.Json;
using Gallery.Web.Tests.Fixtures;
using Microsoft.Playwright;
using MongoDB.Driver;

namespace Gallery.Web.Tests;

/// <summary>
/// 009 T051 (US1, US3, US4; FR-003, FR-011, FR-019, FR-022, FR-031; UI-2, UI-3, UI-4, UI-5, UI-7):
/// library.js and reorder.js in a real browser, asserting outcomes: the stored order after a drag
/// or a keyboard drop, the counts the selection bar shows, the picker note, the publish flow, and
/// MEASURED geometry and computed colours (a markup assertion cannot see where a row is drawn or
/// what colour a button is painted, so these guards measure the rendered page on purpose).
/// Every DOM probe is scoped to the container that owns the control (section.lib, reorder-grid,
/// .builder); only the per-state "one filled button" count deliberately spans the document, since
/// the rule is per page state.
/// </summary>
[Collection("library-browser")]
[Trait("Category", "Browser")]
public sealed class LibraryBrowserTests(LibraryBrowserFixture fx)
{
    private const float WaitMs = 15_000;

    // ── shared probes ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Visible buttons (button, summary, a.btn) whose computed background is FILLED: not
    /// transparent and not the page's own background colour (an outlined Secondary on white is not
    /// filled). Split into those outside an open modal dialog and those inside it.
    /// </summary>
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

    private sealed record Filled(string[] Outside, string[] Inside, bool Modal);

    private static async Task<Filled> FilledAsync(IPage page)
    {
        var json = await page.EvaluateAsync<JsonElement>(FilledButtonsJs);
        return new Filled(
            json.GetProperty("outside").EnumerateArray().Select(e => e.GetString()!).ToArray(),
            json.GetProperty("inside").EnumerateArray().Select(e => e.GetString()!).ToArray(),
            json.GetProperty("modal").GetBoolean());
    }

    /// <summary>UI-4: every .btn-danger in the document is outlined: transparent fill, a coloured, non-zero border.</summary>
    private static async Task AssertDangerOutlinedAsync(IPage page, string state)
    {
        var rows = await page.EvaluateAsync<JsonElement>("""
            () => [...document.querySelectorAll('.btn-danger')].map(el => {
              const s = getComputedStyle(el);
              const n = (s.backgroundColor.match(/[\d.]+/g) || []).map(Number);
              const b = (s.borderTopColor.match(/[\d.]+/g) || []).map(Number);
              return { label: el.textContent.trim(), bgAlpha: n.length > 3 ? n[3] : 1,
                       borderAlpha: b.length > 3 ? b[3] : 1, borderWidth: parseFloat(s.borderTopWidth),
                       border: s.borderTopColor };
            })
            """);
        var list = rows.EnumerateArray().ToList();
        Assert.True(list.Count > 0, $"{state}: no delete button found");
        foreach (var r in list)
        {
            var label = r.GetProperty("label").GetString();
            Assert.True(r.GetProperty("bgAlpha").GetDouble() == 0, $"{state}: delete button '{label}' has a filled background");
            Assert.True(r.GetProperty("borderAlpha").GetDouble() > 0 && r.GetProperty("borderWidth").GetDouble() > 0,
                $"{state}: delete button '{label}' has no coloured border ({r.GetProperty("border").GetString()})");
            Assert.NotEqual("rgb(0, 0, 0)", r.GetProperty("border").GetString());
        }
    }

    /// <summary>
    /// UI-2: per tile in <paramref name="tileSelector"/>, the control row's box lies below the photo
    /// cell's bottom edge and inside the tile's box; returns the labels of controls that intersect
    /// the cell.
    /// </summary>
    private static async Task<List<string>> AssertRowsUnderCellsAsync(IPage page, string tileSelector, string rowSelector, string where)
    {
        var tiles = await page.EvaluateAsync<JsonElement>("""
            ([tileSel, rowSel]) => [...document.querySelectorAll(tileSel)].map(tile => {
              const box = r => { const b = r.getBoundingClientRect(); return { t: b.top, b: b.bottom, l: b.left, r: b.right }; };
              const cell = tile.querySelector(':scope > .cell');
              const row = tile.querySelector(rowSel);
              const c = box(cell);
              const hits = [...tile.querySelectorAll('a, button, label.select-mark, label.switch')]
                .filter(el => el.getClientRects().length > 0)
                .filter(el => { const e = box(el); return e.l < c.r && e.r > c.l && e.t < c.b && e.b > c.t; })
                .map(el => el.matches('label.select-mark') ? 'select-mark' : (el.getAttribute('aria-label') || el.textContent.trim()));
              return { cell: c, row: row ? box(row) : null, tile: box(tile), hits };
            })
            """, new[] { tileSelector, rowSelector });
        var all = tiles.EnumerateArray().ToList();
        Assert.True(all.Count > 0, $"{where}: no tiles matched {tileSelector}");
        var hits = new List<string>();
        var i = 0;
        foreach (var t in all)
        {
            i++;
            Assert.True(t.GetProperty("row").ValueKind == JsonValueKind.Object, $"{where}: tile {i} has no {rowSelector}");
            double R(string box, string side) => t.GetProperty(box).GetProperty(side).GetDouble();
            Assert.True(R("row", "t") >= R("cell", "b") - 0.5,
                $"{where}: tile {i} row top {R("row", "t")} is above the cell bottom {R("cell", "b")}");
            Assert.True(R("row", "t") >= R("tile", "t") - 0.5 && R("row", "b") <= R("tile", "b") + 0.5
                        && R("row", "l") >= R("tile", "l") - 0.5 && R("row", "r") <= R("tile", "r") + 0.5,
                $"{where}: tile {i} row [{R("row", "l")},{R("row", "t")},{R("row", "r")},{R("row", "b")}] is outside the tile [{R("tile", "l")},{R("tile", "t")},{R("tile", "r")},{R("tile", "b")}]");
            hits.AddRange(t.GetProperty("hits").EnumerateArray().Select(h => $"tile {i}: {h.GetString()}"));
        }
        return hits;
    }

    private static Task<string> CountTextAsync(IPage page) =>
        page.Locator("section.lib .selection-bar .count").InnerTextAsync();

    private static ILocator Bar(IPage page) => page.Locator("section.lib .selection-bar");

    private static ILocator Marks(IPage page) => page.Locator("section.lib .cs .tile label.select-mark");

    private static ILocator BuilderHandles(IPage page) => page.Locator(".builder reorder-grid .cs > .tile .b-ctl .handle");

    /// <summary>
    /// Runs <paramref name="action"/> and waits until the main frame has committed a NEW document
    /// (a form submit and its 303, or a re-rendered refusal) and that document has loaded.
    /// </summary>
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
    }

    private static async Task<List<string>> PostsDuring(IPage page, Func<Task> action, int settleMs = 600)
    {
        var posts = new List<string>();
        void OnRequest(object? _, IRequest r)
        {
            if (r.Method == "POST") lock (posts) posts.Add(r.Url);
        }
        page.Request += OnRequest;
        try
        {
            await action();
            await page.WaitForTimeoutAsync(settleMs);
        }
        finally
        {
            page.Request -= OnRequest;
        }
        lock (posts) return posts.ToList();
    }

    // ── T050 fixture guard ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Fixture_guard_library_is_styled_and_library_js_upgraded_the_bar()
    {
        await using var site = await fx.NewSiteAsync();
        await site.ImagesAsync(2);
        var page = await site.OpenAsync("/admin/library");

        var css = await page.EvaluateAsync<int>("() => (document.head.querySelector('style')?.textContent || '').length");
        Assert.True(css > 2000, $"the inlined stylesheet is {css} characters; the fixture lost the scoped CSS");
        Assert.Equal("grid", await page.Locator("section.lib .cs").EvaluateAsync<string>("el => getComputedStyle(el).display"));
        await page.Locator("section.lib .selection-bar[data-script='ready']").WaitForAsync(new() { State = WaitForSelectorState.Attached, Timeout = WaitMs });
        Assert.True(await Bar(page).EvaluateAsync<bool>("el => el.hidden"));
    }

    // ── FR-022 / FR-019: pointer drag ──────────────────────────────────────────────────────

    [Fact]
    public async Task Drag_a_builder_tile_to_the_front_shows_the_slot_and_stores_the_new_order()
    {
        await using var site = await fx.NewSiteAsync();
        var ids = await site.ImagesAsync(3);
        var gid = await site.GalleryAsync("Coast", ids);
        var page = await site.OpenAsync($"/admin/galleries/{gid}");

        var grid = page.Locator(".builder reorder-grid");
        var from = await BuilderHandles(page).Nth(2).BoundingBoxAsync();
        var target = await grid.Locator(".cs > .tile").Nth(0).Locator(".cell").BoundingBoxAsync();

        await page.Mouse.MoveAsync(from!.X + from.Width / 2, from.Y + from.Height / 2);
        await page.Mouse.DownAsync();
        await page.Mouse.MoveAsync(target!.X + target.Width / 2, target.Y + target.Height / 2, new() { Steps = 12 });

        var slot = await grid.EvaluateAsync<JsonElement>("""
            el => {
              const cs = el.querySelector('.cs');
              const kids = [...cs.children];
              const slot = cs.querySelector(':scope > .drop-slot');
              return { index: slot ? kids.indexOf(slot) : -1, height: slot ? slot.getBoundingClientRect().height : 0 };
            }
            """);
        Assert.Equal(0, slot.GetProperty("index").GetInt32());
        Assert.True(slot.GetProperty("height").GetDouble() > 0, "the drop slot ghost has no height");

        await NavigatesAsync(page, () => page.Mouse.UpAsync());
        Assert.Equal([ids[2], ids[0], ids[1]], (await site.GalleryDocAsync(gid)).Items);
    }

    [Fact]
    public async Task Drag_a_gallery_card_to_the_front_shows_the_slot_and_stores_the_new_home_order()
    {
        await using var site = await fx.NewSiteAsync();
        var ids = await site.ImagesAsync(3);
        var a = await site.GalleryAsync("Alpha", [ids[0]]);
        var b = await site.GalleryAsync("Beta", [ids[1]]);
        var c = await site.GalleryAsync("Gamma", [ids[2]]);
        Assert.Equal([a, b, c], await site.HomeOrderAsync());
        var page = await site.OpenAsync("/admin/galleries");

        var grid = page.Locator("section.gal reorder-grid");
        var from = await grid.Locator(".cs > *").Nth(2).Locator(".gal-row .handle").BoundingBoxAsync();
        var target = await grid.Locator(".cs > *").Nth(0).Locator(".cell").BoundingBoxAsync();

        await page.Mouse.MoveAsync(from!.X + from.Width / 2, from.Y + from.Height / 2);
        await page.Mouse.DownAsync();
        await page.Mouse.MoveAsync(target!.X + target.Width / 2, target.Y + target.Height / 2, new() { Steps = 12 });
        var index = await grid.EvaluateAsync<int>(
            "el => { const cs = el.querySelector('.cs'); return [...cs.children].indexOf(cs.querySelector(':scope > .drop-slot')); }");
        Assert.Equal(0, index);

        await NavigatesAsync(page, () => page.Mouse.UpAsync());
        Assert.Equal([c, a, b], await site.HomeOrderAsync());
    }

    // ── keyboard reorder ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Keyboard_lift_move_two_and_drop_stores_the_order_and_announces_the_position()
    {
        await using var site = await fx.NewSiteAsync();
        var ids = await site.ImagesAsync(4);
        var gid = await site.GalleryAsync("Coast", ids);
        var page = await site.OpenAsync($"/admin/galleries/{gid}");

        var handle = BuilderHandles(page).Nth(0);
        await handle.FocusAsync();
        await page.Keyboard.PressAsync("Space");
        Assert.Equal("true", await handle.GetAttributeAsync("aria-pressed"));
        await page.Keyboard.PressAsync("ArrowRight");
        await page.Keyboard.PressAsync("ArrowRight");

        // The live region belongs to the lifted handle's row (the tile now third in the grid).
        var live = page.Locator(".builder reorder-grid .cs > .tile .b-ctl [role='status'][aria-live='polite']");
        Assert.Equal(1, await live.CountAsync());
        Assert.Equal("Position 3 of 4", (await live.InnerTextAsync()).Trim());

        await NavigatesAsync(page, () => page.Keyboard.PressAsync("Space"));
        Assert.Equal([ids[1], ids[2], ids[0], ids[3]], (await site.GalleryDocAsync(gid)).Items);
    }

    [Fact]
    public async Task Keyboard_escape_after_a_move_restores_the_order_and_posts_nothing()
    {
        await using var site = await fx.NewSiteAsync();
        var ids = await site.ImagesAsync(3);
        var gid = await site.GalleryAsync("Coast", ids);
        var page = await site.OpenAsync($"/admin/galleries/{gid}");

        var tiles = page.Locator(".builder reorder-grid .cs > .tile");
        var before = await tiles.Locator(".b-ctl button[name='image'][value]").EvaluateAllAsync<string[]>("els => els.map(e => e.value)");
        var handle = BuilderHandles(page).Nth(0);
        var posts = await PostsDuring(page, async () =>
        {
            await handle.FocusAsync();
            await page.Keyboard.PressAsync("Space");
            await page.Keyboard.PressAsync("ArrowRight");
            await page.Keyboard.PressAsync("Escape");
        });

        Assert.Empty(posts);
        Assert.Equal("false", await handle.GetAttributeAsync("aria-pressed"));
        var after = await tiles.Locator(".b-ctl button[name='image'][value]").EvaluateAllAsync<string[]>("els => els.map(e => e.value)");
        Assert.Equal(before, after);
        Assert.Equal(ids, (await site.GalleryDocAsync(gid)).Items);
    }

    // ── selection (US1, UI-7) ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Selecting_three_tiles_then_select_all_counts_the_page_and_the_limit_is_the_page_size()
    {
        await using var site = await fx.NewSiteAsync(new() { ["Library:PageSize"] = "5" });
        await site.ImagesAsync(7);
        var page = await site.OpenAsync("/admin/library");

        Assert.Equal(5, await Marks(page).CountAsync());
        Assert.Equal("5", await Bar(page).GetAttributeAsync("data-limit"));
        Assert.False(await Bar(page).IsVisibleAsync(), "the selection bar is visible with nothing selected");

        for (var i = 0; i < 3; i++) await Marks(page).Nth(i).ClickAsync();
        Assert.True(await Bar(page).IsVisibleAsync());
        Assert.Equal("3 selected", await CountTextAsync(page));

        await Bar(page).GetByRole(AriaRole.Button, new() { Name = "Select all on this page", Exact = true }).ClickAsync();
        Assert.Equal("5 selected", await CountTextAsync(page));
        Assert.Equal(5, await page.Locator("section.lib .cs input[name='ids']:checked").CountAsync());

        await Bar(page).GetByRole(AriaRole.Button, new() { Name = "Clear", Exact = true }).ClickAsync();
        Assert.Equal(0, await page.Locator("section.lib .cs input[name='ids']:checked").CountAsync());
        Assert.False(await Bar(page).IsVisibleAsync());
    }

    [Fact]
    public async Task A_tick_past_the_limit_is_refused_at_selection_time_with_a_status_line()
    {
        await using var site = await fx.NewSiteAsync();
        await site.ImagesAsync(4);
        var page = await site.OpenAsync("/admin/library");
        // The page never holds more tiles than the limit, so the refusal path is driven by lowering
        // the bar's limit in the page: the script reads data-limit at selection time.
        await Bar(page).EvaluateAsync("el => { el.dataset.limit = '2'; }");

        await Marks(page).Nth(0).ClickAsync();
        await Marks(page).Nth(1).ClickAsync();
        await Marks(page).Nth(2).ClickAsync();

        Assert.Equal("2 selected", await CountTextAsync(page));
        Assert.False(await page.Locator("section.lib .cs input[name='ids']").Nth(2).IsCheckedAsync());
        Assert.Equal("You can select up to 2 photos at a time.",
            (await Bar(page).Locator("[role='status']").InnerTextAsync()).Trim());

        // Select all on this page stops at the limit too.
        await Bar(page).GetByRole(AriaRole.Button, new() { Name = "Clear", Exact = true }).ClickAsync();
        await Marks(page).Nth(3).ClickAsync();
        await Bar(page).GetByRole(AriaRole.Button, new() { Name = "Select all on this page", Exact = true }).ClickAsync();
        Assert.Equal("2 selected", await CountTextAsync(page));
        Assert.Equal(2, await page.Locator("section.lib .cs input[name='ids']:checked").CountAsync());
        Assert.Equal("You can select up to 2 photos at a time.",
            (await Bar(page).Locator("[role='status']").InnerTextAsync()).Trim());
    }

    // ── picker (FR-011, decisions row 58) ──────────────────────────────────────────────────

    [Fact]
    public async Task Picker_shows_the_public_note_and_adds_to_an_existing_gallery_without_a_new_title()
    {
        await using var site = await fx.NewSiteAsync();
        var ids = await site.ImagesAsync(3);
        var pub = await site.GalleryAsync("Shown", [ids[0]], publish: true);
        var priv = await site.GalleryAsync("Hidden", [ids[0]]);
        var page = await site.OpenAsync("/admin/library");

        // Tiles are newest first: ids[2], ids[1], ids[0].
        await Marks(page).Nth(0).ClickAsync();
        await Marks(page).Nth(1).ClickAsync();
        await Bar(page).GetByRole(AriaRole.Button, new() { Name = "Add to gallery…", Exact = true }).ClickAsync();

        var dialog = page.Locator("section.lib dialog#lib-picker");
        Assert.True(await dialog.EvaluateAsync<bool>("d => d.open && d.matches(':modal')"));
        var options = dialog.Locator("label.lib-option");
        var shown = options.Filter(new() { HasText = "Shown · Public" });
        var hidden = options.Filter(new() { HasText = "Hidden · Private" });
        Assert.True(await shown.Locator(".lib-note").IsVisibleAsync());
        Assert.Equal("Visitors will see these photos once they're added.", (await shown.Locator(".lib-note").InnerTextAsync()).Trim());
        Assert.Equal(0, await hidden.Locator(".lib-note").CountAsync());

        // UI-3 with the picker open: one filled button behind the dialog, one inside it.
        var filled = await FilledAsync(page);
        Assert.True(filled.Modal);
        Assert.Equal(["Add to gallery…"], filled.Outside);
        Assert.Equal(["Add"], filled.Inside);

        await hidden.Locator("input[type='radio']").CheckAsync();
        Assert.False(await dialog.Locator("input[name='newTitle']").EvaluateAsync<bool>("i => i.required"));
        await NavigatesAsync(page, () =>
            dialog.GetByRole(AriaRole.Button, new() { Name = "Add", Exact = true }).ClickAsync());

        Assert.Equal([ids[0], ids[2], ids[1]], (await site.GalleryDocAsync(priv)).Items);
        Assert.Equal([ids[0]], (await site.GalleryDocAsync(pub)).Items);
    }

    // ── publish (UI-5) ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task List_switch_reads_false_when_private_and_publishes_at_once_with_no_warning()
    {
        await using var site = await fx.NewSiteAsync();
        var ids = await site.ImagesAsync(2);
        var gid = await site.GalleryAsync("Coast", ids);
        var page = await site.OpenAsync("/admin/galleries");

        var card = page.Locator($"section.gal reorder-grid .cs > *:has(a[href='/admin/galleries/{gid}'])");
        var input = card.Locator(".gal-row input[role='switch']");
        Assert.False(await input.EvaluateAsync<bool>("i => i.checked"), "a private gallery's switch is checked after load");

        // No confirmation step on the list (DEFAULT decision): the switch publishes at once.
        await NavigatesAsync(page, () => card.Locator(".gal-row label.switch").ClickAsync());
        Assert.Equal("public", (await site.GalleryDocAsync(gid)).Visibility);
        Assert.Equal(0, await page.Locator(".publish-warning").CountAsync());
        Assert.Equal(["New gallery"], (await FilledAsync(page)).Outside);
        var after = page.Locator($"section.gal reorder-grid .cs > *:has(a[href='/admin/galleries/{gid}']) .gal-row input[role='switch']");
        Assert.True(await after.EvaluateAsync<bool>("i => i.checked"));

        // And off again: unpublished at once.
        await NavigatesAsync(page, () =>
            page.Locator($"section.gal reorder-grid .cs > *:has(a[href='/admin/galleries/{gid}']) .gal-row label.switch").ClickAsync());
        Assert.Equal("private", (await site.GalleryDocAsync(gid)).Visibility);
    }

    [Fact]
    public async Task Builder_switch_keeps_typed_edits_shows_a_buttonless_warning_and_Save_stores_all_three()
    {
        await using var site = await fx.NewSiteAsync();
        var ids = await site.ImagesAsync(2);
        var gid = await site.GalleryAsync("Coast", ids);
        var page = await site.OpenAsync($"/admin/galleries/{gid}");
        var panel = page.Locator(".builder .side-panel");
        var warning = panel.Locator(".publish-warning");
        Assert.False(await warning.IsVisibleAsync(), "the warning shows before the switch is turned on");

        await panel.Locator("input[name='title']").FillAsync("Coastline");
        await panel.Locator("textarea[name='description']").FillAsync("Winter walks.");
        var url = page.Url;

        // Flipping the switch submits nothing and does not navigate; the warning appears with no buttons.
        var posts = await PostsDuring(page, () => panel.Locator("label.switch").ClickAsync());
        Assert.Empty(posts);
        Assert.Equal(url, page.Url);
        Assert.True(await panel.Locator("input[role='switch']").EvaluateAsync<bool>("i => i.checked"));
        Assert.True(await warning.IsVisibleAsync(), "the publish warning is not shown while the switch is on");
        // The warning names the stored title (server-rendered; Save applies the typed one).
        Assert.Contains("When you save, “Coast” becomes public: it will be listed on the home page with its 2 photos", await warning.InnerTextAsync());
        Assert.Equal(0, await warning.Locator("button").CountAsync());
        Assert.Equal(0, await page.GetByRole(AriaRole.Button, new() { Name = "Make public" }).CountAsync());
        var filled = (await FilledAsync(page)).Outside;
        Assert.Contains("Save", filled);
        Assert.DoesNotContain("Make public", filled);
        // The typed values are still there.
        Assert.Equal("Coastline", await panel.Locator("input[name='title']").InputValueAsync());
        Assert.Equal("Winter walks.", await panel.Locator("textarea[name='description']").InputValueAsync());
        Assert.Equal("private", (await site.GalleryDocAsync(gid)).Visibility);

        // Turning it back off hides the warning; on again shows it.
        await panel.Locator("label.switch").ClickAsync();
        Assert.False(await warning.IsVisibleAsync());
        await panel.Locator("label.switch").ClickAsync();
        Assert.True(await warning.IsVisibleAsync());

        // Save stores the title, the description AND the visibility in one request.
        await NavigatesAsync(page, () => page.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).ClickAsync());
        var g = await site.GalleryDocAsync(gid);
        Assert.Equal(("Coastline", "Winter walks.", "public"), (g.Title, g.Description, g.Visibility));
        Assert.Equal("Saved. The gallery is public", (await page.Locator(".b-status[role='status']").InnerTextAsync()).Trim());
        Assert.Equal(0, await page.Locator(".publish-warning").CountAsync());
    }

    // ── UI-2 geometry ──────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(1440)]
    [InlineData(1024)]
    [InlineData(390)]
    public async Task Controls_sit_under_the_photo_and_only_the_select_mark_is_over_it(int width)
    {
        await using var site = await fx.NewSiteAsync();
        var ids = await site.ImagesAsync(3);
        var gid = await site.GalleryAsync("Coast", ids, publish: true);
        await site.GalleryAsync("Harbour", [ids[0]]);

        var library = await site.OpenAsync("/admin/library", width);
        var libHits = await AssertRowsUnderCellsAsync(library, "section.lib .cs > .tile", ".lib-row", $"library @{width}");
        Assert.Equal(3, libHits.Count);
        Assert.All(libHits, h => Assert.EndsWith(": select-mark", h));

        var builder = await site.OpenAsync($"/admin/galleries/{gid}", width);
        Assert.Empty(await AssertRowsUnderCellsAsync(builder, ".builder .cs > .tile", ".b-ctl", $"builder @{width}"));

        var list = await site.OpenAsync("/admin/galleries", width);
        Assert.Empty(await AssertRowsUnderCellsAsync(list, "section.gal .cs > *", ".gal-row", $"gallery list @{width}"));
    }

    // ── UI-3 / UI-4 per state ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task One_filled_button_per_state_and_every_delete_is_outlined()
    {
        await using var site = await fx.NewSiteAsync();
        var ids = await site.ImagesAsync(3);
        var gid = await site.GalleryAsync("Coast", ids);

        // Library, no selection: C1 gives the page no primary until something is selected.
        var library = await site.OpenAsync("/admin/library");
        Assert.Empty((await FilledAsync(library)).Outside);

        // Library, selection: the bar's Add is the one primary.
        await Marks(library).Nth(0).ClickAsync();
        await AssertDangerOutlinedAsync(library, "library, selection");
        Assert.Equal(["Add to gallery…"], (await FilledAsync(library)).Outside);

        // Library, side panel open with a selection: the panel's Save is the one primary.
        var panel = await site.OpenAsync($"/admin/library?open={ids[0]}");
        await Marks(panel).Nth(1).ClickAsync();
        Assert.True(await Bar(panel).IsVisibleAsync());
        Assert.Equal(["Save"], (await FilledAsync(panel)).Outside);
        await AssertDangerOutlinedAsync(panel, "library, side panel");

        // Gallery list at rest: New gallery. Builder at rest: Save.
        var list = await site.OpenAsync("/admin/galleries");
        Assert.Equal(["New gallery"], (await FilledAsync(list)).Outside);
        var builder = await site.OpenAsync($"/admin/galleries/{gid}");
        Assert.Equal(["Save"], (await FilledAsync(builder)).Outside);
        await AssertDangerOutlinedAsync(builder, "builder");

        // Builder, publish warning (switch on, not yet saved): Save stays the one primary.
        await builder.Locator(".builder .side-panel label.switch").ClickAsync();
        Assert.True(await builder.Locator(".builder .publish-warning").IsVisibleAsync());
        Assert.Equal(["Save"], (await FilledAsync(builder)).Outside);
        await AssertDangerOutlinedAsync(builder, "builder, publish warning");
    }

    /// <summary>Waits (briefly) for <paramref name="el"/> to hold focus: dialogs restore focus on their queued 'close' event.</summary>
    private static async Task FocusedAsync(ILocator el, string because)
    {
        for (var i = 0; i < 40; i++)
        {
            if (await el.EvaluateAsync<bool>("b => b === document.activeElement")) return;
            await Task.Delay(25);
        }
        Assert.Fail(because + ": " + await el.Page.EvaluateAsync<string>("() => document.activeElement?.outerHTML.slice(0, 200) ?? 'none'"));
    }

    // ── bulk delete confirm (modal DangerConfirm) ──────────────────────────────────────────

    private static ILocator BulkDialog(IPage page) => page.Locator("section.lib dialog#lib-bulk-confirm");

    private static Task<bool> IsModalAsync(ILocator dialog) => dialog.EvaluateAsync<bool>("d => d.open && d.matches(':modal')");

    private static async Task OpenBulkDeleteAsync(IPage page)
    {
        var delete = Bar(page).GetByRole(AriaRole.Button, new() { Name = "Delete…", Exact = true });
        await delete.ClickAsync();
        Assert.True(await IsModalAsync(BulkDialog(page)), "Delete… did not open a modal dialog");
    }

    [Fact]
    public async Task Bulk_delete_opens_a_modal_confirm_that_blocks_the_page_and_names_the_count()
    {
        await using var site = await fx.NewSiteAsync();
        var ids = await site.ImagesAsync(3);
        var page = await site.OpenAsync("/admin/library");
        await Marks(page).Nth(0).ClickAsync();
        await Marks(page).Nth(1).ClickAsync();

        await OpenBulkDeleteAsync(page);
        var dialog = BulkDialog(page);
        Assert.Equal(1, await page.Locator("dialog[open]").CountAsync());
        Assert.Equal("Delete 2 photos?", (await dialog.Locator("h2").InnerTextAsync()).Trim());
        Assert.Equal("Delete 2 photos?", await dialog.EvaluateAsync<string>("d => document.getElementById(d.getAttribute('aria-labelledby')).textContent.trim()"));
        Assert.Equal("This removes them from every gallery and deletes the original files. It can't be undone.",
            (await dialog.Locator(".danger-consequence").InnerTextAsync()).Trim());

        // Cancel first (left) with the focus; the named danger action last (right).
        var buttons = await dialog.Locator(".danger-actions button").EvaluateAllAsync<string[]>("els => els.map(e => e.textContent.trim())");
        Assert.Equal(["Cancel", "Delete 2 photos"], buttons);
        var cancelBox = await dialog.GetByRole(AriaRole.Button, new() { Name = "Cancel", Exact = true }).BoundingBoxAsync();
        var actionBox = await dialog.GetByRole(AriaRole.Button, new() { Name = "Delete 2 photos", Exact = true }).BoundingBoxAsync();
        Assert.True(cancelBox!.X < actionBox!.X, "Cancel is not on the left of the danger action");
        Assert.Equal("Cancel", await page.EvaluateAsync<string>("() => document.activeElement?.textContent.trim()"));

        // Only one "Delete…" control in the page (the bar's), and nothing behind the dialog is live:
        // the point under "Add to gallery…" hits the backdrop, and a click there does nothing.
        Assert.Equal(1, await page.GetByRole(AriaRole.Button, new() { Name = "Delete…", Exact = true }).CountAsync());
        var hit = await Bar(page).GetByRole(AriaRole.Button, new() { Name = "Add to gallery…", Exact = true }).EvaluateAsync<bool>(
            "b => { const r = b.getBoundingClientRect(); const at = document.elementFromPoint(r.left + r.width / 2, r.top + r.height / 2); return at === b || b.contains(at); }");
        Assert.False(hit, "Add to gallery… is still reachable behind the open confirm");
        var add = Bar(page).GetByRole(AriaRole.Button, new() { Name = "Add to gallery…", Exact = true });
        await Assert.ThrowsAsync<TimeoutException>(() => add.ClickAsync(new() { Timeout = 1000 }));
        Assert.False(await page.Locator("dialog#lib-picker").EvaluateAsync<bool>("d => d.open"));

        // UI-3 / UI-4 with the confirm open: nothing filled inside it, its action is outlined danger.
        var filled = await FilledAsync(page);
        Assert.True(filled.Modal);
        Assert.Empty(filled.Inside);
        await AssertDangerOutlinedAsync(page, "library, bulk confirm open");

        // The ids follow the ticks into the hidden lib-delete form.
        var copied = await page.Locator("form#lib-delete input[name='ids']").EvaluateAllAsync<string[]>("els => els.map(e => e.value)");
        Assert.Equal(new[] { ids[2], ids[1] }.Order(), copied.Order());
    }

    [Fact]
    public async Task Bulk_delete_Esc_and_Cancel_close_the_confirm_and_delete_nothing()
    {
        await using var site = await fx.NewSiteAsync();
        var ids = await site.ImagesAsync(3);
        var page = await site.OpenAsync("/admin/library");
        await Marks(page).Nth(0).ClickAsync();
        await Marks(page).Nth(1).ClickAsync();
        var delete = Bar(page).GetByRole(AriaRole.Button, new() { Name = "Delete…", Exact = true });

        var posts = await PostsDuring(page, async () =>
        {
            await OpenBulkDeleteAsync(page);
            await page.Keyboard.PressAsync("Escape");
            Assert.False(await BulkDialog(page).EvaluateAsync<bool>("d => d.open"), "Esc did not close the confirm");
            await FocusedAsync(delete, "focus did not return to Delete… after Esc");

            await OpenBulkDeleteAsync(page);
            await BulkDialog(page).GetByRole(AriaRole.Button, new() { Name = "Cancel", Exact = true }).ClickAsync();
            Assert.False(await BulkDialog(page).EvaluateAsync<bool>("d => d.open"), "Cancel did not close the confirm");
            await FocusedAsync(delete, "focus did not return to Delete… after Cancel");
        });

        Assert.Empty(posts);
        Assert.Equal(3, await site.Images.CountDocumentsAsync(i => ids.Contains(i.Id) && i.DeletingAt == null));
        Assert.Equal("2 selected", await CountTextAsync(page));
        // The page is live again.
        await Bar(page).GetByRole(AriaRole.Button, new() { Name = "Add to gallery…", Exact = true }).ClickAsync(new() { Timeout = 2000 });
        Assert.True(await page.Locator("dialog#lib-picker").EvaluateAsync<bool>("d => d.open"));
    }

    [Fact]
    public async Task Bulk_delete_danger_button_deletes_the_ticked_photos()
    {
        await using var site = await fx.NewSiteAsync();
        var ids = await site.ImagesAsync(3);
        var page = await site.OpenAsync("/admin/library");
        await Marks(page).Nth(0).ClickAsync();
        await Marks(page).Nth(1).ClickAsync();

        await OpenBulkDeleteAsync(page);
        await NavigatesAsync(page, () =>
            BulkDialog(page).GetByRole(AriaRole.Button, new() { Name = "Delete 2 photos", Exact = true }).ClickAsync());

        // Tiles are newest first: the first two marks were ids[2] and ids[1].
        Assert.Equal(1, await site.Images.CountDocumentsAsync(i => ids.Contains(i.Id) && i.DeletingAt == null));
        Assert.Equal(1, await site.Images.CountDocumentsAsync(i => i.Id == ids[0] && i.DeletingAt == null));
    }

    [Fact]
    public async Task Single_photo_delete_in_the_side_panel_is_a_modal_confirm_with_cancel()
    {
        await using var site = await fx.NewSiteAsync();
        var ids = await site.ImagesAsync(2);
        var page = await site.OpenAsync($"/admin/library?open={ids[0]}");
        var trigger = page.Locator(".side-panel .danger-confirm .danger-trigger");
        Assert.Equal("Delete photo…", (await trigger.InnerTextAsync()).Trim());

        await trigger.ClickAsync();
        var dialog = page.Locator(".side-panel dialog.danger-dialog");
        Assert.True(await IsModalAsync(dialog));
        Assert.Equal("Delete “Photo 1”?", (await dialog.Locator("h2").InnerTextAsync()).Trim());
        Assert.Equal("Cancel", await page.EvaluateAsync<string>("() => document.activeElement?.textContent.trim()"));
        await dialog.GetByRole(AriaRole.Button, new() { Name = "Cancel", Exact = true }).ClickAsync();
        Assert.False(await dialog.EvaluateAsync<bool>("d => d.open"));
        await FocusedAsync(trigger, "focus did not return to Delete photo…");

        await trigger.ClickAsync();
        await NavigatesAsync(page, () => dialog.GetByRole(AriaRole.Button, new() { Name = "Delete photo", Exact = true }).ClickAsync());
        Assert.Equal(0, await site.Images.CountDocumentsAsync(i => i.Id == ids[0] && i.DeletingAt == null));
        Assert.Equal(1, await site.Images.CountDocumentsAsync(i => i.Id == ids[1] && i.DeletingAt == null));
    }

    // ── FR-003: no owner script on public pages ────────────────────────────────────────────

    [Fact]
    public async Task Public_pages_load_no_admin_assets_even_for_the_owner()
    {
        await using var site = await fx.NewSiteAsync();
        var ids = await site.ImagesAsync(2);
        var gid = await site.GalleryAsync("Coast", ids, publish: true);
        var slug = (await site.GalleryDocAsync(gid)).Slug;

        foreach (var path in new[] { "/", $"/g/{gid}/{slug}", $"/g/{gid}/{slug}/{ids[0]}" })
        {
            var ctx = await site.NewContextAsync();
            var page = await ctx.NewPageAsync();
            var requested = new List<string>();
            page.Request += (_, r) => { lock (requested) requested.Add(r.Url); };
            var resp = await page.GotoAsync(site.BaseUrl + path, new() { WaitUntil = WaitUntilState.NetworkIdle });
            Assert.Equal(200, resp!.Status);
            Assert.Equal(0, await page.Locator("script[src*='/admin-assets/']").CountAsync());
            lock (requested) Assert.DoesNotContain(requested, u => u.Contains("/admin-assets/", StringComparison.Ordinal));
        }
    }
}
