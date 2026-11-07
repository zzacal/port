using System.Net;
using Gallery.Core.Ids;
using Gallery.Core.Images;
using Gallery.Web.Admin.Pages;
using Gallery.Web.Components;
using Gallery.Web.Data;
using Gallery.Web.Observability;
using Gallery.Web.Site;
using Gallery.Web.Storage;
using Gallery.Web.Upload;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http.HttpResults;
using MongoDB.Driver;

namespace Gallery.Web.Library;

/// <summary>
/// What the library page (contracts/components.md C1) renders, built by <see cref="LibraryEndpoints"/>.
/// </summary>
/// <param name="Status">The <c>role="status"</c> line: the PRG result or a refusal reason.</param>
/// <param name="EditTitle">The side panel's title value (the submitted one after a refused save, else stored).</param>
/// <param name="EditAdd">014: the pending <c>add</c> ids after a 422-refused save (null otherwise), so the panel keeps them.</param>
/// <param name="EditCreate">014: the pending <c>create</c> titles after a 422-refused save (null otherwise).</param>
/// <param name="EditLeave">014: the pending <c>leave</c> ids after a 422-refused save (null otherwise).</param>
public sealed record LibraryPageModel(
    LibraryPageData Data,
    LibraryOptions Options,
    FormToken Csrf,
    string ScriptUrl,
    string? Status = null,
    bool Saved = false,
    string? EditTitle = null,
    string? EditCaption = null,
    IReadOnlyList<string>? EditAdd = null,
    IReadOnlyList<string>? EditCreate = null,
    IReadOnlyList<string>? EditLeave = null);

/// <summary>One card of the gallery list (contracts/components.md C2).</summary>
/// <param name="Cover">The card's cover (the gallery title as its title), or null for a gallery with no photos.</param>
public sealed record GalleryRow(GalleryRef Gallery, ImageView? Cover)
{
    public bool HasPhotos => Gallery.PhotoCount > 0;
}

/// <summary>What the gallery list (C2, G1) renders, built by <see cref="LibraryEndpoints"/>.</summary>
public sealed record GalleriesPageModel(
    IReadOnlyList<GalleryRow> Rows,
    LibraryOptions Options,
    FormToken Csrf,
    string LibraryScriptUrl,
    string ReorderScriptUrl,
    string? Status = null);

/// <summary>What the gallery builder (C3, B1) renders, built by <see cref="LibraryEndpoints"/>.</summary>
/// <param name="Items">The member images in gallery order (as views).</param>
/// <param name="EditTitle">The title input's value (the submitted one after a refused save, else stored).</param>
/// <param name="EditPublic">The switch's submitted state after a refused save, or null for the stored one.</param>
public sealed record GalleryPageModel(
    GalleryDoc Gallery,
    IReadOnlyList<ImageView> Items,
    LibraryOptions Options,
    FormToken Csrf,
    string LibraryScriptUrl,
    string ReorderScriptUrl,
    string? Status = null,
    string? EditTitle = null,
    string? EditDescription = null,
    bool? EditPublic = null)
{
    public bool HasPhotos => Items.Count > 0;
}

/// <summary>
/// The 009 owner routes of contracts/http.md: L1–L5, G1–G4 and B1–B6 (tasks.md T029, T038, T039). Mapped by
/// <see cref="LibrarySetup.MapLibrary"/> on the protected <c>/admin</c> group, so every route
/// inherits the owner policy and <c>AdminAntiforgeryFilter</c>. Handlers only call the services
/// (<see cref="GalleryService"/>, <see cref="ImageService"/>, <see cref="ImageDeleteService"/>,
/// <see cref="LibraryReader"/>); no write logic lives here.
/// <list type="bullet">
/// <item><c>{gid}</c>/<c>{iid}</c> must pass <see cref="OpaqueId.IsValid"/>, else 404;</item>
/// <item>the form is read only when <c>HasFormContentType</c>; a bare POST is "no fields";</item>
/// <item>success → 303 with the contracted status query; a refusal re-renders the page with the
///   contracted status code and the reason in <c>role="status"</c>, and nothing is stored.</item>
/// </list>
/// G1 and B1 render the gallery list (C2) and the builder (C3); G/B refusals re-render those pages
/// with the contracted status code.
/// </summary>
public static class LibraryEndpoints
{
    public const string LibraryPath = "/admin/library";
    public const string GalleriesPath = "/admin/galleries";

