using System.Net;
using System.Text;
using System.Text.Json;
using Gallery.Web.Tests.Fixtures;
using MongoDB.Bson;
using static Gallery.Web.Tests.Fixtures.UploadFixtures;

namespace Gallery.Web.Tests;

/// <summary>
/// 008 T040 [SAFETY] (FR-001, I.3, I.6, plan item 5): every upload route U1–U11 sits behind the
/// owner session and, for POSTs, the CSRF header. Each case runs on its own host with a fresh
/// batch holding one registered item, and asserts ONE exact status (no "2xx or 4xx" assertions):
/// <list type="bullet">
///   <item>no session: GET → 302 <c>/admin/signin</c>, POST → 401;</item>
///   <item>session, no <c>X-CSRF-TOKEN</c>: every POST → 400 with the batch document unchanged;</item>
///   <item>session and token: the contract status for each route;</item>
///   <item>every response carries <c>Cache-Control: no-store</c>.</item>
/// </list>
/// </summary>
[Collection("s3")]
public class UploadAccessTests : IClassFixture<MongoFixture>, IAsyncLifetime
{
    private readonly S3ProxyFixture _s3;
    private readonly MongoFixture _mongo;
    private readonly FakeGoogle _google = new();

    public UploadAccessTests(S3ProxyFixture s3, MongoFixture mongo)
    {
        _s3 = s3;
        _mongo = mongo;
    }

    public Task InitializeAsync() => _google.InitializeAsync();
    public Task DisposeAsync() => _google.DisposeAsync();

    // ── the route table ──────────────────────────────────────────────────────

    private sealed record Route(
        string Method, string Path, Func<UploadApi.Reg, object?> Body, HttpStatusCode Status, string? Error);

    private static readonly Dictionary<string, Route> Routes = new()
    {
        ["U1"] = new("GET", "/admin/upload", _ => null, HttpStatusCode.OK, null),
        ["U2"] = new("GET", "/admin/upload/state", _ => null, HttpStatusCode.OK, null),
        ["U3"] = new("POST", "/admin/upload/items",
            _ => new { files = new[] { new { name = "second.jpg", size = 1000, type = "image/jpeg" } } }, HttpStatusCode.OK, null),
        ["U4"] = new("POST", "/admin/upload/items/{item}/master",
            r => new { attemptId = r.AttemptId }, HttpStatusCode.UnprocessableEntity, "rejected"),
        ["U5"] = new("POST", "/admin/upload/items/{item}/progress",
            r => new { attemptId = r.AttemptId, state = "processing", percent = 0 }, HttpStatusCode.NoContent, null),
        ["U6"] = new("POST", "/admin/upload/items/{item}/fail",
            r => new { attemptId = r.AttemptId, reason = "Out of memory" }, HttpStatusCode.NoContent, null),
        // A registered item has no checked master yet, so complete answers 409 not-active
        // (decisions row 58); the 422 of a checked item is Complete_after_the_master_check_is_422.
        ["U7"] = new("POST", "/admin/upload/items/{item}/complete",
            r => new
            {
                attemptId = r.AttemptId,
                placeholder = new { color = UploadApi.Color, preview = DataUri(Bytes("gps-placeholder.webp")) },
                timings = Array.Empty<object>(),
            }, HttpStatusCode.Conflict, "not-active"),
        ["U8"] = new("POST", "/admin/upload/items/{item}/restart",
            r => new { attemptId = r.AttemptId }, HttpStatusCode.Conflict, "not-active"),
        ["U9"] = new("POST", "/admin/upload/items/{item}/delete",
            r => new { attemptId = r.AttemptId }, HttpStatusCode.NoContent, null),
        ["U10"] = new("POST", "/admin/upload/confirm",
            r => new { batchId = r.BatchId }, HttpStatusCode.Conflict, "not-ready"),
        ["U11"] = new("POST", "/admin/upload/discard",
            r => new { batchId = r.BatchId }, HttpStatusCode.OK, null),
    };

    public static TheoryData<string> AllRoutes()
    {
        var data = new TheoryData<string>();
        foreach (var k in Routes.Keys) data.Add(k);
        return data;
    }

