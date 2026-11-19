using System.Text.Json;
using Gallery.Web.Tests.Fixtures;
using Microsoft.Playwright;

namespace Gallery.Web.Tests;

/// <summary>
/// 020 T048 (IX.4, FR-038): the workspace frame does not move between contexts. At 1440 × 900 the
/// boxes of <c>.ws-list</c>, <c>.ws-main</c> and <c>.ws-panel</c>, and the gap between the top of
/// <c>.ws-main</c> and its first box, are measured in a real browser for a library collection, a
/// gallery, one photo selected, several photos selected and the upload tray open, and compared for
/// equality within 0.5 px against the first. Rows for "photo large" (Phase 7) and 390 px (Phase 8)
/// are added later.
/// </summary>
[Collection("library-browser")]
[Trait("Category", "Browser")]
public sealed class WorkspaceLayoutBrowserTests(LibraryBrowserFixture fx)
{
    private const float WaitMs = 15_000;
    private const double Tolerance = 0.5;

    /// <summary>[list x, y, w, h, main x, y, w, h, panel x, y, w, h, gap above main's first box].</summary>
    private const string Frame = """
        () => {
          const box = (sel) => {
            const r = document.querySelector(sel).getBoundingClientRect();
            return [r.x, r.y, r.width, r.height];
          };
          const main = document.querySelector('.ws-main');
          const first = [...main.children].find((c) => c.getClientRects().length > 0);
          const gap = first.getBoundingClientRect().top - main.getBoundingClientRect().top;
          return [...box('.ws-list'), ...box('.ws-main'), ...box('.ws-panel'), gap, first.localName + '.' + first.className];
        }
        """;

    private static readonly string[] Names =
    [
        "list x", "list y", "list width", "list height", "main x", "main y", "main width", "main height",
        "panel x", "panel y", "panel width", "panel height", "gap above main's first box",
    ];

    private static async Task<IPage> OpenAsync(LibrarySite site, string path)
    {
        var ctx = await site.NewContextAsync(1440, 900);
        var page = await ctx.NewPageAsync();
        await page.Mouse.MoveAsync(0, 0);
        var resp = await page.GotoAsync(site.BaseUrl + path);
        Assert.Equal(200, resp!.Status);
        await BrowserWait.LoadedAsync(page, WaitMs);
        await page.Locator("admin-workspace[data-script='ready']").WaitForAsync(new() { State = WaitForSelectorState.Attached, Timeout = WaitMs });
        return page;
    }

    private static async Task<(double[] Values, string First)> MeasureAsync(IPage page)
    {
        var raw = await page.EvaluateAsync<JsonElement>(Frame);
        var values = Enumerable.Range(0, Names.Length).Select(i => raw[i].GetDouble()).ToArray();
        return (values, raw[Names.Length].GetString()!);
    }

    [Fact]
    public async Task Frame_geometry_is_the_same_in_every_context_at_1440()
    {
        await using var site = await fx.NewSiteAsync();
        var ids = await site.ImagesAsync(6);
        var gid = await site.GalleryAsync("Coast", ids.Take(3).ToList());
        var cells = ".ws-main .ws-cell";

        var contexts = new (string Name, string Path, Func<IPage, Task> Act, string Check)[]
        {
            ("library collection", "/admin", _ => Task.CompletedTask, "() => document.querySelector('.ws-panel > .side-panel > h2')?.textContent === 'Library'"),
            ("gallery", $"/admin/g/{gid}", _ => Task.CompletedTask, "() => document.querySelector('.ws-panel > .side-panel > h2')?.textContent === 'Gallery'"),
            ("one photo", "/admin", p => p.Locator(cells).Nth(0).ClickAsync(), "() => document.querySelector('.ws-panel > .side-panel > h2')?.textContent === 'Photo'"),
            ("several photos", "/admin", async p =>
            {
                await p.Locator(cells).Nth(0).ClickAsync();
                await p.Locator(cells).Nth(2).ClickAsync(new() { Modifiers = [KeyboardModifier.Shift] });
            }, "() => document.querySelector('.ws-panel > .side-panel > h2')?.textContent === '3 photos selected'"),
            ("upload area open", "/admin", async p =>
            {
                await p.Locator(".ws-main .ws-head .ws-add").ClickAsync();
                await p.Locator("upload-tray > .ws-tray").WaitForAsync(new() { Timeout = WaitMs });
            }, "() => document.querySelector('upload-tray > .ws-tray') !== null"),
        };

        double[]? expected = null;
        var firsts = new List<string>();
        foreach (var (name, path, act, check) in contexts)
        {
            var page = await OpenAsync(site, path);
            await act(page);
            await page.WaitForFunctionAsync(check, null, new() { Timeout = WaitMs, PollingInterval = 25 });
            var (values, first) = await MeasureAsync(page);
            firsts.Add(first);
            Assert.True(values[2] > 0 && values[6] > 0 && values[10] > 0, $"{name}: a region has no width [{string.Join(", ", values)}]");
            expected ??= values;
            for (var i = 0; i < Names.Length; i++)
                Assert.True(Math.Abs(values[i] - expected[i]) <= Tolerance,
                    $"{name}: {Names[i]} is {values[i]:0.##} px, the library collection's is {expected[i]:0.##} px");
            await page.Context.CloseAsync();
        }

        // The frame at 1440: list 232 px, panel 320 px, main between them, all the viewport's height,
        // and nothing above main's first box.
        Assert.Equal([232, 320], new[] { expected![2], expected[10] });
        Assert.Equal(1440 - 232 - 320, expected[6], Tolerance);
        Assert.All(new[] { expected[1], expected[5], expected[9] }, y => Assert.Equal(0, y, Tolerance));
        Assert.All(new[] { expected[3], expected[7], expected[11] }, h => Assert.Equal(900, h, Tolerance));
        Assert.Equal(0, expected[12], Tolerance);
        // The open tray is main's first box; otherwise the heading row is.
        Assert.Equal(["div.ws-head", "div.ws-head", "div.ws-head", "div.ws-head", "upload-tray."], firsts.Select(f => f.Split(' ')[0]).ToArray());
    }

