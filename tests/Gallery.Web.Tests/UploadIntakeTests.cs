using System.Net;
using System.Text;
using Gallery.Web.Tests.Fixtures;
using Gallery.Web.Upload;
using static Gallery.Web.Tests.Fixtures.UploadFixtures;

namespace Gallery.Web.Tests;

/// <summary>
/// 008 T036 [US3] (AS-4/5, FR-005): what the batch refuses at intake, with the exact contract
/// strings, while the valid entries in the same request are accepted; batch and request caps; the
/// register body limit; and the server's own read of a staged master overruling what the page
/// declared (a renamed PNG, an original over <c>MaxFileBytes</c>).
/// </summary>
[Collection("s3")]
public class UploadIntakeTests : IClassFixture<MongoFixture>, IAsyncLifetime
{
    private const string Priv = "priv";

    private readonly S3ProxyFixture _s3;
    private readonly MongoFixture _mongo;
    private readonly FakeGoogle _google = new();

    public UploadIntakeTests(S3ProxyFixture s3, MongoFixture mongo)
    {
        _s3 = s3;
        _mongo = mongo;
    }

    public Task InitializeAsync() => _google.InitializeAsync();
    public Task DisposeAsync() => _google.DisposeAsync();

    private Task<UploadHost> NewHostAsync(Dictionary<string, string?>? overrides = null) =>
        UploadHost.CreateAsync(_mongo, _google, _s3, overrides);

    private static object[] Files(int n, string prefix) =>
        Enumerable.Range(0, n).Select(i => (object)new { name = $"{prefix}{i}.jpg", size = 1000, type = "image/jpeg" }).ToArray();

    [Fact]
    public async Task Png_type_oversize_and_empty_name_are_refused_while_the_valid_entries_are_accepted()
    {
        await using var host = await NewHostAsync();
        var resp = await host.PostJsonAsync("/admin/upload/items", new
        {
            files = new object[]
            {
                new { name = "first.jpg", size = 1000, type = "image/jpeg" },
                new { name = "shot.png", size = 1000, type = "image/png" },
                new { name = "huge.jpg", size = 20_971_521, type = "image/jpeg" },
                new { name = "", size = 1000, type = "image/jpeg" },
                new { name = "last.jpg", size = 20_971_520, type = "image/jpeg" },
            },
        });

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var json = await UploadApi.JsonAsync(resp);
        Assert.Equal([0, 4], json.GetProperty("accepted").EnumerateArray().Select(a => a.GetProperty("index").GetInt32()));
        var refused = json.GetProperty("refused").EnumerateArray()
            .Select(r => (r.GetProperty("index").GetInt32(), r.GetProperty("name").GetString(), r.GetProperty("reason").GetString()))
            .ToList();
        Assert.Equal(
        [
            (1, "shot.png", "not a JPEG"),
            (2, "huge.jpg", "20.0 MB, over the 20 MB limit"),
            (3, "", "file name is empty"),
        ], refused);

        var batch = await UploadApi.BatchAsync(host, json.GetProperty("batchId").GetString()!);
        Assert.Equal(["first.jpg", "last.jpg"], batch["items"].AsBsonArray.Select(i => i["fileName"].AsString));
        Assert.All(batch["items"].AsBsonArray, i => Assert.Equal("queued", i["state"].AsString));
    }

    [Fact]
    public async Task A_batch_of_95_plus_10_accepts_5_and_refuses_5_as_full()
    {
        await using var host = await NewHostAsync();
        var first = await host.PostJsonAsync("/admin/upload/items", new { files = Files(95, "a") });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(95, (await UploadApi.JsonAsync(first)).GetProperty("accepted").GetArrayLength());

        var resp = await host.PostJsonAsync("/admin/upload/items", new { files = Files(10, "b") });
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var json = await UploadApi.JsonAsync(resp);
        Assert.Equal([0, 1, 2, 3, 4], json.GetProperty("accepted").EnumerateArray().Select(a => a.GetProperty("index").GetInt32()));
        var refused = json.GetProperty("refused").EnumerateArray().ToList();
        Assert.Equal([5, 6, 7, 8, 9], refused.Select(r => r.GetProperty("index").GetInt32()));
        Assert.All(refused, r => Assert.Equal("batch is full (100 photos)", r.GetProperty("reason").GetString()));
        Assert.Equal(["b5.jpg", "b6.jpg", "b7.jpg", "b8.jpg", "b9.jpg"], refused.Select(r => r.GetProperty("name").GetString()));

        var batch = await UploadApi.BatchAsync(host, json.GetProperty("batchId").GetString()!);
        Assert.Equal(100, batch["items"].AsBsonArray.Count);
    }

