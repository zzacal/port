using Gallery.Core.Images;
using Gallery.Web.Data;
using Gallery.Web.Observability;
using Gallery.Web.Site;
using Gallery.Web.Storage;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Gallery.Web.Library;

/// <summary>One gallery as the library sees it: the picker option and the status lines (data-model "GalleryRef").</summary>
public sealed record GalleryRef(string Id, string Slug, string Title, string Visibility, int PhotoCount)
{
    public bool IsPublic => Visibility == "public";
}

/// <summary>One library tile (data-model "LibraryPage").</summary>
/// <param name="Galleries">The galleries holding the image, in home order.</param>
/// <param name="ViewHref">The photo page in the first gallery (home order) holding it, or null (FR-005, UI-1).</param>
public sealed record LibraryTile(
    ImageView Image, string? Caption, string Status, string? ViewHref,
    int GalleryCount, int PublicGalleryCount, IReadOnlyList<GalleryRef> Galleries);

/// <summary>One page of the library (data-model "LibraryPage").</summary>
/// <param name="Open">The image of <c>?open={iid}</c> (on this page or not), or null when unknown or tombstoned.</param>
public sealed record LibraryPageData(
    int Page, int TotalPages, long Total, IReadOnlyList<LibraryTile> Tiles, IReadOnlyList<GalleryRef> Galleries,
    LibraryTile? Open = null);

/// <summary>
/// The library read (research R-04, tasks.md T028): four timed reads, <c>db.library.count</c>,
/// <c>db.library.page</c>, <c>db.galleries.list</c> and <c>db.site.order</c>. Tombstoned images
/// (<c>deletingAt</c>) are hidden; the order is <c>{createdAt:-1, _id:-1}</c> (index
/// <c>library_page</c>); <c>PageSize</c> tiles per page; a page past the end renders the last page.
/// Status lines and View links are computed in memory from the gallery list. Scoped.
/// </summary>
public sealed class LibraryReader(IMongoDatabase db, OpTimer timer, LibraryOptions options, StorageOptions storage)
{
    private readonly IMongoCollection<ImageDoc> _images = db.GetCollection<ImageDoc>("images");
    private readonly IMongoCollection<BsonDocument> _galleries = db.GetCollection<BsonDocument>("galleries");
    private readonly IMongoCollection<BsonDocument> _site = db.GetCollection<BsonDocument>("site");

    private static readonly FilterDefinition<ImageDoc> Live = Builders<ImageDoc>.Filter.Exists(i => i.DeletingAt, false);

    /// <summary>The FR-007 status line.</summary>
    public static string StatusLine(int galleries, int publicGalleries) => galleries switch
    {
        0 => "Not in a gallery",
        _ when publicGalleries == 0 => $"In {galleries} {Plural(galleries, "gallery", "galleries")}",
        _ => $"In {galleries} {Plural(galleries, "gallery", "galleries")} · public in {publicGalleries}",
    };

    private static string Plural(int n, string one, string many) => n == 1 ? one : many;

