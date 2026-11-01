using Gallery.Web.Data;
using Gallery.Web.Tests.Fixtures;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using MongoDB.Driver;
using System.Net;

namespace Gallery.Web.Tests;

/// <summary>
/// T023 [US4] [SAFETY]: Admin access-control sweep — invariants I-1, I-4, I-5, I-6,
/// no-config host behaviour, HTTP→HTTPS redirect, and FR-022 log lines.
/// </summary>
[Collection("mongo")]
public class AdminAccessTests(MongoFixture mongo) : IAsyncLifetime
{
    // Shared FakeGoogle (one per class); each test creates its own AdminHost.
    private FakeGoogle _google = null!;

    public async Task InitializeAsync()
    {
        _google = new FakeGoogle();
        await _google.InitializeAsync();
    }

    public async Task DisposeAsync() => await _google.DisposeAsync();

    // ── I-1: GET admin paths without session → 302 /admin/signin ─────────────

    [Theory]
    [InlineData("/admin")]
    [InlineData("/admin/upload")]
    [InlineData("/admin/library")]
    [InlineData("/admin/galleries")]
    [InlineData("/admin/x/y")]
    public async Task I1_NoSession_GET_admin_redirects_to_signin_no_admin_markup(string path)
    {
        var host = await AdminHost.CreateAsync(mongo, _google);
        await using var _ = host;

        var resp = await host.GetAsync(path);
        Assert.Equal(HttpStatusCode.Found, resp.StatusCode);
        var loc = resp.Headers.Location?.ToString() ?? "";
        Assert.StartsWith("/admin/signin", loc);

        var body = await resp.Content.ReadAsStringAsync();
        Assert.DoesNotContain("owner-bar", body);
        Assert.DoesNotContain("admin-empty", body);
    }

    // ── I-4: without session, non-GET/HEAD → 401 ─────────────────────────────

