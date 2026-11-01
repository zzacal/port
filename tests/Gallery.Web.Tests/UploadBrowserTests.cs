using System.Text.RegularExpressions;
using Amazon.S3.Model;
using Gallery.Web.Data;
using Gallery.Web.Storage;
using Gallery.Web.Tests.Fixtures;
using Gallery.Web.Upload;
using Microsoft.Playwright;
using MongoDB.Driver;

namespace Gallery.Web.Tests;

/// <summary>
/// 008 T045: the upload page in a real browser: upload.js, the module worker and the vendored
/// encoders, against the S3 emulator (research R-14). Each test gets its own app and database
/// (<see cref="UploadBrowserFixture.NewSiteAsync"/>) and its own browser context. This fixture's
/// Chrome runs with web security off (T041, decisions row 63), so
/// <see cref="Storage_PUTs_carry_only_content_type"/> proves FR-044 by the recorded request headers.
/// </summary>
[Collection("upload-browser")]
[Trait("Category", "Browser")]
public sealed class UploadBrowserTests(UploadBrowserFixture fx)
{
    private const float LongWaitMs = 90_000;
    private const float WaitMs = 30_000;

    private static string Fixture(string name)
    {
        var upload = Path.Combine(Render.RepoRoot, "tests", "fixtures", "upload", name);
        return File.Exists(upload) ? upload : Path.Combine(Render.RepoRoot, "tests", "fixtures", name);
    }

    private static ILocator Tiles(IPage page) => page.Locator("upload-batch .cs > .batch-item");

    private static ILocator Tile(IPage page, string fileName) =>
        Tiles(page).Filter(new() { Has = page.Locator($".meta .fname[title='{fileName}']") });

    private static Task Pick(IPage page, params string[] paths) =>
        page.Locator("upload-batch .dropzone .dz-input").SetInputFilesAsync(paths);

    private static Task WaitForState(IPage page, string fileName, string state, float timeout = LongWaitMs) =>
        Tile(page, fileName).And(page.Locator($".state-{state}")).WaitForAsync(new() { Timeout = timeout });

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

    // ── Fixture guard (T044): the browser carries the owner cookie and the script runs ──────

    [Fact]
    public async Task Fixture_guard_upload_batch_is_shown_after_load()
    {
        await using var site = await fx.NewSiteAsync();
        var page = await site.OpenUploadAsync();
        await page.WaitForFunctionAsync("() => !document.querySelector('upload-batch').hidden",
            null, new() { Timeout = WaitMs });
        Assert.False(await page.EvaluateAsync<bool>("document.querySelector('upload-batch').hidden"));
        Assert.True(await page.Locator("upload-batch .dropzone").IsVisibleAsync());
    }

    // ── US1 AS-2/4/6, FR-014: two photos through the worker, then confirm ───────────────────

    [Fact]
    public async Task Worker_path_two_photos_reach_ready_and_confirm()
    {
        await using var site = await fx.NewSiteAsync();
        var page = await site.OpenUploadAsync();

        await Pick(page, Fixture("orientation-6.jpg"), Fixture("gps-camera.jpg"));
        await WaitForState(page, "orientation-6.jpg", "succeeded");
        await WaitForState(page, "gps-camera.jpg", "succeeded");
        Assert.Equal(2, await Tiles(page).And(page.Locator(".state-succeeded")).CountAsync());

        await page.GetByRole(AriaRole.Button, new() { Name = "Add 2 photos to library" }).ClickAsync();
        await page.WaitForURLAsync(new Regex(@"/admin/upload\?confirmed="), new() { Timeout = WaitMs });
        Assert.Contains("?confirmed=", page.Url);
        Assert.Contains("2 photos added", await page.Locator(".upload-result").InnerTextAsync());

        var images = await site.Db.GetCollection<ImageDoc>("images").Find(FilterDefinition<ImageDoc>.Empty).ToListAsync();
        Assert.Equal(2, images.Count);
        var upright = Assert.Single(images, i => i.Title == "orientation-6");
        Assert.Equal(200, upright.Width);
        Assert.Equal(400, upright.Height);

        // Every public file of the orientation-6 photo parses through reader 1 upright.
        using var s3 = new S3ClientFactory(fx.Storage).CreateClient();
        var files = upright.Derivatives.SelectMany(d => new[] { d.Avif, d.Webp }).ToList();
        Assert.Equal(2, files.Count);
        foreach (var key in files)
        {
            using var obj = await s3.GetObjectAsync(fx.Storage.PublicBucket, key);
            using var ms = new MemoryStream();
            await obj.ResponseStream.CopyToAsync(ms);
            var parsed = ContainerParser.Parse(ms.ToArray());
            Assert.True(parsed.Ok, $"{key}: {parsed.Reason}");
            Assert.True(parsed.Height > parsed.Width, $"{key} is {parsed.Width}x{parsed.Height}, not upright");
        }

        Assert.Equal(2, site.LogLines.Count(l => l.StartsWith("op.timing browser.total ", StringComparison.Ordinal)));
    }

    // ── FR-044: a storage PUT carries content-type and nothing the page added beyond it ─────

