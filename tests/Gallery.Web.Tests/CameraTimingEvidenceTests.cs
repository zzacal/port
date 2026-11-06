using System.Net;
using Gallery.Web.Data;
using Gallery.Web.Library;
using Gallery.Web.Tests.Fixtures;
using MongoDB.Driver;

namespace Gallery.Web.Tests;

/// <summary>
/// 013 T024 timing evidence (IV.1-IV.4, SC-004; evidence/p95.sh). One <see cref="LibraryHost"/>
/// (output cache OFF, so every GET reaches Mongo) whose photos carry cameras. The owner publishes two
/// galleries, retitles a camera photo and unpublishes one gallery, so the projection logs
/// <c>db.cameras.load</c> and <c>db.cameras.write</c>. Then, after 10 warm-up GETs per route, it
/// GETs <c>/g</c>, the 12-photo gallery page, one of its photo pages, <c>/c</c> and that camera's
/// <c>/c/{slug}</c> (the same 12 photos) 100 times each. It asserts an <c>op.timing</c> line for
/// each camera timer and an <c>http.request</c> line for each camera route template, and writes the
/// setup lines plus the measured lines (warm-up dropped), one message per line, to the path in env
/// <c>CAMERA_TIMINGS_OUT</c> (default specs/013-by-camera/evidence/camera-timings.log).
/// </summary>
[Collection("s3")]
[Trait("Category", "Evidence")]
public class CameraTimingEvidenceTests(S3ProxyFixture s3, MongoFixture mongo) : IClassFixture<MongoFixture>
{
    private const int WarmUp = 10;
    private const int Gets = 100;
    private const int Photos = 12;

    private static readonly string[] CameraOps = ["db.cameras.load", "db.cameras.write", "db.cameras.list", "db.camera.read"];

    private static string[] Messages(LibraryHost h)
    {
        lock (h.LogLines) return h.LogLines.SelectMany(l => l.Split('\n')).ToArray();
    }

    private static Task<OperationResult> Gs(LibraryHost h, Func<GalleryService, Task<OperationResult>> call) => h.RunAsync(call);

    private static async Task<(string Id, string Slug, List<string> Items)> GalleryAsync(
        LibraryHost h, string title, string make, string model, int photos, DateTime t0)
    {
        var created = await Gs(h, s => s.CreateAsync(title));
        Assert.Equal(OperationStatus.Ok, created.Status);
        var gid = created.GalleryId!;
        var ids = new List<string>();
        for (var i = 0; i < photos; i++)
            ids.Add((await h.InsertUploadedImageAsync(t0.AddMinutes(i), title: $"{title} {i:00}", files: false,
                shape: img => img.Camera = new CameraDoc { Make = make, Model = model })).Id);
        Assert.True((await Gs(h, s => s.AddAsync(gid, ids))).IsOk);
        var g = await h.Galleries.Find(x => x.Id == gid).FirstAsync();
        return (gid, g.Slug, g.Items.ToList());
    }

    [Fact]
    public async Task Camera_and_mirror_routes_are_logged_for_the_p95_script()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3);
        var t0 = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);

        // Owner actions: publish two galleries, retitle a camera photo, unpublish one gallery.
        var big = await GalleryAsync(h, "Coastline", "Canon", "Canon EOS R5", Photos, t0);
        var other = await GalleryAsync(h, "Fernery", "NIKON CORPORATION", "NIKON Z 8", 3, t0.AddDays(1));
        Assert.True((await Gs(h, s => s.PublishAsync(big.Id, confirmed: true))).IsOk);
        Assert.True((await Gs(h, s => s.PublishAsync(other.Id, confirmed: true))).IsOk);
        Assert.True((await h.RunAsync<ImageService, OperationResult>(s => s.UpdateTextAsync(big.Items[0], "Coastline renamed", null))).IsOk);
        Assert.True((await Gs(h, s => s.UnpublishAsync(other.Id))).IsOk);
        await h.EdgeIdleAsync();

        var cameras = await h.Db.GetCollection<CameraGroupDoc>("cameras").Find(FilterDefinition<CameraGroupDoc>.Empty).ToListAsync();
        var camera = Assert.Single(cameras);
        Assert.Equal(Photos, camera.Items.Count);
        Assert.True(new HashSet<string>(camera.Items).SetEquals(big.Items));

        var setup = Messages(h);

        var routes = new[]
        {
            "/g",
            $"/g/{big.Id}/{big.Slug}",
            $"/g/{big.Id}/{big.Slug}/{big.Items[5]}",
            "/c",
            $"/c/{camera.Slug}",
            // T029 adds the camera photo route
            $"/c/{camera.Slug}/{camera.Items[5]}",
        };
        string[] templates = ["/g", "/g/{id}/{slug}", "/g/{id}/{slug}/{imageId}", "/c", "/c/{slug}", "/c/{slug}/{iid}"];

        foreach (var path in routes)
            for (var i = 0; i < WarmUp; i++)
                Assert.Equal(HttpStatusCode.OK, (await h.GetAsync(path)).StatusCode);

        lock (h.LogLines) h.LogLines.Clear();

        foreach (var path in routes)
            for (var i = 0; i < Gets; i++)
                Assert.Equal(HttpStatusCode.OK, (await h.GetAsync(path)).StatusCode);

        var measured = Messages(h);
        var lines = setup.Concat(measured).ToArray();

        var requests = measured
            .Where(l => l.StartsWith("http.request GET ", StringComparison.Ordinal))
            .Select(l => l.Split(' '))
            .Where(f => f.Length >= 6 && f[3] == "200")
            .ToList();
        Assert.Equal(routes.Length * Gets, requests.Count);
        foreach (var template in templates)
            Assert.True(requests.Count(f => f[2] == template) == Gets, $"{template}: {requests.Count(f => f[2] == template)} lines");

        var missingOps = CameraOps.Where(op => !lines.Any(l => l.StartsWith($"op.timing {op} ", StringComparison.Ordinal))).ToList();
        Assert.True(missingOps.Count == 0, "no op.timing line for: " + string.Join(", ", missingOps));
        foreach (var template in new[] { "/c", "/c/{slug}", "/c/{slug}/{iid}" })
            Assert.Contains(lines, l => l.StartsWith($"http.request GET {template} 200 ", StringComparison.Ordinal));

        var outPath = Environment.GetEnvironmentVariable("CAMERA_TIMINGS_OUT") is { Length: > 0 } env
            ? env
            : Path.Combine(Render.RepoRoot, "specs", "013-by-camera", "evidence", "camera-timings.log");
        Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
        await File.WriteAllLinesAsync(outPath, lines);
    }
}
