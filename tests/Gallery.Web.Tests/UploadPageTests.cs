using System.Net;
using Gallery.Core.Ids;
using Gallery.Web.Data;
using Gallery.Web.Tests.Fixtures;
using Gallery.Web.Upload;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;

namespace Gallery.Web.Tests;

/// <summary>
/// 008 T034: the upload page U1 (contracts/http.md U1, contracts/components.md C3) and the
/// admin-asset cache rules (contracts/http.md "Static assets"). FR-001, FR-002, FR-006, FR-021,
/// SC-010.
/// </summary>
[Collection("s3")]
public class UploadPageTests : IClassFixture<MongoFixture>, IAsyncLifetime
{
    // The page's U+2063 sentinel as Razor encodes it in markup.
    private const string Sentinel = "&#x2063;";

    private readonly S3ProxyFixture _s3;
    private readonly MongoFixture _mongo;
    private readonly FakeGoogle _google = new();

    public UploadPageTests(S3ProxyFixture s3, MongoFixture mongo)
    {
        _s3 = s3;
        _mongo = mongo;
    }

    public Task InitializeAsync() => _google.InitializeAsync();
    public Task DisposeAsync() => _google.DisposeAsync();

    // The test host runs from the test output dir, which has no wwwroot. Point the web root at the
    // repo's (Render.WebRoot), as a deployed app's content root would, so the static admin assets
    // are served and AdminAssets hashes the real files.
    private Task<UploadHost> NewHostAsync(Dictionary<string, string?>? overrides = null) =>
        UploadHost.CreateAsync(_mongo, _google, _s3, overrides, b =>
        {
            b.Environment.WebRootPath = Render.WebRoot;
            b.Environment.WebRootFileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(Render.WebRoot);
        });

    private static IMongoCollection<BatchDoc> Batches(UploadHost host) =>
        host.Services.GetRequiredService<IMongoClient>().GetDatabase(host.DatabaseName)
            .GetCollection<BatchDoc>(BatchStore.CollectionName);

    private static async Task<string> PageAsync(UploadHost host, string path = "/admin/upload")
    {
        var resp = await host.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        return await resp.Content.ReadAsStringAsync();
    }

    private static List<string> Lines(UploadHost host, string prefix)
    {
        lock (host.LogLines)
            return host.LogLines.Where(l => l.StartsWith(prefix, StringComparison.Ordinal)).ToList();
    }

    private static BatchItemDoc Item(int n, string state, DateTime heartbeat) => new()
    {
        ItemId = OpaqueId.NewId(),
        AttemptId = OpaqueId.NewId(),
        FileName = $"IMG_{n:0000}.jpg",
        FileSize = 2_516_582,
        State = state,
        Percent = 0,
        HeartbeatAt = heartbeat,
        Stored = [],
    };

    // ── the page ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Owner_gets_the_upload_page()
    {
        await using var host = await NewHostAsync();
        var resp = await host.GetAsync("/admin/upload");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal("text/html; charset=utf-8", resp.Content.Headers.ContentType?.ToString());
        Assert.Equal("no-store", resp.Headers.CacheControl?.ToString());
        Assert.Equal("noindex", string.Join(",", resp.Headers.GetValues("X-Robots-Tag")));

        var html = await resp.Content.ReadAsStringAsync();
        Assert.Contains("JPEG only · up to 20 MB each · up to 100 photos per batch", html);
        Assert.Contains("<noscript><p class=\"upload-nojs\">Uploading needs JavaScript. Turn it on to add photos.</p></noscript>", html);
        Assert.Equal(1, Guards.Count(html, "<upload-batch hidden "));
        Assert.Equal(1, Guards.Count(html, "<upload-batch"));
        Assert.Equal(1, Guards.Count(html, "<script type=\"module\" src=\"/admin-assets/upload.js?v="));
        Assert.Equal(1, Guards.Count(html, "<script"));
        Assert.Matches("<script type=\"module\" src=\"/admin-assets/upload.js\\?v=[0-9a-f]{8}\"></script>", html);
        Assert.Matches("data-worker=\"/admin-assets/upload-worker.js\\?v=[0-9a-f]{8}\"", html);

        foreach (var s in new[] { "queued", "processing", "uploading", "succeeded", "failed", "interrupted" })
            Assert.Equal(1, Guards.Count(html, $"<template data-tile=\"{s}\">"));
        Assert.Equal(6, Guards.Count(html, "<template data-tile="));
        foreach (var s in new[] { "inprogress", "ready", "blocked", "confirmdiscard" })
            Assert.Equal(1, Guards.Count(html, $"<template data-footer=\"{s}\">"));
        Assert.Equal(4, Guards.Count(html, "<template data-footer="));
        Assert.Equal(1, Guards.Count(html, "<template data-refusals>"));

        // The upload-batch data attributes, from the same UploadOptions the server enforces with.
        Assert.Contains("data-max-bytes=\"20971520\"", html);
        Assert.Contains("data-max-photos=\"100\"", html);
        Assert.Contains("data-state-url=\"/admin/upload/state\"", html);
        Assert.Contains("data-batch=\"\"", html);
        Assert.Matches("data-csrf=\"[^\"]+\"", html);
        Assert.Equal(1, Guards.Count(html, "<form id=\"upload-actions\" method=\"post\" action=\"/admin/upload\" hidden></form>"));

        // Owner bar marks Upload as the current page; no batch → no footer outside the templates.
        Assert.Contains("<a href=\"/admin/upload\" aria-current=\"page\"", html);
        Assert.Equal(4, Guards.Count(html, "<footer class=\"batch-footer\""));
        Assert.DoesNotContain("upload-result", html);
        Assert.DoesNotContain("style=", html);
    }

