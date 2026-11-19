using System.Net;
using System.Text.RegularExpressions;
using Gallery.Web.Data;
using Gallery.Web.Tests.Fixtures;
using MongoDB.Driver;

namespace Gallery.Web.Tests;

/// <summary>
/// 009 T034 (US1 path, SC-001): from the library to a public gallery in at most 6 owner actions,
/// every step over HTTP. <see cref="InvariantChecker"/> runs after each step; the recorder holds
/// exactly P-MEMBERS ∪ P-VIS for the whole path (contracts/edge.md).
/// </summary>
[Collection("s3")]
public class LibraryFlowTests(S3ProxyFixture s3, MongoFixture mongo) : IClassFixture<MongoFixture>
{
    private static KeyValuePair<string, string> F(string k, string v) => new(k, v);

    private static async Task StepDoneAsync(LibraryHost h)
    {
        await h.EdgeIdleAsync();
        await InvariantChecker.AssertAsync(h.Db);
    }

    /// <summary>The JSON answer's root element (020 TI-14).</summary>
    private static async Task<System.Text.Json.JsonElement> JsonAsync(HttpResponseMessage resp)
    {
        using var doc = await UploadHost.ParseAsync(resp);
        Assert.NotNull(doc);
        return doc!.RootElement.Clone();
    }

    [Fact]
    public async Task Forty_photos_become_a_public_gallery_in_three_actions()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3);
        var t0 = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc);
        var images = new List<ImageDoc>();
        for (var i = 0; i < 45; i++)
            images.Add(await h.InsertUploadedImageAsync(t0.AddMinutes(i), files: false));
        var forty = images.Take(40).Select(i => i.Id).ToList();
        var five = images.Skip(40).Select(i => i.Id).ToList();

        Assert.Equal(HttpStatusCode.OK, (await h.GetAsync("/admin")).StatusCode);
        var actions = 0;

        // (1) A3: a new gallery "Coast" holding the 40 → 201 with its id and added 40.
        var add = await h.PostJsonAsync("/admin/api/galleries", new { title = "Coast", ids = forty });
        actions++;
        Assert.Equal(HttpStatusCode.Created, add.StatusCode);
        var created = await JsonAsync(add);
        Assert.Equal(40, created.GetProperty("added").GetInt32());
        var gid = created.GetProperty("id").GetString()!;
        Assert.Matches("^[0-9a-z]{26}$", gid);
        var gallery = await h.Galleries.Find(g => g.Id == gid).FirstAsync();
        Assert.Equal(("Coast", "private"), (gallery.Title, gallery.Visibility));
        Assert.Equal(forty, gallery.Items);
        await StepDoneAsync(h);

        // (2) A9: move the 4th photo first.
        var order = new List<string> { forty[3] };
        order.AddRange(forty.Where((_, i) => i != 3));
        var reorder = await h.PostJsonAsync($"/admin/api/galleries/{gid}/order", new { order });
        actions++;
        Assert.Equal(HttpStatusCode.OK, reorder.StatusCode);
        Assert.Equal(order, (await h.Galleries.Find(g => g.Id == gid).FirstAsync()).Items);
        await StepDoneAsync(h);

        // (3) 020 TI-14: no builder page and no Save form; the gallery is still private before the switch.
        Assert.Equal("private", (await h.Galleries.Find(g => g.Id == gid).FirstAsync()).Visibility);
        await StepDoneAsync(h);

        // (4) A6: the public switch on → 200, public.
        var publish = await h.PostJsonAsync($"/admin/api/galleries/{gid}/visibility", new { @public = true });
        actions++;
        Assert.Equal(HttpStatusCode.OK, publish.StatusCode);
        Assert.Equal("Coast is now public. Visitors can see it.", (await JsonAsync(publish)).GetProperty("message").GetString());
        Assert.Equal("public", (await h.Galleries.Find(g => g.Id == gid).FirstAsync()).Visibility);
        await StepDoneAsync(h);

        Assert.True(actions <= 6, $"{actions} owner actions");

        // The recorder holds exactly P-MEMBERS ∪ P-VIS of these steps: the gallery page, all 40 photo
        // pages (P-VIS covers every item) and "/g" (P-VIS).
        var expected = new List<string> { LibraryHost.Page("/g"), LibraryHost.Page($"/g/{gid}/coast") };
        expected.AddRange(forty.Select(id => LibraryHost.Page($"/g/{gid}/coast/{id}")));
        Assert.Equal(expected.Order(StringComparer.Ordinal).ToArray(), h.Recorder.AllPurged().Order(StringComparer.Ordinal).ToArray());
        h.Recorder.Clear();

        // "/g" lists Coast with the moved photo as its cover.
        var home = WebUtility.HtmlDecode(await (await h.GetAsync("/g")).Content.ReadAsStringAsync());
        Assert.Contains($"/g/{gid}/coast", home);
        var moved = images[3];
        Assert.Contains(moved.Derivatives[0].Avif, home);
        Assert.DoesNotContain(images[0].Derivatives[0].Avif, home);

        // A7 of the same 40 plus 5 new → added 5, skipped 40.
        var again = await h.PostJsonAsync($"/admin/api/galleries/{gid}/photos", new { ids = forty.Concat(five).ToList() });
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        var againJson = await JsonAsync(again);
        Assert.Equal((5, 40), (againJson.GetProperty("added").GetInt32(), againJson.GetProperty("skipped").GetInt32()));
        Assert.Equal("5 photos added to Coast. 40 were already in it. Visitors can see them now.",
            againJson.GetProperty("message").GetString());
        await StepDoneAsync(h);
        Assert.Equal(forty.Count + five.Count, (await h.Galleries.Find(g => g.Id == gid).FirstAsync()).Items.Count);
    }
}
