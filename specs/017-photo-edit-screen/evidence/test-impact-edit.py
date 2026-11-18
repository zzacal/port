#!/usr/bin/env python3
"""017 stated test changes (contracts/test-impact.md, classes A, B and D). Parent-only.

Each row asserts the old string's exact count (in the text as left by the rows before it) before
anything is written; any mismatch aborts with nothing changed. Run from the repo root:
    DRYRUN=1 uv run --no-project python specs/017-photo-edit-screen/evidence/test-impact-edit.py   # counts only
    uv run --no-project python specs/017-photo-edit-screen/evidence/test-impact-edit.py A9 B2      # apply rows
Row A2 covers A2 and A3 (the same redirect string twice in LibraryMembershipEditTests). The old
blocks of A13, A15, A16, A17, D1 and D2 were sliced from the live files at T005, so they match by
construction. D2 also deletes OpenParam, whose last callers are the deleted and rewritten facts.
"""
import os
import pathlib
import sys

T = pathlib.Path("tests/Gallery.Web.Tests")

# (row, class, file, old, new, expected count)
ROWS = [
    ('A1', 'A', T / 'LibraryEditTests.cs',
     '$"/admin/library?page=1&open={img.Id}&saved=1"',
     '$"/admin/library/images/{img.Id}?page=1&saved=1"', 1),
    ('A2', 'A', T / 'LibraryMembershipEditTests.cs',
     '$"/admin/library?page=1&open={img.Id}&saved=1"',
     '$"/admin/library/images/{img.Id}?page=1&saved=1"', 2),
    ('A4', 'A', T / 'EdgeModeTests.cs',
     '$"/admin/library?page=1&open={a.Id}&saved=1"',
     '$"/admin/library/images/{a.Id}?page=1&saved=1"', 1),
    ('A5', 'A', T / 'MembershipTimingEvidenceTests.cs',
     '$"/admin/library?page=1&open={iid}&saved=1"',
     '$"/admin/library/images/{iid}?page=1&saved=1"', 1),
    ('A6', 'A', T / 'LibraryTimingEvidenceTests.cs',
     '$"/admin/library?page=1&open={iid}&saved=1"',
     '$"/admin/library/images/{iid}?page=1&saved=1"', 1),
    ('A7', 'A', T / 'LibraryEditTests.cs',
     '$"/admin/library?open={img.Id}"',
     '$"/admin/library/images/{img.Id}"', 2),
    ('A7', 'A', T / 'LibraryEditTests.cs',
     '$"/admin/library?open={loose.Id}"',
     '$"/admin/library/images/{loose.Id}"', 1),
    ('A8', 'A', T / 'LibraryPageTests.cs',
     '        var html = await GetOkAsync(h, $"/admin/library?open={img.Id}");\n        var aside = Regex.Match(html, "<aside class=\\"side-panel\\"',
     '        var html = await GetOkAsync(h, $"/admin/library/images/{img.Id}");\n        var aside = Regex.Match(html, "<aside class=\\"side-panel\\"', 1),
    ('A8', 'A', T / 'LibraryPageTests.cs',
     '        var bar = Regex.Match(html, "<div class=\\"selection-bar\\"[^>]*>").Value;',
     '        // 017 A8 (stated change): the selection bar and the picker are on the library, not the edit screen.\n        var library = await GetOkAsync(h, "/admin/library");\n        var bar = Regex.Match(library, "<div class=\\"selection-bar\\"[^>]*>").Value;', 1),
    ('A8', 'A', T / 'LibraryPageTests.cs',
     '        var picker = Regex.Match(html, "<input[^>]*name=\\"newTitle\\"[^>]*>").Value;',
     '        var picker = Regex.Match(library, "<input[^>]*name=\\"newTitle\\"[^>]*>").Value;', 1),
    ('A9', 'A', T / 'GalleryFieldBrowserTests.cs',
     'OpenAsync(site, $"/admin/library?open={p}")',
     'OpenAsync(site, $"/admin/library/images/{p}")', 6),
    ('A9', 'A', T / 'GalleryFieldBrowserTests.cs',
     'OpenAsync(site, $"/admin/library?open={s.Ids[0]}")',
     'OpenAsync(site, $"/admin/library/images/{s.Ids[0]}")', 4),
    ('A9', 'A', T / 'GalleryFieldBrowserTests.cs',
     'OpenAsync(site, $"/admin/library?open={s.Ids[1]}")',
     'OpenAsync(site, $"/admin/library/images/{s.Ids[1]}")', 4),
    ('A9', 'A', T / 'GalleryFieldBrowserTests.cs',
     'OpenAsync(site, $"/admin/library?open={ids[0]}")',
     'OpenAsync(site, $"/admin/library/images/{ids[0]}")', 2),
    ('A10', 'A', T / 'LibraryBrowserTests.cs',
     '        // Library, side panel open with a selection: the panel\'s Save is the one primary.\n        var panel = await site.OpenAsync($"/admin/library?open={ids[0]}");\n        await Marks(panel).Nth(1).ClickAsync();\n        Assert.True(await Bar(panel).IsVisibleAsync());\n        Assert.Equal(["Save"], (await FilledAsync(panel)).Outside);\n        await AssertDangerOutlinedAsync(panel, "library, side panel");',
     '        // 017 A10 (stated change): the photo edit screen (no grid, so no selection): its Save is the one primary.\n        var panel = await site.OpenAsync($"/admin/library/images/{ids[0]}");\n        Assert.Equal(["Save"], (await FilledAsync(panel)).Outside);\n        await AssertDangerOutlinedAsync(panel, "photo edit screen");', 1),
    ('A10', 'A', T / 'LibraryBrowserTests.cs',
     '        var page = await site.OpenAsync($"/admin/library?open={ids[0]}");\n        var trigger = page.Locator(".side-panel .danger-confirm .danger-trigger");',
     '        var page = await site.OpenAsync($"/admin/library/images/{ids[0]}");\n        var trigger = page.Locator(".side-panel .danger-confirm .danger-trigger");', 1),
    ('A11', 'A', T / 'LibraryPageTests.cs',
     'Assert.Matches($"<a class=\\"icon-button pa-edit\\" href=\\"\\\\?page=1&open={img.Id}#t-{img.Id}\\"", row);',
     'Assert.Matches($"<a class=\\"icon-button pa-edit\\" href=\\"/admin/library/images/{img.Id}\\\\?page=1\\"", row); // 017 A11', 1),
    ('A12', 'A', T / 'LibraryMembershipTests.cs',
     'Assert.Equal($"?page=1&open={iid}#t-{iid}", Attr(group, "href"));',
     'Assert.Equal($"/admin/library/images/{iid}?page=1", Attr(group, "href")); // 017 A12', 1),
    ('A13', 'A', T / 'LibraryPageTests.cs',
     '        // With ?open: the panel\'s Save is the one primary and Add renders Secondary.\n        var open = await GetOkAsync(h, $"/admin/library?open={img.Id}");\n        Assert.Equal(["Save"], PrimaryButtons(open));\n        Assert.Matches("<button class=\\"btn btn-secondary\\"[^>]*>\\\\s*Add to gallery…\\\\s*</button>", open);\n        // UI-4: Delete photo (trigger and the confirm dialog\'s action) is outlined Danger, never Primary.\n        Assert.Matches("<button [^>]*class=\\"btn btn-danger danger-trigger\\" type=\\"button\\"[^>]*>(Delete photo…|Delete photo&#x2026;)</button>", open);\n        Assert.Matches("<button class=\\"btn btn-danger\\"[^>]*form=\\"lib-del-one\\"[^>]*>\\\\s*Delete photo\\\\s*</button>", open);\n        Assert.DoesNotMatch("btn-primary[^>]*>\\\\s*Delete", open);',
     '        // 017 A13 (stated change): an old ?open address is the plain library, so Add stays the one primary.\n        var old = await GetOkAsync(h, $"/admin/library?open={img.Id}");\n        Assert.Equal(["Add to gallery…"], PrimaryButtons(old));\n\n        // On the photo edit screen the panel\'s Save is the one primary.\n        var open = await GetOkAsync(h, $"/admin/library/images/{img.Id}");\n        Assert.Equal(["Save"], PrimaryButtons(open));\n        // UI-4: Delete photo (trigger and the confirm dialog\'s action) is outlined Danger, never Primary.\n        Assert.Matches("<button [^>]*class=\\"btn btn-danger danger-trigger\\" type=\\"button\\"[^>]*>(Delete photo…|Delete photo&#x2026;)</button>", open);\n        Assert.Matches("<button class=\\"btn btn-danger\\"[^>]*form=\\"lib-del-one\\"[^>]*>\\\\s*Delete photo\\\\s*</button>", open);\n        Assert.DoesNotMatch("btn-primary[^>]*>\\\\s*Delete", open);', 1),
    ('A14', 'A', T / 'LibraryPageTests.cs',
     'new[] { "/admin/library", $"/admin/library?open={img.Id}" }',
     'new[] { "/admin/library", $"/admin/library/images/{img.Id}" }', 1),
    ('A15', 'A', T / 'PhotoActionsTests.cs',
     '    public async Task Open_photo_is_the_one_active_tile_and_its_icons_close_the_editor()\n    {\n        await using var h = await LibraryHost.CreateAsync(mongo, s3);\n        var a = await ArrangeAsync(h);\n        var html = await GetOkAsync(h, $"/admin/library?open={a.One}");\n        var ids = LibraryHtml.TileIds(html);\n        Assert.Equal(3, ids.Count);\n\n        var active = new List<string>();\n        foreach (var iid in ids)\n        {\n            var row = Assert.Single(LibraryHtml.Blocks(LibraryHtml.TileOf(html, iid), "div", "photo-actions"));\n            var group = ControlOf(row, "pa-group");\n            var edit = ControlOf(row, "pa-edit");\n            var expected = iid == a.One ? $"?page=1#t-{iid}" : $"?page=1&open={iid}#t-{iid}";\n            Assert.Equal(expected, Attr(edit, "href"));\n            Assert.Equal(expected, Attr(group, "href"));\n            Assert.Equal(Attr(group, "aria-current"), Attr(edit, "aria-current"));\n            if (Attr(edit, "aria-current") == "true") active.Add(iid);\n        }\n        Assert.Equal([a.One], active);\n        // On the whole page: exactly the open tile\'s group and edit icons are current.\n        Assert.Equal([$"pa-group t-{a.One}", $"pa-edit t-{a.One}"], CurrentPhotoControls(html));\n    }',
     '    public async Task Old_open_param_marks_no_tile_and_every_icon_opens_the_edit_screen()\n    {\n        await using var h = await LibraryHost.CreateAsync(mongo, s3);\n        var a = await ArrangeAsync(h);\n        // 017 A15 (stated change): ?open is no longer read. No tile is current, and every tile\'s gallery\n        // and edit icons open that photo\'s edit screen.\n        var html = await GetOkAsync(h, $"/admin/library?open={a.One}");\n        var ids = LibraryHtml.TileIds(html);\n        Assert.Equal(3, ids.Count);\n\n        foreach (var iid in ids)\n        {\n            var row = Assert.Single(LibraryHtml.Blocks(LibraryHtml.TileOf(html, iid), "div", "photo-actions"));\n            var group = ControlOf(row, "pa-group");\n            var edit = ControlOf(row, "pa-edit");\n            Assert.Equal($"/admin/library/images/{iid}?page=1", Attr(edit, "href"));\n            Assert.Equal($"/admin/library/images/{iid}?page=1", Attr(group, "href"));\n            Assert.Null(Attr(edit, "aria-current"));\n            Assert.Null(Attr(group, "aria-current"));\n        }\n        Assert.Empty(CurrentPhotoControls(html));\n    }', 1),
    ('A16', 'A', T / 'TileIconBrowserTests.cs',
     '    public async Task SC003_with_js_edit_and_gallery_toggle_one_panel_at_a_time(int width, int height)\n    {\n        await using var site = await fx.NewSiteAsync();\n        var ids = await site.ImagesAsync(2, "Toggle");\n        var (a, b) = (ids[0], ids[1]);\n        var page = await OpenAsync(site, "/admin/library", width, height);\n        await ToggleStepsAsync(page, a, b, checkViewport: true);\n    }\n\n    [Fact]\n    public async Task SC003_without_js_the_same_three_steps_work()\n    {\n        await using var site = await fx.NewSiteAsync();\n        var ids = await site.ImagesAsync(2, "Toggle");\n        var (ctx, page) = await NoScriptAsync(fx, site);\n        try\n        {\n            await GotoAsync(page, site, "/admin/library");\n            Assert.False(await page.EvaluateAsync<bool>("() => document.querySelector(\'.selection-bar\')?.dataset.script === \'ready\'"));\n            await ToggleStepsAsync(page, ids[0], ids[1], checkViewport: false);\n        }\n        finally\n        {\n            await ctx.DisposeAsync();\n        }\n    }\n\n    private static async Task ToggleStepsAsync(IPage page, string a, string b, bool checkViewport)\n    {\n        // 1. A\'s pencil opens A.\n        await ClickNavigatesAsync(page, $"#t-{a} .pa-edit");\n        Assert.Equal(a, OpenParam(page));\n        Assert.True(await page.Locator("#lib-edit").IsVisibleAsync());\n        Assert.Equal([$"t-{a}"], await ActiveEditsAsync(page));\n        Assert.Equal([$"t-{a}"], await ActiveGroupsAsync(page));\n\n        // 2. B\'s folder opens B, with its Galleries field in view.\n        await ClickNavigatesAsync(page, $"#t-{b} .pa-group");\n        Assert.Equal(b, OpenParam(page));\n        Assert.True(await page.Locator("#lib-edit").IsVisibleAsync());\n        Assert.EndsWith($"/admin/library/images/{b}", await page.Locator("#lib-edit").GetAttributeAsync("action"));\n        if (checkViewport)\n            Assert.True(await InViewportAsync(page, "#lib-edit .combobox"), "B\'s Galleries field is not in the viewport");\n        Assert.Equal([$"t-{b}"], await ActiveEditsAsync(page));\n        Assert.Equal([$"t-{b}"], await ActiveGroupsAsync(page));\n\n        // 3. B\'s pencil closes the editor.\n        await ClickNavigatesAsync(page, $"#t-{b} .pa-edit");\n        Assert.Null(OpenParam(page));\n        Assert.Equal(0, await page.Locator("#lib-edit").CountAsync());\n        Assert.Empty(await ActiveEditsAsync(page));\n        Assert.Empty(await ActiveGroupsAsync(page));\n    }',
     '    public async Task SC003_with_js_edit_and_gallery_open_the_photo_edit_screen(int width, int height)\n    {\n        await using var site = await fx.NewSiteAsync();\n        var ids = await site.ImagesAsync(2, "Toggle");\n        var (a, b) = (ids[0], ids[1]);\n        var page = await OpenAsync(site, "/admin/library", width, height);\n        await OpenStepsAsync(page, site, a, b, checkViewport: true);\n    }\n\n    [Fact]\n    public async Task SC003_without_js_the_same_steps_work()\n    {\n        await using var site = await fx.NewSiteAsync();\n        var ids = await site.ImagesAsync(2, "Toggle");\n        var (ctx, page) = await NoScriptAsync(fx, site);\n        try\n        {\n            await GotoAsync(page, site, "/admin/library");\n            Assert.False(await page.EvaluateAsync<bool>("() => document.querySelector(\'.selection-bar\')?.dataset.script === \'ready\'"));\n            await OpenStepsAsync(page, site, ids[0], ids[1], checkViewport: false);\n        }\n        finally\n        {\n            await ctx.DisposeAsync();\n        }\n    }\n\n    /// <summary>017 A16 (stated change): the icons open the photo\'s own edit screen; nothing toggles.</summary>\n    private static async Task OpenStepsAsync(IPage page, LibrarySite site, string a, string b, bool checkViewport)\n    {\n        // 1. A\'s pencil opens A\'s edit screen: the panel and one photo, no grid.\n        await ClickNavigatesAsync(page, $"#t-{a} .pa-edit");\n        Assert.Equal(($"/admin/library/images/{a}", "?page=1"), (new Uri(page.Url).AbsolutePath, new Uri(page.Url).Query));\n        Assert.True(await page.Locator("#lib-edit").IsVisibleAsync());\n        Assert.Equal(1, await page.Locator("main picture").CountAsync());\n        Assert.Equal(0, await page.Locator(".lib-tile").CountAsync());\n\n        // 2. Back on the library no tile is current.\n        await GotoAsync(page, site, "/admin/library");\n        Assert.Empty(await ActiveEditsAsync(page));\n        Assert.Empty(await ActiveGroupsAsync(page));\n\n        // 3. B\'s folder opens B\'s edit screen, with its Galleries field in view.\n        await ClickNavigatesAsync(page, $"#t-{b} .pa-group");\n        Assert.Equal(($"/admin/library/images/{b}", "?page=1"), (new Uri(page.Url).AbsolutePath, new Uri(page.Url).Query));\n        Assert.EndsWith($"/admin/library/images/{b}", await page.Locator("#lib-edit").GetAttributeAsync("action"));\n        if (checkViewport)\n            Assert.True(await InViewportAsync(page, "#lib-edit .combobox"), "B\'s Galleries field is not in the viewport");\n    }', 1),
    ('A17', 'A', T / 'TileIconBrowserTests.cs',
     '    public async Task FR010_no_icon_is_filled_at_rest_and_only_Save_with_a_panel_open_and_A_active()\n    {\n        await using var site = await fx.NewSiteAsync();\n        var ids = await site.ImagesAsync(3);\n        await site.GalleryAsync("Coast", [ids[0]], publish: true);\n\n        var rest = await OpenAsync(site, "/admin/library");\n        Assert.Empty(await FilledOutsideAsync(rest));\n\n        var open = await OpenAsync(site, $"/admin/library?open={ids[0]}");\n        Assert.Equal([$"t-{ids[0]}"], await ActiveEditsAsync(open));\n        Assert.Equal(["Save"], await FilledOutsideAsync(open));\n    }',
     '    public async Task FR010_no_icon_is_filled_at_rest_and_only_Save_on_the_edit_screen()\n    {\n        await using var site = await fx.NewSiteAsync();\n        var ids = await site.ImagesAsync(3);\n        await site.GalleryAsync("Coast", [ids[0]], publish: true);\n\n        var rest = await OpenAsync(site, "/admin/library");\n        Assert.Empty(await FilledOutsideAsync(rest));\n        // 017 A17 (stated change): no tile is ever current; the edit screen\'s Save is its one filled button.\n        Assert.Empty(await ActiveEditsAsync(rest));\n\n        var open = await OpenAsync(site, $"/admin/library/images/{ids[0]}");\n        Assert.Equal(["Save"], await FilledOutsideAsync(open));\n    }', 1),
    ('A18', 'A', T / 'LibraryAccessTests.cs',
     '        ["L5"] = ("POST", "/admin/library/images/{iid}/delete"),\n',
     '        ["L5"] = ("POST", "/admin/library/images/{iid}/delete"),\n        ["L6"] = ("GET", "/admin/library/images/{iid}"), // 017 A18\n', 1),
    ('A18', 'A', T / 'LibraryAccessTests.cs',
     '    public void The_route_table_is_the_15_routes_of_the_contract()\n    {\n        Assert.Equal(15, Routes.Count);\n        Assert.Equal(["L1", "L2", "L3", "L4", "L5", "G1",',
     '    public void The_route_table_is_the_16_routes_of_the_contract()\n    {\n        Assert.Equal(16, Routes.Count);\n        Assert.Equal(["L1", "L2", "L3", "L4", "L5", "L6", "G1",', 1),
    ('B1', 'B', T / 'EdgeSecretTests.cs',
     '                     ("GET /admin/library?open= (L1)", $"/admin/library?page=1&open={a.Id}"),\n',
     '                     ("GET /admin/library?open= (L1)", $"/admin/library?page=1&open={a.Id}"),\n                     ("GET /admin/library/images/{iid} (L6)", $"/admin/library/images/{a.Id}?page=1"), // 017 B1\n', 1),
    ('B2', 'B', T / 'LayoutConsistencyBrowserTests.cs',
     '("builder", $"/admin/galleries/{gid}") };',
     '("builder", $"/admin/galleries/{gid}"), ("photo", $"/admin/library/images/{ids[0]}") }; // + photo (017 B2)', 1),
    ('D1', 'D', T / 'GridWidthBrowserTests.cs',
     'private enum Grid { Library, LibraryOpen, Builder, List }',
     'private enum Grid { Library, Builder, List } // LibraryOpen removed (017 D1)', 1),
    ('D1', 'D', T / 'GridWidthBrowserTests.cs',
     'Grid.Library or Grid.LibraryOpen =>',
     'Grid.Library =>', 2),
    ('D1', 'D', T / 'GridWidthBrowserTests.cs',
     '        (Grid.LibraryOpen, 1200) => 2,                // grid 752 px < 800\n        (Grid.LibraryOpen, 1300 or 1440 or 1920) => 4, // grid 852 px and more; 153 px tiles fit a 140 px row\n',
     '', 1),
    ('D1', 'D', T / 'GridWidthBrowserTests.cs',
     '    [Fact]\n    public async Task SC007_library_with_the_panel_open_drops_to_2_columns_when_its_grid_is_under_800px()\n    {\n        await using var site = await fx.NewSiteAsync();\n        var ids = await site.ImagesAsync(5);\n        await site.GalleryAsync("Coast", [ids[0], ids[1]], publish: true);\n        var errors = await CheckAsync(site, Grid.LibraryOpen, $"/admin/library?open={ids[0]}", 5);\n        Assert.True(errors.Count == 0, string.Join("\\n", errors));\n    }\n\n',
     '', 1),
    ('D2', 'D', T / 'TileIconBrowserTests.cs',
     '    // ── panel in view: sticky aside (K7) and J1 ──────────────────────────────────────────────\n\n    [Fact]\n    public async Task At_1440_the_sticky_panel_is_in_view_beside_the_40th_tile()\n    {\n        await using var site = await fx.NewSiteAsync();\n        await site.ImagesAsync(45, "Many");\n        var page = await OpenAsync(site, "/admin/library");\n        var tile = page.Locator("section.lib .lib-tile").Nth(39);\n        await tile.ScrollIntoViewIfNeededAsync();\n        var id = await tile.GetAttributeAsync("id");\n        await ClickNavigatesAsync(page, $"#{id} .pa-edit");\n\n        Assert.True(await page.EvaluateAsync<double>("() => scrollY") > 1000, "the page did not land at the 40th tile");\n        Assert.True(await InViewportAsync(page, $"#{id}"), "the 40th tile is not in view");\n        Assert.True(await InViewportAsync(page, "#lib-edit"), "the panel\'s top is not in the viewport");\n    }\n\n    [Fact]\n    public async Task At_390_with_js_the_stacked_panels_galleries_field_is_brought_into_view()\n    {\n        await using var site = await fx.NewSiteAsync();\n        await site.ImagesAsync(45, "Many");\n        var page = await OpenAsync(site, "/admin/library", 390, 844);\n        var tile = page.Locator("section.lib .lib-tile").Nth(39);\n        await tile.ScrollIntoViewIfNeededAsync();\n        var id = await tile.GetAttributeAsync("id");\n        await ClickNavigatesAsync(page, $"#{id} .pa-edit");\n\n        Assert.Equal(id![2..], OpenParam(page));\n        Assert.True(await InViewportAsync(page, "#lib-edit .combobox"), "the Galleries field is not in the viewport");\n    }\n\n',
     '', 1),
    ('D2', 'D', T / 'TileIconBrowserTests.cs',
     '    private static string? OpenParam(IPage page)\n    {\n        var q = System.Web.HttpUtility.ParseQueryString(new Uri(page.Url).Query);\n        return q["open"];\n    }\n\n',
     '', 1),
]


def main() -> int:
    ids = {r[0] for r in ROWS}
    want = set(sys.argv[1:]) or ids
    unknown = want - ids
    if unknown:
        print(f"unknown rows: {sorted(unknown)}")
        return 2
    dry = os.environ.get("DRYRUN") == "1"
    texts, bad = {}, 0
    for row, cls, path, old, new, n in ROWS:
        if row not in want:
            continue
        text = texts.get(path) or path.read_text(encoding="utf-8")
        got = text.count(old)
        if got != n:
            print(f"FAIL {row} {path} x{got} (expected {n}): {old[:60]!r}")
            bad += 1
            continue
        texts[path] = text.replace(old, new)
        print(f"OK {cls} {row} {path} x{got}: {old[:50]!r} -> {new[:50]!r}")
    if bad:
        print("aborted, nothing written")
        return 1
    if dry:
        print("DRYRUN: nothing written")
        return 0
    for path, text in texts.items():
        path.write_text(text, encoding="utf-8")
    print(f"wrote {len(texts)} files")
    return 0


if __name__ == "__main__":
    sys.exit(main())