    public static TheoryData<string> PostRoutes()
    {
        var data = new TheoryData<string>();
        foreach (var (k, r) in Routes) if (r.Method == "POST") data.Add(k);
        return data;
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    /// <summary>A signed-in host whose open batch holds one registered (queued) item.</summary>
    private async Task<(UploadHost Host, UploadApi.Reg Item)> FreshAsync()
    {
        var host = await UploadHost.CreateAsync(_mongo, _google, _s3);
        try
        {
            return (host, await UploadApi.RegisterOneAsync(host, "first.jpg", 1000));
        }
        catch
        {
            await host.DisposeAsync();
            throw;
        }
    }

    private static Task<HttpResponseMessage> SendAsync(UploadHost host, Route route, UploadApi.Reg r, bool csrf)
    {
        var request = new HttpRequestMessage(new HttpMethod(route.Method), route.Path.Replace("{item}", r.ItemId));
        var body = route.Body(r);
        if (body is not null)
            request.Content = new StringContent(
                JsonSerializer.Serialize(body, new JsonSerializerOptions(JsonSerializerDefaults.Web)), Encoding.UTF8, "application/json");
        if (csrf)
            request.Headers.TryAddWithoutValidation("X-CSRF-TOKEN", host.CsrfToken);
        return host.Host.SendAsync(request);
    }

    private static void AssertNoStore(HttpResponseMessage resp, string route) =>
        Assert.True(resp.Headers.CacheControl?.NoStore == true,
            $"{route} answered {(int)resp.StatusCode} without Cache-Control: no-store ({resp.Headers.CacheControl})");

    private static async Task<string> BatchJsonAsync(UploadHost host, string batchId) =>
        (await UploadApi.BatchAsync(host, batchId)).ToJson();

    // ── no session ───────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(AllRoutes))]
    public async Task Without_a_session_GET_redirects_to_signin_and_POST_is_401(string name)
    {
        var route = Routes[name];
        var (host, r) = await FreshAsync();
        await using var _ = host;
        var before = await BatchJsonAsync(host, r.BatchId);
        host.Host.ClearCookies();
        Assert.Null(host.Host.SessionCookie);

        var resp = await SendAsync(host, route, r, csrf: true);

        if (route.Method == "GET")
        {
            Assert.Equal(HttpStatusCode.Found, resp.StatusCode);
            Assert.StartsWith("/admin/signin", resp.Headers.Location?.ToString());
            var body = await resp.Content.ReadAsStringAsync();
            Assert.DoesNotContain(r.ItemId, body);
            Assert.DoesNotContain("first.jpg", body);
        }
        else
        {
            Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
        }
        AssertNoStore(resp, name);
        Assert.Equal(before, await BatchJsonAsync(host, r.BatchId));
    }

    // ── session, no CSRF token ───────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(PostRoutes))]
    public async Task With_a_session_and_no_csrf_token_every_POST_is_400_and_changes_nothing(string name)
    {
        var route = Routes[name];
        var (host, r) = await FreshAsync();
        await using var _ = host;
        var before = await BatchJsonAsync(host, r.BatchId);
        var staged = await host.ListAsync("priv", Gallery.Web.Upload.StagingKeys.BatchPrefix(r.BatchId));

        var resp = await SendAsync(host, route, r, csrf: false);

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        AssertNoStore(resp, name);
        Assert.Equal(before, await BatchJsonAsync(host, r.BatchId));
        Assert.Equal(staged, await host.ListAsync("priv", Gallery.Web.Upload.StagingKeys.BatchPrefix(r.BatchId)));
        Assert.Empty(UploadApi.Lines(host, "upload.discarded "));
    }

    [Theory]
    [MemberData(nameof(PostRoutes))]
    public async Task With_a_session_and_a_wrong_csrf_token_every_POST_is_400(string name)
    {
        var route = Routes[name];
        var (host, r) = await FreshAsync();
        await using var _ = host;
        var before = await BatchJsonAsync(host, r.BatchId);

        var request = new HttpRequestMessage(HttpMethod.Post, route.Path.Replace("{item}", r.ItemId))
        {
            Content = new StringContent(JsonSerializer.Serialize(route.Body(r), new JsonSerializerOptions(JsonSerializerDefaults.Web)),
                Encoding.UTF8, "application/json"),
        };
        request.Headers.TryAddWithoutValidation("X-CSRF-TOKEN", "not-the-token");
        var resp = await host.Host.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        AssertNoStore(resp, name);
        Assert.Equal(before, await BatchJsonAsync(host, r.BatchId));
    }

    // ── session and token: the exact contract status ─────────────────────────

    [Theory]
    [MemberData(nameof(AllRoutes))]
    public async Task With_a_session_and_a_token_each_route_answers_its_contract_status(string name)
    {
        var route = Routes[name];
        var (host, r) = await FreshAsync();
        await using var _ = host;

        var resp = await SendAsync(host, route, r, csrf: true);

        Assert.True(route.Status == resp.StatusCode,
            $"{name} answered {(int)resp.StatusCode}, expected {(int)route.Status}: {await resp.Content.ReadAsStringAsync()}");
        AssertNoStore(resp, name);
        if (route.Error is not null)
            await UploadApi.AssertErrorAsync(resp, route.Status, route.Error);
    }

    [Fact]
    public async Task Complete_after_the_master_check_is_422()
    {
        var (host, _) = await FreshAsync();
        await using var __ = host;
        var (r, _) = await UploadApi.StageMasterAsync(host, Bytes("gps-camera.jpg"));

        // No sizes were PUT, so validation rejects the photo.
        var resp = await UploadApi.CompleteAsync(host, r.ItemId, r.AttemptId);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
        AssertNoStore(resp, "U7");
        await UploadApi.AssertErrorAsync(resp, HttpStatusCode.UnprocessableEntity, "rejected");
    }
}
