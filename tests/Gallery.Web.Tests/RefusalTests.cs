using Gallery.Web.Tests.Fixtures;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Gallery.Web.Tests;

/// <summary>
/// T022 [SAFETY]: User Story 3 — refusal and failed sign-in tests.
/// Verifies refused and failed callbacks leave no session, no DB mutation,
/// correct log events, and the correct page notice.
/// </summary>
[Collection("mongo")]
public class RefusalTests(MongoFixture mongo) : IAsyncLifetime
{
    private FakeGoogle _fakeGoogle = null!;
    private string _dbName = null!;

    public async Task InitializeAsync()
    {
        _fakeGoogle = new FakeGoogle();
        await _fakeGoogle.InitializeAsync();

        // Establish a claimed DB: sign in as owner-1 so the site is claimed.
        var claimHost = await AdminHost.CreateAsync(mongo, _fakeGoogle);
        _dbName = claimHost.DatabaseName;
        await claimHost.SignInAsync("owner-1");
        await claimHost.DisposeAsync();
    }

    public async Task DisposeAsync()
    {
        await _fakeGoogle.DisposeAsync();
    }

    // ── [SAFETY] Intruder is refused ─────────────────────────────────────────

    [Fact]
    public async Task Refused_intruder_gets_no_session_and_site_doc_unchanged()
    {
        var siteDocBefore = await SnapshotSiteDocAsync();

        await using var host = await NewHostAsync();
        var resp = await host.SignInAsync("intruder");

        // → 302 /admin/signin?r=refused
        Assert.Equal(System.Net.HttpStatusCode.Found, resp.StatusCode);
        Assert.Equal("/admin/signin?r=refused", resp.Headers.Location?.ToString());

        // No non-empty __Host-gallery Set-Cookie on the callback response (I-2)
        AssertNoSessionCookieSet(resp);

        // Site doc byte-identical after refusal (I-3)
        var siteDocAfter = await SnapshotSiteDocAsync();
        Assert.Equal(siteDocBefore, siteDocAfter);

        // Exactly one auth.refused with not_owner
        var refusedLogs = host.LogLines
            .Where(l => l.Contains("auth.refused") && l.Contains("not_owner"))
            .ToList();
        Assert.Single(refusedLogs);

        // No auth.signin logged at all
        var signinLogs = host.LogLines.Where(l => l.Contains("auth.signin")).ToList();
        Assert.Empty(signinLogs);
    }

    [Fact]
    public async Task Refused_page_shows_not_the_owner_notice()
    {
        await using var host = await NewHostAsync();
        await host.SignInAsync("intruder");

        var pageResp = await host.GetAsync("/admin/signin?r=refused");
        var html = await pageResp.Content.ReadAsStringAsync();
        Assert.Contains("Not the owner", html);
    }

    [Fact]
    public async Task Refused_client_cannot_access_admin_shell()
    {
        await using var host = await NewHostAsync();
        await host.SignInAsync("intruder");

        // No session cookie was set, so /admin must redirect to sign-in
        var adminResp = await host.GetAsync("/admin");
        Assert.Equal(System.Net.HttpStatusCode.Found, adminResp.StatusCode);
        Assert.Equal("/admin/signin", adminResp.Headers.Location?.ToString());
    }

    // ── prompt=select_account (FR-009) ───────────────────────────────────────

    [Fact]
    public async Task Post_signin_challenge_includes_prompt_select_account()
    {
        await using var host = await NewHostAsync();

        var pageResp = await host.GetAsync("/admin/signin");
        var html = await pageResp.Content.ReadAsStringAsync();
        var token = ExtractToken(html)
            ?? throw new InvalidOperationException("No CSRF token on sign-in page");

        var challengeResp = await host.PostFormAsync("/admin/signin", new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
        });