    /// <summary>
    /// Page <paramref name="page"/> (1-based; below 1 reads page 1, past the end reads the last page).
    /// <paramref name="open"/> names the side panel's image; when it is not on this page it costs one
    /// more <c>db.library.page</c> read. An unknown or tombstoned id leaves the panel closed.
    /// </summary>
    public async Task<LibraryPageData> PageAsync(int page, string? open = null)
    {
        var total = await timer.Time("db.library.count", () => _images.CountDocumentsAsync(Live), isDbRead: true);
        var size = options.PageSize;
        var totalPages = Math.Max(1, (int)((total + size - 1) / size));
        page = Math.Clamp(page, 1, totalPages);

        var docs = await timer.Time("db.library.page", () => _images.Find(Live)
            .Sort(Builders<ImageDoc>.Sort.Descending(i => i.CreatedAt).Descending(i => i.Id))
            .Skip((page - 1) * size)
            .Limit(size)
            .Project<ImageDoc>(Builders<ImageDoc>.Projection
                .Include(i => i.Id).Include(i => i.Title).Include(i => i.Caption)
                .Include(i => i.Width).Include(i => i.Height).Include(i => i.Placeholder)
                .Include(i => i.Derivatives).Include(i => i.GalleryIds).Include(i => i.CreatedAt))
            .ToListAsync(), isDbRead: true);

        var galleries = await GalleriesAsync();
        var byId = galleries.ToDictionary(g => g.Id, StringComparer.Ordinal);
        var rank = galleries.Select((g, i) => (g.Id, i)).ToDictionary(x => x.Id, x => x.i, StringComparer.Ordinal);

        var url = PublicEndpoints.MakeUrl(storage);
        LibraryTile ToTile(ImageDoc d)
        {
            var holders = d.GalleryIds
                .Distinct(StringComparer.Ordinal)
                .Where(byId.ContainsKey)
                .OrderBy(id => rank[id])
                .Select(id => byId[id])
                .ToList();
            var publicCount = holders.Count(g => g.IsPublic);
            var first = holders.FirstOrDefault();
            return new LibraryTile(
                ViewMapper.ToView(d, url),
                d.Caption,
                StatusLine(holders.Count, publicCount),
                first is null ? null : $"/g/{first.Id}/{first.Slug}/{d.Id}",
                holders.Count,
                publicCount,
                holders);
        }
        var tiles = docs.Select(ToTile).ToList();

        LibraryTile? openTile = null;
        if (!string.IsNullOrEmpty(open))
        {
            var openDoc = docs.FirstOrDefault(d => d.Id == open)
                ?? await timer.Time("db.library.page", () => _images
                    .Find(Live & Builders<ImageDoc>.Filter.Eq(i => i.Id, open)).FirstOrDefaultAsync(), isDbRead: true);
            if (openDoc is not null) openTile = ToTile(openDoc);
        }

        return new LibraryPageData(page, totalPages, total, tiles, galleries, openTile);
    }

    /// <summary>
    /// Every gallery as a <see cref="GalleryRef"/>, in <c>site.homeOrder</c> order (galleries missing
    /// from it follow, by title). Two reads: <c>db.galleries.list</c> (photo count by <c>$size</c>) and
    /// <c>db.site.order</c>.
    /// </summary>
    public async Task<IReadOnlyList<GalleryRef>> GalleriesAsync()
    {
        var projection = new BsonDocument
        {
            { "slug", 1 },
            { "title", 1 },
            { "visibility", 1 },
            { "count", new BsonDocument("$size", new BsonDocument("$ifNull", new BsonArray { "$items", new BsonArray() })) },
        };
        var docs = await timer.Time("db.galleries.list", () =>
            _galleries.Find(FilterDefinition<BsonDocument>.Empty).Project(projection).ToListAsync(), isDbRead: true);

        var site = await timer.Time("db.site.order", () => _site
            .Find(Builders<BsonDocument>.Filter.Eq("_id", SiteProjector.SiteId))
            .Project(Builders<BsonDocument>.Projection.Include("homeOrder"))
            .FirstOrDefaultAsync(), isDbRead: true);
        var order = site is not null && site.TryGetValue("homeOrder", out var o) && o.IsBsonArray
            ? o.AsBsonArray.Where(v => v.IsString).Select(v => v.AsString).ToList()
            : [];
        var rank = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var id in order) rank.TryAdd(id, rank.Count);

        return docs
            .Select(d => new GalleryRef(
                d["_id"].AsString,
                d.GetValue("slug", "").AsString,
                d.GetValue("title", "").AsString,
                d.GetValue("visibility", "private").AsString,
                d.GetValue("count", 0).ToInt32()))
            .OrderBy(g => rank.TryGetValue(g.Id, out var r) ? r : int.MaxValue)
            .ThenBy(g => g.Title, StringComparer.Ordinal)
            .ThenBy(g => g.Id, StringComparer.Ordinal)
            .ToList();
    }
}
