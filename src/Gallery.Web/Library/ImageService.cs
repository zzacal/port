using Gallery.Web.Data;
using Gallery.Web.Edge;
using Gallery.Web.Observability;
using MongoDB.Driver;

namespace Gallery.Web.Library;

/// <summary>
/// Owner writes to one image's text and the library's bulk add (tasks.md T023). A title/caption
/// save purges P-TEXT (FR-038); bulk add calls <see cref="GalleryService.CreateAsync"/> (for a new
/// gallery) and then ONE <see cref="GalleryService.AddAsync"/>, which applies the single-item gate
/// <see cref="GalleryService.AddOneAsync"/> per id (FR-012, UI-6). [SAFETY] II.5: this class never
/// writes <c>visibility</c>. Scoped.
/// </summary>
public sealed class ImageService(
    IMongoDatabase db, OpTimer timer, GalleryService galleries, LibraryOptions options, ILogger<ImageService> log)
{
    private readonly IMongoCollection<ImageDoc> _images = db.GetCollection<ImageDoc>("images");
    private readonly IMongoCollection<GalleryDoc> _galleries = db.GetCollection<GalleryDoc>("galleries");

    /// <summary>
    /// L2: a null field keeps the stored value. Title trimmed, 1..<c>TitleMax</c> (whitespace-only
    /// is refused 422); caption trimmed, 0..<c>CaptionMax</c>, an empty caption stores null. A
    /// refusal stores nothing. The cards of the galleries holding it are rebuilt and P-TEXT is purged
    /// when anything changed. An unknown or tombstoned image answers NotFound.
    /// </summary>
    public async Task<OperationResult> UpdateTextAsync(string iid, string? title, string? caption)
    {
        if (title is not null && GalleryService.TitleProblem(title, options) is { } problem)
            return Refuse(problem);
        var cap = caption?.Trim();
        if (cap is not null && cap.Length > options.CaptionMax)
            return Refuse($"The caption can be at most {options.CaptionMax} characters.");

        var image = await timer.Time("db.gallery.load", () =>
            _images.Find(Builders<ImageDoc>.Filter.Eq(i => i.Id, iid)).FirstOrDefaultAsync());
        if (image is null || image.DeletingAt is not null) return OperationResult.NotFound();

        var newTitle = title?.Trim() ?? image.Title;
        var newCaption = caption is null ? image.Caption : (cap!.Length == 0 ? null : cap);
        if (newTitle == image.Title && newCaption == image.Caption)
            return OperationResult.Ok();

        await Indexes.EnsureOnceAsync(db);
        var update = Builders<ImageDoc>.Update.Set(i => i.Title, newTitle);
        update = update.Set(i => i.Caption, newCaption); // an empty caption stores null
        await timer.Time("db.image.update", () => _images.UpdateOneAsync(Builders<ImageDoc>.Filter.Eq(i => i.Id, iid), update));

        var holders = await timer.Time("db.gallery.load", () =>
            _galleries.Find(Builders<GalleryDoc>.Filter.AnyEq(g => g.Items, iid)).ToListAsync());
        var scope = galleries.Begin("image.update");
        foreach (var g in holders)
            scope.TouchText(GallerySnapshot.From(g), iid);
        await scope.CompleteAsync();
        return OperationResult.Ok();
    }

    /// <summary>
    /// L3: <paramref name="ids"/> must hold 1..<c>PageSize</c> ids (else 422, nothing written). With
    /// <paramref name="galleryId"/> null a new private gallery titled <paramref name="newTitle"/> is
    /// created first (<see cref="GalleryService.CreateAsync"/>). Then ONE
    /// <see cref="GalleryService.AddAsync"/> call. Result: added, skipped, gallery id and title.
    /// Timed as <c>library.bulk.add</c>.
    /// </summary>
    public Task<OperationResult> BulkAddAsync(IReadOnlyList<string> ids, string? galleryId, string? newTitle) =>
        timer.Time("library.bulk.add", async () =>
        {
            if (ids.Count == 0) return Refuse("Select at least one photo.");
            if (ids.Count > options.PageSize) return Refuse($"Select at most {options.PageSize} photos.");

            var gid = galleryId;
            if (gid is null)
            {
                var created = await galleries.CreateAsync(newTitle);
                if (!created.IsOk) return created;
                gid = created.GalleryId!;
            }
            return await galleries.AddAsync(gid, ids);
        });

    private OperationResult Refuse(string reason)
    {
        OpTiming.Log(log, "db.image.update", 0, "refused");
        return OperationResult.Refused(422, reason);
    }
}
