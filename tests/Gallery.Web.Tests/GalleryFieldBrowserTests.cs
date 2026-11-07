using System.Text.Json;
using Gallery.Web.Components.Molecules;
using Gallery.Web.Tests.Fixtures;
using Microsoft.Playwright;
using MongoDB.Driver;

namespace Gallery.Web.Tests;

/// <summary>
/// 014 T015/T016 (US1, US2; SC-003, SC-005; contracts K1 "Behaviour", K3): the photo panel's
/// Galleries field (the TokenCombobox molecule driven by library.js) in a real browser. Outcomes
/// are asserted on the stored documents after a real Save, on the announced status text, and on
/// measured geometry and computed styles. The parity fact pins library.js's filter to
/// <see cref="TokenCombobox.Match"/>: the expected rows and status are computed in C# here.
/// The mouse is parked at (0,0) before every navigation, so a hover never triggers a prerender.
/// </summary>
[Collection("library-browser")]
[Trait("Category", "Browser")]
public sealed class GalleryFieldBrowserTests(LibraryBrowserFixture fx)
{
    private const float WaitMs = 15_000;
    private const string Field = "#lib-gf";
    private const string InputSel = "#lib-gf-input";

    // ── shared probes ──────────────────────────────────────────────────────────────────────

    /// <summary>Copied from LibraryBrowserTests (private there): visible filled buttons outside/inside an open modal.</summary>
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

    /// <summary>The visible rows of the list, in DOM order, as their text.</summary>
    private static Task<string[]> VisibleRowsAsync(IPage page) =>
        page.Locator($"{Field} .cb-list").EvaluateAsync<string[]>(
            "ul => ul.hidden ? [] : [...ul.querySelectorAll(':scope > .cb-opt')].filter(r => !r.hidden).map(r => r.textContent.trim())");

    /// <summary>The highlighted row's text (aria-selected and aria-activedescendant must agree), or null.</summary>
    private static async Task<string?> HighlightedAsync(IPage page)
    {
        var json = await page.Locator(Field).EvaluateAsync<JsonElement>("""
            root => {
              const sel = [...root.querySelectorAll('.cb-list > .cb-opt')].filter(r => r.getAttribute('aria-selected') === 'true');
              const active = root.querySelector('.cb-input').getAttribute('aria-activedescendant');
              return { sel: sel.map(r => ({ id: r.id, text: r.textContent.trim(), hidden: r.hidden })), active };
            }
            """);
        var sel = json.GetProperty("sel").EnumerateArray().ToList();
        if (sel.Count == 0)
        {
            Assert.True(json.GetProperty("active").ValueKind == JsonValueKind.Null, "aria-activedescendant is set with no highlighted row");
            return null;
        }
        Assert.Single(sel);
        Assert.False(sel[0].GetProperty("hidden").GetBoolean(), "the highlighted row is hidden");
        Assert.Equal(sel[0].GetProperty("id").GetString(), json.GetProperty("active").GetString());
        return sel[0].GetProperty("text").GetString();
    }

    private static async Task<string> StatusAsync(IPage page) =>
        (await page.Locator($"{Field} .cb-status").TextContentAsync() ?? "").Trim();

    private static ILocator Input(IPage page) => page.Locator(InputSel);

    private static ILocator Row(IPage page, string gid) => page.Locator($"#lib-gf-o-{gid}");

    private static ILocator NewRow(IPage page) => page.Locator("#lib-gf-new");

