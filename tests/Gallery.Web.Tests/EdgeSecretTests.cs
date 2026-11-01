using System.Net;
using Gallery.Web.Edge;
using Gallery.Web.Tests.Fixtures;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http;
using MongoDB.Driver;

namespace Gallery.Web.Tests;

/// <summary>
/// 009 T054 [SAFETY] (SC-011, FR-043; contracts/edge.md "Credentials"): a dev-zone host
/// (<c>Edge:ZoneId</c> and <c>Edge:ApiToken</c> set) whose named edge <c>HttpClient</c>s talk to a
/// recording fake instead of Cloudflare. Every owner page (L1, L1 <c>?open=</c>, G1, B1) and a
/// publish, rename and delete run; the token appears in no response body, no response header and no
/// captured log line. Non-vacuity: the fake saw it in the <c>Authorization</c> header. Failure
/// messages name the route or event, never the value.
/// </summary>
[Collection("s3")]
public class EdgeSecretTests(S3ProxyFixture s3, MongoFixture mongo) : IClassFixture<MongoFixture>
{
    // A test literal only (tasks.md "Secrets"): never a real credential.
    private const string Token = "test-edge-token-0123456789abcdef";
    private const string Zone = "zone-test";

    private static KeyValuePair<string, string> F(string k, string v) => new(k, v);

    /// <summary>Answers every request <c>200 {"success":true}</c> and records the URL and Authorization header.</summary>
    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly object _lock = new();
        private readonly List<(string Url, string? Authorization)> _requests = [];

