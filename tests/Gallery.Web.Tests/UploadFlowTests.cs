using System.Net;
using System.Text;
using System.Text.Json;
using Gallery.Core.Ids;
using Gallery.Web.Data;
using Gallery.Web.Observability;
using Gallery.Web.Tests.Fixtures;
using Gallery.Web.Upload;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Bson;
using MongoDB.Driver;
using static Gallery.Web.Tests.Fixtures.UploadFixtures;

namespace Gallery.Web.Tests;

/// <summary>
/// 008 T029 [SAFETY]: the upload server path U2–U5 and U7 end to end over HTTP, with committed
/// fixture bytes PUT to real presigned URLs on the S3 emulator, and no browser.
/// Rejections must write NOTHING to the public bucket (FR-026), and the stored camera and
/// dimensions must come from the server's own read of the master, never the request
/// (FR-022, FR-027; plan item 8).
/// </summary>
[Collection("s3")]
public class UploadFlowTests : IClassFixture<MongoFixture>, IAsyncLifetime
{
    private const string Pub = "pub";
    private const string Priv = "priv";
    private const string Placeholder = "gps-placeholder.webp";
    private const string Color = "#a1b2c3";

    private readonly S3ProxyFixture _s3;
    private readonly MongoFixture _mongo;
    private readonly FakeGoogle _google = new();

    public UploadFlowTests(S3ProxyFixture s3, MongoFixture mongo)
    {
        _s3 = s3;
        _mongo = mongo;
    }

    public Task InitializeAsync() => _google.InitializeAsync();
    public Task DisposeAsync() => _google.DisposeAsync();

    private Task<UploadHost> NewHostAsync() => UploadHost.CreateAsync(_mongo, _google, _s3);

    // ── helpers ──────────────────────────────────────────────────────────────

    private sealed record Registered(string BatchId, string ItemId, string AttemptId, string MasterUrl);

