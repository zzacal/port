using System.Text.Json;
using System.Text.RegularExpressions;
using Gallery.Web.Tests.Fixtures;
using Microsoft.Playwright;

namespace Gallery.Web.Tests;

/// <summary>
/// 010 T037-T039: navigation measured in a real browser (research R-13). Every measured assertion
/// prints what it measured. Tests that write (rename, remove) restore the seeded state in a
/// <c>finally</c>, so no test depends on another's order. Paths are read from Mongo each time
/// (<see cref="NavBrowserFixture.SlugAsync"/>), never cached across a write.
/// </summary>
[Collection("nav-browser")]
[Trait("Category", "Browser")]
public sealed class NavBrowserTests(NavBrowserFixture site)
{
    private const string S = NavBrowserFixture.SeptemberId;
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private static int HeightFor(int width) => width < 600 ? 844 : 900;

    private async Task<(IBrowserContext Ctx, IPage Page)> Open(string path, int width = 1440, bool js = true, bool prerender = false)
    {
        site.ResetHits();
        var ctx = await site.NewContextAsync(width, HeightFor(width), js, prerender);
        var page = await ctx.NewPageAsync();
        await page.GotoAsync(site.BaseUrl + path, new() { WaitUntil = WaitUntilState.NetworkIdle });
        return (ctx, page);
    }

    private static string Path(IPage page) => new Uri(page.Url).PathAndQuery + new Uri(page.Url).Fragment;

    /// <summary>
    /// Waits up to <paramref name="ms"/> for the URL to satisfy <paramref name="until"/> and returns the
    /// path either way, so a key that did nothing fails on the caller's assertion, not a timeout.
    /// </summary>
    private static async Task<string> PathAfter(IPage page, Func<string, bool> until, int ms = 15000)
    {
        // Polls page.Url: a key-driven navigation can commit before any wait is registered.
        var deadline = DateTime.UtcNow.AddMilliseconds(ms);
        while (!until(Path(page)))
        {
            if (DateTime.UtcNow > deadline) return $"{Path(page)} (timed out after {ms} ms)";
            await Task.Delay(50);
        }
        // The document's own readyState, not Playwright's lifecycle tracking: Playwright intermittently
        // misses the load event of a link navigation that has already completed (decisions row 59).
        try { await page.WaitForFunctionAsync("document.readyState === 'complete'", null, new() { Timeout = ms }); }
        catch (Exception e) when (e is PlaywrightException or TimeoutException) { return $"{Path(page)} (not loaded after {ms} ms)"; }
        return Path(page);
    }

    /// <summary>Waits <paramref name="ms"/>, then returns the path: for keys that must do nothing.</summary>
    private static async Task<string> PathAfterNothing(IPage page, int ms = 700)
    {
        await page.WaitForTimeoutAsync(ms);
        await page.WaitForLoadStateAsync(LoadState.Load);
        return Path(page);
    }

    private sealed record Box(string Name, double L, double T, double R, double B)
    {
        public double W => R - L;
        public double H => B - T;
        public override string ToString() => $"{Name}[l={L:0.#} t={T:0.#} w={W:0.#} h={H:0.#}]";
        public bool Overlaps(Box o) => Math.Min(R, o.R) - Math.Max(L, o.L) > 0.5 && Math.Min(B, o.B) - Math.Max(T, o.T) > 0.5;
    }

    private static async Task<List<Box>> Boxes(IPage page, string selectorsJson)
    {
        var json = await page.EvaluateAsync<string>($$"""
            JSON.stringify({{selectorsJson}}.flatMap(([name, sel]) => [...document.querySelectorAll(sel)].map(e => {
                const r = e.getBoundingClientRect();
                return { name, l: r.left, t: r.top + scrollY, r: r.right, b: r.bottom + scrollY };
            })))
            """);
        return JsonSerializer.Deserialize<List<Box>>(json, Json)!;
    }

