using System.Text.Json;
using Gallery.Web.Data;
using Gallery.Web.Library;
using Gallery.Web.Tests.Fixtures;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using MongoDB.Driver;

namespace Gallery.Web.Tests;

/// <summary>
/// 020 T058 (US6, SC-008, FR-037; contracts/components.md K1, K12, C12): the workspace on a phone. At
/// 390 × 844 with touch (HasTouch, IsMobile) and taps only, each P1 story's independent test: US1 retitle a
/// photo and add two photos to a gallery, US3 reorder with Move later and turn Public on (here), US2
/// upload one photo into a gallery (<see cref="WorkspacePhoneUploadBrowserTests"/>). At every step nothing
/// scrolls sideways and every visible control's box is at least 44 × 44 px (<see cref="PhoneRules"/>).
/// The details panel is a bottom sheet that opens on a tap on a photo and closes with Done; a gallery's
/// settings are one tap away (Gallery settings). Owner walk-through: the collections list is a menu under
/// one button (T074); a select-mark tap multi-selects without the sheet, and the selection bar's Add to
/// gallery opens it (T075).
/// </summary>
[Collection("library-browser")]
[Trait("Category", "Browser")]
public sealed class WorkspacePhoneBrowserTests(LibraryBrowserFixture fx)
{
    private const float WaitMs = 15_000;

    /// <summary>A phone page at <paramref name="path"/>, loaded, workspace.js ready. Close it with <c>page.Context.CloseAsync()</c>.</summary>
    private async Task<IPage> PhoneAsync(LibrarySite site, string path)
    {
        var page = await PhoneRules.OpenAsync(fx.Browser, await site.NewContextAsync(), site.BaseUrl, path, LibraryBrowserFixture.ImageBaseUrl);
        return page;
    }

    private static ILocator Cells(IPage page) => page.Locator(".ws-main .ws-cell");

    private static Task<string> PanelHeadingAsync(IPage page) =>
        page.EvaluateAsync<string>("() => document.querySelector('.ws-panel > .side-panel > h2')?.textContent.trim() ?? '(no panel)'");

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

    private static Task WaitForSheetAsync(IPage page, string heading) =>
        WaitForAsync(page, """
            h => document.querySelector('admin-workspace').classList.contains('ws-sheet')
              && document.querySelector('.ws-panel > .side-panel > h2')?.textContent.trim() === h
              && document.querySelector('.ws-panel > .side-panel').getClientRects().length > 0
            """, heading, $"the {heading} sheet");

    private static Task WaitForNoSheetAsync(IPage page) =>
        WaitForAsync(page, """
            () => !document.querySelector('admin-workspace').classList.contains('ws-sheet')
              && [...document.querySelectorAll('.ws-panel > .side-panel')].every(p => p.getClientRects().length === 0)
            """, null, "the sheet closed");

    private static Task WaitForToastAsync(IPage page) =>
        WaitForAsync(page, "() => document.querySelector('.ws-panel .ws-toast').textContent.length > 0", what: "a toast");

    private static Task ClearToastAsync(IPage page) =>
        page.EvaluateAsync("() => { document.querySelector('.ws-panel .ws-toast').textContent = ''; }");

    private static ILocator Done(IPage page) => page.Locator(".ws-panel > .side-panel .ws-done");

    private static ILocator Bar(IPage page) => page.Locator(".ws-main .ws-selbar");

    private static Task<string[]> SelectedAsync(IPage page) =>
        page.EvaluateAsync<string[]>("() => [...document.querySelectorAll('.ws-main .ws-cell[aria-selected=\"true\"]')].map(c => c.dataset.id)");

    /// <summary>The selection bar shows "{n} selected" (fixed to the window's bottom, full width), or is not shown.</summary>
    private static Task WaitForBarAsync(IPage page, int? n) =>
        WaitForAsync(page, """
            n => {
              const bar = document.querySelector('.ws-main .ws-selbar');
              const shown = bar.getClientRects().length > 0;
              if (n === null) return !shown;
              const r = bar.getBoundingClientRect();
              return shown && bar.querySelector('.ws-selbar-text').textContent === `${n} selected`
                && getComputedStyle(bar).position === 'fixed' && r.left === 0 && r.right === innerWidth && Math.abs(r.bottom - innerHeight) < 0.5;
            }
            """, n, n is null ? "no selection bar" : $"the bar \"{n} selected\"");

