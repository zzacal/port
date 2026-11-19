using System.Net;
using System.Text.RegularExpressions;
using Gallery.Web.Data;
using Gallery.Web.Edge;
using Gallery.Web.Library;
using Gallery.Web.Tests.Fixtures;
using MongoDB.Driver;

namespace Gallery.Web.Tests;

/// <summary>
/// 009 T033 (US2; FR-009, FR-010, FR-038 P-TEXT, FR-044): the side panel and L2 over HTTP. A save
/// answers 303 and shows the stored values; refusals answer 422 with the reason in
/// <c>role="status"</c> and store nothing; a bare POST changes nothing; the purge set is exact; the
/// local output cache drops exactly the changed pages.
/// </summary>
[Collection("s3")]
public class LibraryEditTests(S3ProxyFixture s3, MongoFixture mongo) : IClassFixture<MongoFixture>
{
    private static Task<OperationResult> Gs(LibraryHost h, Func<GalleryService, Task<OperationResult>> call) => h.RunAsync(call);

    private static Task<ImageDoc> ImageAsync(LibraryHost h, string iid) => h.Images.Find(i => i.Id == iid).FirstAsync();

    private static async Task<string> GalleryAsync(LibraryHost h, string title, IReadOnlyList<string> ids, bool publish = false)
    {
        var created = await Gs(h, s => s.CreateAsync(title));
        Assert.True(created.IsOk);
        var gid = created.GalleryId!;
        if (ids.Count > 0) Assert.True((await Gs(h, s => s.AddAsync(gid, ids))).IsOk);
        if (publish) Assert.True((await Gs(h, s => s.PublishAsync(gid, confirmed: true))).IsOk);
        return gid;
    }

    // 020 TI-10: L2 → A1 (JSON; an absent field keeps the stored value).
    private static Task<HttpResponseMessage> SaveAsync(LibraryHost h, string iid, params (string, string)[] fields) =>
        h.PostJsonAsync($"/admin/api/photos/{iid}", fields.ToDictionary(f => f.Item1, f => f.Item2));

    /// <summary>The JSON answer's root element (020 TI-10).</summary>
    private static async Task<System.Text.Json.JsonElement> JsonAsync(HttpResponseMessage resp)
    {
        using var doc = await UploadHost.ParseAsync(resp);
        Assert.NotNull(doc);
        return doc!.RootElement.Clone();
    }

    private static int Count(LibraryHost h, string op)
    {
        lock (h.LogLines)
            return h.LogLines.Count(l => l.StartsWith($"op.timing {op} ", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Save_answers_303_and_the_panel_shows_the_stored_values_and_Saved()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3);
        var img = await h.InsertUploadedImageAsync(files: false, title: "IMG_0001");

        var resp = await SaveAsync(h, img.Id, ("title", "  Harbour at dusk "), ("caption", "Low tide & gulls."));
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var json = await JsonAsync(resp);
        Assert.Equal(("Harbour at dusk", "Low tide & gulls.", "Saved"),
            (json.GetProperty("title").GetString(), json.GetProperty("caption").GetString(), json.GetProperty("message").GetString()));

        var stored = await ImageAsync(h, img.Id);
        Assert.Equal(("Harbour at dusk", "Low tide & gulls."), (stored.Title, stored.Caption));
    }

    public static TheoryData<string, string?, string> Refusals() => new()
    {
        { "blank", "", "Enter a title." },
        { "whitespace", "   ", "Enter a title." },
        { "201 chars", new string('t', 201), "The title can be at most 200 characters." },
        { "2001-char caption", null, "The caption can be at most 2000 characters." },
    };

    [Theory]
    [MemberData(nameof(Refusals))]
    public async Task Invalid_text_is_422_with_the_reason_and_stores_nothing(string _, string? title, string reason)
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3);
        var img = await h.InsertUploadedImageAsync(files: false, title: "Stored title");
        await SaveAsync(h, img.Id, ("title", "Stored title"), ("caption", "Stored caption"));
        await h.SettleAsync();