    /// <summary>The tile element's box against the viewport, plus scrollY: "l,t,r,b vw×vh scrollY".</summary>
    // Synchronous on purpose: with JavaScript off, requestAnimationFrame callbacks never run, so a
    // promise waiting on one never settles. A short wait lets the fragment scroll settle first.
    private static async Task<(bool Inside, string Measured)> TileInView(IPage page, string imageId)
    {
        await page.WaitForTimeoutAsync(150);
        var m = await page.EvaluateAsync<double[]?>($$"""
            (() => {
                const e = document.getElementById('p-{{imageId}}');
                if (!e) return null;
                const r = e.getBoundingClientRect(), d = document.documentElement;
                return [r.left, r.top, r.right, r.bottom, d.clientWidth, d.clientHeight, scrollY];
            })()
            """);
        if (m is null) return (false, $"#p-{imageId} not found on {page.Url}");
        var inside = m[0] >= -0.5 && m[1] >= -0.5 && m[2] <= m[4] + 0.5 && m[3] <= m[5] + 0.5;
        return (inside, $"#p-{imageId} l={m[0]:0.#} t={m[1]:0.#} r={m[2]:0.#} b={m[3]:0.#} viewport={m[4]}x{m[5]} scrollY={m[6]:0.#} url={page.Url}");
    }

    // ── Fixture guards ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Fixture_is_styled_and_the_projector_wrote_the_neighbours()
    {
        var (ctx, page) = await Open(await site.GalleryPathAsync(NavBrowserFixture.CoastId));
        await using var _ = ctx;
        var css = await page.EvaluateAsync<int>("document.querySelector('style').textContent.length");
        Assert.True(css > 10_000, $"inlined stylesheet is {css} chars");
        var links = await page.EvaluateAsync<string[]>("[...document.querySelectorAll('nav.gallery-neighbours a')].map(a => a.getAttribute('href') + ' ' + a.textContent)");
        Assert.Equal([$"{await site.GalleryPathAsync(S)} ← {NavBrowserFixture.SeptemberTitle}", $"{await site.GalleryPathAsync(NavBrowserFixture.SoloId)} Solo →"], links);
    }

    // ── T037 SC-001: the gallery link lands on the photo's tile ───────────────────────────────

    [Theory]
    [InlineData(1440)]
    [InlineData(390)]
    public async Task Gallery_link_lands_on_the_tile_for_every_photo(int width)
    {
        var ctx = await site.NewContextAsync(width, HeightFor(width));
        await using var _ = ctx;
        var page = await ctx.NewPageAsync();
        var failures = new List<string>();
        for (var i = 0; i < NavBrowserFixture.PhotoCount; i++)
        {
            var id = site.PhotoIds[i];
            // Park the pointer: since review 2 the crumb sits at the same spot on every page, so the
            // previous click would leave it hovering the next page's crumb and start the moderate
            // speculation rule mid-test (decisions row 53).
            await page.Mouse.MoveAsync(width - 1, HeightFor(width) - 1);
            await page.GotoAsync(site.BaseUrl + await site.PhotoPathAsync(i), new() { WaitUntil = WaitUntilState.Load });
            await page.Locator(".breadcrumb a[href*=\"#p-\"]").ClickAsync();
            var path = await PathAfter(page, p => p.EndsWith($"#p-{id}"));
            var (inside, measured) = await TileInView(page, id);
            if (!path.EndsWith($"#p-{id}") || !inside)
            {
                var diag = await page.EvaluateAsync<string>("(() => { const n = performance.getEntriesByType('navigation')[0]; return `ready=${document.readyState} type=${n.type} activationStart=${n.activationStart} loadEnd=${n.loadEventEnd} prerendering=${document.prerendering}`; })()");
                failures.Add($"photo {i + 1}: path={path} {measured} {diag}");
            }
        }
        Assert.True(failures.Count == 0, $"{failures.Count} of 23 photos at {width}px did not land on the tile:\n{string.Join("\n", failures)}");
    }

    [Theory]
    [InlineData(1440)]
    [InlineData(390)]
    public async Task Gallery_link_lands_on_the_tile_with_javascript_off(int width)
    {
        var (ctx, page) = await Open(await site.PhotoPathAsync(17), width, js: false);
        await using var _ = ctx;
        await page.Locator(".breadcrumb a[href*=\"#p-\"]").ClickAsync();
        var id = site.PhotoIds[17];
        var path = await PathAfter(page, p => p.EndsWith($"#p-{id}"));
        var (inside, measured) = await TileInView(page, id);
        Assert.True(path.EndsWith($"#p-{id}") && inside, $"JS off at {width}px: path={path} {measured}");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(11)]
    [InlineData(22)]
    public async Task Escape_lands_on_the_tile(int i)
    {
        var (ctx, page) = await Open(await site.PhotoPathAsync(i));
        await using var _ = ctx;
        await page.Keyboard.PressAsync("Escape");
        var id = site.PhotoIds[i];
        var path = await PathAfter(page, p => p.EndsWith($"#p-{id}"));
        var (inside, measured) = await TileInView(page, id);
        Assert.True(path == $"{await site.GalleryPathAsync(S)}#p-{id}" && inside, $"Escape on photo {i + 1}: path={path} {measured}");
    }

