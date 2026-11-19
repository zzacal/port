using Gallery.Core.Ids;
using Gallery.Web.Data;
using Gallery.Web.Library;
using Gallery.Web.Observability;
using Gallery.Web.Upload;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;

namespace Gallery.Web.Workspace;

/// <summary>
/// 020 A1–A11 (contracts/http.md "Writes"; tasks.md T014): the workspace's JSON write routes on the
/// protected <c>/admin</c> group passed in, so each inherits the owner policy and
/// <c>AdminAntiforgeryFilter</c> ([SAFETY] I.3, I.6). Every handler: <c>{gid}</c>/<c>{iid}</c>
/// through <see cref="OpaqueId.IsValid"/> (else 404); the body through
/// <see cref="UploadEndpoints.ReadAsync{T}"/> inside the handler (C-14: absent or invalid → 400
/// <c>bad-request</c>, oversize → 413); one call to the service the contract names; a refusal →
/// <c>{ error, message }</c> with the service's status and reason; success → the listed fields plus
/// <c>message</c> from <see cref="Messages"/>. No write rule lives here.
/// </summary>
public static class WorkspaceApi
{
    /// <summary>The default A-route body limit.</summary>
    public const long SmallBodyLimit = 4 * 1024;

    /// <summary>A2, A3, A4, A5, A7, A8.</summary>
    public const long IdsBodyLimit = 16 * 1024;

    /// <summary>A9.</summary>
    public const long OrderBodyLimit = 64 * 1024;

    public static RouteGroupBuilder MapWorkspaceApi(this RouteGroupBuilder admin)
    {
        admin.MapPost("/api/photos/delete", (Delegate)DeletePhotos)                       // A2
            .WithMetadata(new RequestSizeLimitAttribute(IdsBodyLimit));
        admin.MapPost("/api/photos/{iid}", SavePhoto)                                     // A1
            .WithMetadata(new RequestSizeLimitAttribute(SmallBodyLimit));
        admin.MapPost("/api/galleries", (Delegate)CreateGallery)                         // A3
            .WithMetadata(new RequestSizeLimitAttribute(IdsBodyLimit));
        admin.MapPost("/api/galleries/order", (Delegate)OrderGalleries)                  // A4
            .WithMetadata(new RequestSizeLimitAttribute(IdsBodyLimit));
        admin.MapPost("/api/galleries/{gid}", SaveGallery)                               // A5
            .WithMetadata(new RequestSizeLimitAttribute(IdsBodyLimit));
        admin.MapPost("/api/galleries/{gid}/visibility", Visibility)                     // A6
            .WithMetadata(new RequestSizeLimitAttribute(SmallBodyLimit));
        admin.MapPost("/api/galleries/{gid}/photos", AddPhotos)                          // A7
            .WithMetadata(new RequestSizeLimitAttribute(IdsBodyLimit));
        admin.MapPost("/api/galleries/{gid}/photos/remove", RemovePhotos)                // A8
            .WithMetadata(new RequestSizeLimitAttribute(IdsBodyLimit));
        admin.MapPost("/api/galleries/{gid}/order", OrderPhotos)                         // A9
            .WithMetadata(new RequestSizeLimitAttribute(OrderBodyLimit));
        admin.MapPost("/api/galleries/{gid}/cover", Cover)                               // A10
            .WithMetadata(new RequestSizeLimitAttribute(SmallBodyLimit));
        admin.MapPost("/api/galleries/{gid}/delete", DeleteGallery)                      // A11
            .WithMetadata(new RequestSizeLimitAttribute(SmallBodyLimit));
        return admin;
    }

    // ── request bodies (only the fields the contract names) ──────────────────

    public sealed record PhotoTextBody(string? Title, string? Caption);
    public sealed record IdsBody(List<string?>? Ids);
    public sealed record CreateBody(string? Title, List<string?>? Ids);
    public sealed record OrderBody(List<string?>? Order);
    public sealed record GalleryTextBody(string? Title, string? Description);
    public sealed record VisibilityBody(bool? Public);
    public sealed record CoverBody([property: System.Text.Json.Serialization.JsonRequired] string? Image);
    public sealed record EmptyBody;

    // ── A1 POST /admin/api/photos/{iid} ──────────────────────────────────────

