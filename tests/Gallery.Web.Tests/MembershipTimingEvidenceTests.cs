using System.Net;
using Gallery.Web.Data;
using Gallery.Web.Edge;
using Gallery.Web.Library;
using Gallery.Web.Tests.Fixtures;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;

namespace Gallery.Web.Tests;

/// <summary>
/// 014 T019 timing evidence (contracts/http.md log events; SC-002). One <see cref="LibraryHost"/>
/// holds 100 photos and one existing gallery. It runs 100 photo-panel saves (L2), each POSTing one
/// <c>add</c> (the existing gallery, which that photo is not yet in, so every add is a real write)
/// and one <c>create</c> (a unique title per save), with antiforgery. It asserts at least 100
/// <c>op.timing library.image.memberships … ok</c> lines and at least 100
/// <c>http.request POST /admin/library/images/{iid} 303</c> lines, waits on
/// <see cref="EdgeQueue.WhenIdleAsync"/>, and writes the host's log messages, one per line in log
/// order, to specs/014-gallery-combobox/evidence/membership-timings.log, which evidence/p95.sh reads.
/// </summary>
[Collection("s3")]
[Trait("Category", "Evidence")]
public class MembershipTimingEvidenceTests(S3ProxyFixture s3, MongoFixture mongo) : IClassFixture<MongoFixture>
{
    private const int Saves = 100;

    private static KeyValuePair<string, string> F(string k, string v) => new(k, v);

    private static string[] Messages(LibraryHost h)
    {
        lock (h.LogLines) return h.LogLines.SelectMany(l => l.Split('\n')).ToArray();
    }

    [Fact]
    public async Task Panel_saves_with_an_add_and_a_create_are_logged_for_the_p95_script()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3);
        var t0 = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc);

        var photos = new List<string>();
        for (var i = 0; i < Saves; i++)
            photos.Add((await h.InsertUploadedImageAsync(t0.AddMinutes(i), title: $"Photo {i:000}")).Id);

        var shared = await h.RunAsync<GalleryService, OperationResult>(s => s.CreateAsync("Shared"));
        Assert.True(shared.IsOk);
        var gid = shared.GalleryId!;
        await h.SettleAsync();

        // ── L2: 100 panel saves, each one add (a real write) and one create ──
        for (var i = 0; i < Saves; i++)
        {
            var iid = photos[i];
            var resp = await h.PostFormAsync($"/admin/library/images/{iid}",
                [F("add", gid), F("create", $"Created {i:000}"), F("page", "1")]);
            Assert.Equal(HttpStatusCode.SeeOther, resp.StatusCode);
            Assert.Equal($"/admin/library?page=1&open={iid}&saved=1", resp.Headers.Location?.ToString());
        }

        // Every add landed and every create made one gallery.
        Assert.Equal(photos, (await h.Galleries.Find(g => g.Id == gid).FirstAsync()).Items);
        Assert.Equal(Saves + 1, await h.Galleries.CountDocumentsAsync(FilterDefinition<GalleryDoc>.Empty));

        // ── edge drains, then the evidence ───────────────────────────────────
        Assert.True(await h.Services.GetRequiredService<EdgeQueue>().WhenIdleAsync(TimeSpan.FromSeconds(60)),
            "The edge queue did not drain within 60 s.");
        await InvariantChecker.AssertAsync(h.Db);

        var lines = Messages(h);
        var ok = lines.Count(l => l.StartsWith("op.timing library.image.memberships ", StringComparison.Ordinal)
            && l.EndsWith(" ok", StringComparison.Ordinal));
        Assert.True(ok >= Saves, $"Only {ok} op.timing library.image.memberships ok lines (need {Saves}).");
        var posts = lines.Count(l => l.StartsWith("http.request POST /admin/library/images/{iid} 303 ", StringComparison.Ordinal));
        Assert.True(posts >= Saves, $"Only {posts} http.request POST /admin/library/images/{{iid}} 303 lines (need {Saves}).");

        var path = Path.Combine(Render.RepoRoot, "specs", "014-gallery-combobox", "evidence", "membership-timings.log");
        await File.WriteAllLinesAsync(path, lines);
    }
}
