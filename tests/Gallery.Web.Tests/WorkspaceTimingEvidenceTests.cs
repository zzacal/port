using System.Net;
using System.Text.Json;
using Gallery.Web.Library;
using Gallery.Web.Tests.Fixtures;
using static Gallery.Web.Tests.Fixtures.UploadFixtures;

namespace Gallery.Web.Tests;

/// <summary>
/// 020 T059 timing evidence (SC-005, SC-006, SC-013; evidence/p95.sh). Part 1, one
/// <see cref="LibraryHost"/> holding 100 photos and 3 galleries, one ("Coast") holding 30: 100 GETs
/// each of W1 full, W1 <c>X-Workspace: main</c>, W3 full, W3 <c>main</c> and W4; then 30 POSTs each
/// of A1, A5, A7 (one id), A8 (that id, re-added by the next A7, so every A8 removes it), A9 and
/// A10, all answering 200. Part 2, one <see cref="UploadHost"/>: ten photos registered and staged
/// with <see cref="UploadFixtures"/>, one forced to fail validation (a GPS AVIF, U7 answers 422),
/// nine U7 + U12; the line <c>evidence.forced-failure {item}</c> names the forced item. Both hosts'
/// log messages, one per line in log order (library host first), go to the path in env
/// <c>WORKSPACE_TIMING_LOG</c> (default specs/020-admin-workspace/evidence/workspace-timings.log).
/// </summary>
[Collection("s3")]
[Trait("Category", "Evidence")]
public class WorkspaceTimingEvidenceTests(S3ProxyFixture s3, MongoFixture mongo) : IClassFixture<MongoFixture>, IAsyncLifetime
{
    private const int Gets = 100;
    private const int Posts = 30;
    private const int UploadPhotos = 10;
    private const int Joins = 9;
    private const int ForcedIndex = 4;
    private const string Color = "#a1b2c3";

    private readonly FakeGoogle _google = new();

    public Task InitializeAsync() => _google.InitializeAsync();
    public Task DisposeAsync() => _google.DisposeAsync();

    private static string[] Messages(List<string> logLines)
    {
        lock (logLines) return logLines.SelectMany(l => l.Split('\n')).ToArray();
    }

    /// <summary>The request line split: method, route, status, duration_ms, db_reads.</summary>
    private sealed record Req(string Method, string Route, string Status, string Reads);

    private static List<Req> Requests(IEnumerable<string> lines) =>
        lines.Where(l => l.StartsWith("http.request ", StringComparison.Ordinal))
            .Select(l => l.Split(' '))
            .Where(f => f.Length >= 6)
            .Select(f => new Req(f[1], f[2], f[3], f[5]))
            .ToList();

    private static async Task<HttpResponseMessage> OkAsync(Task<HttpResponseMessage> call, string what)
    {
        var resp = await call;
        var body = await resp.Content.ReadAsStringAsync();
        Assert.True(resp.StatusCode == HttpStatusCode.OK, $"{what} answered {(int)resp.StatusCode}: {body}");
        return resp;
    }

