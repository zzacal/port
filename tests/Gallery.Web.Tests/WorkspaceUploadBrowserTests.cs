using System.Text.Json;
using System.Text.RegularExpressions;
using Amazon.S3.Model;
using Gallery.Web.Data;
using Gallery.Web.Library;
using Gallery.Web.Storage;
using Gallery.Web.Tests.Fixtures;
using Gallery.Web.Upload;
using Gallery.Web.Workspace;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using MongoDB.Driver;

namespace Gallery.Web.Tests;

/// <summary>
/// 020 T047 (US2; contracts/components.md K7, K10, C13; contracts/http.md U2–U13): the upload tray in
/// the workspace, in a real browser at 1440 × 900, against the S3 emulator. upload.js, its module
/// worker and the vendored encoders run for real. The old UploadBrowserTests map here (TI-70); the
/// rest is US2: each photo joins as it finishes (U12) and appears marked New, a gallery destination
/// is kept across a collection switch, Hide/Show, Done (U13), a reload reopens the tray, SC-013's
/// U7 → U12 ≤ 2 s, and [SAFETY II.5a] the public notice before any file is picked.
/// Each test gets its own app and database (<see cref="UploadBrowserFixture.NewSiteAsync"/>).
/// </summary>
[Collection("upload-browser")]
[Trait("Category", "Browser")]
public sealed class WorkspaceUploadBrowserTests(UploadBrowserFixture fx)
{
    private const float LongWaitMs = 120_000;
    private const float WaitMs = 30_000;
    private const string PublicNotice = "Coast is public. Visitors will see these photos as soon as each one is added.";

    // ── helpers ────────────────────────────────────────────────────────────────────────────

    private static string Fixture(string name)
    {
        var upload = Path.Combine(Render.RepoRoot, "tests", "fixtures", "upload", name);
        return File.Exists(upload) ? upload : Path.Combine(Render.RepoRoot, "tests", "fixtures", name);
    }

    /// <summary>Copies of one fixture under distinct names (p1.jpg …) in a temp dir.</summary>
    private static string[] Copies(DirectoryInfo dir, int n, string source = "gps-camera.jpg", string prefix = "p")
    {
        var bytes = File.ReadAllBytes(Fixture(source));
        return Enumerable.Range(1, n).Select(i =>
        {
            var path = Path.Combine(dir.FullName, $"{prefix}{i}.jpg");
            File.WriteAllBytes(path, bytes);
            return path;
        }).ToArray();
    }

    /// <summary>The owner's workspace at <paramref name="path"/>, loaded, with workspace.js ready (its U2 check done).</summary>
    private async Task<IPage> OpenAsync(UploadSite site, string path, string? initScript = null,
        Func<IPage, Task>? before = null, bool javaScript = true)
    {
        var page = await site.OpenWorkspaceAsync(path, javaScript, async p =>
        {
            await p.Mouse.MoveAsync(0, 0);
            await p.RouteAsync(fx.Storage.ImageBaseUrl + "/**", r => r.FulfillAsync(new() { Status = 404 }));
            if (initScript is not null) await p.AddInitScriptAsync(initScript);
            if (before is not null) await before(p);
        });
        if (javaScript) await ReadyAsync(page);
        return page;
    }

    private static Task ReadyAsync(IPage page) =>
        page.Locator("admin-workspace[data-script='ready']").WaitForAsync(new() { State = WaitForSelectorState.Attached, Timeout = WaitMs });

    private static ILocator Tray(IPage page) => page.Locator("upload-tray > .ws-tray");
    private static ILocator Rows(IPage page) => page.Locator("upload-tray .ws-up");
    private static ILocator Row(IPage page, string fileName) => page.Locator($"upload-tray .ws-up[aria-label='{fileName}']");
    private static ILocator Notice(IPage page) => page.Locator("upload-tray > .ws-tray > .ws-notice");
    private static ILocator TrayButton(IPage page, string value) => page.Locator($"upload-tray button[name='tray'][value='{value}']");

    private static async Task AddPhotosAsync(IPage page)
    {
        await page.Locator(".ws-main .ws-head .ws-add").ClickAsync();
        await Tray(page).WaitForAsync(new() { Timeout = WaitMs });
    }

    private static Task PickAsync(IPage page, params string[] paths) =>
        page.Locator("upload-tray input.dz-input").SetInputFilesAsync(paths);

    private static Task<string> HeadingAsync(IPage page) =>
        page.EvaluateAsync<string>("() => document.querySelector('upload-tray .ws-tray-head')?.textContent ?? '(no tray)'");

    private static async Task WaitForAsync(IPage page, string predicateJs, object? arg = null, string? what = null, float timeout = WaitMs)
    {
        try
        {
            await page.WaitForFunctionAsync(predicateJs, arg, new() { Timeout = timeout, PollingInterval = 50 });
        }
        catch (TimeoutException)
        {
            var rows = await page.EvaluateAsync<string>(
                "() => [...document.querySelectorAll('upload-tray .ws-up')].map(r => r.textContent.trim()).join(' | ')");
            throw new TimeoutException($"waited {timeout} ms for {what ?? predicateJs} at {page.Url}; heading '{await HeadingAsync(page)}'; rows [{rows}]");
        }
    }

    /// <summary>Waits until <paramref name="added"/> rows say Added and <paramref name="failed"/> show a problem.</summary>
    private static Task SettledAsync(IPage page, int added, int failed = 0, float timeout = LongWaitMs) =>
        WaitForAsync(page,
            "([a, f]) => document.querySelectorAll('upload-tray .ws-up .ws-up-ok').length === a && document.querySelectorAll('upload-tray .ws-up .ws-up-problem').length === f",
            new[] { added, failed }, $"{added} Added, {failed} failed", timeout);

    private static Task<string> RowWordAsync(ILocator row) =>
        row.EvaluateAsync<string>("r => (r.querySelector('.ws-up-reason') ?? r.querySelector('.ws-up-state') ?? r.querySelector('progress'))?.textContent.trim() ?? ''");

    /// <summary>Every request the page sends to the storage origin, recorded as it is issued.</summary>
    private List<IRequest> RecordStorage(IPage page)
    {
        var seen = new List<IRequest>();
        page.Request += (_, r) =>
        {
            if (r.Url.StartsWith(fx.S3Origin, StringComparison.Ordinal))
                lock (seen) seen.Add(r);
        };
        return seen;
    }

