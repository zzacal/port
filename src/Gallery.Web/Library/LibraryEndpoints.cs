using System.Net;
using Gallery.Core.Ids;
using Gallery.Web.Admin.Pages;
using Gallery.Web.Components;
using Gallery.Web.Upload;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Gallery.Web.Library;

/// <summary>
/// What the library page (contracts/components.md C1) renders, built by <see cref="LibraryEndpoints"/>.
/// </summary>
/// <param name="Status">The <c>role="status"</c> line: the PRG result or a refusal reason.</param>
/// <param name="EditTitle">The side panel's title value (the submitted one after a refused save, else stored).</param>
public sealed record LibraryPageModel(
    LibraryPageData Data,
    LibraryOptions Options,
    FormToken Csrf,
    string ScriptUrl,
    string? Status = null,
    bool Saved = false,
    string? EditTitle = null,
    string? EditCaption = null);

/// <summary>
/// The 009 owner routes of contracts/http.md: L1–L5, G2–G4 and B2–B6 (tasks.md T029). Mapped by
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
/// G1 and B1 (the gallery pages) belong to Batch E; until then G/B refusals re-render through the
/// minimal <see cref="StatusPage"/> below.
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

        admin.MapPost("/galleries", (Delegate)CreateGallery);                                 // G2
        admin.MapPost("/galleries/order", (Delegate)ReorderGalleries);                        // G3
        admin.MapPost("/galleries/{gid}/visibility", Visibility);                             // G4

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

        var r = await images.UpdateTextAsync(iid, title, caption);
        return r.Status switch
        {
            OperationStatus.Ok => SeeOther($"{LibraryPath}?page={page}&open={iid}&saved=1"),
            OperationStatus.NotFound => await RenderLibraryAsync(ctx, reader, options, antiforgery, assets, page,
                null, 404, "That photo no longer exists."),
            _ => await RenderLibraryAsync(ctx, reader, options, antiforgery, assets, page,
                iid, r.StatusCode, r.Reason, editTitle: title, editCaption: caption),
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

    // ── G2 POST /admin/galleries ─────────────────────────────────────────────

    private static async Task<IResult> CreateGallery(HttpContext ctx, GalleryService galleries, IAntiforgery antiforgery)
    {
        var form = await FormAsync(ctx);
        var r = await galleries.CreateAsync(Field(form, "title"));
        return r.IsOk
            ? SeeOther($"{GalleriesPath}/{r.GalleryId}")
            : StatusPage(ctx, antiforgery, r.StatusCode, "Galleries", r.Reason ?? "", GalleriesPath);
    }

    // ── G3 POST /admin/galleries/order ──────────────────────────────────────

    private static async Task<IResult> ReorderGalleries(HttpContext ctx, GalleryService galleries, IAntiforgery antiforgery)
    {
        var form = await FormAsync(ctx);
        var r = await galleries.ReorderHomeAsync(Ids(form, "order"));
        return r.IsOk
            ? SeeOther($"{GalleriesPath}?ordered=1")
            : StatusPage(ctx, antiforgery, r.StatusCode, "Galleries", r.Reason ?? "", GalleriesPath);
    }

    // ── G4 POST /admin/galleries/{gid}/visibility ────────────────────────────

    private static async Task<IResult> Visibility(string gid, HttpContext ctx, GalleryService galleries, IAntiforgery antiforgery)
    {
        if (!OpaqueId.IsValid(gid)) return Results.NotFound();
        var form = await FormAsync(ctx);
        var wantPublic = form["public"] == "on";
        var confirmed = form["confirm"] == "1";
        var from = form["from"] == "list" ? "list" : "builder";
        var back = from == "list" ? GalleriesPath : $"{GalleriesPath}/{gid}";

        var gallery = await galleries.LoadAsync(gid);
        if (gallery is null) return Results.NotFound();

        if (wantPublic)
        {
            // The service checks emptiness FIRST, then confirmation, then writes conditionally.
            var r = await galleries.PublishAsync(gid, confirmed);
            return r.Status switch
            {
                OperationStatus.Ok => SeeOther($"{back}?visibility=public"),
                OperationStatus.NotFound => Results.NotFound(),
                OperationStatus.NeedsConfirm => PublishWarningPage(ctx, antiforgery, gid, gallery.Title, gallery.Items.Count, from, back),
                _ => StatusPage(ctx, antiforgery, r.StatusCode, gallery.Title, r.Reason ?? "", back),
            };
        }

        if (gallery.Visibility != "public")
        {
            // Nothing to unpublish. An empty private gallery answers the empty refusal whatever was
            // sent (contracts/http.md G4, [SAFETY]); a non-empty one is already private (no write).
            return gallery.Items.Count == 0
                ? StatusPage(ctx, antiforgery, 409, gallery.Title, GalleryService.EmptyPublishReason, back)
                : SeeOther($"{back}?visibility=private");
        }

        var u = await galleries.UnpublishAsync(gid);
        return u.Status == OperationStatus.NotFound ? Results.NotFound() : SeeOther($"{back}?visibility=private");
    }

    // ── B2 POST /admin/galleries/{gid} ───────────────────────────────────────

    private static async Task<IResult> SaveGallery(string gid, HttpContext ctx, GalleryService galleries, IAntiforgery antiforgery)
    {
        if (!OpaqueId.IsValid(gid)) return Results.NotFound();
        var form = await FormAsync(ctx);
        var r = await galleries.UpdateTextAsync(gid, Field(form, "title"), Field(form, "description"));
        return Builder(ctx, antiforgery, gid, r, $"{GalleriesPath}/{gid}?saved=1");
    }

    // ── B3 POST /admin/galleries/{gid}/order ─────────────────────────────────

    private static async Task<IResult> ReorderGallery(string gid, HttpContext ctx, GalleryService galleries, IAntiforgery antiforgery)
    {
        if (!OpaqueId.IsValid(gid)) return Results.NotFound();
        var form = await FormAsync(ctx);
        var r = await galleries.ReorderAsync(gid, Ids(form, "order"));
        return Builder(ctx, antiforgery, gid, r, $"{GalleriesPath}/{gid}?ordered=1");
    }

    // ── B4 POST /admin/galleries/{gid}/cover ─────────────────────────────────

    private static async Task<IResult> Cover(string gid, HttpContext ctx, GalleryService galleries, IAntiforgery antiforgery)
    {
        if (!OpaqueId.IsValid(gid)) return Results.NotFound();
        var form = await FormAsync(ctx);
        var image = Field(form, "image");
        var r = await galleries.SetCoverAsync(gid, string.IsNullOrEmpty(image) ? null : image);
        return Builder(ctx, antiforgery, gid, r, $"{GalleriesPath}/{gid}");
    }

    // ── B5 POST /admin/galleries/{gid}/remove ────────────────────────────────

    private static async Task<IResult> Remove(string gid, HttpContext ctx, GalleryService galleries, IAntiforgery antiforgery)
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
        return Builder(ctx, antiforgery, gid, r, $"{GalleriesPath}/{gid}?removed={r.Removed}");
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
        string? editTitle = null, string? editCaption = null)
    {
        var data = await reader.PageAsync(page, open);
        if (statusFromQuery) status = StatusFromQuery(ctx.Request.Query, data.Galleries);

        var tokens = antiforgery.GetAndStoreTokens(ctx);
        var model = new LibraryPageModel(
            data, options, new FormToken(tokens.FormFieldName, tokens.RequestToken!),
            assets.UrlFor(AdminAssets.LibraryScriptPath),
            status, saved && data.Open is not null,
            editTitle ?? data.Open?.Image.Title,
            editCaption ?? data.Open?.Caption);

        return new RazorComponentResult<LibraryPage>(new Dictionary<string, object?>
        {
            [nameof(LibraryPage.Model)] = model,
        })
        { StatusCode = statusCode };
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

    private static IResult Builder(HttpContext ctx, IAntiforgery antiforgery, string gid, OperationResult r, string ok) =>
        r.Status switch
        {
            OperationStatus.Ok => SeeOther(ok),
            OperationStatus.NotFound => Results.NotFound(),
            _ => StatusPage(ctx, antiforgery, r.StatusCode, "Gallery", r.Reason ?? "", $"{GalleriesPath}/{gid}"),
        };

    /// <summary>
    /// The minimal owner page for G/B refusals until Batch E's gallery pages exist (tasks.md T029):
    /// the reason in <c>role="status"</c> and a link back. No script.
    /// </summary>
    private static IResult StatusPage(HttpContext ctx, IAntiforgery antiforgery, int status, string heading, string reason, string back) =>
        Html(status, heading, $"<p role=\"status\">{E(reason)}</p><p><a href=\"{E(back)}\">Back</a></p>");

    /// <summary>The G4 publish warning (no write), with a form that re-posts it confirmed.</summary>
    private static IResult PublishWarningPage(HttpContext ctx, IAntiforgery antiforgery, string gid, string title, int photos,
        string from, string back)
    {
        var tokens = antiforgery.GetAndStoreTokens(ctx);
        var body =
            $"<p role=\"status\">Make “{E(title)}” public? It will be listed on the home page with its {photos} photos, and anyone can see them.</p>" +
            $"<form method=\"post\" action=\"{GalleriesPath}/{gid}/visibility\">" +
            $"<input type=\"hidden\" name=\"{E(tokens.FormFieldName)}\" value=\"{E(tokens.RequestToken!)}\">" +
            "<input type=\"hidden\" name=\"public\" value=\"on\"><input type=\"hidden\" name=\"confirm\" value=\"1\">" +
            $"<input type=\"hidden\" name=\"from\" value=\"{from}\">" +
            "<button class=\"btn btn-primary\" type=\"submit\">Make public</button></form>" +
            $"<p><a href=\"{E(back)}\">Keep private</a></p>";
        return Html(200, title, body);
    }

    private static IResult Html(int status, string heading, string body) =>
        Results.Content(
            "<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><title>" + E(heading) +
            "</title><meta name=\"robots\" content=\"noindex\"></head><body class=\"admin\"><main class=\"admin-main\"><h1>" +
            E(heading) + "</h1>" + body + "</main></body></html>",
            "text/html; charset=utf-8", statusCode: status);

    private static string E(string s) => WebUtility.HtmlEncode(s);

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
