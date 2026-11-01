using Gallery.Web.Data;
using Gallery.Web.Edge;
using Gallery.Web.Library;
using Gallery.Web.Tests.Fixtures;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Gallery.Web.Tests;

/// <summary>
/// 009 T027 (US2; FR-010, FR-012, FR-038 P-TEXT; II.5; decisions rows 24 and 30):
/// <see cref="ImageService.UpdateTextAsync"/> limits, trimming and keep-on-null, the exact P-TEXT
/// set, and <see cref="ImageService.BulkAddAsync"/>'s cap and new-gallery path. Galleries are built
/// through <see cref="GalleryService"/>; after every operation <see cref="InvariantChecker"/> runs.
/// </summary>
[Collection("s3")]
public class ImageServiceTests(S3ProxyFixture s3, MongoFixture mongo) : IClassFixture<MongoFixture>
{
    private static string Gp(string gid, string slug) => LibraryHost.Page(PurgeSets.GalleryPath(gid, slug));
    private static string Pp(string gid, string slug, string iid) => LibraryHost.Page(PurgeSets.PhotoPath(gid, slug, iid));
    private static readonly string Home = LibraryHost.Page("/");

    private static void SetEq(IEnumerable<string> expected, IEnumerable<string> actual) =>
        Assert.Equal(expected.Distinct().Order(StringComparer.Ordinal).ToArray(), actual.Order(StringComparer.Ordinal).ToArray());

    private static Task<OperationResult> Is(LibraryHost h, Func<ImageService, Task<OperationResult>> call) => h.RunAsync(call);
    private static Task<OperationResult> Gs(LibraryHost h, Func<GalleryService, Task<OperationResult>> call) => h.RunAsync(call);

    private static Task<ImageDoc> ImageAsync(LibraryHost h, string iid) => h.Images.Find(i => i.Id == iid).FirstAsync();

