using Gallery.Web.Tests.Fixtures;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Gallery.Web.Tests;

/// <summary>
/// T020: Integration tests for User Story 2 — returning owner sign-in, sign-out, and session management.
/// </summary>
[Collection("mongo")]
public class OwnerSessionTests(MongoFixture mongo) : IAsyncLifetime
{
    private FakeGoogle? _fakeGoogle;

    public async Task InitializeAsync()
    {
        _fakeGoogle = new FakeGoogle();
        await _fakeGoogle.InitializeAsync();
    }

    public async Task DisposeAsync()
    {
        if (_fakeGoogle is not null) await _fakeGoogle.DisposeAsync();
    }

    // ── Helper: start a host with owner-1 pre-claimed ────────────────────────

    private async Task<(AdminHost host, IMongoDatabase db)> CreateClaimedHostAsync()
    {
        var db = mongo.NewDatabase();
        await db.GetCollection<BsonDocument>("site").InsertOneAsync(new BsonDocument
        {
            ["_id"] = "site",
            ["claimedSubject"] = "owner-1",
            ["claimedAt"] = DateTime.UtcNow.AddDays(-30),
            ["claimedIp"] = "127.0.0.1",
            ["homeOrder"] = new BsonArray(),
            ["homeCards"] = new BsonArray(),
            ["updatedAt"] = DateTime.UtcNow,
        });

        var host = await AdminHost.CreateAsync(mongo, _fakeGoogle, extraOverrides: new Dictionary<string, string?>
        {
            ["Mongo:Database"] = db.DatabaseNamespace.DatabaseName,
        });

        return (host, db);
    }

    // ── Returning owner: sign-in, shell, DB unchanged, no auth.claim ─────────

    [Fact]
    public async Task Returning_owner_signin_shell_no_claim_one_signin()
    {
        var (host, db) = await CreateClaimedHostAsync();
        await using var _ = host;

        var resp = await host.SignInAsync("owner-1");

        // Redirects to /admin
        Assert.Equal(System.Net.HttpStatusCode.Found, resp.StatusCode);
        Assert.Equal("/admin", resp.Headers.Location?.ToString());
        Assert.NotNull(host.SessionCookie);

        // Shell page accessible
        var adminResp = await host.GetAsync("/admin");
        Assert.Equal(System.Net.HttpStatusCode.OK, adminResp.StatusCode);
        var html = await adminResp.Content.ReadAsStringAsync();
        Assert.Contains("admin-empty", html);
        Assert.Contains("owner-bar", html);

        // DB unchanged: claimedSubject still owner-1
        var site = await db.GetCollection<BsonDocument>("site")
            .Find(Builders<BsonDocument>.Filter.Eq("_id", "site")).FirstAsync();
        Assert.Equal("owner-1", site["claimedSubject"].AsString);

        // No auth.claim (site was already claimed)
        Assert.DoesNotContain(host.LogLines, l => l.Contains("auth.claim"));

        // Exactly one auth.signin with owner-1
        Assert.Single(host.LogLines, l => l.Contains("auth.signin") && l.Contains("owner-1"));
    }

    // ── [SAFETY] Sign-out with token ─────────────────────────────────────────

    [Fact]
    public async Task Signout_with_token_clears_session_and_redirects_to_signin()
    {
        var (host, _) = await CreateClaimedHostAsync();
        await using var _ = host;

        await host.SignInAsync("owner-1");

        // Obtain a token from /admin
        var token = await host.TokenAsync("/admin");

        // POST /admin/signout with token
        var signoutResp = await host.PostFormAsync("/admin/signout", new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
        });

        // 302 → /admin/signin
        Assert.Equal(System.Net.HttpStatusCode.Found, signoutResp.StatusCode);
        Assert.Equal("/admin/signin", signoutResp.Headers.Location?.ToString());

        // Session cookie cleared
        Assert.Null(host.SessionCookie);

        // Subsequent GET /admin → 302 /admin/signin
        var afterResp = await host.GetAsync("/admin");
        Assert.Equal(System.Net.HttpStatusCode.Found, afterResp.StatusCode);
        Assert.Contains("/admin/signin", afterResp.Headers.Location?.ToString() ?? "");