    public static RouteGroupBuilder MapLibraryEndpoints(this RouteGroupBuilder admin)
    {
        admin.MapMethods("/library", ["GET", "HEAD"], (Delegate)Library);                   // L1
        admin.MapPost("/library/images/{iid}", SaveImage);                                    // L2
        admin.MapPost("/library/add", (Delegate)BulkAdd);                                     // L3
        admin.MapPost("/library/delete", (Delegate)BulkDelete);                               // L4
        admin.MapPost("/library/images/{iid}/delete", DeleteImage);                           // L5

        admin.MapMethods("/galleries", ["GET", "HEAD"], (Delegate)Galleries);                 // G1
        admin.MapPost("/galleries", (Delegate)CreateGallery);                                 // G2
        admin.MapPost("/galleries/order", (Delegate)ReorderGalleries);                        // G3
        admin.MapPost("/galleries/{gid}/visibility", Visibility);                             // G4

        admin.MapMethods("/galleries/{gid}", ["GET", "HEAD"], BuilderPage);                   // B1
        admin.MapPost("/galleries/{gid}", SaveGallery);                                       // B2
        admin.MapPost("/galleries/{gid}/order", ReorderGallery);                              // B3
        admin.MapPost("/galleries/{gid}/cover", Cover);                                       // B4
        admin.MapPost("/galleries/{gid}/remove", Remove);                                     // B5
        admin.MapPost("/galleries/{gid}/delete", DeleteGallery);                              // B6
        return admin;
    }

    // ── L1 GET /admin/library ────────────────────────────────────────────────

    private static Task<IResult> Library(HttpContext ctx, LibraryReader reader, LibraryOptions options,
        IAntiforgery antiforgery, AdminAssets assets)
    {
        var q = ctx.Request.Query;
        var page = PageOf(q["page"].ToString());
        var open = q["open"].ToString();
        return RenderLibraryAsync(ctx, reader, options, antiforgery, assets, page,
            OpaqueId.IsValid(open) ? open : null, statusCode: 200, status: null, saved: q["saved"] == "1",
            statusFromQuery: true);
    }

    // ── L2 POST /admin/library/images/{iid} ──────────────────────────────────

    private static async Task<IResult> SaveImage(string iid, HttpContext ctx, ImageService images,
        LibraryReader reader, LibraryOptions options, IAntiforgery antiforgery, AdminAssets assets)
    {
        if (!OpaqueId.IsValid(iid)) return Results.NotFound();
        var form = await FormAsync(ctx);
        var page = PageOf(form["page"].ToString());
        var title = Field(form, "title");
        var caption = Field(form, "caption");

        // 014: the repeated gallery-membership fields (contracts/http.md); absent means no change.
        var edit = MembershipEdit.Normalise(Ids(form, "add"), Ids(form, "create"), Ids(form, "leave"));

        var r = await images.SavePanelAsync(iid, title, caption, edit);
        // A 422 keeps the pending tokens; a 404 gallery refusal shows the current memberships.
        var keep = r.StatusCode == 422;
        return r.Status switch
        {
            OperationStatus.Ok => SeeOther($"{LibraryPath}?page={page}&open={iid}&saved=1"),
            OperationStatus.NotFound => await RenderLibraryAsync(ctx, reader, options, antiforgery, assets, page,
                null, 404, "That photo no longer exists."),
            _ => await RenderLibraryAsync(ctx, reader, options, antiforgery, assets, page,
                iid, r.StatusCode, r.Reason, editTitle: title, editCaption: caption,
                editAdd: keep ? edit.Add : null, editCreate: keep ? edit.Create : null, editLeave: keep ? edit.Leave : null),
        };
    }

    // ── L3 POST /admin/library/add ───────────────────────────────────────────

