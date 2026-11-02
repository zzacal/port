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

        Assert.Equal(HttpStatusCode.OK, (await h.GetAsync("/admin/library")).StatusCode);
        var actions = 0;

        // (1) L3: add the 40 to a new gallery "Coast" → 303 to its builder with ?added=40.
        var add = await h.PostFormAsync("/admin/library/add",
            forty.Select(id => F("ids", id)).Append(F("gallery", "new")).Append(F("newTitle", "Coast")).Append(F("page", "1")));
        actions++;
        Assert.Equal(HttpStatusCode.SeeOther, add.StatusCode);
        var loc = add.Headers.Location!.ToString();
        Assert.Matches("^/admin/galleries/[0-9a-z]{26}\\?added=40$", loc);
        var gid = loc["/admin/galleries/".Length..loc.IndexOf('?')];
        var gallery = await h.Galleries.Find(g => g.Id == gid).FirstAsync();
        Assert.Equal(("Coast", "private"), (gallery.Title, gallery.Visibility));
        Assert.Equal(forty, gallery.Items);
        await StepDoneAsync(h);

        // (2) B3: move the 4th photo first.
        var order = new List<string> { forty[3] };
        order.AddRange(forty.Where((_, i) => i != 3));
        var reorder = await h.PostFormAsync($"/admin/galleries/{gid}/order", order.Select(id => F("order", id)));
        actions++;
        Assert.Equal(HttpStatusCode.SeeOther, reorder.StatusCode);
        Assert.Equal($"/admin/galleries/{gid}?ordered=1", reorder.Headers.Location?.ToString());
        Assert.Equal(order, (await h.Galleries.Find(g => g.Id == gid).FirstAsync()).Items);
        await StepDoneAsync(h);

        // (3) B1: the builder's Save form carries the switch, with the Save-time warning (no buttons).
        var builder = await h.GetAsync($"/admin/galleries/{gid}");
        Assert.Equal(HttpStatusCode.OK, builder.StatusCode);
        var builderHtml = WebUtility.HtmlDecode(await builder.Content.ReadAsStringAsync());
        Assert.Contains("When you save, “Coast” becomes public: it will be listed on the home page with its 40 photos, and anyone can see them.",
            builderHtml);
        Assert.DoesNotContain("Make public", builderHtml);
        Assert.Equal("private", (await h.Galleries.Find(g => g.Id == gid).FirstAsync()).Visibility);
        await StepDoneAsync(h);

        // (4) B2 Save with the switch on → 303 back to the builder, public.
        var publish = await h.PostFormAsync($"/admin/galleries/{gid}",
            [F("title", "Coast"), F("description", ""), F("vis", "1"), F("public", "on")]);
        actions++;
        Assert.Equal(HttpStatusCode.SeeOther, publish.StatusCode);
        Assert.Equal($"/admin/galleries/{gid}?saved=1&visibility=public", publish.Headers.Location?.ToString());
        Assert.Equal("public", (await h.Galleries.Find(g => g.Id == gid).FirstAsync()).Visibility);
        await StepDoneAsync(h);

        Assert.True(actions <= 6, $"{actions} owner actions");

        // The recorder holds exactly P-MEMBERS ∪ P-VIS of these steps: the gallery page, all 40 photo
        // pages (P-VIS covers every item) and "/" (P-VIS).
        var expected = new List<string> { LibraryHost.Page("/"), LibraryHost.Page($"/g/{gid}/coast") };
        expected.AddRange(forty.Select(id => LibraryHost.Page($"/g/{gid}/coast/{id}")));
        Assert.Equal(expected.Order(StringComparer.Ordinal).ToArray(), h.Recorder.AllPurged().Order(StringComparer.Ordinal).ToArray());
        h.Recorder.Clear();

        // "/" lists Coast with the moved photo as its cover.
        var home = WebUtility.HtmlDecode(await (await h.GetAsync("/")).Content.ReadAsStringAsync());
        Assert.Contains($"/g/{gid}/coast", home);
        var moved = images[3];
        Assert.Contains(moved.Derivatives[0].Avif, home);
        Assert.DoesNotContain(images[0].Derivatives[0].Avif, home);

        // Library tiles say "Coast · public in 1" (010 T035, stated change, decisions row 30: the
        // status text content, tags stripped, now names the gallery as a link).
        var lib = await LibraryHtml.BodyAsync(await h.GetAsync("/admin/library"));
        foreach (var id in forty)
            Assert.Contains("<span class=\"lib-status\"", LibraryHtml.TileOf(lib, id));
        static string StatusText(string tile) => Regex.Replace(
            Regex.Match(tile, "<span class=\"lib-status\"[^>]*>(.*?)</span>").Groups[1].Value, "<[^>]*>", "");
        Assert.Equal(40, LibraryHtml.Blocks(lib, "div", "lib-tile").Count(t => StatusText(t) == "Coast · public in 1"));

        // L3 of the same 40 plus 5 new → "5 added · 40 were already in Coast".
        var again = await h.PostFormAsync("/admin/library/add",
            forty.Concat(five).Select(id => F("ids", id)).Append(F("gallery", gid)).Append(F("page", "1")));
        Assert.Equal(HttpStatusCode.SeeOther, again.StatusCode);
        var againLoc = again.Headers.Location!.ToString();
        Assert.Equal($"/admin/library?page=1&added=5&skipped=40&to={gid}", againLoc);
        await StepDoneAsync(h);
        var status = LibraryHtml.Status(await LibraryHtml.BodyAsync(await h.GetAsync(againLoc)));
        Assert.Equal("5 added · 40 were already in Coast", status);
        Assert.Equal(forty.Count + five.Count, (await h.Galleries.Find(g => g.Id == gid).FirstAsync()).Items.Count);
    }
}
