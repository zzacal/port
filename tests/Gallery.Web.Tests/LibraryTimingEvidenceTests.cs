using System.Net;
using Gallery.Web.Data;
using Gallery.Web.Edge;
using Gallery.Web.Tests.Fixtures;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;

namespace Gallery.Web.Tests;

/// <summary>
/// 009 T057 timing evidence (contracts/log-events.md; SC-002, SC-003, SC-005, FR-049). One
/// <see cref="LibraryHost"/> holds 100 uploaded images (files in S3Proxy) plus five more sets of 100
/// for the bulk deletes. It GETs <c>/admin/library</c> 30 times, then runs 30 of each save (L2, B2,
/// B3, B4, B5, G3, G4), 5 bulk adds of 100 (L3), 5 bulk deletes of 100 (L4), and 5 each of G2, L5
/// and B6 (decisions row 61: p95.sh has one SC-003 row per 009 POST route and n=0 fails), waits on
/// <see cref="EdgeQueue.WhenIdleAsync"/> (60 s), asserts an <c>op.timing</c> line for every 009 op
/// name except <c>maintenance.remove-seeded</c> (T046) and <c>edge.purge.call</c>/<c>edge.warm.request</c>
/// (dev-zone only), and writes the host's log messages, one per line in log order, to
/// specs/009-library-galleries/evidence/library-timings.log, which evidence/p95.sh reads.
/// </summary>
[Collection("s3")]
[Trait("Category", "Evidence")]
public class LibraryTimingEvidenceTests(S3ProxyFixture s3, MongoFixture mongo) : IClassFixture<MongoFixture>
{
    private const int Images = 100;
    private const int PageGets = 30;
    private const int Saves = 30;
    private const int Bulk = 5;
    private const int Few = 5;

    private static readonly string[] Ops =
    [
        "db.library.page", "db.library.count", "db.galleries.list", "db.site.order",
        "db.image.update", "db.image.tombstone", "db.image.delete", "db.images.shared",
        "db.gallery.create", "db.gallery.update", "db.gallery.delete", "db.gallery.load",
        "db.membership.repair", "db.site.project", "s3.delete", "library.image.delete",
        "library.bulk.add", "library.bulk.delete", "cache.evict",
    ];

    private static KeyValuePair<string, string> F(string k, string v) => new(k, v);

    private static string[] Messages(LibraryHost h)
    {
        lock (h.LogLines) return h.LogLines.SelectMany(l => l.Split('\n')).ToArray();
    }

    private static async Task SeeOtherAsync(Task<HttpResponseMessage> call, string? location = null)
    {
        var resp = await call;
        Assert.Equal(HttpStatusCode.SeeOther, resp.StatusCode);
        if (location is not null) Assert.Equal(location, resp.Headers.Location?.ToString());
    }

    private static string GidOf(HttpResponseMessage resp)
    {
        var loc = resp.Headers.Location!.ToString();
        var end = loc.IndexOf('?');
        return loc["/admin/galleries/".Length..(end < 0 ? loc.Length : end)];
    }

    private static List<string> Rotate(List<string> ids) => [.. ids.Skip(1), ids[0]];

