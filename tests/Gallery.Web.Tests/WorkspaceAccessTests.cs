using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Gallery.Web.Library;
using Gallery.Web.Tests.Fixtures;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Gallery.Web.Tests;

/// <summary>
/// 020 T017 [SAFETY] (I.3, I.6; contracts/http.md; TI-35, C-11): every workspace route sits behind
/// the owner session and, for POSTs, the antiforgery token. Each case runs on its own host holding
/// real galleries and photos, and every body WOULD change something if a gate let it through:
/// <list type="bullet">
///   <item>no session: POST → 401 even with the token header, dump of <c>images</c>,
///     <c>galleries</c>, <c>site</c> unchanged;</item>
///   <item>session but no token: JSON POST, bare POST and a wrong token → 400, dump unchanged, no purge;</item>
///   <item>session + CSRF header + empty body → 400 <c>bad-request</c> on every A-route, never 500;</item>
///   <item>every response carries <c>Cache-Control: no-store</c> and <c>X-Robots-Tag: noindex</c>.</item>
/// </list>
/// W1–W4 rows join the table in T035.
/// </summary>
[Collection("s3")]
public class WorkspaceAccessTests(S3ProxyFixture s3, MongoFixture mongo) : IClassFixture<MongoFixture>
{
    /// <summary>Route id → (method, path template). {gid}/{iid} are replaced with the host's real ids.</summary>
    private static readonly Dictionary<string, (string Method, string Path)> Routes = new()
    {
        ["A1"] = ("POST", "/admin/api/photos/{iid}"),
        ["A2"] = ("POST", "/admin/api/photos/delete"),
        ["A3"] = ("POST", "/admin/api/galleries"),
        ["A4"] = ("POST", "/admin/api/galleries/order"),
        ["A5"] = ("POST", "/admin/api/galleries/{gid}"),
        ["A6"] = ("POST", "/admin/api/galleries/{gid}/visibility"),
        ["A7"] = ("POST", "/admin/api/galleries/{gid}/photos"),
        ["A8"] = ("POST", "/admin/api/galleries/{gid}/photos/remove"),
        ["A9"] = ("POST", "/admin/api/galleries/{gid}/order"),
        ["A10"] = ("POST", "/admin/api/galleries/{gid}/cover"),
        ["A11"] = ("POST", "/admin/api/galleries/{gid}/delete"),
    };

    [Fact]
    public void The_route_table_is_A1_to_A11()
    {
        Assert.Equal(["A1", "A2", "A3", "A4", "A5", "A6", "A7", "A8", "A9", "A10", "A11"], Routes.Keys.ToList());
    }

    public static TheoryData<string> Enabled() => new(Routes.Keys);

    /// <param name="Gid">"Coast": photos Iid and Other, private.</param>
    /// <param name="Gid2">"Second": empty, after Coast in home order.</param>
    /// <param name="Loose">A photo in no gallery.</param>
    private sealed record Ctx(LibraryHost Host, string Gid, string Gid2, string Iid, string Other, string Loose);

    private async Task<Ctx> ArrangeAsync()
    {
        var h = await LibraryHost.CreateAsync(mongo, s3);
        var img = await h.InsertUploadedImageAsync(files: false, title: "Pier");
        var other = await h.InsertUploadedImageAsync(files: false, title: "Rocks");
        var loose = await h.InsertUploadedImageAsync(files: false, title: "Sand");
        var created = await h.RunAsync<GalleryService, OperationResult>(s => s.CreateAsync("Coast"));
        var gid = created.GalleryId!;
        Assert.True((await h.RunAsync<GalleryService, OperationResult>(s => s.AddAsync(gid, [img.Id, other.Id]))).IsOk);
        var second = await h.RunAsync<GalleryService, OperationResult>(s => s.CreateAsync("Second"));
        await h.SettleAsync();
        return new Ctx(h, gid, second.GalleryId!, img.Id, other.Id, loose.Id);
    }

    private static string PathOf(Ctx c, string route) =>
        Routes[route].Path.Replace("{gid}", c.Gid).Replace("{iid}", c.Iid);

    /// <summary>A body that WOULD change data if a gate let it through.</summary>
    private static object BodyOf(Ctx c, string route) => route switch
    {
        "A1" => new { title = "Hacked", caption = "Hacked" },
        "A2" => new { ids = new[] { c.Iid } },
        "A3" => new { title = "Hacked", ids = new[] { c.Loose } },
        "A4" => new { order = new[] { c.Gid2, c.Gid } },
        "A5" => new { title = "Hacked", description = "Hacked" },
        "A6" => new { @public = true },
        "A7" => new { ids = new[] { c.Loose } },
        "A8" => new { ids = new[] { c.Iid } },
        "A9" => new { order = new[] { c.Other, c.Iid } },
        "A10" => new { image = c.Other },
        _ => new { },
    };