    private static async Task<IResult> BulkAdd(HttpContext ctx, ImageService images,
        LibraryReader reader, LibraryOptions options, IAntiforgery antiforgery, AdminAssets assets)
    {
        var form = await FormAsync(ctx);
        var page = PageOf(form["page"].ToString());
        var ids = Ids(form, "ids");
        var gallery = form["gallery"].ToString();

        // The service checks the id count first (422), then creates the new gallery (422 title) or
        // loads the named one (404 when unknown; an absent or malformed id is unknown too).
        var r = gallery == "new"
            ? await images.BulkAddAsync(ids, null, Field(form, "newTitle") ?? "")
            : await images.BulkAddAsync(ids, gallery, null);

        if (r.Status == OperationStatus.NotFound)
            return await RenderLibraryAsync(ctx, reader, options, antiforgery, assets, page, null, 404,
                "That gallery no longer exists.");
        if (!r.IsOk)
            return await RenderLibraryAsync(ctx, reader, options, antiforgery, assets, page, null, r.StatusCode, r.Reason);

        return gallery == "new"
            ? SeeOther($"{GalleriesPath}/{r.GalleryId}?added={r.Added}")
            : SeeOther($"{LibraryPath}?page={page}&added={r.Added}&skipped={r.Skipped}&to={r.GalleryId}");
    }

    // ── L4 POST /admin/library/delete ────────────────────────────────────────

    private static async Task<IResult> BulkDelete(HttpContext ctx, ImageDeleteService deleter,
        LibraryReader reader, LibraryOptions options, IAntiforgery antiforgery, AdminAssets assets)
    {
        var form = await FormAsync(ctx);
        var page = PageOf(form["page"].ToString());
        var r = await deleter.DeleteManyAsync(Ids(form, "ids"));
        if (!r.IsOk)
            return await RenderLibraryAsync(ctx, reader, options, antiforgery, assets, page, null, r.StatusCode, r.Reason);
        return SeeOther($"{LibraryPath}?page={page}&deleted={r.Deleted}&leftovers={r.Leftovers}&failed={r.Failed}");
    }

    // ── L5 POST /admin/library/images/{iid}/delete ───────────────────────────

    private static async Task<IResult> DeleteImage(string iid, HttpContext ctx, ImageDeleteService deleter)
    {
        if (!OpaqueId.IsValid(iid)) return Results.NotFound();
        var form = await FormAsync(ctx);
        var page = PageOf(form["page"].ToString());
        var r = await deleter.DeleteAsync(iid);
        // A missing image is indistinguishable from one already deleted: 303 with deleted=0 (idempotent).
        return r.Status == OperationStatus.NotFound
            ? SeeOther($"{LibraryPath}?page={page}&deleted=0&leftovers=0")
            : SeeOther($"{LibraryPath}?page={page}&deleted={r.Deleted}&leftovers={r.Leftovers}");
    }

    // ── G1 GET /admin/galleries ──────────────────────────────────────────────

    private static Task<IResult> Galleries(HttpContext ctx, LibraryReader reader, IMongoDatabase db, OpTimer timer,
        StorageOptions storage, LibraryOptions options, IAntiforgery antiforgery, AdminAssets assets) =>
        RenderGalleriesAsync(ctx, reader, db, timer, storage, options, antiforgery, assets, 200,
            GalleriesStatusFromQuery(ctx.Request.Query));

    // ── G2 POST /admin/galleries ─────────────────────────────────────────────

    private static async Task<IResult> CreateGallery(HttpContext ctx, GalleryService galleries, LibraryReader reader,
        IMongoDatabase db, OpTimer timer, StorageOptions storage, LibraryOptions options, IAntiforgery antiforgery, AdminAssets assets)
    {
        var form = await FormAsync(ctx);
        var r = await galleries.CreateAsync(Field(form, "title"));
        return r.IsOk
            ? SeeOther($"{GalleriesPath}/{r.GalleryId}")
            : await RenderGalleriesAsync(ctx, reader, db, timer, storage, options, antiforgery, assets, r.StatusCode, r.Reason);
    }

    // ── G3 POST /admin/galleries/order ──────────────────────────────────────

    private static async Task<IResult> ReorderGalleries(HttpContext ctx, GalleryService galleries, LibraryReader reader,
        IMongoDatabase db, OpTimer timer, StorageOptions storage, LibraryOptions options, IAntiforgery antiforgery, AdminAssets assets)
    {
        var form = await FormAsync(ctx);
        var r = await galleries.ReorderHomeAsync(Ids(form, "order"));
        return r.IsOk
            ? SeeOther($"{GalleriesPath}?ordered=1")
            : await RenderGalleriesAsync(ctx, reader, db, timer, storage, options, antiforgery, assets, r.StatusCode, r.Reason);
    }

