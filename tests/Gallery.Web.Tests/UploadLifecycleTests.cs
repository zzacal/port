using System.Net;
using System.Text.Json;
using Gallery.Core.Ids;
using Gallery.Web.Storage;
using Gallery.Web.Tests.Fixtures;
using Gallery.Web.Upload;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;
using static Gallery.Web.Tests.Fixtures.UploadFixtures;

namespace Gallery.Web.Tests;

/// <summary>
/// 008 T038 [US4]: the item and batch lifecycle endpoints U6 fail, U8 restart, U9 delete and
/// U11 discard (contracts/http.md, data-model L-02–L-06, FR-011–FR-013, FR-017, FR-036, SC-003),
/// plus the best-effort cleanup log (decisions row 44) and U9 on an item a refused confirm copy
/// left failed (decisions row 61).
/// </summary>
[Collection("s3")]
public class UploadLifecycleTests : IClassFixture<MongoFixture>, IAsyncLifetime
{
    private const string Pub = "pub";
    private const string Priv = "priv";

    private readonly S3ProxyFixture _s3;
    private readonly MongoFixture _mongo;
    private readonly FakeGoogle _google = new();

    public UploadLifecycleTests(S3ProxyFixture s3, MongoFixture mongo)
    {
        _s3 = s3;
        _mongo = mongo;
    }

    public Task InitializeAsync() => _google.InitializeAsync();
    public Task DisposeAsync() => _google.DisposeAsync();

    private Task<UploadHost> NewHostAsync() => UploadHost.CreateAsync(_mongo, _google, _s3);

    // ── U6 fail ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Fail_marks_only_that_item_failed_with_the_reason_and_keeps_its_files()
    {
        await using var host = await NewHostAsync();
        var (r, _) = await UploadApi.StageMasterAsync(host, Bytes("gps-camera.jpg"));
        var other = await UploadApi.RegisterOneAsync(host, "other.jpg", 1000);
        var otherBefore = (await UploadApi.ItemAsync(host, other.BatchId, other.ItemId))!.ToJson();

        var resp = await UploadApi.ItemPost(host, r.ItemId, "fail",
            new { attemptId = r.AttemptId, reason = "Out of memory while preparing sizes" });
        Assert.Equal(HttpStatusCode.NoContent, resp.StatusCode);

        var item = (await UploadApi.ItemAsync(host, r.BatchId, r.ItemId))!;
        Assert.Equal("failed", item["state"].AsString);
        Assert.Equal("Out of memory while preparing sizes", item["reason"].AsString);
        Assert.Equal(r.AttemptId, item["attemptId"].AsString);
        // FR-017: the other item is untouched.
        Assert.Equal(otherBefore, (await UploadApi.ItemAsync(host, other.BatchId, other.ItemId))!.ToJson());
        // L-02: the attempt's staged files are kept until Restart or Delete.
        Assert.Equal([StagingKeys.Master(r.BatchId, r.ItemId, r.AttemptId)],
            await host.ListAsync(Priv, StagingKeys.AttemptPrefix(r.BatchId, r.ItemId, r.AttemptId)));

        // U2 reports it failed with the reason.
        var (_, state) = await host.GetJsonAsync("/admin/upload/state");
        var si = state!.RootElement.GetProperty("batch").GetProperty("items").EnumerateArray()
            .Single(i => i.GetProperty("itemId").GetString() == r.ItemId);
        Assert.Equal("failed", si.GetProperty("state").GetString());
        Assert.Equal("Out of memory while preparing sizes", si.GetProperty("reason").GetString());

        // A second fail on a failed item: 409 not-active.
        await UploadApi.AssertErrorAsync(await UploadApi.ItemPost(host, r.ItemId, "fail",
            new { attemptId = r.AttemptId, reason = "again" }), HttpStatusCode.Conflict, "not-active");
    }

