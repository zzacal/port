using System.Net;
using Gallery.Web.Library;
using Gallery.Web.Tests.Fixtures;

namespace Gallery.Web.Tests;

/// <summary>
/// 017 T017 timing evidence (SC-005, R-09), post-change capture. One <see cref="LibraryHost"/> holds
/// 100 uploaded images and 3 galleries holding the first 10 (the same data as T003's pre-change
/// capture of <c>/admin/library?page=1&amp;open={id}</c>, kept in evidence/pre-timings.log). It runs
/// 100 GETs of <c>/admin/library/images/{id}?page=1</c>, asserts at least 100
/// <c>http.request GET /admin/library/images/</c> lines, and writes the host's log messages, one per
/// line in log order, to the path in env <c>PHOTO_TIMING_LOG</c> (default
/// specs/017-photo-edit-screen/evidence/photo-timings.log), which evidence/p95.sh compares against
/// pre-timings.log.
/// </summary>
[Collection("s3")]
[Trait("Category", "Evidence")]
public class PhotoEditTimingEvidenceTests(S3ProxyFixture s3, MongoFixture mongo) : IClassFixture<MongoFixture>
{
    private const int Images = 100;
    private const int Gets = 100;
    private const string PhotoLine = "http.request GET /admin/library/images/";

    private static string[] Messages(LibraryHost h)
    {
        lock (h.LogLines) return h.LogLines.SelectMany(l => l.Split('\n')).ToArray();
    }

    private static async Task<string> CreateAsync(LibraryHost h, string title)
    {
        var r = await h.RunAsync<GalleryService, OperationResult>(s => s.CreateAsync(title));
        Assert.True(r.IsOk);
        return r.GalleryId!;
    }

    [Fact]
    public async Task Post_photo_edit_screen()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3);
        var t0 = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc);

        var photos = new List<string>();
        for (var i = 0; i < Images; i++)
            photos.Add((await h.InsertUploadedImageAsync(t0.AddMinutes(i), title: $"Photo {i:000}", files: false)).Id);

        for (var g = 0; g < 3; g++)
        {
            var gid = await CreateAsync(h, $"Gallery {g}");
            Assert.True((await h.RunAsync<GalleryService, OperationResult>(s => s.AddAsync(gid, photos.Take(10).ToList()))).IsOk);
        }
        await h.SettleAsync();

        var id = photos[0];
        for (var i = 0; i < Gets; i++)
        {
            var resp = await h.GetAsync($"/admin/library/images/{id}?page=1");
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
            var html = await resp.Content.ReadAsStringAsync();
            Assert.Contains("id=\"lib-edit\"", html);
        }

        var lines = Messages(h);
        var n = lines.Count(l => l.StartsWith(PhotoLine, StringComparison.Ordinal));
        Assert.True(n >= Gets,
            $"Only {n} '{PhotoLine.Trim()}' lines (need {Gets}). GET lines seen: "
            + string.Join(" | ", lines.Where(l => l.StartsWith("http.request GET ", StringComparison.Ordinal)).Distinct().Take(5)));

        var path = Environment.GetEnvironmentVariable("PHOTO_TIMING_LOG") is { Length: > 0 } env
            ? (Path.IsPathRooted(env) ? env : Path.Combine(Render.RepoRoot, env))
            : Path.Combine(Render.RepoRoot, "specs", "017-photo-edit-screen", "evidence", "photo-timings.log");
        await File.WriteAllLinesAsync(path, lines);
    }
}
