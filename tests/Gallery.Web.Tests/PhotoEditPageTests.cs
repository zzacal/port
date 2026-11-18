using System.Net;
using System.Text.RegularExpressions;
using Gallery.Core.Images;
using Gallery.Web.Data;
using Gallery.Web.Library;
using Gallery.Web.Tests.Fixtures;
using MongoDB.Driver;

namespace Gallery.Web.Tests;

/// <summary>
/// 017 T015 (US1, US2; contracts/http.md L6, L1, L2; contracts/components.md P1, P3) [SAFETY]: the
/// photo edit screen over HTTP. One photo in main (no tiles, no grid), <c>sizes</c> from
/// <see cref="Sizes.ForAside"/>, the breadcrumb back to library page N at the tile, the edit and
/// delete forms carrying the antiforgery token and the return page, the Saved state, 404s, HEAD,
/// three reads, owner-only access; the library's tiles link to the screen and <c>?open=</c> is ignored.
/// </summary>
[Collection("s3")]
public class PhotoEditPageTests(S3ProxyFixture s3, MongoFixture mongo) : IClassFixture<MongoFixture>
{
    private static async Task<string> GalleryAsync(LibraryHost h, string title, IReadOnlyList<string> ids)
    {
        var created = await h.RunAsync<GalleryService, OperationResult>(s => s.CreateAsync(title));
        Assert.True(created.IsOk);
        var gid = created.GalleryId!;
        if (ids.Count > 0) Assert.True((await h.RunAsync<GalleryService, OperationResult>(s => s.AddAsync(gid, ids))).IsOk);
        return gid;
    }

    private static async Task<string> GetOkAsync(LibraryHost h, string path)
    {
        var resp = await h.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        return await LibraryHtml.BodyAsync(resp);
    }

    /// <summary>A photo held by two galleries, settled.</summary>
    private static async Task<ImageDoc> ArrangeAsync(LibraryHost h)
    {
        var img = await h.InsertUploadedImageAsync(files: false, title: "Harbour");
        await GalleryAsync(h, "Alpha", [img.Id]);
        await GalleryAsync(h, "Bravo", [img.Id]);
        await h.SettleAsync();
        return img;
    }

    private static string MainOf(string html) =>
        Regex.Match(html, "<main\\b[^>]*>(.*?)</main>", RegexOptions.Singleline).Groups[1].Value;

    private static string? Attr(string tag, string name)
    {
        var m = Regex.Match(tag, $"\\s{Regex.Escape(name)}(=\"([^\"]*)\")?(?=[\\s/>])");
        return !m.Success ? null : m.Groups[1].Success ? m.Groups[2].Value : "";
    }

    /// <summary>The opening tag of the element with <paramref name="id"/>.</summary>
    private static string TagById(string html, string tag, string id) =>
        Assert.Single(Regex.Matches(html, $"<{tag}\\b[^>]*\\sid=\"{Regex.Escape(id)}\"[^>]*>").Select(m => m.Value));

    /// <summary>The element (open tag through close) with <paramref name="id"/>.</summary>
    private static string BlockById(string html, string tag, string id) =>
        LibraryHtml.Element(html, tag, html.IndexOf(TagById(html, tag, id), StringComparison.Ordinal));

    private static string? HiddenValue(string form, string name)
    {
        var tag = Regex.Matches(form, "<input\\b[^>]*>").Select(m => m.Value)
            .SingleOrDefault(t => Attr(t, "name") == name && Attr(t, "type") == "hidden");
        return tag is null ? null : Attr(tag, "value");
    }

    private static List<string> CrumbHrefs(string html)
    {
        var nav = LibraryHtml.Element(html, "nav", html.IndexOf("<nav class=\"breadcrumb\"", StringComparison.Ordinal));
        return Regex.Matches(nav, "<a\\b[^>]*href=\"([^\"]*)\"").Select(m => m.Groups[1].Value).ToList();
    }

    private static string CrumbCurrent(string html) =>
        Regex.Match(html, "<li aria-current=\"page\"[^>]*><span[^>]*>(.*?)</span></li>").Groups[1].Value;

