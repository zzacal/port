using Gallery.Core.Ids;
using Gallery.Web.Admin.Pages;
using Gallery.Web.Components;
using Gallery.Web.Components.Molecules;
using Gallery.Web.Components.Organisms;
using Gallery.Web.Library;
using Gallery.Web.Upload;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Gallery.Web.Workspace;

/// <summary>
/// 020 T033 [SAFETY] (contracts/http.md "Pages and fragments"; research R-01, R-02, R-03): the workspace
/// pages W1 <c>/admin</c>, W2 <c>/admin/unplaced</c>, W3 <c>/admin/g/{gid}</c> and the tile fragment
/// W4 <c>/admin/tiles/{iid}</c>. Mapped on the passed protected <c>admin</c> group, so every route
/// inherits the owner policy (I.3) and the antiforgery filter (I.6); none is anonymous.
/// <list type="bullet">
/// <item><c>X-Workspace</c> absent → the full document; <c>main</c>/<c>chrome</c>/<c>more</c> → that
///   fragment (<c>text/html</c>, no <c>&lt;html&gt;</c>); any other value → 400, before any read;</item>
/// <item><c>more</c> is W1/W2 only and needs a well-formed <c>?before=</c> cursor, else 400;</item>
/// <item>an invalid or unknown gallery or tile id → 404;</item>
/// <item>reads per response are exactly R-02: W1/W2 full 4, W3 full 5, main 2, chrome 3, more 1, W4 1.</item>
/// </list>
/// </summary>
public static class WorkspaceEndpoints
{
    /// <summary>The fragment selector request header.</summary>
    public const string Header = "X-Workspace";

    private static readonly string[] GetVerbs = ["GET", "HEAD"];

    public static RouteGroupBuilder MapWorkspace(this RouteGroupBuilder admin)
    {
        admin.MapMethods("/", GetVerbs, (HttpContext ctx, WorkspaceReader reader, LibraryOptions options,
            IAntiforgery antiforgery, AdminAssets assets) =>
            LibraryAsync(ctx, reader, options, antiforgery, assets, unplaced: false));                    // W1
        admin.MapMethods("/unplaced", GetVerbs, (HttpContext ctx, WorkspaceReader reader, LibraryOptions options,
            IAntiforgery antiforgery, AdminAssets assets) =>
            LibraryAsync(ctx, reader, options, antiforgery, assets, unplaced: true));                     // W2
        admin.MapMethods("/g/{gid}", GetVerbs, GalleryAsync);                                             // W3
        admin.MapGet("/tiles/{iid}", TileAsync);                                                          // W4
        return admin;
    }

    /// <summary>
    /// The part the request asks for; false (→ 400) for a value other than <c>main</c>, <c>chrome</c>,
    /// <c>more</c>, for an empty value and for more than one value.
    /// </summary>
    internal static bool TryPart(HttpRequest request, out WorkspacePart part)
    {
        part = WorkspacePart.Full;
        if (!request.Headers.TryGetValue(Header, out var values)) return true;
        if (values.Count != 1) return false;
        switch (values[0])
        {
            case "main": part = WorkspacePart.Main; return true;
            case "chrome": part = WorkspacePart.Chrome; return true;
            case "more": part = WorkspacePart.More; return true;
            default: return false;
        }
    }