    [Fact]
    public async Task Fail_reason_is_stripped_of_control_characters_and_capped_at_200()
    {
        await using var host = await NewHostAsync();
        var r = await UploadApi.RegisterOneAsync(host, "a.jpg", 1000);
        var reason = "bad\u0000\u0007\n\tthing " + new string('x', 300);

        var resp = await UploadApi.ItemPost(host, r.ItemId, "fail", new { attemptId = r.AttemptId, reason });
        Assert.Equal(HttpStatusCode.NoContent, resp.StatusCode);

        var stored = (await UploadApi.ItemAsync(host, r.BatchId, r.ItemId))!["reason"].AsString;
        Assert.Equal(200, stored.Length);
        Assert.Equal(("badthing " + new string('x', 300))[..200], stored);
        Assert.DoesNotContain(stored, char.IsControl);
    }

    // ── U8 restart ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Restart_with_a_stored_master_copies_it_and_returns_sizes_for_the_new_attempt()
    {
        await using var host = await NewHostAsync();
        var (r, oldSizes) = await UploadApi.StageMasterAsync(host, Bytes("gps-camera.jpg"));
        var oldMaster = (await UploadApi.ItemAsync(host, r.BatchId, r.ItemId))!["master"].AsBsonDocument;
        Assert.Equal(HttpStatusCode.NoContent, (await UploadApi.ItemPost(host, r.ItemId, "fail",
            new { attemptId = r.AttemptId, reason = "encode failed" })).StatusCode);

        var resp = await UploadApi.ItemPost(host, r.ItemId, "restart", new { attemptId = r.AttemptId });
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var json = await UploadApi.JsonAsync(resp);
        var next = json.GetProperty("attemptId").GetString()!;
        Assert.True(OpaqueId.IsValid(next));
        Assert.NotEqual(r.AttemptId, next);
        Assert.False(json.GetProperty("needsFile").GetBoolean());
        Assert.Equal("photo.jpg", json.GetProperty("fileName").GetString());
        Assert.False(json.TryGetProperty("master", out _));
        var size = Assert.Single(json.GetProperty("sizes").EnumerateArray());
        Assert.Equal(320, size.GetProperty("w").GetInt32());
        var avifUrl = size.GetProperty("avif").GetProperty("url").GetString()!;
        var webpUrl = size.GetProperty("webp").GetProperty("url").GetString()!;
        Assert.Equal("image/avif", size.GetProperty("avif").GetProperty("contentType").GetString());
        Assert.Equal("image/webp", size.GetProperty("webp").GetProperty("contentType").GetString());
        Assert.Contains($"/{StagingKeys.Size(r.BatchId, r.ItemId, next, 320, "avif")}?", avifUrl);
        Assert.Contains($"/{StagingKeys.Size(r.BatchId, r.ItemId, next, 320, "webp")}?", webpUrl);

        // The master lives under the new attempt; the old attempt prefix is empty.
        Assert.Equal([StagingKeys.Master(r.BatchId, r.ItemId, next)],
            await host.ListAsync(Priv, StagingKeys.ItemPrefix(r.BatchId, r.ItemId)));
        Assert.Empty(await host.ListAsync(Priv, StagingKeys.AttemptPrefix(r.BatchId, r.ItemId, r.AttemptId)));

        // Reset fields; master kept (same length and upright size).
        var item = (await UploadApi.ItemAsync(host, r.BatchId, r.ItemId))!;
        Assert.Equal(next, item["attemptId"].AsString);
        Assert.Equal("queued", item["state"].AsString);
        Assert.Equal(0, item["percent"].AsInt32);
        Assert.True(item["reason"].IsBsonNull);
        Assert.Empty(item["stored"].AsBsonArray);
        Assert.True(item["validated"].IsBsonNull);
        Assert.Equal(host.Clock.GetUtcNow().UtcDateTime, item["heartbeatAt"].ToUniversalTime(), TimeSpan.FromMilliseconds(1));
        var m = item["master"].AsBsonDocument;
        Assert.Equal(oldMaster["length"], m["length"]);
        Assert.Equal(oldMaster["width"], m["width"]);
        Assert.Equal(oldMaster["height"], m["height"]);

        // The old attemptId is stale on U5.
        await UploadApi.AssertErrorAsync(await UploadApi.ItemPost(host, r.ItemId, "progress",
            new { attemptId = r.AttemptId, state = "processing", percent = 0 }), HttpStatusCode.Conflict, "stale-attempt");

        // A PUT to an OLD size URL lands under the old prefix and is never read: junk there,
        // good files under the new attempt, and complete validates the new attempt only.
        await UploadApi.PutOkAsync(oldSizes[0].AvifUrl, AvifWithItem("Exif", Bytes("gps-320.avif")), "image/avif");
        await UploadApi.PutOkAsync(oldSizes[0].WebpUrl, WebpWithGpsExif(), "image/webp");
        Assert.Equal(2, (await host.ListAsync(Priv, StagingKeys.AttemptPrefix(r.BatchId, r.ItemId, r.AttemptId))).Count);
        await UploadApi.PutOkAsync(avifUrl, Bytes("gps-320.avif"), "image/avif");
        await UploadApi.PutOkAsync(webpUrl, Bytes("gps-320.webp"), "image/webp");

        var done = await UploadApi.CompleteAsync(host, r.ItemId, next);
        Assert.Equal(HttpStatusCode.OK, done.StatusCode);
        Assert.Equal("succeeded", (await UploadApi.JsonAsync(done)).GetProperty("state").GetString());
        var v = (await UploadApi.ItemAsync(host, r.BatchId, r.ItemId))!["validated"].AsBsonDocument;
        Assert.Equal(400, v["width"].AsInt32);
        Assert.Equal(Bytes("gps-320.avif").LongLength, v["sizes"][0]["bytesAvif"].ToInt64());
        Assert.Empty(UploadApi.Lines(host, "upload.rejected "));
        // s3.presign: U3 one, U4 two, U8 two (SC-006 reads every permission).
        Assert.Equal(5, UploadApi.Lines(host, "op.timing s3.presign ").Count(l => l.EndsWith(" ok")));
    }

