using Gallery.Web.Data;
using Gallery.Web.Library;
using Gallery.Web.Tests.Fixtures;
using Gallery.Web.Workspace;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;

namespace Gallery.Web.Tests;

/// <summary>
/// 020 T031 (research R-02, R-03; data-model "Read models"): <see cref="WorkspaceReader"/> returns the
/// two counts from one aggregate, chunks newest first with a keyset cursor, the unplaced filter,
/// never a tombstoned photo, gallery members in <c>items</c> order, and costs exactly the named
/// timed reads per call. The reader is not registered yet (T034), so each test builds it from the
/// host's scoped services.
/// </summary>
[Collection("s3")]
public class WorkspaceReaderTests(S3ProxyFixture s3, MongoFixture mongo) : IClassFixture<MongoFixture>
{
    private static readonly DateTime T0 = new(2026, 3, 1, 12, 0, 0, DateTimeKind.Utc);

    private static async Task<T> ReadAsync<T>(LibraryHost h, Func<WorkspaceReader, Task<T>> call)
    {
        using var scope = h.Scope();
        return await call(ActivatorUtilities.CreateInstance<WorkspaceReader>(scope.ServiceProvider));
    }

    /// <summary>The result of <paramref name="call"/> and the timed db ops it logged, in order.</summary>
    private static async Task<(T Result, string[] Ops)> LoggedAsync<T>(LibraryHost h, Func<WorkspaceReader, Task<T>> call)
    {
        await h.EdgeIdleAsync();
        int before;
        lock (h.LogLines) before = h.LogLines.Count;
        var result = await ReadAsync(h, call);
        string[] ops;
        lock (h.LogLines)
            ops = h.LogLines.Skip(before).SelectMany(l => l.Split('\n'))
                .Where(l => l.StartsWith("op.timing db.", StringComparison.Ordinal))
                .Select(l => l.Split(' ')[1]).ToArray();
        return (result, ops);
    }

    private static Task<ImageDoc> AtAsync(LibraryHost h, int minutes, string? title = null) =>
        h.InsertUploadedImageAsync(T0.AddMinutes(minutes), title, files: false);

    private static Task Tombstone(LibraryHost h, string iid) =>
        h.Images.UpdateOneAsync(i => i.Id == iid, Builders<ImageDoc>.Update.Set(i => i.DeletingAt, DateTime.UtcNow));

