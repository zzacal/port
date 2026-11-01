using System.Security.Claims;
using Gallery.Web.Admin.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;

namespace Gallery.Web.Admin.Auth;

/// <summary>
/// OIDC event handlers wired into the OIDC options in AuthSetup.
/// OnTokenValidated (T016): runs the owner sign-in outcome logic.
/// OnRemoteFailure (T021): handles errors from the OIDC provider.
/// </summary>
public static class OidcEvents
{
    /// <summary>
    /// T016: Called after the ID token is validated. Resolves the sign-in outcome and
    /// either establishes the owner session or redirects to the refused page.
    /// </summary>
    public static async Task OnTokenValidated(TokenValidatedContext context)
    {
        var sub = context.Principal?.FindFirst("sub")?.Value;
        if (string.IsNullOrEmpty(sub))
        {
            context.Fail("no sub");
            return;
        }

        var ip = context.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        var signIn = context.HttpContext.RequestServices.GetRequiredService<OwnerSignIn>();
        var outcome = await signIn.ResolveAsync(sub, ip);

        if (outcome is SignInOutcome.Claimed or SignInOutcome.SignedIn)
        {
            // Replace principal with a minimal identity (only sub)
            var identity = new ClaimsIdentity(
                [new Claim("sub", sub)],
                AuthSetup.OwnerScheme, "sub", null);
            context.Principal = new ClaimsPrincipal(identity);
            if (context.Properties is null)
                context.Properties = new Microsoft.AspNetCore.Authentication.AuthenticationProperties();
            context.Properties.IsPersistent = true;

            var logger = context.HttpContext.RequestServices
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger("Gallery.Auth");
            logger.LogInformation("auth.signin {sub} {ip}", sub, ip);
        }
        else
        {
            // Refused: redirect to sign-in page
            context.HandleResponse();
            context.Response.Redirect("/admin/signin?r=refused");
        }
    }

    /// <summary>
    /// T021: Handles remote failures (cancel, error response, bad state, replay, nonce mismatch).
    /// Logs auth.failed and redirects to /admin/signin?r=failed.
    /// </summary>
    public static Task OnRemoteFailure(RemoteFailureContext context)
    {
        var ip = context.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var reason = context.Failure?.GetType().Name ?? "unknown";

        var logger = context.HttpContext.RequestServices
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("Gallery.Auth");
        logger.LogInformation("auth.failed {ip} {reason}", ip, reason);

        context.HandleResponse();
        context.Response.Redirect("/admin/signin?r=failed");
        return Task.CompletedTask;
    }
}
