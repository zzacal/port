using System.Net;
using Gallery.Web.Library;
using Gallery.Web.Tests.Fixtures;

namespace Gallery.Web.Tests;

/// <summary>
/// 015 T004 timing evidence (SC-005, R-09). One <see cref="LibraryHost"/> holds 100 photos and one
/// gallery "Coast" holding 40 of them. It runs 100 GETs of <c>/admin/library</c> and 100 GETs of the
/// Coast builder, asserts at least 100 <c>http.request GET</c> lines for each route template, and
/// writes the host's log messages, one per line in log order, to the path in env
/// <c>TILE_TIMING_LOG</c> (default specs/015-tile-icon-controls/evidence/tile-timings.log), which
/// evidence/p95.sh compares against the pre-change capture (pre-timings.log).
/// </summary>
[Collection("s3")]
[Trait("Category", "Evidence")]
public class TileTimingEvidenceTests(S3ProxyFixture s3, MongoFixture mongo) : IClassFixture<MongoFixture>
{
    private const int Gets = 100;
    private const string LibraryLine = "http.request GET /admin/library 200 ";
    private const string BuilderLine = "http.request GET /admin/galleries/{gid} 200 ";

    private static string[] Messages(LibraryHost h)
    {
        lock (h.LogLines) return h.LogLines.SelectMany(l => l.Split('\n')).ToArray();
    }

    [Fact]
    public async Task Library_and_builder_GETs_are_logged_for_the_p95_script()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3);
        var t0 = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc);

        var photos = new List<string>();
        for (var i = 0; i < 100; i++)
            photos.Add((await h.InsertUploadedImageAsync(t0.AddMinutes(i), title: $"Photo {i:000}", files: false)).Id);

        var coast = await h.RunAsync<GalleryService, OperationResult>(s => s.CreateAsync("Coast"));
        Assert.True(coast.IsOk);
        var gid = coast.GalleryId!;
        Assert.True((await h.RunAsync<GalleryService, OperationResult>(s => s.AddAsync(gid, photos.Take(40).ToList()))).IsOk);
        await h.SettleAsync();

        for (var i = 0; i < Gets; i++)
        {
            var lib = await h.GetAsync("/admin/library");
            Assert.Equal(HttpStatusCode.OK, lib.StatusCode);
            _ = await lib.Content.ReadAsStringAsync();
            var b = await h.GetAsync($"/admin/galleries/{gid}");
            Assert.Equal(HttpStatusCode.OK, b.StatusCode);
            _ = await b.Content.ReadAsStringAsync();
        }

        var lines = Messages(h);
        var libs = lines.Count(l => l.StartsWith(LibraryLine, StringComparison.Ordinal));
        Assert.True(libs >= Gets, $"Only {libs} '{LibraryLine.Trim()}' lines (need {Gets}).");
        var builders = lines.Count(l => l.StartsWith(BuilderLine, StringComparison.Ordinal));
        Assert.True(builders >= Gets,
            $"Only {builders} '{BuilderLine.Trim()}' lines (need {Gets}). GET lines seen: "
            + string.Join(" | ", lines.Where(l => l.StartsWith("http.request GET ", StringComparison.Ordinal)).Distinct().Take(5)));

        var path = Environment.GetEnvironmentVariable("TILE_TIMING_LOG") is { Length: > 0 } env
            ? (Path.IsPathRooted(env) ? env : Path.Combine(Render.RepoRoot, env))
            : Path.Combine(Render.RepoRoot, "specs", "015-tile-icon-controls", "evidence", "tile-timings.log");
        await File.WriteAllLinesAsync(path, lines);
    }
}
