using System.Net;
using System.Text.RegularExpressions;
using Gallery.Web.Library;
using Gallery.Web.Tests.Fixtures;

namespace Gallery.Web.Tests;

/// <summary>
/// 015 A3 (requirement change, owner clarify: count only; replaces 010 T034's lib-status facts):
/// the library tile's gallery icon (<c>.pa-group</c>) shows the membership state as its glyph
/// (<c>folder-plus</c> / <c>folder</c> / <c>folders</c>) and the count as its label ("Not in a
/// gallery", "In 1 gallery", "In {n} galleries"). It never names a gallery and never counts public
/// ones. Galleries are built through <see cref="GalleryService"/>, the way the owner builds them.
/// </summary>
[Collection("s3")]
public class LibraryMembershipTests(S3ProxyFixture s3, MongoFixture mongo) : IClassFixture<MongoFixture>
{
    private static Task<OperationResult> Gs(LibraryHost h, Func<GalleryService, Task<OperationResult>> call) => h.RunAsync(call);

    private static async Task<string> GalleryAsync(LibraryHost h, string title, IReadOnlyList<string> ids, bool publish = false)
    {
        var created = await Gs(h, s => s.CreateAsync(title));
        Assert.True(created.IsOk);
        var gid = created.GalleryId!;
        if (ids.Count > 0) Assert.True((await Gs(h, s => s.AddAsync(gid, ids))).IsOk);
        if (publish) Assert.True((await Gs(h, s => s.PublishAsync(gid, confirmed: true))).IsOk);
        return gid;
    }

    private static async Task<string> GetOkAsync(LibraryHost h, string path)
    {
        var resp = await h.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        return await LibraryHtml.BodyAsync(resp);
    }

    /// <summary>The tile's single gallery icon link.</summary>
    private static string GroupOf(string html, string iid) =>
        Assert.Single(LibraryHtml.Blocks(LibraryHtml.TileOf(html, iid), "a", "pa-group"));

    private static string Attr(string element, string name) =>
        Regex.Match(element, $"\\s{name}=\"([^\"]*)\"").Groups[1].Value;

    private sealed record Setup(string Html, string None, string One, string Three);

    private static async Task<Setup> ArrangeAsync(LibraryHost h)
    {
        var none = await h.InsertUploadedImageAsync(files: false);
        var one = await h.InsertUploadedImageAsync(files: false);
        var three = await h.InsertUploadedImageAsync(files: false);

        // Only "coast" is public.
        await GalleryAsync(h, "coast", [three.Id], publish: true);
        await GalleryAsync(h, "Alpine", [three.Id]);
        await GalleryAsync(h, "birds", [one.Id, three.Id]);
        await h.SettleAsync();

        var html = await GetOkAsync(h, "/admin/library");
        return new Setup(html, none.Id, one.Id, three.Id);
    }

    [Fact]
    public async Task Zero_galleries_is_folder_plus_and_not_in_a_gallery()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3);
        var s = await ArrangeAsync(h);

        var group = GroupOf(s.Html, s.None);
        Assert.Equal(("folder-plus", "Not in a gallery", "Not in a gallery"), (Attr(group, "data-icon"), Attr(group, "aria-label"), Attr(group, "title")));
    }

    [Fact]
    public async Task One_gallery_is_folder_and_in_1_gallery()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3);
        var s = await ArrangeAsync(h);

        var group = GroupOf(s.Html, s.One);
        Assert.Equal(("folder", "In 1 gallery", "In 1 gallery"), (Attr(group, "data-icon"), Attr(group, "aria-label"), Attr(group, "title")));
    }

    [Fact]
    public async Task Three_galleries_is_folders_and_in_3_galleries_whatever_their_visibility()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3);
        var s = await ArrangeAsync(h);

        var group = GroupOf(s.Html, s.Three);
        Assert.Equal(("folders", "In 3 galleries", "In 3 galleries"), (Attr(group, "data-icon"), Attr(group, "aria-label"), Attr(group, "title")));
    }

    [Fact]
    public async Task The_label_never_names_a_gallery_or_counts_public_ones()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3);
        var s = await ArrangeAsync(h);

        foreach (var iid in new[] { s.None, s.One, s.Three })
        {
            var row = Assert.Single(LibraryHtml.Blocks(LibraryHtml.TileOf(s.Html, iid), "div", "photo-actions"));
            // What the owner reads or hears: the row's text and every aria-label/title value (the view
            // link's href legitimately holds the gallery slug, so raw markup is not checked).
            var words = Regex.Replace(row, "<[^>]*>", " ") + " "
                + string.Join(" ", Regex.Matches(row, "(?:aria-label|title)=\"([^\"]*)\"").Select(m => m.Groups[1].Value));
            foreach (var title in new[] { "coast", "Alpine", "birds" })
                Assert.DoesNotContain(title, words, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("public in", words, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("public", Attr(GroupOf(s.Html, iid), "aria-label"), StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("/admin/galleries/", row);
        }
    }

    [Fact]
    public async Task SC002_every_tile_has_exactly_one_gallery_icon_that_opens_the_photo_like_edit()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3);
        var s = await ArrangeAsync(h);

        var ids = LibraryHtml.TileIds(s.Html);
        Assert.Equal(3, ids.Count);
        foreach (var iid in ids)
        {
            var tile = LibraryHtml.TileOf(s.Html, iid);
            var group = GroupOf(s.Html, iid);
            var edit = Assert.Single(LibraryHtml.Blocks(tile, "a", "pa-edit"));
            Assert.Equal($"?page=1&open={iid}#t-{iid}", Attr(group, "href"));
            Assert.Equal(Attr(edit, "href"), Attr(group, "href"));
        }
    }
}
