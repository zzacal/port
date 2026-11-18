using System.Net;
using Gallery.Web.Library;
using Gallery.Web.Tests.Fixtures;

namespace Gallery.Web.Tests;

/// <summary>
/// 017 T003 timing evidence (SC-005, R-09), pre-change capture. One <see cref="LibraryHost"/> holds
/// 100 uploaded images and 3 galleries holding the first 10. It runs 100 GETs of
/// <c>/admin/library?page=1&amp;open={id}</c> (the library with the photo panel open, the screen the
/// edit page replaces), asserts at least 100 <c>http.request GET /admin/library 200</c> lines, and
/// writes the host's log messages, one per line in log order, to the path in env
/// <c>PHOTO_TIMING_LOG</c> (default specs/017-photo-edit-screen/evidence/pre-timings.log), which
/// evidence/p95.sh compares against the post-change capture (photo-timings.log).
/// </summary>
[Collection("s3")]
[Trait("Category", "Evidence")]
public class PhotoEditTimingEvidenceTests(S3ProxyFixture s3, MongoFixture mongo) : IClassFixture<MongoFixture>
{
    private const int Images = 100;
    private const int Gets = 100;
    private const string LibraryLine = "http.request GET /admin/library 200 ";

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
    public async Task Pre_library_with_panel()
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
            var resp = await h.GetAsync($"/admin/library?page=1&open={id}");
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
            var html = await resp.Content.ReadAsStringAsync();
            Assert.Contains("id=\"lib-edit\"", html);
        }

        var lines = Messages(h);
        var n = lines.Count(l => l.StartsWith(LibraryLine, StringComparison.Ordinal));
        Assert.True(n >= Gets,
            $"Only {n} '{LibraryLine.Trim()}' lines (need {Gets}). GET lines seen: "
            + string.Join(" | ", lines.Where(l => l.StartsWith("http.request GET ", StringComparison.Ordinal)).Distinct().Take(5)));

        var path = Environment.GetEnvironmentVariable("PHOTO_TIMING_LOG") is { Length: > 0 } env
            ? (Path.IsPathRooted(env) ? env : Path.Combine(Render.RepoRoot, env))
            : Path.Combine(Render.RepoRoot, "specs", "017-photo-edit-screen", "evidence", "pre-timings.log");
        await File.WriteAllLinesAsync(path, lines);
    }
}