    [Fact]
    public async Task L6_shows_one_photo_and_no_grid_with_the_aside_sizes_and_high_priority()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3);
        var img = await ArrangeAsync(h);
        var html = await GetOkAsync(h, $"/admin/library/images/{img.Id}?page=3");
        var main = MainOf(html);

        Assert.Equal(1, Guards.Count(main, "<picture"));
        Assert.DoesNotMatch("class=\"tile[\" ]", html);
        Assert.DoesNotMatch("class=\"cs[\" ]", html);
        Assert.DoesNotContain("name=\"ids\"", html);

        var sources = Regex.Matches(main, "<source\\b[^>]*>").Select(m => Attr(m.Value, "sizes")).ToList();
        Assert.Equal([Sizes.ForAside(), Sizes.ForAside()], sources);
        var img0 = Assert.Single(Regex.Matches(main, "<img\\b[^>]*fetchpriority=\"high\"[^>]*>").Select(m => m.Value));
        Assert.Equal("Harbour", Attr(img0, "alt"));
        // The photo is not a link: no <a> in main at all (the View link is in the aside).
        Assert.DoesNotMatch("<a\\b", main);
    }

    [Theory]
    [InlineData("?page=3", 3)]
    [InlineData("?page=x", 1)]
    [InlineData("", 1)]
    public async Task Breadcrumb_goes_back_to_library_page_N_at_the_tile(string query, int page)
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3);
        var img = await ArrangeAsync(h);
        var html = await GetOkAsync(h, $"/admin/library/images/{img.Id}{query}");

        Assert.Equal([$"/admin/library?page={page}#t-{img.Id}"], CrumbHrefs(html));
        Assert.Equal("Harbour", CrumbCurrent(html));
    }

    [Fact]
    public async Task Edit_and_delete_forms_post_to_the_photo_with_the_return_page_and_the_antiforgery_token()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3);
        var img = await ArrangeAsync(h);
        var html = await GetOkAsync(h, $"/admin/library/images/{img.Id}?page=3");

        var edit = BlockById(html, "form", "lib-edit");
        Assert.Equal($"/admin/library/images/{img.Id}", Attr(TagById(html, "form", "lib-edit"), "action"));
        Assert.Equal("post", Attr(TagById(html, "form", "lib-edit"), "method"));
        Assert.Equal("3", HiddenValue(edit, "page"));
        var token = HiddenValue(edit, "__RequestVerificationToken");
        Assert.False(string.IsNullOrEmpty(token), "the edit form carries no antiforgery token");

        var del = BlockById(html, "form", "lib-del-one");
        Assert.Equal($"/admin/library/images/{img.Id}/delete", Attr(TagById(html, "form", "lib-del-one"), "action"));
        Assert.Equal("3", HiddenValue(del, "page"));
        Assert.False(string.IsNullOrEmpty(HiddenValue(del, "__RequestVerificationToken")));
        Assert.Contains("id=\"lib-gf\"", edit);
    }

    [Fact]
    public async Task Saved_shows_the_status_line_first_and_the_panel_Saved()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3);
        var img = await ArrangeAsync(h);

        var html = await GetOkAsync(h, $"/admin/library/images/{img.Id}?page=1&saved=1");
        var first = Regex.Match(html, "<[a-z]+\\b[^>]*role=\"status\"[^>]*>").Value;
        Assert.Equal("<p class=\"lib-result\" role=\"status\">", Regex.Replace(first, " b-[a-z0-9]+", ""));
        Assert.Equal("Saved", LibraryHtml.Status(html));
        Assert.Matches("<span class=\"sp-status\"[^>]*>Saved</span>", html);

        var plain = await GetOkAsync(h, $"/admin/library/images/{img.Id}?page=1");
        Assert.DoesNotContain("lib-result", plain);
        Assert.DoesNotMatch("<span class=\"sp-status\"[^>]*>Saved</span>", plain);
    }

    [Fact]
    public async Task Malformed_unknown_and_tombstoned_ids_are_404_and_HEAD_answers_200_with_no_body()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3);
        var img = await ArrangeAsync(h);
        var dead = await h.InsertUploadedImageAsync(files: false, title: "Gone");
        await h.Images.UpdateOneAsync(i => i.Id == dead.Id, Builders<ImageDoc>.Update.Set(i => i.DeletingAt, DateTime.UtcNow));

        foreach (var path in new[]
                 {
                     "/admin/library/images/not-an-id",
                     $"/admin/library/images/{Gallery.Core.Ids.OpaqueId.NewId()}",
                     $"/admin/library/images/{dead.Id}",
                 })
        {
            var resp = await h.GetAsync(path);
            Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
            Assert.Empty(await resp.Content.ReadAsByteArrayAsync());
        }

        var head = await h.Host.SendAsync(new HttpRequestMessage(HttpMethod.Head, $"/admin/library/images/{img.Id}"));
        Assert.Equal(HttpStatusCode.OK, head.StatusCode);
        Assert.Empty(await head.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task L6_costs_three_reads()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3);
        var img = await ArrangeAsync(h);
        await h.EdgeIdleAsync();
        int before;
        lock (h.LogLines) before = h.LogLines.Count;
        await GetOkAsync(h, $"/admin/library/images/{img.Id}?page=1");

        // The middleware logs the route template, not the concrete path.
        const string prefix = "http.request GET /admin/library/images/{iid} 200 ";
        List<string> requests = [];
        for (var i = 0; i < 100 && requests.Count == 0; i++)
        {
            lock (h.LogLines)
                requests = h.LogLines.Skip(before).SelectMany(l => l.Split('\n'))
                    .Where(l => l.StartsWith(prefix, StringComparison.Ordinal)).ToList();
            if (requests.Count == 0) await Task.Delay(20);
        }
        var line = Assert.Single(requests);
        Assert.Equal("3", line.Split(' ')[^1]);
    }

    [Fact]
    public async Task Signed_out_L6_answers_the_same_challenge_as_the_library()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3);
        var img = await ArrangeAsync(h);
        h.Host.ClearCookies();

        var library = await h.GetAsync("/admin/library");
        var edit = await h.GetAsync($"/admin/library/images/{img.Id}?page=1");
        Assert.NotEqual(HttpStatusCode.OK, library.StatusCode);
        Assert.Equal(library.StatusCode, edit.StatusCode);
        Assert.NotNull(library.Headers.Location);
        Assert.Equal(library.Headers.Location, edit.Headers.Location);
        Assert.DoesNotContain("Harbour", await edit.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Old_open_param_renders_the_plain_library_and_every_tile_links_to_its_edit_screen()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3);
        var img = await ArrangeAsync(h);
        var other = await h.InsertUploadedImageAsync(files: false, title: "Rocks");
        var html = await GetOkAsync(h, $"/admin/library?open={img.Id}");

        Assert.DoesNotContain("side-panel", html);
        Assert.DoesNotContain("lib-del-one", html);
        var ids = LibraryHtml.TileIds(html);
        Assert.Equal(2, ids.Count);
        foreach (var iid in ids)
        {
            var row = Assert.Single(LibraryHtml.Blocks(LibraryHtml.TileOf(html, iid), "div", "photo-actions"));
            foreach (var cls in new[] { "pa-edit", "pa-group" })
            {
                var control = Assert.Single(Regex.Matches(row, "<(a|span|button)\\b[^>]*>").Select(m => m.Value),
                    t => (Attr(t, "class") ?? "").Split(' ').Contains(cls));
                Assert.Equal($"/admin/library/images/{iid}?page=1", Attr(control, "href"));
                Assert.Null(Attr(control, "aria-current"));
            }
        }
        Assert.Contains(other.Id, ids);
    }

    [Fact]
    public async Task A_refused_save_re_renders_the_edit_screen_with_the_reason_and_the_typed_caption()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3);
        var img = await ArrangeAsync(h);

        var resp = await h.PostFormAsync($"/admin/library/images/{img.Id}",
            [new("title", ""), new("caption", "Typed & kept"), new("page", "2")]);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
        var html = await LibraryHtml.BodyAsync(resp);

        Assert.Equal("Enter a title.", LibraryHtml.Status(html));
        Assert.Matches("<textarea[^>]*name=\"caption\"[^>]*>Typed & kept</textarea>", html);
        Assert.Equal(1, Guards.Count(MainOf(html), "<picture"));
        Assert.Equal([$"/admin/library?page=2#t-{img.Id}"], CrumbHrefs(html));
        Assert.Equal("2", HiddenValue(BlockById(html, "form", "lib-edit"), "page"));
    }

    [Fact]
    public async Task No_inline_style_and_exactly_one_script()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3);
        var img = await ArrangeAsync(h);
        var resp = await h.GetAsync($"/admin/library/images/{img.Id}?page=1");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var raw = Guards.StripStyle(await resp.Content.ReadAsStringAsync());

        Guards.AssertNoInlineStyle(raw);
        Assert.Equal(1, Guards.Count(raw, "<script"));
        Assert.Matches("<script type=\"module\" src=\"/admin-assets/library\\.js[^\"]*\"[^>]*></script>", raw);
    }
}
