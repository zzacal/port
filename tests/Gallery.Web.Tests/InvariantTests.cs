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

    /// <summary>The JSON answer's root element (020 TI-16).</summary>
    private static async Task<System.Text.Json.JsonElement> JsonAsync(HttpResponseMessage resp)
    {
        using var doc = await UploadHost.ParseAsync(resp);
        Assert.NotNull(doc);
        return doc!.RootElement.Clone();
    }

    // 020 TI-16: every step is a JSON workspace call.
    private static async Task<HttpResponseMessage> Post(LibraryHost h, string step, HttpStatusCode expected, string path, object body)
    {
        var resp = await h.PostJsonAsync(path, body);
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
        var created = await Post(h, "create", HttpStatusCode.Created, "/admin/api/galleries", new { title = "Coast" });
        var gid = (await JsonAsync(created)).GetProperty("id").GetString()!;
        Assert.Matches("^[0-9a-z]{26}$", gid);
        await AfterAsync(h, "create");
        var spare = (await JsonAsync(await Post(h, "create spare", HttpStatusCode.Created, "/admin/api/galleries", new { title = "Spare" })))
            .GetProperty("id").GetString()!;
        await AfterAsync(h, "create spare");

        // 2. bulk add (L3): all five into Coast.
        var add = await Post(h, "bulk add", HttpStatusCode.OK, $"/admin/api/galleries/{gid}/photos", new { ids });
        var added = await JsonAsync(add);
        Assert.Equal((5, 0), (added.GetProperty("added").GetInt32(), added.GetProperty("skipped").GetInt32()));
        await AfterAsync(h, "bulk add");
        var items = (await h.Galleries.Find(g => g.Id == gid).FirstAsync()).Items.ToList();
        Assert.Equal(ids.ToHashSet(), items.ToHashSet());

        // 3. reorder (B3): last first.
        var order = items.Skip(items.Count - 1).Concat(items.Take(items.Count - 1)).ToList();
        await Post(h, "reorder", HttpStatusCode.OK, $"/admin/api/galleries/{gid}/order", new { order });
        Assert.Equal(order, (await h.Galleries.Find(g => g.Id == gid).FirstAsync()).Items);
        await AfterAsync(h, "reorder");

        // 4. pin (B4) the third item, 5. unpin.
        await Post(h, "pin", HttpStatusCode.OK, $"/admin/api/galleries/{gid}/cover", new { image = order[2] });
        Assert.Equal(order[2], (await h.Galleries.Find(g => g.Id == gid).FirstAsync()).PinnedCover);
        await AfterAsync(h, "pin");
        await Post(h, "unpin", HttpStatusCode.OK, $"/admin/api/galleries/{gid}/cover", new { image = (string?)null });
        Assert.Null((await h.Galleries.Find(g => g.Id == gid).FirstAsync()).PinnedCover);
        await AfterAsync(h, "unpin");

        // 6. remove (B5) the first item.
        var removed = await Post(h, "remove", HttpStatusCode.OK, $"/admin/api/galleries/{gid}/photos/remove", new { ids = new[] { order[0] } });
        Assert.Equal(1, (await JsonAsync(removed)).GetProperty("removed").GetInt32());
        await AfterAsync(h, "remove");

        // 7. publish (G4, the switch on): the home page now lists Coast.
        await Post(h, "publish", HttpStatusCode.OK, $"/admin/api/galleries/{gid}/visibility", new { @public = true });
        var home = await AfterAsync(h, "publish");
        Assert.Contains($"/g/{gid}/coast", home);

        // 8. rename (B2): the listing follows the new slug.
        await Post(h, "rename", HttpStatusCode.OK, $"/admin/api/galleries/{gid}", new { title = "Coast Path" });
        home = await AfterAsync(h, "rename");
        Assert.Contains($"/g/{gid}/coast-path", home);
        Assert.DoesNotContain($"/g/{gid}/coast\"", home);

        // 9. unpublish (G4 without public): the home page no longer lists it.
        await Post(h, "unpublish", HttpStatusCode.OK, $"/admin/api/galleries/{gid}/visibility", new { @public = false });
        home = await AfterAsync(h, "unpublish");
        Assert.DoesNotContain($"/g/{gid}/", home);

        // Publish again so the bulk delete changes a listed gallery's cover and count.
        await Post(h, "republish", HttpStatusCode.OK, $"/admin/api/galleries/{gid}/visibility", new { @public = true });
        await AfterAsync(h, "republish");

        // 10. bulk delete (L4): two members, one of them the current cover.
        var members = (await h.Galleries.Find(g => g.Id == gid).FirstAsync()).Items.ToList();
        var doomed = new[] { members[0], members[1] };
        var del = await Post(h, "bulk delete", HttpStatusCode.OK, "/admin/api/photos/delete", new { ids = doomed });
        var deleted = await JsonAsync(del);
        Assert.Equal((2, 0, 0),
            (deleted.GetProperty("deleted").GetInt32(), deleted.GetProperty("leftovers").GetInt32(), deleted.GetProperty("failed").GetInt32()));
        Assert.Equal(members.Skip(2).ToList(), (await h.Galleries.Find(g => g.Id == gid).FirstAsync()).Items);
        home = await AfterAsync(h, "bulk delete");
        Assert.Contains($"/g/{gid}/coast-path", home);

        // 11. gallery delete (B6).
        await Post(h, "gallery delete", HttpStatusCode.OK, $"/admin/api/galleries/{gid}/delete", new { });
        Assert.Equal(0, await h.Galleries.CountDocumentsAsync(g => g.Id == gid));
        home = await AfterAsync(h, "gallery delete");
        Assert.DoesNotContain($"/g/{gid}/", home);

        // 12. publish refusal (G4 on the empty Spare): 409, nothing stored.
        var dump = await DumpAsync(h);
        var refused = await Post(h, "publish refusal", HttpStatusCode.Conflict, $"/admin/api/galleries/{spare}/visibility",
            new { @public = true });
        Assert.Contains(GalleryService.EmptyPublishReason, (await JsonAsync(refused)).GetProperty("message").GetString());
        Assert.Equal(dump, await DumpAsync(h));
        await AfterAsync(h, "publish refusal");

        // 13. stale reorder (B3 on Spare naming a photo it does not hold): 409, nothing stored.
        var stale = await Post(h, "stale reorder", HttpStatusCode.Conflict, $"/admin/api/galleries/{spare}/order",
            new { order = new[] { members[2] } });
        Assert.Equal("This gallery changed in another tab. Reload to see the current order.",
            (await JsonAsync(stale)).GetProperty("message").GetString());
        Assert.Equal(dump, await DumpAsync(h));
        await AfterAsync(h, "stale reorder");
    }
}