    private static async Task<IResult> SavePhoto(string iid, HttpContext ctx, ImageService images, IMongoDatabase db, OpTimer timer)
    {
        if (!OpaqueId.IsValid(iid)) return NotFound(Messages.PhotoGone);
        var (body, bad) = await UploadEndpoints.ReadAsync<PhotoTextBody>(ctx);
        if (bad is not null) return bad;

        var r = await images.UpdateTextAsync(iid, body!.Title, body.Caption);
        if (!r.IsOk) return Fail(r, Messages.PhotoGone);

        var stored = await timer.Time("db.gallery.load", () => db.GetCollection<ImageDoc>("images")
            .Find(Builders<ImageDoc>.Filter.Eq(i => i.Id, iid))
            .Project(i => new { i.Title, i.Caption }).FirstOrDefaultAsync());
        if (stored is null) return NotFound(Messages.PhotoGone);
        return Results.Json(new { title = stored.Title, caption = stored.Caption, message = Messages.Saved });
    }

    // ── A2 POST /admin/api/photos/delete ─────────────────────────────────────

    private static async Task<IResult> DeletePhotos(HttpContext ctx, ImageDeleteService deleter)
    {
        var (body, bad) = await UploadEndpoints.ReadAsync<IdsBody>(ctx);
        if (bad is not null) return bad;
        if (Ids(body!.Ids) is not { } ids) return Required("ids");

        var r = await deleter.DeleteManyAsync(ids);
        if (!r.IsOk) return Fail(r, ImageService.NoGalleryReason);
        return Results.Json(new
        {
            deleted = r.Deleted,
            leftovers = r.Leftovers,
            failed = r.Failed,
            message = Messages.Deleted(r.Deleted, r.Leftovers, r.Failed),
        });
    }

    // ── A3 POST /admin/api/galleries ─────────────────────────────────────────

    private static async Task<IResult> CreateGallery(HttpContext ctx, GalleryService galleries, ImageService images)
    {
        var (body, bad) = await UploadEndpoints.ReadAsync<CreateBody>(ctx);
        if (bad is not null) return bad;
        if (body!.Title is null) return Required("title");

        OperationResult r;
        int? added = null;
        if (body.Ids is null)
        {
            r = await galleries.CreateAsync(body.Title);
        }
        else
        {
            if (Ids(body.Ids) is not { } ids) return Required("ids");
            r = await images.BulkAddAsync(ids, null, body.Title);
            added = r.Added;
        }
        if (!r.IsOk) return Fail(r, ImageService.NoGalleryReason);

        var g = await galleries.LoadAsync(r.GalleryId!);
        if (g is null) return NotFound(ImageService.NoGalleryReason);
        return Results.Json(new
        {
            id = g.Id,
            slug = g.Slug,
            title = g.Title,
            added = added ?? 0,
            message = Messages.Created(g.Title, added),
        }, statusCode: StatusCodes.Status201Created);
    }

    // ── A4 POST /admin/api/galleries/order ───────────────────────────────────

    private static async Task<IResult> OrderGalleries(HttpContext ctx, GalleryService galleries)
    {
        var (body, bad) = await UploadEndpoints.ReadAsync<OrderBody>(ctx);
        if (bad is not null) return bad;
        if (Ids(body!.Order) is not { } order) return Required("order");

        var r = await galleries.ReorderHomeAsync(order);
        return r.IsOk ? Results.Json(new { message = Messages.GalleryOrderSaved }) : Fail(r, ImageService.NoGalleryReason);
    }

    // ── A5 POST /admin/api/galleries/{gid} ───────────────────────────────────

    private static async Task<IResult> SaveGallery(string gid, HttpContext ctx, GalleryService galleries)
    {
        if (!OpaqueId.IsValid(gid)) return NotFound(ImageService.NoGalleryReason);
        var (body, bad) = await UploadEndpoints.ReadAsync<GalleryTextBody>(ctx);
        if (bad is not null) return bad;

        var r = await galleries.UpdateTextAsync(gid, body!.Title, body.Description);
        if (!r.IsOk) return Fail(r, ImageService.NoGalleryReason);

        var g = await galleries.LoadAsync(gid);
        if (g is null) return NotFound(ImageService.NoGalleryReason);
        return Results.Json(new { title = g.Title, slug = g.Slug, description = g.Description, message = Messages.Saved });
    }