    /// <summary>A new context and page with the mouse parked at (0,0) BEFORE the navigation.</summary>
    private static async Task<IPage> OpenAsync(LibrarySite site, string path)
    {
        var ctx = await site.NewContextAsync();
        var page = await ctx.NewPageAsync();
        await page.Mouse.MoveAsync(0, 0);
        var resp = await page.GotoAsync(site.BaseUrl + path, new() { WaitUntil = WaitUntilState.NetworkIdle });
        Assert.True(resp is not null && resp.Status == 200 && new Uri(page.Url).AbsolutePath == path.Split('?')[0],
            $"GET {path} answered {resp?.Status} at {page.Url}");
        await page.Locator(InputSel).WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = WaitMs });
        return page;
    }

    /// <summary>Runs <paramref name="action"/> and waits for a new document to commit and load.</summary>
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

    /// <summary>Clicks Save with the mouse parked first, and waits for the 303's page.</summary>
    private static async Task SaveAsync(IPage page)
    {
        var save = page.Locator(".side-panel .sp-save button");
        await NavigatesAsync(page, async () =>
        {
            await save.ScrollIntoViewIfNeededAsync();
            var box = await save.BoundingBoxAsync();
            await page.Mouse.MoveAsync(0, 0);
            await page.Mouse.ClickAsync(box!.X + box.Width / 2, box.Y + box.Height / 2);
        });
        await page.Mouse.MoveAsync(0, 0);
    }

    /// <summary>
    /// Seeds two photos: P (ids[0]) in the public "Coast" (so Coast is P's token), and Q (ids[1])
    /// in nothing; plus the private, empty Alpine, Birds, Coastline, Desert and Harbour.
    /// </summary>
    private sealed record Seed(List<string> Ids, Dictionary<string, string> G);

    private static async Task<Seed> SeedAsync(LibrarySite site, params string[] extra)
    {
        var ids = await site.ImagesAsync(2);
        var g = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Coast"] = await site.GalleryAsync("Coast", [ids[0]], publish: true),
        };
        foreach (var t in new[] { "Alpine", "Birds", "Coastline", "Desert", "Harbour" }.Concat(extra))
            g[t] = await site.GalleryAsync(t, []);
        return new Seed(ids, g);
    }

    private static async Task<List<string>> HoldersOfAsync(LibrarySite site, string iid) =>
        (await site.Galleries.Find(x => x.Items.Contains(iid)).ToListAsync()).Select(x => x.Id).Order(StringComparer.Ordinal).ToList();

    // ── T015 US1: keyboard only (SC-005) ───────────────────────────────────────────────────

    [Fact]
    public async Task Keyboard_only_type_coa_Enter_then_Tab_to_Save_adds_the_photo_and_the_reload_shows_a_link()
    {
        await using var site = await fx.NewSiteAsync();
        var s = await SeedAsync(site);
        var p = s.Ids[0];
        var page = await OpenAsync(site, $"/admin/library?open={p}");

        // Tab into the field: from the caption, past the saved token's link and ×, to the input.
        await page.Locator(".side-panel textarea[name='caption']").FocusAsync();
        var reached = false;
        for (var i = 0; i < 4 && !reached; i++)
        {
            await page.Keyboard.PressAsync("Tab");
            reached = await Input(page).EvaluateAsync<bool>("el => el === document.activeElement");
        }
        Assert.True(reached, "Tab did not reach the combobox input");

        await page.Keyboard.TypeAsync("coa");
        Assert.Equal("Coastline · Private", await HighlightedAsync(page));
        await page.Keyboard.PressAsync("Enter");
        Assert.Equal(1, await page.Locator($"{Field} .cb-token.is-pending[data-id='{s.G["Coastline"]}']").CountAsync());
        Assert.Equal("", await Input(page).InputValueAsync());

        var onSave = false;
        for (var i = 0; i < 6 && !onSave; i++)
        {
            await page.Keyboard.PressAsync("Tab");
            onSave = await page.EvaluateAsync<bool>("() => document.activeElement?.matches('.side-panel .sp-save button') === true");
        }
        Assert.True(onSave, "Tab did not reach Save");
        await page.Mouse.MoveAsync(0, 0);
        await NavigatesAsync(page, () => page.Keyboard.PressAsync("Enter"));

        Assert.Contains(p, (await site.GalleryDocAsync(s.G["Coastline"])).Items);
        Assert.Equal(new[] { s.G["Coast"], s.G["Coastline"] }.Order(StringComparer.Ordinal).ToList(), await HoldersOfAsync(site, p));
        var hrefs = await page.Locator($"{Field} .cb-tokens > .cb-token > a").EvaluateAllAsync<string[]>("els => els.map(a => a.getAttribute('href'))");
        Assert.Equal([$"/admin/galleries/{s.G["Coast"]}", $"/admin/galleries/{s.G["Coastline"]}"], hrefs);
        Assert.Equal(0, await page.Locator($"{Field} .cb-token.is-pending").CountAsync());
    }

    // ── T015 US1: mouse, pending style, announcements ──────────────────────────────────────

    [Fact]
    public async Task Mouse_pick_makes_a_dashed_pending_token_and_announces_options_and_the_add()
    {
        await using var site = await fx.NewSiteAsync();
        var s = await SeedAsync(site);
        var page = await OpenAsync(site, $"/admin/library?open={s.Ids[0]}");

        await Input(page).FillAsync("coa");
        Assert.Equal(["Coastline · Private", "+ coa"], await VisibleRowsAsync(page));
        Assert.Equal("2 options", await StatusAsync(page));

        await Row(page, s.G["Coastline"]).ClickAsync();
        await page.Mouse.MoveAsync(0, 0);
        var pending = page.Locator($"{Field} .cb-token.is-pending");
        Assert.Equal(1, await pending.CountAsync());
        Assert.Equal(s.G["Coastline"], await pending.GetAttributeAsync("data-id"));
        Assert.Equal("Added Coastline", await StatusAsync(page));
        Assert.True(await Input(page).EvaluateAsync<bool>("el => el === document.activeElement"), "focus left the input after a mouse pick");
        Assert.Equal("true", await page.Locator($"{Field} .cb-list").EvaluateAsync<string>("ul => String(ul.hidden)"));

        // The cloned template carries the component's CSS scope: dashed; the saved token is not.
        Assert.Equal("dashed", await pending.EvaluateAsync<string>("el => getComputedStyle(el).borderTopStyle"));
        var saved = page.Locator($"{Field} .cb-token[data-kind='saved']");
        Assert.Equal(s.G["Coast"], await saved.GetAttributeAsync("data-id"));
        Assert.NotEqual("dashed", await saved.EvaluateAsync<string>("el => getComputedStyle(el).borderTopStyle"));
        Assert.Equal($"Remove from Coastline", await pending.Locator(".cb-x").GetAttributeAsync("aria-label"));
        Assert.Equal(s.G["Coastline"], await pending.Locator("input[type='hidden'][name='add']").InputValueAsync());
    }

    [Fact]
    public async Task Public_option_is_one_line_and_picking_it_shows_the_warning_until_the_token_is_removed()
    {
        await using var site = await fx.NewSiteAsync();
        var s = await SeedAsync(site);
        var page = await OpenAsync(site, $"/admin/library?open={s.Ids[1]}");
        var warn = page.Locator($"{Field} .cb-warn");
        Assert.False(await warn.IsVisibleAsync());

        await Input(page).FillAsync("coa");
        var coast = Row(page, s.G["Coast"]);
        Assert.Equal("Coast · Public", (await coast.TextContentAsync())!.Trim());
        var lines = await coast.EvaluateAsync<double>(
            "el => { const s = getComputedStyle(el); return (el.clientHeight - parseFloat(s.paddingTop) - parseFloat(s.paddingBottom)) / parseFloat(s.lineHeight); }");
        Assert.True(lines < 1.5, $"the public option wraps ({lines:0.0} lines)");

        await coast.ClickAsync();
        await page.Mouse.MoveAsync(0, 0);
        Assert.True(await warn.IsVisibleAsync(), "no public warning after picking Coast");
        Assert.Equal("Coast is public: visitors will see this photo after you save.", (await warn.TextContentAsync())!.Trim());

        await page.Locator($"{Field} .cb-token.is-pending .cb-x").ClickAsync();
        await page.Mouse.MoveAsync(0, 0);
        Assert.Equal(0, await page.Locator($"{Field} .cb-token").CountAsync());
        Assert.False(await warn.IsVisibleAsync(), "the warning stayed after the pending token was removed");
        Assert.Equal("Removed Coast", await StatusAsync(page));
        Assert.True(await page.Locator($"{Field} .cb-empty").IsVisibleAsync(), "cb-empty did not come back with no tokens");
        // A pending token adds nothing when removed.
        Assert.Equal(0, await page.Locator($"{Field} input[type='hidden'][name='leave']").CountAsync());
    }

    // ── T015: geometry ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Remove_hit_box_is_at_least_44_square_and_a_60_character_label_is_truncated_with_its_title()
    {
        await using var site = await fx.NewSiteAsync();
        var ids = await site.ImagesAsync(1);
        var longTitle = string.Concat(Enumerable.Repeat("Longtitle ", 6));
        Assert.Equal(60, longTitle.Length);
        longTitle = longTitle.TrimEnd() + "x";
        Assert.Equal(60, longTitle.Length);
        await site.GalleryAsync(longTitle, [ids[0]]);
        var page = await OpenAsync(site, $"/admin/library?open={ids[0]}");

        var token = page.Locator($"{Field} .cb-tokens > .cb-token").First;
        var box = await token.Locator(".cb-x").EvaluateAsync<JsonElement>("b => { const r = b.getBoundingClientRect(); return { w: r.width, h: r.height }; }");
        Assert.True(box.GetProperty("w").GetDouble() >= 44 && box.GetProperty("h").GetDouble() >= 44,
            $"× hit box is {box.GetProperty("w").GetDouble()}×{box.GetProperty("h").GetDouble()}");

        var link = token.Locator("a");
        var trunc = await link.EvaluateAsync<JsonElement>("a => ({ sw: a.scrollWidth, cw: a.clientWidth, title: a.getAttribute('title'), text: a.textContent })");
        Assert.True(trunc.GetProperty("sw").GetInt32() > trunc.GetProperty("cw").GetInt32(), "the 60-character label is not truncated");
        Assert.Equal(longTitle, trunc.GetProperty("title").GetString());
        Assert.Equal(longTitle, trunc.GetProperty("text").GetString());
    }

    // ── T015: Esc ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Esc_closes_the_list_keeps_the_text_and_neither_closes_nor_submits_the_panel()
    {
        await using var site = await fx.NewSiteAsync();
        var s = await SeedAsync(site);
        var page = await OpenAsync(site, $"/admin/library?open={s.Ids[0]}");
        var url = page.Url;

        var escapes = 0;
        var posts = await PostsDuring(page, async () =>
        {
            await page.EvaluateAsync("() => { window.__esc = 0; document.addEventListener('keydown', e => { if (e.key === 'Escape') window.__esc++; }); }");
            await Input(page).FocusAsync();
            await page.Keyboard.TypeAsync("coa");
            Assert.Equal("true", await Input(page).GetAttributeAsync("aria-expanded"));
            await page.Keyboard.PressAsync("Escape");
            escapes = await page.EvaluateAsync<int>("() => window.__esc");
        });

        Assert.Empty(posts);
        Assert.Equal(0, escapes); // stopPropagation: the panel's own Esc handling never sees it
        Assert.Equal(url, page.Url);
        Assert.True(await page.Locator($"{Field} .cb-list").EvaluateAsync<bool>("ul => ul.hidden"), "Esc did not close the list");
        Assert.Equal("false", await Input(page).GetAttributeAsync("aria-expanded"));
        Assert.Equal("coa", await Input(page).InputValueAsync());
        Assert.True(await page.Locator(".side-panel").IsVisibleAsync(), "Esc closed the panel");
        Assert.True(await Input(page).EvaluateAsync<bool>("el => el === document.activeElement"));

        // With the list closed, Esc does nothing in the field (it propagates as usual).
        await page.Keyboard.PressAsync("Escape");
        Assert.Equal(1, await page.EvaluateAsync<int>("() => window.__esc"));
        Assert.True(await page.Locator(".side-panel").IsVisibleAsync());
        Assert.Equal(new[] { s.G["Coast"] }, await HoldersOfAsync(site, s.Ids[0]));
    }

    // ── T015: parity with TokenCombobox.Match (decision 14) ────────────────────────────────

    [Fact]
    public async Task Rows_highlight_and_status_equal_TokenCombobox_Match_for_each_query()
    {
        await using var site = await fx.NewSiteAsync();
        var s = await SeedAsync(site, "Alpine beach", "Beach", "Beaches");
        var page = await OpenAsync(site, $"/admin/library?open={s.Ids[0]}");

        // The page's own options and tokens, read back from the store (as K3 builds them).
        var docs = await site.Galleries.Find(FilterDefinition<Gallery.Web.Data.GalleryDoc>.Empty).ToListAsync();
        var options = docs.Select(d => new ComboOption(d.Id, d.Title, d.Visibility == "public" ? "Public" : "Private",
            d.Visibility == "public" ? $"{d.Title} is public" : null)).ToList();
        IReadOnlyList<ComboToken> tokens = [new(s.G["Coast"], "Coast", $"/admin/galleries/{s.G["Coast"]}")];
        Assert.Equal(9, options.Count);

        foreach (var q in new[] { "coa", "COAST", " desert ", "x", "a", "beach" })
        {
            var m = TokenCombobox.Match(q, options, tokens, 10, 200, true);
            var expected = m.Shown.Select(o => $"{o.Label} · {o.Detail}").ToList();
            if (m.CreateLabel is not null) expected.Add($"+ {m.CreateLabel}");

            await Input(page).FillAsync(q);
            var rows = await VisibleRowsAsync(page);
            Assert.True(expected.SequenceEqual(rows), $"query \"{q}\": rows [{string.Join(" | ", rows)}], Match [{string.Join(" | ", expected)}]");
            Assert.Equal(expected.Count == 0 ? null : expected[0], await HighlightedAsync(page));
            Assert.Equal(m.Status, await StatusAsync(page));
            Assert.Equal(expected.Count > 0 ? "true" : "false", await Input(page).GetAttributeAsync("aria-expanded"));
        }

        // The rank step is what orders "beach": exact, prefix, contains.
        Assert.Equal(["Beach", "Beaches", "Alpine beach"],
            TokenCombobox.Match("beach", options, tokens, 10, 200, true).Shown.Select(o => o.Label).ToArray());
    }

    // ── T015: SC-003 ───────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task With_200_galleries_a_keystroke_filters_within_100_ms_median_of_5()
    {
        await using var site = await fx.NewSiteAsync();
        var ids = await site.ImagesAsync(1);
        for (var i = 0; i < 200; i++)
            await site.GalleryAsync($"Gallery {i + 1:000}", []);
        var page = await OpenAsync(site, $"/admin/library?open={ids[0]}");
        Assert.Equal(200, await page.Locator($"{Field} .cb-list > .cb-opt:not(.cb-new)").CountAsync());

        await page.EvaluateAsync("""
            () => {
              window.__cbTimes = [];
              document.addEventListener('keydown', e => {
                if (!e.target.classList || !e.target.classList.contains('cb-input')) return;
                const t0 = performance.now();
                requestAnimationFrame(() => window.__cbTimes.push(performance.now() - t0));
              }, true);
            }
            """);
        await Input(page).FocusAsync();
        foreach (var ch in "galle")
        {
            await page.Keyboard.TypeAsync(ch.ToString());
            await page.WaitForTimeoutAsync(150);
        }
        var times = (await page.EvaluateAsync<double[]>("() => window.__cbTimes")).Order().ToArray();
        Assert.Equal(5, times.Length);
        Assert.True(times[2] < 100, $"median keystroke-to-frame {times[2]:0.0} ms (all: {string.Join(", ", times.Select(t => t.ToString("0.0")))})");
        Assert.Equal("11 options", await StatusAsync(page)); // 10 shown (maxShown) + "+ galle"
    }

    // ── T015: one filled button ────────────────────────────────────────────────────────────

    [Fact]
    public async Task The_panel_s_only_filled_button_is_Save_at_rest_and_with_the_list_open()
    {
        await using var site = await fx.NewSiteAsync();
        var s = await SeedAsync(site);
        var page = await OpenAsync(site, $"/admin/library?open={s.Ids[0]}");
        Assert.Equal(["Save"], await FilledOutsideAsync(page));
        await Input(page).FillAsync("a");
        Assert.Equal(["Save"], await FilledOutsideAsync(page));
        await Row(page, s.G["Alpine"]).ClickAsync();
        await page.Mouse.MoveAsync(0, 0);
        Assert.Equal(["Save"], await FilledOutsideAsync(page));
    }

    // ── T016 US2: create from typed text ───────────────────────────────────────────────────

    [Fact]
    public async Task Typing_Night_walks_offers_the_create_row_last_and_Save_makes_one_private_gallery_holding_the_photo()
    {
        await using var site = await fx.NewSiteAsync();
        var s = await SeedAsync(site, "Walks at noon");
        var p = s.Ids[1];
        var before = await site.Galleries.CountDocumentsAsync(FilterDefinition<Gallery.Web.Data.GalleryDoc>.Empty);
        var page = await OpenAsync(site, $"/admin/library?open={p}");

        await Input(page).FillAsync("Night walks");
        var rows = await VisibleRowsAsync(page);
        Assert.Equal("+ Night walks", rows[^1]);

        await NewRow(page).ClickAsync();
        await page.Mouse.MoveAsync(0, 0);
        var pending = page.Locator($"{Field} .cb-token.is-pending[data-kind='create']");
        Assert.Equal("Night walks", await pending.GetAttributeAsync("data-id"));
        Assert.Equal("Night walks", await pending.Locator("input[type='hidden'][name='create']").InputValueAsync());
        Assert.Equal("Added new Night walks", await StatusAsync(page));

        await SaveAsync(page);

        Assert.Equal(before + 1, await site.Galleries.CountDocumentsAsync(FilterDefinition<Gallery.Web.Data.GalleryDoc>.Empty));
        var made = await site.Galleries.Find(g => g.Title == "Night walks").ToListAsync();
        var g = Assert.Single(made);
        Assert.Equal("private", g.Visibility);
        Assert.Equal([p], g.Items);
        Assert.Equal([g.Id], await HoldersOfAsync(site, p));
    }

    [Fact]
    public async Task An_exact_title_with_case_and_spaces_differing_offers_no_create_row()
    {
        await using var site = await fx.NewSiteAsync();
        var s = await SeedAsync(site);
        var page = await OpenAsync(site, $"/admin/library?open={s.Ids[1]}");

        await Input(page).FillAsync(" coast ");
        Assert.False(await NewRow(page).IsVisibleAsync(), "a + row is offered for an existing title");
        Assert.DoesNotContain(await VisibleRowsAsync(page), r => r.StartsWith('+'));
        Assert.Equal(["Coast · Public", "Coastline · Private"], await VisibleRowsAsync(page));
    }

    [Fact]
    public async Task A_201_character_text_shows_the_too_long_message_and_no_create_row()
    {
        await using var site = await fx.NewSiteAsync();
        var s = await SeedAsync(site);
        var page = await OpenAsync(site, $"/admin/library?open={s.Ids[1]}");

        await Input(page).FillAsync(new string('a', 201));
        var msg = page.Locator($"{Field} .cb-msg");
        Assert.True(await msg.IsVisibleAsync(), "the too-long message is not shown");
        Assert.Equal("Too long: at most 200 characters.", (await msg.TextContentAsync())!.Trim());
        Assert.False(await NewRow(page).IsVisibleAsync());
        Assert.Equal("No matches", await StatusAsync(page));

        await Input(page).FillAsync("a");
        Assert.Equal("", (await msg.TextContentAsync())!.Trim());
        Assert.False(await msg.IsVisibleAsync());
    }

    [Fact]
    public async Task After_picking_plus_X_typing_X_again_offers_no_second_create()
    {
        await using var site = await fx.NewSiteAsync();
        var s = await SeedAsync(site);
        var page = await OpenAsync(site, $"/admin/library?open={s.Ids[1]}");

        await Input(page).FillAsync("X");
        Assert.Equal(["+ X"], await VisibleRowsAsync(page));
        await page.Keyboard.PressAsync("Enter");
        Assert.Equal(1, await page.Locator($"{Field} .cb-token[data-kind='create']").CountAsync());

        await Input(page).FillAsync("X");
        Assert.False(await NewRow(page).IsVisibleAsync(), "a second + X is offered");
        Assert.Empty(await VisibleRowsAsync(page));
        Assert.Equal("Already added", await StatusAsync(page));
        await Input(page).FillAsync("x");
        Assert.False(await NewRow(page).IsVisibleAsync(), "a second + x is offered (case differing)");
    }

    // ── T018 US3: remove a gallery token ───────────────────────────────────────────────────

    private static ILocator LeaveInputs(IPage page) => page.Locator($"{Field} input[type='hidden'][name='leave']");

    private static Task<string[]> LeaveValuesAsync(IPage page) =>
        LeaveInputs(page).EvaluateAllAsync<string[]>("els => els.map(i => i.value)");

    private static Task<string[]> TokenIdsAsync(IPage page) =>
        page.Locator($"{Field} .cb-tokens > .cb-token").EvaluateAllAsync<string[]>("els => els.map(li => li.dataset.id)");

    private static async Task AssertInLibraryAsync(LibrarySite site, string iid)
    {
        var doc = await site.Images.Find(i => i.Id == iid).SingleAsync();
        Assert.Null(doc.DeletingAt);
    }

    private static List<string> MembershipLines(LibrarySite site) =>
        site.LogLines.Where(l => l.StartsWith("op.timing library.image.memberships ", StringComparison.Ordinal)).ToList();

    [Fact]
    public async Task Clicking_x_on_a_saved_token_then_Save_takes_the_photo_out_and_keeps_it_in_the_library()
    {
        await using var site = await fx.NewSiteAsync();
        var s = await SeedAsync(site);
        var p = s.Ids[0];
        var page = await OpenAsync(site, $"/admin/library?open={p}");
        var coast = s.G["Coast"];
        Assert.Equal([coast], await TokenIdsAsync(page));

        await page.Locator($"{Field} .cb-token[data-id='{coast}'] .cb-x").ClickAsync();
        await page.Mouse.MoveAsync(0, 0);
        Assert.Empty(await TokenIdsAsync(page));
        Assert.Equal([coast], await LeaveValuesAsync(page));
        Assert.Equal("Removed Coast", await StatusAsync(page));

        await SaveAsync(page);

        Assert.DoesNotContain(p, (await site.GalleryDocAsync(coast)).Items);
        Assert.Empty(await HoldersOfAsync(site, p));
        Assert.Empty((await site.Images.Find(i => i.Id == p).SingleAsync()).GalleryIds);
        await AssertInLibraryAsync(site, p);
        Assert.Empty(await TokenIdsAsync(page));
        Assert.Equal(0, await LeaveInputs(page).CountAsync());
    }

    [Fact]
    public async Task Backspace_in_the_empty_input_removes_the_last_token()
    {
        await using var site = await fx.NewSiteAsync();
        var s = await SeedAsync(site);
        var p = s.Ids[0];
        // A second holder of P, after Coast in title order (tokens render in title order).
        var pierWalks = await site.GalleryAsync("Pier walks", [p]);
        var page = await OpenAsync(site, $"/admin/library?open={p}");
        Assert.Equal([s.G["Coast"], pierWalks], await TokenIdsAsync(page));

        await Input(page).FocusAsync();
        Assert.Equal("", await Input(page).InputValueAsync());
        await page.Keyboard.PressAsync("Backspace");

        Assert.Equal([s.G["Coast"]], await TokenIdsAsync(page));
        Assert.Equal([pierWalks], await LeaveValuesAsync(page));
        Assert.Equal("Removed Pier walks", await StatusAsync(page));
        Assert.True(await Input(page).EvaluateAsync<bool>("el => el === document.activeElement"), "focus left the input after Backspace");

        await SaveAsync(page);
        Assert.Equal([s.G["Coast"]], await HoldersOfAsync(site, p));
        Assert.DoesNotContain(p, (await site.GalleryDocAsync(pierWalks)).Items);
        await AssertInLibraryAsync(site, p);
    }

    [Fact]
    public async Task Removing_a_saved_token_then_re_picking_it_leaves_no_leave_input_and_Save_changes_nothing()
    {
        await using var site = await fx.NewSiteAsync();
        var s = await SeedAsync(site);
        var p = s.Ids[0];
        var coast = s.G["Coast"];
        var itemsBefore = (await site.GalleryDocAsync(coast)).Items.ToList();
        var page = await OpenAsync(site, $"/admin/library?open={p}");

        await page.Locator($"{Field} .cb-token[data-id='{coast}'] .cb-x").ClickAsync();
        await page.Mouse.MoveAsync(0, 0);
        Assert.Equal([coast], await LeaveValuesAsync(page));

        await Input(page).FillAsync("coast");
        await Row(page, coast).ClickAsync();
        await page.Mouse.MoveAsync(0, 0);

        Assert.Equal(0, await LeaveInputs(page).CountAsync());
        Assert.Equal(0, await page.Locator($"{Field} input[type='hidden'][name='add']").CountAsync());
        Assert.Equal([coast], await TokenIdsAsync(page));
        var token = page.Locator($"{Field} .cb-token[data-id='{coast}']");
        Assert.Equal("saved", await token.GetAttributeAsync("data-kind"));
        Assert.Equal(0, await page.Locator($"{Field} .cb-token.is-pending").CountAsync());

        await SaveAsync(page);

        Assert.Equal(itemsBefore, (await site.GalleryDocAsync(coast)).Items);
        Assert.Equal([coast], await HoldersOfAsync(site, p));
        Assert.Equal([coast], (await site.Images.Find(i => i.Id == p).SingleAsync()).GalleryIds);
        Assert.Empty(MembershipLines(site));
    }

    [Fact]
    public async Task Keyboard_only_Tab_to_x_and_Enter_removes_the_token_and_Save_takes_the_photo_out()
    {
        await using var site = await fx.NewSiteAsync();
        var s = await SeedAsync(site);
        var p = s.Ids[0];
        var coast = s.G["Coast"];
        var page = await OpenAsync(site, $"/admin/library?open={p}");

        await page.Locator(".side-panel textarea[name='caption']").FocusAsync();
        var onX = false;
        for (var i = 0; i < 4 && !onX; i++)
        {
            await page.Keyboard.PressAsync("Tab");
            onX = await page.EvaluateAsync<bool>(
                $"() => document.activeElement?.matches(\"{Field} .cb-token[data-id='{coast}'] .cb-x\") === true");
        }
        Assert.True(onX, "Tab did not reach the saved token's ×");
        await page.Keyboard.PressAsync("Enter");

        Assert.Empty(await TokenIdsAsync(page));
        Assert.Equal([coast], await LeaveValuesAsync(page));
        Assert.Equal("Removed Coast", await StatusAsync(page));
        Assert.True(await Input(page).EvaluateAsync<bool>("el => el === document.activeElement"), "focus did not move to the input after the last ×");

        var onSave = false;
        for (var i = 0; i < 6 && !onSave; i++)
        {
            await page.Keyboard.PressAsync("Tab");
            onSave = await page.EvaluateAsync<bool>("() => document.activeElement?.matches('.side-panel .sp-save button') === true");
        }
        Assert.True(onSave, "Tab did not reach Save");
        await page.Mouse.MoveAsync(0, 0);
        await NavigatesAsync(page, () => page.Keyboard.PressAsync("Enter"));

        Assert.DoesNotContain(p, (await site.GalleryDocAsync(coast)).Items);
        Assert.Empty(await HoldersOfAsync(site, p));
        await AssertInLibraryAsync(site, p);
    }
}
