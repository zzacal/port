using Gallery.Web.Data;
using Gallery.Web.Storage;
using Gallery.Web.Tests.Fixtures;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Gallery.Web.Tests;

/// <summary>
/// T017: Integration tests for owner claim flow (US1).
/// Tests sign-in page states, the full claim flow, cookie attributes,
/// security guards, and re-seeding preservation.
/// </summary>
[Collection("mongo")]
public class OwnerClaimTests(MongoFixture mongo) : IAsyncLifetime
{
    private AdminHost? _host;
    private FakeGoogle? _fakeGoogle;

    public async Task InitializeAsync()
    {
        _fakeGoogle = new FakeGoogle();
        await _fakeGoogle.InitializeAsync();
        _host = await AdminHost.CreateAsync(mongo, _fakeGoogle);
    }

    public async Task DisposeAsync()
    {
        if (_host is not null) await _host.DisposeAsync();
        if (_fakeGoogle is not null) await _fakeGoogle.DisposeAsync();
    }

    // ── Sign-in page states ───────────────────────────────────────────────────

    [Fact]
    public async Task Unclaimed_no_doc_shows_claim_page()
    {
        // No site doc = unclaimed
        var resp = await _host!.GetAsync("/admin/signin");
        var body = await resp.Content.ReadAsStringAsync();
        Assert.Equal(System.Net.HttpStatusCode.OK, resp.StatusCode);
        Assert.Contains("Claim this site", body);
    }

    [Fact]
    public async Task Unclaimed_null_subject_shows_claim_page()
    {
        // Insert site doc with claimedSubject: null
        var db = mongo.NewDatabase();
        await db.GetCollection<BsonDocument>("site").InsertOneAsync(new BsonDocument
        {
            ["_id"] = "site",
            ["claimedSubject"] = BsonNull.Value,
            ["homeOrder"] = new BsonArray(),
            ["homeCards"] = new BsonArray(),
            ["updatedAt"] = DateTime.UtcNow,
        });

        var host = await AdminHost.CreateAsync(mongo, _fakeGoogle, new Dictionary<string, string?>
        {
            ["Mongo:Database"] = db.DatabaseNamespace.DatabaseName,
        });
        await using var _ = host;

        var resp = await host.GetAsync("/admin/signin");
        var html = await resp.Content.ReadAsStringAsync();
        Assert.Contains("Claim this site", html);
    }

    // ── [SAFETY] Full claim flow ──────────────────────────────────────────────

    [Fact]
    public async Task SignIn_claims_site_sets_session_cookie_and_logs_claim()
    {
        var resp = await _host!.SignInAsync("owner-1");

        // Should redirect to /admin
        Assert.Equal(System.Net.HttpStatusCode.Found, resp.StatusCode);
        var location = resp.Headers.Location?.ToString();
        Assert.Equal("/admin", location);

        // Session cookie should be set
        Assert.NotNull(_host.SessionCookie);

        // GET /admin should return 200 with admin-empty and owner-bar
        var adminResp = await _host.GetAsync("/admin");
        Assert.Equal(System.Net.HttpStatusCode.OK, adminResp.StatusCode);
        var adminHtml = await adminResp.Content.ReadAsStringAsync();
        Assert.Contains("admin-empty", adminHtml);
        Assert.Contains("owner-bar", adminHtml);

        // The stored owner record: sub, a recent claim time, and the client IP.
        var site = await Site(_host).Find(Builders<BsonDocument>.Filter.Eq("_id", "site")).FirstAsync();
        Assert.Equal("owner-1", site["claimedSubject"].AsString);
        Assert.InRange(site["claimedAt"].ToUniversalTime(), DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow.AddSeconds(5));
        Assert.False(string.IsNullOrWhiteSpace(site["claimedIp"].AsString));

        var claimLogs = _host.LogLines.Where(l => l.Contains("auth.claim") && l.Contains("owner-1")).ToList();
        Assert.Single(claimLogs);

        var timingLogs = _host.LogLines.Where(l => l.Contains("op.timing db.site.claim") && l.Contains("won")).ToList();
        Assert.Single(timingLogs);
    }

    // ── [SAFETY] Cookie attributes ────────────────────────────────────────────