    // R-01-a: hovering the link (speculation rules, moderate) should make Chrome prerender the
    // gallery page, and the click then ACTIVATES it; it must still scroll to the fragment.
    // The hover-then-click landing is asserted here on the prerender-ENABLED browser.
    [Fact]
    public async Task Hover_then_click_lands_on_the_tile()
    {
        var (ctx, page) = await Open(await site.PhotoPathAsync(15), prerender: true);
        await using var _ = ctx;
        var link = page.Locator(".breadcrumb a[href*=\"#p-\"]");
        await link.HoverAsync();
        await page.WaitForTimeoutAsync(1000);
        await link.ClickAsync();
        var id = site.PhotoIds[15];
        var path = await PathAfter(page, p => p.EndsWith($"#p-{id}"));
        var activation = await page.EvaluateAsync<double>("performance.getEntriesByType('navigation')[0].activationStart");
        var (inside, measured) = await TileInView(page, id);
        Assert.True(path.EndsWith($"#p-{id}") && inside, $"hover then click: path={path} activationStart={activation:0.#} {measured}");
    }

    // The guard of the case above: the gallery page must really have been prerendered and
    // activated, or the hover case proves nothing about R-01-a. Under Playwright it cannot be:
    // Chrome refuses every prerender while a DevTools client is attached to the page
    // (Preload.prerenderStatusUpdated: "PrerenderingDisabledByDevTools", Chrome 156, also after
    // Page.setPrerenderingAllowed(true)), so activationStart is always 0. Escalated to the parent
    // (Batch F blocker); the assertion is kept, not weakened.
    [Fact(Skip = "Batch F blocker: Chrome disables prerender under DevTools (PrerenderingDisabledByDevTools); parent decision")]
    public async Task Hover_then_click_activates_a_prerendered_gallery_page_at_the_tile()
    {
        var (ctx, page) = await Open(await site.PhotoPathAsync(15), prerender: true);
        await using var _ = ctx;
        var link = page.Locator(".breadcrumb a[href*=\"#p-\"]");
        await link.HoverAsync();
        await page.WaitForTimeoutAsync(1000);
        await link.ClickAsync();
        var id = site.PhotoIds[15];
        var path = await PathAfter(page, p => p.EndsWith($"#p-{id}"));
        var activation = await page.EvaluateAsync<double>("performance.getEntriesByType('navigation')[0].activationStart");
        var (inside, measured) = await TileInView(page, id);
        Assert.True(activation > 0, $"gallery page was not activated from a prerender (activationStart={activation}); {measured}");
        Assert.True(path.EndsWith($"#p-{id}") && inside, $"prerendered activation: path={path} activationStart={activation:0.#} {measured}");
    }

    [Fact]
    public async Task A_stale_gallery_link_after_a_rename_still_lands_on_the_tile()
    {
        var id = site.PhotoIds[19];
        var (ctx, page) = await Open(await site.PhotoPathAsync(19));
        await using var _ = ctx;
        var oldHref = await page.Locator(".breadcrumb a[href*=\"#p-\"]").GetAttributeAsync("href");
        try
        {
            var slug = await site.RenameAsync(S, "September Sixth Roll");
            Assert.Equal("september-sixth-roll", slug);
            await page.Locator(".breadcrumb a[href*=\"#p-\"]").ClickAsync(); // the page still carries the old slug
            var path = await PathAfter(page, p => p.Contains(slug));
            var (inside, measured) = await TileInView(page, id);
            Assert.True(path == $"/g/{S}/{slug}#p-{id}" && inside, $"old href {oldHref} → path={path} {measured}");
        }
        finally
        {
            await site.RenameAsync(S, NavBrowserFixture.SeptemberTitle);
        }
    }