    // ── G4 POST /admin/galleries/{gid}/visibility ────────────────────────────

    /// <summary>
    /// G4: the gallery list's switch (and any direct post). No confirmation step (owner feedback,
    /// decisions log): <c>public=on</c> publishes at once through <see cref="GalleryService.PublishAsync"/>
    /// (emptiness FIRST, then the conditional <c>items.0</c> write), anything else unpublishes.
    /// </summary>
    private static async Task<IResult> Visibility(string gid, HttpContext ctx, GalleryService galleries, LibraryReader reader,
        IMongoDatabase db, OpTimer timer, StorageOptions storage, LibraryOptions options, IAntiforgery antiforgery, AdminAssets assets)
    {
        if (!OpaqueId.IsValid(gid)) return Results.NotFound();
        var form = await FormAsync(ctx);
        var wantPublic = form["public"] == "on";
        var fromList = form["from"] == "list";
        var back = fromList ? GalleriesPath : $"{GalleriesPath}/{gid}";

        var gallery = await galleries.LoadAsync(gid);
        if (gallery is null) return Results.NotFound();

        // A refusal re-renders the page the request came from.
        Task<IResult> Page(int status, string? reason) => fromList
            ? RenderGalleriesAsync(ctx, reader, db, timer, storage, options, antiforgery, assets, status, reason)
            : RenderBuilderAsync(ctx, galleries, db, timer, storage, options, antiforgery, assets, gid, status, reason);

        if (wantPublic)
        {
            // The service checks emptiness FIRST, then writes conditionally on items.0.
            var r = await galleries.PublishAsync(gid, confirmed: true, loaded: gallery);
            return r.Status switch
            {
                OperationStatus.Ok => SeeOther($"{back}?visibility=public"),
                OperationStatus.NotFound => Results.NotFound(),
                _ => await Page(r.StatusCode, r.Reason),
            };
        }

        if (gallery.Visibility != "public")
        {
            // Nothing to unpublish. An empty private gallery answers the empty refusal whatever was
            // sent (contracts/http.md G4, [SAFETY]); a non-empty one is already private (no write).
            return gallery.Items.Count == 0
                ? await Page(409, GalleryService.EmptyPublishReason)
                : SeeOther($"{back}?visibility=private");
        }

        var u = await galleries.UnpublishAsync(gid, loaded: gallery);
        return u.Status == OperationStatus.NotFound ? Results.NotFound() : SeeOther($"{back}?visibility=private");
    }

    // ── B1 GET /admin/galleries/{gid} ────────────────────────────────────────

    private static Task<IResult> BuilderPage(string gid, HttpContext ctx, GalleryService galleries, IMongoDatabase db,
        OpTimer timer, StorageOptions storage, LibraryOptions options, IAntiforgery antiforgery, AdminAssets assets) =>
        !OpaqueId.IsValid(gid)
            ? Task.FromResult(Results.NotFound())
            : RenderBuilderAsync(ctx, galleries, db, timer, storage, options, antiforgery, assets, gid, 200,
                BuilderStatusFromQuery(ctx.Request.Query));

    // ── B2 POST /admin/galleries/{gid} ───────────────────────────────────────

    private static async Task<IResult> SaveGallery(string gid, HttpContext ctx, GalleryService galleries, IMongoDatabase db,
        OpTimer timer, StorageOptions storage, LibraryOptions options, IAntiforgery antiforgery, AdminAssets assets)
    {
        if (!OpaqueId.IsValid(gid)) return Results.NotFound();
        var form = await FormAsync(ctx);
        var title = Field(form, "title");
        var description = Field(form, "description");
        // The switch is read only when the form carries the vis marker: an unchecked checkbox sends
        // nothing, so without the marker a bare or partial POST could not be told from "turn it off".
        bool? wantPublic = form["vis"] == "1" ? form["public"] == "on" : null;
        var (r, nowPublic) = await galleries.SaveAsync(gid, title, description, wantPublic);
        var ok = nowPublic switch
        {
            true => $"{GalleriesPath}/{gid}?saved=1&visibility=public",
            false => $"{GalleriesPath}/{gid}?saved=1&visibility=private",
            null => $"{GalleriesPath}/{gid}?saved=1",
        };
        return await Builder(ctx, galleries, db, timer, storage, options, antiforgery, assets, gid, r,
            ok, title, description, wantPublic);
    }