    /// <summary>The JSON bodies of every U3 register the page sends.</summary>
    private static List<JsonElement> RecordRegisters(IPage page)
    {
        var seen = new List<JsonElement>();
        page.Request += (_, r) =>
        {
            if (r.Method == "POST" && r.Url.EndsWith("/admin/upload/items", StringComparison.Ordinal) && r.PostData is { } body)
                lock (seen) seen.Add(JsonDocument.Parse(body).RootElement.Clone());
        };
        return seen;
    }

    /// <summary>Storage PUTs held for <paramref name="ms"/> each, so the tray stays Working long enough to look at.</summary>
    private Task SlowStorageAsync(IPage page, int ms) =>
        page.RouteAsync(fx.S3Origin + "/**", async r =>
        {
            await Task.Delay(ms);
            try { await r.ContinueAsync(); } catch (PlaywrightException) { }
        });

    private static IMongoCollection<ImageDoc> Images(UploadSite site) => site.Db.GetCollection<ImageDoc>("images");
    private static IMongoCollection<GalleryDoc> Galleries(UploadSite site) => site.Db.GetCollection<GalleryDoc>("galleries");

    private static async Task<List<ImageDoc>> NewImagesAsync(UploadSite site, IEnumerable<string> before) =>
        (await Images(site).Find(FilterDefinition<ImageDoc>.Empty).ToListAsync()).Where(i => !before.Contains(i.Id)).ToList();

    private static async Task<string> SeedImageAsync(UploadSite site, string title = "Seeded")
    {
        var image = SeedShapes.UploadedImage(createdAt: DateTime.UtcNow.AddHours(-1), title: title);
        await Images(site).InsertOneAsync(image);
        return image.Id;
    }

    private static async Task<string> GalleryAsync(UploadSite site, string title, IReadOnlyList<string> ids, bool publish = false)
    {
        using var scope = site.Services.CreateScope();
        var galleries = scope.ServiceProvider.GetRequiredService<GalleryService>();
        var created = await galleries.CreateAsync(title);
        Assert.True(created.IsOk, created.Reason);
        var gid = created.GalleryId!;
        if (ids.Count > 0) Assert.True((await galleries.AddAsync(gid, ids)).IsOk);
        if (publish) Assert.True((await galleries.PublishAsync(gid, confirmed: true)).IsOk);
        return gid;
    }

    private static Task<string[]> CellIdsAsync(IPage page) =>
        page.EvaluateAsync<string[]>("() => [...document.querySelectorAll('.ws-main .ws-cell')].map(c => c.dataset.id)");

    /// <summary>The ids of the cells carrying the New mark, in page order.</summary>
    private static Task<string[]> NewMarkedAsync(IPage page) =>
        page.EvaluateAsync<string[]>("() => [...document.querySelectorAll('.ws-main .ws-cell')].filter(c => [...c.querySelectorAll('.ws-cell-marks .ws-mark')].some(m => m.textContent === 'New')).map(c => c.dataset.id)");

    /// <summary>[SAFETY II.5a] The notice is on screen as plain text: the sentence, a real box, not in a details.</summary>
    private static async Task AssertNoticeShownAsync(IPage page, string when)
    {
        var notice = Notice(page);
        Assert.True(await notice.CountAsync() == 1, $"{when}: {await notice.CountAsync()} notices");
        Assert.True(await notice.IsVisibleAsync(), $"{when}: the notice is not visible");
        Assert.Equal(PublicNotice, (await notice.InnerTextAsync()).Trim());
        var box = await notice.BoundingBoxAsync();
        Assert.True(box is { Width: > 0, Height: > 0 }, $"{when}: the notice's box is {box?.Width}x{box?.Height}");
        Assert.False(await notice.EvaluateAsync<bool>("n => n.closest('details') !== null"), $"{when}: the notice is inside <details>");
        Assert.Null(await notice.GetAttributeAsync("title"));
    }

    // ── US2-1: Add photos opens the tray, naming where and the limits, before any file ─────

    [Fact]
    public async Task Add_photos_opens_the_tray_naming_where_and_the_limits_before_any_file()
    {
        await using var site = await fx.NewSiteAsync();
        var gid = await GalleryAsync(site, "Coast", [await SeedImageAsync(site)]);
        var page = await OpenAsync(site, "/admin");
        var registers = RecordRegisters(page);

        Assert.True(await page.Locator("upload-tray").EvaluateAsync<bool>("t => t.hidden && t.children.length === 0"));
        await AddPhotosAsync(page);
        Assert.Equal("Add photos to your library", await HeadingAsync(page));
        Assert.Equal("JPEG only · up to 20 MB each · up to 100 photos per batch",
            (await page.Locator("upload-tray .dz-limits").InnerTextAsync()).Trim());
        Assert.Equal(0, await Notice(page).CountAsync());
        Assert.Equal(["Close"], (await page.Locator("upload-tray button[name='tray']").AllInnerTextsAsync()).Select(t => t.Trim()).ToArray());
        // upload.js came by import(): the page still has exactly its one <script>.
        Assert.Equal(1, await page.EvaluateAsync<int>("() => document.scripts.length"));

        // Close (nothing registered) puts the tray away.
        await TrayButton(page, "close").ClickAsync();
        await WaitForAsync(page, "() => { const t = document.querySelector('upload-tray'); return t.hidden && t.children.length === 0; }", what: "the tray closed");

        await page.Locator($".ws-list a[href='/admin/g/{gid}']").ClickAsync();
        await WaitForAsync(page, "() => document.querySelector('.ws-main .ws-head h1')?.textContent === 'Coast'", what: "Coast");
        await AddPhotosAsync(page);
        Assert.Equal("Add photos to Coast", await HeadingAsync(page));
        Assert.Equal(0, await Notice(page).CountAsync()); // Coast is private
        lock (registers) Assert.Empty(registers);
    }

    // ── worker path (TI-70): two photos join as each finishes, no confirm ───────────────────