        var fields = title is null
            ? new[] { ("title", "Fine"), ("caption", new string('c', 2001)) }
            : new[] { ("title", title), ("caption", "Changed caption") };
        var resp = await SaveAsync(h, img.Id, fields);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
        Assert.Equal(reason, (await JsonAsync(resp)).GetProperty("message").GetString());
        var stored = await ImageAsync(h, img.Id);
        Assert.Equal(("Stored title", "Stored caption"), (stored.Title, stored.Caption));
        Assert.Empty(await h.SettleAsync());
    }

    [Fact]
    public async Task Bare_POST_answers_400_and_keeps_both_stored_values()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3);
        var img = await h.InsertUploadedImageAsync(files: false, title: "Stored title");
        await SaveAsync(h, img.Id, ("title", "Stored title"), ("caption", "Stored caption"));
        await h.SettleAsync();

        var resp = await h.PostBareAsync($"/admin/api/photos/{img.Id}", csrfHeader: true);

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        Assert.Equal("bad-request", (await JsonAsync(resp)).GetProperty("error").GetString());
        var stored = await ImageAsync(h, img.Id);
        Assert.Equal(("Stored title", "Stored caption"), (stored.Title, stored.Caption));
        Assert.Empty(await h.SettleAsync());
    }

    [Fact]
    public async Task Unknown_or_malformed_image_is_404()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3);
        Assert.Equal(HttpStatusCode.NotFound, (await SaveAsync(h, Gallery.Core.Ids.OpaqueId.NewId(), ("title", "x"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SaveAsync(h, "not-an-id", ("title", "x"))).StatusCode);
    }

    [Fact]
    public async Task Purge_is_exactly_P_TEXT_and_nothing_for_an_image_in_no_gallery()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3);
        var cover = await h.InsertUploadedImageAsync(files: false);
        var img = await h.InsertUploadedImageAsync(files: false);
        var loose = await h.InsertUploadedImageAsync(files: false);
        var gid = await GalleryAsync(h, "Coast", [cover.Id, img.Id], publish: true);
        await h.SettleAsync();

        Assert.Equal(HttpStatusCode.OK, (await SaveAsync(h, img.Id, ("title", "New title"))).StatusCode);
        var purged = await h.SettleAsync();
        Assert.Equal(
            new[] { LibraryHost.Page($"/g/{gid}/coast"), LibraryHost.Page($"/g/{gid}/coast/{img.Id}") }.Order(StringComparer.Ordinal).ToArray(),
            purged.Order(StringComparer.Ordinal).ToArray());

        Assert.Equal(HttpStatusCode.OK, (await SaveAsync(h, loose.Id, ("title", "Loose title"))).StatusCode);
        Assert.Empty(await h.SettleAsync());
        Assert.Equal("Loose title", (await ImageAsync(h, loose.Id)).Title);
    }

    [Fact]
    public async Task Saving_a_title_evicts_the_photo_page_but_not_the_home_page()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3, extraOverrides: new() { ["Site:OutputCache"] = "true" });
        var cover = await h.InsertUploadedImageAsync(files: false, title: "Cover");
        var img = await h.InsertUploadedImageAsync(files: false, title: "Before");
        var gid = await GalleryAsync(h, "Coast", [cover.Id, img.Id], publish: true);
        await h.SettleAsync();
        var photo = $"/g/{gid}/coast/{img.Id}";

        Assert.Equal(HttpStatusCode.OK, (await h.GetAsync(photo)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await h.GetAsync("/g")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await h.GetAsync(photo)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await h.GetAsync("/g")).StatusCode);
        Assert.Equal(1, Count(h, "db.gallery.read"));
        Assert.Equal(1, Count(h, "db.site.read"));

        Assert.Equal(HttpStatusCode.OK, (await SaveAsync(h, img.Id, ("title", "After"))).StatusCode);

        var page = await h.GetAsync(photo);
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        var body = WebUtility.HtmlDecode(await page.Content.ReadAsStringAsync());
        Assert.Contains("After", body);
        Assert.DoesNotContain(">Before<", body);
        Assert.Equal(2, Count(h, "db.gallery.read"));

        Assert.Equal(HttpStatusCode.OK, (await h.GetAsync("/g")).StatusCode);
        Assert.Equal(1, Count(h, "db.site.read"));
    }
}