    // Headers the browser adds to any fetch (T045's list). `host` and `connection` are HTTP/1.1
    // transport headers Chrome's network stack writes on every request; `host` is one of the two
    // signed headers (content-type;host, T015), so it must be present.
    private static readonly HashSet<string> BrowserHeaders =
        ["content-type", "origin", "referer", "user-agent", "content-length", "host", "connection"];

    [Fact]
    public async Task Storage_PUTs_carry_only_content_type()
    {
        await using var site = await fx.NewSiteAsync();
        var ctx = await site.NewContextAsync();
        var page = await ctx.NewPageAsync();
        var storage = RecordStorage(page);
        await page.GotoAsync(site.BaseUrl + "/admin/upload", new() { WaitUntil = WaitUntilState.NetworkIdle });

        await Pick(page, Fixture("gps-camera.jpg"));
        // Settle on either end state first, so a PUT the emulator refuses (an extra signed-header
        // mismatch) fails on the header assertion below, not on a timeout.
        await Tile(page, "gps-camera.jpg").And(page.Locator(".state-succeeded, .state-failed"))
            .WaitForAsync(new() { Timeout = LongWaitMs });

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
        await WaitForState(page, "gps-camera.jpg", "succeeded", 5_000);
        // master.jpg, 320.avif, 320.webp (gps-camera.jpg is 400 px wide → ladder [320]).
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
        var ctx = await site.NewContextAsync();
        var page = await ctx.NewPageAsync();
        var storage = RecordStorage(page);
        var registers = 0;
        page.Request += (_, r) => { if (r.Url.EndsWith("/admin/upload/items", StringComparison.Ordinal)) registers++; };
        await page.GotoAsync(site.BaseUrl + "/admin/upload", new() { WaitUntil = WaitUntilState.NetworkIdle });

        await Pick(page, Fixture("not-a-jpeg.jpg"), Fixture("fixture-6000x4000.jpg"));
        var rows = page.Locator("upload-batch .dropzone .dz-refusals li");
        await rows.Nth(1).WaitForAsync(new() { Timeout = WaitMs });
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        Assert.Equal(
            ["not-a-jpeg.jpg — not a JPEG", "fixture-6000x4000.jpg — 0.4 MB, over the 0.2 MB limit"],
            (await rows.AllInnerTextsAsync()).Select(t => t.Trim()).ToArray());
        Assert.Equal(0, await Tiles(page).CountAsync());
        Assert.Equal(0, registers);
        lock (storage) Assert.Empty(storage);
    }

    // ── FR-003: the whole document is the drop target ───────────────────────────────────────

    [Fact]
    public async Task Drop_on_document_adds_tiles()
    {
        await using var site = await fx.NewSiteAsync();
        var page = await site.OpenUploadAsync();
        var b64 = Convert.ToBase64String(await File.ReadAllBytesAsync(Fixture("orientation-6.jpg")));

        await page.EvaluateAsync(@"(b64) => {
            const bytes = Uint8Array.from(atob(b64), c => c.charCodeAt(0));
            const dt = new DataTransfer();
            dt.items.add(new File([bytes], 'orientation-6.jpg', { type: 'image/jpeg' }));
            const target = document.body;
            target.dispatchEvent(new DragEvent('dragover', { bubbles: true, cancelable: true, dataTransfer: dt }));
            target.dispatchEvent(new DragEvent('drop', { bubbles: true, cancelable: true, dataTransfer: dt }));
        }", b64);

        await Tile(page, "orientation-6.jpg").WaitForAsync(new() { Timeout = WaitMs });
        Assert.Equal(1, await Tiles(page).CountAsync());
        await WaitForState(page, "orientation-6.jpg", "succeeded");
    }

    // ── FR-017, US4 AS-1: one broken photo fails alone ──────────────────────────────────────

    // T045 says "FF D8 FF + random". Bytes that random never reach the worker: U4's server-side
    // header check refuses them first ("unreadable JPEG header"), which is the master check, not a
    // worker failure. So the garbage file keeps a REAL JPEG header (gps-camera.jpg up to and
    // including its SOS segment) and replaces the entropy-coded data with random bytes: the server
    // accepts the header, and the worker's decode fails.
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
    public async Task Worker_failure_fails_only_that_photo()
    {
        var dir = Directory.CreateTempSubdirectory("gallery-upload-browser-");
        try
        {
            var garbage = await WriteGarbageJpeg(dir.FullName);

            await using var site = await fx.NewSiteAsync();
            var page = await site.OpenUploadAsync();
            await Pick(page, garbage, Fixture("gps-camera.jpg"));

            await WaitForState(page, "garbage.jpg", "failed");
            await WaitForState(page, "gps-camera.jpg", "succeeded");

            var failed = Tile(page, "garbage.jpg");
            var reason = (await failed.Locator(".status .danger").InnerTextAsync()).Trim();
            Assert.StartsWith("Could not prepare sizes", reason);
            // The page reported it through U6: the server holds the same failure.
            var server = await page.EvaluateAsync<string>(
                "async () => { const s = await (await fetch('/admin/upload/state')).json(); " +
                "const i = s.batch.items.find(x => x.fileName === 'garbage.jpg'); return i.state + '|' + i.reason; }");
            Assert.Equal($"failed|{reason}", server);
            Assert.True(await failed.GetByRole(AriaRole.Button, new() { Name = "Restart garbage.jpg" }).IsVisibleAsync());
            Assert.True(await failed.GetByRole(AriaRole.Button, new() { Name = "Delete garbage.jpg" }).IsVisibleAsync());

            var footer = page.Locator("upload-batch > .batch-footer");
            Assert.Equal("1 ready · 1 failed", (await footer.Locator(".bf-summary").InnerTextAsync()).Trim());
            Assert.Equal("Restart or delete the 1 failed photos to continue",
                (await footer.Locator(".bf-reason").InnerTextAsync()).Trim());
            Assert.True(await footer.GetByRole(AriaRole.Button, new() { Name = "Add 2 photos to library" }).IsDisabledAsync());
        }
        finally
        {
            dir.Delete(true);
        }
    }