    [Fact]
    public async Task Counts_are_one_aggregate_read_of_live_and_live_unplaced()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3);
        var a = await AtAsync(h, 1);
        var b = await AtAsync(h, 2);
        await AtAsync(h, 3);
        var gone = await AtAsync(h, 4);
        var goneUnplaced = await AtAsync(h, 5);
        await GalleryHtml.GalleryAsync(h, "Alpha", [a.Id, b.Id, gone.Id]);
        await Tombstone(h, gone.Id);
        await Tombstone(h, goneUnplaced.Id);

        var (counts, ops) = await LoggedAsync(h, r => r.CountsAsync());

        Assert.Equal(new WorkspaceCounts(3, 1), counts);
        Assert.Equal(["db.library.count"], ops);
    }

    [Fact]
    public async Task Counts_of_an_empty_library_are_zero()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3);
        var (counts, ops) = await LoggedAsync(h, r => r.CountsAsync());
        Assert.Equal(new WorkspaceCounts(0, 0), counts);
        Assert.Equal(["db.library.count"], ops);
    }

    [Fact]
    public async Task Chunks_walk_newest_first_with_the_cursor_and_detect_more_with_one_read_each()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3, extraOverrides: new() { ["Library:PageSize"] = "2" });
        var old = await AtAsync(h, 1);
        var tieA = await AtAsync(h, 2);
        var tieB = await AtAsync(h, 2); // same createdAt: _id desc breaks the tie across the chunk edge
        var gone = await AtAsync(h, 3);
        var newest = await AtAsync(h, 4);
        await Tombstone(h, gone.Id);
        var ties = new[] { tieA.Id, tieB.Id }.OrderDescending(StringComparer.Ordinal).ToArray();
        string[] expected = [newest.Id, ties[0], ties[1], old.Id];

        var (first, ops1) = await LoggedAsync(h, r => r.ChunkAsync(false, null));
        Assert.NotNull(first);
        Assert.Equal(["db.library.page"], ops1);
        Assert.Equal(expected[..2], first.Tiles.Select(t => t.Id).ToArray());
        var tie0 = ties[0] == tieA.Id ? tieA : tieB;
        Assert.Equal($"{tie0.CreatedAt.Ticks}.{tie0.Id}", first.Next);

        var (second, ops2) = await LoggedAsync(h, r => r.ChunkAsync(false, first.Next));
        Assert.NotNull(second);
        Assert.Equal(["db.library.page"], ops2);
        Assert.Equal(expected[2..], second.Tiles.Select(t => t.Id).ToArray());
        Assert.Null(second.Next); // exactly PageSize left: no further chunk
    }

    [Fact]
    public async Task A_chunk_carries_the_tile_data()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3);
        var img = await AtAsync(h, 1, "Harbour");
        await h.Images.UpdateOneAsync(i => i.Id == img.Id, Builders<ImageDoc>.Update.Set(i => i.Caption, "Low tide"));
        var gid = await GalleryHtml.GalleryAsync(h, "Alpha", [img.Id]);

        var chunk = await ReadAsync(h, r => r.ChunkAsync(false, null));

        var tile = Assert.Single(chunk!.Tiles);
        Assert.Equal(img.Id, tile.Id);
        Assert.Equal("Harbour", tile.Title);
        Assert.Equal("Low tide", tile.Caption);
        Assert.Equal([gid], tile.GalleryIds);
        Assert.Equal(img.Derivatives.Count, tile.Image.Variants.Count);
        Assert.Equal(img.Width, tile.Image.Width);
        Assert.Null(chunk.Next);
    }

    [Fact]
    public async Task The_unplaced_chunk_has_only_live_photos_in_no_gallery()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3);
        var u1 = await AtAsync(h, 1);
        var placed = await AtAsync(h, 2);
        var u2 = await AtAsync(h, 3);
        var gone = await AtAsync(h, 4);
        await GalleryHtml.GalleryAsync(h, "Alpha", [placed.Id]);
        await Tombstone(h, gone.Id);

        var (chunk, ops) = await LoggedAsync(h, r => r.ChunkAsync(true, null));

        Assert.Equal([u2.Id, u1.Id], chunk!.Tiles.Select(t => t.Id).ToArray());
        Assert.Null(chunk.Next);
        Assert.Equal(["db.library.page"], ops);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("123")]
    [InlineData("123.")]
    [InlineData(".abc")]
    [InlineData("-5.abc")]
    [InlineData("1x.abc")]
    [InlineData("99999999999999999999.abc")]
    public async Task A_malformed_cursor_answers_null_with_no_read(string before)
    {
        Assert.False(WorkspaceReader.TryParseCursor(before, out _, out _));
        await using var h = await LibraryHost.CreateAsync(mongo, s3);
        await AtAsync(h, 1);

        var (chunk, ops) = await LoggedAsync(h, r => r.ChunkAsync(false, before));

        Assert.Null(chunk);
        Assert.Empty(ops);
    }

    [Fact]
    public void A_cursor_round_trips()
    {
        var doc = new ImageDoc { Id = "abc.def", CreatedAt = T0 };
        Assert.True(WorkspaceReader.TryParseCursor(WorkspaceReader.Cursor(doc), out var at, out var id));
        Assert.Equal(T0, at);
        Assert.Equal(DateTimeKind.Utc, at.Kind);
        Assert.Equal("abc.def", id);
    }

    [Fact]
    public async Task Members_are_the_live_items_in_gallery_order_with_one_read()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3);
        var a = await AtAsync(h, 1);
        var b = await AtAsync(h, 2);
        var c = await AtAsync(h, 3);
        var gone = await AtAsync(h, 4);
        await AtAsync(h, 5); // not a member
        var gid = await GalleryHtml.GalleryAsync(h, "Alpha", [c.Id, a.Id, gone.Id, b.Id]);
        Assert.True((await GalleryHtml.Gs(h, s => s.ReorderAsync(gid, [b.Id, gone.Id, c.Id, a.Id]))).IsOk);
        await Tombstone(h, gone.Id);
        var g = await GalleryHtml.LoadAsync(h, gid);

        var (members, ops) = await LoggedAsync(h, r => r.MembersAsync(g));

        Assert.Equal([b.Id, c.Id, a.Id], members.Select(t => t.Id).ToArray());
        Assert.Equal(["db.library.page"], ops);
    }

    [Fact]
    public async Task Tile_is_one_read_and_null_for_unknown_or_tombstoned()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3);
        var img = await AtAsync(h, 1, "Harbour");
        var gone = await AtAsync(h, 2);
        await Tombstone(h, gone.Id);

        var (tile, ops) = await LoggedAsync(h, r => r.TileAsync(img.Id));
        Assert.Equal(img.Id, tile!.Id);
        Assert.Equal("Harbour", tile.Title);
        Assert.Equal(["db.library.page"], ops);

        var (none, ops2) = await LoggedAsync(h, r => r.TileAsync(gone.Id));
        Assert.Null(none);
        Assert.Equal(["db.library.page"], ops2);

        var (unknown, ops3) = await LoggedAsync(h, r => r.TileAsync("0123456789abcdefghjkmnpqrs"));
        Assert.Null(unknown);
        Assert.Equal(["db.library.page"], ops3);
    }

    [Fact]
    public async Task Galleries_and_gallery_call_the_existing_reads()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3);
        var img = await AtAsync(h, 1);
        var a = await GalleryHtml.GalleryAsync(h, "Alpha", [img.Id]);
        var b = await GalleryHtml.GalleryAsync(h, "Bravo", []);

        var (galleries, ops) = await LoggedAsync(h, r => r.GalleriesAsync());
        var expected = await h.RunAsync<LibraryReader, IReadOnlyList<GalleryRef>>(r => r.GalleriesAsync());
        Assert.Equal(expected, galleries);
        Assert.Equal([a, b], galleries.Select(g => g.Id).ToArray());
        Assert.Equal(["db.galleries.list", "db.site.order"], ops);

        var (g, ops2) = await LoggedAsync(h, r => r.GalleryAsync(a));
        Assert.Equal(a, g!.Id);
        Assert.Equal([img.Id], g.Items);
        Assert.Equal(["db.gallery.load"], ops2);

        var (none, ops3) = await LoggedAsync(h, r => r.GalleryAsync("0123456789abcdefghjkmnpqrs"));
        Assert.Null(none);
        Assert.Equal(["db.gallery.load"], ops3);
    }
}
