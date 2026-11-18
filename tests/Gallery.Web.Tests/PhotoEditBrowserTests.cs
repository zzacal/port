using Gallery.Web.Data;
using Gallery.Web.Tests.Fixtures;
using Microsoft.Playwright;
using MongoDB.Driver;

namespace Gallery.Web.Tests;

/// <summary>
/// 017 T016 (US1, US2; SC-001, SC-002, SC-004; FR-003, FR-005, FR-006, FR-007): the photo edit
/// screen in a real browser. Geometry is MEASURED on the rendered page: the photo's <c>.frame</c>
/// (the box <c>Picture</c> sizes from the stored dimensions, not the bitmap, which the routed image
/// host answers 404). The mouse is parked at (0,0) before every navigation.
/// </summary>
[Collection("library-browser")]
[Trait("Category", "Browser")]
public sealed class PhotoEditBrowserTests(LibraryBrowserFixture fx)
{
    private const float WaitMs = 15_000;

    private sealed record Box(double X, double Y, double W, double H, double ScrollY, double ScrollWidth, double InnerWidth, double InnerHeight)
    {
        public double Area => W * H;
    }

    // ── helpers ────────────────────────────────────────────────────────────────────────────

    /// <summary>Inserts an upload-shaped image with stored <paramref name="w"/> × <paramref name="h"/>.</summary>
    private static async Task<string> ShapedAsync(LibrarySite site, int w, int h, string title)
    {
        var image = SeedShapes.UploadedImage(title: title);
        image.Width = w;
        image.Height = h;
        await site.Images.InsertOneAsync(image);
        return image.Id;
    }

    private static async Task<IPage> GotoAsync(IPage page, LibrarySite site, string path)
    {
        await page.Mouse.MoveAsync(0, 0);
        var resp = await page.GotoAsync(site.BaseUrl + path, new() { WaitUntil = WaitUntilState.NetworkIdle });
        Assert.True(resp is not null && resp.Status == 200 && new Uri(page.Url).AbsolutePath == path.Split('?')[0],
            $"GET {path} answered {resp?.Status} at {page.Url}");
        return page;
    }

    private static async Task<IPage> OpenAsync(LibrarySite site, string path, int width = 1440, int height = 900)
    {
        var ctx = await site.NewContextAsync(width, height);
        return await GotoAsync(await ctx.NewPageAsync(), site, path);
    }