    [Fact]
    public async Task A_removed_photo_anchor_opens_the_gallery_at_the_top()
    {
        var id = site.PhotoIds[10];
        var order = await site.ItemsAsync(S);
        await site.RemovePhotoAsync(S, id);
        try
        {
            var ctx = await site.NewContextAsync();
            await using var _ = ctx;
            var page = await ctx.NewPageAsync();
            var response = await page.GotoAsync($"{site.BaseUrl}{await site.GalleryPathAsync(S)}#p-{id}", new() { WaitUntil = WaitUntilState.NetworkIdle });
            await page.WaitForTimeoutAsync(150);
            var scrollY = await page.EvaluateAsync<double>("scrollY");
            var present = await page.EvaluateAsync<bool>($"document.getElementById('p-{id}') !== null");
            Assert.True(response!.Status == 200 && scrollY == 0 && !present,
                $"status={response.Status} scrollY={scrollY} element present={present}");
        }
        finally
        {
            await site.RestorePhotoAsync(S, id, order);
        }
        Assert.Equal(order, await site.ItemsAsync(S));
    }

    // ── T038 SC-003: tap boxes, stacking and centring ────────────────────────────────────────

    private static readonly int[] TapWidths = [360, 390, 600, 1024, 1440];

    public static TheoryData<int> TitleLengths() => [10, 30, 70];

    private static string TitleOf(int length) => length switch
    {
        10 => "Coastlines",
        30 => "A long weekend on the north co",
        70 => "A very long gallery title that keeps going well past the narrow budget",
        _ => throw new ArgumentOutOfRangeException(nameof(length)),
    };

    [Theory]
    [MemberData(nameof(TitleLengths))]
    public async Task Tap_boxes_stacking_and_centring(int titleLength)
    {
        var title = TitleOf(titleLength);
        Assert.Equal(titleLength, title.Length);
        await site.RenameAsync(S, title);
        try
        {
            var ctx = await site.NewContextAsync();
            await using var _ = ctx;
            var page = await ctx.NewPageAsync();
            var failures = new List<string>();
            foreach (var width in TapWidths)
            {
                await page.SetViewportSizeAsync(width, HeightFor(width));
                foreach (var i in new[] { 0, 11, 22 })
                {
                    await page.GotoAsync(site.BaseUrl + await site.PhotoPathAsync(i), new() { WaitUntil = WaitUntilState.Load });
                    var where = $"{width}px photo {i + 1} title {titleLength}";
                    var boxes = await Boxes(page, """[["prev","article.solo a[rel=prev]"],["next","article.solo a[rel=next]"],["crumb",".breadcrumb a"]]""");
                    var expected = new List<string> { "crumb", "crumb", "crumb" }; // 011: Home, Galleries, the gallery
                    if (i > 0) expected.Add("prev");
                    if (i < 22) expected.Add("next");
                    if (!boxes.Select(b => b.Name).Order().SequenceEqual(expected.Order()))
                        failures.Add($"{where}: links [{string.Join(", ", boxes)}], expected [{string.Join(", ", expected)}]");
                    CheckBoxes(boxes, where, failures);

                    // Owner review 1: Previous/Next are whole words (no ellipsis), the count sits
                    // centred in the Pager on one line, the breadcrumb (the header itself since review 2) is
                    // one left-aligned line above the stage, and the stage keeps 80vh.
                    var c = await page.EvaluateAsync<double[]>("""
                        (() => {
                            const words = [...document.querySelectorAll('article.solo .pager a')]
                                .filter(a => a.scrollWidth > a.clientWidth + 0.5 || !['Previous', 'Next'].includes(a.textContent)).length;
                            const n = document.querySelector('article.solo .photo-count'), nr = n.getBoundingClientRect();
                            const pr = document.querySelector('article.solo .pager').getBoundingClientRect();
                            const bc = document.querySelector('nav.breadcrumb'), ol = (() => { const r = document.createRange(); r.selectNodeContents(bc.querySelector('a')); return r.getBoundingClientRect(); })();
                            const hd = document.querySelector('header.site-header').getBoundingClientRect();
                            const st = document.querySelector('article.solo .stage').getBoundingClientRect();
                            const tops = new Set([...bc.querySelectorAll('li')].map(li => { const r = li.getBoundingClientRect(); return Math.round((r.top + r.bottom) / 2); }));
                            return [words, nr.left, nr.right, pr.left, pr.right, n.getClientRects().length,
                                    ol.left, ol.right, document.documentElement.clientWidth, tops.size,
                                    (bc.closest('header.site-header') ? hd.top : 1e9), bc.getBoundingClientRect().top, bc.getBoundingClientRect().bottom, st.top,
                                    st.height, innerHeight, document.documentElement.scrollWidth];
                        })()
                        """);
                    if (c[0] != 0) failures.Add($"{where}: {c[0]} Pager link(s) truncated or not a whole word");
                    if (c[5] != 1) failures.Add($"{where}: count wraps ({c[5]} lines)");
                    var offset = (c[1] + c[2]) / 2 - (c[3] + c[4]) / 2;
                    if (Math.Abs(offset) > 2) failures.Add($"{where}: count off centre by {offset:0.##}px (count {c[1]:0.#}-{c[2]:0.#}, pager {c[3]:0.#}-{c[4]:0.#})");
                    // Owner review 2 (R3): left-aligned; the first crumb's TEXT starts on the page margin.
                    var margin = width < 600 ? 16 : width < 1200 ? 32 : 48;
                    if (Math.Abs(c[6] - margin) > 1) failures.Add($"{where}: breadcrumb text starts at {c[6]:0.#}px, page margin {margin}px");
                    if (c[9] != 1) failures.Add($"{where}: breadcrumb wraps onto {c[9]} lines");
                    if (!(c[10] <= c[11] + 0.5 && c[12] <= c[13] + 0.5)) failures.Add($"{where}: breadcrumb {c[11]:0.#}-{c[12]:0.#} not inside the header (top {c[10]:0.#}) and above the stage top {c[13]:0.#}");
                    if (Math.Abs(c[14] - 0.8 * c[15]) > 0.5) failures.Add($"{where}: .stage height {c[14]:0.##} != 0.8 * innerHeight {c[15]}");
                    if (c[16] > c[8] + 0.5) failures.Add($"{where}: horizontal scroll ({c[16]} > {c[8]})");
                }

                // The gallery neighbour links (Coast sits between the renamed gallery and Solo).
                await page.GotoAsync(site.BaseUrl + await site.GalleryPathAsync(NavBrowserFixture.CoastId), new() { WaitUntil = WaitUntilState.Load });
                var nb = await Boxes(page, """[["neighbour-prev","nav.gallery-neighbours a[rel=prev]"],["neighbour-next","nav.gallery-neighbours a[rel=next]"]]""");
                if (nb.Count != 2) failures.Add($"{width}px Coast: neighbour links [{string.Join(", ", nb)}]");
                CheckBoxes(nb, $"{width}px Coast neighbours title {titleLength}", failures);
            }
            Assert.True(failures.Count == 0, string.Join("\n", failures));
        }
        finally
        {
            await site.RenameAsync(S, NavBrowserFixture.SeptemberTitle);
        }
    }

