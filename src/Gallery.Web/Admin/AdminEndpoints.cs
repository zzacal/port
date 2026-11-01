using Gallery.Web.Admin.Auth;
using Gallery.Web.Admin.Pages;
using Gallery.Web.Components;
using Gallery.Web.Data;
using Gallery.Web.Site;
using Gallery.Web.Storage;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Gallery.Web.Admin;

/// <summary>
/// Maps admin routes. The protected group requires the "owner" policy and validates anti-forgery.
/// </summary>
public static class AdminEndpoints
{
    public static void MapAdmin(this WebApplication app)
    {
        var getVerbs = new[] { "GET", "HEAD" };

        // H1: GET/HEAD /admin/signin — sign-in page (no auth required)
        app.MapMethods("/admin/signin", getVerbs, async (
            HttpContext ctx,
            OwnerStore ownerStore,
            ContentReader contentReader,
            StorageOptions storage,
            IAntiforgery antiforgery,
            GoogleOptions googleOptions) =>
        {
            // If already authenticated, redirect to /admin
            var auth = await ctx.AuthenticateAsync(AuthSetup.OwnerScheme);
            if (auth.Succeeded)
                return Results.Redirect("/admin");

            var r = ctx.Request.Query["r"].ToString();

            // Determine notice
            string? notice = null;
            if (r == "failed")
                notice = "Sign-in didn't complete. Nothing was saved.";
            else if (!googleOptions.IsConfigured)
                notice = "Sign-in isn't set up on this server.";

            // Determine state
            SignInState state;
            if (r == "refused")
            {
                state = SignInState.Refused;
            }
            else
            {
                var owner = await ownerStore.ReadOwnerAsync();
                state = owner is null ? SignInState.Claim : SignInState.SignIn;
            }

            // Get cover image from first home card with a cover
            Gallery.Core.Images.ImageView? cover = null;
            try
            {
                var site = await contentReader.HomeAsync();
                if (site is not null)
                {
                    var url = PublicEndpoints.MakeUrl(storage);
                    var cardWithCover = site.HomeCards.FirstOrDefault(c => c.Cover is not null);
                    if (cardWithCover is not null)
                        cover = ViewMapper.ToCard(cardWithCover, url).Image;
                }
            }
            catch { /* cover is optional */ }

            var tokens = antiforgery.GetAndStoreTokens(ctx);
            var af = new FormToken(tokens.FormFieldName, tokens.RequestToken!);

            return (IResult)new RazorComponentResult<SignInPage>(new Dictionary<string, object?>
            {
                [nameof(SignInPage.State)] = state,
                [nameof(SignInPage.Cover)] = cover,
                [nameof(SignInPage.Notice)] = notice,
                [nameof(SignInPage.Antiforgery)] = af,
            });
        });

        // H2: POST /admin/signin — initiate Google OIDC challenge (no auth required)
        app.MapPost("/admin/signin", async (
            HttpContext ctx,
            IAntiforgery antiforgery,
            OwnerStore ownerStore,
            ContentReader contentReader,
            StorageOptions storage,
            GoogleOptions googleOptions) =>
        {
            // Check antiforgery manually (this route is outside the filter group)
            var isValid = await antiforgery.IsRequestValidAsync(ctx);
            if (!isValid)
            {
                var owner400 = await ownerStore.ReadOwnerAsync();
                var state400 = owner400 is null ? SignInState.Claim : SignInState.SignIn;
                Gallery.Core.Images.ImageView? cover400 = null;
                try
                {
                    var site = await contentReader.HomeAsync();
                    if (site is not null)
                    {
                        var url = PublicEndpoints.MakeUrl(storage);
                        var cardWithCover = site.HomeCards.FirstOrDefault(c => c.Cover is not null);
                        if (cardWithCover is not null)
                            cover400 = ViewMapper.ToCard(cardWithCover, url).Image;
                    }
                }
                catch { }
                var tokens400 = antiforgery.GetAndStoreTokens(ctx);
                var af400 = new FormToken(tokens400.FormFieldName, tokens400.RequestToken!);
                ctx.Response.StatusCode = 400;
                return (IResult)new RazorComponentResult<SignInPage>(new Dictionary<string, object?>
                {
                    [nameof(SignInPage.State)] = state400,
                    [nameof(SignInPage.Cover)] = cover400,
                    [nameof(SignInPage.Notice)] = "Your sign-in form expired. Try again.",
                    [nameof(SignInPage.Antiforgery)] = af400,
                });
            }

            if (!googleOptions.IsConfigured)
            {
                var owner503 = await ownerStore.ReadOwnerAsync();
                var state503 = owner503 is null ? SignInState.Claim : SignInState.SignIn;
                var tokens503 = antiforgery.GetAndStoreTokens(ctx);
                var af503 = new FormToken(tokens503.FormFieldName, tokens503.RequestToken!);
                ctx.Response.StatusCode = 503;
                return (IResult)new RazorComponentResult<SignInPage>(new Dictionary<string, object?>
                {
                    [nameof(SignInPage.State)] = state503,
                    [nameof(SignInPage.Cover)] = (Gallery.Core.Images.ImageView?)null,
                    [nameof(SignInPage.Notice)] = "Sign-in isn't set up on this server.",
                    [nameof(SignInPage.Antiforgery)] = af503,
                });
            }

            return Results.Challenge(
                new AuthenticationProperties { RedirectUri = "/admin" },
                ["google"]);
        });

        // Protected group: requires owner session + anti-forgery filter
        var admin = app
            .MapGroup("/admin")
            .RequireAuthorization("owner")
            .AddEndpointFilter<AdminAntiforgeryFilter>();

        // H4: GET/HEAD /admin — owner shell
        admin.MapMethods("/", getVerbs, (HttpContext ctx, IAntiforgery antiforgery) =>
        {
            var tokens = antiforgery.GetAndStoreTokens(ctx);
            var af = new FormToken(tokens.FormFieldName, tokens.RequestToken!);
            return (IResult)new RazorComponentResult<ShellPage>(new Dictionary<string, object?>
            {
                [nameof(ShellPage.SignOut)] = af,
            });
        });

        // H5: POST /admin/signout — sign out (log, clear cookie, redirect)
        admin.MapPost("/signout", async (HttpContext ctx, ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("Gallery.Auth");
            var sub = ctx.User.FindFirst("sub")?.Value ?? "unknown";
            var ip = ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            logger.LogInformation("auth.signout {sub} {ip}", sub, ip);
            await ctx.SignOutAsync(AuthSetup.OwnerScheme);
            return Results.Redirect("/admin/signin");
        });

        // H6: Catch-all — any unmatched /admin/** returns 404 (after auth + CSRF)
        admin.MapMethods("{**rest}", ["GET", "HEAD", "POST", "PUT", "DELETE", "PATCH", "OPTIONS"],
            () => Results.StatusCode(404));
    }
}
