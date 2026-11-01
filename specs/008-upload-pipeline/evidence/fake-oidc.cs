#:sdk Microsoft.NET.Sdk.Web
#:property PublishAot=false
// 008 T050 (decisions row 40): a throwaway OIDC provider for verify.sh, so the live curl
// walkthrough signs in unattended with dummy client values instead of real Google.
// It serves the same endpoints as tests/Gallery.Web.Tests/Fixtures/FakeGoogle.cs (discovery,
// jwks, token) plus the authorize redirect a browser would follow. No #:package: the RS256
// id_token is built by hand with System.Security.Cryptography.RSA and base64url.
//
//   dotnet run fake-oidc.cs -- --urls http://127.0.0.1:5093
//
// GET /authorize?redirect_uri=..&state=..&nonce=..[&login_hint=<sub>] gives code=base64url(sub.nonce)
//   (sub defaults to "verify-owner"): with response_mode=form_post (the ASP.NET default) a 200 HTML
//   form that posts code and state to redirect_uri, otherwise a 302 to {redirect_uri}?code=..&state=..
// POST /token with code=... answers an id_token for that sub and nonce, aud = client_id.
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
var app = builder.Build();

using var rsa = RSA.Create(2048);
var kid = Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant();

string B64Url(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');
byte[] FromB64Url(string s)
{
    s = s.Replace('-', '+').Replace('_', '/');
    s += (s.Length % 4) switch { 2 => "==", 3 => "=", _ => "" };
    return Convert.FromBase64String(s);
}
string BaseUrl(HttpContext ctx) => $"{ctx.Request.Scheme}://{ctx.Request.Host}";

app.MapGet("/.well-known/openid-configuration", (HttpContext ctx) =>
{
    var b = BaseUrl(ctx);
    return Results.Json(new
    {
        issuer = b,
        authorization_endpoint = $"{b}/authorize",
        token_endpoint = $"{b}/token",
        jwks_uri = $"{b}/jwks",
        response_types_supported = new[] { "code" },
        subject_types_supported = new[] { "public" },
        id_token_signing_alg_values_supported = new[] { "RS256" },
    });
});

app.MapGet("/jwks", () =>
{
    var p = rsa.ExportParameters(false);
    return Results.Json(new
    {
        keys = new[] { new { kty = "RSA", use = "sig", alg = "RS256", kid, n = B64Url(p.Modulus!), e = B64Url(p.Exponent!) } },
    });
});

app.MapGet("/authorize", (HttpContext ctx) =>
{
    var q = ctx.Request.Query;
    var redirect = q["redirect_uri"].ToString();
    var state = q["state"].ToString();
    var nonce = q["nonce"].ToString();
    if (redirect.Length == 0 || state.Length == 0 || nonce.Length == 0)
        return Results.BadRequest(new { error = "invalid_request" });
    var sub = q["login_hint"].ToString() is { Length: > 0 } hint ? hint : "verify-owner";
    var code = B64Url(Encoding.UTF8.GetBytes($"{sub}.{nonce}"));
    if (q["response_mode"] == "form_post")
    {
        // What a real provider sends for form_post: an auto-submitting form back to the app.
        string H(string s) => System.Net.WebUtility.HtmlEncode(s);
        var html = $"<!doctype html><html><body onload=\"document.forms[0].submit()\">" +
                   $"<form method=\"post\" action=\"{H(redirect)}\">" +
                   $"<input type=\"hidden\" name=\"code\" value=\"{H(code)}\">" +
                   $"<input type=\"hidden\" name=\"state\" value=\"{H(state)}\">" +
                   "</form></body></html>";
        return Results.Content(html, "text/html");
    }
    var sep = redirect.Contains('?') ? '&' : '?';
    return Results.Redirect($"{redirect}{sep}code={Uri.EscapeDataString(code)}&state={Uri.EscapeDataString(state)}");
});

app.MapPost("/token", async (HttpContext ctx) =>
{
    var form = await ctx.Request.ReadFormAsync();
    string sub, nonce;
    try
    {
        var decoded = Encoding.UTF8.GetString(FromB64Url(form["code"].ToString()));
        var dot = decoded.IndexOf('.');
        if (dot <= 0) throw new FormatException();
        sub = decoded[..dot];
        nonce = decoded[(dot + 1)..];
    }
    catch (FormatException)
    {
        return Results.Json(new { error = "invalid_grant" }, statusCode: 400);
    }

    var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    var header = JsonSerializer.SerializeToUtf8Bytes(new { alg = "RS256", typ = "JWT", kid });
    var payload = JsonSerializer.SerializeToUtf8Bytes(new
    {
        iss = BaseUrl(ctx),
        aud = form["client_id"].ToString(),
        sub,
        nonce,
        iat = now,
        nbf = now,
        exp = now + 300,
    });
    var signingInput = $"{B64Url(header)}.{B64Url(payload)}";
    var sig = rsa.SignData(Encoding.ASCII.GetBytes(signingInput), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
    return Results.Json(new
    {
        access_token = "x",
        token_type = "Bearer",
        expires_in = 3600,
        id_token = $"{signingInput}.{B64Url(sig)}",
    });
});

app.Run();