    [Fact]
    public async Task One_hundred_and_one_entries_in_one_request_is_400()
    {
        await using var host = await NewHostAsync();
        await UploadApi.AssertErrorAsync(await host.PostJsonAsync("/admin/upload/items", new { files = Files(101, "x") }),
            HttpStatusCode.BadRequest, "bad-request");
        var (_, state) = await host.GetJsonAsync("/admin/upload/state");
        Assert.Equal(System.Text.Json.JsonValueKind.Null, state!.RootElement.GetProperty("batch").ValueKind);
    }

    [Fact]
    public async Task A_200_KB_register_body_is_413()
    {
        await using var host = await NewHostAsync();
        var json = $"{{\"files\":[{{\"name\":\"a.jpg\",\"size\":1,\"type\":\"image/jpeg\"}}],\"pad\":\"{new string('x', 200 * 1024)}\"}}";
        var req = new HttpRequestMessage(HttpMethod.Post, "/admin/upload/items")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        req.Headers.TryAddWithoutValidation("X-CSRF-TOKEN", host.CsrfToken);

        var resp = await host.Host.SendAsync(req);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, resp.StatusCode);
        var (_, state) = await host.GetJsonAsync("/admin/upload/state");
        Assert.Equal(System.Text.Json.JsonValueKind.Null, state!.RootElement.GetProperty("batch").ValueKind);
    }

    [Fact]
    public async Task A_png_renamed_to_jpg_is_refused_by_the_master_check()
    {
        await using var host = await NewHostAsync();
        var bytes = Bytes("not-a-jpeg.jpg");
        var r = await UploadApi.RegisterOneAsync(host, "renamed.jpg", bytes.LongLength); // the page says image/jpeg
        await UploadApi.PutOkAsync(r.MasterUrl, bytes, "image/jpeg");
        Assert.NotEmpty(await host.ListAsync(Priv, StagingKeys.AttemptPrefix(r.BatchId, r.ItemId, r.AttemptId)));

        var resp = await UploadApi.ItemPost(host, r.ItemId, "master", new { attemptId = r.AttemptId });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
        var json = await UploadApi.JsonAsync(resp);
        Assert.Equal("rejected", json.GetProperty("error").GetString());
        Assert.Equal("not a JPEG", json.GetProperty("message").GetString());
        var item = (await UploadApi.ItemAsync(host, r.BatchId, r.ItemId))!;
        Assert.Equal("failed", item["state"].AsString);
        Assert.Equal("not a JPEG", item["reason"].AsString);
        Assert.True(item["master"].IsBsonNull);
        Assert.Empty(await host.ListAsync(Priv, StagingKeys.AttemptPrefix(r.BatchId, r.ItemId, r.AttemptId)));
    }

    [Fact]
    public async Task A_staged_master_over_MaxFileBytes_is_refused_with_the_size_reason()
    {
        await using var host = await NewHostAsync(new() { ["Upload:MaxFileBytes"] = "1000" });
        var bytes = Bytes("gps-camera.jpg"); // 1296 bytes, a real JPEG
        Assert.True(bytes.LongLength > 1000);
        // The page declares an allowed size; the server checks the stored object's real length.
        var r = await UploadApi.RegisterOneAsync(host, "big.jpg", 900);
        await UploadApi.PutOkAsync(r.MasterUrl, bytes, "image/jpeg");

        var resp = await UploadApi.ItemPost(host, r.ItemId, "master", new { attemptId = r.AttemptId });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
        var json = await UploadApi.JsonAsync(resp);
        Assert.Equal("rejected", json.GetProperty("error").GetString());
        const string reason = "0.0 MB, over the 0 MB limit";
        Assert.Equal(reason, MasterCheck.OverLimit(bytes.LongLength, 1000));
        Assert.Equal(reason, json.GetProperty("message").GetString());
        var item = (await UploadApi.ItemAsync(host, r.BatchId, r.ItemId))!;
        Assert.Equal("failed", item["state"].AsString);
        Assert.Equal(reason, item["reason"].AsString);
        Assert.Empty(await host.ListAsync(Priv, StagingKeys.AttemptPrefix(r.BatchId, r.ItemId, r.AttemptId)));
    }
}