    [Fact]
    public async Task Session_cookie_has_correct_attributes()
    {
        // The callback response of a real claim carries the session cookie.
        var oidcResp = await _host!.SignInAsync("owner-1");

        // Check that the __Host-gallery cookie has required attributes
        var setCookies = oidcResp.Headers
            .Where(h => h.Key.Equals("Set-Cookie", StringComparison.OrdinalIgnoreCase))
            .SelectMany(h => h.Value)
            .ToList();

        var sessionCookie = setCookies.FirstOrDefault(c =>
            c.StartsWith("__Host-gallery=", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(sessionCookie);

        var attrs = sessionCookie!.Split(';').Select(a => a.Trim()).ToList();
        var attrNames = attrs.Skip(1).Select(a => a.Split('=')[0].ToLowerInvariant()).ToHashSet();

        Assert.Contains("path=/", attrs.Skip(1).Select(a => a.ToLowerInvariant()));
        Assert.Contains("secure", attrNames);
        Assert.Contains("httponly", attrNames);
        Assert.Contains("samesite=lax", attrs.Skip(1).Select(a => a.ToLowerInvariant()));
        Assert.DoesNotContain("domain", attrNames);
    }

    [Fact]
    public async Task Antiforgery_cookie_has_correct_attributes()
    {
        var resp = await _host!.GetAsync("/admin/signin");

        var setCookies = resp.Headers
            .Where(h => h.Key.Equals("Set-Cookie", StringComparison.OrdinalIgnoreCase))
            .SelectMany(h => h.Value)
            .ToList();

        var afCookie = setCookies.FirstOrDefault(c =>
            c.StartsWith("__Host-af=", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(afCookie);

        var attrs = afCookie!.Split(';').Select(a => a.Trim()).ToList();
        var attrNames = attrs.Skip(1).Select(a => a.Split('=')[0].ToLowerInvariant()).ToHashSet();

        Assert.Contains("path=/", attrs.Skip(1).Select(a => a.ToLowerInvariant()));
        Assert.Contains("secure", attrNames);
        Assert.Contains("httponly", attrNames);
        Assert.DoesNotContain("domain", attrNames);
    }

    // ── Security guards ───────────────────────────────────────────────────────

    [Theory]
    [InlineData("?r=refused")]
    [InlineData("?r=failed")]
    [InlineData("")]
    public async Task Sign_in_pages_pass_security_guards(string query)
    {
        var resp = await _host!.GetAsync("/admin/signin" + query);
        var html = await resp.Content.ReadAsStringAsync();
        Guards.AssertNoScript(html);
        Guards.AssertNoInlineStyle(html);
    }

    [Fact]
    public async Task Shell_page_passes_security_guards()
    {
        await _host!.SignInAsync("owner-1");
        var resp = await _host.GetAsync("/admin");
        var html = await resp.Content.ReadAsStringAsync();
        Guards.AssertNoScript(html);
        Guards.AssertNoInlineStyle(html);
    }

    // ── After claim, sign-in page shows SignIn (not Claim) ──────────────────

    [Fact]
    public async Task After_claim_new_client_sees_signin_not_claim()
    {
        await _host!.SignInAsync("owner-1");

        // New host with SAME DB (different HTTP client = different cookie jar = no session)
        var host2 = await AdminHost.CreateAsync(mongo, _fakeGoogle, extraOverrides: new Dictionary<string, string?>
        {
            ["Mongo:Database"] = _host.DatabaseName,
        });
        await using var _ = host2;

        var resp = await host2.GetAsync("/admin/signin");
        var html = await resp.Content.ReadAsStringAsync();
        Assert.Contains("Sign in", html);
        Assert.DoesNotContain("Claim this site", html);
    }

    // ── Cover image on sign-in page ───────────────────────────────────────────

    [Fact]
    public async Task Sign_in_page_shows_has_cover_when_gallery_has_cover()
    {
        // Seed a site doc with a home card that has a cover
        var db = mongo.NewDatabase();
        var siteCol = db.GetCollection<BsonDocument>("site");
        await siteCol.InsertOneAsync(new BsonDocument
        {
            ["_id"] = "site",
            ["homeOrder"] = new BsonArray { "aaaaaaaaaaaaaaaaaaaaaaaa22" },
            ["homeCards"] = new BsonArray
            {
                new BsonDocument
                {
                    ["galleryId"] = "aaaaaaaaaaaaaaaaaaaaaaaa22",
                    ["slug"] = "test-gallery",
                    ["title"] = "Test Gallery",
                    ["imageCount"] = 1,
                    ["cover"] = new BsonDocument
                    {
                        ["imageId"] = "eeeeeeeeeeeeeeeeeeeeeeee22",
                        ["width"] = 1280,
                        ["height"] = 960,
                        ["placeholder"] = new BsonDocument { ["color"] = "#aabbcc", ["preview"] = "data:image/webp;base64,AAAA" },
                        ["derivatives"] = new BsonArray
                        {
                            new BsonDocument { ["w"] = 320, ["avif"] = "hash320.avif", ["webp"] = "hash320.webp", ["bytesAvif"] = 100, ["bytesWebp"] = 80 }
                        }
                    }
                }
            },
            ["updatedAt"] = DateTime.UtcNow,
        });

        var host = await AdminHost.CreateAsync(mongo, _fakeGoogle, new Dictionary<string, string?>
        {
            ["Mongo:Database"] = db.DatabaseNamespace.DatabaseName,
        });
        await using var _ = host;

        var resp = await host.GetAsync("/admin/signin");
        var html = await resp.Content.ReadAsStringAsync();
        Assert.Contains("has-cover", html);
    }

    private IMongoCollection<BsonDocument> Site(AdminHost host) =>
        new MongoClient(mongo.ConnectionString).GetDatabase(host.DatabaseName).GetCollection<BsonDocument>("site");

    [Fact]
    public async Task Sign_in_page_has_no_cover_when_no_gallery_with_image()
    {
        // No site doc → no cover
        var resp = await _host!.GetAsync("/admin/signin");
        var html = await resp.Content.ReadAsStringAsync();
        Assert.DoesNotContain("has-cover", html);
    }
}
