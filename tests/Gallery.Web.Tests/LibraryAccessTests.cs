using System.Net;
using Gallery.Web.Library;
using Gallery.Web.Tests.Fixtures;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Gallery.Web.Tests;

/// <summary>
/// 009 T035 [SAFETY] (FR-001, I.3, I.6; contracts/http.md): every 009 owner route sits behind the
/// owner session and, for POSTs, the antiforgery token. Each case runs on its own host holding a real
/// gallery with one photo, so a POST that slipped past the gates would actually change something:
/// <list type="bullet">
///   <item>no session: GET → 302 <c>/admin/signin</c>, POST → 401;</item>
///   <item>session but NO antiforgery token: every POST → 400, and a dump of <c>images</c>,
///     <c>galleries</c> and <c>site</c> is identical before and after;</item>
///   <item>every response carries <c>Cache-Control: no-store</c> and <c>X-Robots-Tag: noindex</c>.</item>
/// </list>
/// G1 and B1 (the gallery pages) were enabled by Batch E (T040).
/// </summary>
[Collection("s3")]
public class LibraryAccessTests(S3ProxyFixture s3, MongoFixture mongo) : IClassFixture<MongoFixture>
{
    /// <summary>Route id → (method, path template). {gid}/{iid} are replaced with the host's real ids.</summary>
    private static readonly Dictionary<string, (string Method, string Path)> Routes = new()
    {
        ["L1"] = ("GET", "/admin/library"),
        ["L2"] = ("POST", "/admin/library/images/{iid}"),
        ["L3"] = ("POST", "/admin/library/add"),
        ["L4"] = ("POST", "/admin/library/delete"),
        ["L5"] = ("POST", "/admin/library/images/{iid}/delete"),
        ["G1"] = ("GET", "/admin/galleries"),
        ["G2"] = ("POST", "/admin/galleries"),
        ["G3"] = ("POST", "/admin/galleries/order"),
        ["G4"] = ("POST", "/admin/galleries/{gid}/visibility"),
        ["B1"] = ("GET", "/admin/galleries/{gid}"),
        ["B2"] = ("POST", "/admin/galleries/{gid}"),
        ["B3"] = ("POST", "/admin/galleries/{gid}/order"),
        ["B4"] = ("POST", "/admin/galleries/{gid}/cover"),
        ["B5"] = ("POST", "/admin/galleries/{gid}/remove"),
        ["B6"] = ("POST", "/admin/galleries/{gid}/delete"),
    };

    [Fact]
    public void The_route_table_is_the_15_routes_of_the_contract()
    {
        Assert.Equal(15, Routes.Count);
        Assert.Equal(["L1", "L2", "L3", "L4", "L5", "G1", "G2", "G3", "G4", "B1", "B2", "B3", "B4", "B5", "B6"], Routes.Keys.ToList());
    }

    public static TheoryData<string> Enabled() => new(Routes.Keys);

    private sealed record Ctx(LibraryHost Host, string Gid, string Iid);

    private async Task<Ctx> ArrangeAsync()
    {
        var h = await LibraryHost.CreateAsync(mongo, s3);
        var img = await h.InsertUploadedImageAsync(files: false, title: "Pier");
        var other = await h.InsertUploadedImageAsync(files: false, title: "Rocks");
        var created = await h.RunAsync<GalleryService, OperationResult>(s => s.CreateAsync("Coast"));
        var gid = created.GalleryId!;
        Assert.True((await h.RunAsync<GalleryService, OperationResult>(s => s.AddAsync(gid, [img.Id, other.Id]))).IsOk);
        await h.SettleAsync();
        return new Ctx(h, gid, img.Id);
    }

    private static string PathOf(Ctx c, string route) =>
        Routes[route].Path.Replace("{gid}", c.Gid).Replace("{iid}", c.Iid);

    /// <summary>A form that WOULD change data if a gate let it through (no antiforgery field).</summary>
    private static FormUrlEncodedContent BodyOf(Ctx c, string route) => new(Fields(c, route));