    // ── FR-011, E-04: Restart with no stored original asks for the file again ───────────────

    private async Task<IPage> FailMasterThenReloadAndRestart(UploadSite site)
    {
        var page = await site.OpenUploadAsync();
        await page.RouteAsync(fx.S3Origin + "/**", r => r.AbortAsync());
        await Pick(page, Fixture("gps-camera.jpg"));
        await WaitForState(page, "gps-camera.jpg", "failed");
        Assert.Contains("upload failed", await Tile(page, "gps-camera.jpg").Locator(".status .danger").InnerTextAsync());

        await page.UnrouteAllAsync();
        await page.ReloadAsync(new() { WaitUntil = WaitUntilState.NetworkIdle });
        await WaitForState(page, "gps-camera.jpg", "failed", WaitMs);
        await Tile(page, "gps-camera.jpg").GetByRole(AriaRole.Button, new() { Name = "Restart gps-camera.jpg" }).ClickAsync();
        await Tile(page, "gps-camera.jpg").GetByText("Pick gps-camera.jpg again").WaitForAsync(new() { Timeout = WaitMs });
        return page;
    }

    [Fact]
    public async Task Restart_without_master_asks_for_the_file()
    {
        // Same name: the item continues.
        await using (var site = await fx.NewSiteAsync())
        {
            var page = await FailMasterThenReloadAndRestart(site);
            await Tile(page, "gps-camera.jpg").Locator("input[type=file]").SetInputFilesAsync(Fixture("gps-camera.jpg"));
            await WaitForState(page, "gps-camera.jpg", "succeeded");
            Assert.Equal(1, await Tiles(page).CountAsync());
        }

        // A different name: registered as a new item, the restarted one still waits for its file.
        await using (var site = await fx.NewSiteAsync())
        {
            var page = await FailMasterThenReloadAndRestart(site);
            await Tile(page, "gps-camera.jpg").Locator("input[type=file]").SetInputFilesAsync(Fixture("orientation-6.jpg"));
            await WaitForState(page, "orientation-6.jpg", "succeeded");
            Assert.Equal(2, await Tiles(page).CountAsync());
            Assert.True(await Tile(page, "gps-camera.jpg").GetByText("Pick gps-camera.jpg again").IsVisibleAsync());
        }
    }

    // ── FR-006: no script → the notice, and no control that cannot work ─────────────────────

    [Fact]
    public async Task No_js_shows_notice_and_no_control()
    {
        await using var site = await fx.NewSiteAsync();
        var page = await site.OpenUploadAsync(javaScript: false);
        var notice = page.Locator(".upload-nojs");
        Assert.True(await notice.IsVisibleAsync());
        Assert.Equal("Uploading needs JavaScript. Turn it on to add photos.", (await notice.InnerTextAsync()).Trim());
        Assert.False(await page.Locator("upload-batch").IsVisibleAsync());
        Assert.False(await page.Locator("upload-batch .dropzone").IsVisibleAsync());
    }

    // ── US1 AS-3: encoding runs off the main thread ─────────────────────────────────────────

    [Fact]
    public async Task Main_thread_stays_responsive()
    {
        await using var site = await fx.NewSiteAsync();
        var page = await site.OpenUploadAsync(beforeNavigate: p => p.AddInitScriptAsync(@"
            window.__longTasks = [];
            new PerformanceObserver(list => {
                for (const e of list.getEntries()) window.__longTasks.push(e.duration);
            }).observe({ type: 'longtask', buffered: true });"));

        await page.EvaluateAsync("() => { window.__longTasks.length = 0; }");
        await Pick(page, Fixture("fixture-6000x4000.jpg"));
        await WaitForState(page, "fixture-6000x4000.jpg", "succeeded", 120_000);

        var tasks = await page.EvaluateAsync<double[]>("window.__longTasks");
        Assert.True(tasks.All(ms => ms <= 200), $"long tasks: [{string.Join(", ", tasks.Select(t => $"{t:0} ms"))}]");
        Assert.Equal(1, site.LogLines.Count(l => l.StartsWith("op.timing browser.total ", StringComparison.Ordinal)));
    }
}