    private static HttpRequestMessage Json(Ctx c, string route, string? token)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, PathOf(c, route))
        {
            Content = new StringContent(JsonSerializer.Serialize(BodyOf(c, route), new JsonSerializerOptions(JsonSerializerDefaults.Web)),
                Encoding.UTF8, "application/json"),
        };
        if (token is not null) req.Headers.TryAddWithoutValidation("X-CSRF-TOKEN", token);
        return req;
    }

    private static void AssertAdminHeaders(HttpResponseMessage resp, string route)
    {
        Assert.True(resp.Headers.CacheControl?.NoStore == true,
            $"{route} answered {(int)resp.StatusCode} without Cache-Control: no-store ({resp.Headers.CacheControl})");
        Assert.True(resp.Headers.TryGetValues("X-Robots-Tag", out var robots) && robots.Contains("noindex"),
            $"{route} answered {(int)resp.StatusCode} without X-Robots-Tag: noindex");
    }

    private static async Task<string> DumpAsync(LibraryHost h)
    {
        var parts = new List<string>();
        foreach (var name in new[] { "images", "galleries", "site" })
        {
            var docs = await h.Db.GetCollection<BsonDocument>(name).Find(FilterDefinition<BsonDocument>.Empty)
                .Sort(Builders<BsonDocument>.Sort.Ascending("_id")).ToListAsync();
            parts.Add(name + ":" + string.Join("\n", docs.Select(d => d.ToJson())));
        }
        return string.Join("\n---\n", parts);
    }

    [Theory]
    [MemberData(nameof(Enabled))]
    public async Task Without_a_session_POST_is_401_even_with_the_token(string route)
    {
        var c = await ArrangeAsync();
        await using var _ = c.Host;
        c.Host.Host.ClearCookies();
        Assert.Null(c.Host.Host.SessionCookie);
        var before = await DumpAsync(c.Host);

        var resp = await c.Host.Host.SendAsync(Json(c, route, c.Host.CsrfToken));
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
        AssertAdminHeaders(resp, route);

        await c.Host.EdgeIdleAsync();
        Assert.Equal(before, await DumpAsync(c.Host));
        Assert.Empty(c.Host.Recorder.AllPurged());
    }

    [Theory]
    [MemberData(nameof(Enabled))]
    public async Task With_a_session_and_no_antiforgery_token_every_POST_is_400_and_stores_nothing(string route)
    {
        var c = await ArrangeAsync();
        await using var _ = c.Host;
        var before = await DumpAsync(c.Host);

        var json = await c.Host.Host.SendAsync(Json(c, route, token: null));
        Assert.Equal(HttpStatusCode.BadRequest, json.StatusCode);
        AssertAdminHeaders(json, route);

        var bare = await c.Host.PostBareAsync(PathOf(c, route));
        Assert.Equal(HttpStatusCode.BadRequest, bare.StatusCode);
        AssertAdminHeaders(bare, route);

        var wrong = await c.Host.Host.SendAsync(Json(c, route, token: "not-the-token"));
        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
        AssertAdminHeaders(wrong, route);

        await c.Host.EdgeIdleAsync();
        Assert.Equal(before, await DumpAsync(c.Host));
        Assert.Empty(c.Host.Recorder.AllPurged());
    }

    /// <summary>
    /// C-14: a POST with the CSRF header and no body reaches the handler, whose
    /// <c>UploadEndpoints.ReadAsync</c> answers 400 <c>bad-request</c> (never a 500, never a write).
    /// </summary>
    [Theory]
    [MemberData(nameof(Enabled))]
    public async Task A_bare_POST_with_the_CSRF_header_is_400_bad_request_never_a_500(string route)
    {
        var c = await ArrangeAsync();
        await using var _ = c.Host;
        var before = await DumpAsync(c.Host);

        var resp = await c.Host.PostBareAsync(PathOf(c, route), csrfHeader: true);

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        AssertAdminHeaders(resp, route);
        var body = JsonNode.Parse(await resp.Content.ReadAsStringAsync())!;
        var want = JsonNode.Parse("{\"error\":\"bad-request\",\"message\":\"The request body is not valid JSON.\"}")!;
        Assert.True(JsonNode.DeepEquals(want, body), $"{route}: {body.ToJsonString()}");

        await c.Host.EdgeIdleAsync();
        Assert.Equal(before, await DumpAsync(c.Host));
        Assert.Empty(c.Host.Recorder.AllPurged());
    }

    // ── 020 T035: W1–W4 (pages, fragments, tile) [SAFETY I.3] ─────────────────

    /// <summary>W route id → path template. {gid}/{iid} are replaced with the host's real ids.</summary>
    private static readonly Dictionary<string, string> Pages = new()
    {
        ["W1"] = "/admin",
        ["W2"] = "/admin/unplaced",
        ["W3"] = "/admin/g/{gid}",
        ["W4"] = "/admin/tiles/{iid}",
    };

    [Fact]
    public void The_page_table_is_W1_to_W4()
    {
        Assert.Equal(["W1", "W2", "W3", "W4"], Pages.Keys.ToList());
    }

    public static TheoryData<string> PageRoutes() => new(Pages.Keys);

    private static string PagePath(Ctx c, string route) =>
        Pages[route].Replace("{gid}", c.Gid).Replace("{iid}", c.Iid);

    /// <summary>
    /// Without a session every W-route, by GET and HEAD and with each <c>X-Workspace</c> value, answers
    /// 302 to <c>/admin/signin</c> with no workspace markup, no photo id and no read of the library.
    /// </summary>
    [Theory]
    [MemberData(nameof(PageRoutes))]
    public async Task Without_a_session_W_routes_redirect_to_signin_and_show_nothing(string route)
    {
        var c = await ArrangeAsync();
        await using var _ = c.Host;
        c.Host.Host.ClearCookies();
        Assert.Null(c.Host.Host.SessionCookie);

        foreach (var method in new[] { HttpMethod.Get, HttpMethod.Head })
            foreach (var part in new string?[] { null, "main", "chrome", "more" })
            {
                int before;
                lock (c.Host.LogLines) before = c.Host.LogLines.Count;
                var req = new HttpRequestMessage(method, PagePath(c, route) + (part == "more" ? "?before=1.x" : ""));
                if (part is not null) req.Headers.TryAddWithoutValidation("X-Workspace", part);
                var resp = await c.Host.Host.SendAsync(req);

                Assert.True(resp.StatusCode == HttpStatusCode.Found, $"{method} {route} [{part}] answered {(int)resp.StatusCode}");
                var loc = resp.Headers.Location!;
                Assert.StartsWith("/admin/signin", loc.IsAbsoluteUri ? loc.PathAndQuery : loc.OriginalString);
                AssertAdminHeaders(resp, route);
                var body = await resp.Content.ReadAsStringAsync();
                Assert.DoesNotContain("<admin-workspace", body);
                Assert.DoesNotContain("ws-", body);
                Assert.DoesNotContain(c.Iid, body);
                await Task.Delay(20);
                lock (c.Host.LogLines)
                    Assert.DoesNotContain(c.Host.LogLines.Skip(before).SelectMany(l => l.Split('\n')),
                        l => l.StartsWith("op.timing db.library.", StringComparison.Ordinal));
            }
    }

    /// <summary>With the owner session every W-route answers 200 with the admin headers.</summary>
    [Theory]
    [MemberData(nameof(PageRoutes))]
    public async Task With_a_session_W_routes_answer_200_with_the_admin_headers(string route)
    {
        var c = await ArrangeAsync();
        await using var _ = c.Host;

        var resp = await c.Host.GetAsync(PagePath(c, route));
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        AssertAdminHeaders(resp, route);
        Assert.Contains($"data-id=\"{(route == "W2" ? c.Loose : c.Iid)}\"", await resp.Content.ReadAsStringAsync());
    }

    /// <summary>W-routes are read-only: a POST with the session and token reaches no handler (404), changes nothing.</summary>
    [Theory]
    [MemberData(nameof(PageRoutes))]
    public async Task W_routes_take_no_POST(string route)
    {
        var c = await ArrangeAsync();
        await using var _ = c.Host;
        var before = await DumpAsync(c.Host);

        var resp = await c.Host.PostBareAsync(PagePath(c, route), csrfHeader: true);
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
        AssertAdminHeaders(resp, route);

        await c.Host.EdgeIdleAsync();
        Assert.Equal(before, await DumpAsync(c.Host));
        Assert.Empty(c.Host.Recorder.AllPurged());
    }
}
