using System.Net;
using System.Net.Sockets;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Gallery.Web.Data;
using Gallery.Web.Edge;
using Gallery.Web.Library;
using Gallery.Web.Storage;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using MongoDB.Driver;

namespace Gallery.Web.Tests.Fixtures;

[CollectionDefinition("library-browser")]
public class LibraryBrowserCollection : ICollectionFixture<LibraryBrowserFixture> { }

/// <summary>
/// 009 T050: the library, gallery list and builder pages with library.js and reorder.js in a real
/// browser (references/browser-verification.md), modelled on <see cref="UploadBrowserFixture"/>.
/// Starts, once per "library-browser" collection:
/// - its own Mongo container (a <see cref="MongoFixture"/>) and its own S3Proxy container with the
///   buckets <c>pub</c>/<c>priv</c> (not shared with the "s3" collection);
/// - <see cref="FakeGoogle"/>, so the app registers OIDC exactly as the other admin hosts do;
/// - headless Google Chrome via <c>Channel = "chrome"</c>, prerender off. Web security stays ON
///   (no cross-origin call is made from these pages).
/// Each test gets its own app instance and database through <see cref="NewSiteAsync"/>: the real
/// app via <c>AppBuilder.Build</c> with <c>--environment Development --contentRoot src/Gallery.Web
/// --applicationName Gallery.Web</c> (so the scoped CSS and wwwroot resolve), <c>Site:OutputCache=false</c>,
/// one <see cref="RecordingEdgeProvider"/> as the edge, and captured <c>LogLines</c>.
///
/// Cookie: the app is served on <c>http://localhost:{port}</c> with the request scheme forced to
/// https. The owner session (minted with <see cref="AdminHost.MintSession(WebApplication, string, DateTimeOffset, DateTimeOffset)"/>)
/// is set through CDP against <c>https://localhost:{port}</c>, because CDP refuses a Secure cookie
/// for an http URL; Chrome treats localhost as a secure context and sends it to the http origin.
/// </summary>
public sealed class LibraryBrowserFixture : IAsyncLifetime
{
    public const string Owner = "owner-1";
    public const string SessionCookie = "__Host-gallery";
    public const string ImageBaseUrl = "https://img.test";

    private const string S3Image =
        "andrewgaul/s3proxy@sha256:87662b2a5afcdfa5f478a1c61650bae4c87bbd15f6ff2823236bcfd66ef7fe04";

    private readonly MongoFixture _mongo = new();
    private readonly FakeGoogle _google = new();
    private IContainer? _s3;
    private IPlaywright? _playwright;