    private sealed record Size(int W, string AvifUrl, string WebpUrl);

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage resp)
    {
        var text = await resp.Content.ReadAsStringAsync();
        return JsonDocument.Parse(text).RootElement.Clone();
    }

    private static async Task<Registered> RegisterAsync(UploadHost host, string name, long size)
    {
        var resp = await host.PostJsonAsync("/admin/upload/items",
            new { files = new[] { new { name, size, type = "image/jpeg" } } });
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var json = await JsonAsync(resp);
        Assert.Equal(0, json.GetProperty("refused").GetArrayLength());
        var a = json.GetProperty("accepted")[0];
        Assert.Equal("image/jpeg", a.GetProperty("master").GetProperty("contentType").GetString());
        return new Registered(
            json.GetProperty("batchId").GetString()!,
            a.GetProperty("itemId").GetString()!,
            a.GetProperty("attemptId").GetString()!,
            a.GetProperty("master").GetProperty("url").GetString()!);
    }

    /// <summary>Register, PUT the master, and run U4. Returns the registration and the size URLs.</summary>
    private static async Task<(Registered R, JsonElement Master, List<Size> Sizes)> StageMasterAsync(
        UploadHost host, byte[] master, string name = "photo.jpg")
    {
        var r = await RegisterAsync(host, name, master.LongLength);
        var put = await UploadHost.PutToPresignedAsync(r.MasterUrl, master, "image/jpeg");
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);

        var resp = await host.PostJsonAsync($"/admin/upload/items/{r.ItemId}/master", new { attemptId = r.AttemptId });
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var json = await JsonAsync(resp);
        var sizes = json.GetProperty("sizes").EnumerateArray().Select(s => new Size(
            s.GetProperty("w").GetInt32(),
            s.GetProperty("avif").GetProperty("url").GetString()!,
            s.GetProperty("webp").GetProperty("url").GetString()!)).ToList();
        return (r, json, sizes);
    }

    private static async Task PutAsync(string url, byte[] bytes, string contentType)
    {
        var put = await UploadHost.PutToPresignedAsync(url, bytes, contentType);
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
    }

    private static Task<HttpResponseMessage> CompleteAsync(UploadHost host, Registered r, object? extra = null,
        string? placeholder = null, object[]? timings = null)
    {
        var body = new Dictionary<string, object?>
        {
            ["attemptId"] = r.AttemptId,
            ["placeholder"] = new { color = Color, preview = placeholder ?? DataUri(Bytes(Placeholder)) },
            ["timings"] = timings ?? [],
        };
        if (extra is not null)
            foreach (var p in extra.GetType().GetProperties())
                body[p.Name] = p.GetValue(extra);
        return host.PostJsonAsync($"/admin/upload/items/{r.ItemId}/complete", body);
    }

    private static IMongoDatabase Db(UploadHost host) =>
        host.Services.GetRequiredService<IMongoClient>().GetDatabase(host.DatabaseName);

    private static async Task<BsonDocument> BatchDocAsync(UploadHost host, string batchId) =>
        await Db(host).GetCollection<BsonDocument>(BatchStore.CollectionName)
            .Find(new BsonDocument("_id", batchId)).SingleAsync();

    private static async Task<BsonDocument> ItemDocAsync(UploadHost host, string batchId, string itemId) =>
        (await BatchDocAsync(host, batchId))["items"].AsBsonArray
            .Select(i => i.AsBsonDocument).Single(i => i["itemId"] == itemId);

    private static List<string> Lines(UploadHost host, string prefix)
    {
        lock (host.LogLines)
            return host.LogLines.Where(l => l.StartsWith(prefix, StringComparison.Ordinal)).ToList();
    }

    private static async Task AssertErrorAsync(HttpResponseMessage resp, HttpStatusCode status, string error)
    {
        Assert.Equal(status, resp.StatusCode);
        var json = await JsonAsync(resp);
        Assert.Equal(error, json.GetProperty("error").GetString());
        Assert.False(string.IsNullOrWhiteSpace(json.GetProperty("message").GetString()));
    }

    // ── happy path ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Happy_path_registers_checks_validates_and_succeeds()
    {
        await using var host = await NewHostAsync();
        var pubBefore = await host.ListAsync(Pub, "");

        var (r, master, sizes) = await StageMasterAsync(host, Bytes("gps-camera.jpg"));
        Assert.Equal(400, master.GetProperty("width").GetInt32());
        Assert.Equal(200, master.GetProperty("height").GetInt32());
        var only = Assert.Single(sizes);
        Assert.Equal(320, only.W);

        await PutAsync(only.AvifUrl, Bytes("gps-320.avif"), "image/avif");
        await PutAsync(only.WebpUrl, Bytes("gps-320.webp"), "image/webp");

        var timings = new object[]
        {
            new { op = "browser.total", ms = 812.4 },
            new { op = "browser.encode.avif.320", ms = 5 },
            new { op = "evil", ms = 1 },
            new { op = "browser.total", ms = -1 },
        };
        var resp = await CompleteAsync(host, r, timings: timings);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal("succeeded", (await JsonAsync(resp)).GetProperty("state").GetString());

        // Stored record: dimensions from the master, camera from the master, content-addressed keys.
        var item = await ItemDocAsync(host, r.BatchId, r.ItemId);
        Assert.Equal("succeeded", item["state"].AsString);
        var v = item["validated"].AsBsonDocument;
        Assert.Equal(400, v["width"].AsInt32);
        Assert.Equal(200, v["height"].AsInt32);
        Assert.Equal("TESTCAM", v["camera"]["make"].AsString);
        Assert.Equal("T1", v["camera"]["model"].AsString);
        Assert.Equal(Color, v["placeholder"]["color"].AsString);
        Assert.Equal(DataUri(Bytes(Placeholder)), v["placeholder"]["preview"].AsString);
        var size = Assert.Single(v["sizes"].AsBsonArray).AsBsonDocument;
        Assert.Equal(320, size["w"].AsInt32);
        Assert.Matches("^[0-9a-f]{16}\\.avif$", size["avifKey"].AsString);
        Assert.Matches("^[0-9a-f]{16}\\.webp$", size["webpKey"].AsString);
        Assert.Equal(Bytes("gps-320.avif").LongLength, size["bytesAvif"].ToInt64());
        Assert.Equal(Bytes("gps-320.webp").LongLength, size["bytesWebp"].ToInt64());
        Assert.False(string.IsNullOrEmpty(size["avifEtag"].AsString));
        Assert.False(string.IsNullOrEmpty(size["webpEtag"].AsString));
        Assert.Equal(item["master"]["etag"].AsString, v["masterEtag"].AsString);

        // No location data anywhere in the batch document.
        var batchJson = (await BatchDocAsync(host, r.BatchId)).ToJson();
        Assert.DoesNotContain("gps", batchJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("latitude", batchJson, StringComparison.OrdinalIgnoreCase);

        // Nothing reaches the public bucket before confirm.
        Assert.Equal(pubBefore, await host.ListAsync(Pub, ""));

        // U2 reports the effective state.
        var (stateResp, state) = await host.GetJsonAsync("/admin/upload/state");
        Assert.Equal(HttpStatusCode.OK, stateResp.StatusCode);
        var b = state!.RootElement.GetProperty("batch");
        Assert.Equal(r.BatchId, b.GetProperty("id").GetString());
        Assert.False(b.GetProperty("confirming").GetBoolean());
        Assert.Equal(20_971_520, b.GetProperty("limits").GetProperty("maxFileBytes").GetInt64());
        Assert.Equal(100, b.GetProperty("limits").GetProperty("maxPhotos").GetInt32());
        var si = Assert.Single(b.GetProperty("items").EnumerateArray());
        Assert.Equal("succeeded", si.GetProperty("state").GetString());
        Assert.True(si.GetProperty("masterStored").GetBoolean());
        Assert.Equal("photo.jpg", si.GetProperty("fileName").GetString());

        // ── timing and log lines (FR-037–FR-040) ──
        Assert.Single(Lines(host, "http.request POST /admin/upload/items/{itemId}/complete 200 "));
        Assert.Single(Lines(host, "op.timing upload.validate "), l => l.EndsWith(" ok"));
        Assert.Equal(2, Lines(host, "op.timing upload.master-check ").Count(l => l.EndsWith(" ok"))); // U4 + U7
        foreach (var op in new[] { "s3.presign", "s3.head", "s3.get", "s3.get-range", "db.batch.update" })
            Assert.Contains(Lines(host, $"op.timing {op} "), l => l.EndsWith(" ok"));

        var browser = Lines(host, "op.timing browser.");
        Assert.Equal(2, browser.Count);
        Assert.Contains("op.timing browser.total 812.4 ok", browser);
        Assert.Contains("op.timing browser.encode.avif.320 5 ok", browser);
        Assert.Equal([$"upload.timings_dropped {r.ItemId} 2"], Lines(host, "upload.timings_dropped "));
        Assert.Empty(Lines(host, "upload.rejected "));
    }

    [Fact]
    public async Task Complete_ignores_page_camera_and_dimensions()
    {
        await using var host = await NewHostAsync();
        var (r, _, sizes) = await StageMasterAsync(host, Bytes("gps-camera.jpg"));
        await PutAsync(sizes[0].AvifUrl, Bytes("gps-320.avif"), "image/avif");
        await PutAsync(sizes[0].WebpUrl, Bytes("gps-320.webp"), "image/webp");

        var resp = await CompleteAsync(host, r, extra: new
        {
            camera = new { make = "Evil", model = "Evil-1" },
            width = 9999,
            height = 9999,
        });
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var v = (await ItemDocAsync(host, r.BatchId, r.ItemId))["validated"].AsBsonDocument;
        Assert.Equal("TESTCAM", v["camera"]["make"].AsString);
        Assert.Equal("T1", v["camera"]["model"].AsString);
        Assert.Equal(400, v["width"].AsInt32);
        Assert.Equal(200, v["height"].AsInt32);
        Assert.DoesNotContain("Evil", (await BatchDocAsync(host, r.BatchId)).ToJson());
        Assert.DoesNotContain("9999", (await BatchDocAsync(host, r.BatchId)).ToJson());
    }

    // ── rejections (US2 AS-1/2/3/6, FR-026) ──────────────────────────────────

    /// <summary>
    /// Runs one rejection case: stage the 400-wide master, PUT the given sizes, complete, and assert
    /// 422 with the exact reason, the item failed, the attempt prefix empty, the public bucket
    /// unchanged and exactly one clean <c>upload.rejected</c> line naming <paramref name="file"/>.
    /// </summary>
    private async Task AssertRejectedAsync(byte[]? avif, byte[]? webp, string file, string reason, string? placeholder = null)
    {
        await using var host = await NewHostAsync();
        var pubBefore = await host.ListAsync(Pub, "");

        var (r, _, sizes) = await StageMasterAsync(host, Bytes("gps-camera.jpg"));
        if (avif is not null) await PutAsync(sizes[0].AvifUrl, avif, "image/avif");
        if (webp is not null) await PutAsync(sizes[0].WebpUrl, webp, "image/webp");
        var prefix = StagingKeys.AttemptPrefix(r.BatchId, r.ItemId, r.AttemptId);
        Assert.NotEmpty(await host.ListAsync(Priv, prefix)); // staged files exist before the rejection

        var resp = await CompleteAsync(host, r, placeholder: placeholder);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
        var json = await JsonAsync(resp);
        Assert.Equal("rejected", json.GetProperty("error").GetString());
        Assert.Equal(reason, json.GetProperty("message").GetString());

        var item = await ItemDocAsync(host, r.BatchId, r.ItemId);
        Assert.Equal("failed", item["state"].AsString);
        Assert.Equal(reason, item["reason"].AsString);
        Assert.True(item["validated"].IsBsonNull);

        Assert.Empty(await host.ListAsync(Priv, prefix));
        Assert.Equal(pubBefore, await host.ListAsync(Pub, ""));

        var line = Assert.Single(Lines(host, "upload.rejected "));
        Assert.Equal($"upload.rejected {r.BatchId} {r.ItemId} {file} {reason}", line);
        Assert.StartsWith(file + ": ", reason);
        foreach (var leak in new[] { "TESTCAM", "T1", "staging/", "http", "X-Amz" })
            Assert.DoesNotContain(leak, line, StringComparison.Ordinal);
    }

    [Fact]
    public Task Rejects_gps_avif_stored_as_320_avif() =>
        AssertRejectedAsync(AvifWithItem("Exif", Bytes("gps-320.avif")), Bytes("gps-320.webp"),
            "320.avif", "320.avif: AVIF item Exif");

    [Fact]
    public Task Rejects_gps_webp_stored_as_320_webp() =>
        AssertRejectedAsync(Bytes("gps-320.avif"), WebpWithGpsExif(),
            "320.webp", "320.webp: VP8X EXIF flag");

    [Fact]
    public Task Rejects_wrong_width() =>
        // A 200-wide AVIF stored as 320.avif.
        AssertRejectedAsync(Bytes("orient6-200.avif"), Bytes("gps-320.webp"),
            "320.avif", "320.avif: width does not match the file name");

    [Fact]
    public Task Rejects_a_missing_size() =>
        AssertRejectedAsync(Bytes("gps-320.avif"), null, "320.webp", "320.webp: not found");

    [Fact]
    public Task Rejects_placeholder_with_exif() =>
        AssertRejectedAsync(Bytes("gps-320.avif"), Bytes("gps-320.webp"),
            "placeholder", "placeholder: VP8X EXIF flag", DataUri(Bytes("placeholder-exif.webp")));

    [Fact]
    public async Task Master_check_rejects_a_non_jpeg_and_cleans_up()
    {
        await using var host = await NewHostAsync();
        var pubBefore = await host.ListAsync(Pub, "");
        var bytes = Bytes("not-a-jpeg.jpg");
        var r = await RegisterAsync(host, "x.jpg", bytes.LongLength);
        await PutAsync(r.MasterUrl, bytes, "image/jpeg");

        var resp = await host.PostJsonAsync($"/admin/upload/items/{r.ItemId}/master", new { attemptId = r.AttemptId });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
        var json = await JsonAsync(resp);
        Assert.Equal("rejected", json.GetProperty("error").GetString());
        Assert.Equal("not a JPEG", json.GetProperty("message").GetString());
        var item = await ItemDocAsync(host, r.BatchId, r.ItemId);
        Assert.Equal("failed", item["state"].AsString);
        Assert.Equal("not a JPEG", item["reason"].AsString);
        Assert.Empty(await host.ListAsync(Priv, StagingKeys.AttemptPrefix(r.BatchId, r.ItemId, r.AttemptId)));
        Assert.Equal(pubBefore, await host.ListAsync(Pub, ""));
        Assert.Equal([$"upload.rejected {r.BatchId} {r.ItemId} master.jpg not a JPEG"], Lines(host, "upload.rejected "));
    }

    [Fact]
    public async Task Master_check_on_a_missing_original_is_422_and_leaves_the_state()
    {
        await using var host = await NewHostAsync();
        var r = await RegisterAsync(host, "x.jpg", 1000);

        var resp = await host.PostJsonAsync($"/admin/upload/items/{r.ItemId}/master", new { attemptId = r.AttemptId });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
        Assert.Equal("original not found", (await JsonAsync(resp)).GetProperty("message").GetString());
        var item = await ItemDocAsync(host, r.BatchId, r.ItemId);
        Assert.Equal("queued", item["state"].AsString);
        Assert.True(item["reason"].IsBsonNull);
        Assert.Empty(Lines(host, "upload.rejected "));
    }

    // ── U4 presigns only the server's ladder (FR-020, FR-023) ────────────────

    [Fact]
    public async Task Master_presigns_one_size_for_a_400_wide_original()
    {
        await using var host = await NewHostAsync();
        var (r, master, sizes) = await StageMasterAsync(host, Bytes("gps-camera.jpg"));

        Assert.Equal([320], sizes.Select(s => s.W));
        var s = master.GetProperty("sizes")[0];
        Assert.Equal(3, s.EnumerateObject().Count()); // w, avif, webp: two URLs
        Assert.Equal("image/avif", s.GetProperty("avif").GetProperty("contentType").GetString());
        Assert.Equal("image/webp", s.GetProperty("webp").GetProperty("contentType").GetString());
        Assert.Contains($"/{StagingKeys.Size(r.BatchId, r.ItemId, r.AttemptId, 320, "avif")}?", sizes[0].AvifUrl);
        Assert.Contains($"/{StagingKeys.Size(r.BatchId, r.ItemId, r.AttemptId, 320, "webp")}?", sizes[0].WebpUrl);

        var m = (await ItemDocAsync(host, r.BatchId, r.ItemId))["master"].AsBsonDocument;
        Assert.Equal(400, m["width"].AsInt32);
        Assert.Equal(200, m["height"].AsInt32);
        Assert.Equal(Bytes("gps-camera.jpg").LongLength, m["length"].ToInt64());
    }

    [Fact]
    public async Task Master_presigns_six_widths_for_a_6000_wide_original()
    {
        await using var host = await NewHostAsync();
        var (_, master, sizes) = await StageMasterAsync(host, Bytes("fixture-6000x4000.jpg"));

        Assert.Equal(6000, master.GetProperty("width").GetInt32());
        Assert.Equal(4000, master.GetProperty("height").GetInt32());
        Assert.Equal([320, 640, 960, 1280, 1920, 2560], sizes.Select(s => s.W));
        Assert.Equal(12, sizes.SelectMany(s => new[] { s.AvifUrl, s.WebpUrl }).Distinct().Count());
    }

    [Fact]
    public async Task Master_applies_orientation_to_the_ladder()
    {
        await using var host = await NewHostAsync();
        var (_, master, sizes) = await StageMasterAsync(host, Bytes("orientation-6.jpg"));

        Assert.Equal(200, master.GetProperty("width").GetInt32());
        Assert.Equal(400, master.GetProperty("height").GetInt32());
        Assert.Equal([200], sizes.Select(s => s.W));
    }

    // ── shared rules: stale attempt, unknown item, effective state ───────────

    [Fact]
    public async Task Stale_attempt_on_master_progress_and_complete_is_409()
    {
        await using var host = await NewHostAsync();
        var (r, _, _) = await StageMasterAsync(host, Bytes("gps-camera.jpg"));
        var stale = OpaqueId.NewId();

        await AssertErrorAsync(await host.PostJsonAsync($"/admin/upload/items/{r.ItemId}/master", new { attemptId = stale }),
            HttpStatusCode.Conflict, "stale-attempt");
        await AssertErrorAsync(await host.PostJsonAsync($"/admin/upload/items/{r.ItemId}/progress",
                new { attemptId = stale, state = "processing", percent = 0 }),
            HttpStatusCode.Conflict, "stale-attempt");
        await AssertErrorAsync(await CompleteAsync(host, r with { AttemptId = stale }),
            HttpStatusCode.Conflict, "stale-attempt");

        // Nothing changed.
        var item = await ItemDocAsync(host, r.BatchId, r.ItemId);
        Assert.Equal("queued", item["state"].AsString);
        Assert.Equal(r.AttemptId, item["attemptId"].AsString);
    }

    [Fact]
    public async Task Unknown_invalid_or_deleted_item_is_404()
    {
        await using var host = await NewHostAsync();
        var r = await RegisterAsync(host, "x.jpg", 1000);

        foreach (var path in new[] { "master", "progress", "complete" })
        {
            var body = new { attemptId = r.AttemptId, state = "processing", percent = 0 };
            await AssertErrorAsync(await host.PostJsonAsync($"/admin/upload/items/{OpaqueId.NewId()}/{path}", body),
                HttpStatusCode.NotFound, "not-found");
            Assert.Equal(HttpStatusCode.NotFound,
                (await host.PostJsonAsync($"/admin/upload/items/NOT-AN-ID/{path}", body)).StatusCode);
        }

        // Deleted: pull the item through the store, as U9 will.
        var store = new BatchStore(Db(host), new OpTimer(NullLogger<OpTimer>.Instance), host.Clock, new UploadOptions());
        Assert.True(await store.PullItemAsync(r.BatchId, r.ItemId));
        await AssertErrorAsync(await host.PostJsonAsync($"/admin/upload/items/{r.ItemId}/master", new { attemptId = r.AttemptId }),
            HttpStatusCode.NotFound, "not-found");
    }

    [Fact]
    public async Task Progress_moves_forward_only_and_refuses_terminal_and_interrupted_items()
    {
        await using var host = await NewHostAsync();
        var r = await RegisterAsync(host, "x.jpg", 1000);
        var url = $"/admin/upload/items/{r.ItemId}/progress";

        var up = await host.PostJsonAsync(url, new { attemptId = r.AttemptId, state = "uploading", percent = 40 });
        Assert.Equal(HttpStatusCode.NoContent, up.StatusCode);
        var back = await host.PostJsonAsync(url, new { attemptId = r.AttemptId, state = "processing", percent = 50 });
        Assert.Equal(HttpStatusCode.NoContent, back.StatusCode);
        var item = await ItemDocAsync(host, r.BatchId, r.ItemId);
        Assert.Equal("uploading", item["state"].AsString);
        Assert.Equal(50, item["percent"].AsInt32);
        Assert.Equal(host.Clock.GetUtcNow().UtcDateTime, item["heartbeatAt"].ToUniversalTime(), TimeSpan.FromMilliseconds(1));

        // 121 s of silence: effectively interrupted → 409 not-active, and U2 says interrupted.
        host.Clock.Advance(TimeSpan.FromSeconds(121));
        await AssertErrorAsync(await host.PostJsonAsync(url, new { attemptId = r.AttemptId, state = "uploading", percent = 60 }),
            HttpStatusCode.Conflict, "not-active");
        var (_, state) = await host.GetJsonAsync("/admin/upload/state");
        var si = Assert.Single(state!.RootElement.GetProperty("batch").GetProperty("items").EnumerateArray());
        Assert.Equal("interrupted", si.GetProperty("state").GetString());
        Assert.Equal("uploading", (await ItemDocAsync(host, r.BatchId, r.ItemId))["state"].AsString); // never stored

        // A failed item: 409 not-active.
        var other = await RegisterAsync(host, "y.jpg", 1000);
        var bytes = Bytes("not-a-jpeg.jpg");
        var r2 = await RegisterAsync(host, "z.jpg", bytes.LongLength);
        await PutAsync(r2.MasterUrl, bytes, "image/jpeg");
        Assert.Equal(HttpStatusCode.UnprocessableEntity,
            (await host.PostJsonAsync($"/admin/upload/items/{r2.ItemId}/master", new { attemptId = r2.AttemptId })).StatusCode);
        await AssertErrorAsync(await host.PostJsonAsync($"/admin/upload/items/{r2.ItemId}/progress",
                new { attemptId = r2.AttemptId, state = "processing", percent = 0 }),
            HttpStatusCode.Conflict, "not-active");
        Assert.Equal("queued", (await ItemDocAsync(host, other.BatchId, other.ItemId))["state"].AsString);
    }

    // ── U2 / U3 ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task State_is_null_without_an_open_batch()
    {
        await using var host = await NewHostAsync();
        var (resp, json) = await host.GetJsonAsync("/admin/upload/state");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal(JsonValueKind.Null, json!.RootElement.GetProperty("batch").ValueKind);
        Assert.Equal("no-store", resp.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task Register_refuses_per_file_with_the_contract_reasons()
    {
        await using var host = await NewHostAsync();
        var resp = await host.PostJsonAsync("/admin/upload/items", new
        {
            files = new object[]
            {
                new { name = "ok.jpg", size = 1000, type = "image/jpeg" },
                new { name = "b.heic", size = 1000, type = "image/heic" },
                new { name = "big.jpg", size = 20_971_521, type = "image/jpeg" },
                new { name = "zero.jpg", size = 0, type = "image/jpeg" },
                new { name = "/\\\u0001", size = 1000, type = "image/jpeg" },
                new { name = "../dir/\u0007na\\me.jpg", size = 1000, type = "image/jpeg" },
            },
        });
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var json = await JsonAsync(resp);

        var accepted = json.GetProperty("accepted").EnumerateArray().ToList();
        Assert.Equal([0, 5], accepted.Select(a => a.GetProperty("index").GetInt32()));
        var refused = json.GetProperty("refused").EnumerateArray()
            .Select(x => (x.GetProperty("index").GetInt32(), x.GetProperty("reason").GetString())).ToList();
        Assert.Equal(
            [(1, "not a JPEG"), (2, "20.0 MB, over the 20 MB limit"), (3, "0.0 MB, over the 20 MB limit"), (4, "file name is empty")],
            refused);

        var batchId = json.GetProperty("batchId").GetString()!;
        var batch = await BatchDocAsync(host, batchId);
        Assert.Equal(["ok.jpg", "..dirname.jpg"], batch["items"].AsBsonArray.Select(i => i["fileName"].AsString));
        foreach (var a in accepted)
        {
            var itemId = a.GetProperty("itemId").GetString()!;
            var attemptId = a.GetProperty("attemptId").GetString()!;
            Assert.True(OpaqueId.IsValid(itemId));
            Assert.True(OpaqueId.IsValid(attemptId));
            var url = a.GetProperty("master").GetProperty("url").GetString()!;
            Assert.Contains($"/{StagingKeys.Master(batchId, itemId, attemptId)}?", url);
        }
        Assert.Equal(2, Lines(host, "op.timing s3.presign ").Count);
    }

    [Fact]
    public async Task Register_with_zero_or_too_many_files_is_400()
    {
        await using var host = await NewHostAsync();
        await AssertErrorAsync(await host.PostJsonAsync("/admin/upload/items", new { files = Array.Empty<object>() }),
            HttpStatusCode.BadRequest, "bad-request");
        var many = Enumerable.Range(0, 101).Select(i => new { name = $"{i}.jpg", size = 1, type = "image/jpeg" }).ToArray();
        await AssertErrorAsync(await host.PostJsonAsync("/admin/upload/items", new { files = many }),
            HttpStatusCode.BadRequest, "bad-request");
        var (_, state) = await host.GetJsonAsync("/admin/upload/state");
        Assert.Equal(JsonValueKind.Null, state!.RootElement.GetProperty("batch").ValueKind);
    }

    [Fact]
    public async Task Register_fills_the_batch_to_100_and_refuses_the_rest()
    {
        await using var host = await NewHostAsync();
        var first = Enumerable.Range(0, 98).Select(i => new { name = $"{i}.jpg", size = 1, type = "image/jpeg" }).ToArray();
        Assert.Equal(HttpStatusCode.OK, (await host.PostJsonAsync("/admin/upload/items", new { files = first })).StatusCode);

        var more = Enumerable.Range(0, 3).Select(i => new { name = $"m{i}.jpg", size = 1, type = "image/jpeg" }).ToArray();
        var json = await JsonAsync(await host.PostJsonAsync("/admin/upload/items", new { files = more }));
        Assert.Equal([0, 1], json.GetProperty("accepted").EnumerateArray().Select(a => a.GetProperty("index").GetInt32()));
        var refused = Assert.Single(json.GetProperty("refused").EnumerateArray());
        Assert.Equal(2, refused.GetProperty("index").GetInt32());
        Assert.Equal("batch is full (100 photos)", refused.GetProperty("reason").GetString());
        Assert.Equal(100, (await BatchDocAsync(host, json.GetProperty("batchId").GetString()!))["items"].AsBsonArray.Count);
    }

    // ── body handling: bodiless POST, oversize body, CSRF ────────────────────

    [Fact]
    public async Task Bodiless_post_is_400_not_500()
    {
        await using var host = await NewHostAsync();
        var r = await RegisterAsync(host, "x.jpg", 1000);
        foreach (var path in new[] { "/admin/upload/items", $"/admin/upload/items/{r.ItemId}/master",
                     $"/admin/upload/items/{r.ItemId}/progress", $"/admin/upload/items/{r.ItemId}/complete" })
        {
            var req = new HttpRequestMessage(HttpMethod.Post, path);
            req.Headers.TryAddWithoutValidation("X-CSRF-TOKEN", host.CsrfToken);
            await AssertErrorAsync(await host.Host.SendAsync(req), HttpStatusCode.BadRequest, "bad-request");
        }
    }

    [Fact]
    public async Task Each_endpoint_enforces_its_own_body_limit()
    {
        await using var host = await NewHostAsync();
        var r = await RegisterAsync(host, "x.jpg", 1000);

        async Task<HttpStatusCode> Post(string path, int bytes)
        {
            var json = $"{{\"attemptId\":\"{r.AttemptId}\",\"pad\":\"{new string('x', bytes)}\"}}";
            var req = new HttpRequestMessage(HttpMethod.Post, path)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
            req.Headers.TryAddWithoutValidation("X-CSRF-TOKEN", host.CsrfToken);
            return (await host.Host.SendAsync(req)).StatusCode;
        }

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, await Post($"/admin/upload/items/{r.ItemId}/progress", 5 * 1024));
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, await Post($"/admin/upload/items/{r.ItemId}/master", 5 * 1024));
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, await Post($"/admin/upload/items/{r.ItemId}/complete", 17 * 1024));
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, await Post("/admin/upload/items", 129 * 1024));
        // Under the complete limit but over the small one: not 413 (it reaches the handler).
        Assert.Equal(HttpStatusCode.BadRequest, await Post($"/admin/upload/items/{r.ItemId}/progress", 1024));
        Assert.Equal(HttpStatusCode.Conflict, await Post($"/admin/upload/items/{r.ItemId}/complete", 8 * 1024));
    }

    [Fact]
    public async Task Post_without_csrf_is_400_and_registers_nothing()
    {
        await using var host = await NewHostAsync();
        var resp = await host.PostJsonAsync("/admin/upload/items",
            new { files = new[] { new { name = "a.jpg", size = 1, type = "image/jpeg" } } }, csrf: false);
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        var (_, state) = await host.GetJsonAsync("/admin/upload/state");
        Assert.Equal(JsonValueKind.Null, state!.RootElement.GetProperty("batch").ValueKind);
    }
}