    [Fact]
    public async Task Page_holds_no_storage_url_or_key()
    {
        await using var host = await NewHostAsync();
        // An open batch with an item, so tile markup with real ids is rendered too.
        var now = host.Clock.GetUtcNow().UtcDateTime;
        await Batches(host).InsertOneAsync(new BatchDoc
        {
            Id = OpaqueId.NewId(), State = BatchStates.Open, CreatedAt = now, UpdatedAt = now,
            Items = [Item(1, ItemStates.Uploading, now)],
        });

        var html = await PageAsync(host);
        Assert.DoesNotContain("X-Amz-", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("/priv/", html);
        Assert.DoesNotContain("staging/", html);
        Assert.DoesNotContain(_s3.StorageOptions.ServiceUrl!, html);
    }

    [Fact]
    public async Task Limits_come_from_configuration()
    {
        await using var host = await NewHostAsync(new() { ["Upload:MaxFileBytes"] = "10485760" });
        var html = await PageAsync(host);

        Assert.Contains("JPEG only · up to 10 MB each · up to 100 photos per batch", html);
        Assert.Contains("data-max-bytes=\"10485760\"", html);
        Assert.DoesNotContain("up to 20 MB", html);
    }

    [Fact]
    public async Task Hundred_item_batch_renders_every_tile_in_one_batch_read()
    {
        await using var host = await NewHostAsync();
        var now = host.Clock.GetUtcNow().UtcDateTime;
        var items = Enumerable.Range(1, 100).Select(n => Item(n, ItemStates.Queued, now)).ToList();
        var batchId = OpaqueId.NewId();
        await Batches(host).InsertOneAsync(new BatchDoc
        {
            Id = batchId, State = BatchStates.Open, CreatedAt = now, UpdatedAt = now, Items = items,
        });

        int before;
        lock (host.LogLines) before = host.LogLines.Count;
        var resp = await host.GetAsync("/admin/upload");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var html = await resp.Content.ReadAsStringAsync();

        // 100 real tiles plus the 6 sentinel tile templates.
        Assert.Equal(106, Guards.Count(html, "data-item=\""));
        Assert.Equal(6, Guards.Count(html, $"data-item=\"{Sentinel}id{Sentinel}\""));
        foreach (var item in items)
            Assert.Equal(1, Guards.Count(html, $"data-item=\"{item.ItemId}\""));
        Assert.Contains($"data-batch=\"{batchId}\"", html);

        // In progress footer (0 of 100 ready), rendered once outside the templates.
        Assert.Equal(5, Guards.Count(html, "<footer class=\"batch-footer\""));
        Assert.Matches("<span b-[a-z0-9]+>0 of 100 ready</span>", html);

        // One datastore read: Server-Timing and the log agree.
        var db = resp.Headers.GetValues("Server-Timing").Count(h => h.Contains("db;dur="));
        Assert.Equal(1, db);
        List<string> after;
        lock (host.LogLines) after = host.LogLines.Skip(before).ToList();
        Assert.Single(after, l => l.StartsWith("op.timing db.batch.read ", StringComparison.Ordinal));
        var http = Assert.Single(after, l => l.StartsWith("http.request GET /admin/upload ", StringComparison.Ordinal));
        Assert.Matches("^http\\.request GET /admin/upload 200 [0-9.]+ 1$", http);
    }

    [Fact]
    public async Task Tiles_show_the_effective_state_and_the_footer_mapping()
    {
        await using var host = await NewHostAsync();
        var now = host.Clock.GetUtcNow().UtcDateTime;
        var stale = now - TimeSpan.FromMinutes(3);
        var ok = Item(1, ItemStates.Succeeded, now);
        var stopped = Item(2, ItemStates.Uploading, stale); // effective interrupted
        var failed = Item(3, ItemStates.Failed, now);
        failed.Reason = "640.webp: EXIF chunk";
        await Batches(host).InsertOneAsync(new BatchDoc
        {
            Id = OpaqueId.NewId(), State = BatchStates.Open, CreatedAt = now, UpdatedAt = now,
            Items = [ok, stopped, failed],
        });

        var html = await PageAsync(host);
        Assert.Matches($"<div class=\"batch-item state-succeeded\" data-item=\"{ok.ItemId}\"", html);
        Assert.Matches($"<div class=\"batch-item state-interrupted\" data-item=\"{stopped.ItemId}\"", html);
        Assert.Matches($"<div class=\"batch-item state-failed\" data-item=\"{failed.ItemId}\"", html);
        Assert.Contains("640.webp: EXIF chunk", html);

        // Blocked: Failed = failed + interrupted (decisions row 26).
        Assert.Contains("Restart or delete the 2 failed photos to continue", System.Net.WebUtility.HtmlDecode(html));
        Assert.Matches("<span b-[a-z0-9]+>1 ready · <span class=\"bf-failed\" b-[a-z0-9]+>2 failed</span>", html);
    }

    [Fact]
    public async Task Ready_batch_renders_the_ready_footer()
    {
        await using var host = await NewHostAsync();
        var now = host.Clock.GetUtcNow().UtcDateTime;
        await Batches(host).InsertOneAsync(new BatchDoc
        {
            Id = OpaqueId.NewId(), State = BatchStates.Open, CreatedAt = now, UpdatedAt = now,
            Items = [Item(1, ItemStates.Succeeded, now), Item(2, ItemStates.Succeeded, now)],
        });

        var html = await PageAsync(host);
        // The live footer sits before the templates block.
        var outside = html[..html.IndexOf("<div class=\"upload-templates\"", StringComparison.Ordinal)];
        Assert.Matches("<span b-[a-z0-9]+>All 2 photos ready</span>", outside);
        Assert.Equal(1, Guards.Count(outside, "<footer class=\"batch-footer\""));
    }

    [Fact]
    public async Task Confirmed_query_shows_the_result_for_a_confirmed_batch_only()
    {
        await using var host = await NewHostAsync();
        var now = host.Clock.GetUtcNow().UtcDateTime;
        var confirmedId = OpaqueId.NewId();
        await Batches(host).InsertOneAsync(new BatchDoc
        {
            Id = confirmedId, State = BatchStates.Confirmed, CreatedAt = now, UpdatedAt = now,
            ConfirmedAt = now, ConfirmedCount = 2,
        });

        var resp = await host.GetAsync($"/admin/upload?confirmed={confirmedId}");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var html = await resp.Content.ReadAsStringAsync();
        Assert.Contains("<p class=\"upload-result\" role=\"status\">2 photos added to your library. <a href=\"/admin/library\">Open the library</a></p>", html);
        Assert.Equal(1, resp.Headers.GetValues("Server-Timing").Count(h => h.Contains("db;dur=")));
        // The drop target is empty: no real tiles and no live footer.
        Assert.Equal(6, Guards.Count(html, "data-item=\""));
        Assert.Contains("data-batch=\"\"", html);

        foreach (var other in new[] { OpaqueId.NewId(), "not-an-id", "" })
        {
            var page = await PageAsync(host, $"/admin/upload?confirmed={other}");
            Assert.DoesNotContain("upload-result", page);
            Assert.DoesNotContain("photos added to your library", page);
        }
    }

    [Fact]
    public async Task Confirmed_query_of_an_open_batch_shows_no_result()
    {
        await using var host = await NewHostAsync();
        var now = host.Clock.GetUtcNow().UtcDateTime;
        var openId = OpaqueId.NewId();
        await Batches(host).InsertOneAsync(new BatchDoc
        {
            Id = openId, State = BatchStates.Open, CreatedAt = now, UpdatedAt = now,
            Items = [Item(1, ItemStates.Succeeded, now)],
        });

        var html = await PageAsync(host, $"/admin/upload?confirmed={openId}");
        Assert.DoesNotContain("upload-result", html);
        Assert.Contains($"data-batch=\"{openId}\"", html);
    }

    [Fact]
    public async Task Head_answers_200_without_a_body()
    {
        await using var host = await NewHostAsync();
        var resp = await host.Host.SendAsync(new HttpRequestMessage(HttpMethod.Head, "/admin/upload"));
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal("no-store", resp.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task Signed_out_get_redirects_to_signin()
    {
        await using var host = await NewHostAsync();
        host.Host.ClearCookies();
        var resp = await host.GetAsync("/admin/upload");
        Assert.Equal(HttpStatusCode.Found, resp.StatusCode);
        var location = resp.Headers.Location!;
        var path = location.IsAbsoluteUri ? location.PathAndQuery : location.OriginalString;
        Assert.StartsWith("/admin/signin", path);
    }

    // ── static admin assets ─────────────────────────────────────────────────

    [Fact]
    public async Task Admin_assets_cache_rules()
    {
        await using var host = await NewHostAsync();

        var versioned = await host.GetAsync("/admin-assets/upload.js?v=x");
        Assert.Equal(HttpStatusCode.OK, versioned.StatusCode);
        Assert.Equal("public, max-age=31536000, immutable", versioned.Headers.CacheControl?.ToString());

        var bare = await host.GetAsync("/admin-assets/upload.js");
        Assert.Equal(HttpStatusCode.OK, bare.StatusCode);
        Assert.Equal("no-cache", bare.Headers.CacheControl?.ToString());

        var worker = await host.GetAsync("/admin-assets/upload-worker.js?v=x");
        Assert.Equal(HttpStatusCode.OK, worker.StatusCode);
        Assert.Equal("public, max-age=31536000, immutable", worker.Headers.CacheControl?.ToString());

        var wasm = await host.GetAsync("/admin-assets/vendor/jsquash-avif-2.1.1/avif_enc.wasm");
        Assert.Equal(HttpStatusCode.OK, wasm.StatusCode);
        Assert.Equal("application/wasm", wasm.Content.Headers.ContentType?.MediaType);
        Assert.Equal("public, max-age=31536000, immutable", wasm.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task Script_version_is_the_content_hash()
    {
        await using var host = await NewHostAsync();
        var html = await PageAsync(host);
        var bytes = await File.ReadAllBytesAsync(Path.Combine(Render.WebRoot, "admin-assets", "upload.js"));
        var hash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes))[..8];
        Assert.Contains($"src=\"/admin-assets/upload.js?v={hash}\"", html);
        Assert.Equal(hash, host.Services.GetRequiredService<AdminAssets>().ScriptVersion);
    }

    [Fact]
    public void Missing_asset_gets_version_dev()
    {
        var env = new MissingEnv();
        Assert.Equal("dev", AdminAssets.VersionOf(env, "admin-assets/nope.js"));
        Assert.Equal("dev", new AdminAssets(env).ScriptVersion);
    }

    private sealed class MissingEnv : Microsoft.AspNetCore.Hosting.IWebHostEnvironment
    {
        public string WebRootPath { get; set; } = "";
        public Microsoft.Extensions.FileProviders.IFileProvider WebRootFileProvider { get; set; } = new Microsoft.Extensions.FileProviders.NullFileProvider();
        public string ApplicationName { get; set; } = "Gallery.Web";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = new Microsoft.Extensions.FileProviders.NullFileProvider();
        public string ContentRootPath { get; set; } = "";
        public string EnvironmentName { get; set; } = "Test";
    }
}