    // ── A6 POST /admin/api/galleries/{gid}/visibility [SAFETY] II.6 ──────────

    /// <summary>
    /// A6 in the G4 handler's order (LibraryEndpoints.Visibility): unknown → 404; <c>public: true</c>
    /// → <see cref="GalleryService.PublishAsync"/> (emptiness FIRST, then the conditional
    /// <c>items.0</c> write); <c>public: false</c> on a gallery that is not public → 409
    /// <see cref="GalleryService.EmptyPublishReason"/> when it is empty, else 200 with no write;
    /// <c>public: false</c> on a public gallery → <see cref="GalleryService.UnpublishAsync"/>.
    /// </summary>
    private static async Task<IResult> Visibility(string gid, HttpContext ctx, GalleryService galleries)
    {
        if (!OpaqueId.IsValid(gid)) return NotFound(ImageService.NoGalleryReason);
        var (body, bad) = await UploadEndpoints.ReadAsync<VisibilityBody>(ctx);
        if (bad is not null) return bad;
        if (body!.Public is not { } wantPublic) return Required("public");

        var gallery = await galleries.LoadAsync(gid);
        if (gallery is null) return NotFound(ImageService.NoGalleryReason);

        if (wantPublic)
        {
            var r = await galleries.PublishAsync(gid, confirmed: true, loaded: gallery);
            return r.IsOk
                ? Results.Json(new { @public = true, message = Messages.NowPublic(gallery.Title) })
                : Fail(r, ImageService.NoGalleryReason);
        }

        if (gallery.Visibility != "public")
        {
            // Nothing to unpublish. An empty private gallery answers the empty refusal whatever was
            // sent ([SAFETY] II.6); a non-empty one is already private (no write).
            return gallery.Items.Count == 0
                ? UploadEndpoints.Error(409, "refused", GalleryService.EmptyPublishReason)
                : Results.Json(new { @public = false, message = Messages.NowPrivate(gallery.Title) });
        }

        var u = await galleries.UnpublishAsync(gid, loaded: gallery);
        return u.IsOk
            ? Results.Json(new { @public = false, message = Messages.NowPrivate(gallery.Title) })
            : Fail(u, ImageService.NoGalleryReason);
    }

    // ── A7 POST /admin/api/galleries/{gid}/photos ────────────────────────────

    private static async Task<IResult> AddPhotos(string gid, HttpContext ctx, ImageService images, GalleryService galleries)
    {
        if (!OpaqueId.IsValid(gid)) return NotFound(ImageService.NoGalleryReason);
        var (body, bad) = await UploadEndpoints.ReadAsync<IdsBody>(ctx);
        if (bad is not null) return bad;
        if (Ids(body!.Ids) is not { } ids) return Required("ids");

        var r = await images.BulkAddAsync(ids, gid, null);
        if (!r.IsOk) return Fail(r, ImageService.NoGalleryReason);

        var g = await galleries.LoadAsync(gid);
        if (g is null) return NotFound(ImageService.NoGalleryReason);
        var isPublic = g.Visibility == "public";
        return Results.Json(new
        {
            added = r.Added,
            skipped = r.Skipped,
            @public = isPublic,
            message = Messages.Added(r.Added, r.Skipped, g.Title, isPublic),
        });
    }

    // ── A8 POST /admin/api/galleries/{gid}/photos/remove ─────────────────────

    private static async Task<IResult> RemovePhotos(string gid, HttpContext ctx, GalleryService galleries)
    {
        if (!OpaqueId.IsValid(gid)) return NotFound(ImageService.NoGalleryReason);
        var (body, bad) = await UploadEndpoints.ReadAsync<IdsBody>(ctx);
        if (bad is not null) return bad;
        if (Ids(body!.Ids) is not { } ids) return Required("ids");

        var r = await galleries.RemoveManyAsync(gid, ids);
        if (!r.IsOk) return Fail(r, ImageService.NoGalleryReason);

        var g = await galleries.LoadAsync(gid);
        if (g is null) return NotFound(ImageService.NoGalleryReason);
        var isPublic = g.Visibility == "public";
        var listed = isPublic && g.Items.Count > 0;
        var hidden = isPublic && r.Removed > 0 && g.Items.Count == 0;
        return Results.Json(new
        {
            removed = r.Removed,
            @public = isPublic,
            listed,
            message = Messages.Removed(r.Removed, g.Title, hidden),
        });
    }