    // ── B3 POST /admin/galleries/{gid}/order ─────────────────────────────────

    private static async Task<IResult> ReorderGallery(string gid, HttpContext ctx, GalleryService galleries, IMongoDatabase db,
        OpTimer timer, StorageOptions storage, LibraryOptions options, IAntiforgery antiforgery, AdminAssets assets)
    {
        if (!OpaqueId.IsValid(gid)) return Results.NotFound();
        var form = await FormAsync(ctx);
        var r = await galleries.ReorderAsync(gid, Ids(form, "order"));
        return await Builder(ctx, galleries, db, timer, storage, options, antiforgery, assets, gid, r,
            $"{GalleriesPath}/{gid}?ordered=1");
    }

    // ── B4 POST /admin/galleries/{gid}/cover ─────────────────────────────────

    private static async Task<IResult> Cover(string gid, HttpContext ctx, GalleryService galleries, IMongoDatabase db,
        OpTimer timer, StorageOptions storage, LibraryOptions options, IAntiforgery antiforgery, AdminAssets assets)
    {
        if (!OpaqueId.IsValid(gid)) return Results.NotFound();
        var form = await FormAsync(ctx);
        var image = Field(form, "image");
        var r = await galleries.SetCoverAsync(gid, string.IsNullOrEmpty(image) ? null : image);
        return await Builder(ctx, galleries, db, timer, storage, options, antiforgery, assets, gid, r,
            $"{GalleriesPath}/{gid}");
    }

    // ── B5 POST /admin/galleries/{gid}/remove ────────────────────────────────

    private static async Task<IResult> Remove(string gid, HttpContext ctx, GalleryService galleries, IMongoDatabase db,
        OpTimer timer, StorageOptions storage, LibraryOptions options, IAntiforgery antiforgery, AdminAssets assets)
    {
        if (!OpaqueId.IsValid(gid)) return Results.NotFound();
        var form = await FormAsync(ctx);
        var image = Field(form, "image");
        if (string.IsNullOrEmpty(image))
        {
            // No image named: nothing to remove (idempotent), but an unknown gallery is still 404.
            return await galleries.LoadAsync(gid) is null
                ? Results.NotFound()
                : SeeOther($"{GalleriesPath}/{gid}?removed=0");
        }
        var r = await galleries.RemoveAsync(gid, image);
        return await Builder(ctx, galleries, db, timer, storage, options, antiforgery, assets, gid, r,
            $"{GalleriesPath}/{gid}?removed={r.Removed}");
    }

    // ── B6 POST /admin/galleries/{gid}/delete ────────────────────────────────

    private static async Task<IResult> DeleteGallery(string gid, GalleryService galleries)
    {
        if (!OpaqueId.IsValid(gid)) return Results.NotFound();
        var r = await galleries.DeleteAsync(gid);
        // Already deleted (or never existed) → 303 with deleted=0 (tasks.md T022).
        return SeeOther($"{GalleriesPath}?deleted={(r.Status == OperationStatus.NotFound ? 0 : 1)}");
    }

    // ── rendering ────────────────────────────────────────────────────────────

    private static async Task<IResult> RenderLibraryAsync(
        HttpContext ctx, LibraryReader reader, LibraryOptions options, IAntiforgery antiforgery, AdminAssets assets,
        int page, string? open, int statusCode, string? status, bool saved = false, bool statusFromQuery = false,
        string? editTitle = null, string? editCaption = null,
        IReadOnlyList<string>? editAdd = null, IReadOnlyList<string>? editCreate = null, IReadOnlyList<string>? editLeave = null)
    {
        var data = await reader.PageAsync(page, open);
        if (statusFromQuery) status = StatusFromQuery(ctx.Request.Query, data.Galleries);

        var model = new LibraryPageModel(
            data, options, Token(ctx, antiforgery),
            assets.UrlFor(AdminAssets.LibraryScriptPath),
            status, saved && data.Open is not null,
            editTitle ?? data.Open?.Image.Title,
            editCaption ?? data.Open?.Caption,
            editAdd, editCreate, editLeave);

        return new RazorComponentResult<LibraryPage>(new Dictionary<string, object?>
        {
            [nameof(LibraryPage.Model)] = model,
        })
        { StatusCode = statusCode };
    }

