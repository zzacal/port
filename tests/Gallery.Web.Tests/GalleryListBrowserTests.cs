using System.Text.Json;
using Gallery.Web.Components.Molecules;
using Gallery.Web.Data;
using Gallery.Web.Library;
using Gallery.Web.Tests.Fixtures;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using MongoDB.Driver;

namespace Gallery.Web.Tests;

/// <summary>
/// Feature 016 T016 (US1, US2 [SAFETY], US3; SC-001, SC-002, SC-003, SC-004, SC-006): the gallery
/// list's cards in a real browser: the title on the cover (or in the empty card's .gal-band) and one
/// GalleryActions icon row under the cell. Geometry and colours are MEASURED on the rendered page;
/// publish, unpublish and reorder are read back from storage, with scripting on and off.
/// The mouse is parked at (0,0) before every navigation so a hover never colours a control.
/// </summary>
[Collection("library-browser")]
[Trait("Category", "Browser")]
public sealed class GalleryListBrowserTests(LibraryBrowserFixture fx)
{
    private const float WaitMs = 15_000;
    private const string List = "/admin/galleries";

    // 1x1 PNGs of one colour (SC-004), as in TileIconBrowserTests.
    private const string White = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAIAAACQd1PeAAAADElEQVR4nGP4//8/AAX+Av4N70a4AAAAAElFTkSuQmCC";
    private const string Black = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAIAAACQd1PeAAAADElEQVR4nGNgYGAAAAAEAAH2FzhVAAAAAElFTkSuQmCC";
    private const string Grey = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAIAAACQd1PeAAAADElEQVR4nGNoaGgAAAMEAYFL09IQAAAAAElFTkSuQmCC";

    public static TheoryData<int, int> Widths() => new() { { 390, 844 }, { 900, 900 }, { 1440, 900 } };

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