    /// <summary>
    /// The sheet: fixed to the bottom of the window, full width, below the top 4 × --space-l; Done in its
    /// heading row (vertically centred on the h2, to its right).
    /// </summary>
    private static async Task AssertSheetAsync(IPage page, string when)
    {
        var m = await page.EvaluateAsync<JsonElement>("""
            () => {
              const panel = document.querySelector('.ws-panel');
              const p = panel.getBoundingClientRect();
              const h = document.querySelector('.ws-panel > .side-panel > h2').getBoundingClientRect();
              const d = document.querySelector('.ws-panel > .side-panel .ws-done').getBoundingClientRect();
              return [p.left, p.right, p.top, p.bottom, innerWidth, innerHeight, (h.top + h.bottom) / 2, (d.top + d.bottom) / 2, h.right, d.left, getComputedStyle(panel).position];
            }
            """);
        double V(int i) => m[i].GetDouble();
        Assert.True(m[10].GetString() == "fixed", $"{when}: the panel is {m[10]}, not a fixed sheet");
        Assert.True(Math.Abs(V(0)) <= 0.5 && Math.Abs(V(1) - V(4)) <= 0.5, $"{when}: the sheet spans {V(0)}..{V(1)}, the window 0..{V(4)}");
        Assert.True(Math.Abs(V(3) - V(5)) <= 0.5, $"{when}: the sheet's bottom is {V(3)}, the window's {V(5)}");
        Assert.True(V(2) >= 4 * 48 - 0.5, $"{when}: the sheet's top is {V(2)}");
        Assert.True(Math.Abs(V(6) - V(7)) <= 2, $"{when}: Done's centre {V(7)} is not on the heading's row ({V(6)})");
        Assert.True(V(9) >= V(8), $"{when}: Done ({V(9)}) is not right of the heading ({V(8)})");
    }

    // ── US1 by touch: retitle a photo, add two photos to a gallery ────────────────────────────

    [Fact]
    public async Task Us1_by_touch_retitle_a_photo_and_add_two_to_a_gallery()
    {
        await using var site = await fx.NewSiteAsync();
        var ids = await site.ImagesAsync(4);
        var coast = await site.GalleryAsync("Coast", []);
        var page = await PhoneAsync(site, "/admin");
        var order = await page.EvaluateAsync<string[]>("() => [...document.querySelectorAll('.ws-main .ws-cell')].map(c => c.dataset.id)");
        Assert.Equal(Enumerable.Reverse(ids).ToArray(), order);
        await PhoneRules.AssertAsync(page, "All photos");
        Assert.Equal(0, await page.Locator(".ws-main .ws-settings").CountAsync());

        // A tap on a photo opens its details as a sheet.
        await Cells(page).Nth(0).TapAsync();
        await WaitForSheetAsync(page, "Photo");
        await AssertSheetAsync(page, "one photo");
        await PhoneRules.AssertAsync(page, "one photo, sheet open");

        // Retitle: type, then Done (leaving the field saves it, A1).
        var title = page.Locator(".ws-panel > .side-panel input[name='title']");
        await title.TapAsync();
        await title.FillAsync("Pier");
        await Done(page).TapAsync();
        await WaitForNoSheetAsync(page);
        await WaitForAsync(page, "id => document.querySelector(`.ws-main .ws-cell[data-id=\"${id}\"]`).dataset.title === 'Pier'", order[0], "the new title on the cell");
        Assert.Equal("Pier", (await site.Images.Find(i => i.Id == order[0]).SingleAsync()).Title);
        await PhoneRules.AssertAsync(page, "sheet closed, one selected");

        // A select-mark tap on a second photo: two selected, the sheet stays closed; Add to gallery opens it on them.
        await WaitForBarAsync(page, 1);
        await Cells(page).Nth(1).Locator("label.select-mark").TapAsync();
        await WaitForBarAsync(page, 2);
        Assert.False(await page.EvaluateAsync<bool>("() => document.querySelector('admin-workspace').classList.contains('ws-sheet')"));
        await PhoneRules.AssertAsync(page, "two photos, bar");
        await Bar(page).Locator(".ws-selbar-add").TapAsync();
        await WaitForSheetAsync(page, "2 photos selected");
        await WaitForBarAsync(page, null);
        await AssertSheetAsync(page, "two photos");
        await PhoneRules.AssertAsync(page, "two photos, sheet open");

        // Tick Coast: both join it (A7).
        await ClearToastAsync(page);
        await page.Locator($".ws-panel > .side-panel label.ws-checks-row:has(input[data-member='{coast}'])").TapAsync();
        await WaitForToastAsync(page);
        Assert.Equal(new[] { order[0], order[1] }.Order(), (await site.GalleryDocAsync(coast)).Items.Order());
        Assert.True(await page.Locator($".ws-panel > .side-panel input[data-member='{coast}']").IsCheckedAsync());

        await Done(page).TapAsync();
        await WaitForNoSheetAsync(page);
        await PhoneRules.AssertAsync(page, "after adding");
        await page.Context.CloseAsync();
    }