    [Fact]
    public async Task Two_photos_join_as_each_finishes_with_no_confirm()
    {
        await using var site = await fx.NewSiteAsync();
        var page = await OpenAsync(site, "/admin");
        var registers = RecordRegisters(page);

        await AddPhotosAsync(page);
        await PickAsync(page, Fixture("orientation-6.jpg"), Fixture("gps-camera.jpg"));
        await SettledAsync(page, 2);
        Assert.Equal("2 photos added to your library", await HeadingAsync(page));

        var images = await Images(site).Find(FilterDefinition<ImageDoc>.Empty).ToListAsync();
        Assert.Equal(2, images.Count);
        Assert.All(images, i => Assert.Empty(i.GalleryIds));
        var upright = Assert.Single(images, i => i.Title == "orientation-6");
        Assert.Equal(200, upright.Width);
        Assert.Equal(400, upright.Height);

        // Every public file of the orientation-6 photo parses upright.
        using var s3 = new S3ClientFactory(fx.Storage).CreateClient();
        foreach (var key in upright.Derivatives.SelectMany(d => new[] { d.Avif, d.Webp }))
        {
            using var obj = await s3.GetObjectAsync(fx.Storage.PublicBucket, key);
            using var ms = new MemoryStream();
            await obj.ResponseStream.CopyToAsync(ms);
            var parsed = ContainerParser.Parse(ms.ToArray());
            Assert.True(parsed.Ok, $"{key}: {parsed.Reason}");
            Assert.True(parsed.Height > parsed.Width, $"{key} is {parsed.Width}x{parsed.Height}, not upright");
        }
        Assert.Equal(2, site.LogLines.Count(l => l.StartsWith("op.timing browser.total ", StringComparison.Ordinal)));
        Assert.Equal(2, site.LogLines.Count(l => l.StartsWith("upload.joined ", StringComparison.Ordinal)));

        // Both cells are first in All photos, marked New; a library upload sends neither new field.
        var cells = await CellIdsAsync(page);
        Assert.Equal(images.Select(i => i.Id).Order(), cells.Take(2).Order());
        Assert.Equal(cells.Take(2).Order(), (await NewMarkedAsync(page)).Order());
        lock (registers)
        {
            var body = Assert.Single(registers);
            Assert.Equal(["files"], body.EnumerateObject().Select(p => p.Name).ToArray());
        }
    }

    // ── FR-044: a storage PUT carries content-type and nothing the page added beyond it ─────

    private static readonly HashSet<string> BrowserHeaders =
        ["content-type", "origin", "referer", "user-agent", "content-length", "host", "connection"];