    private static IEnumerable<KeyValuePair<string, string>> Fields(Ctx c, string route) => route switch
    {
        "L2" => [new("title", "Hacked"), new("caption", "Hacked")],
        "L3" => [new("ids", c.Iid), new("gallery", "new"), new("newTitle", "Hacked")],
        "L4" => [new("ids", c.Iid)],
        "G2" => [new("title", "Hacked")],
        "G3" => [new("order", c.Gid)],
        "G4" => [new("public", "on"), new("from", "list")],
        "B2" => [new("title", "Hacked"), new("description", "Hacked")],
        "B4" => [new("image", c.Iid)],
        "B5" => [new("image", c.Iid)],
        _ => [new("page", "1")],
    };

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
    public async Task Without_a_session_GET_redirects_to_signin_and_POST_is_401(string route)
    {
        var c = await ArrangeAsync();
        await using var _ = c.Host;
        c.Host.Host.ClearCookies();
        Assert.Null(c.Host.Host.SessionCookie);
        var before = await DumpAsync(c.Host);

        var (method, _) = Routes[route];
        HttpResponseMessage resp;
        if (method == "GET")
        {
            resp = await c.Host.GetAsync(PathOf(c, route));
            Assert.Equal(HttpStatusCode.Found, resp.StatusCode);
            Assert.StartsWith("/admin/signin", resp.Headers.Location?.ToString() ?? "");
            Assert.DoesNotContain("owner-bar", await resp.Content.ReadAsStringAsync());
        }
        else
        {
            // Even WITH a valid token in the form, no session is 401.
            var withToken = new FormUrlEncodedContent(Fields(c, route).Append(new("__RequestVerificationToken", c.Host.CsrfToken)));
            resp = await c.Host.Host.SendAsync(new HttpRequestMessage(HttpMethod.Post, PathOf(c, route)) { Content = withToken });
            Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
        }
        AssertAdminHeaders(resp, route);
        Assert.Equal(before, await DumpAsync(c.Host));
    }

    [Theory]
    [MemberData(nameof(Enabled))]
    public async Task With_a_session_and_no_antiforgery_token_every_POST_is_400_and_stores_nothing(string route)
    {
        var c = await ArrangeAsync();
        await using var _ = c.Host;
        var (method, _) = Routes[route];

        if (method == "GET")
        {
            var ok = await c.Host.GetAsync(PathOf(c, route));
            Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
            AssertAdminHeaders(ok, route);
            return;
        }

        var before = await DumpAsync(c.Host);

        var form = await c.Host.Host.SendAsync(new HttpRequestMessage(HttpMethod.Post, PathOf(c, route)) { Content = BodyOf(c, route) });
        Assert.Equal(HttpStatusCode.BadRequest, form.StatusCode);
        AssertAdminHeaders(form, route);

        var bare = await c.Host.PostBareAsync(PathOf(c, route));
        Assert.Equal(HttpStatusCode.BadRequest, bare.StatusCode);
        AssertAdminHeaders(bare, route);

        var wrong = await c.Host.Host.SendAsync(new HttpRequestMessage(HttpMethod.Post, PathOf(c, route))
        {
            Content = new FormUrlEncodedContent([new KeyValuePair<string, string>("__RequestVerificationToken", "not-the-token")]),
        });
        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);

        await c.Host.EdgeIdleAsync();
        Assert.Equal(before, await DumpAsync(c.Host));
        Assert.Empty(c.Host.Recorder.AllPurged());
    }

    public static TheoryData<string, int> BarePosts() => new()
    {
        { "L2", 303 }, { "L3", 422 }, { "L4", 422 }, { "L5", 303 },
        { "G2", 422 }, { "G3", 409 }, { "G4", 303 },
        { "B2", 303 }, { "B3", 409 }, { "B4", 303 }, { "B5", 303 }, { "B6", 303 },
    };

    /// <summary>
    /// The bare-POST class (contracts/http.md): a POST with no body and only the CSRF header is
    /// "no fields", never a 500 from <c>ReadFormAsync</c>. Each route answers its exact contracted
    /// status for "nothing sent".
    /// </summary>
    [Theory]
    [MemberData(nameof(BarePosts))]
    public async Task A_bare_POST_with_the_CSRF_header_is_no_fields_never_a_500(string route, int status)
    {
        var c = await ArrangeAsync();
        await using var _ = c.Host;

        var resp = await c.Host.PostBareAsync(PathOf(c, route), csrfHeader: true);

        Assert.Equal(status, (int)resp.StatusCode);
        AssertAdminHeaders(resp, route);
    }

    [Theory]
    [InlineData("G1")]
    [InlineData("B1")]
    public async Task Batch_E_pages_are_owner_only(string route)
    {
        var c = await ArrangeAsync();
        await using var _ = c.Host;
        var ok = await c.Host.GetAsync(PathOf(c, route));
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        AssertAdminHeaders(ok, route);
        c.Host.Host.ClearCookies();
        var anon = await c.Host.GetAsync(PathOf(c, route));
        Assert.Equal(HttpStatusCode.Found, anon.StatusCode);
        Assert.StartsWith("/admin/signin", anon.Headers.Location?.ToString() ?? "");
        AssertAdminHeaders(anon, route);
    }
}
