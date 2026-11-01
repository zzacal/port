namespace Gallery.Web.Admin;

/// <summary>
/// Middleware extension that sets Cache-Control: no-store and X-Robots-Tag: noindex
/// on all /admin and /signin-oidc responses (FR-017, invariant I-5).
/// </summary>
public static class AdminHeaders
{
    public static IApplicationBuilder UseAdminHeaders(this IApplicationBuilder app)
    {
        return app.Use(async (context, next) =>
        {
            var path = context.Request.Path;
            if (path.StartsWithSegments("/admin", StringComparison.OrdinalIgnoreCase)
                || path.StartsWithSegments("/signin-oidc", StringComparison.OrdinalIgnoreCase))
            {
                context.Response.OnStarting(() =>
                {
                    context.Response.Headers["Cache-Control"] = "no-store";
                    context.Response.Headers["X-Robots-Tag"] = "noindex";
                    return Task.CompletedTask;
                });
            }

            await next(context);
        });
    }
}