    private static Task<HttpResponseMessage> GetMainAsync(LibraryHost h, string path)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.TryAddWithoutValidation("X-Workspace", "main");
        return h.Host.SendAsync(request);
    }

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage resp) =>
        JsonDocument.Parse(await resp.Content.ReadAsStringAsync()).RootElement.Clone();

    [Fact]
    public async Task Workspace_reads_writes_and_joins_are_logged_for_the_p95_script()
    {
        var lines = new List<string>();
        lines.AddRange(await LibraryPartAsync());
        lines.AddRange(await UploadPartAsync());

        // Written before the asserts so a failing run still leaves the log to inspect.
        var path = Environment.GetEnvironmentVariable("WORKSPACE_TIMING_LOG") is { Length: > 0 } env
            ? (Path.IsPathRooted(env) ? env : Path.Combine(Render.RepoRoot, env))
            : Path.Combine(Render.RepoRoot, "specs", "020-admin-workspace", "evidence", "workspace-timings.log");
        await File.WriteAllLinesAsync(path, lines);

        // ── the counts p95.sh needs ──────────────────────────────────────────
        var reqs = Requests(lines);
        int Count(string route, string reads, string method = "GET") =>
            reqs.Count(r => r.Method == method && r.Route == route && r.Status == "200" && r.Reads == reads);
        int W1(string reads) => Count("/admin", reads) + Count("/admin/", reads);

        Assert.True(W1("4") >= Gets, $"W1 full (reads 4): {W1("4")} lines, need {Gets}.");
        Assert.True(W1("2") >= Gets, $"W1 main (reads 2): {W1("2")} lines, need {Gets}.");
        Assert.Equal(Gets, Count("/admin/g/{gid}", "4"));
        Assert.Equal(Gets, Count("/admin/g/{gid}", "1"));
        Assert.Equal(Gets, Count("/admin/tiles/{iid}", "1"));
        Assert.All(reqs.Where(r => r.Route == "/admin/tiles/{iid}" && r.Status == "200"), r => Assert.Equal("1", r.Reads));
        Assert.All(reqs.Where(r => r.Route == "/admin/g/{gid}" && r.Status == "200"), r => Assert.Contains(r.Reads, new[] { "4", "1" }));
        Assert.All(reqs.Where(r => (r.Route == "/admin" || r.Route == "/admin/") && r.Status == "200"),
            r => Assert.Contains(r.Reads, new[] { "4", "2", "1" }));

        foreach (var route in new[]
                 {
                     "/admin/api/photos/{iid}", "/admin/api/galleries/{gid}", "/admin/api/galleries/{gid}/photos",
                     "/admin/api/galleries/{gid}/photos/remove", "/admin/api/galleries/{gid}/order",
                     "/admin/api/galleries/{gid}/cover",
                 })
        {
            var n = reqs.Count(r => r.Method == "POST" && r.Route == route && r.Status == "200");
            Assert.True(n == Posts, $"POST {route} 200: {n} lines, expected {Posts}.");
        }

        var joined = lines.Where(l => l.StartsWith("upload.joined ", StringComparison.Ordinal)).ToList();
        Assert.Equal(Joins, joined.Count);
        var joinOk = lines.Count(l => l.Split(' ') is [ "op.timing", "upload.join", _, "ok", ..]);
        Assert.True(joinOk >= Joins, $"op.timing upload.join .. ok: {joinOk} lines, need {Joins}.");
        var forced = Assert.Single(lines, l => l.StartsWith("evidence.forced-failure ", StringComparison.Ordinal));
        var forcedItem = forced.Split(' ')[1];
        Assert.DoesNotContain(joined, l => l.Split(' ')[2] == forcedItem);

    }

    /// <summary>Part 1: W1, W3, W4 GETs and the A1/A5/A7/A8/A9/A10 POSTs; returns the host's log.</summary>
    private async Task<string[]> LibraryPartAsync()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3);
        var t0 = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc);

        var photos = new List<string>();
        for (var i = 0; i < 100; i++)
            photos.Add((await h.InsertUploadedImageAsync(t0.AddMinutes(i), title: $"Photo {i:000}", files: false)).Id);

        var gids = new List<string>();
        foreach (var title in new[] { "Coast", "Hills", "Town" })
        {
            var created = await h.RunAsync<GalleryService, OperationResult>(s => s.CreateAsync(title));
            Assert.True(created.IsOk);
            gids.Add(created.GalleryId!);
        }
        var gid = gids[0];
        var members = photos.Take(30).ToList();
        Assert.True((await h.RunAsync<GalleryService, OperationResult>(s => s.AddAsync(gid, members))).IsOk);
        Assert.True((await h.RunAsync<GalleryService, OperationResult>(s => s.AddAsync(gids[1], photos.Skip(30).Take(5).ToList()))).IsOk);
        await h.SettleAsync();

        // ── reads ────────────────────────────────────────────────────────────
        var tile = photos[50];
        for (var i = 0; i < Gets; i++)
        {
            await OkAsync(h.GetAsync("/admin"), "W1 full");
            await OkAsync(GetMainAsync(h, "/admin"), "W1 main");
            await OkAsync(h.GetAsync($"/admin/g/{gid}"), "W3 full");
            await OkAsync(GetMainAsync(h, $"/admin/g/{gid}"), "W3 main");
            await OkAsync(h.GetAsync($"/admin/tiles/{tile}"), "W4");
        }

        // ── writes ───────────────────────────────────────────────────────────
        var outsider = photos[60]; // A7/A8 move this one id in and out of Coast
        var forward = members.ToArray();
        var reversed = members.AsEnumerable().Reverse().ToArray();
        for (var i = 0; i < Posts; i++)
        {
            await OkAsync(h.PostJsonAsync($"/admin/api/photos/{photos[70]}", new { title = $"Edited {i}", caption = $"Caption {i}" }), "A1");
            await OkAsync(h.PostJsonAsync($"/admin/api/galleries/{gid}", new { title = i % 2 == 0 ? "Coast" : "Coastline", description = $"Description {i}" }), "A5");

            var add = await JsonAsync(await OkAsync(h.PostJsonAsync($"/admin/api/galleries/{gid}/photos", new { ids = new[] { outsider } }), "A7"));
            Assert.Equal(1, add.GetProperty("added").GetInt32());
            var remove = await JsonAsync(await OkAsync(h.PostJsonAsync($"/admin/api/galleries/{gid}/photos/remove", new { ids = new[] { outsider } }), "A8"));
            Assert.Equal(1, remove.GetProperty("removed").GetInt32());

            await OkAsync(h.PostJsonAsync($"/admin/api/galleries/{gid}/order", new { order = i % 2 == 0 ? reversed : forward }), "A9");
            await OkAsync(h.PostJsonAsync($"/admin/api/galleries/{gid}/cover", new { image = members[i % members.Count] }), "A10");
        }
        await h.EdgeIdleAsync();
        return Messages(h.LogLines);
    }

    private sealed record Photo(string Name, string Master, int W, string Avif, string Webp, string Placeholder);

    // Same fixtures as UploadJoinTests: gps-camera.jpg → [320], orientation-6.jpg → [200].
    private static readonly Photo PhotoA = new("IMG_0001.jpg", "gps-camera.jpg", 320, "gps-320.avif", "gps-320.webp", "gps-placeholder.webp");
    private static readonly Photo PhotoB = new("Harbour at dusk.jpg", "orientation-6.jpg", 200, "orient6-200.avif", "orient6-200.webp", "orient6-placeholder.webp");

    /// <summary>Part 2: ten staged photos, one forced to fail U7 validation, nine U7 + U12.</summary>
    private async Task<string[]> UploadPartAsync()
    {
        await using var host = await UploadHost.CreateAsync(mongo, _google, s3);

        // Register all ten (U3), then stage each: PUT master, U4, PUT both sizes.
        var specs = Enumerable.Range(0, UploadPhotos).Select(i => i % 2 == 0 ? PhotoA : PhotoB).ToList();
        var reg = await OkAsync(host.PostJsonAsync("/admin/upload/items", new
        {
            files = specs.Select((p, i) => new { name = $"{i:00} {p.Name}", size = Bytes(p.Master).LongLength, type = "image/jpeg" }).ToArray(),
        }), "U3");
        var accepted = (await JsonAsync(reg)).GetProperty("accepted").EnumerateArray().ToList();
        Assert.Equal(UploadPhotos, accepted.Count);

        var staged = new List<(string ItemId, string AttemptId, Photo Photo)>();
        for (var i = 0; i < UploadPhotos; i++)
        {
            var p = specs[i];
            var a = accepted[i];
            var itemId = a.GetProperty("itemId").GetString()!;
            var attemptId = a.GetProperty("attemptId").GetString()!;
            Assert.Equal(HttpStatusCode.OK,
                (await UploadHost.PutToPresignedAsync(a.GetProperty("master").GetProperty("url").GetString()!, Bytes(p.Master), "image/jpeg")).StatusCode);
            var check = await OkAsync(host.PostJsonAsync($"/admin/upload/items/{itemId}/master", new { attemptId }), "U4");
            var size = Assert.Single((await JsonAsync(check)).GetProperty("sizes").EnumerateArray());
            Assert.Equal(p.W, size.GetProperty("w").GetInt32());
            // The forced item gets a GPS-carrying AVIF (an Exif item), so U7 rejects it.
            var avif = i == ForcedIndex ? AvifWithItem("Exif", Bytes(p.Avif)) : Bytes(p.Avif);
            Assert.Equal(HttpStatusCode.OK,
                (await UploadHost.PutToPresignedAsync(size.GetProperty("avif").GetProperty("url").GetString()!, avif, "image/avif")).StatusCode);
            Assert.Equal(HttpStatusCode.OK,
                (await UploadHost.PutToPresignedAsync(size.GetProperty("webp").GetProperty("url").GetString()!, Bytes(p.Webp), "image/webp")).StatusCode);
            staged.Add((itemId, attemptId, p));
        }

        // U7 for all ten (the forced one answers 422 rejected), U12 for the nine that validated.
        string? forced = null;
        for (var i = 0; i < UploadPhotos; i++)
        {
            var (itemId, attemptId, p) = staged[i];
            var done = await host.PostJsonAsync($"/admin/upload/items/{itemId}/complete", new
            {
                attemptId,
                placeholder = new { color = Color, preview = DataUri(Bytes(p.Placeholder)) },
                timings = Array.Empty<object>(),
            });
            if (i == ForcedIndex)
            {
                Assert.Equal(HttpStatusCode.UnprocessableEntity, done.StatusCode);
                Assert.Equal("rejected", (await JsonAsync(done)).GetProperty("error").GetString());
                forced = itemId;
                continue;
            }
            Assert.Equal(HttpStatusCode.OK, done.StatusCode);
            var join = await OkAsync(host.PostJsonAsync($"/admin/upload/items/{itemId}/join", new { attemptId }), "U12");
            Assert.Equal(itemId, (await JsonAsync(join)).GetProperty("imageId").GetString());
        }
        Assert.NotNull(forced);

        return [.. Messages(host.LogLines), $"evidence.forced-failure {forced}"];
    }
}
