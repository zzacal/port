using Gallery.Web.Data;
using Gallery.Web.Observability;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Gallery.Web.Library;

/// <summary>One gallery as the workspace sees it: a collections-list row and a checklist option (data-model "GalleryRef").</summary>
public sealed record GalleryRef(string Id, string Slug, string Title, string Visibility, int PhotoCount)
{
    public bool IsPublic => Visibility == "public";
}

/// <summary>
/// The gallery list read (two timed reads, <c>db.galleries.list</c> and <c>db.site.order</c>) and the
/// live-image filter and tile projection that <see cref="Gallery.Web.Workspace.WorkspaceReader"/> reuses
/// (020 C-16). Tombstoned images (<c>deletingAt</c>) are never live. Scoped.
/// </summary>
public sealed class LibraryReader(IMongoDatabase db, OpTimer timer)
{
    private readonly IMongoCollection<BsonDocument> _galleries = db.GetCollection<BsonDocument>("galleries");
    private readonly IMongoCollection<BsonDocument> _site = db.GetCollection<BsonDocument>("site");

    internal static readonly FilterDefinition<ImageDoc> Live = Builders<ImageDoc>.Filter.Exists(i => i.DeletingAt, false);

    /// <summary>The image fields a workspace tile needs.</summary>
    internal static readonly ProjectionDefinition<ImageDoc> TileFields = Builders<ImageDoc>.Projection
        .Include(i => i.Id).Include(i => i.Title).Include(i => i.Caption)
        .Include(i => i.Width).Include(i => i.Height).Include(i => i.Placeholder)
        .Include(i => i.Derivatives).Include(i => i.GalleryIds).Include(i => i.CreatedAt);

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