    /// <summary>
    /// The gallery list (C2): every gallery in home order (<see cref="LibraryReader.GalleriesAsync"/>,
    /// two reads) plus one <c>db.galleries.list</c> read of the stored cards for the covers.
    /// </summary>
    private static async Task<IResult> RenderGalleriesAsync(
        HttpContext ctx, LibraryReader reader, IMongoDatabase db, OpTimer timer, StorageOptions storage,
        LibraryOptions options, IAntiforgery antiforgery, AdminAssets assets, int statusCode, string? status)
    {
        var refs = await reader.GalleriesAsync();
        var cards = await timer.Time("db.galleries.list", () => db.GetCollection<GalleryDoc>("galleries")
            .Find(FilterDefinition<GalleryDoc>.Empty)
            .Project<GalleryDoc>(Builders<GalleryDoc>.Projection.Include(g => g.Id).Include(g => g.Card))
            .ToListAsync(), isDbRead: true);
        var cardOf = cards.Where(g => g.Card is not null).ToDictionary(g => g.Id, g => g.Card!, StringComparer.Ordinal);

        var url = PublicEndpoints.MakeUrl(storage);
        var rows = refs
            .Select(g => new GalleryRow(g,
                g.PhotoCount > 0 && cardOf.TryGetValue(g.Id, out var card) && card.Cover is not null
                    ? Site.ViewMapper.ToCard(card, url).Image
                    : null))
            .ToList();

        var model = new GalleriesPageModel(
            rows, options, Token(ctx, antiforgery),
            assets.UrlFor(AdminAssets.LibraryScriptPath),
            assets.UrlFor(AdminAssets.ReorderScriptPath),
            status);

        return new RazorComponentResult<GalleriesPage>(new Dictionary<string, object?>
        {
            [nameof(GalleriesPage.Model)] = model,
        })
        { StatusCode = statusCode };
    }

    /// <summary>
    /// The builder (C3): the gallery (<c>db.gallery.load</c>) and its live member images in gallery
    /// order (one owner image read, <c>db.library.page</c>; tombstoned images are left out). An
    /// unknown gallery is 404.
    /// </summary>
    private static async Task<IResult> RenderBuilderAsync(
        HttpContext ctx, GalleryService galleries, IMongoDatabase db, OpTimer timer, StorageOptions storage,
        LibraryOptions options, IAntiforgery antiforgery, AdminAssets assets, string gid, int statusCode, string? status,
        string? editTitle = null, string? editDescription = null, bool? editPublic = null)
    {
        var gallery = await galleries.LoadAsync(gid);
        if (gallery is null) return Results.NotFound();

        var docs = gallery.Items.Count == 0
            ? []
            : await timer.Time("db.library.page", () => db.GetCollection<ImageDoc>("images")
                .Find(Builders<ImageDoc>.Filter.In(i => i.Id, gallery.Items) & Builders<ImageDoc>.Filter.Exists(i => i.DeletingAt, false))
                .ToListAsync(), isDbRead: true);
        var byId = docs.ToDictionary(d => d.Id, StringComparer.Ordinal);
        var url = PublicEndpoints.MakeUrl(storage);
        var items = gallery.Items
            .Where(byId.ContainsKey)
            .Select(id => Site.ViewMapper.ToView(byId[id], url))
            .ToList();

        var model = new GalleryPageModel(
            gallery, items, options, Token(ctx, antiforgery),
            assets.UrlFor(AdminAssets.LibraryScriptPath),
            assets.UrlFor(AdminAssets.ReorderScriptPath),
            status,
            editTitle ?? gallery.Title,
            editDescription ?? gallery.Description,
            editPublic);

        return new RazorComponentResult<GalleryPage>(new Dictionary<string, object?>
        {
            [nameof(GalleryPage.Model)] = model,
        })
        { StatusCode = statusCode };
    }

    private static FormToken Token(HttpContext ctx, IAntiforgery antiforgery)
    {
        var tokens = antiforgery.GetAndStoreTokens(ctx);
        return new FormToken(tokens.FormFieldName, tokens.RequestToken!);
    }