    // ── US3 by touch: reorder with Move later, turn Public on; settings one tap away ──────────

    [Fact]
    public async Task Us3_by_touch_reorder_with_Move_later_and_turn_Public_on()
    {
        await using var site = await fx.NewSiteAsync();
        var ids = await site.ImagesAsync(3);
        var gid = await site.GalleryAsync("Coast", ids);
        var page = await PhoneAsync(site, $"/admin/g/{gid}");
        Assert.Equal(ids.ToArray(), await page.EvaluateAsync<string[]>("() => [...document.querySelectorAll('.ws-main .ws-cell')].map(c => c.dataset.id)"));
        await PhoneRules.AssertAsync(page, "gallery");

        // Nothing selected: the gallery's settings are one tap away.
        Assert.False(await page.Locator(".ws-panel > .side-panel").IsVisibleAsync());
        await page.Locator(".ws-main .ws-settings").TapAsync();
        await WaitForSheetAsync(page, "Gallery");
        await AssertSheetAsync(page, "gallery settings");
        await PhoneRules.AssertAsync(page, "gallery settings");
        await Done(page).TapAsync();
        await WaitForNoSheetAsync(page);

        // Move later on the first photo (A9).
        await Cells(page).Nth(0).TapAsync();
        await WaitForSheetAsync(page, "Photo");
        await PhoneRules.AssertAsync(page, "photo in a gallery");
        await ClearToastAsync(page);
        await page.Locator(".ws-panel > .side-panel .ws-later").TapAsync();
        await WaitForToastAsync(page);
        string[] moved = [ids[1], ids[0], ids[2]];
        await WaitForAsync(page, "o => [...document.querySelectorAll('.ws-main .ws-cell')].map(c => c.dataset.id).join(' ') === o", string.Join(' ', moved), "the new order in the grid");
        Assert.Equal(moved, (await site.GalleryDocAsync(gid)).Items);
        await Done(page).TapAsync();
        await WaitForNoSheetAsync(page);

        // Public on (A6) from Gallery settings; the sheet stays open with the server's new hint.
        await page.Locator(".ws-main .ws-settings").TapAsync();
        await WaitForSheetAsync(page, "Gallery");
        Assert.Empty(await page.EvaluateAsync<string[]>("() => [...document.querySelectorAll('.ws-main .ws-cell[aria-selected=\"true\"]')].map(c => c.dataset.id)"));
        await ClearToastAsync(page);
        await page.Locator(".ws-panel > .side-panel label.switch").TapAsync();
        await WaitForAsync(page, """
            () => document.querySelector('.ws-panel > .side-panel input[role="switch"]')?.checked === true
              && document.querySelector('.ws-panel > .side-panel p.hint')?.textContent === 'Visitors can see this gallery and its photos.'
              && document.querySelector('.ws-main .ws-meta')?.textContent.includes('Public')
            """, null, "Public on");
        Assert.Equal("public", (await site.GalleryDocAsync(gid)).Visibility);
        await WaitForSheetAsync(page, "Gallery");
        await PhoneRules.AssertAsync(page, "public gallery settings");
        await Done(page).TapAsync();
        await WaitForNoSheetAsync(page);
        await PhoneRules.AssertAsync(page, "public gallery");
        await page.Context.CloseAsync();
    }