    public IBrowser Browser { get; private set; } = default!;
    public StorageOptions Storage { get; private set; } = default!;

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
            ImageBaseUrl = ImageBaseUrl,
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
            Args = ["--disable-features=Prerender2"],
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
    /// Starts the real app on a fresh database, claims the site for <see cref="Owner"/> through the
    /// app's own <c>OwnerStore</c>, and mints that owner's session cookie.
    /// </summary>
    public async Task<LibrarySite> NewSiteAsync(Dictionary<string, string?>? extraOverrides = null)
    {
        var db = _mongo.NewDatabase();
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
            ["Edge:SiteBaseUrl"] = "https://site.test",
            ["Edge:PurgePages"] = "true",
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
        var recorder = new RecordingEdgeProvider();
        var app = Gallery.Web.AppBuilder.Build(
            ["--urls", $"http://127.0.0.1:{port}", "--environment", "Development",
             "--contentRoot", Path.Combine(Render.RepoRoot, "src", "Gallery.Web"),
             "--applicationName", "Gallery.Web"],
            overrides,
            b =>
            {
                b.Logging.AddProvider(new CaptureLoggerProvider(logLines));
                b.Services.AddTransient<Microsoft.AspNetCore.Hosting.IStartupFilter, ForceHttpsSchemeFilter>();
                b.Services.RemoveAll<IEdgeProvider>();
                b.Services.AddSingleton<IEdgeProvider>(recorder);
            });
        await app.StartAsync();

        try
        {
            using (var scope = app.Services.CreateScope())
            {
                var claim = await scope.ServiceProvider.GetRequiredService<OwnerStore>()
                    .TryClaimAsync(Owner, "127.0.0.1", DateTime.UtcNow);
                if (claim != ClaimResult.Won)
                    throw new InvalidOperationException($"Claiming the site as {Owner} answered {claim}.");
            }
            var now = DateTimeOffset.UtcNow;
            var session = AdminHost.MintSession(app, Owner, now.AddMinutes(-1), now.AddDays(7));
            return new LibrarySite(this, app, $"http://localhost:{port}", db, logLines, recorder, session);
        }
        catch
        {
            await app.DisposeAsync();
            throw;
        }
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
public sealed class LibrarySite(
    LibraryBrowserFixture fixture, WebApplication app, string baseUrl, IMongoDatabase db,
    List<string> logLines, RecordingEdgeProvider recorder, string session) : IAsyncDisposable
{
    private readonly List<IBrowserContext> _contexts = [];

    public string BaseUrl { get; } = baseUrl;
    public IMongoDatabase Db { get; } = db;
    public IServiceProvider Services => app.Services;
    public RecordingEdgeProvider Recorder { get; } = recorder;

    public IMongoCollection<ImageDoc> Images => Db.GetCollection<ImageDoc>("images");
    public IMongoCollection<GalleryDoc> Galleries => Db.GetCollection<GalleryDoc>("galleries");

    /// <summary>A snapshot of the captured log messages (all categories).</summary>
    public IReadOnlyList<string> LogLines
    {
        get { lock (logLines) return logLines.ToList(); }
    }

    // ── data, written the way the owner's own actions write it ───────────────────────────

    /// <summary>Inserts <paramref name="n"/> upload-shaped images (newest last) and returns their ids.</summary>
    public async Task<List<string>> ImagesAsync(int n, string titlePrefix = "Photo")
    {
        var ids = new List<string>();
        var start = DateTime.UtcNow.AddMinutes(-n);
        for (var i = 0; i < n; i++)
        {
            var image = SeedShapes.UploadedImage(createdAt: start.AddSeconds(i), title: $"{titlePrefix} {i + 1}");
            await Images.InsertOneAsync(image);
            ids.Add(image.Id);
        }
        return ids;
    }

    /// <summary>Creates a gallery through <see cref="GalleryService"/>, adds <paramref name="ids"/>, optionally publishes.</summary>
    public async Task<string> GalleryAsync(string title, IReadOnlyList<string> ids, bool publish = false)
    {
        using var scope = app.Services.CreateScope();
        var galleries = scope.ServiceProvider.GetRequiredService<GalleryService>();
        var created = await galleries.CreateAsync(title);
        if (!created.IsOk) throw new InvalidOperationException($"Create '{title}': {created.Reason}");
        var gid = created.GalleryId!;
        if (ids.Count > 0)
        {
            var added = await galleries.AddAsync(gid, ids);
            if (!added.IsOk) throw new InvalidOperationException($"Add to '{title}': {added.Reason}");
        }
        if (publish)
        {
            var published = await galleries.PublishAsync(gid, confirmed: true);
            if (!published.IsOk) throw new InvalidOperationException($"Publish '{title}': {published.Reason}");
        }
        return gid;
    }

    public Task<GalleryDoc> GalleryDocAsync(string gid) => Galleries.Find(g => g.Id == gid).FirstAsync();

    public async Task<List<string>> HomeOrderAsync()
    {
        var site = await Db.GetCollection<MongoDB.Bson.BsonDocument>("site")
            .Find(new MongoDB.Bson.BsonDocument("_id", "site")).FirstAsync();
        return site["homeOrder"].AsBsonArray.Select(v => v.AsString).ToList();
    }

    // ── browser ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// A new browser context (own cache and cookies) with the owner session injected as the
    /// <c>__Host-gallery</c> cookie: Secure, HttpOnly, Lax, path /, host-only. Image requests to
    /// the (unresolvable) image host are answered 404 at once so loads never wait on DNS.
    /// </summary>
    public async Task<IBrowserContext> NewContextAsync(int width = 1440, int height = 900, bool owner = true)
    {
        var ctx = await fixture.Browser.NewContextAsync(new()
        {
            ViewportSize = new() { Width = width, Height = height },
        });
        _contexts.Add(ctx);
        await ctx.RouteAsync(LibraryBrowserFixture.ImageBaseUrl + "/**", r => r.FulfillAsync(new() { Status = 404 }));
        if (owner)
            await ctx.AddCookiesAsync(
            [
                new Microsoft.Playwright.Cookie
                {
                    Name = LibraryBrowserFixture.SessionCookie,
                    Value = session,
                    // CDP refuses a Secure cookie set against an http:// URL, so it is set for the
                    // https origin of the same host and port (see the class summary).
                    Url = BaseUrl.Replace("http://", "https://", StringComparison.Ordinal) + "/",
                    Secure = true,
                    HttpOnly = true,
                    SameSite = SameSiteAttribute.Lax,
                },
            ]);
        return ctx;
    }

    /// <summary>
    /// A new context and page, navigated to <paramref name="path"/> and loaded. Throws when the
    /// owner session was not accepted (an owner page then redirects to sign-in).
    /// </summary>
    public async Task<IPage> OpenAsync(string path, int width = 1440, int height = 900, bool owner = true)
    {
        var ctx = await NewContextAsync(width, height, owner);
        var page = await ctx.NewPageAsync();
        var resp = await page.GotoAsync(BaseUrl + path, new() { WaitUntil = WaitUntilState.NetworkIdle });
        var expected = path.Split('?')[0];
        if (resp is null || resp.Status != 200 || !new Uri(page.Url).AbsolutePath.Equals(expected, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"GET {path} answered {resp?.Status} at {page.Url}; the owner session cookie was not accepted.");
        return page;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var c in _contexts) await c.DisposeAsync();
        await app.StopAsync();
        await app.DisposeAsync();
    }
}