    [Fact]
    public async Task Title_and_caption_are_saved_trimmed_and_null_fields_keep_values()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3);
        var img = await h.InsertUploadedImageAsync(files: false);

        var r = await Is(h, s => s.UpdateTextAsync(img.Id, "  Harbour at dusk  ", "  Low tide.  "));
        await h.SettleAsync();
        Assert.Equal(OperationStatus.Ok, r.Status);
        var saved = await ImageAsync(h, img.Id);
        Assert.Equal(("Harbour at dusk", "Low tide."), (saved.Title, saved.Caption));

        var keep = await Is(h, s => s.UpdateTextAsync(img.Id, null, null));
        Assert.Equal(OperationStatus.Ok, keep.Status);
        var kept = await ImageAsync(h, img.Id);
        Assert.Equal(("Harbour at dusk", "Low tide."), (kept.Title, kept.Caption));

        var cleared = await Is(h, s => s.UpdateTextAsync(img.Id, null, "   "));
        await h.SettleAsync();
        Assert.Equal(OperationStatus.Ok, cleared.Status);
        var raw = await h.Db.GetCollection<BsonDocument>("images").Find(new BsonDocument("_id", img.Id)).FirstAsync();
        Assert.True(raw["caption"].IsBsonNull);
        Assert.Equal("Harbour at dusk", raw["title"].AsString);
    }

    [Fact]
    public async Task Invalid_title_or_caption_is_422_and_stores_nothing()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3);
        var img = await h.InsertUploadedImageAsync(files: false, title: "Stored");
        await Is(h, s => s.UpdateTextAsync(img.Id, null, "Stored caption"));
        await h.SettleAsync();

        var blank = await Is(h, s => s.UpdateTextAsync(img.Id, "   ", null));
        var tooLong = await Is(h, s => s.UpdateTextAsync(img.Id, new string('t', 201), null));
        var longCaption = await Is(h, s => s.UpdateTextAsync(img.Id, "Fine", new string('c', 2001)));

        Assert.Equal((OperationStatus.Refused, 422, "Enter a title."), (blank.Status, blank.StatusCode, blank.Reason));
        Assert.Equal((OperationStatus.Refused, 422, "The title can be at most 200 characters."), (tooLong.Status, tooLong.StatusCode, tooLong.Reason));
        Assert.Equal((OperationStatus.Refused, 422, "The caption can be at most 2000 characters."), (longCaption.Status, longCaption.StatusCode, longCaption.Reason));
        var stored = await ImageAsync(h, img.Id);
        Assert.Equal(("Stored", "Stored caption"), (stored.Title, stored.Caption));
        Assert.Equal(3, h.LogLines.Count(l => l.StartsWith("op.timing db.image.update ", StringComparison.Ordinal) && l.EndsWith(" refused", StringComparison.Ordinal)));
        Assert.Empty(await h.SettleAsync());

        // At the limits exactly: accepted.
        var max = await Is(h, s => s.UpdateTextAsync(img.Id, new string('t', 200), new string('c', 2000)));
        Assert.Equal(OperationStatus.Ok, max.Status);
    }

    [Fact]
    public async Task P_TEXT_is_exact_for_a_cover_in_a_public_gallery_and_a_non_cover()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3);
        var a = await h.InsertUploadedImageAsync(files: false);
        var b = await h.InsertUploadedImageAsync(files: false);
        var pub = (await Gs(h, s => s.CreateAsync("Coast"))).GalleryId!;
        var priv = (await Gs(h, s => s.CreateAsync("Drafts"))).GalleryId!;
        await Gs(h, s => s.AddAsync(pub, [a.Id, b.Id]));
        await Gs(h, s => s.PublishAsync(pub, confirmed: true));
        await Gs(h, s => s.AddAsync(priv, [b.Id, a.Id]));
        await h.SettleAsync();

        // a is the cover of the listed gallery (items[0]) and a non-cover in the private one.
        var r = await Is(h, s => s.UpdateTextAsync(a.Id, "New title", null));
        var purged = await h.SettleAsync();
        Assert.Equal(OperationStatus.Ok, r.Status);
        SetEq([Gp(pub, "coast"), Pp(pub, "coast", a.Id), Home, Gp(priv, "drafts"), Pp(priv, "drafts", a.Id)], purged);

        // b is a non-cover in the public gallery and the cover of the private one: no "/".
        var r2 = await Is(h, s => s.UpdateTextAsync(b.Id, null, "A caption"));
        var purged2 = await h.SettleAsync();
        Assert.Equal(OperationStatus.Ok, r2.Status);
        SetEq([Gp(pub, "coast"), Pp(pub, "coast", b.Id), Gp(priv, "drafts"), Pp(priv, "drafts", b.Id)], purged2);
    }

    [Fact]
    public async Task An_image_in_no_gallery_with_a_page_purges_nothing()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3);
        var img = await h.InsertUploadedImageAsync(files: false);

        var r = await Is(h, s => s.UpdateTextAsync(img.Id, "Alone", "Nobody sees this yet"));
        await h.SettleAsync();

        Assert.Equal(OperationStatus.Ok, r.Status);
        Assert.Equal("Alone", (await ImageAsync(h, img.Id)).Title);
        Assert.Equal(0, h.Recorder.CallCount);
    }

    [Fact]
    public async Task Bulk_add_of_PageSize_plus_one_ids_is_422_with_nothing_written()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3);
        var img = await h.InsertUploadedImageAsync(files: false);
        var gid = (await Gs(h, s => s.CreateAsync("Cap"))).GalleryId!;
        await h.SettleAsync();
        var ids = Enumerable.Repeat(img.Id, 101).ToList();

        var existing = await Is(h, s => s.BulkAddAsync(ids, gid, null));
        var toNew = await Is(h, s => s.BulkAddAsync(ids, null, "Never made"));
        var none = await Is(h, s => s.BulkAddAsync([], gid, null));

        Assert.Equal((OperationStatus.Refused, 422, "Select at most 100 photos."), (existing.Status, existing.StatusCode, existing.Reason));
        Assert.Equal((OperationStatus.Refused, 422, "Select at most 100 photos."), (toNew.Status, toNew.StatusCode, toNew.Reason));
        Assert.Equal((OperationStatus.Refused, 422, "Select at least one photo."), (none.Status, none.StatusCode, none.Reason));
        Assert.Empty((await h.Galleries.Find(g => g.Id == gid).FirstAsync()).Items);
        Assert.Equal(1, await h.Galleries.CountDocumentsAsync(FilterDefinition<GalleryDoc>.Empty));
        Assert.Empty((await ImageAsync(h, img.Id)).GalleryIds);
        Assert.Empty(await h.SettleAsync());
    }

    [Fact]
    public async Task Bulk_add_to_a_new_gallery_creates_it_private_and_reuses_the_single_item_gate()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3);
        var a = await h.InsertUploadedImageAsync(files: false);
        var b = await h.InsertUploadedImageAsync(files: false);

        var r = await Is(h, s => s.BulkAddAsync([a.Id, b.Id, a.Id, "zzzzzzzzzzzzzzzzzzzzzzzzzz"], null, "  Fresh Set "));
        var purged = await h.SettleAsync();

        Assert.Equal(OperationStatus.Ok, r.Status);
        Assert.Equal((2, 2, "Fresh Set"), (r.Added, r.Skipped, r.GalleryTitle));
        var g = await h.Galleries.Find(x => x.Id == r.GalleryId).FirstAsync();
        Assert.Equal("private", g.Visibility);
        Assert.Equal(new[] { a.Id, b.Id }, g.Items.ToArray());
        Assert.Equal(new[] { g.Id }, (await h.SiteAsync()).HomeOrder.ToArray());
        SetEq([Gp(g.Id, "fresh-set"), Pp(g.Id, "fresh-set", a.Id), Pp(g.Id, "fresh-set", b.Id)], purged);
        // One request, ONE EdgeWork (contracts/edge.md): one edge.lag line for it.
        Assert.Single(h.LogLines, l => l.StartsWith("edge.lag gallery.add ", StringComparison.Ordinal));

        var bad = await Is(h, s => s.BulkAddAsync([a.Id], null, "   "));
        Assert.Equal((OperationStatus.Refused, 422, "Enter a title."), (bad.Status, bad.StatusCode, bad.Reason));
        Assert.Equal(1, await h.Galleries.CountDocumentsAsync(FilterDefinition<GalleryDoc>.Empty));
        Assert.Equal(2, h.LogLines.Count(l => l.StartsWith("op.timing library.bulk.add ", StringComparison.Ordinal)));
    }
}