    // ── the sheet and its controls ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Select_mark_taps_multi_select_without_the_sheet_and_the_bar_opens_it_and_desktop_is_unchanged()
    {
        await using var site = await fx.NewSiteAsync();
        var ids = await site.ImagesAsync(4);
        var gid = await site.GalleryAsync("Coast", ids);
        var page = await PhoneAsync(site, $"/admin/g/{gid}");

        // Closed: no panel shown, no bar.
        Assert.False(await page.Locator(".ws-panel > .side-panel").IsVisibleAsync());
        await WaitForBarAsync(page, null);

        // T075: select-mark taps toggle photos into the selection; the sheet stays closed, the bar counts.
        await Cells(page).Nth(1).Locator("label.select-mark").TapAsync();
        await WaitForBarAsync(page, 1);
        await Cells(page).Nth(2).Locator("label.select-mark").TapAsync();
        await WaitForBarAsync(page, 2);
        await Cells(page).Nth(3).Locator("label.select-mark").TapAsync();
        await WaitForBarAsync(page, 3);
        await WaitForNoSheetAsync(page);
        Assert.Equal([ids[1], ids[2], ids[3]], await SelectedAsync(page));
        await PhoneRules.AssertAsync(page, "three selected by the mark");
        // A second tap on a mark takes that photo out.
        await Cells(page).Nth(3).Locator("label.select-mark").TapAsync();
        await WaitForBarAsync(page, 2);
        Assert.Equal([ids[1], ids[2]], await SelectedAsync(page));
        await WaitForNoSheetAsync(page);

        // The bar's one Primary button opens the sheet on the selection panel (its gallery checklist); the bar hides.
        var add = Bar(page).Locator("button");
        Assert.Equal(1, await add.CountAsync());
        Assert.Equal("Add to gallery", (await add.TextContentAsync())!.Trim());
        Assert.Equal("btn btn-primary ws-selbar-add", await add.GetAttributeAsync("class"));
        await add.TapAsync();
        await WaitForSheetAsync(page, "2 photos selected");
        await WaitForBarAsync(page, null);
        Assert.True(await page.Locator(".ws-panel > .side-panel .ws-checks").IsVisibleAsync());
        await AssertSheetAsync(page, "Add to gallery");
        await PhoneRules.AssertAsync(page, "selection sheet");

        // Done closes the sheet and keeps the selection: the bar is back.
        await Done(page).TapAsync();
        await WaitForNoSheetAsync(page);
        await WaitForBarAsync(page, 2);
        Assert.Equal([ids[1], ids[2]], await SelectedAsync(page));

        // One photo: Add to gallery opens the photo panel.
        await Cells(page).Nth(2).Locator("label.select-mark").TapAsync();
        await WaitForBarAsync(page, 1);
        await Bar(page).Locator(".ws-selbar-add").TapAsync();
        await WaitForSheetAsync(page, "Photo");
        await Done(page).TapAsync();
        await WaitForNoSheetAsync(page);

        // A tap on the photo itself keeps today's behaviour: only it is selected, the sheet opens.
        await Cells(page).Nth(0).TapAsync();
        await WaitForSheetAsync(page, "Photo");
        Assert.Equal([ids[0]], await SelectedAsync(page));
        await WaitForBarAsync(page, null);
        await Done(page).TapAsync();
        await WaitForNoSheetAsync(page);
        await WaitForBarAsync(page, 1);

        // Clear selection: no selection, no bar, no sheet.
        Assert.True(await page.Locator(".ws-main .ws-clear").IsVisibleAsync());
        await page.Locator(".ws-main .ws-clear").TapAsync();
        await WaitForAsync(page, "() => document.querySelectorAll('.ws-main .ws-cell[aria-selected=\"true\"]').length === 0", what: "no selection");
        Assert.False(await page.Locator(".ws-main .ws-clear").IsVisibleAsync());
        Assert.False(await page.Locator(".ws-panel > .side-panel").IsVisibleAsync());
        await WaitForBarAsync(page, null);
        await page.Context.CloseAsync();

        // From 1200 px: no Gallery settings, no Done, no menu button, no bar; the panel in its column; the
        // select mark adds to the selection and the panel follows it at once.
        var desk = await site.OpenAsync($"/admin/g/{gid}");
        await desk.Locator("admin-workspace[data-script='ready']").WaitForAsync(new() { State = WaitForSelectorState.Attached, Timeout = WaitMs });
        Assert.Equal(1, await desk.Locator(".ws-main .ws-settings").CountAsync());
        Assert.False(await desk.Locator(".ws-main .ws-settings").IsVisibleAsync());
        Assert.False(await desk.Locator(".ws-panel > .side-panel .ws-done").IsVisibleAsync());
        Assert.Equal(1, await desk.Locator(".ws-list .ws-menu-toggle").CountAsync());
        Assert.False(await desk.Locator(".ws-list .ws-menu-toggle").IsVisibleAsync());
        Assert.True(await desk.Locator(".ws-list .ws-collections").IsVisibleAsync());
        await desk.Locator(".ws-main .ws-cell").Nth(0).ClickAsync();
        Assert.Equal("Photo", await PanelHeadingAsync(desk));
        Assert.False(await desk.Locator(".ws-panel > .side-panel .ws-done").IsVisibleAsync());
        Assert.Equal("static", await desk.Locator(".ws-panel").EvaluateAsync<string>("p => getComputedStyle(p).position"));
        Assert.False(await Bar(desk).IsVisibleAsync());
        await desk.Locator(".ws-main .ws-cell").Nth(1).Locator("label.select-mark").ClickAsync();
        Assert.Equal("2 photos selected", await PanelHeadingAsync(desk));
        Assert.True(await desk.Locator(".ws-panel > .side-panel").IsVisibleAsync());
        Assert.False(await Bar(desk).IsVisibleAsync());
        await desk.Context.CloseAsync();
    }