    // ── A9 POST /admin/api/galleries/{gid}/order ─────────────────────────────

    private static async Task<IResult> OrderPhotos(string gid, HttpContext ctx, GalleryService galleries)
    {
        if (!OpaqueId.IsValid(gid)) return NotFound(ImageService.NoGalleryReason);
        var (body, bad) = await UploadEndpoints.ReadAsync<OrderBody>(ctx);
        if (bad is not null) return bad;
        if (Ids(body!.Order) is not { } order) return Required("order");

        var r = await galleries.ReorderAsync(gid, order);
        return r.IsOk ? Results.Json(new { message = Messages.OrderSaved }) : Fail(r, ImageService.NoGalleryReason);
    }

    // ── A10 POST /admin/api/galleries/{gid}/cover ────────────────────────────

    private static async Task<IResult> Cover(string gid, HttpContext ctx, GalleryService galleries, IMongoDatabase db, OpTimer timer)
    {
        if (!OpaqueId.IsValid(gid)) return NotFound(ImageService.NoGalleryReason);
        var (body, bad) = await UploadEndpoints.ReadAsync<CoverBody>(ctx);
        if (bad is not null) return bad;

        var r = await galleries.SetCoverAsync(gid, body!.Image);
        if (!r.IsOk) return Fail(r, ImageService.NoGalleryReason);

        var g = await galleries.LoadAsync(gid);
        if (g is null) return NotFound(ImageService.NoGalleryReason);
        if (body.Image is null)
            return Results.Json(new { cover = (string?)null, message = Messages.FirstPhotoCover(g.Title) });

        var photoTitle = await timer.Time("db.gallery.load", () => db.GetCollection<ImageDoc>("images")
            .Find(Builders<ImageDoc>.Filter.Eq(i => i.Id, body.Image))
            .Project(i => i.Title).FirstOrDefaultAsync());
        return Results.Json(new { cover = body.Image, message = Messages.Cover(photoTitle ?? string.Empty, g.Title) });
    }

    // ── A11 POST /admin/api/galleries/{gid}/delete ───────────────────────────

    private static async Task<IResult> DeleteGallery(string gid, HttpContext ctx, GalleryService galleries)
    {
        if (!OpaqueId.IsValid(gid)) return NotFound(ImageService.NoGalleryReason);
        var (_, bad) = await UploadEndpoints.ReadAsync<EmptyBody>(ctx);
        if (bad is not null) return bad;

        // The title for the message; a gallery already gone answers 200 (idempotent).
        var gallery = await galleries.LoadAsync(gid);
        if (gallery is null) return Results.Json(new { message = Messages.GalleryAlreadyDeleted });

        var r = await galleries.DeleteAsync(gid);
        return r.Status == OperationStatus.NotFound
            ? Results.Json(new { message = Messages.GalleryAlreadyDeleted })
            : Results.Json(new { message = Messages.GalleryDeleted(gallery.Title) });
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    /// <summary>The id list, or null when it is absent or holds a null/empty entry (400).</summary>
    private static List<string>? Ids(List<string?>? ids) =>
        ids is null || ids.Any(string.IsNullOrEmpty) ? null : ids.Select(i => i!).ToList();

    private static IResult Required(string field) =>
        UploadEndpoints.Error(400, "bad-request", $"{field} is required.");

    private static IResult NotFound(string message) => UploadEndpoints.Error(404, "not-found", message);

    /// <summary>
    /// A service refusal as <c>{ error, message }</c>: the status the service gave, the code from its
    /// <see cref="OperationStatus"/> (<c>not-found</c>, <c>stale</c>, <c>refused</c>) and its reason
    /// (<paramref name="notFound"/> when a NotFound carries none).
    /// </summary>
    private static IResult Fail(OperationResult r, string notFound) => r.Status switch
    {
        OperationStatus.NotFound => UploadEndpoints.Error(r.StatusCode, "not-found", r.Reason ?? notFound),
        OperationStatus.Stale => UploadEndpoints.Error(r.StatusCode, "stale", r.Reason ?? string.Empty),
        _ => UploadEndpoints.Error(r.StatusCode, "refused", r.Reason ?? string.Empty),
    };
}