    [Fact]
    public async Task Restart_without_a_master_asks_for_the_file_again()
    {
        await using var host = await NewHostAsync();
        var r = await UploadApi.RegisterOneAsync(host, "IMG_0042.jpg", 1000);
        Assert.Equal(HttpStatusCode.NoContent, (await UploadApi.ItemPost(host, r.ItemId, "fail",
            new { attemptId = r.AttemptId, reason = "upload failed (500)" })).StatusCode);

        var resp = await UploadApi.ItemPost(host, r.ItemId, "restart", new { attemptId = r.AttemptId });
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var json = await UploadApi.JsonAsync(resp);
        var next = json.GetProperty("attemptId").GetString()!;
        Assert.NotEqual(r.AttemptId, next);
        Assert.True(json.GetProperty("needsFile").GetBoolean());
        Assert.Equal("IMG_0042.jpg", json.GetProperty("fileName").GetString());
        Assert.False(json.TryGetProperty("sizes", out _));
        var master = json.GetProperty("master");
        Assert.Equal("image/jpeg", master.GetProperty("contentType").GetString());
        Assert.Contains($"/{StagingKeys.Master(r.BatchId, r.ItemId, next)}?", master.GetProperty("url").GetString());

        var item = (await UploadApi.ItemAsync(host, r.BatchId, r.ItemId))!;
        Assert.Equal("queued", item["state"].AsString);
        Assert.True(item["master"].IsBsonNull);
        Assert.True(item["reason"].IsBsonNull);

        // The new master URL works end to end: PUT, then U4 with the new attempt.
        await UploadApi.PutOkAsync(master.GetProperty("url").GetString()!, Bytes("gps-camera.jpg"), "image/jpeg");
        Assert.Equal(HttpStatusCode.OK,
            (await UploadApi.ItemPost(host, r.ItemId, "master", new { attemptId = next })).StatusCode);
    }

    [Fact]
    public async Task Restart_of_an_active_or_succeeded_item_is_409_not_active()
    {
        await using var host = await NewHostAsync();
        var r = await UploadApi.RegisterOneAsync(host, "a.jpg", 1000);
        await UploadApi.AssertErrorAsync(await UploadApi.ItemPost(host, r.ItemId, "restart", new { attemptId = r.AttemptId }),
            HttpStatusCode.Conflict, "not-active");

        var s = await UploadApi.StageSucceededAsync(host);
        await UploadApi.AssertErrorAsync(await UploadApi.ItemPost(host, s.ItemId, "restart", new { attemptId = s.AttemptId }),
            HttpStatusCode.Conflict, "not-active");
        Assert.Equal(r.AttemptId, (await UploadApi.ItemAsync(host, r.BatchId, r.ItemId))!["attemptId"].AsString);
    }

