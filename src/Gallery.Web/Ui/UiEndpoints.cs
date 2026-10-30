using Gallery.Web.Ui.Pages;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Gallery.Web.Ui;

public static class UiEndpoints
{
    /// <summary>
    /// Maps /ui/ and one route per UiCatalog entry. Call only in Development (FR-010);
    /// becomes the owner policy once sign-in exists.
    /// </summary>
    public static void MapUi(this WebApplication app)
    {
        var ui = app.MapGroup("/ui").AddEndpointFilter(async (ctx, next) =>
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