    // ── T074: the collections menu ───────────────────────────────────────────────────────────

    private static Task WaitForMenuAsync(IPage page, bool open) =>
        WaitForAsync(page, """
            open => {
              const b = document.querySelector('.ws-list .ws-menu-toggle');
              const m = document.getElementById(b.getAttribute('aria-controls'));
              return b.getAttribute('aria-expanded') === String(open) && (m.getClientRects().length > 0) === open;
            }
            """, open, open ? "the menu open" : "the menu closed");

    private static Task<string> MenuNameAsync(IPage page) =>
        page.EvaluateAsync<string>("() => document.querySelector('.ws-list .ws-menu-toggle .ws-menu-name').textContent");

    [Fact]
    public async Task Collections_list_is_a_menu_under_one_button()
    {
        await using var site = await fx.NewSiteAsync();
        var ids = await site.ImagesAsync(3);
        var coast = await site.GalleryAsync("Coast", ids.Take(2).ToList(), publish: true);
        var harbour = await site.GalleryAsync("Harbour", [ids[2]]);
        var page = await PhoneAsync(site, "/admin");

        // Closed: one button at the top of the workspace, full width, naming the open collection; no row shown.
        var toggle = page.Locator(".ws-list .ws-menu-toggle");
        Assert.Equal(1, await toggle.CountAsync());
        Assert.Equal("button", await toggle.EvaluateAsync<string>("b => b.localName"));
        Assert.Equal("btn btn-secondary ws-menu-toggle", await toggle.GetAttributeAsync("class"));
        Assert.Equal("ws-menu", await toggle.GetAttributeAsync("aria-controls"));
        Assert.Equal("All photos", await MenuNameAsync(page));
        Assert.Equal("▾", await toggle.EvaluateAsync<string>("b => b.querySelector('[aria-hidden=\"true\"]').textContent"));
        await WaitForMenuAsync(page, false);
        Assert.Equal(0, await page.Locator(".ws-list a.ws-coll-row:visible").CountAsync());
        var tb = (await toggle.BoundingBoxAsync())!;
        var lb = (await page.Locator(".ws-list").BoundingBoxAsync())!;
        Assert.Equal(0, lb.Y, 0.5);
        Assert.True(tb.Y < lb.Y + lb.Height && tb.Width >= PhoneRules.Width - 2 * 16 - 0.5, $"the button is at {tb.Y}, {tb.Width} px wide");
        await PhoneRules.AssertAsync(page, "menu closed");

        // Open: full width, in order All photos, Not in a gallery, the Galleries heading, each gallery (Private
        // mark, count), New gallery, View site, Sign out; every row at least 44 × 44.
        await toggle.TapAsync();
        await WaitForMenuAsync(page, true);
        var rows = await page.EvaluateAsync<string[]>("""
            () => [...document.querySelectorAll('#ws-menu .ws-collections > *, #ws-menu .ws-coll-foot > *')]
              .filter(el => el.getClientRects().length > 0 && !el.matches('.ws-coll-foot'))
              .map(el => el.textContent.trim().replace(/\s+/g, ' '))
            """);
        Assert.Equal(["All photos3", "Not in a gallery0", "Galleries in home page order", "Coast2", "HarbourPrivate1", "New gallery", "View site ↗", "Sign out"], rows);
        var menu = (await page.Locator("#ws-menu").BoundingBoxAsync())!;
        Assert.Equal(0, menu.X, 0.5);
        Assert.Equal(PhoneRules.Width, menu.Width, 0.5);
        Assert.True(menu.Y >= tb.Y + tb.Height - 0.5, $"the menu starts at {menu.Y}, above the button's bottom {tb.Y + tb.Height}");
        await PhoneRules.AssertAsync(page, "menu open");
        foreach (var name in new[] { "View site ↗", "Sign out", "New gallery" })
        {
            var b = (await page.Locator("#ws-menu .btn", new() { HasTextString = name }).BoundingBoxAsync())!;
            Assert.True(b.Width >= 44 && b.Height >= 44, $"{name} is {b.Width} × {b.Height}");
        }

        // The button again closes it; Escape closes it; a tap outside closes it and does nothing else.
        await toggle.TapAsync();
        await WaitForMenuAsync(page, false);
        await toggle.TapAsync();
        await WaitForMenuAsync(page, true);
        await page.Keyboard.PressAsync("Escape");
        await WaitForMenuAsync(page, false);
        await toggle.TapAsync();
        await WaitForMenuAsync(page, true);
        await page.Mouse.ClickAsync(PhoneRules.Width / 2, PhoneRules.Height - 20);
        await WaitForMenuAsync(page, false);
        Assert.Empty(await SelectedAsync(page));

        // A chrome re-read while open (the list is swapped) keeps it open and working.
        await toggle.TapAsync();
        await WaitForMenuAsync(page, true);
        await page.EvaluateAsync("() => { document.querySelector('#ws-menu .ws-collections').dataset.old = '1'; document.dispatchEvent(new CustomEvent('upload:stale')); }");
        await WaitForAsync(page, "() => !document.querySelector('#ws-menu .ws-collections').dataset.old", what: "the list re-read");
        await WaitForMenuAsync(page, true);
        Assert.Equal("All photos", await MenuNameAsync(page));

        // Choosing a collection navigates and closes it; the button names the new collection.
        await page.Locator($"#ws-menu a.ws-coll-row[data-gid='{harbour}']").TapAsync();
        await WaitForAsync(page, "g => location.pathname === `/admin/g/${g}` && document.querySelector('.ws-main .ws-head h1')?.textContent === 'Harbour'", harbour, "Harbour open");
        await WaitForMenuAsync(page, false);
        Assert.Equal("Harbour", await MenuNameAsync(page));
        await toggle.TapAsync();
        await WaitForMenuAsync(page, true);
        await page.Locator("#ws-menu a.ws-coll-row[href='/admin/unplaced']").TapAsync();
        await WaitForAsync(page, "() => location.pathname === '/admin/unplaced'", what: "Not in a gallery open");
        await WaitForMenuAsync(page, false);
        Assert.Equal("Not in a gallery", await MenuNameAsync(page));
        // Back: the address, the list's current row and the button follow; the menu stays closed.
        await page.GoBackAsync();
        await WaitForAsync(page, "() => document.querySelector('.ws-main .ws-head h1')?.textContent === 'Harbour'", what: "back to Harbour");
        Assert.Equal("Harbour", await MenuNameAsync(page));
        await WaitForMenuAsync(page, false);
        await page.Context.CloseAsync();

        // A gallery address names the gallery in the full document.
        var direct = await PhoneAsync(site, $"/admin/g/{coast}");
        Assert.Equal("Coast", await MenuNameAsync(direct));
        await direct.Context.CloseAsync();
    }
}

