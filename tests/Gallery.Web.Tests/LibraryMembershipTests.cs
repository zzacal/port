using System.Net;
using System.Text.RegularExpressions;
using Gallery.Web.Library;
using Gallery.Web.Tests.Fixtures;

namespace Gallery.Web.Tests;

/// <summary>
/// 010 T034 (US5, FR-011, SC-009): the library tile's <c>lib-status</c> links the first gallery by
/// title (case-insensitive, any visibility), then " +N" for the others, then " · public in M".
/// Galleries are built through <see cref="GalleryService"/>, the way the owner builds them.
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

    /// <summary>The (decoded) inner markup of the tile's single <c>span.lib-status</c>.</summary>
    private static string StatusOf(string html, string iid)
    {
        var span = Assert.Single(LibraryHtml.Blocks(LibraryHtml.TileOf(html, iid), "span", "lib-status"));
        return Regex.Match(span, "^<span\\b[^>]*>(.*)</span>$", RegexOptions.Singleline).Groups[1].Value;
    }

    private static string TextOf(string inner) => Regex.Replace(inner, "<[^>]*>", "");

    /// <summary>Asserts the inner markup is exactly one builder link to <paramref name="gid"/> titled <paramref name="title"/>, then <paramref name="tail"/>.</summary>
    private static void AssertLinked(string inner, string gid, string title, string tail)
    {
        var m = Regex.Match(inner, "^<a class=\"text-link\" href=\"([^\"]*)\"[^>]*>([^<]*)</a>(.*)$", RegexOptions.Singleline);
        Assert.True(m.Success, $"not a leading link: {inner}");
        Assert.Equal($"/admin/galleries/{gid}", m.Groups[1].Value);
        Assert.Equal(title, m.Groups[2].Value);
        Assert.Equal(tail, m.Groups[3].Value);
        Assert.Single(Regex.Matches(inner, "<a\\b"));
    }

    private sealed record Setup(string Html, string None, string One, string Three, string Coast, string Alpine, string Birds);

    private static async Task<Setup> ArrangeAsync(LibraryHost h)
    {
        var none = await h.InsertUploadedImageAsync(files: false);
        var one = await h.InsertUploadedImageAsync(files: false);
        var three = await h.InsertUploadedImageAsync(files: false);

        // Home order: coast, Alpine, birds. Only "coast" is public.
        var coast = await GalleryAsync(h, "coast", [three.Id], publish: true);
        var alpine = await GalleryAsync(h, "Alpine", [three.Id]);
        var birds = await GalleryAsync(h, "birds", [one.Id, three.Id]);
        await h.SettleAsync();

        var order = (await h.SiteAsync()).HomeOrder;
        Assert.Equal(coast, order[0]);
        Assert.True(order.IndexOf(coast) < order.IndexOf(alpine));

        var html = await GetOkAsync(h, "/admin/library");
        return new Setup(html, none.Id, one.Id, three.Id, coast, alpine, birds);
    }

    [Fact]
    public async Task Zero_galleries_is_plain_text_with_no_link()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3);
        var s = await ArrangeAsync(h);

        var inner = StatusOf(s.Html, s.None);
        Assert.Equal("Not in a gallery", inner);
        Assert.DoesNotContain("<a", inner);
    }

    [Fact]
    public async Task One_gallery_links_to_its_builder_with_the_title()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3);
        var s = await ArrangeAsync(h);

        var inner = StatusOf(s.Html, s.One);
        AssertLinked(inner, s.Birds, "birds", "");
        Assert.Equal("birds", TextOf(inner));
    }

    [Fact]
    public async Task Three_galleries_link_the_first_by_title_case_insensitive_then_others_and_public_count()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3);
        var s = await ArrangeAsync(h);

        var inner = StatusOf(s.Html, s.Three);
        // "Alpine" < "birds" < "coast" case-insensitively, though "coast" is first in home order
        // (and the only public one): the private "Alpine" is still chosen.
        AssertLinked(inner, s.Alpine, "Alpine", " +2 · public in 1");
        Assert.Equal("Alpine +2 · public in 1", TextOf(inner));
        Assert.DoesNotContain($"/admin/galleries/{s.Coast}", inner);
        Assert.DoesNotContain($"/admin/galleries/{s.Birds}", inner);
    }

    [Fact]
    public async Task A_private_only_first_gallery_is_still_chosen()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3);
        var photo = await h.InsertUploadedImageAsync(files: false);
        var zed = await GalleryAsync(h, "Zed", [photo.Id], publish: true);     // first in home order, public
        var amber = await GalleryAsync(h, "amber", [photo.Id]);               // private, first by title
        await h.SettleAsync();

        var inner = StatusOf(await GetOkAsync(h, "/admin/library"), photo.Id);
        AssertLinked(inner, amber, "amber", " +1 · public in 1");
        Assert.DoesNotContain(zed, inner);
    }

    [Fact]
    public async Task SC009_every_tile_in_a_gallery_has_exactly_one_builder_link_in_its_status()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3);
        var s = await ArrangeAsync(h);

        var ids = LibraryHtml.TileIds(s.Html);
        Assert.Equal(3, ids.Count);
        foreach (var iid in ids)
        {
            var links = Regex.Matches(StatusOf(s.Html, iid), "href=\"/admin/galleries/").Count;
            Assert.Equal(iid == s.None ? 0 : 1, links);
        }
    }
}