    /// <summary>The PRG status line (contracts/components.md C1 "Status text").</summary>
    public static string? StatusFromQuery(IQueryCollection q, IReadOnlyList<GalleryRef> galleries)
    {
        if (q["saved"] == "1") return "Saved";
        if (int.TryParse(q["added"], out var added))
        {
            var skipped = int.TryParse(q["skipped"], out var s) ? s : 0;
            var to = q["to"].ToString();
            var title = galleries.FirstOrDefault(g => g.Id == to)?.Title ?? "the gallery";
            return skipped > 0 ? $"{added} added · {skipped} were already in {title}" : $"{added} added";
        }
        if (int.TryParse(q["deleted"], out var deleted))
        {
            var text = $"{deleted} deleted";
            if (int.TryParse(q["leftovers"], out var k) && k > 0)
                text += $" · {k} files could not be removed and were logged";
            if (int.TryParse(q["failed"], out var f) && f > 0)
                text += $" · {f} could not be deleted. Try again.";
            return text;
        }
        return null;
    }

    /// <summary>The gallery list's PRG status line (G2–G4, B6 redirects).</summary>
    public static string? GalleriesStatusFromQuery(IQueryCollection q)
    {
        if (q["ordered"] == "1") return "Order saved";
        if (VisibilityStatus(q) is { } v) return v;
        if (q["deleted"] == "1") return "Gallery deleted";
        if (q["deleted"] == "0") return "That gallery was already deleted.";
        return null;
    }

    /// <summary>The builder's PRG status line (L3 new gallery, B2–B5, G4 redirects).</summary>
    public static string? BuilderStatusFromQuery(IQueryCollection q)
    {
        if (q["saved"] == "1")
            return VisibilityStatus(q) is { } changed ? $"Saved. {changed}" : "Saved";
        if (q["ordered"] == "1") return "Order saved";
        if (int.TryParse(q["added"], out var added)) return $"{added} added";
        if (q["removed"] == "1") return "Removed from this gallery";
        return VisibilityStatus(q);
    }

    private static string? VisibilityStatus(IQueryCollection q) => q["visibility"].ToString() switch
    {
        "public" => "The gallery is public",
        "private" => "The gallery is private",
        _ => null,
    };

    /// <summary>A builder POST's outcome: 303 on success, 404 unknown, else the builder re-rendered with the reason.</summary>
    private static async Task<IResult> Builder(
        HttpContext ctx, GalleryService galleries, IMongoDatabase db, OpTimer timer, StorageOptions storage,
        LibraryOptions options, IAntiforgery antiforgery, AdminAssets assets, string gid, OperationResult r, string ok,
        string? editTitle = null, string? editDescription = null, bool? editPublic = null) =>
        r.Status switch
        {
            OperationStatus.Ok => SeeOther(ok),
            OperationStatus.NotFound => Results.NotFound(),
            _ => await RenderBuilderAsync(ctx, galleries, db, timer, storage, options, antiforgery, assets, gid,
                r.StatusCode, r.Reason, editTitle: editTitle, editDescription: editDescription, editPublic: editPublic),
        };

    // ── request helpers ──────────────────────────────────────────────────────

    /// <summary>303 See Other (Results.Redirect answers 302).</summary>
    private static IResult SeeOther(string location) => new SeeOtherResult(location);

    private sealed class SeeOtherResult(string location) : IResult
    {
        public Task ExecuteAsync(HttpContext httpContext)
        {
            httpContext.Response.StatusCode = StatusCodes.Status303SeeOther;
            httpContext.Response.Headers.Location = location;
            return Task.CompletedTask;
        }
    }

    /// <summary>The form, or an empty one for a bare POST: never <c>ReadFormAsync</c> without a form content type.</summary>
    private static async Task<IFormCollection> FormAsync(HttpContext ctx) =>
        ctx.Request.HasFormContentType ? await ctx.Request.ReadFormAsync() : FormCollection.Empty;

    /// <summary>A field's value, or null when the field was not sent at all (null keeps the stored value).</summary>
    private static string? Field(IFormCollection form, string name) =>
        form.TryGetValue(name, out var v) ? v.ToString() : null;

    private static List<string> Ids(IFormCollection form, string name) =>
        form.TryGetValue(name, out var v) ? v.Where(s => !string.IsNullOrEmpty(s)).Select(s => s!).ToList() : [];

    private static int PageOf(string? s) => int.TryParse(s, out var p) && p >= 1 ? p : 1;
}