/// <summary>
/// 020 T058 (US2 at 390 px, SC-008): upload one photo into a gallery by touch. Its own class because the
/// upload pipeline needs the "upload-browser" fixture.
/// </summary>
[Collection("upload-browser")]
[Trait("Category", "Browser")]
public sealed class WorkspacePhoneUploadBrowserTests(UploadBrowserFixture fx)
{
    private const float WaitMs = 30_000;
    private const float LongWaitMs = 120_000;

    [Fact]
    public async Task Us2_by_touch_upload_one_photo_into_a_gallery()
    {
        var dir = Directory.CreateTempSubdirectory("gallery-ws-phone-");
        try
        {
            await using var site = await fx.NewSiteAsync();
            string gid;
            using (var scope = site.Services.CreateScope())
            {
                var created = await scope.ServiceProvider.GetRequiredService<GalleryService>().CreateAsync("Coast");
                Assert.True(created.IsOk, created.Reason);
                gid = created.GalleryId!;
            }
            var page = await PhoneRules.OpenAsync(fx.Browser, await site.NewContextAsync(), site.BaseUrl, $"/admin/g/{gid}", fx.Storage.ImageBaseUrl!, WaitMs);
            await PhoneRules.AssertAsync(page, "empty gallery");

            await page.Locator(".ws-main .ws-head .ws-add").TapAsync();
            await page.Locator("upload-tray > .ws-tray").WaitForAsync(new() { Timeout = WaitMs });
            Assert.Equal("Add photos to Coast", await page.EvaluateAsync<string>("() => document.querySelector('upload-tray .ws-tray-head')?.textContent"));
            await PhoneRules.AssertAsync(page, "tray open");

            var source = Path.Combine(Render.RepoRoot, "tests", "fixtures", "upload", "gps-camera.jpg");
            if (!File.Exists(source)) source = Path.Combine(Render.RepoRoot, "tests", "fixtures", "gps-camera.jpg");
            var file = Path.Combine(dir.FullName, "harbour.jpg");
            File.Copy(source, file);
            await page.Locator("upload-tray input.dz-input").SetInputFilesAsync(file);
            await page.WaitForFunctionAsync("() => document.querySelectorAll('upload-tray .ws-up .ws-up-ok').length === 1", null,
                new() { Timeout = LongWaitMs, PollingInterval = 50 });
            await page.WaitForFunctionAsync("() => document.querySelectorAll('.ws-main .ws-cell').length === 1", null,
                new() { Timeout = WaitMs, PollingInterval = 50 });
            await PhoneRules.AssertAsync(page, "one added");

            await page.Locator("upload-tray button[name='tray'][value='done']").TapAsync();
            await page.WaitForFunctionAsync("() => document.querySelector('upload-tray').hidden", null, new() { Timeout = WaitMs, PollingInterval = 50 });

            var images = await site.Db.GetCollection<ImageDoc>("images").Find(FilterDefinition<ImageDoc>.Empty).ToListAsync();
            var image = Assert.Single(images);
            Assert.Equal([gid], image.GalleryIds);
            var coast = await site.Db.GetCollection<GalleryDoc>("galleries").Find(g => g.Id == gid).SingleAsync();
            Assert.Equal([image.Id], coast.Items);
            await PhoneRules.AssertAsync(page, "tray closed");
            await page.Context.CloseAsync();
        }
        finally
        {
            dir.Delete(true);
        }
    }
}