    private static void CheckBoxes(List<Box> boxes, string where, List<string> failures)
    {
        foreach (var b in boxes)
            if (b.W < 44 || b.H < 44) failures.Add($"{where}: {b} is smaller than 44x44");
        for (var x = 0; x < boxes.Count; x++)
            for (var y = x + 1; y < boxes.Count; y++)
                if (boxes[x].Overlaps(boxes[y])) failures.Add($"{where}: {boxes[x]} overlaps {boxes[y]}");
    }

    // ── T038 FR-005: keys ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Right_left_and_escape_navigate()
    {
        var (ctx, page) = await Open(await site.PhotoPathAsync(5));
        await using var _ = ctx;
        await page.Keyboard.PressAsync("ArrowRight");
        Assert.Equal(await site.PhotoPathAsync(6), await PathAfter(page, p => p.EndsWith(site.PhotoIds[6])));
        await page.Keyboard.PressAsync("ArrowLeft");
        Assert.Equal(await site.PhotoPathAsync(5), await PathAfter(page, p => p.EndsWith(site.PhotoIds[5])));
        await page.Keyboard.PressAsync("Escape");
        Assert.Equal($"{await site.GalleryPathAsync(S)}#p-{site.PhotoIds[5]}", await PathAfter(page, p => p.Contains("#p-")));
    }

    [Theory]
    [InlineData("Control+ArrowRight")]
    [InlineData("Meta+ArrowRight")]
    [InlineData("Shift+ArrowRight")]
    [InlineData("Control+Escape")]
    public async Task Modified_keys_are_not_intercepted(string chord)
    {
        var photo = await site.PhotoPathAsync(5);
        var (ctx, page) = await Open(photo);
        await using var _ = ctx;
        await page.Keyboard.PressAsync(chord);
        Assert.Equal(photo, await PathAfterNothing(page));
    }

