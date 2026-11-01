using Gallery.Web.Ui.Pages;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Gallery.Web.Ui;

public static class UiEndpoints
{
    /// <summary>
    /// Maps /ui/ and one route per UiCatalog entry, in every environment, behind the owner session
    /// (FR-021, D-21). Without it a GET is redirected to /admin/signin.
    /// </summary>
    public static void MapUi(this WebApplication app)
    {
        var ui = app.MapGroup("/ui").RequireAuthorization("owner").AddEndpointFilter(async (ctx, next) =>
        {
            ctx.HttpContext.Response.Headers["X-Robots-Tag"] = "noindex";
            return await next(ctx);
        });

        string[] verbs = ["GET", "HEAD"];

        ui.MapMethods("/", verbs, () => new RazorComponentResult<Pages.Index>());

        foreach (var entry in UiCatalog.Entries)
        {
            var type = entry.PageType;
            ui.MapMethods("/" + entry.Slug, verbs, () => new RazorComponentResult(type));
        }
    }
}
