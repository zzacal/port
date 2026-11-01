using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Gallery.Web.Tests.Fixtures;

/// <summary>
/// T013: Helper that starts the Gallery app for integration testing, with a manual cookie jar,
/// log capture, and sign-in flow helpers.
/// </summary>
public sealed class AdminHost : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly HttpClient _client;
    private readonly Dictionary<string, string> _cookieJar = new();

    /// <summary>Thread-safe list of formatted log messages from all categories.</summary>
    public List<string> LogLines { get; }

    /// <summary>The underlying DI services - use for direct DI access in tests.</summary>
    public IServiceProvider Services => _app.Services;

    /// <summary>The Mongo database name used by this host.</summary>
    public string DatabaseName { get; }

    private AdminHost(WebApplication app, HttpClient client, List<string> logLines, string databaseName)
    {
        _app = app;
        _client = client;
        LogLines = logLines;
        DatabaseName = databaseName;
    }

    public static async Task<AdminHost> CreateAsync(
        MongoFixture mongo,
        FakeGoogle? fakeGoogle = null,
        Dictionary<string, string?>? extraOverrides = null,
        Action<WebApplicationBuilder>? configure = null,
        bool forceHttpsScheme = true)
    {
        var db = mongo.NewDatabase();
        var dbName = db.DatabaseNamespace.DatabaseName;
        var overrides = new Dictionary<string, string?>
        {
            ["Mongo:ConnectionString"] = mongo.ConnectionString,
            ["Mongo:Database"] = db.DatabaseNamespace.DatabaseName,
            ["Storage:ImageBaseUrl"] = "https://img.test",
            ["Storage:ServiceUrl"] = "http://localhost:19999",
            ["Storage:AccessKeyId"] = "local",
            ["Storage:SecretAccessKey"] = "localsecret",
            ["Storage:PublicBucket"] = "pub",
            ["Storage:PrivateBucket"] = "priv",
            ["Site:OutputCache"] = "false",
        };

        if (fakeGoogle is not null)
        {
            overrides["Authentication:Google:ClientId"] = "test-client";
            overrides["Authentication:Google:ClientSecret"] = "test-secret";
            overrides["Authentication:Google:Authority"] = fakeGoogle.Authority;
            overrides["Authentication:Google:RequireHttpsMetadata"] = "false";
        }

        if (extraOverrides is not null)
            foreach (var (k, v) in extraOverrides)
                overrides[k] = v;

        // Use the SAME list instance in both the provider and the host
        var logLines = new List<string>();

        var app = Gallery.Web.AppBuilder.Build(
            ["--urls", "http://127.0.0.1:0"],
            overrides,
            b =>
            {
                b.Logging.AddProvider(new CaptureLoggerProvider(logLines));
                // Tests bind plain HTTP, but anti-forgery with SecurePolicy=Always refuses non-https
                // requests. Marking each request https makes the app behave as it does behind the dev
                // certificate. Pass forceHttpsScheme=false to test the real http→https redirect.
                if (forceHttpsScheme)
                    b.Services.AddTransient<Microsoft.AspNetCore.Hosting.IStartupFilter, ForceHttpsSchemeFilter>();
                configure?.Invoke(b);
            });

        await app.StartAsync();

        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
        };

        var client = new HttpClient(handler)
        {
            BaseAddress = new Uri(app.Urls.First()),
        };

        return new AdminHost(app, client, logLines, dbName);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request)
    {
        // Add cookies from jar
        if (_cookieJar.Count > 0)
        {
            var cookieHeader = string.Join("; ", _cookieJar.Select(kv => $"{kv.Key}={kv.Value}"));
            request.Headers.TryAddWithoutValidation("Cookie", cookieHeader);
        }

        var response = await _client.SendAsync(request);
        UpdateCookieJar(response);
        return response;
    }

    private void UpdateCookieJar(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var setCookieHeaders))
            return;

        foreach (var header in setCookieHeaders)
        {
            var parts = header.Split(';');
            if (parts.Length == 0) continue;

            var nameValue = parts[0].Trim();
            var eqIdx = nameValue.IndexOf('=');
            if (eqIdx < 0) continue;

            var name = nameValue[..eqIdx];
            var value = nameValue[(eqIdx + 1)..];

            bool expired = false;
            foreach (var attr in parts.Skip(1))
            {
                var a = attr.Trim();
                if (a.StartsWith("max-age=", StringComparison.OrdinalIgnoreCase))
                {
                    if (int.TryParse(a[8..], out var maxAge) && maxAge <= 0)
                        expired = true;
                }
            }

            if (expired || string.IsNullOrEmpty(value))
                _cookieJar.Remove(name);
            else
                _cookieJar[name] = value;
        }
    }

    public async Task<HttpResponseMessage> GetAsync(string path)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        return await SendAsync(request);
    }

    public async Task<HttpResponseMessage> PostFormAsync(string path, Dictionary<string, string> form)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new FormUrlEncodedContent(form),
        };
        return await SendAsync(request);
    }

    /// <summary>
    /// Reads a page at <paramref name="path"/> and returns the value of the first hidden
    /// __RequestVerificationToken input.
    /// </summary>
    public async Task<string> TokenAsync(string path)
    {
        var resp = await GetAsync(path);
        var html = await resp.Content.ReadAsStringAsync();
        return ExtractToken(html)
            ?? throw new InvalidOperationException($"No __RequestVerificationToken found in {path}");
    }

    private static string? ExtractToken(string html)
    {
        const string marker = "name=\"__RequestVerificationToken\" value=\"";
        var idx = html.IndexOf(marker, StringComparison.Ordinal);
        if (idx < 0) return null;
        idx += marker.Length;
        var end = html.IndexOf('"', idx);
        if (end < 0) return null;
        return html[idx..end];
    }

    public string? SessionCookie => _cookieJar.TryGetValue("__Host-gallery", out var v) ? v : null;

    /// <summary>
    /// Drives the full sign-in flow: GET /admin/signin → POST /admin/signin → POST /signin-oidc.
    /// Returns the final /signin-oidc callback response.
    /// </summary>
    public async Task<HttpResponseMessage> SignInAsync(string sub)
    {
        var (state, nonce) = await StartSignInAsync();
        var code = FakeGoogle.Code(sub, nonce);
        var oidcResp = await PostFormAsync("/signin-oidc", new Dictionary<string, string>
        {
            ["code"] = code,
            ["state"] = state,
        });
        return oidcResp;
    }

    /// <summary>
    /// Drives the flow up to the authorize redirect and returns (state, nonce).
    /// </summary>
    public async Task<(string state, string nonce)> StartSignInAsync()
    {
        var signinResp = await GetAsync("/admin/signin");
        var signinHtml = await signinResp.Content.ReadAsStringAsync();
        var csrfToken = ExtractToken(signinHtml)
            ?? throw new InvalidOperationException("No CSRF token on sign-in page");

        var challengeResp = await PostFormAsync("/admin/signin", new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = csrfToken,
        });

        var location = challengeResp.Headers.Location?.ToString()
            ?? throw new InvalidOperationException("No redirect from POST /admin/signin");

        var uri = new Uri(location);
        var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
        var state = query["state"] ?? throw new InvalidOperationException("No state in authorize redirect");
        var nonce = query["nonce"] ?? throw new InvalidOperationException("No nonce in authorize redirect");

        return (state, nonce);
    }

    /// <summary>
    /// Mints a session cookie using the app's own TicketDataFormat.
    /// </summary>
    public string MintSession(string sub, DateTimeOffset issued, DateTimeOffset expires)
    {
        var options = _app.Services
            .GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get("owner");

        var identity = new System.Security.Claims.ClaimsIdentity(
            [new System.Security.Claims.Claim("sub", sub)],
            "owner", "sub", null);
        var principal = new System.Security.Claims.ClaimsPrincipal(identity);
        var ticket = new Microsoft.AspNetCore.Authentication.AuthenticationTicket(
            principal,
            new Microsoft.AspNetCore.Authentication.AuthenticationProperties
            {
                IssuedUtc = issued,
                ExpiresUtc = expires,
                IsPersistent = true,
            },
            "owner");

        return options.TicketDataFormat.Protect(ticket);
    }

    public void SetSessionCookie(string value)
    {
        _cookieJar["__Host-gallery"] = value;
    }

    /// <summary>Forgets every cookie, as a new browser would.</summary>
    public void ClearCookies() => _cookieJar.Clear();

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _app.StopAsync();
        if (_app is IAsyncDisposable d) await d.DisposeAsync();
    }
}

/// <summary>Thread-safe logger provider that captures formatted log messages.</summary>
internal sealed class CaptureLoggerProvider(List<string> lines) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new CaptureLogger(lines);
    public void Dispose() { }
}

internal sealed class CaptureLogger(List<string> lines) : ILogger
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        var msg = formatter(state, exception);
        if (exception is not null)
            msg += $"\n  EXCEPTION: {exception.GetType().Name}: {exception.Message}";
        lock (lines) lines.Add(msg);
    }
}

/// <summary>
/// IStartupFilter that prepends a middleware setting Request.Scheme to "https".
/// Enables antiforgery with SecurePolicy=Always to work over plain HTTP in tests.
/// </summary>
internal sealed class ForceHttpsSchemeFilter : Microsoft.AspNetCore.Hosting.IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
    {
        return app =>
        {
            app.Use(async (ctx, nextDelegate) =>
            {
                ctx.Request.Scheme = "https";
                await nextDelegate(ctx);
            });
            next(app);
        };
    }
}
