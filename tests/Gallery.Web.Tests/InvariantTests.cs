using System.Net;
using System.Text.RegularExpressions;
using Gallery.Web.Library;
using Gallery.Web.Tests.Fixtures;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Gallery.Web.Tests;

/// <summary>
/// 009 T055 (SC-007, SC-009): one scripted owner session over HTTP on <see cref="LibraryHost"/>:
/// create, bulk add, reorder, pin, unpin, remove, publish, rename, unpublish, bulk delete, gallery
/// delete, publish refusal and stale reorder. After EVERY step the edge queue drains,
/// <see cref="InvariantChecker.AssertAsync"/> passes, and a fresh anonymous-cache GET <c>/</c> logs
/// exactly one <c>http.request GET /g 200 {ms} 1</c> line (011: the listing moved to /g) (<c>db_reads</c> = 1). The two refusals
/// also store nothing.
/// </summary>
[Collection("s3")]
public partial class InvariantTests(S3ProxyFixture s3, MongoFixture mongo) : IClassFixture<MongoFixture>
{
    private static KeyValuePair<string, string> F(string k, string v) => new(k, v);

    [GeneratedRegex(@"^http\.request GET /g 200 [\d.]+ 1$")]
    private static partial Regex HomeOneRead();

    private static async Task<string> DumpAsync(LibraryHost h)
    {
        var parts = new List<string>();
        foreach (var name in new[] { "images", "galleries", "site" })
        {
            var docs = await h.Db.GetCollection<BsonDocument>(name).Find(FilterDefinition<BsonDocument>.Empty)
                .Sort(Builders<BsonDocument>.Sort.Ascending("_id")).ToListAsync();
            parts.Add(name + ":" + string.Join(",", docs.Select(d => d.ToJson())));
        }
        return string.Join("\n", parts);
    }

    /// <summary>The step's post-condition: invariants hold and the home page is one datastore read.</summary>
    private static async Task<string> AfterAsync(LibraryHost h, string step)
    {
        await h.EdgeIdleAsync();
        try
        {
            await InvariantChecker.AssertAsync(h.Db);
        }
        catch (InvariantChecker.InvariantViolation e)
        {
            Assert.Fail($"after {step}: {e.Message}");
        }

        int before;
        lock (h.LogLines) before = h.LogLines.Count;
        var home = await h.GetAsync("/g");
        Assert.True(home.StatusCode == HttpStatusCode.OK, $"after {step}: GET /g answered {(int)home.StatusCode}");
        var html = WebUtility.HtmlDecode(await home.Content.ReadAsStringAsync());

        // The request line is written when the response completes; wait briefly for it.
        List<string> lines = [];
        for (var i = 0; i < 100; i++)
        {
            lock (h.LogLines) lines = h.LogLines.Skip(before).Where(l => l.StartsWith("http.request GET /g ", StringComparison.Ordinal)).ToList();
            if (lines.Count > 0) break;
            await Task.Delay(20);
        }
        Assert.True(lines.Count == 1, $"after {step}: {lines.Count} http.request lines for one GET /g");
        Assert.True(HomeOneRead().IsMatch(lines[0]), $"after {step}: GET /g logged '{lines[0]}', expected 200 with db_reads 1");
        return html;
    }

    private static string GidOf(string location) =>
        location["/admin/galleries/".Length..(location.IndexOf('?') is var q and >= 0 ? q : location.Length)];

    private static async Task<HttpResponseMessage> Post(LibraryHost h, string step, HttpStatusCode expected, string path,
        IEnumerable<KeyValuePair<string, string>> fields)
    {
        var resp = await h.PostFormAsync(path, fields);
        Assert.True(resp.StatusCode == expected, $"{step}: POST {path} answered {(int)resp.StatusCode}, expected {(int)expected}");
        return resp;
    }