    // Alt+Left is Back in Chrome on Windows and Linux; on macOS it is a caret move and Back is
    // Cmd+[. Either way the handler must not turn it into Previous. History is photo 12 → photo 6,
    // so Back (photo 12), nothing (photo 6) and an intercepted Previous (photo 5) are told apart;
    // the expected outcome is the one this platform's browser gives.
    [Fact]
    public async Task Alt_left_is_not_intercepted()
    {
        var (ctx, page) = await Open(await site.PhotoPathAsync(11));
        await using var _ = ctx;
        var photo = await site.PhotoPathAsync(5);
        await page.GotoAsync(site.BaseUrl + photo, new() { WaitUntil = WaitUntilState.Load });
        await page.EvaluateAsync("window.__clicked = []; document.addEventListener('click', e => window.__clicked.push(e.target.closest('a')?.getAttribute('href')), true)");
        await page.Keyboard.PressAsync("Alt+ArrowLeft");
        var expected = OperatingSystem.IsMacOS() ? photo : await site.PhotoPathAsync(11);
        var after = OperatingSystem.IsMacOS() ? await PathAfterNothing(page) : await PathAfter(page, p => p == expected);
        Assert.True(after == expected,
            $"Alt+Left went to {after}, expected {expected} (photo 6 is {photo}; Back is photo 12; the handler's Previous would be photo 5)");
        if (OperatingSystem.IsMacOS())
        {
            var clicked = await page.EvaluateAsync<string[]>("window.__clicked");
            Assert.True(clicked.Length == 0, $"Alt+Left clicked [{string.Join(", ", clicked)}]");
        }
    }

    [Theory]
    [InlineData("ArrowRight")]
    [InlineData("ArrowLeft")]
    [InlineData("Escape")]
    public async Task A_key_in_a_focused_input_does_nothing(string key)
    {
        var photo = await site.PhotoPathAsync(5);
        var (ctx, page) = await Open(photo);
        await using var _ = ctx;
        await page.EvaluateAsync("document.body.append(Object.assign(document.createElement('input'), { id: 'probe' }))");
        await page.FocusAsync("#probe");
        await page.Keyboard.PressAsync(key);
        Assert.Equal(photo, await PathAfterNothing(page));
    }

    [Theory]
    [InlineData(22, "ArrowRight")]
    [InlineData(0, "ArrowLeft")]
    public async Task Keys_past_the_edge_do_nothing(int i, string key)
    {
        var photo = await site.PhotoPathAsync(i);
        var (ctx, page) = await Open(photo);
        await using var _ = ctx;
        await page.Keyboard.PressAsync(key);
        Assert.Equal(photo, await PathAfterNothing(page));
    }

    // ── T038 FR-006: keys keep the predictive loading ────────────────────────────────────────

    [Fact]
    public async Task After_right_the_next_photo_is_served_from_the_prefetch()
    {
        var (ctx, page) = await Open(await site.PhotoPathAsync(2));
        await using var _ = ctx;
        await page.WaitForTimeoutAsync(500);
        var warmed = site.HitFiles();
        await page.Keyboard.PressAsync("ArrowRight");
        var path = await PathAfter(page, p => p.EndsWith(site.PhotoIds[3]));
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        var main = await page.EvaluateAsync<string>("document.querySelector('article.solo picture img').currentSrc.split('/').pop()");
        Assert.Equal(await site.PhotoPathAsync(3), path);
        Assert.True(warmed.Contains(main), $"photo 3's page warmed [{string.Join(", ", warmed)}], photo 4 shows {main}");
        Assert.True(site.Hits(main) == 1, $"{main} was requested {site.Hits(main)} times");
    }

    [Fact]
    public async Task Keys_work_with_save_data()
    {
        var ctx = await site.NewContextAsync();
        await using var _ = ctx;
        await ctx.AddInitScriptAsync("Object.defineProperty(Navigator.prototype, 'connection', { get: () => ({ saveData: true }) })");
        var page = await ctx.NewPageAsync();
        site.ResetHits();
        await page.GotoAsync(site.BaseUrl + await site.PhotoPathAsync(2), new() { WaitUntil = WaitUntilState.NetworkIdle });
        await page.WaitForTimeoutAsync(500);
        // Guards the guard: Save-Data is really on (the neighbour prefetch did not run).
        var neighbours = await page.EvaluateAsync<string>("document.querySelector('article.solo').dataset.prefetch");
        var prefetched = neighbours.Split('|')
            .SelectMany(s => s.Split(',').Select(c => c.Trim().Split(' ')[0].Split('/').Last()))
            .Where(f => site.Hits(f) > 0).ToList();
        Assert.True(prefetched.Count == 0, $"Save-Data emulation off: prefetched [{string.Join(", ", prefetched)}]");

        await page.Keyboard.PressAsync("ArrowRight");
        Assert.Equal(await site.PhotoPathAsync(3), await PathAfter(page, p => p.EndsWith(site.PhotoIds[3])));
        await page.Keyboard.PressAsync("ArrowLeft");
        Assert.Equal(await site.PhotoPathAsync(2), await PathAfter(page, p => p.EndsWith(site.PhotoIds[2])));
        await page.Keyboard.PressAsync("Escape");
        Assert.Equal($"{await site.GalleryPathAsync(S)}#p-{site.PhotoIds[2]}", await PathAfter(page, p => p.Contains("#p-")));
    }

