using System.Net;
using System.Net.Sockets;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Gallery.Web.Storage;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Playwright;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Gallery.Web.Tests.Fixtures;

[CollectionDefinition("upload-browser")]
public class UploadBrowserCollection : ICollectionFixture<UploadBrowserFixture> { }

/// <summary>
/// 008 T044: the upload page, its script, its worker and the vendored encoders in a real browser
/// (research R-14, references/browser-verification.md). Starts, once per "upload-browser"
/// collection:
/// - its own Mongo container (a <see cref="MongoFixture"/>) and its own S3Proxy container with the
///   buckets <c>pub</c>/<c>priv</c>, configured like <see cref="S3ProxyFixture"/> (not shared);
/// - <see cref="FakeGoogle"/>, so the app registers OIDC exactly as in the other admin hosts;
/// - headless Google Chrome via <c>Channel = "chrome"</c>.
/// Each test gets its own app instance and database through <see cref="NewSiteAsync"/>, so one
/// test's open batch can never show up in another's page.
///
/// T041 (probe P9, decisions rows 35 and 63): S3Proxy answers 403 to every CORS preflight, so
/// THIS fixture's Chrome alone runs with <c>--disable-web-security</c>. FR-044 is then proven by
/// recording the PUT request headers, not by a CORS refusal. Playwright always launches Chrome with
/// its own temporary profile (<c>--user-data-dir</c> in a temp dir, removed on close), which is
/// what <c>--disable-web-security</c> needs; Playwright refuses an explicit <c>--user-data-dir</c>
/// argument, so the fixture does not pass one.
///
/// Cookie: the app is served on <c>http://localhost:{port}</c> with the request scheme forced to
/// https (as <see cref="AdminHost"/> does). Chrome treats localhost as a secure context and keeps
/// the Secure <c>__Host-gallery</c> and <c>__Host-af</c> cookies there, so no dev certificate is
/// needed; the fixture guard test fails first if that ever stops being true.
/// </summary>
public sealed class UploadBrowserFixture : IAsyncLifetime
{
    public const string Owner = "owner-1";
    public const string SessionCookie = "__Host-gallery";

    private const string S3Image =
        "andrewgaul/s3proxy@sha256:87662b2a5afcdfa5f478a1c61650bae4c87bbd15f6ff2823236bcfd66ef7fe04";

    private readonly MongoFixture _mongo = new();
    private readonly FakeGoogle _google = new();
    private IContainer? _s3;
    private IPlaywright? _playwright;

    public IBrowser Browser { get; private set; } = default!;
    public StorageOptions Storage { get; private set; } = default!;

    /// <summary>The storage origin the page PUTs to, e.g. <c>http://localhost:49153</c>.</summary>
    public string S3Origin => Storage.ServiceUrl!;