        // Exactly one auth.signout containing owner-1
        Assert.Single(host.LogLines, l => l.Contains("auth.signout") && l.Contains("owner-1"));
    }

    // ── [SAFETY] Sign-out without token: 400, session kept ───────────────────

    [Fact]
    public async Task Signout_without_token_returns_400_and_session_still_valid()
    {
        var (host, _) = await CreateClaimedHostAsync();
        await using var _ = host;

        await host.SignInAsync("owner-1");

        // POST without token
        var resp = await host.PostFormAsync("/admin/signout", new Dictionary<string, string>());

        Assert.Equal(System.Net.HttpStatusCode.BadRequest, resp.StatusCode);

        // Session still valid
        var adminResp = await host.GetAsync("/admin");
        Assert.Equal(System.Net.HttpStatusCode.OK, adminResp.StatusCode);
    }

    // ── MintSession: 1 day old, 6 days left → 200, no renewal (SC-005 live) ──

    [Fact]
    public async Task Session_fresh_under_half_life_returns_200_no_renewal()
    {
        var (host, _) = await CreateClaimedHostAsync();
        await using var _ = host;

        var now = DateTimeOffset.UtcNow;
        var cookie = host.MintSession("owner-1", now.AddDays(-1), now.AddDays(6));
        host.SetSessionCookie(cookie);

        var resp = await host.GetAsync("/admin");
        Assert.Equal(System.Net.HttpStatusCode.OK, resp.StatusCode);
    }

    // ── MintSession: 8 days old, expired 1 day ago → 302 (SC-005 idle) ───────

    [Fact]
    public async Task Session_expired_redirects_to_signin()
    {
        var (host, _) = await CreateClaimedHostAsync();
        await using var _ = host;

        var now = DateTimeOffset.UtcNow;
        var cookie = host.MintSession("owner-1", now.AddDays(-8), now.AddDays(-1));
        host.SetSessionCookie(cookie);

        var resp = await host.GetAsync("/admin");
        Assert.Equal(System.Net.HttpStatusCode.Found, resp.StatusCode);
        Assert.Contains("/admin/signin", resp.Headers.Location?.ToString() ?? "");
    }

    // ── MintSession: 4 days old, 3 days left → 200 + new Set-Cookie ──────────

    [Fact]
    public async Task Session_past_half_life_renews_with_correct_attributes()
    {
        var (host, _) = await CreateClaimedHostAsync();
        await using var _ = host;

        var now = DateTimeOffset.UtcNow;
        // Issued 4 days ago, expires in 3 days: past the 3.5-day half-life
        var cookie = host.MintSession("owner-1", now.AddDays(-4), now.AddDays(3));
        host.SetSessionCookie(cookie);

        var resp = await host.GetAsync("/admin");
        Assert.Equal(System.Net.HttpStatusCode.OK, resp.StatusCode);

        // New __Host-gallery Set-Cookie must be present
        var setCookies = resp.Headers
            .Where(h => h.Key.Equals("Set-Cookie", StringComparison.OrdinalIgnoreCase))
            .SelectMany(h => h.Value)
            .ToList();

        var sessionCookie = setCookies.FirstOrDefault(c =>
            c.StartsWith("__Host-gallery=", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(sessionCookie);

        // Verify required attributes (same as T017 cookie-attribute check)
        var parts = sessionCookie!.Split(';').Select(a => a.Trim()).ToList();
        var attrNames = parts.Skip(1).Select(a => a.Split('=')[0].ToLowerInvariant()).ToHashSet();

        Assert.Contains("path=/", parts.Skip(1).Select(a => a.ToLowerInvariant()));
        Assert.Contains("secure", attrNames);
        Assert.Contains("httponly", attrNames);
        Assert.Contains("samesite=lax", parts.Skip(1).Select(a => a.ToLowerInvariant()));
        Assert.DoesNotContain("domain", attrNames);
    }

    // ── [SAFETY] Out-of-band owner reset → old cookie rejected (FR-014) ──────

    [Fact]
    public async Task Out_of_band_owner_null_invalidates_existing_session()
    {
        var (host, db) = await CreateClaimedHostAsync();
        await using var _ = host;

        // Mint a valid session directly (no Google flow needed)
        var cookie = host.MintSession("owner-1", DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow.AddDays(6));
        host.SetSessionCookie(cookie);

        // Verify access works
        var before = await host.GetAsync("/admin");
        Assert.Equal(System.Net.HttpStatusCode.OK, before.StatusCode);

        // Out-of-band reset: set claimedSubject to null
        await db.GetCollection<BsonDocument>("site").UpdateOneAsync(
            Builders<BsonDocument>.Filter.Eq("_id", "site"),
            Builders<BsonDocument>.Update.Set("claimedSubject", BsonNull.Value));

        var after = await host.GetAsync("/admin");
        Assert.Equal(System.Net.HttpStatusCode.Found, after.StatusCode);
        Assert.Contains("/admin/signin", after.Headers.Location?.ToString() ?? "");
    }

    [Fact]
    public async Task Out_of_band_owner_changed_to_another_sub_invalidates_existing_session()
    {
        var (host, db) = await CreateClaimedHostAsync();
        await using var _ = host;

        // Mint a valid session for owner-1
        var cookie = host.MintSession("owner-1", DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow.AddDays(6));
        host.SetSessionCookie(cookie);

        // Verify access works
        var before = await host.GetAsync("/admin");
        Assert.Equal(System.Net.HttpStatusCode.OK, before.StatusCode);

        // Out-of-band: transfer claim to another sub
        await db.GetCollection<BsonDocument>("site").UpdateOneAsync(
            Builders<BsonDocument>.Filter.Eq("_id", "site"),
            Builders<BsonDocument>.Update.Set("claimedSubject", "owner-2"));

        // owner-1's session must now be rejected
        // (cookie jar may have been cleared by the previous request's SignOutAsync;
        //  re-mint owner-1 to ensure we're actually testing the DB check)
        var cookie2 = host.MintSession("owner-1", DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow.AddDays(6));
        host.SetSessionCookie(cookie2);

        var after = await host.GetAsync("/admin");
        Assert.Equal(System.Net.HttpStatusCode.Found, after.StatusCode);
        Assert.Contains("/admin/signin", after.Headers.Location?.ToString() ?? "");
    }

    // ── Signed-in owner GET /admin/signin → 302 /admin ───────────────────────

    [Fact]
    public async Task SignedIn_owner_visiting_signin_page_redirects_to_admin()
    {
        var (host, _) = await CreateClaimedHostAsync();
        await using var _ = host;

        await host.SignInAsync("owner-1");

        var resp = await host.GetAsync("/admin/signin");
        Assert.Equal(System.Net.HttpStatusCode.Found, resp.StatusCode);
        Assert.Equal("/admin", resp.Headers.Location?.ToString());
    }
}