    /// <summary>W1/W2: All photos or Not in a gallery.</summary>
    private static async Task<IResult> LibraryAsync(HttpContext ctx, WorkspaceReader reader, LibraryOptions options,
        IAntiforgery antiforgery, AdminAssets assets, bool unplaced)
    {
        if (!TryPart(ctx.Request, out var part)) return Results.BadRequest();
        var current = unplaced ? CollectionList.UnplacedKey : CollectionList.All;

        switch (part)
        {
            case WorkspacePart.More:
            {
                string? before = ctx.Request.Query["before"];
                if (string.IsNullOrEmpty(before)) return Results.BadRequest();
                var chunk = await reader.ChunkAsync(unplaced, before);                      // 1 read
                if (chunk is null) return Results.BadRequest();
                return Render(Model(part, current, null, null, null, chunk.Tiles, chunk.Next, null, options, assets));
            }
            case WorkspacePart.Main:
            {
                var counts = await reader.CountsAsync();                                    // 1
                var chunk = (await reader.ChunkAsync(unplaced, null))!;                     // 2
                return Render(Model(part, current, counts, null, null, chunk.Tiles, chunk.Next, null, options, assets));
            }
            case WorkspacePart.Chrome:
            {
                var galleries = await reader.GalleriesAsync();                              // 2
                var counts = await reader.CountsAsync();                                    // 3
                return Render(Model(part, current, counts, galleries, null, [], null, Token(ctx, antiforgery), options, assets));
            }
            default:
            {
                var counts = await reader.CountsAsync();                                    // 1
                var chunk = (await reader.ChunkAsync(unplaced, null))!;                     // 2
                var galleries = await reader.GalleriesAsync();                              // 4
                return Render(Model(part, current, counts, galleries, null, chunk.Tiles, chunk.Next, Token(ctx, antiforgery), options, assets));
            }
        }
    }

    /// <summary>W3: one gallery's members, in gallery order. Unknown or invalid → 404.</summary>
    private static async Task<IResult> GalleryAsync(string gid, HttpContext ctx, WorkspaceReader reader,
        LibraryOptions options, IAntiforgery antiforgery, AdminAssets assets)
    {
        if (!TryPart(ctx.Request, out var part)) return Results.BadRequest();
        if (part == WorkspacePart.More) return Results.BadRequest();
        if (!OpaqueId.IsValid(gid)) return Results.NotFound();

        if (part == WorkspacePart.Chrome)
        {
            var galleries = await reader.GalleriesAsync();                                  // 2
            if (!galleries.Any(g => g.Id == gid)) return Results.NotFound();
            var counts = await reader.CountsAsync();                                        // 3
            return Render(Model(part, gid, counts, galleries, null, [], null, Token(ctx, antiforgery), options, assets));
        }

        var gallery = await reader.GalleryAsync(gid);                                       // 1
        if (gallery is null) return Results.NotFound();
        var members = await reader.MembersAsync(gallery);                                   // 2
        if (part == WorkspacePart.Main)
            return Render(Model(part, gid, null, null, gallery, members, null, null, options, assets));

        var all = await reader.GalleriesAsync();                                            // 4
        var totals = await reader.CountsAsync();                                            // 5
        return Render(Model(part, gid, totals, all, gallery, members, null, Token(ctx, antiforgery), options, assets));
    }

    /// <summary>W4: one live photo's cell (one read). Unknown, invalid or tombstoned → 404.</summary>
    private static async Task<IResult> TileAsync(string iid, WorkspaceReader reader)
    {
        if (!OpaqueId.IsValid(iid)) return Results.NotFound();
        var tile = await reader.TileAsync(iid);                                             // 1
        if (tile is null) return Results.NotFound();
        return new RazorComponentResult<PhotoCell>(new Dictionary<string, object?>
        {
            [nameof(PhotoCell.Image)] = tile.Image,
            [nameof(PhotoCell.Caption)] = tile.Caption,
            [nameof(PhotoCell.GalleryIds)] = tile.GalleryIds,
        });
    }

    private static WorkspacePageModel Model(WorkspacePart part, string current, WorkspaceCounts? counts,
        IReadOnlyList<GalleryRef>? galleries, Data.GalleryDoc? gallery, IReadOnlyList<WorkspaceTile> tiles, string? next,
        FormToken? csrf, LibraryOptions options, AdminAssets assets) =>
        new(part, current, counts, galleries, gallery, tiles, next, csrf, options,
            assets.UrlFor(AdminAssets.WorkspaceScriptPath), assets.ScriptUrl, assets.WorkerUrl);

    private static RazorComponentResult<WorkspacePage> Render(WorkspacePageModel model) =>
        new(new Dictionary<string, object?> { [nameof(WorkspacePage.Model)] = model });

    /// <summary>The antiforgery request token for <c>data-csrf</c> and the sign-out form.</summary>
    private static FormToken Token(HttpContext ctx, IAntiforgery antiforgery)
    {
        var tokens = antiforgery.GetAndStoreTokens(ctx);
        return new FormToken(tokens.FormFieldName, tokens.RequestToken!);
    }
}
