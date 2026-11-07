using System.Net;
using Gallery.Web.Library;
using Gallery.Web.Tests.Fixtures;

namespace Gallery.Web.Tests;

/// <summary>
/// 016 T004 timing evidence (SC-005, R-08). One <see cref="LibraryHost"/> holds 30 galleries: 24 with
/// photos (12 of them public) and 6 empty (3 of those public, published before their photo was
/// removed). It runs 100 GETs of <c>/admin/galleries</c>, asserts at least 100
/// <c>http.request GET /admin/galleries 200</c> lines, and writes the host's log messages, one per
/// line in log order, to the path in env <c>LIST_TIMING_LOG</c> (default
/// specs/016-galleries-list-icons/evidence/list-timings.log), which evidence/p95.sh compares against
/// the pre-change capture (pre-timings.log).
/// </summary>
[Collection("s3")]
[Trait("Category", "Evidence")]
public class GalleryListTimingEvidenceTests(S3ProxyFixture s3, MongoFixture mongo) : IClassFixture<MongoFixture>
{
    private const int Gets = 100;
    private const string ListLine = "http.request GET /admin/galleries 200 ";

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

    private static async Task OkAsync(LibraryHost h, Func<GalleryService, Task<OperationResult>> call) =>
        Assert.True((await h.RunAsync<GalleryService, OperationResult>(call)).IsOk);

    [Fact]
    public async Task Gallery_list_GETs_are_logged_for_the_p95_script()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3);
        var t0 = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc);

        var photos = new List<string>();
        for (var i = 0; i < 30; i++)
            photos.Add((await h.InsertUploadedImageAsync(t0.AddMinutes(i), title: $"Photo {i:000}", files: false)).Id);

        // 24 with photos, half public.
        for (var i = 0; i < 24; i++)
        {
            var gid = await CreateAsync(h, $"Gallery {i:00}");
            await OkAsync(h, s => s.AddAsync(gid, [photos[i]]));
            if (i % 2 == 0) await OkAsync(h, s => s.PublishAsync(gid, confirmed: true));
        }

        // 6 empty; 3 public (published while they held a photo, then the photo was removed).
        for (var i = 0; i < 6; i++)
        {
            var gid = await CreateAsync(h, $"Empty {i}");
            if (i < 3)
            {
                await OkAsync(h, s => s.AddAsync(gid, [photos[24 + i]]));
                await OkAsync(h, s => s.PublishAsync(gid, confirmed: true));
                await OkAsync(h, s => s.RemoveAsync(gid, photos[24 + i]));
            }
        }
        await h.SettleAsync();

        for (var i = 0; i < Gets; i++)
        {
            var resp = await h.GetAsync("/admin/galleries");
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
            _ = await resp.Content.ReadAsStringAsync();
        }

        var lines = Messages(h);
        var n = lines.Count(l => l.StartsWith(ListLine, StringComparison.Ordinal));
        Assert.True(n >= Gets,
            $"Only {n} '{ListLine.Trim()}' lines (need {Gets}). GET lines seen: "
            + string.Join(" | ", lines.Where(l => l.StartsWith("http.request GET ", StringComparison.Ordinal)).Distinct().Take(5)));

        var path = Environment.GetEnvironmentVariable("LIST_TIMING_LOG") is { Length: > 0 } env
            ? (Path.IsPathRooted(env) ? env : Path.Combine(Render.RepoRoot, env))
            : Path.Combine(Render.RepoRoot, "specs", "016-galleries-list-icons", "evidence", "list-timings.log");
        await File.WriteAllLinesAsync(path, lines);
    }
}