    // ── U9 delete ────────────────────────────────────────────────────────────

    public static TheoryData<string> DeleteStates => new()
    {
        "queued", "processing", "uploading", "succeeded", "failed", "interrupted",
    };

    [Theory]
    [MemberData(nameof(DeleteStates))]
    public async Task Delete_in_each_state_removes_the_item_and_its_prefix(string state)
    {
        await using var host = await NewHostAsync();
        var keep = await UploadApi.RegisterOneAsync(host, "keep.jpg", 1000);
        UploadApi.Reg r;
        if (state == "succeeded")
        {
            r = await UploadApi.StageSucceededAsync(host);
        }
        else
        {
            (r, _) = await UploadApi.StageMasterAsync(host, Bytes("gps-camera.jpg"));
            switch (state)
            {
                case "processing" or "uploading":
                    Assert.Equal(HttpStatusCode.NoContent, (await UploadApi.ItemPost(host, r.ItemId, "progress",
                        new { attemptId = r.AttemptId, state, percent = 10 })).StatusCode);
                    break;
                case "failed":
                    Assert.Equal(HttpStatusCode.NoContent, (await UploadApi.ItemPost(host, r.ItemId, "fail",
                        new { attemptId = r.AttemptId, reason = "x" })).StatusCode);
                    break;
                case "interrupted":
                    host.Clock.Advance(TimeSpan.FromSeconds(121));
                    break;
            }
        }
        Assert.Equal(state, UploadApi.EffectiveOf(await host.GetJsonAsync("/admin/upload/state"), r.ItemId));
        var itemPrefix = StagingKeys.ItemPrefix(r.BatchId, r.ItemId);
        Assert.NotEmpty(await host.ListAsync(Priv, itemPrefix));

        var resp = await UploadApi.ItemPost(host, r.ItemId, "delete", new { attemptId = r.AttemptId });
        Assert.Equal(HttpStatusCode.NoContent, resp.StatusCode);
        Assert.Null(await UploadApi.ItemAsync(host, r.BatchId, r.ItemId));
        Assert.Empty(await host.ListAsync(Priv, itemPrefix));
        Assert.NotNull(await UploadApi.ItemAsync(host, keep.BatchId, keep.ItemId));

        // Idempotent for the open batch, with or without an attemptId.
        Assert.Equal(HttpStatusCode.NoContent,
            (await UploadApi.ItemPost(host, r.ItemId, "delete", new { attemptId = r.AttemptId })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await UploadApi.ItemPost(host, r.ItemId, "delete", new { })).StatusCode);
        Assert.Single((await UploadApi.BatchAsync(host, r.BatchId))["items"].AsBsonArray);
    }