        Assert.Equal(System.Net.HttpStatusCode.Found, challengeResp.StatusCode);
        var location = challengeResp.Headers.Location?.ToString() ?? "";
        Assert.Contains("prompt=select_account", location);
    }

    // ── Code "fail" → failed outcome ─────────────────────────────────────────

    [Fact]
    public async Task Code_fail_redirects_to_failed_page_and_no_session()
    {
        var siteDocBefore = await SnapshotSiteDocAsync();

        await using var host = await NewHostAsync();
        var (state, _) = await host.StartSignInAsync();

        // FakeGoogle returns 400 for code="fail"
        var callbackResp = await host.PostFormAsync("/signin-oidc", new Dictionary<string, string>
        {
            ["code"] = "fail",
            ["state"] = state,
        });

        // → 302 /admin/signin?r=failed
        Assert.Equal(System.Net.HttpStatusCode.Found, callbackResp.StatusCode);
        Assert.Equal("/admin/signin?r=failed", callbackResp.Headers.Location?.ToString());

        // No session cookie on the callback response (I-2)
        AssertNoSessionCookieSet(callbackResp);
        Assert.Null(host.SessionCookie);

        // Site doc byte-identical (I-3)
        var siteDocAfter = await SnapshotSiteDocAsync();
        Assert.Equal(siteDocBefore, siteDocAfter);

        // Exactly one auth.failed
        var failedLogs = host.LogLines.Where(l => l.Contains("auth.failed")).ToList();
        Assert.Single(failedLogs);

        // Page shows "Sign-in didn't complete"
        // Razor HTML-encodes the apostrophe as &#x27;, so decode before asserting.
        var pageResp = await host.GetAsync("/admin/signin?r=failed");
        var rawHtml = await pageResp.Content.ReadAsStringAsync();
        var pageText = System.Net.WebUtility.HtmlDecode(rawHtml);
        Assert.Contains("Sign-in didn't complete", pageText);
    }

    // ── Cancel at Google (error=access_denied) ───────────────────────────────

    [Fact]
    public async Task Cancel_at_google_redirects_to_failed_page_and_no_session()
    {
        var siteDocBefore = await SnapshotSiteDocAsync();

        await using var host = await NewHostAsync();
        var (state, _) = await host.StartSignInAsync();

        // Google sends error=access_denied + real state when the user cancels
        var callbackResp = await host.PostFormAsync("/signin-oidc", new Dictionary<string, string>
        {
            ["error"] = "access_denied",
            ["state"] = state,
        });

        // → 302 /admin/signin?r=failed
        Assert.Equal(System.Net.HttpStatusCode.Found, callbackResp.StatusCode);
        Assert.Equal("/admin/signin?r=failed", callbackResp.Headers.Location?.ToString());

        // No session cookie (I-2)
        AssertNoSessionCookieSet(callbackResp);
        Assert.Null(host.SessionCookie);

        // Site doc unchanged (I-3)
        var siteDocAfter = await SnapshotSiteDocAsync();
        Assert.Equal(siteDocBefore, siteDocAfter);

        // Exactly one auth.failed
        var failedLogs = host.LogLines.Where(l => l.Contains("auth.failed")).ToList();
        Assert.Single(failedLogs);
    }

    // ── Tampered state → failed outcome ──────────────────────────────────────

    [Fact]
    public async Task Tampered_state_redirects_to_failed_page()
    {
        await using var host = await NewHostAsync();
        var (_, nonce) = await host.StartSignInAsync();

        var code = FakeGoogle.Code("owner-1", nonce);

        // Replace the real state with garbage so the OIDC middleware rejects it
        var callbackResp = await host.PostFormAsync("/signin-oidc", new Dictionary<string, string>
        {
            ["code"] = code,
            ["state"] = "tampered-state-xyz",
        });

        Assert.Equal(System.Net.HttpStatusCode.Found, callbackResp.StatusCode);
        Assert.Equal("/admin/signin?r=failed", callbackResp.Headers.Location?.ToString());
    }

    // ── Callback replay → failed, no second auth.signin ──────────────────────

    [Fact]
    public async Task Callback_replay_fails_and_no_second_auth_signin()
    {
        await using var host = await NewHostAsync();
        var (state, nonce) = await host.StartSignInAsync();
        var code = FakeGoogle.Code("owner-1", nonce);

        // First callback succeeds: owner-1 is the stored owner → SignedIn
        var first = await host.PostFormAsync("/signin-oidc", new Dictionary<string, string>
        {
            ["code"] = code,
            ["state"] = state,
        });
        Assert.Equal(System.Net.HttpStatusCode.Found, first.StatusCode);
        Assert.Equal("/admin", first.Headers.Location?.ToString());

        // Second callback with the same code+state: correlation cookie was consumed → fails
        var second = await host.PostFormAsync("/signin-oidc", new Dictionary<string, string>
        {
            ["code"] = code,
            ["state"] = state,
        });
        Assert.Equal(System.Net.HttpStatusCode.Found, second.StatusCode);
        Assert.Equal("/admin/signin?r=failed", second.Headers.Location?.ToString());

        // auth.signin appeared exactly once (the first successful callback only)
        var signinLogs = host.LogLines.Where(l => l.Contains("auth.signin")).ToList();
        Assert.Single(signinLogs);
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    /// <summary>Creates a fresh host pointing to the already-claimed DB.</summary>
    private Task<AdminHost> NewHostAsync() =>
        AdminHost.CreateAsync(mongo, _fakeGoogle, new Dictionary<string, string?>
        {
            ["Mongo:Database"] = _dbName,
        });

    /// <summary>Returns the site document serialized to JSON for byte-exact comparison.</summary>
    private async Task<string> SnapshotSiteDocAsync()
    {
        var doc = await SiteCollection()
            .Find(Builders<BsonDocument>.Filter.Eq("_id", "site"))
            .FirstOrDefaultAsync();
        return doc?.ToJson() ?? string.Empty;
    }

    private IMongoCollection<BsonDocument> SiteCollection() =>
        new MongoClient(mongo.ConnectionString)
            .GetDatabase(_dbName)
            .GetCollection<BsonDocument>("site");

    /// <summary>
    /// Asserts that the response does NOT contain a non-empty __Host-gallery Set-Cookie (I-2).
    /// </summary>
    private static void AssertNoSessionCookieSet(HttpResponseMessage response)
    {
        var setCookies = response.Headers
            .Where(h => h.Key.Equals("Set-Cookie", StringComparison.OrdinalIgnoreCase))
            .SelectMany(h => h.Value)
            .ToList();

        foreach (var cookie in setCookies)
        {
            if (!cookie.StartsWith("__Host-gallery=", StringComparison.OrdinalIgnoreCase))
                continue;
            var value = cookie["__Host-gallery=".Length..].Split(';')[0];
            Assert.True(
                string.IsNullOrEmpty(value),
                $"Expected no non-empty __Host-gallery cookie on this response, but found: {cookie}");
        }
    }

    private static string? ExtractToken(string html)
    {
        const string marker = "name=\"__RequestVerificationToken\" value=\"";
        var idx = html.IndexOf(marker, StringComparison.Ordinal);
        if (idx < 0) return null;
        idx += marker.Length;
        var end = html.IndexOf('"', idx);
        return end < 0 ? null : html[idx..end];
    }
}
