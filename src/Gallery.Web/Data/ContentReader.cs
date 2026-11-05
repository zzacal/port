using Gallery.Web.Observability;
using MongoDB.Driver;

namespace Gallery.Web.Data;

/// <summary>
/// Scoped read-only accessor for the three public collections.
/// Each method wraps its query in <see cref="OpTimer.Time"/> so reads are counted and timed.
/// </summary>
public sealed class ContentReader(IMongoDatabase db, OpTimer timer)
{
    private IMongoCollection<SiteDoc> Sites =>
        db.GetCollection<SiteDoc>("site");

    private IMongoCollection<GalleryDoc> Galleries =>
        db.GetCollection<GalleryDoc>("galleries");

    private IMongoCollection<ImageDoc> Images =>
        db.GetCollection<ImageDoc>("images");

    /// <summary>Returns the site document, projecting only <c>homeCards</c>.</summary>
    public Task<SiteDoc?> HomeAsync() =>
        timer.Time("db.site.read", async () =>
        {
            var projection = Builders<SiteDoc>.Projection.Include(s => s.HomeCards);
            return (SiteDoc?)await Sites.Find(s => s.Id == "site")
                               .Project<SiteDoc>(projection)
                               .FirstOrDefaultAsync();
        }, isDbRead: true);

    /// <summary>
    /// Returns a gallery by id. When <paramref name="forPhoto"/> is true, projects only the
    /// fields needed by the photo page; otherwise returns everything except <c>card</c>.
    /// </summary>
    public Task<GalleryDoc?> GalleryAsync(string id, bool forPhoto) =>
        timer.Time("db.gallery.read", async () =>
        {
            var filter = Builders<GalleryDoc>.Filter.Eq(g => g.Id, id);
            ProjectionDefinition<GalleryDoc> projection;
            if (forPhoto)
            {
                projection = Builders<GalleryDoc>.Projection
                    .Include(g => g.Slug)
                    .Include(g => g.Title)
                    .Include(g => g.Visibility)
                    .Include(g => g.Items);
            }
            else
            {
                projection = Builders<GalleryDoc>.Projection.Exclude(g => g.Card);
            }
            return (GalleryDoc?)await Galleries.Find(filter)
                                   .Project<GalleryDoc>(projection)
                                   .FirstOrDefaultAsync();
        }, isDbRead: true);

    /// <summary>
    /// Returns images for the given ids. When <paramref name="full"/> is false, returns only the
    /// fields needed for the grid; when true, returns the full document. The result is returned in
    /// the same order as <paramref name="ids"/>.
    /// </summary>
    public Task<IReadOnlyList<ImageDoc>> ImagesAsync(IEnumerable<string> ids, bool full) =>
        timer.Time("db.images.read", async () =>
        {
            var idList = ids.ToList();
            var filter = Builders<ImageDoc>.Filter.In(img => img.Id, idList);

            IFindFluent<ImageDoc, ImageDoc> query;
            if (full)
            {
                query = Images.Find(filter);
            }
            else
            {
                // Grid projection: _id, title, width, height, placeholder, derivatives
                var projection = Builders<ImageDoc>.Projection
                    .Include(img => img.Id)
                    .Include(img => img.Title)
                    .Include(img => img.Width)
                    .Include(img => img.Height)
                    .Include(img => img.Placeholder)
                    .Include(img => img.Derivatives);
                query = Images.Find(filter).Project<ImageDoc>(projection);
            }

            var docs = await query.ToListAsync();

            // Return in the order of the requested ids.
            var index = docs.ToDictionary(d => d.Id);
            IReadOnlyList<ImageDoc> ordered = idList
                .Where(id => index.ContainsKey(id))
                .Select(id => index[id])
                .ToList();
            return ordered;
        }, isDbRead: true);

    private IMongoCollection<CameraGroupDoc> Cameras =>
        db.GetCollection<CameraGroupDoc>("cameras");

    /// <summary>
    /// 013 C1 (contracts/routes.md reader table): every camera, projecting only <c>card</c>, in
    /// stored <c>rank</c> order. One find.
    /// </summary>
    public Task<IReadOnlyList<CameraGroupDoc>> CamerasAsync() =>
        timer.Time("db.cameras.list", async () =>
        {
            var projection = Builders<CameraGroupDoc>.Projection.Include(c => c.Card);
            IReadOnlyList<CameraGroupDoc> docs = await Cameras.Find(FilterDefinition<CameraGroupDoc>.Empty)
                .Project<CameraGroupDoc>(projection)
                .SortBy(c => c.Rank)
                .ToListAsync();
            return docs;
        }, isDbRead: true);

    /// <summary>
    /// 013 C2/C3 (contracts/routes.md reader table): the camera at <paramref name="slug"/>,
    /// projecting only <c>slug</c>, <c>name</c> and <c>items</c>; null when no camera has it.
    /// </summary>
    public Task<CameraGroupDoc?> CameraAsync(string slug) =>
        timer.Time("db.camera.read", async () =>
        {
            var projection = Builders<CameraGroupDoc>.Projection
                .Include(c => c.Slug)
                .Include(c => c.Name)
                .Include(c => c.Items);
            return (CameraGroupDoc?)await Cameras.Find(Builders<CameraGroupDoc>.Filter.Eq(c => c.Slug, slug))
                .Project<CameraGroupDoc>(projection)
                .FirstOrDefaultAsync();
        }, isDbRead: true);
}