    public async Task InitializeAsync()
    {
        await _mongo.InitializeAsync();
        await _google.InitializeAsync();

        _s3 = new ContainerBuilder(S3Image)
            .WithPortBinding(80, true)
            .WithEnvironment("S3PROXY_AUTHORIZATION", "aws-v2-or-v4")
            .WithEnvironment("S3PROXY_IDENTITY", "local")
            .WithEnvironment("S3PROXY_CREDENTIAL", "localsecret")
            .WithEnvironment("JCLOUDS_PROVIDER", "transient")
            .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r =>
                r.ForPort(80).ForStatusCode(HttpStatusCode.Forbidden)))
            .Build();
        await _s3.StartAsync();

        Storage = new StorageOptions
        {
            ServiceUrl = $"http://localhost:{_s3.GetMappedPublicPort(80)}",
            AccessKeyId = "local",
            SecretAccessKey = "localsecret",
            PublicBucket = "pub",
            PrivateBucket = "priv",
            ImageBaseUrl = "https://img.test",
        };
        using (var client = new S3ClientFactory(Storage).CreateClient())
        {
            await client.PutBucketAsync("pub");
            await client.PutBucketAsync("priv");
        }

        _playwright = await Playwright.CreateAsync();
        Browser = await _playwright.Chromium.LaunchAsync(new()
        {
            Channel = "chrome",
            Headless = true,
            Args = ["--disable-features=Prerender2", "--disable-web-security"],
        });
    }

    public async Task DisposeAsync()
    {
        if (Browser is not null) await Browser.DisposeAsync();
        _playwright?.Dispose();
        if (_s3 is not null) await _s3.DisposeAsync();
        await _google.DisposeAsync();
        await _mongo.DisposeAsync();
    }

    /// <summary>
    /// Starts the real app (AppBuilder.Build, Development, the app's content root so the scoped CSS
    /// and wwwroot resolve) on a fresh database whose site document names <see cref="Owner"/> as
    /// the claimed owner, and mints that owner's session cookie with the app's own ticket format.
    /// </summary>
    public async Task<UploadSite> NewSiteAsync(Dictionary<string, string?>? extraOverrides = null)
    {
        var db = _mongo.NewDatabase();
        await db.GetCollection<BsonDocument>("site").InsertOneAsync(new BsonDocument
        {
            ["_id"] = "site",
            ["claimedSubject"] = Owner,
            ["claimedAt"] = DateTime.UtcNow,
            ["claimedIp"] = "::1",
            ["homeOrder"] = new BsonArray(),
            ["homeCards"] = new BsonArray(),
            ["updatedAt"] = DateTime.UtcNow,
        });

        var overrides = new Dictionary<string, string?>
        {
            ["Mongo:ConnectionString"] = _mongo.ConnectionString,
            ["Mongo:Database"] = db.DatabaseNamespace.DatabaseName,
            ["Storage:ImageBaseUrl"] = Storage.ImageBaseUrl,
            ["Storage:ServiceUrl"] = Storage.ServiceUrl,
            ["Storage:AccessKeyId"] = Storage.AccessKeyId,
            ["Storage:SecretAccessKey"] = Storage.SecretAccessKey,
            ["Storage:PublicBucket"] = Storage.PublicBucket,
            ["Storage:PrivateBucket"] = Storage.PrivateBucket,
            ["Site:OutputCache"] = "false",
            ["Authentication:Google:ClientId"] = "test-client",
            ["Authentication:Google:ClientSecret"] = "test-secret",
            ["Authentication:Google:Authority"] = _google.Authority,
            ["Authentication:Google:RequireHttpsMetadata"] = "false",
            ["Logging:LogLevel:Default"] = "Information",
            ["Logging:LogLevel:Microsoft"] = "Warning",
        };
        if (extraOverrides is not null)
            foreach (var (k, v) in extraOverrides)
                overrides[k] = v;

        var port = FreePort();
        var logLines = new List<string>();
        var app = Gallery.Web.AppBuilder.Build(
            ["--urls", $"http://127.0.0.1:{port}", "--environment", "Development",
             "--contentRoot", Path.Combine(Render.RepoRoot, "src", "Gallery.Web"),
             "--applicationName", "Gallery.Web"],
            overrides,
            b =>
            {
                b.Logging.AddProvider(new CaptureLoggerProvider(logLines));
                b.Services.AddTransient<Microsoft.AspNetCore.Hosting.IStartupFilter, ForceHttpsSchemeFilter>();
            });
        await app.StartAsync();

        try
        {
            var session = MintSession(app, Owner);
            return new UploadSite(this, app, $"http://localhost:{port}", db, logLines, session);
        }
        catch
        {
            await app.DisposeAsync();
            throw;
        }
    }

    /// <summary>One implementation, shared with AdminHost (decisions row 64).</summary>
    private static string MintSession(WebApplication app, string sub)
    {
        var now = DateTimeOffset.UtcNow;
        return AdminHost.MintSession(app, sub, now.AddMinutes(-1), now.AddDays(7));
    }

    private static int FreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }
}

/// <summary>One app instance + database for one test, with the owner session ready to inject.</summary>
public sealed class UploadSite(
    UploadBrowserFixture fixture, WebApplication app, string baseUrl, IMongoDatabase db,
    List<string> logLines, string session) : IAsyncDisposable
{
    private readonly List<IBrowserContext> _contexts = [];

    public string BaseUrl { get; } = baseUrl;
    public IMongoDatabase Db { get; } = db;
    public IServiceProvider Services => app.Services;

    /// <summary>A snapshot of the captured log messages (all categories).</summary>
    public IReadOnlyList<string> LogLines
    {
        get { lock (logLines) return logLines.ToList(); }
    }

    /// <summary>
    /// A new browser context (own cache and cookies) with the owner session injected as the
    /// <c>__Host-gallery</c> cookie: Secure, HttpOnly, Lax, path /, host-only.
    /// </summary>
    public async Task<IBrowserContext> NewContextAsync(bool javaScript = true)
    {
        var ctx = await fixture.Browser.NewContextAsync(new()
        {
            ViewportSize = new() { Width = 1440, Height = 900 },
            JavaScriptEnabled = javaScript,
        });
        _contexts.Add(ctx);
        await ctx.AddCookiesAsync(
        [
            new Microsoft.Playwright.Cookie
            {
                Name = UploadBrowserFixture.SessionCookie,
                Value = session,
                // CDP refuses a Secure cookie set against an http:// URL ("Invalid cookie fields"),
                // so it is set for the https origin of the same host and port. Chrome treats
                // localhost as a secure context and sends Secure cookies to http://localhost.
                Url = BaseUrl.Replace("http://", "https://", StringComparison.Ordinal) + "/",
                Secure = true,
                HttpOnly = true,
                SameSite = SameSiteAttribute.Lax,
            },
        ]);
        return ctx;
    }

    /// <summary>A new context and page, navigated to <c>/admin/upload</c> and loaded.</summary>
    public async Task<IPage> OpenUploadAsync(bool javaScript = true, Func<IPage, Task>? beforeNavigate = null)
    {
        var ctx = await NewContextAsync(javaScript);
        var page = await ctx.NewPageAsync();
        if (beforeNavigate is not null) await beforeNavigate(page);
        var resp = await page.GotoAsync(BaseUrl + "/admin/upload", new() { WaitUntil = WaitUntilState.NetworkIdle });
        if (resp is null || resp.Status != 200 || !page.Url.EndsWith("/admin/upload", StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"GET /admin/upload answered {resp?.Status} at {page.Url}; the owner session cookie was not accepted.");
        return page;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var c in _contexts) await c.DisposeAsync();
        await app.StopAsync();
        await app.DisposeAsync();
    }
}
