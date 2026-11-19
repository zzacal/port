using System.Globalization;
using Gallery.Web.Data;
using Gallery.Web.Library;
using Gallery.Web.Observability;
using Gallery.Web.Site;
using Gallery.Web.Storage;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Gallery.Web.Workspace;

/// <summary>
/// The workspace reads (research R-02, R-03; tasks T031). Each method is a fixed number of timed
/// reads: <see cref="CountsAsync"/> one <c>db.library.count</c> aggregate; <see cref="ChunkAsync"/>,
/// <see cref="MembersAsync"/> and <see cref="TileAsync"/> one <c>db.library.page</c> find each;
/// <see cref="GalleriesAsync"/> is <see cref="LibraryReader.GalleriesAsync"/> (two reads) and
/// <see cref="GalleryAsync"/> is <see cref="GalleryService.LoadAsync"/> (<c>db.gallery.load</c>).
/// Tombstoned images (<c>deletingAt</c>) are never returned (<see cref="LibraryReader.Live"/>). Scoped.
/// </summary>
public sealed class WorkspaceReader(
    IMongoDatabase db, OpTimer timer, LibraryOptions options, StorageOptions storage,
    LibraryReader library, GalleryService galleries)
{
    private readonly IMongoCollection<ImageDoc> _images = db.GetCollection<ImageDoc>("images");

    /// <summary>Live photos in no gallery (an empty or absent <c>galleryIds</c>).</summary>
    private static readonly FilterDefinition<ImageDoc> Unplaced = Builders<ImageDoc>.Filter.Exists("galleryIds.0", false);

    /// <summary>The <c>library_page</c> index order: <c>{createdAt:-1, _id:-1}</c>.</summary>
    private static readonly SortDefinition<ImageDoc> Newest =
        Builders<ImageDoc>.Sort.Descending(i => i.CreatedAt).Descending(i => i.Id);

    /// <summary>The chunk size and bulk cap, <c>Library:PageSize</c>.</summary>
    public int PageSize => options.PageSize;

    /// <summary>Both counts in ONE aggregate read, timed <c>db.library.count</c>.</summary>
    public async Task<WorkspaceCounts> CountsAsync()
    {
        var group = new BsonDocument
        {
            { "_id", BsonNull.Value },
            { "total", new BsonDocument("$sum", 1) },
            {
                "unplaced", new BsonDocument("$sum", new BsonDocument("$cond", new BsonArray
                {
                    new BsonDocument("$eq", new BsonArray
                    {
                        new BsonDocument("$size", new BsonDocument("$ifNull", new BsonArray { "$galleryIds", new BsonArray() })),
                        0,
                    }),
                    1,
                    0,
                }))
            },
        };
        var doc = await timer.Time("db.library.count",
            () => _images.Aggregate().Match(LibraryReader.Live).Group(group).FirstOrDefaultAsync(), isDbRead: true);
        return doc is null
            ? new WorkspaceCounts(0, 0)
            : new WorkspaceCounts(doc["total"].ToInt64(), doc["unplaced"].ToInt64());
    }

    /// <summary>The cursor of <paramref name="d"/>: <c>{createdAt ticks}.{id}</c>.</summary>
    public static string Cursor(ImageDoc d) => $"{d.CreatedAt.Ticks.ToString(CultureInfo.InvariantCulture)}.{d.Id}";

    /// <summary>
    /// Parses a <c>{createdAt ticks}.{id}</c> cursor. False for anything else (the endpoint answers 400).
    /// </summary>
    public static bool TryParseCursor(string? cursor, out DateTime createdAt, out string id)
    {
        createdAt = default;
        id = string.Empty;
        if (string.IsNullOrEmpty(cursor)) return false;
        var dot = cursor.IndexOf('.');
        if (dot <= 0 || dot == cursor.Length - 1) return false;
        if (!long.TryParse(cursor.AsSpan(0, dot), NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)) return false;
        if (ticks > DateTime.MaxValue.Ticks) return false;
        createdAt = new DateTime(ticks, DateTimeKind.Utc);
        id = cursor[(dot + 1)..];
        return true;
    }

    /// <summary>
    /// One chunk of All photos (<paramref name="unplaced"/> false) or Not in a gallery (true), newest
    /// first, after <paramref name="before"/> (null: the first chunk). ONE <c>db.library.page</c> find
    /// with <c>Limit(PageSize + 1)</c> to know whether more exist. A malformed
    /// <paramref name="before"/> answers null with no read (the caller answers 400).
    /// </summary>
    public async Task<WorkspaceChunk?> ChunkAsync(bool unplaced, string? before)
    {
        var f = Builders<ImageDoc>.Filter;
        var filter = LibraryReader.Live;
        if (unplaced) filter &= Unplaced;
        if (before is not null)
        {
            if (!TryParseCursor(before, out var at, out var id)) return null;
            filter &= f.Lt(i => i.CreatedAt, at) | (f.Eq(i => i.CreatedAt, at) & f.Lt(i => i.Id, id));
        }

        var size = options.PageSize;
        var docs = await timer.Time("db.library.page", () => _images.Find(filter)
            .Sort(Newest)
            .Limit(size + 1)
            .Project<ImageDoc>(LibraryReader.TileFields)
            .ToListAsync(), isDbRead: true);

        var more = docs.Count > size;
        if (more) docs.RemoveRange(size, docs.Count - size);
        var url = PublicEndpoints.MakeUrl(storage);
        return new WorkspaceChunk(docs.Select(d => Tile(d, url)).ToList(), more ? Cursor(docs[^1]) : null);
    }

    /// <summary>
    /// Every live member of <paramref name="g"/> in <c>g.Items</c> order: ONE <c>db.library.page</c>
    /// find (also for an empty gallery, so the read count is fixed). Missing or tombstoned items are skipped.
    /// </summary>
    public async Task<IReadOnlyList<WorkspaceTile>> MembersAsync(GalleryDoc g)
    {
        var ids = g.Items.Distinct(StringComparer.Ordinal).ToList();
        var docs = await timer.Time("db.library.page", () => _images
            .Find(LibraryReader.Live & Builders<ImageDoc>.Filter.In(i => i.Id, ids))
            .Project<ImageDoc>(LibraryReader.TileFields)
            .ToListAsync(), isDbRead: true);

        var byId = docs.ToDictionary(d => d.Id, StringComparer.Ordinal);
        var url = PublicEndpoints.MakeUrl(storage);
        return ids.Where(byId.ContainsKey).Select(id => Tile(byId[id], url)).ToList();
    }

    /// <summary>One live photo's tile: ONE <c>db.library.page</c> find. Unknown or tombstoned → null.</summary>
    public async Task<WorkspaceTile?> TileAsync(string iid)
    {
        var doc = await timer.Time("db.library.page", () => _images
            .Find(LibraryReader.Live & Builders<ImageDoc>.Filter.Eq(i => i.Id, iid))
            .Project<ImageDoc>(LibraryReader.TileFields)
            .FirstOrDefaultAsync(), isDbRead: true);
        return doc is null ? null : Tile(doc, PublicEndpoints.MakeUrl(storage));
    }

    /// <summary>Every gallery in home order: <see cref="LibraryReader.GalleriesAsync"/> (two reads).</summary>
    public Task<IReadOnlyList<GalleryRef>> GalleriesAsync() => library.GalleriesAsync();

    /// <summary>One gallery: <see cref="GalleryService.LoadAsync"/> (<c>db.gallery.load</c>); unknown → null.</summary>
    public Task<GalleryDoc?> GalleryAsync(string gid) => galleries.LoadAsync(gid);

    private static WorkspaceTile Tile(ImageDoc d, Func<string, string> url) =>
        new(ViewMapper.ToView(d, url), d.Caption, d.GalleryIds);
}