    [Theory]
    [InlineData("POST",   "/admin")]
    [InlineData("POST",   "/admin/signout")]
    [InlineData("POST",   "/admin/x")]
    [InlineData("PUT",    "/admin")]
    [InlineData("PUT",    "/admin/x")]
    [InlineData("DELETE", "/admin")]
    [InlineData("DELETE", "/admin/x")]
    public async Task I4_NoSession_NonGet_returns_401(string method, string path)
    {
        var host = await AdminHost.CreateAsync(mongo, _google);
        await using var _ = host;

        using var client = RawClientFor(host);
        var req = new HttpRequestMessage(new HttpMethod(method), path);
        var resp = await client.SendAsync(req);
        // Auth middleware OnRedirectToLogin: 401 for non-GET/HEAD
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    // ── I-4: with owner session + no token → 400 (antiforgery filter) ────────

    [Theory]
    [InlineData("/admin")]
    [InlineData("/admin/signout")]
    [InlineData("/admin/x")]
    public async Task I4_WithSession_NoToken_POST_returns_400(string path)
    {
        var host = await CreateHostWithClaimedOwner();
        await using var _ = host;

        var resp = await host.PostFormAsync(path, new Dictionary<string, string>());
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    // ── I-4: with session + valid token on /admin/x → 404 ────────────────────

    [Fact]
    public async Task I4_WithSessionAndToken_POST_adminX_returns_404()
    {
        var host = await CreateHostWithClaimedOwner();
        await using var _ = host;

        var token = await host.TokenAsync("/admin");
        var resp = await host.PostFormAsync("/admin/x", new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
        });
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    // ── I-5: every admin and /signin-oidc response has no-store + noindex ────

    [Theory]
    [InlineData("/admin")]
    [InlineData("/admin/upload")]
    [InlineData("/admin/x/y")]
    [InlineData("/admin/signin")]
    public async Task I5_GET_admin_responses_carry_NoStore_and_Noindex(string path)
    {
        var host = await AdminHost.CreateAsync(mongo, _google);
        await using var _ = host;

        var resp = await host.GetAsync(path);
        AssertNoStore(resp);
        AssertNoindex(resp);
    }

    [Fact]
    public async Task I5_NoSession_POST_admin_401_carries_NoStore_and_Noindex()
    {
        var host = await AdminHost.CreateAsync(mongo, _google);
        await using var _ = host;

        using var client = RawClientFor(host);
        var resp = await client.SendAsync(new HttpRequestMessage(HttpMethod.Post, "/admin"));
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
        AssertNoStore(resp);
        AssertNoindex(resp);
    }

    [Fact]
    public async Task I5_WithSession_NoToken_400_carries_NoStore_and_Noindex()
    {
        var host = await CreateHostWithClaimedOwner();
        await using var _ = host;

        var resp = await host.PostFormAsync("/admin/x", new Dictionary<string, string>());
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        AssertNoStore(resp);
        AssertNoindex(resp);
    }

    [Fact]
    public async Task I5_SigninOidc_callback_carries_NoStore_and_Noindex()
    {
        var host = await AdminHost.CreateAsync(mongo, _google);
        await using var _ = host;

        // Full sign-in drives POST /signin-oidc; the response is the OIDC callback response.
        var oidcResp = await host.SignInAsync("owner-1");
        AssertNoStore(oidcResp);
        AssertNoindex(oidcResp);
    }

    // ── Owner with session GET /admin/upload and /admin/library → 200; unbuilt routes 404 ──
    // Requirement change (008 T035, decisions row 38): this test asserted 404 for /admin/upload
    // while no upload page existed; 008 flipped it to 200. Requirement change (009 T036, 009
    // decisions row 56): 009 builds the library, so it flips to 200 too and the "not built" guard
    // moves to /admin/not-built. No other assertion in this file changed.

    [Fact]
    public async Task Owner_GetAdminUpload_and_library_return_200()
    {
        var host = await CreateHostWithClaimedOwner();
        await using var _ = host;

        var resp = await host.GetAsync("/admin/upload");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var library = await host.GetAsync("/admin/library");
        Assert.Equal(HttpStatusCode.OK, library.StatusCode);

        var notBuilt = await host.GetAsync("/admin/not-built");
        Assert.Equal(HttpStatusCode.NotFound, notBuilt.StatusCode);
    }

    // ── FR-022: LogLines has http.request entries with status and duration ────

    [Fact]
    public async Task FR022_LogLines_has_httpRequest_for_GET_admin_and_POST_signinOidc()
    {
        var host = await AdminHost.CreateAsync(mongo, _google);
        await using var _ = host;

        // Trigger GET /admin (no session → 302)
        await host.GetAsync("/admin");

        // Trigger POST /signin-oidc via full sign-in flow
        await host.SignInAsync("owner-1");

        Assert.True(
            host.LogLines.Any(l => l.Contains("http.request") && l.Contains("GET") && l.Contains("admin")),
            "Expected a log line with 'http.request GET ... admin ...'");

        Assert.True(
            host.LogLines.Any(l => l.Contains("http.request") && l.Contains("POST") && l.Contains("signin-oidc")),
            "Expected a log line with 'http.request POST ... signin-oidc ...'");
    }

    // ── I-6 / SC-007: public pages byte-identical with / without owner cookie ─

    [Fact]
    public async Task I6_PublicPages_ByteIdentical_WithAndWithout_OwnerCookie()
    {
        var db = mongo.NewDatabase();
        await SeedPublicContentAsync(db);
        var dbName = db.DatabaseNamespace.DatabaseName;
        var dbOverrides = new Dictionary<string, string?> { ["Mongo:Database"] = dbName };

        // Host A: signs in as owner (session cookie in jar)
        var hostA = await AdminHost.CreateAsync(mongo, _google, dbOverrides);
        await using var dispA = hostA;
        await hostA.SignInAsync("owner-1");
        Assert.NotNull(hostA.SessionCookie); // verify signed in

        // Host B: no session (fresh host, same DB)
        var hostB = await AdminHost.CreateAsync(mongo, _google, dbOverrides);
        await using var dispB = hostB;

        string[] publicPaths =
        [
            "/",
            $"/g/{SeedGalleryId}/autumn-walk",
            $"/g/{SeedGalleryId}/autumn-walk/{SeedImgId1}",
        ];

        foreach (var path in publicPaths)
        {
            // Capture log count before these public requests
            int beforeCount = hostA.LogLines.Count;

            var respA = await hostA.GetAsync(path);
            var bodyA = await respA.Content.ReadAsStringAsync();

            var respB = await hostB.GetAsync(path);
            var bodyB = await respB.Content.ReadAsStringAsync();

            // Bodies must be byte-identical
            Assert.Equal(bodyA, bodyB);

            // Neither response should set a session cookie
            Assert.False(respA.Headers.Contains("Set-Cookie"),
                $"Set-Cookie present for {path} WITH owner cookie (I-6)");
            Assert.False(respB.Headers.Contains("Set-Cookie"),
                $"Set-Cookie present for {path} WITHOUT owner cookie (I-6)");

            // No db.site.owner.read should have been logged for these public requests
            var newLogs = hostA.LogLines.Skip(beforeCount).ToList();
            Assert.False(
                newLogs.Any(l => l.Contains("db.site.owner.read")),
                $"db.site.owner.read logged for public path '{path}' (I-6)");
        }
    }

    [Fact]
    public async Task I6_NoConfigHost_PublicPages_ByteIdentical_With_MintedSession()
    {
        // R-03 probe part 3: with only the cookie scheme registered (no OIDC),
        // SuppressAutoDefaultScheme prevents it from authenticating public requests.
        var db = mongo.NewDatabase();
        await SeedPublicContentAsync(db);
        var dbName = db.DatabaseNamespace.DatabaseName;
        var dbOverrides = new Dictionary<string, string?> { ["Mongo:Database"] = dbName };

        // No-config: no Google credentials → only cookie scheme registered
        var hostWithCookie = await AdminHost.CreateAsync(mongo, fakeGoogle: null, extraOverrides: dbOverrides);
        await using var dispA = hostWithCookie;

        // Mint a session and put it in the jar
        var sessionVal = hostWithCookie.MintSession(
            "owner-1",
            DateTimeOffset.UtcNow.AddMinutes(-1),
            DateTimeOffset.UtcNow.AddDays(7));
        hostWithCookie.SetSessionCookie(sessionVal);

        // Second host: same DB, no cookie
        var hostNoCookie = await AdminHost.CreateAsync(mongo, fakeGoogle: null, extraOverrides: dbOverrides);
        await using var dispB = hostNoCookie;

        string[] publicPaths =
        [
            "/",
            $"/g/{SeedGalleryId}/autumn-walk",
            $"/g/{SeedGalleryId}/autumn-walk/{SeedImgId1}",
        ];

        foreach (var path in publicPaths)
        {
            int beforeCount = hostWithCookie.LogLines.Count;

            var respA = await hostWithCookie.GetAsync(path);
            var bodyA = await respA.Content.ReadAsStringAsync();

            var respB = await hostNoCookie.GetAsync(path);
            var bodyB = await respB.Content.ReadAsStringAsync();

            Assert.Equal(bodyA, bodyB);

            Assert.False(respA.Headers.Contains("Set-Cookie"),
                $"Set-Cookie present for {path} WITH minted session on no-config host (I-6)");
            Assert.False(respB.Headers.Contains("Set-Cookie"),
                $"Set-Cookie present for {path} WITHOUT cookie on no-config host (I-6)");

            var newLogs = hostWithCookie.LogLines.Skip(beforeCount).ToList();
            Assert.False(
                newLogs.Any(l => l.Contains("db.site.owner.read")),
                $"db.site.owner.read logged for public path '{path}' on no-config host (I-6)");
        }
    }

    // ── No-config host behaviour ──────────────────────────────────────────────

    [Fact]
    public async Task NoConfig_PublicRoot_returns_200()
    {
        var host = await AdminHost.CreateAsync(mongo, fakeGoogle: null);
        await using var _ = host;

        var resp = await host.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Fact]
    public async Task NoConfig_GetAdminSignin_shows_NotSetUp_notice()
    {
        var host = await AdminHost.CreateAsync(mongo, fakeGoogle: null);
        await using var _ = host;

        var resp = await host.GetAsync("/admin/signin");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadAsStringAsync();
        // Razor HTML-encodes ' as &#x27;; check the part without the apostrophe.
        Assert.True(body.Contains("Sign-in isn") && body.Contains("t set up on this server"),
            "Expected 'Sign-in isn't set up on this server' notice in body");
    }

    [Fact]
    public async Task NoConfig_PostAdminSignin_WithToken_returns_503()
    {
        var host = await AdminHost.CreateAsync(mongo, fakeGoogle: null);
        await using var _ = host;

        var token = await host.TokenAsync("/admin/signin");
        var resp = await host.PostFormAsync("/admin/signin", new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
        });
        Assert.Equal(HttpStatusCode.ServiceUnavailable, resp.StatusCode);
    }

    // ── HTTP → HTTPS redirect (R-09) ──────────────────────────────────────────

    [Fact]
    public async Task HttpToHttps_admin_redirects_but_public_does_not()
    {
        var db = mongo.NewDatabase();
        var overrides = new Dictionary<string, string?>
        {
            ["Mongo:ConnectionString"] = mongo.ConnectionString,
            ["Mongo:Database"]        = db.DatabaseNamespace.DatabaseName,
            ["Storage:ImageBaseUrl"]  = "https://img.test",
            ["Storage:ServiceUrl"]    = "http://localhost:19999",
            ["Storage:AccessKeyId"]   = "local",
            ["Storage:SecretAccessKey"] = "localsecret",
            ["Storage:PublicBucket"]  = "pub",
            ["Storage:PrivateBucket"] = "priv",
            ["Site:OutputCache"]      = "false",
        };

        WebApplication app;
        try
        {
            app = Gallery.Web.AppBuilder.Build(
                ["--urls", "http://127.0.0.1:0;https://127.0.0.1:0"],
                overrides);
            await app.StartAsync();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                "BLOCKER: Could not start app with dual http/https bindings. " +
                "Ensure the dev cert exists: `dotnet dev-certs https --trust`. " +
                $"Inner: {ex.GetType().Name}: {ex.Message}", ex);
        }

        try
        {
            var server = app.Services.GetRequiredService<IServer>();
            var feat = server.Features.Get<IServerAddressesFeature>()!;
            var httpBase = feat.Addresses.First(a => a.StartsWith("http://"));

            var handler = new HttpClientHandler
            {
                AllowAutoRedirect = false,
                ServerCertificateCustomValidationCallback = (_, _, _, _) => true,
            };
            using var client = new HttpClient(handler) { BaseAddress = new Uri(httpBase) };

            // GET /admin over HTTP → 307/308 to HTTPS
            var adminResp = await client.GetAsync("/admin");
            Assert.True(
                adminResp.StatusCode == HttpStatusCode.TemporaryRedirect ||
                adminResp.StatusCode == HttpStatusCode.PermanentRedirect,
                $"Expected 307/308 for GET /admin over HTTP but got {(int)adminResp.StatusCode}");
            var loc = adminResp.Headers.Location?.ToString() ?? "";
            Assert.StartsWith("https://", loc);

            // GET / over HTTP → 200 (no HTTPS redirect for public routes)
            var publicResp = await client.GetAsync("/");
            Assert.Equal(HttpStatusCode.OK, publicResp.StatusCode);
        }
        finally
        {
            await app.StopAsync();
            if (app is IAsyncDisposable d) await d.DisposeAsync();
        }
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Creates a host whose DB already has claimedSubject set and whose
    /// cookie jar holds a valid minted session for that owner.
    /// </summary>
    private async Task<AdminHost> CreateHostWithClaimedOwner(string sub = "owner-1")
    {
        var db = mongo.NewDatabase();

        // Seed site doc with a claimed owner so OnValidatePrincipal accepts the session.
        await db.GetCollection<BsonDocument>("site").InsertOneAsync(new BsonDocument
        {
            ["_id"]            = "site",
            ["claimedSubject"] = sub,
            ["claimedAt"]      = DateTime.UtcNow,
            ["claimedIp"]      = "::1",
            ["homeOrder"]      = new BsonArray(),
            ["homeCards"]      = new BsonArray(),
            ["updatedAt"]      = DateTime.UtcNow,
        });

        var host = await AdminHost.CreateAsync(mongo, _google,
            extraOverrides: new Dictionary<string, string?>
            {
                ["Mongo:Database"] = db.DatabaseNamespace.DatabaseName,
            });

        var sessionVal = host.MintSession(
            sub,
            DateTimeOffset.UtcNow.AddMinutes(-1),
            DateTimeOffset.UtcNow.AddDays(7));
        host.SetSessionCookie(sessionVal);
        return host;
    }

    /// <summary>Creates a raw HttpClient pointing at the given host's base URL.</summary>
    private static HttpClient RawClientFor(AdminHost host)
    {
        var server = host.Services.GetRequiredService<IServer>();
        var feat = server.Features.Get<IServerAddressesFeature>()!;
        var baseUrl = feat.Addresses.First();
        return new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false })
        {
            BaseAddress = new Uri(baseUrl),
        };
    }

    private static void AssertNoStore(HttpResponseMessage resp)
    {
        var found = resp.Headers.TryGetValues("Cache-Control", out var vals) &&
                    vals.Any(v => v.Contains("no-store", StringComparison.OrdinalIgnoreCase));
        var actual = resp.Headers.TryGetValues("Cache-Control", out var v2)
            ? string.Join(", ", v2)
            : "(none)";
        Assert.True(found, $"Expected Cache-Control: no-store but got: {actual}");
    }

    private static void AssertNoindex(HttpResponseMessage resp)
    {
        var found = resp.Headers.TryGetValues("X-Robots-Tag", out var vals) &&
                    vals.Any(v => v.Contains("noindex", StringComparison.OrdinalIgnoreCase));
        Assert.True(found, "Expected X-Robots-Tag: noindex");
    }

    // ── Seed helpers (same shape as PublicRoutesTests) ────────────────────────

    private const string SeedGalleryId = "aaccaaccaaccaaccaaccaa2222";
    private const string SeedImgId1    = "aabbccddeeffaabbccddeeff22";
    private const string SeedImgId2    = "bbccddaabbccddaabbccddaa22";

    private static DerivativeDoc MakeDeriv(int w) => new()
    {
        W = w, Avif = $"h{w}.avif", Webp = $"h{w}.webp", BytesAvif = 100, BytesWebp = 80,
    };

    private static PlaceholderDoc Placeholder() => new()
    {
        Color = "#aabbcc", Preview = "data:image/webp;base64,AAAA",
    };

    private static CoverImageDoc MakeCover(string imgId) => new()
    {
        ImageId = imgId,
        Width = 1280, Height = 960,
        Placeholder = Placeholder(),
        Derivatives = [MakeDeriv(320), MakeDeriv(640), MakeDeriv(1280)],
    };

    private static GalleryCardDoc MakeCard(string galId, string slug, string title, string? coverId = null) => new()
    {
        GalleryId = galId,
        Slug = slug, Title = title,
        ImageCount = coverId is null ? 0 : 1,
        Cover = coverId is null ? null : MakeCover(coverId),
    };

    private static ImageDoc MakeImageDoc(string id, string title) => new()
    {
        Id = id, SeedKey = id, SourceHash = id,
        Title = title,
        Width = 1280, Height = 960,
        Placeholder = Placeholder(),
        Derivatives = [MakeDeriv(320), MakeDeriv(640), MakeDeriv(1280)],
        GalleryIds = [],
        CreatedAt = DateTime.UtcNow,
    };

    private async Task SeedPublicContentAsync(IMongoDatabase db)
    {
        var images    = db.GetCollection<ImageDoc>("images");
        var galleries = db.GetCollection<GalleryDoc>("galleries");
        var site      = db.GetCollection<SiteDoc>("site");

        await images.InsertManyAsync([
            MakeImageDoc(SeedImgId1, "Seed Image One"),
            MakeImageDoc(SeedImgId2, "Seed Image Two"),
        ]);

        await galleries.InsertOneAsync(new GalleryDoc
        {
            Id          = SeedGalleryId,
            SeedKey     = "seed-pub1",
            Slug        = "autumn-walk",
            Title       = "Autumn Walk",
            Description = "Seed gallery for access tests",
            Visibility  = "public",
            Items       = [SeedImgId1, SeedImgId2],
            Card        = MakeCard(SeedGalleryId, "autumn-walk", "Autumn Walk", SeedImgId1),
            UpdatedAt   = DateTime.UtcNow,
        });

        await site.InsertOneAsync(new SiteDoc
        {
            Id        = "site",
            HomeOrder = [SeedGalleryId],
            HomeCards = [MakeCard(SeedGalleryId, "autumn-walk", "Autumn Walk", SeedImgId1)],
            UpdatedAt = DateTime.UtcNow,
        });
    }
}
