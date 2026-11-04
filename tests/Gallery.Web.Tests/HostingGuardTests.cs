using System.Net;
using System.Text.RegularExpressions;
using Gallery.Web.Tests.Fixtures;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Logging;

namespace Gallery.Web.Tests;

/// <summary>
/// T009 [SAFETY] (012 contracts/hosting.md "Tests"): the origin guard refuses requests without the
/// shared secret header, the visitor IP comes from CF-Connecting-IP only behind the guard, the
/// forwarded scheme reaches antiforgery, and the secret never appears in any log line.
/// </summary>
[Collection("mongo")]
public class HostingGuardTests(MongoFixture mongo) : IAsyncLifetime
{
    // Fixed 40-character test secret.
    private const string Secret = "hg-origin-secret-0123456789abcdefghijklm";
    private const string Header = "X-Gallery-Origin";
    private const string VisitorIp = "203.0.113.9";

    private FakeGoogle _google = null!;

    public async Task InitializeAsync()
    {
        Assert.Equal(40, Secret.Length);
        _google = new FakeGoogle();
        await _google.InitializeAsync();
    }

    public async Task DisposeAsync() => await _google.DisposeAsync();

    // ── refusals ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task No_header_is_refused_403_empty_body_and_logged_missing()
    {
        await using var host = await Host.StartAsync(mongo, _google, edge: true);

        var resp = await host.Client.GetAsync("/");

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
        Assert.Equal("", await resp.Content.ReadAsStringAsync());
        Assert.Equal("no-store", resp.Headers.CacheControl?.ToString());

        var refused = host.Lines().Where(l => l.StartsWith("origin.refused ", StringComparison.Ordinal)).ToList();
        Assert.Single(refused);
        Assert.Matches(new Regex(@"^origin\.refused GET / 403 \d+(\.\d+)? missing$"), refused[0]);
        // Refused requests are logged only as origin.refused, never as http.request.
        Assert.Equal(0, host.Lines().Count(l => l.StartsWith("http.request ", StringComparison.Ordinal)));
        host.AssertSecretNeverLogged();
    }

    [Theory]
    [InlineData("hg-origin-secret-0123456789abcdefghijklX")] // same length, last char differs
    [InlineData("wrong-short-value")]                          // different length
    [InlineData("hg-origin-secret-0123456789abcdefghijklmX")] // secret plus one char
    public async Task Wrong_header_is_refused_403_and_logged_mismatch(string wrong)
    {
        await using var host = await Host.StartAsync(mongo, _google, edge: true);

        using var req = new HttpRequestMessage(HttpMethod.Get, "/");
        req.Headers.TryAddWithoutValidation(Header, wrong);
        var resp = await host.Client.SendAsync(req);

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
        Assert.Equal("", await resp.Content.ReadAsStringAsync());

        var refused = host.Lines().Where(l => l.StartsWith("origin.refused ", StringComparison.Ordinal)).ToList();
        Assert.Single(refused);
        Assert.Matches(new Regex(@"^origin\.refused GET / 403 \d+(\.\d+)? mismatch$"), refused[0]);
        Assert.All(host.Lines(), l => Assert.DoesNotContain(wrong, l, StringComparison.Ordinal));
        host.AssertSecretNeverLogged();
    }

    // ── pass-through ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Correct_header_passes_and_home_answers_200()
    {
        await using var host = await Host.StartAsync(mongo, _google, edge: true);

        using var req = new HttpRequestMessage(HttpMethod.Get, "/");
        req.Headers.TryAddWithoutValidation(Header, Secret);
        var resp = await host.Client.SendAsync(req);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal(0, host.Lines().Count(l => l.StartsWith("origin.refused ", StringComparison.Ordinal)));
        Assert.Equal(1, host.Lines().Count(l => l.StartsWith("http.request GET / 200 ", StringComparison.Ordinal)));
        host.AssertSecretNeverLogged();
    }