/// <summary>The phone context (390 × 844, touch) and the SC-008 checks, shared by both phone classes.</summary>
internal static class PhoneRules
{
    public const int Width = 390;
    public const int Height = 844;

    /// <summary>
    /// A 390 × 844 touch context carrying the owner session of <paramref name="ownerContext"/> (a site
    /// context with the session cookie), at <paramref name="path"/>, loaded, workspace.js ready. Image
    /// requests answer 404 at once.
    /// </summary>
    public static async Task<IPage> OpenAsync(IBrowser browser, IBrowserContext ownerContext, string baseUrl, string path, string imageBaseUrl, float waitMs = 15_000)
    {
        var https = baseUrl.Replace("http://", "https://", StringComparison.Ordinal) + "/";
        var cookies = await ownerContext.CookiesAsync([https]);
        Assert.NotEmpty(cookies);
        var ctx = await browser.NewContextAsync(new()
        {
            ViewportSize = new() { Width = Width, Height = Height },
            HasTouch = true,
            IsMobile = true,
            DeviceScaleFactor = 2,
        });
        await ctx.RouteAsync(imageBaseUrl + "/**", r => r.FulfillAsync(new() { Status = 404 }));
        await ctx.AddCookiesAsync(cookies.Select(c => new Cookie
        {
            Name = c.Name, Value = c.Value, Url = https, Secure = c.Secure, HttpOnly = c.HttpOnly, SameSite = c.SameSite,
        }));
        var page = await ctx.NewPageAsync();
        var resp = await page.GotoAsync(baseUrl + path);
        Assert.Equal(200, resp!.Status);
        await BrowserWait.LoadedAsync(page, waitMs);
        Assert.Equal(path, new Uri(page.Url).AbsolutePath);
        await page.Locator("admin-workspace[data-script='ready']").WaitForAsync(new() { State = WaitForSelectorState.Attached, Timeout = waitMs });
        return page;
    }