    /// <summary>A page with JavaScript off, carrying the owner session copied from a JS-on context.</summary>
    private async Task<IPage> NoScriptAsync(LibrarySite site)
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
        var page = await ctx.NewPageAsync();
        try
        {
            return await GotoAsync(page, site, List);
        }
        catch
        {
            await ctx.DisposeAsync();
            throw;
        }
    }

    /// <summary>
    /// Runs <paramref name="action"/> and waits until the main frame has committed a NEW document
    /// (a form submit and its 303) and that document has loaded.
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
        await page.Mouse.MoveAsync(0, 0);
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

    /// <summary>The list card whose edit icon goes to the gallery's builder.</summary>
    private static ILocator Card(IPage page, string gid) =>
        page.Locator($"section.gal reorder-grid .cs > *:has(.gallery-actions a.ga-edit[href='/admin/galleries/{gid}'])");

    private static ILocator Vis(IPage page, string gid) => Card(page, gid).Locator(".gallery-actions .ga-vis");

    private static Task<string?> VisIconAsync(IPage page, string gid) =>
        Vis(page, gid).Locator("svg.icon").GetAttributeAsync("data-icon");

    private async Task<string> RemoveAllAsync(LibrarySite site, string gid, IReadOnlyList<string> ids)
    {
        using var scope = site.Services.CreateScope();
        var galleries = scope.ServiceProvider.GetRequiredService<GalleryService>();
        foreach (var id in ids)
        {
            var r = await galleries.RemoveAsync(gid, id);
            Assert.True(r.IsOk, $"remove {id} from {gid}: {r.Reason}");
        }
        return gid;
    }

    private async Task<List<string>> TonedImagesAsync(LibrarySite site, params string[] tones)
    {
        var ids = await site.ImagesAsync(tones.Length, "Tone");
        for (var i = 0; i < ids.Count; i++)
            await site.Images.UpdateOneAsync(d => d.Id == ids[i], Builders<ImageDoc>.Update.Set(d => d.Placeholder.Preview, tones[i]));
        return ids;
    }

    /// <summary>
    /// Six cards: White, Black, Grey (public covers), Harbour (private, with a photo), Bare (public,
    /// no photos) and Empty (private, no photos).
    /// </summary>
    private async Task<(List<string> Gids, List<string> Ids)> SixCardsAsync(LibrarySite site)
    {
        var ids = await TonedImagesAsync(site, White, Black, Grey, Grey, Grey);
        var gids = new List<string>
        {
            await site.GalleryAsync("White", [ids[0]], publish: true),
            await site.GalleryAsync("Black", [ids[1]], publish: true),
            await site.GalleryAsync("Grey", [ids[2]], publish: true),
            await site.GalleryAsync("Harbour", [ids[3]]),
            await RemoveAllAsync(site, await site.GalleryAsync("Bare", [ids[4]], publish: true), [ids[4]]),
            await site.GalleryAsync("Empty", []),
        };
        var bare = await site.GalleryDocAsync(gids[4]);
        Assert.Equal(("public", 0), (bare.Visibility, bare.Items.Count));
        return (gids, ids);
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

    private static double[] Nums(JsonElement e) => e.EnumerateArray().Select(x => x.GetDouble()).ToArray();

    // ── US1: one icon row, the title on the cover (SC-001, SC-002, SC-004) ─────────────────

    [Theory]
    [MemberData(nameof(Widths))]
    public async Task US1_one_icon_row_per_card_edit_last_44px_targets_and_the_title_band_inside_the_cell(int width, int height)
    {
        await using var site = await fx.NewSiteAsync();
        var (gids, _) = await SixCardsAsync(site);
        var page = await OpenAsync(site, List, width, height);

        var cards = (await page.EvaluateAsync<JsonElement>("""
            async () => {
              const box = el => { const r = el.getBoundingClientRect(); return { l: r.left, t: r.top, r: r.right, b: r.bottom, w: r.width, h: r.height }; };
              const rgba = s => { const n = (s.match(/[\d.]+/g) || []).map(Number); return [n[0], n[1], n[2], n.length > 3 ? n[3] : 1]; };
              const out = [];
              for (const card of document.querySelectorAll('section.gal reorder-grid .cs > *')) {
                card.scrollIntoView({ block: 'center' });
                const row = card.querySelector('.gallery-actions');
                const cell = card.querySelector(':scope > .cell');
                const onPhoto = cell.querySelector('.on-photo');
                const band = onPhoto || cell.querySelector('.gal-band');
                const text = onPhoto ? onPhoto.querySelector('.title') : band;
                const kids = [...row.children];
                let photo = null, mainLoaded = null;
                const ph = cell.querySelector('.pic img.ph');
                if (ph) {
                  await ph.decode();
                  const c = document.createElement('canvas'); c.width = 1; c.height = 1;
                  const g = c.getContext('2d'); g.drawImage(ph, 0, 0, 1, 1);
                  const d = g.getImageData(0, 0, 1, 1).data;
                  photo = [d[0], d[1], d[2]];
                  const main = cell.querySelector('.pic .main img');
                  mainLoaded = !!main && main.complete && main.naturalWidth > 0;
                }
                out.push({
                  edit: card.querySelector('.gallery-actions a.ga-edit')?.getAttribute('href') ?? null,
                  kinds: kids.map(k => ['handle', 'ga-vis', 'ga-view', 'ga-edit'].find(c => k.classList.contains(c)) ?? (k.tagName + '.' + k.className)),
                  kids: kids.map(box),
                  row: box(row),
                  rowInCard: card.contains(row) && row.parentElement === card,
                  words: row.querySelectorAll('a.text-link, .btn').length,
                  cell: box(cell),
                  cover: !!onPhoto,
                  band: band ? box(band) : null,
                  bandIsSpan: band ? band.tagName === 'SPAN' && band.classList.contains('gal-band') : null,
                  text: text ? text.textContent.trim() : null,
                  tip: text ? text.getAttribute('title') : null,
                  oneLine: text ? getComputedStyle(text).whiteSpace === 'nowrap' && text.getClientRects().length === 1 : false,
                  under: card.querySelectorAll(':scope > .title').length,
                  fg: text ? rgba(getComputedStyle(text).color) : null,
                  bg: band ? rgba(getComputedStyle(band).backgroundColor) : null,
                  photo, mainLoaded,
                });
              }
              return out;
            }
            """)).EnumerateArray().ToList();

        // One card per gallery, in home order.
        Assert.Equal(gids.Select(g => $"/admin/galleries/{g}").ToList(),
            cards.Select(c => c.GetProperty("edit").GetString() ?? "(none)").ToList());

        var errors = new List<string>();
        var heights = new List<double>();
        var tones = new List<string>();
        var bandColours = new HashSet<string>();
        foreach (var c in cards)
        {
            var what = $"{c.GetProperty("text").GetString()} @{width}px";
            var kinds = c.GetProperty("kinds").EnumerateArray().Select(k => k.GetString()).ToList();
            if (!kinds.SequenceEqual(["handle", "ga-vis", "ga-view", "ga-edit"]))
                errors.Add($"{what}: row children are [{string.Join(", ", kinds)}]");
            var kids = c.GetProperty("kids").EnumerateArray().ToList();
            var rights = kids.Select(k => k.GetProperty("r").GetDouble()).ToList();
            if (kids.Count == 4 && rights[3] < rights.Max())
                errors.Add($"{what}: edit right {rights[3]:F1} is not the row's largest ({rights.Max():F1})");
            var tops = kids.Select(k => k.GetProperty("t").GetDouble()).ToList();
            if (tops.Max() - tops.Min() > 0.5)
                errors.Add($"{what}: the row is not one line (tops {string.Join(", ", tops.Select(t => t.ToString("F1")))})");
            foreach (var (k, i) in kids.Select((k, i) => (k, i)))
                if (k.GetProperty("w").GetDouble() < 44 - 0.01 || k.GetProperty("h").GetDouble() < 44 - 0.01)
                    errors.Add($"{what}: target {kinds.ElementAtOrDefault(i)} is {k.GetProperty("w").GetDouble():F1}x{k.GetProperty("h").GetDouble():F1}");
            if (c.GetProperty("words").GetInt32() != 0)
                errors.Add($"{what}: {c.GetProperty("words").GetInt32()} a.text-link/.btn in the row");
            if (!c.GetProperty("rowInCard").GetBoolean())
                errors.Add($"{what}: the row is not a direct child of the card");
            var row = c.GetProperty("row");
            var cell = c.GetProperty("cell");
            if (row.GetProperty("t").GetDouble() < cell.GetProperty("b").GetDouble() - 0.5)
                errors.Add($"{what}: the row top {row.GetProperty("t").GetDouble():F1} is above the cell bottom");
            heights.Add(Math.Round(row.GetProperty("h").GetDouble(), 2));

            // The band: inside the cell, on its bottom edge region, one line, full title as a tip.
            if (c.GetProperty("band").ValueKind != JsonValueKind.Object)
            {
                errors.Add($"{what}: no title band in the cell");
                continue;
            }
            var band = c.GetProperty("band");
            double B(JsonElement e, string s) => e.GetProperty(s).GetDouble();
            if (B(band, "l") < B(cell, "l") - 1 || B(band, "r") > B(cell, "r") + 1 || B(band, "t") < B(cell, "t") - 1 || B(band, "b") > B(cell, "b") + 1)
                errors.Add($"{what}: band [{B(band, "l"):F1},{B(band, "t"):F1},{B(band, "r"):F1},{B(band, "b"):F1}] is not inside the cell");
            if (!c.GetProperty("cover").GetBoolean())
            {
                if (c.GetProperty("bandIsSpan").GetBoolean() != true)
                    errors.Add($"{what}: the empty card's band is not span.gal-band");
                if (Math.Abs(B(band, "b") - B(cell, "b")) > 1)
                    errors.Add($"{what}: the empty band bottom {B(band, "b"):F1} is not the cell's {B(cell, "b"):F1}");
            }
            if (c.GetProperty("tip").GetString() != c.GetProperty("text").GetString())
                errors.Add($"{what}: title attribute '{c.GetProperty("tip").GetString()}' is not the text");
            if (!c.GetProperty("oneLine").GetBoolean())
                errors.Add($"{what}: the title is not one line");
            if (c.GetProperty("under").GetInt32() != 0)
                errors.Add($"{what}: a title line is still under the cell");
            var fg = Nums(c.GetProperty("fg"));
            var bg = Nums(c.GetProperty("bg"));
            bandColours.Add($"{string.Join(",", fg)}|{string.Join(",", bg)}");

            // SC-004 over the public covers (no dimming), as 015 measured it.
            if (c.GetProperty("photo").ValueKind == JsonValueKind.Array && c.GetProperty("text").GetString() is "White" or "Black" or "Grey")
            {
                if (c.GetProperty("mainLoaded").GetBoolean())
                    errors.Add($"{what}: a main image loaded; only the placeholder must show");
                var photo = Nums(c.GetProperty("photo"));
                if (bg[3] <= 0) errors.Add($"{what}: the band has no background");
                var under = Composite(bg, photo);
                var ratio = Contrast(Composite(fg, under), under);
                tones.Add($"#{(int)photo[0]:x2}{(int)photo[1]:x2}{(int)photo[2]:x2}");
                if (ratio < 4.5) errors.Add($"{what}: band contrast {ratio:F2} over #{(int)photo[0]:x2}{(int)photo[1]:x2}{(int)photo[2]:x2}");
            }
        }
        Assert.True(errors.Count == 0, string.Join("\n", errors));
        Assert.Equal(["#ffffff", "#000000", "#808080"], tones);
        Assert.Single(heights.Distinct());               // SC-001: one row height on every card
        Assert.True(heights[0] >= 44 - 0.01, $"row height {heights[0]}");
        Assert.Equal(["255,255,255,1|0,0,0,0.6"], bandColours.ToList()); // empty band == .on-photo values
    }

    // ── US2 [SAFETY]: publish and unpublish by click, scripting on and off ─────────────────

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task US2_the_eye_off_publishes_and_the_eye_unpublishes_read_from_storage(bool script)
    {
        await using var site = await fx.NewSiteAsync();
        var ids = await site.ImagesAsync(2);
        var gid = await site.GalleryAsync("Coast", ids);
        var page = script ? await OpenAsync(site, List) : await NoScriptAsync(site);

        Assert.Equal("eye-off", await VisIconAsync(page, gid));
        Assert.Equal(1, await Card(page, gid).Locator(".gallery-actions button.ga-vis[type='submit'][name='public'][value='on']").CountAsync());
        await NavigatesAsync(page, () => Vis(page, gid).ClickAsync());
        Assert.Equal("public", (await site.GalleryDocAsync(gid)).Visibility);
        Assert.Equal("eye", await VisIconAsync(page, gid));
        Assert.Equal("Public. Make private", await Vis(page, gid).GetAttributeAsync("aria-label"));
        Assert.Equal(1, await Card(page, gid).Locator(".gallery-actions button.ga-vis[type='submit']:not([name])").CountAsync());

        await NavigatesAsync(page, () => Vis(page, gid).ClickAsync());
        Assert.Equal("private", (await site.GalleryDocAsync(gid)).Visibility);
        Assert.Equal("eye-off", await VisIconAsync(page, gid));
        Assert.Equal("Private. Make public", await Vis(page, gid).GetAttributeAsync("aria-label"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task US2_the_empty_private_control_is_not_a_button_and_a_click_changes_nothing(bool script)
    {
        await using var site = await fx.NewSiteAsync();
        var gid = await site.GalleryAsync("Empty", []);
        var before = await site.GalleryDocAsync(gid);
        var page = script ? await OpenAsync(site, List) : await NoScriptAsync(site);

        var vis = Vis(page, gid);
        Assert.Equal(1, await vis.CountAsync());
        Assert.Equal(1, await Card(page, gid).Locator(".gallery-actions span.ga-vis.is-unavailable[role='img']").CountAsync());
        Assert.Equal(0, await Card(page, gid).Locator(".gallery-actions button.ga-vis, .gallery-actions a.ga-vis").CountAsync());

        var url = page.Url;
        var posts = await PostsDuring(page, () => vis.ClickAsync());
        Assert.Empty(posts);
        Assert.Equal(url, page.Url);
        var after = await site.GalleryDocAsync(gid);
        Assert.Equal((before.Visibility, before.UpdatedAt), (after.Visibility, after.UpdatedAt));
        Assert.Equal(before.Items, after.Items);
        Assert.Equal("private", after.Visibility);
    }

    [Fact]
    public async Task SC006_each_eye_and_its_name_match_storage_for_the_four_states()
    {
        await using var site = await fx.NewSiteAsync();
        var (gids, _) = await SixCardsAsync(site);
        var page = await OpenAsync(site, List);

        // Public (White), private (Harbour), empty-public (Bare), empty-private (Empty).
        var expected = new Dictionary<string, (string Icon, string Name, string Tag)>
        {
            [gids[0]] = ("eye", "Public. Make private", "BUTTON"),
            [gids[3]] = ("eye-off", "Private. Make public", "BUTTON"),
            [gids[4]] = ("eye", "Public, hidden until it has a photo. Make private", "BUTTON"),
            [gids[5]] = ("eye-off", "Private. Add a photo before making this gallery public", "SPAN"),
        };
        foreach (var (gid, want) in expected)
        {
            var doc = await site.GalleryDocAsync(gid);
            var isPublic = doc.Visibility == "public";
            var hasPhotos = doc.Items.Count > 0;
            Assert.Equal(want.Name, GalleryActions.VisibilityLabel(isPublic, hasPhotos));
            Assert.Equal(want.Icon, GalleryActions.VisibilityIcon(isPublic));

            var vis = Vis(page, gid);
            Assert.Equal(1, await vis.CountAsync());
            var got = (await VisIconAsync(page, gid), await vis.GetAttributeAsync("aria-label"),
                await vis.GetAttributeAsync("title"), await vis.EvaluateAsync<string>("e => e.tagName"));
            Assert.Equal((want.Icon, want.Name, want.Name, want.Tag), got);
            Assert.Equal(1, await Card(page, gid).GetByRole(want.Tag == "SPAN" ? AriaRole.Img : AriaRole.Button,
                new() { Name = want.Name, Exact = true }).CountAsync());
        }
    }

    // ── US3: reorder kept (keyboard and mouse, on the move icon) ───────────────────────────

    [Fact]
    public async Task US3_keyboard_lift_move_and_drop_on_the_move_icon_saves_the_order()
    {
        await using var site = await fx.NewSiteAsync();
        var ids = await site.ImagesAsync(3);
        var a = await site.GalleryAsync("Alpha", [ids[0]]);
        var b = await site.GalleryAsync("Beta", [ids[1]]);
        var c = await site.GalleryAsync("Gamma", [ids[2]]);
        Assert.Equal([a, b, c], await site.HomeOrderAsync());
        var page = await OpenAsync(site, List);

        var handle = Card(page, a).Locator(".gallery-actions .handle");
        Assert.Equal("Move Alpha", await handle.GetAttributeAsync("aria-label"));
        await handle.FocusAsync();
        await page.Keyboard.PressAsync("Space");
        Assert.Equal("true", await handle.GetAttributeAsync("aria-pressed"));
        await page.Keyboard.PressAsync("ArrowRight");
        await NavigatesAsync(page, () => page.Keyboard.PressAsync("Space"));
        Assert.Equal([b, a, c], await site.HomeOrderAsync());
    }

    [Fact]
    public async Task US3_a_mouse_drag_of_the_third_card_to_the_front_saves_the_order()
    {
        await using var site = await fx.NewSiteAsync();
        var ids = await site.ImagesAsync(3);
        var a = await site.GalleryAsync("Alpha", [ids[0]]);
        var b = await site.GalleryAsync("Beta", [ids[1]]);
        var c = await site.GalleryAsync("Gamma", [ids[2]]);
        Assert.Equal([a, b, c], await site.HomeOrderAsync());
        var page = await OpenAsync(site, List);

        var grid = page.Locator("section.gal reorder-grid");
        var from = await grid.Locator(".cs > *").Nth(2).Locator(".gallery-actions .handle").BoundingBoxAsync();
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
}