    [Fact]
    public async Task CF_Connecting_IP_behind_guard_is_the_logged_ip()
    {
        await using var host = await Host.StartAsync(mongo, _google, edge: true);

        using var req = new HttpRequestMessage(HttpMethod.Get, "/signin-oidc");
        req.Headers.TryAddWithoutValidation(Header, Secret);
        req.Headers.TryAddWithoutValidation("CF-Connecting-IP", VisitorIp);
        var resp = await host.Client.SendAsync(req);

        Assert.Equal(HttpStatusCode.Redirect, resp.StatusCode);
        Assert.Equal("/admin/signin?r=failed", resp.Headers.Location?.ToString());

        var failed = host.Lines().Where(l => l.StartsWith("auth.failed ", StringComparison.Ordinal)).ToList();
        Assert.Single(failed);
        Assert.Equal(VisitorIp, failed[0].Split(' ')[1]);
        host.AssertSecretNeverLogged();
    }

    [Fact]
    public async Task X_Forwarded_Proto_https_lets_signin_page_render_200()
    {
        await using var host = await Host.StartAsync(mongo, _google, edge: true);

        using var req = new HttpRequestMessage(HttpMethod.Get, "/admin/signin");
        req.Headers.TryAddWithoutValidation(Header, Secret);
        req.Headers.TryAddWithoutValidation("CF-Connecting-IP", VisitorIp);
        req.Headers.TryAddWithoutValidation("X-Forwarded-Proto", "https");
        var resp = await host.Client.SendAsync(req);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Contains("__RequestVerificationToken", await resp.Content.ReadAsStringAsync());
        host.AssertSecretNeverLogged();
    }

    // ── edge unset: today's behavior ─────────────────────────────────────────

    [Fact]
    public async Task Edge_unset_needs_no_header_and_ignores_CF_Connecting_IP()
    {
        await using var host = await Host.StartAsync(mongo, _google, edge: false);

        var home = await host.Client.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, home.StatusCode);

        using var req = new HttpRequestMessage(HttpMethod.Get, "/signin-oidc");
        req.Headers.TryAddWithoutValidation("CF-Connecting-IP", VisitorIp);
        var resp = await host.Client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Redirect, resp.StatusCode);

        var failed = host.Lines().Where(l => l.StartsWith("auth.failed ", StringComparison.Ordinal)).ToList();
        Assert.Single(failed);
        var ip = failed[0].Split(' ')[1];
        Assert.True(ip is "127.0.0.1" or "::1" or "::ffff:127.0.0.1", $"expected loopback, got '{ip}'");
        Assert.NotEqual(VisitorIp, ip);
        Assert.Equal(0, host.Lines().Count(l => l.StartsWith("origin.refused ", StringComparison.Ordinal)));
    }

    // ── host helper ──────────────────────────────────────────────────────────

    private sealed class Host : IAsyncDisposable
    {
        private readonly WebApplication _app;
        private readonly List<string> _lines;
        public HttpClient Client { get; }

        private Host(WebApplication app, List<string> lines, HttpClient client)
        {
            _app = app;
            _lines = lines;
            Client = client;
        }

        public List<string> Lines()
        {
            lock (_lines) return [.. _lines];
        }

        public void AssertSecretNeverLogged()
        {
            var lines = Lines();
            Assert.NotEmpty(lines);
            Assert.All(lines, l => Assert.DoesNotContain(Secret, l, StringComparison.Ordinal));
        }

        public static async Task<Host> StartAsync(MongoFixture mongo, FakeGoogle google, bool edge)
        {
            var db = mongo.NewDatabase();
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
                ["Authentication:Google:ClientId"] = "test-client",
                ["Authentication:Google:ClientSecret"] = "test-secret",
                ["Authentication:Google:Authority"] = google.Authority,
                ["Authentication:Google:RequireHttpsMetadata"] = "false",
            };
            if (edge)
            {
                overrides["Hosting:Edge"] = "cloudflare";
                overrides["Hosting:OriginSecret"] = Secret;
            }

            var lines = new List<string>();
            var app = Gallery.Web.AppBuilder.Build(
                ["--urls", "http://127.0.0.1:0"],
                overrides,
                b => b.Logging.AddProvider(new CaptureLoggerProvider(lines)));
            await app.StartAsync();

            var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false })
            {
                BaseAddress = new Uri(app.Urls.First()),
            };
            return new Host(app, lines, client);
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }
}
