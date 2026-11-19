using Gallery.Web.Admin.Auth;
using Gallery.Web.Admin.Pages;
using Gallery.Web.Components;
using Gallery.Web.Data;
using Gallery.Web.Library;
using Gallery.Web.Site;
using Gallery.Web.Storage;
using Gallery.Web.Upload;
using Gallery.Web.Workspace;
using System.Security.Claims;
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

        // 018 [SAFETY]: the local sign-in rule, decided once at startup (research R-01).
        var local = app.Services.GetRequiredService<LocalSignIn>();
        if (app.Configuration[LocalSignIn.PasswordKey] is not null)
            app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Gallery.Auth")
                .LogInformation("auth.local.state {state}", StateName(local.State));

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
            else if (!googleOptions.IsConfigured && !local.Enabled)
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
                [nameof(SignInPage.Local)] = local.Enabled,
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

        // H7: POST /admin/signin/local — local password sign-in (018). [SAFETY] Mapped ONLY on a
        // development host with a configured password; elsewhere the address falls to H6.
        if (local.Enabled)
            app.MapPost("/admin/signin/local", async (
                HttpContext ctx,
                IAntiforgery antiforgery,
                OwnerStore ownerStore,
                OwnerSignIn ownerSignIn,
                ContentReader contentReader,
                StorageOptions storage,
                ILoggerFactory loggerFactory) =>
            {
                var logger = loggerFactory.CreateLogger("Gallery.Auth");
                var ip = ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";

                // [SAFETY] Check antiforgery manually (this route is outside the filter group)
                if (!await antiforgery.IsRequestValidAsync(ctx))
                {
                    logger.LogInformation("auth.local.refused {ip} {reason}", ip, "bad_form");
                    return await LocalSignInPageAsync(ctx, ownerStore, contentReader, storage, antiforgery,
                        "Your sign-in form expired. Try again.", StatusCodes.Status400BadRequest);
                }

                var form = await ctx.Request.ReadFormAsync();
                if (!local.Matches(form["password"].ToString()))
                {
                    logger.LogInformation("auth.local.refused {ip} {reason}", ip, "bad_password");
                    return await LocalSignInPageAsync(ctx, ownerStore, contentReader, storage, antiforgery,
                        "That password wasn't accepted.", StatusCodes.Status401Unauthorized);
                }

                var sub = await ownerStore.ReadOwnerAsync();
                var claimed = false;
                if (sub is null)
                {
                    // Call, never copy: the one atomic claim path (logs auth.claim / auth.refused).
                    var outcome = await ownerSignIn.ResolveAsync(LocalSignIn.LocalOwner, ip);
                    if (outcome == SignInOutcome.Claimed)
                    {
                        sub = LocalSignIn.LocalOwner;
                        claimed = true;
                    }
                    else if (outcome == SignInOutcome.SignedIn)
                    {
                        sub = LocalSignIn.LocalOwner;
                    }
                    else
                    {
                        // Lost the race: sign in as the winner (I.7d).
                        sub = await ownerStore.ReadOwnerAsync()
                            ?? throw new InvalidOperationException("The site has no owner after a lost claim.");
                    }
                }

                // The same cookie the OIDC handler issues: owner scheme, a single sub claim, persistent.
                var identity = new ClaimsIdentity([new Claim("sub", sub)], AuthSetup.OwnerScheme, "sub", null);
                await ctx.SignInAsync(AuthSetup.OwnerScheme, new ClaimsPrincipal(identity),
                    new AuthenticationProperties { IsPersistent = true });
                logger.LogInformation("auth.local.signin {sub} {ip} {claimed}", sub, ip, claimed ? "true" : "false");
                return Results.Redirect("/admin");
            }); // H7

        // Protected group: requires owner session + anti-forgery filter
        var admin = app
            .MapGroup("/admin")
            .RequireAuthorization("owner")
            .AddEndpointFilter<AdminAntiforgeryFilter>();

        // H4 → 020 W1-W4: the workspace (pages, fragments, tile); before the H6 catch-all
        admin.MapWorkspace();

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

        // 008 upload routes: inherit owner auth + anti-forgery; must stay before the H6 catch-all
        admin.MapUpload();

        // 020 A1-A11 workspace writes: inherit owner auth + anti-forgery; must stay before the H6 catch-all
        admin.MapWorkspaceApi();

        // H6: Catch-all — any unmatched /admin/** returns 404 (after auth + CSRF)
        admin.MapMethods("{**rest}", ["GET", "HEAD", "POST", "PUT", "DELETE", "PATCH", "OPTIONS"],
            () => Results.StatusCode(404));
    }

    /// <summary>018: the log name of a <see cref="LocalSignInState"/>.</summary>
    private static string StateName(LocalSignInState state) => state switch
    {
        LocalSignInState.Enabled => "enabled",
        LocalSignInState.Absent => "absent",
        LocalSignInState.TooShort => "too_short",
        _ => "not_development_host",
    };

    /// <summary>
    /// 018 H7: re-renders the sign-in page (current state, cover, fresh tokens, the local form) with a
    /// notice and status. Never given the password.
    /// </summary>
    private static async Task<IResult> LocalSignInPageAsync(
        HttpContext ctx,
        OwnerStore ownerStore,
        ContentReader contentReader,
        StorageOptions storage,
        IAntiforgery antiforgery,
        string notice,
        int status)
    {
        var owner = await ownerStore.ReadOwnerAsync();
        var state = owner is null ? SignInState.Claim : SignInState.SignIn;
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
        ctx.Response.StatusCode = status;
        return new RazorComponentResult<SignInPage>(new Dictionary<string, object?>
        {
            [nameof(SignInPage.State)] = state,
            [nameof(SignInPage.Cover)] = cover,
            [nameof(SignInPage.Notice)] = notice,
            [nameof(SignInPage.Antiforgery)] = af,
            [nameof(SignInPage.Local)] = true,
        });
    }
}