    [Fact]
    public async Task Storage_PUTs_carry_only_content_type()
    {
        await using var site = await fx.NewSiteAsync();
        List<IRequest> storage = [];
        var page = await OpenAsync(site, "/admin", before: p => { storage = RecordStorage(p); return Task.CompletedTask; });

        await AddPhotosAsync(page);
        await PickAsync(page, Fixture("gps-camera.jpg"));
        // Settle on either end state first, so a refused PUT fails on the header assertion, not a timeout.
        await WaitForAsync(page, "() => document.querySelector('upload-tray .ws-up .ws-up-ok, upload-tray .ws-up .ws-up-problem') !== null",
            what: "an end state", timeout: LongWaitMs);

        List<IRequest> puts;
        lock (storage) puts = storage.Where(r => r.Method == "PUT").ToList();
        Assert.NotEmpty(puts);
        foreach (var put in puts)
        {
            var headers = await put.AllHeadersAsync();
            var extra = headers.Keys
                .Select(k => k.ToLowerInvariant())
                .Where(k => !BrowserHeaders.Contains(k) && !k.StartsWith("accept", StringComparison.Ordinal)
                            && !k.StartsWith("sec-", StringComparison.Ordinal))
                .ToList();
            Assert.True(extra.Count == 0, $"{put.Url.Split('?')[0]} sent [{string.Join(", ", extra)}]");
            Assert.DoesNotContain(headers.Keys, k => k.StartsWith("x-amz-", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(headers.Keys, k => k.Equals("cache-control", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(headers.Keys, k => k.Equals("cookie", StringComparison.OrdinalIgnoreCase));
            Assert.True(headers.ContainsKey("content-type"), $"{put.Url.Split('?')[0]} sent no content-type");
        }
        await SettledAsync(page, 1, 0, 5_000);
        // master.jpg, 320.avif, 320.webp (gps-camera.jpg is 400 px wide → ladder [320]); the join PUTs nothing.
        Assert.Equal(3, puts.Count);
        Assert.Equal(
            ["image/avif", "image/jpeg", "image/webp"],
            (await Task.WhenAll(puts.Select(p => p.HeaderValueAsync("content-type")))).Select(v => v ?? "").Order().ToArray());
    }

    // ── FR-004: refused at pick time, before any request ────────────────────────────────────

    [Fact]
    public async Task Pick_time_refusals()
    {
        // fixture-6000x4000.jpg is 383 661 bytes: over a 200 000-byte limit.
        await using var site = await fx.NewSiteAsync(new() { ["Upload:MaxFileBytes"] = "200000" });
        List<IRequest> storage = [];
        var page = await OpenAsync(site, "/admin", before: p => { storage = RecordStorage(p); return Task.CompletedTask; });
        var registers = RecordRegisters(page);

        await AddPhotosAsync(page);
        await PickAsync(page, Fixture("not-a-jpeg.jpg"), Fixture("fixture-6000x4000.jpg"));
        var rows = page.Locator("upload-tray .dz-refusals li");
        await rows.Nth(1).WaitForAsync(new() { Timeout = WaitMs });
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        Assert.Equal(
            ["not-a-jpeg.jpg — not a JPEG", "fixture-6000x4000.jpg — 0.4 MB, over the 0.2 MB limit"],
            (await rows.AllInnerTextsAsync()).Select(t => t.Trim()).ToArray());
        Assert.Equal(0, await Rows(page).CountAsync());
        lock (registers) Assert.Empty(registers);
        lock (storage) Assert.Empty(storage);
    }

    // ── US2-2: a drop anywhere on the window opens the tray ─────────────────────────────────

    [Fact]
    public async Task Dropping_files_anywhere_opens_the_tray_and_takes_them()
    {
        await using var site = await fx.NewSiteAsync();
        var page = await OpenAsync(site, "/admin");
        Assert.Equal(0, await Tray(page).CountAsync());
        var b64 = Convert.ToBase64String(await File.ReadAllBytesAsync(Fixture("orientation-6.jpg")));

        await page.EvaluateAsync(@"(b64) => {
            const bytes = Uint8Array.from(atob(b64), c => c.charCodeAt(0));
            const dt = new DataTransfer();
            dt.items.add(new File([bytes], 'orientation-6.jpg', { type: 'image/jpeg' }));
            const target = document.querySelector('.ws-list');
            target.dispatchEvent(new DragEvent('dragover', { bubbles: true, cancelable: true, dataTransfer: dt }));
            target.dispatchEvent(new DragEvent('drop', { bubbles: true, cancelable: true, dataTransfer: dt }));
        }", b64);

        await Row(page, "orientation-6.jpg").WaitForAsync(new() { Timeout = WaitMs });
        Assert.True(await Tray(page).IsVisibleAsync());
        Assert.Equal(1, await Rows(page).CountAsync());
        await SettledAsync(page, 1);
        Assert.Equal(1, await Images(site).CountDocumentsAsync(FilterDefinition<ImageDoc>.Empty));
    }

    // ── FR-017, US2-8: one broken photo fails alone; the others are added ──────────────────

    // A real JPEG header (gps-camera.jpg up to and including its SOS segment) and random entropy-coded
    // data: the server's master check accepts it and the worker's decode fails.
    private static async Task<string> WriteGarbageJpeg(string dir)
    {
        var real = await File.ReadAllBytesAsync(Fixture("gps-camera.jpg"));
        var sos = -1;
        for (var i = 2; i < real.Length - 3 && sos < 0; i++)
            if (real[i] == 0xFF && real[i + 1] == 0xDA) sos = i;
        Assert.True(sos > 0, "gps-camera.jpg has no SOS marker");
        var header = real[..(sos + 2 + ((real[sos + 2] << 8) | real[sos + 3]))];
        var random = new byte[4096];
        Random.Shared.NextBytes(random);
        var path = Path.Combine(dir, "garbage.jpg");
        await File.WriteAllBytesAsync(path, [.. header, .. random]);
        return path;
    }

    [Fact]
    public async Task A_failed_photo_holds_nothing_up_and_shows_Retry_and_Remove()
    {
        var dir = Directory.CreateTempSubdirectory("gallery-ws-upload-");
        try
        {
            var garbage = await WriteGarbageJpeg(dir.FullName);
            await using var site = await fx.NewSiteAsync();
            var page = await OpenAsync(site, "/admin");
            await AddPhotosAsync(page);
            await PickAsync(page, garbage, Fixture("gps-camera.jpg"));
            await SettledAsync(page, 1, 1);

            var failed = Row(page, "garbage.jpg");
            var reason = (await failed.Locator(".ws-up-reason").InnerTextAsync()).Trim();
            Assert.StartsWith("Could not prepare sizes", reason);
            var server = await page.EvaluateAsync<string>(
                "async () => { const s = await (await fetch('/admin/upload/state')).json(); " +
                "const i = s.batch.items.find(x => x.fileName === 'garbage.jpg'); return i.state + '|' + i.reason + '|' + s.batch.items.length + '|' + s.batch.added.length; }");
            Assert.Equal($"failed|{reason}|1|1", server);
            Assert.Equal(["Retry", "Remove"], (await failed.Locator("button").AllInnerTextsAsync()).Select(t => t.Trim()).ToArray());
            Assert.True(await failed.GetByRole(AriaRole.Button, new() { Name = "Retry", Exact = true }).IsVisibleAsync());
            Assert.True(await failed.GetByRole(AriaRole.Button, new() { Name = "Remove", Exact = true }).IsVisibleAsync());
            Assert.Equal("Added", await RowWordAsync(Row(page, "gps-camera.jpg")));
            Assert.Equal(1, await Images(site).CountDocumentsAsync(FilterDefinition<ImageDoc>.Empty));
            Assert.Equal("1 photo added · 1 failed", await HeadingAsync(page));
            Assert.True(await TrayButton(page, "done").IsEnabledAsync());

            // Remove drops the failed row; the tray then reads as finished.
            await failed.GetByRole(AriaRole.Button, new() { Name = "Remove", Exact = true }).ClickAsync();
            await WaitForAsync(page, "() => document.querySelectorAll('upload-tray .ws-up').length === 1", what: "one row left");
            Assert.Equal("1 photo added to your library", await HeadingAsync(page));
        }
        finally
        {
            dir.Delete(true);
        }
    }

    // ── FR-011, E-04: Retry with no stored original asks for the file again ─────────────────

    private async Task<IPage> FailMasterThenReloadAndRetry(UploadSite site)
    {
        var page = await OpenAsync(site, "/admin");
        await page.RouteAsync(fx.S3Origin + "/**", r => r.AbortAsync());
        await AddPhotosAsync(page);
        await PickAsync(page, Fixture("gps-camera.jpg"));
        await SettledAsync(page, 0, 1);
        Assert.Contains("upload failed", await Row(page, "gps-camera.jpg").Locator(".ws-up-reason").InnerTextAsync());

        await page.UnrouteAsync(fx.S3Origin + "/**");
        await page.Mouse.MoveAsync(0, 0);
        await page.ReloadAsync(new() { WaitUntil = WaitUntilState.Load });
        await ReadyAsync(page);
        await SettledAsync(page, 0, 1, WaitMs);
        await Row(page, "gps-camera.jpg").GetByRole(AriaRole.Button, new() { Name = "Retry", Exact = true }).ClickAsync();
        await Row(page, "gps-camera.jpg").GetByText("Pick gps-camera.jpg again").WaitForAsync(new() { Timeout = WaitMs });
        return page;
    }

    [Fact]
    public async Task Restart_without_master_asks_for_the_file()
    {
        // Same name: the item continues.
        await using (var site = await fx.NewSiteAsync())
        {
            var page = await FailMasterThenReloadAndRetry(site);
            await Row(page, "gps-camera.jpg").Locator("input.pick-again").SetInputFilesAsync(Fixture("gps-camera.jpg"));
            await SettledAsync(page, 1);
            Assert.Equal(1, await Rows(page).CountAsync());
        }

        // A different name: registered as a new item, the restarted one still waits for its file.
        await using (var site = await fx.NewSiteAsync())
        {
            var page = await FailMasterThenReloadAndRetry(site);
            await Row(page, "gps-camera.jpg").Locator("input.pick-again").SetInputFilesAsync(Fixture("orientation-6.jpg"));
            await WaitForAsync(page, "() => document.querySelector(\"upload-tray .ws-up[aria-label='orientation-6.jpg'] .ws-up-ok\") !== null",
                what: "orientation-6.jpg Added", timeout: LongWaitMs);
            Assert.Equal(2, await Rows(page).CountAsync());
            Assert.True(await Row(page, "gps-camera.jpg").GetByText("Pick gps-camera.jpg again").IsVisibleAsync());
        }
    }

    // ── US2-10: Done with failed photos drops them and says how many ────────────────────────

    [Fact]
    public async Task Done_with_failed_photos_drops_them_and_says_how_many()
    {
        var dir = Directory.CreateTempSubdirectory("gallery-ws-upload-");
        try
        {
            var garbage = await WriteGarbageJpeg(dir.FullName);
            await using var site = await fx.NewSiteAsync();
            var page = await OpenAsync(site, "/admin");
            await AddPhotosAsync(page);
            await PickAsync(page, garbage, Fixture("gps-camera.jpg"));
            await SettledAsync(page, 1, 1);

            var batches = site.Db.GetCollection<BatchDoc>(BatchStore.CollectionName);
            var batch = await batches.Find(b => b.State == BatchStates.Open).SingleAsync();
            var failedItem = Assert.Single(batch.Items);
            using var s3 = new S3ClientFactory(fx.Storage).CreateClient();
            var prefix = StagingKeys.ItemPrefix(batch.Id, failedItem.ItemId);
            Assert.NotEmpty((await s3.ListObjectsV2Async(new ListObjectsV2Request { BucketName = fx.Storage.PrivateBucket, Prefix = prefix })).S3Objects ?? []);

            await TrayButton(page, "done").ClickAsync();
            await WaitForAsync(page, "() => { const t = document.querySelector('upload-tray'); return t.hidden && t.children.length === 0; }", what: "the tray closed");
            await WaitForAsync(page, "() => document.querySelector('.ws-panel .ws-toast').textContent.length > 0", what: "the result line");
            Assert.Equal(Messages.NotAdded(1), await page.EvaluateAsync<string>("() => document.querySelector('.ws-panel .ws-toast').textContent"));

            var after = await batches.Find(b => b.Id == batch.Id).SingleAsync();
            Assert.Empty(after.Items);
            Assert.Empty(after.Added);
            Assert.Null(after.Destination);
            Assert.Empty((await s3.ListObjectsV2Async(new ListObjectsV2Request { BucketName = fx.Storage.PrivateBucket, Prefix = prefix })).S3Objects ?? []);
            Assert.Equal(1, await Images(site).CountDocumentsAsync(FilterDefinition<ImageDoc>.Empty));
            Assert.Single(site.LogLines, l => l == $"upload.cleared {batch.Id} 1");
        }
        finally
        {
            dir.Delete(true);
        }
    }

    // ── no script: the sentence, and no tray ────────────────────────────────────────────────

    [Fact]
    public async Task Without_scripting_the_workspace_says_it_needs_JavaScript()
    {
        await using var site = await fx.NewSiteAsync();
        var page = await OpenAsync(site, "/admin", javaScript: false);
        var notice = page.Locator(".ws-noscript");
        Assert.True(await notice.IsVisibleAsync());
        Assert.Equal("The admin needs JavaScript.", (await notice.InnerTextAsync()).Trim());
        Assert.False(await page.Locator("upload-tray").IsVisibleAsync());
        Assert.Equal(0, await Tray(page).CountAsync());
    }

    // ── encoding runs off the main thread ───────────────────────────────────────────────────

    [Fact]
    public async Task Main_thread_stays_responsive()
    {
        await using var site = await fx.NewSiteAsync();
        var page = await OpenAsync(site, "/admin", initScript: @"
            window.__longTasks = [];
            new PerformanceObserver(list => {
                for (const e of list.getEntries()) window.__longTasks.push(e.duration);
            }).observe({ type: 'longtask', buffered: true });");

        await AddPhotosAsync(page);
        await page.EvaluateAsync("() => { window.__longTasks.length = 0; }");
        await PickAsync(page, Fixture("fixture-6000x4000.jpg"));
        await SettledAsync(page, 1);

        var tasks = await page.EvaluateAsync<double[]>("window.__longTasks");
        Assert.True(tasks.All(ms => ms <= 200), $"long tasks: [{string.Join(", ", tasks.Select(t => $"{t:0} ms"))}]");
        Assert.Equal(1, site.LogLines.Count(l => l.StartsWith("op.timing browser.total ", StringComparison.Ordinal)));
    }

    // ── US2 independent test ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Four_photos_into_a_gallery_with_Hide_Show_Done_then_from_All_photos_into_no_gallery()
    {
        var dir = Directory.CreateTempSubdirectory("gallery-ws-upload-");
        try
        {
            await using var site = await fx.NewSiteAsync();
            var gid = await GalleryAsync(site, "Coast", []);
            var page = await OpenAsync(site, $"/admin/g/{gid}", before: p => SlowStorageAsync(p, 400));
            var registers = RecordRegisters(page);

            await AddPhotosAsync(page);
            Assert.Equal("Add photos to Coast", await HeadingAsync(page));
            await PickAsync(page, Copies(dir, 4, prefix: "g"));
            await WaitForAsync(page, "() => /^Adding to Coast · \\d of 4$/.test(document.querySelector('upload-tray .ws-tray-head')?.textContent ?? '')",
                what: "Working into Coast");

            // Hide: one line with its progress and Show; the work carries on.
            await TrayButton(page, "hide").ClickAsync();
            Assert.Equal(0, await page.Locator("upload-tray .ws-tray-body").CountAsync());
            Assert.Equal(0, await Rows(page).CountAsync());
            Assert.True(await TrayButton(page, "show").IsVisibleAsync());
            Assert.Matches(new Regex(@"^Adding to Coast · \d of 4$"), await HeadingAsync(page));
            await WaitForAsync(page, "() => document.querySelector('upload-tray .ws-tray-head')?.textContent === '4 photos added to Coast'",
                what: "all four added while hidden", timeout: LongWaitMs);
            await TrayButton(page, "show").ClickAsync();
            Assert.Equal(4, await page.Locator("upload-tray .ws-up .ws-up-ok").CountAsync());

            await TrayButton(page, "done").ClickAsync();
            await WaitForAsync(page, "() => document.querySelector('upload-tray').hidden", what: "the tray closed");

            var four = await NewImagesAsync(site, []);
            Assert.Equal(4, four.Count);
            var coast = await Galleries(site).Find(g => g.Id == gid).SingleAsync();
            Assert.Equal(four.Select(i => i.Id).Order(), coast.Items.Order());
            Assert.All(four, i => Assert.Equal([gid], i.GalleryIds));
            Assert.Equal("private", coast.Visibility);
            lock (registers)
            {
                var body = Assert.Single(registers);
                Assert.Equal(["files", "gallery"], body.EnumerateObject().Select(p => p.Name).ToArray());
                Assert.Equal(gid, body.GetProperty("gallery").GetString());
            }

            // All photos holds the four.
            await page.Locator(".ws-list a.ws-coll-row[href='/admin']").ClickAsync();
            await WaitForAsync(page, "() => document.querySelector('.ws-main .ws-head h1')?.textContent === 'All photos'", what: "All photos");
            Assert.Equal(four.Select(i => i.Id).Order(), (await CellIdsAsync(page)).Order());

            // Repeat from All photos: in no gallery.
            await AddPhotosAsync(page);
            Assert.Equal("Add photos to your library", await HeadingAsync(page));
            await PickAsync(page, Copies(dir, 2, prefix: "l"));
            await SettledAsync(page, 2);
            await TrayButton(page, "done").ClickAsync();
            await WaitForAsync(page, "() => document.querySelector('upload-tray').hidden", what: "the tray closed");
            var two = await NewImagesAsync(site, four.Select(i => i.Id));
            Assert.Equal(2, two.Count);
            Assert.All(two, i => Assert.Empty(i.GalleryIds));
            Assert.Equal(4, (await Galleries(site).Find(g => g.Id == gid).SingleAsync()).Items.Count);
        }
        finally
        {
            dir.Delete(true);
        }
    }

    // Owner walk-through on production: after a finished upload into one gallery, Add photos in another
    // gallery did nothing, and the finished tray's "Add more" kept adding to the first gallery. "Add more" is
    // gone (no two controls that do the same thing); Add photos after a finished set starts a new set for the
    // collection that is open; while a set is still working into another collection, Add photos is disabled.
    [Fact]
    public async Task Add_photos_after_a_finished_upload_starts_a_new_set_for_the_open_gallery()
    {
        var dir = Directory.CreateTempSubdirectory("gallery-ws-upload-");
        try
        {
            await using var site = await fx.NewSiteAsync();
            var urban = await GalleryAsync(site, "Urban", []);
            var birds = await GalleryAsync(site, "Birds", []);
            var page = await OpenAsync(site, $"/admin/g/{urban}", before: p => SlowStorageAsync(p, 800));
            var add = page.Locator(".ws-main .ws-head .ws-add");

            await AddPhotosAsync(page);
            await PickAsync(page, Copies(dir, 2, prefix: "u"));
            await WaitForAsync(page, "() => /^Adding to Urban/.test(document.querySelector('upload-tray .ws-tray-head')?.textContent ?? '')", what: "working into Urban");

            // Working into Urban while Birds is open: Add photos is unavailable, and looks it (Button's disabled state).
            await page.Locator($".ws-list a.ws-coll-row[data-gid='{birds}']").ClickAsync();
            await WaitForAsync(page, "() => document.querySelector('.ws-main .ws-head h1')?.textContent === 'Birds'", what: "Birds open");
            await WaitForAsync(page, "() => document.querySelector('.ws-main .ws-head .ws-add')?.disabled === true", what: "Add photos disabled");
            Assert.True(await add.EvaluateAsync<double>("b => parseFloat(getComputedStyle(b).opacity)") < 1);

            await SettledAsync(page, 2);
            Assert.Equal("2 photos added to Urban", await HeadingAsync(page));
            Assert.Equal(0, await TrayButton(page, "more").CountAsync());
            await WaitForAsync(page, "() => document.querySelector('.ws-main .ws-head .ws-add')?.disabled === false", what: "Add photos available again");
            await AddPhotosAsync(page);
            await WaitForAsync(page, "() => document.querySelector('upload-tray .ws-tray-head')?.textContent === 'Add photos to Birds'",
                what: "a new, empty set for Birds");
            await PickAsync(page, Copies(dir, 1, prefix: "b"));
            await SettledAsync(page, 1);
            Assert.Equal("1 photo added to Birds", await HeadingAsync(page));

            var urbanItems = (await Galleries(site).Find(g => g.Id == urban).SingleAsync()).Items;
            var birdItems = (await Galleries(site).Find(g => g.Id == birds).SingleAsync()).Items;
            Assert.Equal(2, urbanItems.Count);
            Assert.Single(birdItems);
            Assert.DoesNotContain(birdItems[0], urbanItems);
        }
        finally
        {
            dir.Delete(true);
        }
    }

    // ── [SAFETY II.5a] the public notice before any file is picked ──────────────────────────

    [Fact]
    public async Task Public_destination_notice_is_visible_before_picking_and_while_working()
    {
        var dir = Directory.CreateTempSubdirectory("gallery-ws-upload-");
        try
        {
            await using var site = await fx.NewSiteAsync();
            var seeded = await SeedImageAsync(site);
            var gid = await GalleryAsync(site, "Coast", [seeded], publish: true);
            var page = await OpenAsync(site, $"/admin/g/{gid}", before: p => SlowStorageAsync(p, 500));
            var registers = RecordRegisters(page);

            Assert.Equal(0, await Notice(page).CountAsync());
            await AddPhotosAsync(page);
            Assert.Equal("Add photos to Coast", await HeadingAsync(page));
            await AssertNoticeShownAsync(page, "before any file is picked");
            // The notice comes directly under the heading.
            Assert.True(await Notice(page).EvaluateAsync<bool>("n => n.previousElementSibling?.classList.contains('ws-tray-head') === true"));
            lock (registers) Assert.Empty(registers);

            await PickAsync(page, Copies(dir, 3));
            await WaitForAsync(page, "() => /^Adding to Coast · \\d of 3$/.test(document.querySelector('upload-tray .ws-tray-head')?.textContent ?? '')",
                what: "Working into Coast");
            await AssertNoticeShownAsync(page, "while working");

            await TrayButton(page, "hide").ClickAsync();
            await TrayButton(page, "show").WaitForAsync(new() { Timeout = WaitMs });
            Assert.Equal(0, await Rows(page).CountAsync());
            await AssertNoticeShownAsync(page, "while hidden");
            await TrayButton(page, "show").ClickAsync();

            await SettledAsync(page, 3);
            await AssertNoticeShownAsync(page, "finished");
            lock (registers)
            {
                var body = Assert.Single(registers);
                Assert.Equal(["files", "gallery", "visibleToVisitors"], body.EnumerateObject().Select(p => p.Name).ToArray());
                Assert.Equal(gid, body.GetProperty("gallery").GetString());
                Assert.True(body.GetProperty("visibleToVisitors").GetBoolean());
            }
            var coast = await Galleries(site).Find(g => g.Id == gid).SingleAsync();
            Assert.Equal("public", coast.Visibility);
            Assert.Equal(4, coast.Items.Count);
        }
        finally
        {
            dir.Delete(true);
        }
    }

    // ── US2-5: each photo appears at the end of the gallery as it finishes, marked New ──────

    [Fact]
    public async Task Each_photo_appears_as_it_finishes_marked_New()
    {
        var dir = Directory.CreateTempSubdirectory("gallery-ws-upload-");
        try
        {
            await using var site = await fx.NewSiteAsync();
            var seeded = await SeedImageAsync(site);
            var gid = await GalleryAsync(site, "Coast", [seeded]);
            // Each time a cell arrives in the grid: the grid's ids and how many rows say Added.
            const string Recorder = """
                window.__arrivals = [];
                document.addEventListener('DOMContentLoaded', () => {
                  new MutationObserver((records) => {
                    const added = records.flatMap(r => [...r.addedNodes]).filter(n => n instanceof Element && n.matches('.ws-cell'));
                    if (added.length === 0) return;
                    window.__arrivals.push({
                      ids: [...document.querySelectorAll('.ws-main .ws-cell')].map(c => c.dataset.id),
                      rows: document.querySelectorAll('upload-tray .ws-up').length,
                      done: document.querySelectorAll('upload-tray .ws-up .ws-up-ok').length,
                    });
                  }).observe(document.querySelector('.ws-main'), { childList: true, subtree: true });
                });
                """;
            var page = await OpenAsync(site, $"/admin/g/{gid}", initScript: Recorder);
            await AddPhotosAsync(page);
            await PickAsync(page, Copies(dir, 3));
            await SettledAsync(page, 3);
            await WaitForAsync(page, "() => document.querySelectorAll('.ws-main .ws-cell').length === 4", what: "four cells");

            var joined = (await NewImagesAsync(site, [seeded])).OrderBy(i => i.Title, StringComparer.Ordinal).Select(i => i.Id).ToList();
            Assert.Equal(3, joined.Count);
            var arrivals = await page.EvaluateAsync<JsonElement>("() => window.__arrivals");
            Assert.Equal(3, arrivals.GetArrayLength());
            for (var i = 0; i < 3; i++)
            {
                var a = arrivals[i];
                Assert.Equal([seeded, .. joined.Take(i + 1)], a.GetProperty("ids").EnumerateArray().Select(x => x.GetString()!).ToArray());
                Assert.Equal(3, a.GetProperty("rows").GetInt32());
            }
            // The first photo arrived while the others were still working.
            Assert.Equal(1, arrivals[0].GetProperty("done").GetInt32());
            string[] order = [seeded, .. joined];
            Assert.Equal(order, await CellIdsAsync(page));
            Assert.Equal(joined.ToArray(), await NewMarkedAsync(page));
            Assert.Equal(order, (await Galleries(site).Find(g => g.Id == gid).SingleAsync()).Items.ToArray());
        }
        finally
        {
            dir.Delete(true);
        }
    }

    // ── US2-4: the upload carries on across a collection switch ─────────────────────────────

    [Fact]
    public async Task Upload_carries_on_across_a_collection_switch()
    {
        var dir = Directory.CreateTempSubdirectory("gallery-ws-upload-");
        try
        {
            await using var site = await fx.NewSiteAsync();
            var seeded = await SeedImageAsync(site);
            var gid = await GalleryAsync(site, "Coast", [seeded]);
            var page = await OpenAsync(site, $"/admin/g/{gid}", before: p => SlowStorageAsync(p, 500));
            await AddPhotosAsync(page);
            await page.EvaluateAsync("() => { window.__tray = document.querySelector('upload-tray'); window.__section = window.__tray.firstElementChild; }");
            await PickAsync(page, Copies(dir, 3));
            await WaitForAsync(page, "() => /^Adding to Coast · 0 of 3$/.test(document.querySelector('upload-tray .ws-tray-head')?.textContent ?? '')",
                what: "Working into Coast");

            await page.Locator(".ws-list a.ws-coll-row[href='/admin']").ClickAsync();
            await WaitForAsync(page, "() => document.querySelector('.ws-main .ws-head h1')?.textContent === 'All photos'", what: "All photos");
            Assert.True(await page.EvaluateAsync<bool>("() => document.querySelector('upload-tray') === window.__tray && document.querySelectorAll('upload-tray').length === 1"));
            Assert.True(await Tray(page).IsVisibleAsync());
            Assert.Matches(new Regex(@"^Adding to Coast · \d of 3$"), await HeadingAsync(page));
            Assert.True(await page.EvaluateAsync<bool>("() => document.querySelector('.ws-main').firstElementChild?.matches('noscript, upload-tray') === true"));

            await SettledAsync(page, 3);
            Assert.Equal("3 photos added to Coast", await HeadingAsync(page));
            var joined = (await NewImagesAsync(site, [seeded])).Select(i => i.Id).ToList();
            Assert.Equal(3, joined.Count);
            var coast = await Galleries(site).Find(g => g.Id == gid).SingleAsync();
            Assert.Equal(seeded, coast.Items[0]);
            Assert.Equal(joined.Order(), coast.Items.Skip(1).Order());
            // In All photos every joined photo is shown, marked New.
            await WaitForAsync(page, "ids => ids.every(id => document.querySelector(`.ws-main .ws-cell[data-id='${id}']`))", joined.ToArray(), "the joined cells");
            Assert.Equal(joined.Order(), (await NewMarkedAsync(page)).Order());
        }
        finally
        {
            dir.Delete(true);
        }
    }

    // ── TI-33: a reload mid-upload reopens the tray with the interrupted rows ───────────────

    [Fact]
    public async Task Reload_mid_upload_reopens_the_tray_with_interrupted_rows()
    {
        var dir = Directory.CreateTempSubdirectory("gallery-ws-upload-");
        try
        {
            await using var site = await fx.NewSiteAsync(new() { ["Upload:HeartbeatTimeoutSeconds"] = "2" });
            // Storage never answers in time: the first photo stays Preparing, the second Waiting.
            var page = await OpenAsync(site, "/admin", before: p => p.RouteAsync(fx.S3Origin + "/**", async r =>
            {
                await Task.Delay(15_000);
                try { await r.AbortAsync(); } catch (PlaywrightException) { }
            }));
            await AddPhotosAsync(page);
            var files = Copies(dir, 2);
            await PickAsync(page, files);
            await WaitForAsync(page, "() => document.querySelector(\"upload-tray .ws-up[aria-label='p1.jpg'] .ws-up-state\")?.textContent === 'Preparing'",
                what: "p1 Preparing");
            await Task.Delay(3_000); // past the 2 s heartbeat timeout

            await page.Mouse.MoveAsync(0, 0);
            await page.ReloadAsync(new() { WaitUntil = WaitUntilState.Load });
            await ReadyAsync(page);
            await Tray(page).WaitForAsync(new() { Timeout = WaitMs });
            Assert.Equal(["p1.jpg", "p2.jpg"], await Rows(page).EvaluateAllAsync<string[]>("rs => rs.map(r => r.getAttribute('aria-label'))"));
            foreach (var name in new[] { "p1.jpg", "p2.jpg" })
            {
                Assert.Equal("Interrupted", await RowWordAsync(Row(page, name)));
                Assert.Equal(["Retry", "Remove"], (await Row(page, name).Locator("button").AllInnerTextsAsync()).Select(t => t.Trim()).ToArray());
            }
            Assert.Equal("0 photos added · 2 failed", await HeadingAsync(page));
            Assert.True(await TrayButton(page, "done").IsEnabledAsync());
        }
        finally
        {
            dir.Delete(true);
        }
    }

    // ── SC-013 shape: one of ten fails; the other nine each join within 2 s of U7 ───────────

    [Fact]
    public async Task One_of_ten_forced_to_fail_the_other_nine_join_within_2_s()
    {
        var dir = Directory.CreateTempSubdirectory("gallery-ws-upload-");
        try
        {
            var garbage = await WriteGarbageJpeg(dir.FullName);
            await using var site = await fx.NewSiteAsync();
            var page = await OpenAsync(site, "/admin", initScript: "performance.setResourceTimingBufferSize(5000);");
            await AddPhotosAsync(page);
            await PickAsync(page, [.. Copies(dir, 9), garbage]);
            await SettledAsync(page, 9, 1, 240_000);

            const string Timings = """
                () => {
                  const out = {};
                  for (const e of performance.getEntriesByType('resource')) {
                    const m = /\/admin\/upload\/items\/([^/?]+)\/(complete|join)$/.exec(new URL(e.name).pathname);
                    if (!m) continue;
                    out[m[1]] ??= {};
                    out[m[1]][m[2]] ??= e.responseEnd;
                  }
                  return Object.entries(out).map(([id, t]) => [id, t.complete ?? -1, t.join ?? -1]);
                }
                """;
            var rows = await page.EvaluateAsync<JsonElement>(Timings);
            var joined = (await Images(site).Find(FilterDefinition<ImageDoc>.Empty).ToListAsync()).Select(i => i.Id).ToHashSet();
            Assert.Equal(9, joined.Count);
            var pairs = rows.EnumerateArray()
                .Select(r => (Id: r[0].GetString()!, Complete: r[1].GetDouble(), Join: r[2].GetDouble()))
                .ToList();
            Assert.Equal(joined.Order(), pairs.Select(p => p.Id).Order());
            Assert.All(pairs, p =>
            {
                Assert.True(p.Complete > 0 && p.Join > p.Complete, $"{p.Id}: U7 ends {p.Complete:0} ms, U12 ends {p.Join:0} ms");
                Assert.True(p.Join - p.Complete <= 2000, $"{p.Id}: U7 → U12 took {p.Join - p.Complete:0} ms");
            });
            Assert.StartsWith("Could not prepare sizes", (await Row(page, "garbage.jpg").Locator(".ws-up-reason").InnerTextAsync()).Trim());
        }
        finally
        {
            dir.Delete(true);
        }
    }

    // ── US2-6: a photo joined into a public gallery is visible to visitors ──────────────────

    [Fact]
    public async Task Visitors_see_a_photo_joined_into_a_public_gallery()
    {
        await using var site = await fx.NewSiteAsync();
        var seeded = await SeedImageAsync(site);
        var gid = await GalleryAsync(site, "Coast", [seeded], publish: true);
        var slug = (await Galleries(site).Find(g => g.Id == gid).SingleAsync()).Slug;
        var page = await OpenAsync(site, $"/admin/g/{gid}");

        var visitor = await fx.Browser.NewContextAsync();
        try
        {
            await visitor.RouteAsync(fx.Storage.ImageBaseUrl + "/**", r => r.FulfillAsync(new() { Status = 404 }));
            var view = await visitor.NewPageAsync();
            var before = await view.GotoAsync($"{site.BaseUrl}/g/{gid}/{slug}");
            Assert.Equal(200, before!.Status);
            Assert.Contains(seeded, await view.ContentAsync());

            await AddPhotosAsync(page);
            await AssertNoticeShownAsync(page, "before picking");
            await PickAsync(page, Fixture("gps-camera.jpg"));
            await SettledAsync(page, 1);
            var joined = Assert.Single(await NewImagesAsync(site, [seeded])).Id;

            var after = await view.GotoAsync($"{site.BaseUrl}/g/{gid}/{slug}");
            Assert.Equal(200, after!.Status);
            var html = await view.ContentAsync();
            Assert.Contains($"/g/{gid}/{slug}/{joined}", html);
            Assert.DoesNotContain("/admin-assets", html);
        }
        finally
        {
            await visitor.DisposeAsync();
        }
    }

    // ── US2-9: Done clears the New marks ────────────────────────────────────────────────────

    [Fact]
    public async Task Done_clears_the_New_marks()
    {
        await using var site = await fx.NewSiteAsync();
        var seeded = await SeedImageAsync(site);
        var page = await OpenAsync(site, "/admin");
        await AddPhotosAsync(page);
        await PickAsync(page, Fixture("orientation-6.jpg"), Fixture("gps-camera.jpg"));
        await SettledAsync(page, 2);
        await WaitForAsync(page, "() => document.querySelectorAll('.ws-main .ws-cell').length === 3", what: "three cells");
        var joined = (await NewImagesAsync(site, [seeded])).Select(i => i.Id).Order().ToArray();
        Assert.Equal(joined, (await NewMarkedAsync(page)).Order());

        await TrayButton(page, "done").ClickAsync();
        await WaitForAsync(page, "() => { const t = document.querySelector('upload-tray'); return t.hidden && t.children.length === 0; }", what: "the tray closed");
        Assert.Empty(await NewMarkedAsync(page));
        Assert.Equal(0, await page.Locator(".ws-main .ws-cell .ws-mark").CountAsync());
        Assert.Equal(3, (await CellIdsAsync(page)).Length);

        // The set is closed on the server: a reload opens no tray.
        await page.Mouse.MoveAsync(0, 0);
        await page.ReloadAsync(new() { WaitUntil = WaitUntilState.Load });
        await ReadyAsync(page);
        Assert.Equal(0, await Tray(page).CountAsync());
        Assert.Empty(await NewMarkedAsync(page));
    }
}
