using System.Security.Cryptography;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Gallery.Web.Tests.Fixtures;

/// <summary>
/// T012: A minimal fake OIDC provider that serves openid-configuration, JWKS, and token endpoints.
/// Used to test the OIDC sign-in flow without hitting Google.
/// Research R-10.
/// </summary>
public sealed class FakeGoogle : IAsyncLifetime
{
    private WebApplication? _app;
    private readonly RSA _rsa = RSA.Create(2048);
    private string _kid = Guid.NewGuid().ToString("N");

    public string Authority { get; private set; } = "";

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder(["--urls", "http://127.0.0.1:0"]);
        builder.Logging.ClearProviders();
        var app = builder.Build();

        app.MapGet("/.well-known/openid-configuration", (HttpContext ctx) =>
        {
            var baseUrl = $"{ctx.Request.Scheme}://{ctx.Request.Host}";
            return Results.Json(new
            {
                issuer = baseUrl,
                authorization_endpoint = $"{baseUrl}/authorize",
                token_endpoint = $"{baseUrl}/token",
                jwks_uri = $"{baseUrl}/jwks",
                response_types_supported = new[] { "code" },
                subject_types_supported = new[] { "public" },
                id_token_signing_alg_values_supported = new[] { "RS256" },
            });
        });

        app.MapGet("/jwks", () =>
        {
            var key = new RsaSecurityKey(_rsa) { KeyId = _kid };
            var jwk = JsonWebKeyConverter.ConvertFromRSASecurityKey(key);
            return Results.Json(new { keys = new[] { jwk } });
        });

        app.MapPost("/token", async (HttpContext ctx) =>
        {
            var form = await ctx.Request.ReadFormAsync();
            var code = form["code"].ToString();

            if (code == "fail")
            {
                ctx.Response.StatusCode = 400;
                await ctx.Response.WriteAsJsonAsync(new { error = "invalid_grant" });
                return;
            }

            string sub, nonce;
            try
            {
                var decoded = System.Text.Encoding.UTF8.GetString(
                    Convert.FromBase64String(PadBase64(code)));
                var dotIdx = decoded.IndexOf('.');
                if (dotIdx < 0) throw new FormatException("No dot separator");
                sub = decoded[..dotIdx];
                nonce = decoded[(dotIdx + 1)..];
            }
            catch
            {
                ctx.Response.StatusCode = 400;
                await ctx.Response.WriteAsJsonAsync(new { error = "invalid_code" });
                return;
            }

            var clientId = form["client_id"].ToString();
            var baseUrl = $"{ctx.Request.Scheme}://{ctx.Request.Host}";
            var now = DateTime.UtcNow;

            var key = new RsaSecurityKey(_rsa) { KeyId = _kid };
            var handler = new JsonWebTokenHandler();
            var descriptor = new SecurityTokenDescriptor
            {
                Issuer = baseUrl,
                Audience = clientId,
                NotBefore = now,
                Expires = now.AddMinutes(5),
                IssuedAt = now,
                SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.RsaSha256),
                Claims = new Dictionary<string, object>
                {
                    ["sub"] = sub,
                    ["nonce"] = nonce,
                },
            };

            var idToken = handler.CreateToken(descriptor);

            await ctx.Response.WriteAsJsonAsync(new
            {
                access_token = "x",
                token_type = "Bearer",
                expires_in = 3600,
                id_token = idToken,
            });
        });

        await app.StartAsync();
        var address = app.Urls.First();
        Authority = address;
        _app = app;
    }

    public async Task DisposeAsync()
    {
        _rsa.Dispose();
        if (_app is not null)
        {
            await _app.StopAsync();
            if (_app is IAsyncDisposable d) await d.DisposeAsync();
        }
    }

    /// <summary>
    /// Creates an authorization code that encodes sub and nonce as base64url({sub}.{nonce}).
    /// </summary>
    public static string Code(string sub, string nonce)
    {
        var raw = $"{sub}.{nonce}";
        return Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(raw))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static string PadBase64(string s)
    {
        // Restore standard base64 from base64url
        s = s.Replace('-', '+').Replace('_', '/');
        switch (s.Length % 4)
        {
            case 2: s += "=="; break;
            case 3: s += "="; break;
        }
        return s;
    }
}
