using System.Net;
using Gallery.Web.Library;
using Gallery.Web.Tests.Fixtures;
using MongoDB.Driver;

namespace Gallery.Web.Tests;

/// <summary>
/// 010 T003 timing evidence (contracts/log-events.md; SC-006, FR-015, FR-016). One
/// <see cref="LibraryHost"/> (output cache off, so every request reaches Mongo) holds three public
/// galleries of 23, 6 and 1 photos and one private gallery of 2, all built through
/// <see cref="GalleryService"/>. After 10 warm-up requests per route it GETs <c>/</c>, the 23-photo
/// gallery page and its 12th photo page 100 times each, asserts 300 <c>http.request</c> lines with
/// status 200, and writes the host's log messages, one per line, to the path in env
/// <c>NAV_TIMINGS_OUT</c> (default specs/010-navigation-improvements/evidence/nav-timings.log),
/// which evidence/p95.sh reads. Run once on the pre-010 code for the baseline.
/// </summary>
[Collection("s3")]
[Trait("Category", "Evidence")]
public class NavTimingEvidenceTests(S3ProxyFixture s3, MongoFixture mongo) : IClassFixture<MongoFixture>
{
    private const int WarmUp = 10;
    private const int Gets = 100;

    private static string[] Messages(LibraryHost h)
    {
        lock (h.LogLines) return h.LogLines.SelectMany(l => l.Split('\n')).ToArray();
    }

    private static async Task<(string Id, string Slug, List<string> Items)> GalleryAsync(
        LibraryHost h, string title, int photos, bool publish, DateTime t0)
    {
        var created = await h.RunAsync<GalleryService, OperationResult>(s => s.CreateAsync(title));
        Assert.Equal(OperationStatus.Ok, created.Status);
        var gid = created.GalleryId!;
        var ids = new List<string>();
        for (var i = 0; i < photos; i++)
            ids.Add((await h.InsertUploadedImageAsync(t0.AddMinutes(i), title: $"{title} {i:00}")).Id);
        Assert.True((await h.RunAsync<GalleryService, OperationResult>(s => s.AddAsync(gid, ids))).IsOk);
        if (publish)
            Assert.True((await h.RunAsync<GalleryService, OperationResult>(s => s.PublishAsync(gid, confirmed: true))).IsOk);
        var g = await h.Galleries.Find(x => x.Id == gid).FirstAsync();
        return (gid, g.Slug, g.Items.ToList());
    }

    [Fact]
    public async Task Home_gallery_and_photo_reads_are_logged_for_the_p95_script()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3);
        var t0 = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc);

        var big = await GalleryAsync(h, "September 6", 23, publish: true, t0);
        await GalleryAsync(h, "Coast", 6, publish: true, t0.AddDays(1));
        await GalleryAsync(h, "Solo", 1, publish: true, t0.AddDays(2));
        await GalleryAsync(h, "Private", 2, publish: false, t0.AddDays(3));
        await h.EdgeIdleAsync();

        var routes = new[]
        {
            "/",
            $"/g/{big.Id}/{big.Slug}",
            $"/g/{big.Id}/{big.Slug}/{big.Items[11]}",
        };

        foreach (var path in routes)
            for (var i = 0; i < WarmUp; i++)
                Assert.Equal(HttpStatusCode.OK, (await h.GetAsync(path)).StatusCode);

        lock (h.LogLines) h.LogLines.Clear();

        foreach (var path in routes)
            for (var i = 0; i < Gets; i++)
            {
                var resp = await h.GetAsync(path);
                Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
            }

        var lines = Messages(h);
        var requests = lines.Where(l => l.StartsWith("http.request GET ", StringComparison.Ordinal)).ToList();
        Assert.Equal(3 * Gets, requests.Count(l => l.Split(' ') is { Length: >= 6 } f && f[3] == "200"));

        var outPath = Environment.GetEnvironmentVariable("NAV_TIMINGS_OUT") is { Length: > 0 } env
            ? env
            : Path.Combine(Render.RepoRoot, "specs", "010-navigation-improvements", "evidence", "nav-timings.log");
        Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
        await File.WriteAllLinesAsync(outPath, lines);
    }
}
