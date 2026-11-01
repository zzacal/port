using Gallery.Web.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Antiforgery;

namespace Gallery.Web.Admin.Auth;

/// <summary>
/// Configures owner authentication: cookie scheme, OIDC (when configured), authorization, antiforgery.
/// Research R-01, R-03, R-04.
/// </summary>
public static class AuthSetup
{
    public const string OwnerScheme = "owner";

    public static IServiceCollection AddOwnerAuth(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // R-03 probe part 3: suppress ASP.NET Core's auto-default-scheme when only one scheme exists.
        AppContext.SetSwitch(
            "Microsoft.AspNetCore.Authentication.SuppressAutoDefaultScheme", true);

        var googleOptions = configuration.GetSection("Authentication:Google").Get<GoogleOptions>()
                            ?? new GoogleOptions();
        services.AddSingleton(googleOptions);

        // Cookie scheme (owner) — no default scheme arguments (R-03)
        var authBuilder = services
            .AddAuthentication()
            .AddCookie(OwnerScheme, o =>
            {
                o.Cookie.Name = "__Host-gallery";
                o.Cookie.Path = "/";
                o.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                o.Cookie.HttpOnly = true;
                o.Cookie.SameSite = SameSiteMode.Lax;
                o.ExpireTimeSpan = TimeSpan.FromDays(7);
                o.SlidingExpiration = true;

                // Unauthenticated GET/HEAD → 302 to /admin/signin; other methods → 401
                o.Events.OnRedirectToLogin = ctx =>
                {
                    var method = ctx.Request.Method;
                    if (method.Equals("GET", StringComparison.OrdinalIgnoreCase)
                        || method.Equals("HEAD", StringComparison.OrdinalIgnoreCase))
                    {
                        ctx.Response.Redirect("/admin/signin");
                    }
                    else
                    {
                        ctx.Response.StatusCode = 401;
                        ctx.Response.ContentLength = 0;
                    }
                    return Task.CompletedTask;
                };

                // Access denied → 403
                o.Events.OnRedirectToAccessDenied = ctx =>
                {
                    ctx.Response.StatusCode = 403;
                    return Task.CompletedTask;
                };

                // R-06: validate principal against stored owner on every admin request
                o.Events.OnValidatePrincipal = async ctx =>
                {
                    var store = ctx.HttpContext.RequestServices.GetRequiredService<OwnerStore>();
                    var storedOwner = await store.ReadOwnerAsync();
                    var sub = ctx.Principal?.FindFirst("sub")?.Value;

                    if (storedOwner is null || sub is null || storedOwner != sub)
                    {
                        ctx.RejectPrincipal();
                        await ctx.HttpContext.SignOutAsync(OwnerScheme);
                    }
                };
            });

        // OIDC only when Google is configured (R-02)
        if (googleOptions.IsConfigured)
        {
            authBuilder.AddOpenIdConnect("google", o =>
            {
                o.Authority = googleOptions.Authority;
                o.RequireHttpsMetadata = googleOptions.RequireHttpsMetadata;
                o.ClientId = googleOptions.ClientId;
                o.ClientSecret = googleOptions.ClientSecret;
                o.ResponseType = "code";
                o.Scope.Clear();
                o.Scope.Add("openid");
                o.MapInboundClaims = false;
                o.SaveTokens = false;
                o.GetClaimsFromUserInfoEndpoint = false;
                o.CallbackPath = "/signin-oidc";
                o.SignInScheme = OwnerScheme;

                o.Events.OnRedirectToIdentityProvider = ctx =>
                {
                    ctx.ProtocolMessage.Prompt = "select_account";
                    return Task.CompletedTask;
                };

                o.Events.OnTokenValidated = OidcEvents.OnTokenValidated;
                o.Events.OnRemoteFailure = OidcEvents.OnRemoteFailure;
            });
        }

        services.AddAuthorization(o =>
        {
            o.AddPolicy("owner", p =>
            {
                p.RequireAuthenticatedUser();
                p.AddAuthenticationSchemes(OwnerScheme);
            });
        });

        services.AddAntiforgery(o =>
        {
            o.Cookie.Name = "__Host-af";
            o.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            o.HeaderName = "X-CSRF-TOKEN";
        });

        services.AddScoped<OwnerSignIn>();

        return services;
    }
}