    [Fact]
    public async Task Every_step_of_an_owner_session_keeps_the_invariants_and_a_one_read_home_page()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3);
        var t0 = new DateTime(2026, 3, 4, 0, 0, 0, DateTimeKind.Utc);
        var imgs = new List<Data.ImageDoc>();
        for (var i = 0; i < 5; i++)
            imgs.Add(await h.InsertUploadedImageAsync(t0.AddMinutes(i), $"Photo {i}"));
        var ids = imgs.Select(i => i.Id).ToList();
        await AfterAsync(h, "setup");

        // 1. create (G2): Coast, and an empty Spare for the refusals.
        var created = await Post(h, "create", HttpStatusCode.SeeOther, "/admin/galleries", [F("title", "Coast")]);
        var gid = GidOf(created.Headers.Location!.ToString());
        Assert.Matches("^[0-9a-z]{26}$", gid);
        await AfterAsync(h, "create");
        var spare = GidOf((await Post(h, "create spare", HttpStatusCode.SeeOther, "/admin/galleries", [F("title", "Spare")]))
            .Headers.Location!.ToString());
        await AfterAsync(h, "create spare");

        // 2. bulk add (L3): all five into Coast.
        var add = await Post(h, "bulk add", HttpStatusCode.SeeOther, "/admin/library/add",
            ids.Select(id => F("ids", id)).Append(F("gallery", gid)).Append(F("page", "1")));
        Assert.Equal($"/admin/library?page=1&added=5&skipped=0&to={gid}", add.Headers.Location?.ToString());
        await AfterAsync(h, "bulk add");
        var items = (await h.Galleries.Find(g => g.Id == gid).FirstAsync()).Items.ToList();
        Assert.Equal(ids.ToHashSet(), items.ToHashSet());

        // 3. reorder (B3): last first.
        var order = items.Skip(items.Count - 1).Concat(items.Take(items.Count - 1)).ToList();
        await Post(h, "reorder", HttpStatusCode.SeeOther, $"/admin/galleries/{gid}/order", order.Select(id => F("order", id)));
        Assert.Equal(order, (await h.Galleries.Find(g => g.Id == gid).FirstAsync()).Items);
        await AfterAsync(h, "reorder");

        // 4. pin (B4) the third item, 5. unpin.
        await Post(h, "pin", HttpStatusCode.SeeOther, $"/admin/galleries/{gid}/cover", [F("image", order[2])]);
        Assert.Equal(order[2], (await h.Galleries.Find(g => g.Id == gid).FirstAsync()).PinnedCover);
        await AfterAsync(h, "pin");
        await Post(h, "unpin", HttpStatusCode.SeeOther, $"/admin/galleries/{gid}/cover", []);
        Assert.Null((await h.Galleries.Find(g => g.Id == gid).FirstAsync()).PinnedCover);
        await AfterAsync(h, "unpin");

        // 6. remove (B5) the first item.
        var removed = await Post(h, "remove", HttpStatusCode.SeeOther, $"/admin/galleries/{gid}/remove", [F("image", order[0])]);
        Assert.Equal($"/admin/galleries/{gid}?removed=1", removed.Headers.Location?.ToString());
        await AfterAsync(h, "remove");

        // 7. publish (G4, the switch on): the home page now lists Coast.
        await Post(h, "publish", HttpStatusCode.SeeOther, $"/admin/galleries/{gid}/visibility",
            [F("public", "on"), F("from", "builder")]);
        var home = await AfterAsync(h, "publish");
        Assert.Contains($"/g/{gid}/coast", home);

        // 8. rename (B2): the listing follows the new slug.
        await Post(h, "rename", HttpStatusCode.SeeOther, $"/admin/galleries/{gid}", [F("title", "Coast Path")]);
        home = await AfterAsync(h, "rename");
        Assert.Contains($"/g/{gid}/coast-path", home);
        Assert.DoesNotContain($"/g/{gid}/coast\"", home);

        // 9. unpublish (G4 without public): the home page no longer lists it.
        await Post(h, "unpublish", HttpStatusCode.SeeOther, $"/admin/galleries/{gid}/visibility", [F("from", "list")]);
        home = await AfterAsync(h, "unpublish");
        Assert.DoesNotContain($"/g/{gid}/", home);

        // Publish again so the bulk delete changes a listed gallery's cover and count.
        await Post(h, "republish", HttpStatusCode.SeeOther, $"/admin/galleries/{gid}/visibility",
            [F("public", "on"), F("from", "list")]);
        await AfterAsync(h, "republish");

        // 10. bulk delete (L4): two members, one of them the current cover.
        var members = (await h.Galleries.Find(g => g.Id == gid).FirstAsync()).Items.ToList();
        var doomed = new[] { members[0], members[1] };
        var del = await Post(h, "bulk delete", HttpStatusCode.SeeOther, "/admin/library/delete",
            doomed.Select(id => F("ids", id)).Append(F("page", "1")));
        Assert.Equal("/admin/library?page=1&deleted=2&leftovers=0&failed=0", del.Headers.Location?.ToString());
        Assert.Equal(members.Skip(2).ToList(), (await h.Galleries.Find(g => g.Id == gid).FirstAsync()).Items);
        home = await AfterAsync(h, "bulk delete");
        Assert.Contains($"/g/{gid}/coast-path", home);

        // 11. gallery delete (B6).
        var gdel = await Post(h, "gallery delete", HttpStatusCode.SeeOther, $"/admin/galleries/{gid}/delete", []);
        Assert.Equal("/admin/galleries?deleted=1", gdel.Headers.Location?.ToString());
        Assert.Equal(0, await h.Galleries.CountDocumentsAsync(g => g.Id == gid));
        home = await AfterAsync(h, "gallery delete");
        Assert.DoesNotContain($"/g/{gid}/", home);

        // 12. publish refusal (G4 on the empty Spare): 409, nothing stored.
        var dump = await DumpAsync(h);
        var refused = await Post(h, "publish refusal", HttpStatusCode.Conflict, $"/admin/galleries/{spare}/visibility",
            [F("public", "on"), F("from", "list")]);
        Assert.Contains(GalleryService.EmptyPublishReason, WebUtility.HtmlDecode(await refused.Content.ReadAsStringAsync()));
        Assert.Equal(dump, await DumpAsync(h));
        await AfterAsync(h, "publish refusal");

        // 13. stale reorder (B3 on Spare naming a photo it does not hold): 409, nothing stored.
        var stale = await Post(h, "stale reorder", HttpStatusCode.Conflict, $"/admin/galleries/{spare}/order",
            [F("order", members[2])]);
        Assert.Contains("This gallery changed in another tab. Reload to see the current order.",
            WebUtility.HtmlDecode(await stale.Content.ReadAsStringAsync()));
        Assert.Equal(dump, await DumpAsync(h));
        await AfterAsync(h, "stale reorder");
    }
}
