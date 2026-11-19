using System.Net;
using Gallery.Web.Admin.Auth;
using Gallery.Web.Tests.Fixtures;

namespace Gallery.Web.Tests;

/// <summary>
/// 018 T014 [US2] [SAFETY] (constitution I.7, contracts/http.md H7 "mapped ONLY when enabled"):
/// on every host that is not a development host with a long-enough password, the local form is
/// absent, <c>POST /admin/signin/local</c> answers exactly like an unmapped admin address, no session
/// is issued and nothing is claimed; <c>auth.local.state</c> names why. A production-target host with
/// the key refuses to build.
/// </summary>
[Collection("mongo")]
public class LocalSignInGuardTests(MongoFixture mongo) : IAsyncLifetime
{
    private const string Password = LocalSignInTests.Password; // 20 characters
    private const string Short = "Lq7-dev-signin-"; // 15 characters

    private FakeGoogle _google = null!;

    public async Task InitializeAsync()
    {
        _google = new FakeGoogle();
        await _google.InitializeAsync();
    }

    public async Task DisposeAsync() => await _google.DisposeAsync();

    /// <summary>The five disabled shapes; returns the host and the expected <c>auth.local.state</c> line (or null).</summary>
    private async Task<(AdminHost Host, string? StateLine)> HostAsync(string shape)
    {
        Assert.Equal(20, Password.Length);
        Assert.Equal(15, Short.Length);
        var pw = new Dictionary<string, string?> { [LocalSignIn.PasswordKey] = Password };
        return shape switch
        {
            "a_production" => (await AdminHost.CreateAsync(mongo, _google, pw),
                "auth.local.state not_development_host"),
            "b_database" => (await AdminHost.CreateAsync(mongo, _google,
                    new(pw) { ["Mongo:Database"] = "lsg_" + Guid.NewGuid().ToString("N") }, devHost: true),
                "auth.local.state not_development_host"),
            "c_bucket" => (await AdminHost.CreateAsync(mongo, _google,
                    new(pw) { ["Storage:PublicBucket"] = "pub" }, devHost: true),
                "auth.local.state not_development_host"),
            "d_no_key" => (await AdminHost.CreateAsync(mongo, _google, devHost: true), null),
            "e_short" => (await AdminHost.CreateAsync(mongo, _google,
                    new() { [LocalSignIn.PasswordKey] = Short }, devHost: true),
                "auth.local.state too_short"),
            _ => throw new ArgumentOutOfRangeException(nameof(shape)),
        };
    }

    [Theory]
    [InlineData("a_production")]
    [InlineData("b_database")]
    [InlineData("c_bucket")]
    [InlineData("d_no_key")]
    [InlineData("e_short")]
    public async Task Disabled_host_has_no_local_form_no_route_no_session(string shape)
    {
        var (host, stateLine) = await HostAsync(shape);
        await using var _ = host;

        // The GET page: no local form.
        var page = await host.GetAsync("/admin/signin");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        var html = await page.Content.ReadAsStringAsync();
        LocalSignInHtml.AssertNoLocalForm(html);

        // POST with a valid token and the right password: exactly like an unmapped admin address.
        var token = await host.TokenAsync("/admin/signin");
        var form = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["password"] = Password,
        };
        var local = await host.PostFormAsync("/admin/signin/local", form);
        var unmapped = await host.PostFormAsync("/admin/signin/nonexistent", form);
        Assert.Equal(HttpStatusCode.Unauthorized, local.StatusCode);
        Assert.Equal(unmapped.StatusCode, local.StatusCode);
        Assert.Equal(await unmapped.Content.ReadAsStringAsync(), await local.Content.ReadAsStringAsync());
        Assert.Equal(string.Empty, await local.Content.ReadAsStringAsync());

        var getLocal = await host.GetAsync("/admin/signin/local");
        var getUnmapped = await host.GetAsync("/admin/signin/nonexistent");
        Assert.Equal(HttpStatusCode.Found, getLocal.StatusCode);
        Assert.Equal("/admin/signin", getLocal.Headers.Location?.ToString());
        Assert.Equal(getUnmapped.StatusCode, getLocal.StatusCode);
        Assert.Equal(getUnmapped.Headers.Location, getLocal.Headers.Location);

        foreach (var r in new[] { page, local, unmapped, getLocal, getUnmapped })
            Assert.False(LocalSignInHtml.SetsOwnerCookie(r));
        Assert.Null(host.SessionCookie);
        Assert.Null(await LocalSignInTests.OwnerAsync(host));

        List<string> lines;
        lock (host.LogLines) lines = host.LogLines.Where(l => l.StartsWith("auth.local.", StringComparison.Ordinal)).ToList();
        if (stateLine is null)
            Assert.Empty(lines);
        else
            Assert.Equal(stateLine, Assert.Single(lines));
    }

    [Fact]
    public void Production_target_host_with_the_key_refuses_to_build_naming_key_not_value()
    {
        var keys = Path.Combine(Path.GetTempPath(), "gallery-lsg-" + Guid.NewGuid().ToString("N"));
        try
        {
            var d = Valid();
            d["Hosting:Target"] = "production";
            d["Hosting:DataProtectionKeysPath"] = keys;
            d[LocalSignIn.PasswordKey] = Password;

            var ex = Assert.Throws<InvalidOperationException>(() =>
                Gallery.Web.AppBuilder.Build(["--urls", "http://127.0.0.1:0"], d));

            Assert.StartsWith("Production configuration invalid: ", ex.Message, StringComparison.Ordinal);
            Assert.Contains("LocalSignIn:Password must not be set", ex.Message, StringComparison.Ordinal);
            Assert.DoesNotContain(Password, ex.Message, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(keys)) Directory.Delete(keys, recursive: true);
        }
    }

    // Copied from ProductionGuardTests.Valid() (that file is not edited).
    private static Dictionary<string, string?> Valid() => new()
    {
        ["Mongo:ConnectionString"] = "mongodb+srv://gallery-app:***@cluster0.example.mongodb.net/",
        ["Mongo:Database"] = "gallery",
        ["Storage:ServiceUrl"] = "https://acct.r2.cloudflarestorage.com",
        ["Storage:AccessKeyId"] = "prod-access-key-id",
        ["Storage:SecretAccessKey"] = "prod-secret-access-key",
        ["Storage:PublicBucket"] = "gallery-public",
        ["Storage:PrivateBucket"] = "gallery-private",
        ["Storage:ImageBaseUrl"] = "https://img.example.com",
        ["Authentication:Google:ClientId"] = "prod-client.apps.googleusercontent.com",
        ["Authentication:Google:ClientSecret"] = "prod-google-client-secret",
        ["Edge:ZoneId"] = "prod-zone-id",
        ["Edge:ApiToken"] = "prod-api-token",
        ["Edge:PurgePages"] = "true",
        ["Edge:SiteBaseUrl"] = "https://gallery.example.com",
        ["Site:OutputCache"] = "true",
        ["Hosting:Edge"] = "cloudflare",
        ["Hosting:OriginSecret"] = "pg-origin-secret-0123456789abcdefghijklm",
        ["Hosting:DataProtectionKeysPath"] = "/home/data/keys",
        ["APPLICATIONINSIGHTS_CONNECTION_STRING"] =
            "InstrumentationKey=00000000-0000-0000-0000-000000000000;IngestionEndpoint=https://127.0.0.1:9/",
    };
}