    [Fact]
    public async Task Library_reads_saves_bulk_operations_and_edge_lag_are_logged_for_the_p95_script()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3);
        var t0 = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc);

        var main = new List<string>();
        for (var i = 0; i < Images; i++)
            main.Add((await h.InsertUploadedImageAsync(t0.AddMinutes(i), title: $"Photo {i:000}")).Id);
        var deleteSets = new List<List<string>>();
        for (var s = 0; s < Bulk; s++)
        {
            var set = new List<string>();
            for (var i = 0; i < Images; i++)
                set.Add((await h.InsertUploadedImageAsync(t0.AddDays(-1 - s).AddMinutes(i))).Id);
            deleteSets.Add(set);
        }

        // ── L1: 30 loads of a full 100-image page ────────────────────────────
        for (var i = 0; i < PageGets; i++)
        {
            var resp = await h.GetAsync("/admin/library");
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
            var html = await LibraryHtml.BodyAsync(resp);
            Assert.Equal(Images, LibraryHtml.Blocks(html, "div", "lib-tile").Count);
        }

        // ── L2: 30 image saves ───────────────────────────────────────────────
        for (var i = 0; i < Saves; i++)
        {
            var iid = main[i];
            await SeeOtherAsync(h.PostFormAsync($"/admin/library/images/{iid}",
                [F("title", $"Saved {i:000}"), F("caption", $"Caption {i}"), F("page", "1")]),
                $"/admin/library?page=1&open={iid}&saved=1");
        }

        // ── L3: 5 bulk adds of 100 into new galleries ────────────────────────
        var bulkGalleries = new List<string>();
        for (var b = 0; b < Bulk; b++)
        {
            var resp = await h.PostFormAsync("/admin/library/add",
                main.Select(id => F("ids", id)).Append(F("gallery", "new")).Append(F("newTitle", $"Bulk {b}")).Append(F("page", "1")));
            Assert.Equal(HttpStatusCode.SeeOther, resp.StatusCode);
            Assert.EndsWith($"?added={Images}", resp.Headers.Location!.ToString());
            bulkGalleries.Add(GidOf(resp));
        }
        var gid = bulkGalleries[0];

        // ── G2: 5 gallery creates ────────────────────────────────────────────
        for (var i = 0; i < Few; i++)
        {
            var resp = await h.PostFormAsync("/admin/galleries", [F("title", $"Created {i}")]);
            Assert.Equal(HttpStatusCode.SeeOther, resp.StatusCode);
            Assert.Matches("^/admin/galleries/[0-9a-z]{26}$", resp.Headers.Location!.ToString());
        }

        // ── B2: 30 builder text saves (description only, the slug stays) ─────
        for (var i = 0; i < Saves; i++)
            await SeeOtherAsync(h.PostFormAsync($"/admin/galleries/{gid}", [F("title", "Bulk 0"), F("description", $"Notes {i}.")]),
                $"/admin/galleries/{gid}?saved=1");

        // ── B3: 30 builder reorders of the full 100 ──────────────────────────
        var items = (await h.Galleries.Find(g => g.Id == gid).FirstAsync()).Items.ToList();
        for (var i = 0; i < Saves; i++)
        {
            items = Rotate(items);
            await SeeOtherAsync(h.PostFormAsync($"/admin/galleries/{gid}/order", items.Select(id => F("order", id))),
                $"/admin/galleries/{gid}?ordered=1");
        }
        Assert.Equal(items, (await h.Galleries.Find(g => g.Id == gid).FirstAsync()).Items);

        // ── B4: 30 cover saves, pin and unpin alternately ────────────────────
        for (var i = 0; i < Saves; i++)
        {
            IEnumerable<KeyValuePair<string, string>> form = i % 2 == 0 ? [F("image", items[i])] : [];
            await SeeOtherAsync(h.PostFormAsync($"/admin/galleries/{gid}/cover", form), $"/admin/galleries/{gid}");
        }

        // ── G4: 30 visibility saves, publish (confirmed) and unpublish ───────
        for (var i = 0; i < Saves; i++)
        {
            var publish = i % 2 == 0;
            IEnumerable<KeyValuePair<string, string>> form = publish
                ? [F("public", "on"), F("confirm", "1"), F("from", "builder")]
                : [F("from", "builder")];
            await SeeOtherAsync(h.PostFormAsync($"/admin/galleries/{gid}/visibility", form),
                $"/admin/galleries/{gid}?visibility={(publish ? "public" : "private")}");
        }

        // ── G3: 30 home reorders of the full list ────────────────────────────
        var home = (await h.SiteAsync()).HomeOrder.ToList();
        Assert.Equal(Bulk + Few, home.Count);
        for (var i = 0; i < Saves; i++)
        {
            home = Rotate(home);
            await SeeOtherAsync(h.PostFormAsync("/admin/galleries/order", home.Select(id => F("order", id))),
                "/admin/galleries?ordered=1");
        }
        Assert.Equal(home, (await h.SiteAsync()).HomeOrder);

        // ── B5: 30 removes of distinct members ───────────────────────────────
        for (var i = 0; i < Saves; i++)
            await SeeOtherAsync(h.PostFormAsync($"/admin/galleries/{gid}/remove", [F("image", items[i])]),
                $"/admin/galleries/{gid}?removed=1");
        Assert.Equal(Images - Saves, (await h.Galleries.Find(g => g.Id == gid).FirstAsync()).Items.Count);

        // ── B6: 5 gallery deletes (bulk galleries 1–4, each with 100 photos, and one created) ──
        var created = (await h.SiteAsync()).HomeOrder.Except(bulkGalleries).First();
        foreach (var g in bulkGalleries.Skip(1).Append(created))
            await SeeOtherAsync(h.PostFormAsync($"/admin/galleries/{g}/delete", []), "/admin/galleries?deleted=1");

        // ── L5: 5 single deletes of gallery members ──────────────────────────
        foreach (var iid in main.TakeLast(Few))
            await SeeOtherAsync(h.PostFormAsync($"/admin/library/images/{iid}/delete", [F("page", "1")]),
                "/admin/library?page=1&deleted=1&leftovers=0");

        // ── L4: 5 bulk deletes of 100 ────────────────────────────────────────
        foreach (var set in deleteSets)
            await SeeOtherAsync(h.PostFormAsync("/admin/library/delete", set.Select(id => F("ids", id)).Append(F("page", "1"))),
                $"/admin/library?page=1&deleted={Images}&leftovers=0&failed=0");
        Assert.Equal(Images - Few, await h.Images.CountDocumentsAsync(FilterDefinition<ImageDoc>.Empty));

        // ── edge drains, then the evidence ───────────────────────────────────
        Assert.True(await h.Services.GetRequiredService<EdgeQueue>().WhenIdleAsync(TimeSpan.FromSeconds(60)),
            "The edge queue did not drain within 60 s.");
        await InvariantChecker.AssertAsync(h.Db);

        var lines = Messages(h);
        var missing = Ops.Where(op => !lines.Any(l => l.StartsWith($"op.timing {op} ", StringComparison.Ordinal))).ToList();
        Assert.True(missing.Count == 0, "No op.timing line for: " + string.Join(", ", missing));
        Assert.Contains(lines, l => l.StartsWith("edge.lag ", StringComparison.Ordinal));

        var path = Path.Combine(Render.RepoRoot, "specs", "009-library-galleries", "evidence", "library-timings.log");
        await File.WriteAllLinesAsync(path, lines);
    }
}