    [Fact]
    public async Task Delete_removes_every_attempt_of_the_item()
    {
        await using var host = await NewHostAsync();
        var (r, oldSizes) = await UploadApi.StageMasterAsync(host, Bytes("gps-camera.jpg"));
        Assert.Equal(HttpStatusCode.NoContent, (await UploadApi.ItemPost(host, r.ItemId, "fail",
            new { attemptId = r.AttemptId, reason = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await UploadApi.ItemPost(host, r.ItemId, "restart",
            new { attemptId = r.AttemptId })).StatusCode);
        // A late PUT through the old URL recreates a file under the old attempt.
        await UploadApi.PutOkAsync(oldSizes[0].AvifUrl, Bytes("gps-320.avif"), "image/avif");
        Assert.Equal(2, (await host.ListAsync(Priv, StagingKeys.ItemPrefix(r.BatchId, r.ItemId))).Count);

        Assert.Equal(HttpStatusCode.NoContent, (await UploadApi.ItemPost(host, r.ItemId, "delete", new { })).StatusCode);
        Assert.Empty(await host.ListAsync(Priv, StagingKeys.ItemPrefix(r.BatchId, r.ItemId)));
        Assert.Null(await UploadApi.ItemAsync(host, r.BatchId, r.ItemId));
    }

    [Fact]
    public async Task Delete_of_an_unknown_item_or_with_no_open_batch_is_404()
    {
        await using var host = await NewHostAsync();
        await UploadApi.AssertErrorAsync(await UploadApi.ItemPost(host, OpaqueId.NewId(), "delete", new { }),
            HttpStatusCode.NotFound, "not-found");
        Assert.Equal(HttpStatusCode.NotFound,
            (await UploadApi.ItemPost(host, "NOT-AN-ID", "delete", new { })).StatusCode);
    }

    // ── stale attempt ────────────────────────────────────────────────────────

    [Fact]
    public async Task Restart_and_delete_with_a_stale_attempt_are_409_and_change_nothing()
    {
        await using var host = await NewHostAsync();
        var (r, _) = await UploadApi.StageMasterAsync(host, Bytes("gps-camera.jpg"));
        Assert.Equal(HttpStatusCode.NoContent, (await UploadApi.ItemPost(host, r.ItemId, "fail",
            new { attemptId = r.AttemptId, reason = "x" })).StatusCode);
        var before = (await UploadApi.BatchAsync(host, r.BatchId)).ToJson();
        var stale = OpaqueId.NewId();

        await UploadApi.AssertErrorAsync(await UploadApi.ItemPost(host, r.ItemId, "restart", new { attemptId = stale }),
            HttpStatusCode.Conflict, "stale-attempt");
        await UploadApi.AssertErrorAsync(await UploadApi.ItemPost(host, r.ItemId, "delete", new { attemptId = stale }),
            HttpStatusCode.Conflict, "stale-attempt");
        await UploadApi.AssertErrorAsync(await UploadApi.ItemPost(host, r.ItemId, "fail", new { attemptId = stale, reason = "y" }),
            HttpStatusCode.Conflict, "stale-attempt");

        Assert.Equal(before, (await UploadApi.BatchAsync(host, r.BatchId)).ToJson());
        Assert.NotEmpty(await host.ListAsync(Priv, StagingKeys.AttemptPrefix(r.BatchId, r.ItemId, r.AttemptId)));
    }

    // ── best-effort cleanup (decisions row 44) ───────────────────────────────

    [Fact]
    public async Task TryDelete_with_a_throwing_delegate_logs_one_cleanup_failed_and_does_not_throw()
    {
        var log = new ListLogger();
        const string key = "staging/aaaaaaaaaaaaaaaaaaaaaaaaaa/x.avif";

        await ImageStore.TryDeleteAsync(() => throw new TimeoutException("secret-ish detail http://x"), key, log);

        var line = Assert.Single(log.Lines);
        Assert.Equal($"upload.cleanup_failed {key} TimeoutException", line);
        Assert.DoesNotContain("secret-ish", line);

        // A delegate that completes logs nothing.
        await ImageStore.TryDeleteAsync(() => Task.CompletedTask, key, log);
        Assert.Single(log.Lines);
    }

    private sealed class ListLogger : ILogger
    {
        public List<string> Lines { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Lines.Add(formatter(state, exception));
    }
}

/// <summary>Shared HTTP helpers for the Batch F upload tests (intake, lifecycle, resume, access).</summary>
internal static class UploadApi
{
    public const string Color = "#a1b2c3";

    public sealed record Reg(string BatchId, string ItemId, string AttemptId, string MasterUrl);

    public sealed record Size(int W, string AvifUrl, string WebpUrl);

    public static async Task<JsonElement> JsonAsync(HttpResponseMessage resp) =>
        JsonDocument.Parse(await resp.Content.ReadAsStringAsync()).RootElement.Clone();

    public static async Task<Reg> RegisterOneAsync(UploadHost host, string name, long size)
    {
        var resp = await host.PostJsonAsync("/admin/upload/items",
            new { files = new[] { new { name, size, type = "image/jpeg" } } });
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var json = await JsonAsync(resp);
        Assert.Equal(0, json.GetProperty("refused").GetArrayLength());
        var a = json.GetProperty("accepted")[0];
        return new Reg(json.GetProperty("batchId").GetString()!, a.GetProperty("itemId").GetString()!,
            a.GetProperty("attemptId").GetString()!, a.GetProperty("master").GetProperty("url").GetString()!);
    }

    /// <summary>Register, PUT the master, U4. Returns the registration and the size URLs.</summary>
    public static async Task<(Reg R, List<Size> Sizes)> StageMasterAsync(UploadHost host, byte[] master, string name = "photo.jpg")
    {
        var r = await RegisterOneAsync(host, name, master.LongLength);
        await PutOkAsync(r.MasterUrl, master, "image/jpeg");
        var resp = await ItemPost(host, r.ItemId, "master", new { attemptId = r.AttemptId });
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var sizes = (await JsonAsync(resp)).GetProperty("sizes").EnumerateArray().Select(s => new Size(
            s.GetProperty("w").GetInt32(),
            s.GetProperty("avif").GetProperty("url").GetString()!,
            s.GetProperty("webp").GetProperty("url").GetString()!)).ToList();
        return (r, sizes);
    }

    /// <summary>gps-camera.jpg (400×200, one 320 size) all the way to succeeded.</summary>
    public static async Task<Reg> StageSucceededAsync(UploadHost host, string name = "IMG_0001.jpg")
    {
        var (r, sizes) = await StageMasterAsync(host, Bytes("gps-camera.jpg"), name);
        await PutOkAsync(sizes[0].AvifUrl, Bytes("gps-320.avif"), "image/avif");
        await PutOkAsync(sizes[0].WebpUrl, Bytes("gps-320.webp"), "image/webp");
        Assert.Equal(HttpStatusCode.OK, (await CompleteAsync(host, r.ItemId, r.AttemptId)).StatusCode);
        return r;
    }

    public static Task<HttpResponseMessage> CompleteAsync(UploadHost host, string itemId, string attemptId,
        string placeholder = "gps-placeholder.webp") =>
        ItemPost(host, itemId, "complete", new
        {
            attemptId,
            placeholder = new { color = Color, preview = DataUri(Bytes(placeholder)) },
            timings = Array.Empty<object>(),
        });

    public static Task<HttpResponseMessage> ItemPost(UploadHost host, string itemId, string action, object body) =>
        host.PostJsonAsync($"/admin/upload/items/{itemId}/{action}", body);

    public static async Task PutOkAsync(string url, byte[] bytes, string contentType) =>
        Assert.Equal(HttpStatusCode.OK, (await UploadHost.PutToPresignedAsync(url, bytes, contentType)).StatusCode);

    public static IMongoDatabase Db(UploadHost host) =>
        host.Services.GetRequiredService<IMongoClient>().GetDatabase(host.DatabaseName);

    public static async Task<BsonDocument> BatchAsync(UploadHost host, string batchId) =>
        await Db(host).GetCollection<BsonDocument>(BatchStore.CollectionName)
            .Find(new BsonDocument("_id", batchId)).SingleAsync();

    /// <summary>The stored item, or null when it is not in the batch.</summary>
    public static async Task<BsonDocument?> ItemAsync(UploadHost host, string batchId, string itemId) =>
        (await BatchAsync(host, batchId))["items"].AsBsonArray.Select(i => i.AsBsonDocument)
            .SingleOrDefault(i => i["itemId"] == itemId);

    /// <summary>The effective state U2 reports for <paramref name="itemId"/>.</summary>
    public static string EffectiveOf((HttpResponseMessage Response, JsonDocument? Body) state, string itemId)
    {
        Assert.Equal(HttpStatusCode.OK, state.Response.StatusCode);
        return state.Body!.RootElement.GetProperty("batch").GetProperty("items").EnumerateArray()
            .Single(i => i.GetProperty("itemId").GetString() == itemId).GetProperty("state").GetString()!;
    }

    public static List<string> Lines(UploadHost host, string prefix)
    {
        lock (host.LogLines)
            return host.LogLines.Where(l => l.StartsWith(prefix, StringComparison.Ordinal)).ToList();
    }

    public static async Task AssertErrorAsync(HttpResponseMessage resp, HttpStatusCode status, string error)
    {
        Assert.Equal(status, resp.StatusCode);
        var json = await JsonAsync(resp);
        Assert.Equal(error, json.GetProperty("error").GetString());
        Assert.False(string.IsNullOrWhiteSpace(json.GetProperty("message").GetString()));
    }
}