    // ── T056 (FR-038, SC-007): the "photo large" context at 1440 ───────────────────────────

    [Fact]
    public async Task Frame_geometry_photo_large_equals_the_library_collection_at_1440()
    {
        await using var site = await fx.NewSiteAsync();
        await site.ImagesAsync(6);

        var library = await OpenAsync(site, "/admin");
        await library.WaitForFunctionAsync("() => document.querySelector('.ws-panel > .side-panel > h2')?.textContent === 'Library'", null, new() { Timeout = WaitMs, PollingInterval = 25 });
        var (expected, _) = await MeasureAsync(library);
        await library.Context.CloseAsync();

        var page = await OpenAsync(site, "/admin");
        await page.Locator(".ws-main .ws-cell").Nth(2).DblClickAsync();
        await page.WaitForFunctionAsync("""
            () => { const v = document.querySelector('.ws-main .ws-largeview'); return !v.hidden && v.querySelector('.ws-stage-frame .pic') !== null
              && document.querySelector('.ws-panel > .side-panel > h2')?.textContent === 'Photo'; }
            """, null, new() { Timeout = WaitMs, PollingInterval = 25 });
        var (values, first) = await MeasureAsync(page);
        for (var i = 0; i < Names.Length; i++)
            Assert.True(Math.Abs(values[i] - expected[i]) <= Tolerance,
                $"photo large: {Names[i]} is {values[i]:0.##} px, the library collection's is {expected[i]:0.##} px");
        // The stage is main's first (and only) box, and fills main.
        Assert.Equal("div.ws-largeview", first);
        var stage = (await page.Locator(".ws-main .ws-largeview").BoundingBoxAsync())!;
        Assert.Equal([values[4], values[5], values[6], values[7]], new[] { (double)stage.X, stage.Y, stage.Width, stage.Height }, new ToleranceComparer(Tolerance));
        await page.Context.CloseAsync();
    }

    private sealed class ToleranceComparer(double tolerance) : IEqualityComparer<double>
    {
        public bool Equals(double x, double y) => Math.Abs(x - y) <= tolerance;
        public int GetHashCode(double obj) => 0;
    }

    // ── T058 (FR-038, SC-007): every context at 390 × 844 ──────────────────────────────────