    /// <summary>
    /// SC-008 at this moment: the page does not scroll sideways, and every visible control's box is at
    /// least 44 × 44 px. A control is a link, button, field, summary or a label that is the tap target
    /// of a visually hidden box (select mark, switch, checklist row, drop zone); a box hidden inside a
    /// label is measured as its label, and a 1 × 1 px box with no label is not shown. Controls in
    /// templates and closed dialogs are not shown.
    /// </summary>
    public static async Task AssertAsync(IPage page, string when)
    {
        var r = await page.EvaluateAsync<JsonElement>("""
            () => {
              const bad = [];
              const seen = new Set();
              let count = 0;
              const sel = 'a[href], button, input:not([type="hidden"]), textarea, select, summary, label.select-mark, label.switch, label.ws-checks-row';
              for (const el of document.querySelectorAll(sel)) {
                if (el.closest('template') || el.closest('dialog:not([open])')) continue;
                let box = el;
                if (el.matches('input')) {
                  const own = el.getBoundingClientRect();
                  const clipped = own.width <= 1 && own.height <= 1;
                  if (el.matches('[type="checkbox"], [type="radio"], [type="file"]') || clipped) box = el.closest('label') ?? el;
                  // A visually hidden (1 × 1 px) box with no label is not something a finger can see or tap.
                  if (box === el && clipped) continue;
                }
                if (seen.has(box)) continue;
                seen.add(box);
                const b = box.getBoundingClientRect();
                if (box.getClientRects().length === 0 || getComputedStyle(box).visibility === 'hidden') continue;
                count++;
                if (b.width < 43.5 || b.height < 43.5) {
                  const text = (box.textContent || box.getAttribute('aria-label') || box.getAttribute('name') || '').trim().replace(/\s+/g, ' ').slice(0, 30);
                  bad.push(`${box.localName}.${String(box.className).trim().replace(/\s+/g, '.')} "${text}" ${b.width.toFixed(1)}x${b.height.toFixed(1)}`);
                }
              }
              return { sw: document.documentElement.scrollWidth, iw: innerWidth, count, bad };
            }
            """);
        var sw = r.GetProperty("sw").GetDouble();
        var iw = r.GetProperty("iw").GetDouble();
        var bad = r.GetProperty("bad").EnumerateArray().Select(b => b.GetString()).ToList();
        Assert.True(iw == Width, $"{when}: the window is {iw} px wide");
        Assert.True(sw <= iw, $"{when}: the page is {sw} px wide in a {iw} px window (sideways scroll)");
        Assert.True(r.GetProperty("count").GetInt32() >= 3, $"{when}: only {r.GetProperty("count")} controls found");
        Assert.True(bad.Count == 0, $"{when}: controls under 44 × 44 px:\n" + string.Join("\n", bad));
    }
}