    // ── T038 UI-4: the keys are silent ───────────────────────────────────────────────────────

    [Fact]
    public async Task Nothing_mentions_the_keys()
    {
        var hint = new Regex(@"arrow|escape|\besc\b|keyboard|shortcut|\bkeys?\b", RegexOptions.IgnoreCase);
        var failures = new List<string>();
        foreach (var path in new[] { "/", "/g", await site.GalleryPathAsync(S), await site.PhotoPathAsync(0), await site.PhotoPathAsync(11), await site.PhotoPathAsync(22) })
        {
            var (ctx, page) = await Open(path);
            await using var _ = ctx;
            var texts = await page.EvaluateAsync<string[]>("""
                [document.body.innerText,
                 ...[...document.querySelectorAll('[title]')].map(e => 'title=' + e.getAttribute('title')),
                 ...[...document.querySelectorAll('[aria-label]')].map(e => 'aria-label=' + e.getAttribute('aria-label')),
                 ...[...document.querySelectorAll('[aria-keyshortcuts], [accesskey]')].map(e => 'shortcut attr on ' + e.outerHTML.slice(0, 80))]
                """);
            failures.AddRange(texts.Where(t => hint.IsMatch(t) || t.StartsWith("shortcut attr")).Select(t => $"{path}: {t[..Math.Min(t.Length, 200)]}"));
        }
        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    // ── T039 SC-004: the whole walk with JavaScript off ──────────────────────────────────────

    [Fact]
    public async Task Walk_home_gallery_photo_next_back_to_tile_next_gallery_with_javascript_off()
    {
        var (ctx, page) = await Open("/g", js: false);
        await using var _ = ctx;
        var gallery = await site.GalleryPathAsync(S);

        await page.Locator($"a.tile[href='{gallery}']").ClickAsync();
        Assert.Equal(gallery, await PathAfter(page, p => p == gallery));
        await page.Locator($"a.tile[href='{await site.PhotoPathAsync(0)}']").ClickAsync();
        Assert.Equal(await site.PhotoPathAsync(0), await PathAfter(page, p => p.EndsWith(site.PhotoIds[0])));
        await page.Locator("article.solo a[rel=next]").ClickAsync();
        Assert.Equal(await site.PhotoPathAsync(1), await PathAfter(page, p => p.EndsWith(site.PhotoIds[1])));
        await page.Locator(".breadcrumb a[href*=\"#p-\"]").ClickAsync();
        var id = site.PhotoIds[1];
        var path = await PathAfter(page, p => p.EndsWith($"#p-{id}"));
        var (inside, measured) = await TileInView(page, id);
        Assert.True(path == $"{gallery}#p-{id}" && inside, $"back to the gallery: path={path} {measured}");
        await page.Locator("nav.gallery-neighbours a[rel=next]").ClickAsync();
        var coast = await site.GalleryPathAsync(NavBrowserFixture.CoastId);
        Assert.Equal(coast, await PathAfter(page, p => p == coast));
        // Owner review 4: the gallery's name is the breadcrumb's current item (the page has no h1).
        Assert.Equal("Coast", await page.Locator(".breadcrumb li[aria-current=page]").InnerTextAsync());
    }

    // The same walk from every listed gallery: home → gallery → photo 1 → Next (when there is one)
    // → gallery link → the next listed gallery (or, for the last, the previous one).
    [Fact]
    public async Task Every_listed_gallery_walks_with_javascript_off()
    {
        var listed = new[] { (S, (string?)NavBrowserFixture.CoastId), (NavBrowserFixture.CoastId, NavBrowserFixture.SoloId), (NavBrowserFixture.SoloId, null) };
        var (ctx, page) = await Open("/g", js: false);
        await using var _ = ctx;
        foreach (var (gid, next) in listed)
        {
            var gallery = await site.GalleryPathAsync(gid);
            var items = await site.ItemsAsync(gid);
            await page.GotoAsync(site.BaseUrl + "/g");
            await page.Locator($"a.tile[href='{gallery}']").ClickAsync();
            Assert.Equal(gallery, await PathAfter(page, p => p == gallery));
            await page.Locator($"a.tile[href='{gallery}/{items[0]}']").ClickAsync();
            Assert.Equal($"{gallery}/{items[0]}", await PathAfter(page, p => p.EndsWith(items[0])));
            var at = items[0];
            if (items.Count > 1)
            {
                await page.Locator("article.solo a[rel=next]").ClickAsync();
                at = items[1];
                Assert.Equal($"{gallery}/{at}", await PathAfter(page, p => p.EndsWith(at)));
            }
            await page.Locator(".breadcrumb a[href*=\"#p-\"]").ClickAsync();
            var path = await PathAfter(page, p => p.EndsWith($"#p-{at}"));
            var (inside, measured) = await TileInView(page, at);
            Assert.True(path == $"{gallery}#p-{at}" && inside, $"{gid}: path={path} {measured}");
            var rel = next is null ? "prev" : "next";
            var target = await site.GalleryPathAsync(next ?? NavBrowserFixture.CoastId);
            await page.Locator($"nav.gallery-neighbours a[rel={rel}]").ClickAsync();
            Assert.Equal(target, await PathAfter(page, p => p == target));
        }
    }

    // ── T039 SC-007: zero layout shift, no horizontal scroll ─────────────────────────────────

    public static TheoryData<int, string> Widths()
    {
        var d = new TheoryData<int, string>();
        foreach (var w in new[] { 1440, 1024, 390 })
            foreach (var p in new[] { "home", "gallery", "photo" })
                d.Add(w, p);
        return d;
    }

    [Theory]
    [MemberData(nameof(Widths))]
    public async Task No_layout_shift_and_no_horizontal_scroll(int width, string which)
    {
        var path = which switch { "home" => "/g", "gallery" => await site.GalleryPathAsync(S), _ => await site.PhotoPathAsync(11) };
        var ctx = await site.NewContextAsync(width, HeightFor(width));
        await using var _ = ctx;
        var page = await ctx.NewPageAsync();
        await page.AddInitScriptAsync("""
            window.__cls = 0;
            new PerformanceObserver(l => { for (const e of l.getEntries()) if (!e.hadRecentInput) window.__cls += e.value; })
              .observe({ type: 'layout-shift', buffered: true });
            """);
        await page.GotoAsync(site.BaseUrl + path, new() { WaitUntil = WaitUntilState.NetworkIdle });
        await page.EvaluateAsync("window.scrollTo(0, document.body.scrollHeight)");
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await page.WaitForTimeoutAsync(300);

        var cls = await page.EvaluateAsync<double>("window.__cls");
        var widths = await page.EvaluateAsync<double[]>("[document.documentElement.scrollWidth, innerWidth]");
        Assert.True(cls == 0, $"{which} at {width}px: CLS {cls}");
        Assert.True(widths[0] <= widths[1], $"{which} at {width}px: scrollWidth {widths[0]} > innerWidth {widths[1]}");
    }

    // ── T039 SC-008: no two header links to one destination ──────────────────────────────────

    [Fact]
    public async Task Header_links_have_no_duplicate_destination()
    {
        var failures = new List<string>();
        foreach (var path in new[] { "/", "/g", await site.GalleryPathAsync(S), await site.PhotoPathAsync(4), "/g/zzzzzzzzzzzzzzzzzzzzzzzzzz/none" })
        {
            var (ctx, page) = await Open(path);
            await using var _ = ctx;
            var hrefs = await page.EvaluateAsync<string[]>("[...document.querySelectorAll('header.site-header a')].map(a => a.getAttribute('href'))");
            // Owner review 2: the header is the breadcrumb, so home (whose trail is just the current
            // page) has no header link at all; elsewhere no destination repeats.
            if ((path == "/" ? hrefs.Length != 0 : hrefs.Length == 0) || hrefs.Distinct().Count() != hrefs.Length)
                failures.Add($"{path}: header hrefs [{string.Join(", ", hrefs)}]");
        }
        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }
}
