using Gallery.Web.Tests.Fixtures;
using Gallery.Web.Ui;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Gallery.Web.Tests;

/// <summary>
/// T025: /ui catalogue is owner-only (US5). Tests run against a Production-environment host.
/// Without a session, /ui routes redirect to /admin/signin.
/// With a minted owner session, /ui/ returns 200 with all catalogue links.
/// </summary>
[Collection("mongo")]
public class UiOwnerOnlyTests(MongoFixture mongo) : IAsyncLifetime
{
    private AdminHost? _host;

    public async Task InitializeAsync()
    {
        _host = await AdminHost.CreateAsync(
            mongo,
            fakeGoogle: null,
            configure: b => b.Environment.EnvironmentName = "Production");
    }

    public async Task DisposeAsync()
    {
        if (_host is not null) await _host.DisposeAsync();
    }

    // ── Without a session ────────────────────────────────────────────────────

    [Fact]
    public async Task NoSession_UiIndex_Redirects_ToSignIn()
    {
        var resp = await _host!.GetAsync("/ui/");

        Assert.Equal(System.Net.HttpStatusCode.Redirect, resp.StatusCode);
        Assert.Equal("/admin/signin", resp.Headers.Location?.OriginalString);

        var body = await resp.Content.ReadAsStringAsync();
        Assert.DoesNotContain("Specimen", body);
        // No catalogue links in the redirect response
        Assert.DoesNotContain("href=\"/ui/", body);
    }

    [Fact]
    public async Task NoSession_UiFirstSlug_Redirects_ToSignIn()
    {
        var firstSlug = UiCatalog.Entries[0].Slug;
        var resp = await _host!.GetAsync($"/ui/{firstSlug}");

        Assert.Equal(System.Net.HttpStatusCode.Redirect, resp.StatusCode);
        Assert.Equal("/admin/signin", resp.Headers.Location?.OriginalString);

        var body = await resp.Content.ReadAsStringAsync();
        Assert.DoesNotContain("Specimen", body);
        Assert.DoesNotContain("href=\"/ui/", body);
    }

    // ── With a minted owner session ──────────────────────────────────────────

    [Fact]
    public async Task WithOwnerSession_UiIndex_Returns200_WithAllCatalogueLinks()
    {
        const string sub = "owner-ui-test";

        // Seed the site doc so the owner check (OnValidatePrincipal) passes.
        var client = new MongoClient(mongo.ConnectionString);
        var db = client.GetDatabase(_host!.DatabaseName);
        await db.GetCollection<BsonDocument>("site").InsertOneAsync(new BsonDocument
        {
            ["_id"] = "site",
            ["claimedSubject"] = sub,
            ["claimedAt"] = DateTime.UtcNow,
            ["claimedIp"] = "127.0.0.1",
        });

        var now = DateTimeOffset.UtcNow;
        var cookieValue = _host.MintSession(sub, issued: now, expires: now.AddDays(7));
        _host.SetSessionCookie(cookieValue);

        var resp = await _host.GetAsync("/ui/");

        Assert.Equal(System.Net.HttpStatusCode.OK, resp.StatusCode);

        var body = await resp.Content.ReadAsStringAsync();

        // Count href="/ui/..." occurrences. The UiLayout nav adds one extra
        // back-link (href="/ui/"), so subtract 1 to get only the entry links.
        var expectedCount = UiCatalog.Entries.Count;
        var linkPrefix = "href=\"/ui/";
        var rawCount = CountOccurrences(body, linkPrefix);
        // One occurrence is the UiLayout back-nav link (/ui/); the rest are catalogue entries.
        var actualCount = rawCount - 1;

        Assert.Equal(expectedCount, actualCount);
    }

    // ── Helper ───────────────────────────────────────────────────────────────

    private static int CountOccurrences(string text, string pattern)
    {
        int count = 0;
        int idx = 0;
        while ((idx = text.IndexOf(pattern, idx, StringComparison.Ordinal)) >= 0)
        {
            count++;
            idx += pattern.Length;
        }
        return count;
    }
}