    /// <summary>A context with JavaScript off, carrying the owner session copied from a JS-on context.</summary>
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
        return await ctx.NewPageAsync();
    }

    /// <summary>Runs <paramref name="action"/> and waits for the main frame to navigate and load.</summary>
    private static async Task NavigatesAsync(IPage page, Func<Task> action, string what)
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
            Assert.True(done == committed.Task, $"{what} did not navigate (at {page.Url})");
        }
        finally
        {
            page.FrameNavigated -= OnNavigated;
        }
        await BrowserWait.LoadedAsync(page, WaitMs);
        await page.Mouse.MoveAsync(0, 0);
    }

    private static async Task<Box> BoxAsync(IPage page, string sel)
    {
        var v = await page.EvaluateAsync<double[]>("""
            (sel) => {
              const el = document.querySelector(sel);
              if (!el) throw new Error('no ' + sel);
              const r = el.getBoundingClientRect();
              return [r.x, r.y, r.width, r.height, scrollY, document.documentElement.scrollWidth, innerWidth, innerHeight];
            }
            """, sel);
        return new Box(v[0], v[1], v[2], v[3], v[4], v[5], v[6], v[7]);
    }

    private const string Frame = ".pe-stage .frame";

    private static string Query(IPage page) => new Uri(page.Url).Query;

    private static Task<bool> InViewportAsync(IPage page, string sel) =>
        page.EvaluateAsync<bool>("(sel) => { const el = document.querySelector(sel); if (!el) return false; const r = el.getBoundingClientRect(); return r.top >= 0 && r.top < innerHeight; }", sel);

    // ── SC-002: the whole photo, large, at 1440 × 900 ─────────────────────────────────────

    [Theory]
    [InlineData(1280, 960)]
    [InlineData(960, 1280)]
    public async Task SC002_at_1440_the_photo_is_wholly_visible_at_its_stored_aspect_and_at_least_3x_the_tile(int w, int h)
    {
        await using var site = await fx.NewSiteAsync();
        var id = await ShapedAsync(site, w, h, "Shape");

        var lib = await OpenAsync(site, "/admin/library");
        var cell = await BoxAsync(lib, $"#t-{id} .cell");

        var page = await OpenAsync(site, $"/admin/library/images/{id}?page=1");
        var f = await BoxAsync(page, Frame);
        Assert.Equal(0, f.ScrollY);
        Assert.True(f.Y >= 0 && f.Y + f.H <= 900, $"frame y {f.Y}..{f.Y + f.H} is not inside 0..900");
        Assert.True(f.X >= 0 && f.X + f.W <= 1440, $"frame x {f.X}..{f.X + f.W} is not inside 0..1440");
        var aspect = f.W / f.H;
        var stored = (double)w / h;
        Assert.True(Math.Abs(aspect - stored) / stored <= 0.01, $"frame aspect {aspect:F4} vs stored {stored:F4}");
        Assert.True(f.Area >= 3 * cell.Area, $"frame {f.W:F0}×{f.H:F0} = {f.Area:F0} < 3 × tile cell {cell.W:F0}×{cell.H:F0}");
    }

    [Fact]
    public async Task SC002_a_small_photo_is_not_upscaled_at_1440()
    {
        await using var site = await fx.NewSiteAsync();
        var id = await ShapedAsync(site, 400, 300, "Small");
        var page = await OpenAsync(site, $"/admin/library/images/{id}?page=1");
        var f = await BoxAsync(page, Frame);
        Assert.True(f.W <= 400, $"frame width {f.W} > 400 (upscaled)");
        Assert.True(f.W > 0 && f.H > 0);
    }

    [Theory]
    [InlineData(1280, 960)]
    [InlineData(960, 1280)]
    public async Task SC002_at_390_the_photo_is_wholly_visible_on_load_and_nothing_scrolls_sideways(int w, int h)
    {
        await using var site = await fx.NewSiteAsync();
        var id = await ShapedAsync(site, w, h, "Shape");
        var page = await OpenAsync(site, $"/admin/library/images/{id}?page=1", 390, 844);
        var f = await BoxAsync(page, Frame);
        Assert.True(f.Y >= 0 && f.Y + f.H <= f.InnerHeight, $"frame y {f.Y}..{f.Y + f.H} is not inside 0..{f.InnerHeight}");
        Assert.True(f.X >= 0 && f.X + f.W <= f.InnerWidth, $"frame x {f.X}..{f.X + f.W} is not inside 0..{f.InnerWidth}");
        Assert.True(f.ScrollWidth <= f.InnerWidth, $"scrollWidth {f.ScrollWidth} > innerWidth {f.InnerWidth}");
    }

    [Fact]
    public async Task At_1440_the_browser_picks_a_derivative_at_least_960_wide()
    {
        await using var site = await fx.NewSiteAsync();
        var ids = await site.ImagesAsync(1);
        var page = await OpenAsync(site, $"/admin/library/images/{ids[0]}?page=1");
        // The image host answers 404, but currentSrc still names the candidate the browser chose.
        var width = await page.EvaluateAsync<int>("""
            () => {
              const img = document.querySelector('.pe-stage picture.main img');
              const src = img.currentSrc;
              for (const s of img.parentElement.querySelectorAll('source')) {
                for (const c of s.srcset.split(',')) {
                  const [url, d] = c.trim().split(/\s+/);
                  if (url === src) return parseInt(d, 10);
                }
              }
              return -1;
            }
            """);
        Assert.True(width >= 960, $"currentSrc width descriptor {width} < 960");
    }

    // ── SC-004 / SC-001: back to page N at the tile; folder = pencil ───────────────────────

    [Fact]
    public async Task SC004_save_then_breadcrumb_returns_to_library_page_2_with_the_tile_in_view()
    {
        await using var site = await fx.NewSiteAsync(new() { ["Library:PageSize"] = "4" });
        var ids = await site.ImagesAsync(10);
        // Newest first: page 2 holds ids[5], ids[4], ids[3], ids[2]; take its last tile.
        var target = ids[2];
        var page = await OpenAsync(site, "/admin/library?page=2", 390, 844);

        await NavigatesAsync(page, () => page.Locator($"#t-{target} .pa-edit").ClickAsync(), "the pencil");
        Assert.Equal($"/admin/library/images/{target}", new Uri(page.Url).AbsolutePath);
        Assert.Equal("?page=2", Query(page));

        await page.Locator("#lib-edit input[name=\"title\"]").FillAsync("Renamed on page 2");
        await NavigatesAsync(page, () => page.Locator("#lib-edit .side-panel").GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).ClickAsync(), "Save");
        Assert.Equal($"/admin/library/images/{target}", new Uri(page.Url).AbsolutePath);
        Assert.Equal("Renamed on page 2", (await site.Images.Find(i => i.Id == target).FirstAsync()).Title);

        await NavigatesAsync(page, () => page.Locator("nav.breadcrumb a", new() { HasText = "Library" }).ClickAsync(), "the Library crumb");
        Assert.Equal("/admin/library", new Uri(page.Url).AbsolutePath);
        Assert.Equal("?page=2", Query(page));
        Assert.True(await InViewportAsync(page, $"#t-{target}"), $"tile {target} is not in the viewport after going back");
    }

    [Fact]
    public async Task SC001_the_folder_icon_opens_the_same_screen_as_the_pencil()
    {
        await using var site = await fx.NewSiteAsync();
        var ids = await site.ImagesAsync(3);
        await site.GalleryAsync("Coast", [ids[1]]);
        var page = await OpenAsync(site, "/admin/library?page=1");

        await NavigatesAsync(page, () => page.Locator($"#t-{ids[1]} .pa-group").ClickAsync(), "the folder");
        var viaFolder = page.Url;
        Assert.Equal(1, await page.Locator("#lib-edit").CountAsync());

        await GotoAsync(page, site, "/admin/library?page=1");
        await NavigatesAsync(page, () => page.Locator($"#t-{ids[1]} .pa-edit").ClickAsync(), "the pencil");
        Assert.Equal(viaFolder, page.Url);
        Assert.Equal($"{site.BaseUrl}/admin/library/images/{ids[1]}?page=1", viaFolder);
    }

    // ── without JS; delete ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Without_JS_a_caption_save_is_stored_and_the_page_says_Saved()
    {
        await using var site = await fx.NewSiteAsync();
        var ids = await site.ImagesAsync(1);
        var page = await NoScriptAsync(site);
        await GotoAsync(page, site, $"/admin/library/images/{ids[0]}?page=1");

        await page.Locator("#lib-edit textarea[name=\"caption\"]").FillAsync("Written without script");
        await NavigatesAsync(page, () => page.Locator("#lib-edit .side-panel").GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).ClickAsync(), "Save");

        Assert.Equal("Written without script", (await site.Images.Find(i => i.Id == ids[0]).FirstAsync()).Caption);
        Assert.Equal("Saved", (await page.Locator("p.lib-result[role=\"status\"]").InnerTextAsync()).Trim());
        Assert.Equal("Saved", (await page.Locator(".sp-status").InnerTextAsync()).Trim());
    }

    [Fact]
    public async Task Deleting_a_page_2_photo_from_its_edit_screen_lands_on_library_page_2()
    {
        await using var site = await fx.NewSiteAsync(new() { ["Library:PageSize"] = "4" });
        var ids = await site.ImagesAsync(10);
        var target = ids[4];
        var page = await OpenAsync(site, $"/admin/library/images/{target}?page=2");

        await page.Locator("#lib-edit .danger-confirm .danger-trigger").ClickAsync();
        var dialog = page.Locator("#lib-edit dialog.danger-dialog");
        await NavigatesAsync(page, () => dialog.GetByRole(AriaRole.Button, new() { Name = "Delete photo", Exact = true }).ClickAsync(), "Delete photo");

        Assert.Equal("/admin/library", new Uri(page.Url).AbsolutePath);
        Assert.Equal("2", System.Web.HttpUtility.ParseQueryString(Query(page))["page"]);
        Assert.Equal("1 deleted", (await page.Locator("p.lib-result[role=\"status\"]").InnerTextAsync()).Trim());
        Assert.Equal(0, await site.Images.CountDocumentsAsync(i => i.Id == target && i.DeletingAt == null));
    }
}