        public IReadOnlyList<(string Url, string? Authorization)> Requests { get { lock (_lock) return _requests.ToList(); } }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            lock (_lock) _requests.Add((request.RequestUri!.ToString(), request.Headers.Authorization?.ToString()));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"success\":true,\"errors\":[]}"),
            });
        }
    }

    [Fact]
    public async Task The_edge_token_appears_in_no_response_and_no_log_line_but_is_sent_to_the_purge_api()
    {
        var fake = new RecordingHandler();
        await using var h = await LibraryHost.CreateAsync(mongo, s3,
            extraOverrides: new() { ["Edge:ZoneId"] = Zone, ["Edge:ApiToken"] = Token },
            configure: b =>
            {
                // Drop LibraryHost's recorder so AddEdge picks the dev-zone provider from configuration.
                b.Services.RemoveAll<IEdgeProvider>();
                // PostConfigure runs after AddEdge's own handler setup, so both named clients get the fake.
                foreach (var name in new[] { CloudflareEdgeProvider.ApiClientName, CloudflareEdgeProvider.WarmClientName })
                    b.Services.PostConfigure<HttpClientFactoryOptions>(name, o =>
                        o.HttpMessageHandlerBuilderActions.Add(hb => hb.PrimaryHandler = fake));
            });
        Assert.IsType<CloudflareEdgeProvider>(h.Services.GetRequiredService<IEdgeProvider>());

        var scanned = new List<(string Label, HttpResponseMessage Response, string Body)>();
        async Task<HttpResponseMessage> Scan(string label, Task<HttpResponseMessage> call)
        {
            var resp = await call;
            scanned.Add((label, resp, await resp.Content.ReadAsStringAsync()));
            return resp;
        }

        var t0 = new DateTime(2026, 3, 3, 0, 0, 0, DateTimeKind.Utc);
        var a = await h.InsertUploadedImageAsync(t0, "Pier");
        var b = await h.InsertUploadedImageAsync(t0.AddMinutes(1), "Rocks");

        var add = await Scan("POST /admin/library/add (L3)", h.PostFormAsync("/admin/library/add",
            [F("ids", a.Id), F("ids", b.Id), F("gallery", "new"), F("newTitle", "Coast"), F("page", "1")]));
        Assert.Equal(HttpStatusCode.SeeOther, add.StatusCode);
        var loc = add.Headers.Location!.ToString();
        var gid = loc["/admin/galleries/".Length..loc.IndexOf('?')];

        foreach (var (label, path) in new[]
                 {
                     ("GET /admin/library (L1)", "/admin/library"),
                     ("GET /admin/library?open= (L1)", $"/admin/library?page=1&open={a.Id}"),
                     ("GET /admin/galleries (G1)", "/admin/galleries"),
                     ("GET /admin/galleries/{gid} (B1)", $"/admin/galleries/{gid}"),
                 })
        {
            var resp = await Scan(label, h.GetAsync(path));
            Assert.True(resp.StatusCode == HttpStatusCode.OK, $"{label} answered {(int)resp.StatusCode}");
        }

        var warn = await Scan("POST /admin/galleries/{gid}/visibility (G4 warning)", h.PostFormAsync(
            $"/admin/galleries/{gid}/visibility", [F("public", "on"), F("from", "builder")]));
        Assert.Equal(HttpStatusCode.OK, warn.StatusCode);
        var publish = await Scan("POST /admin/galleries/{gid}/visibility (G4 publish)", h.PostFormAsync(
            $"/admin/galleries/{gid}/visibility", [F("public", "on"), F("confirm", "1"), F("from", "builder")]));
        Assert.Equal(HttpStatusCode.SeeOther, publish.StatusCode);
        var rename = await Scan("POST /admin/galleries/{gid} (B2 rename)", h.PostFormAsync(
            $"/admin/galleries/{gid}", [F("title", "Coast Path")]));
        Assert.Equal(HttpStatusCode.SeeOther, rename.StatusCode);
        var delete = await Scan("POST /admin/library/images/{iid}/delete (L5)", h.PostFormAsync(
            $"/admin/library/images/{b.Id}/delete", [F("page", "1")]));
        Assert.Equal(HttpStatusCode.SeeOther, delete.StatusCode);
        var gdelete = await Scan("POST /admin/galleries/{gid}/delete (B6)", h.PostFormAsync(
            $"/admin/galleries/{gid}/delete", []));
        Assert.Equal(HttpStatusCode.SeeOther, gdelete.StatusCode);

        // The pages after the writes (the PRG targets) are owner pages too.
        foreach (var (label, path) in new[]
                 {
                     ("GET /admin/library (L1, after)", "/admin/library?page=1&deleted=1&leftovers=0"),
                     ("GET /admin/galleries (G1, after)", "/admin/galleries?deleted=1"),
                 })
            Assert.Equal(HttpStatusCode.OK, (await Scan(label, h.GetAsync(path))).StatusCode);

        await h.EdgeIdleAsync();
        await InvariantChecker.AssertAsync(h.Db);
        Assert.Equal(0, await h.Galleries.CountDocumentsAsync(FilterDefinition<Data.GalleryDoc>.Empty));

        // ── no response carries the token ────────────────────────────────────
        Assert.Equal(12, scanned.Count);
        foreach (var (label, resp, body) in scanned)
        {
            Assert.False(body.Contains(Token, StringComparison.Ordinal), $"{label}: the response body contains Edge:ApiToken");
            foreach (var (name, values) in resp.Headers.Concat(resp.Content.Headers))
                Assert.False(values.Any(v => v.Contains(Token, StringComparison.Ordinal)),
                    $"{label}: response header {name} contains Edge:ApiToken");
        }

        // ── no log line carries the token ────────────────────────────────────
        List<string> lines;
        lock (h.LogLines) lines = h.LogLines.ToList();
        Assert.NotEmpty(lines);
        for (var i = 0; i < lines.Count; i++)
        {
            if (!lines[i].Contains(Token, StringComparison.Ordinal)) continue;
            var evt = lines[i].Split(' ', 2)[0];
            Assert.Fail($"log line {i} (event {evt}) contains Edge:ApiToken");
        }

        // ── non-vacuity: the dev-zone purge really ran and sent the token in the header ──
        var requests = fake.Requests;
        var purgeUrl = CloudflareEdgeProvider.ApiBase + Zone + "/purge_cache";
        var purges = requests.Where(r => r.Url == purgeUrl).ToList();
        Assert.True(purges.Count >= 1, $"the fake saw {purges.Count} purge requests");
        Assert.True(purges.All(r => r.Authorization == "Bearer " + Token),
            "a purge request did not carry the configured token as its Bearer Authorization header");
        Assert.True(requests.Where(r => r.Url != purgeUrl).All(r => r.Authorization is null),
            "a warm request carried an Authorization header");
        Assert.True(requests.All(r => !r.Url.Contains(Token, StringComparison.Ordinal)), "an outbound URL contains Edge:ApiToken");
        Assert.Contains(lines, l => l.StartsWith("edge.purge gallery.publish dev-zone ", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.StartsWith("op.timing edge.purge.call ", StringComparison.Ordinal) && l.EndsWith(" ok", StringComparison.Ordinal));
    }
}
