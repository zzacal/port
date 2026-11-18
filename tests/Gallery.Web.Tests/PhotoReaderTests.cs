using Gallery.Web.Data;
using Gallery.Web.Library;
using Gallery.Web.Tests.Fixtures;
using MongoDB.Driver;

namespace Gallery.Web.Tests;

/// <summary>
/// 017 T008 (research R-04, data-model "LibraryReader.PhotoAsync"): the edit screen's read. A known
/// photo's tile equals the library page's tile for it (holders in home order); an unknown or
/// tombstoned id answers null after exactly one <c>db.library.page</c> read and no gallery read; a
/// known id costs exactly three timed db reads.
/// </summary>
[Collection("s3")]
public class PhotoReaderTests(S3ProxyFixture s3, MongoFixture mongo) : IClassFixture<MongoFixture>
{
    private static async Task<string> CreateGalleryAsync(LibraryHost h, string title)
    {
        var r = await h.RunAsync<GalleryService, OperationResult>(s => s.CreateAsync(title));
        Assert.True(r.IsOk);
        return r.GalleryId!;
    }

    private static async Task AddAsync(LibraryHost h, string gid, string iid) =>
        Assert.True((await h.RunAsync<GalleryService, OperationResult>(s => s.AddAsync(gid, [iid]))).IsOk);

    /// <summary>The <c>op.timing</c> lines logged while <paramref name="call"/> ran, and its result.</summary>
    private static async Task<(T Result, string[] Lines)> LoggedAsync<T>(LibraryHost h, Func<Task<T>> call)
    {
        await h.EdgeIdleAsync();
        int before;
        lock (h.LogLines) before = h.LogLines.Count;
        var result = await call();
        string[] lines;
        lock (h.LogLines)
            lines = h.LogLines.Skip(before).SelectMany(l => l.Split('\n'))
                .Where(l => l.StartsWith("op.timing ", StringComparison.Ordinal)).ToArray();
        return (result, lines);
    }

    private static int Count(string[] lines, string op) =>
        lines.Count(l => l.StartsWith($"op.timing {op} ", StringComparison.Ordinal));

    private static string[] DbOps(string[] lines) =>
        lines.Select(l => l.Split(' ')[1]).Where(op => op.StartsWith("db.", StringComparison.Ordinal)).ToArray();

    /// <summary>
    /// Record equality of two tiles. The list members (<c>Image.Variants</c>, <c>Galleries</c>) are
    /// compared as sequences first, then shared so the record's own Equals covers every other member.
    /// </summary>
    private static void TileEqual(LibraryTile expected, LibraryTile actual)
    {
        Assert.Equal(expected.Image.Variants, actual.Image.Variants);
        Assert.Equal(expected.Galleries, actual.Galleries);
        Assert.Equal(expected with { Image = expected.Image with { Variants = actual.Image.Variants }, Galleries = actual.Galleries }, actual);
    }

    [Fact]
    public async Task Known_photo_in_two_galleries_equals_the_page_tile_with_holders_in_home_order()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3);
        var img = await h.InsertUploadedImageAsync(title: "Harbour");
        var a = await CreateGalleryAsync(h, "Alpha");
        var b = await CreateGalleryAsync(h, "Bravo");
        var c = await CreateGalleryAsync(h, "Charlie");
        await AddAsync(h, a, img.Id);
        await AddAsync(h, c, img.Id);
        Assert.True((await h.RunAsync<GalleryService, OperationResult>(s => s.ReorderHomeAsync([c, b, a]))).IsOk);
        await h.SettleAsync();

        var (photo, galleries) = await h.RunAsync<LibraryReader, (LibraryTile? Photo, IReadOnlyList<GalleryRef> Galleries)>(r => r.PhotoAsync(img.Id));
        var page = await h.RunAsync<LibraryReader, LibraryPageData>(r => r.PageAsync(1));

        Assert.NotNull(photo);
        Assert.Equal([c, b, a], galleries.Select(g => g.Id).ToArray());
        Assert.Equal(page.Galleries, galleries);
        Assert.Equal([c, a], photo.Galleries.Select(g => g.Id).ToArray());
        Assert.Equal(galleries.Where(g => g.Id == c || g.Id == a).ToArray(), photo.Galleries);
        var cRef = galleries.Single(g => g.Id == c);
        Assert.Equal($"/g/{c}/{cRef.Slug}/{img.Id}", photo.ViewHref);

        var pageTile = page.Tiles.Single(t => t.Image.Id == img.Id);
        Assert.Equal(pageTile.ViewHref, photo.ViewHref);
        TileEqual(pageTile, photo);
    }

    [Fact]
    public async Task Unknown_id_is_null_after_one_image_read_and_no_gallery_read()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3);
        await h.InsertUploadedImageAsync();
        await CreateGalleryAsync(h, "Alpha");

        var (result, lines) = await LoggedAsync(h, () =>
            h.RunAsync<LibraryReader, (LibraryTile? Photo, IReadOnlyList<GalleryRef> Galleries)>(r => r.PhotoAsync("0123456789abcdefghjkmnpqrs")));

        Assert.Null(result.Photo);
        Assert.Empty(result.Galleries);
        Assert.Equal(1, Count(lines, "db.library.page"));
        Assert.Equal(0, Count(lines, "db.galleries.list"));
        Assert.Equal(["db.library.page"], DbOps(lines));
    }

    [Fact]
    public async Task Tombstoned_id_is_null_after_one_image_read_and_no_gallery_read()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3);
        var img = await h.InsertUploadedImageAsync();
        await CreateGalleryAsync(h, "Alpha");
        await h.Images.UpdateOneAsync(i => i.Id == img.Id, Builders<ImageDoc>.Update.Set(i => i.DeletingAt, DateTime.UtcNow));

        var (result, lines) = await LoggedAsync(h, () =>
            h.RunAsync<LibraryReader, (LibraryTile? Photo, IReadOnlyList<GalleryRef> Galleries)>(r => r.PhotoAsync(img.Id)));

        Assert.Null(result.Photo);
        Assert.Empty(result.Galleries);
        Assert.Equal(1, Count(lines, "db.library.page"));
        Assert.Equal(0, Count(lines, "db.galleries.list"));
        Assert.Equal(["db.library.page"], DbOps(lines));
    }

    [Fact]
    public async Task Known_id_costs_exactly_three_timed_db_reads()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3);
        var img = await h.InsertUploadedImageAsync();
        var a = await CreateGalleryAsync(h, "Alpha");
        await AddAsync(h, a, img.Id);
        await h.SettleAsync();

        var (result, lines) = await LoggedAsync(h, () =>
            h.RunAsync<LibraryReader, (LibraryTile? Photo, IReadOnlyList<GalleryRef> Galleries)>(r => r.PhotoAsync(img.Id)));

        Assert.NotNull(result.Photo);
        Assert.Equal(
            ["db.galleries.list", "db.library.page", "db.site.order"],
            DbOps(lines).Order(StringComparer.Ordinal).ToArray());
    }
}