    /// <summary>
    /// Below 1200 px the frame is one column: the collections strip across the top (its row of --tap plus
    /// --space-s above and below), main under it at full width with nothing above its first box, and the
    /// panel either not shown (no sheet) or a sheet fixed to the window's bottom. The strip and main's
    /// position and width must be equal in every context (main's height is its content's); the page never
    /// scrolls sideways.
    /// </summary>
    [Fact]
    public async Task Frame_geometry_is_the_same_in_every_context_at_390()
    {
        await using var site = await fx.NewSiteAsync();
        var ids = await site.ImagesAsync(6);
        var gid = await site.GalleryAsync("Coast", ids.Take(3).ToList());
        var cells = ".ws-main .ws-cell";
        const string Heading = "() => document.querySelector('.ws-panel > .side-panel > h2')?.textContent";

        var contexts = new (string Name, string Path, Func<IPage, Task> Act, string Check, bool Sheet)[]
        {
            ("library collection", "/admin", _ => Task.CompletedTask, $"() => ({Heading})() === 'Library'", false),
            ("gallery", $"/admin/g/{gid}", _ => Task.CompletedTask, $"() => ({Heading})() === 'Gallery'", false),
            ("gallery settings", $"/admin/g/{gid}", p => p.Locator(".ws-main .ws-settings").ClickAsync(), $"() => ({Heading})() === 'Gallery'", true),
            ("one photo", "/admin", p => p.Locator(cells).Nth(0).ClickAsync(), $"() => ({Heading})() === 'Photo'", true),
            ("several photos", "/admin", p => p.Keyboard.PressAsync("ControlOrMeta+A"), $"() => ({Heading})() === '6 photos selected'", true),
            // Below 1200 px a photo opens large from its sheet (a first tap's sheet covers the cell's second tap).
            ("photo large", "/admin", async p =>
            {
                await p.Locator(cells).Nth(2).ClickAsync();
                await p.Locator(".ws-panel > .side-panel .ws-large").ClickAsync();
            },
                "() => !document.querySelector('.ws-main .ws-largeview').hidden && document.querySelector('.ws-main .ws-stage-frame .pic') !== null", false),
            ("upload area open", "/admin", async p =>
            {
                await p.Locator(".ws-main .ws-head .ws-add").ClickAsync();
                await p.Locator("upload-tray > .ws-tray").WaitForAsync(new() { Timeout = WaitMs });
            }, "() => document.querySelector('upload-tray > .ws-tray') !== null", false),
        };

        // Sheet: [panel x, y, w, h, position, side-panel shown, window width, window height, page scroll width].
        const string Sheet = """
            () => {
              const p = document.querySelector('.ws-panel'); const r = p.getBoundingClientRect();
              const sp = document.querySelector('.ws-panel > .side-panel');
              return [r.x, r.y, r.width, r.height, getComputedStyle(p).position, String(!!sp && sp.getClientRects().length > 0),
                innerWidth, innerHeight, document.documentElement.scrollWidth];
            }
            """;
        int[] compared = [0, 1, 2, 3, 4, 5, 6, 12];
        double[]? expected = null;
        var firsts = new List<string>();
        foreach (var (name, path, act, check, sheet) in contexts)
        {
            var ctx = await site.NewContextAsync(390, 844);
            var page = await ctx.NewPageAsync();
            await page.Mouse.MoveAsync(0, 0);
            Assert.Equal(200, (await page.GotoAsync(site.BaseUrl + path))!.Status);
            await BrowserWait.LoadedAsync(page, WaitMs);
            await page.Locator("admin-workspace[data-script='ready']").WaitForAsync(new() { State = WaitForSelectorState.Attached, Timeout = WaitMs });
            await act(page);
            await page.WaitForFunctionAsync(check, null, new() { Timeout = WaitMs, PollingInterval = 25 });
            var (values, first) = await MeasureAsync(page);
            firsts.Add(first);
            expected ??= values;
            foreach (var i in compared)
                Assert.True(Math.Abs(values[i] - expected[i]) <= Tolerance,
                    $"{name}: {Names[i]} is {values[i]:0.##} px, the library collection's is {expected[i]:0.##} px");

            var s = await page.EvaluateAsync<JsonElement>(Sheet);
            double S(int i) => s[i].GetDouble();
            Assert.True(S(8) <= S(6), $"{name}: the page is {S(8)} px wide in a {S(6)} px window");
            Assert.Equal(sheet.ToString().ToLowerInvariant(), s[5].GetString());
            if (sheet)
            {
                Assert.Equal("fixed", s[4].GetString());
                Assert.True(Math.Abs(S(0)) <= Tolerance && Math.Abs(S(2) - S(6)) <= Tolerance, $"{name}: the sheet is at x {S(0)}, {S(2)} px wide");
                Assert.True(Math.Abs(S(1) + S(3) - S(7)) <= Tolerance, $"{name}: the sheet ends at {S(1) + S(3)}, the window at {S(7)}");
                Assert.True(S(1) >= 4 * 48 - Tolerance, $"{name}: the sheet starts at {S(1)}");
            }
            else
            {
                Assert.True(S(3) <= Tolerance, $"{name}: the panel region takes {S(3)} px with no sheet");
            }
            await page.Context.CloseAsync();
        }

        // One column: the strip at the top, full width, one row of --tap plus --space-s above and below;
        // main under it, full width, nothing above its first box.
        Assert.Equal(new double[] { 0, 0, 390, 44 + 2 * 16 }, expected![..4], new ToleranceComparer(Tolerance));
        Assert.Equal(new double[] { 0, 44 + 2 * 16, 390 }, expected[4..7], new ToleranceComparer(Tolerance));
        Assert.Equal(0, expected[12], Tolerance);
        Assert.Equal(["div.ws-head", "div.ws-head", "div.ws-head", "div.ws-head", "div.ws-head", "div.ws-largeview", "upload-tray."],
            firsts.Select(f => f.Split(' ')[0]).ToArray());
    }
}
