using System.Net;
using System.Text;
using System.Text.Json;
using Gallery.Web.Edge;
using Gallery.Web.Tests.Fixtures;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging;

namespace Gallery.Web.Tests;

/// <summary>
/// 009 T018 [SAFETY] (FR-043, SC-011, research R-06): the dev-zone provider against a fake
/// <see cref="HttpMessageHandler"/>. No test calls the real Cloudflare API. The token is a test
/// literal; it must appear only in the purge request's Authorization header: never in a request
/// URL, a log line, an exception message or a warm request.
/// </summary>
public class CloudflareEdgeProviderTests
{
    private const string Token = "test-edge-token-0123456789abcdef";
    private const string Zone = "zone0123456789abcdef0123456789ab";
    private const string PurgeUrl = "https://api.cloudflare.com/client/v4/zones/" + Zone + "/purge_cache";

    private sealed record Captured(HttpMethod Method, string Url, string? Authorization, string? Cookie, string? Body);

    /// <summary>Answers from a script of (status, body) responses, then 200 success; records every request.</summary>
    private sealed class FakeHandler(params (HttpStatusCode Status, string Body)[] script) : HttpMessageHandler
    {
        private readonly Queue<(HttpStatusCode, string)> _script = new(script);
        public List<Captured> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            var cookie = request.Headers.TryGetValues("Cookie", out var c) ? string.Join("; ", c) : null;
            lock (Requests)
                Requests.Add(new Captured(request.Method, request.RequestUri!.ToString(), request.Headers.Authorization?.ToString(), cookie, body));
            (HttpStatusCode status, string text) next;
            lock (_script) next = _script.Count > 0 ? _script.Dequeue() : (HttpStatusCode.OK, """{"success":true,"errors":[],"messages":[],"result":{"id":"x"}}""");
            return new HttpResponseMessage(next.status) { Content = new StringContent(next.text, Encoding.UTF8, "application/json") };
        }
    }

    private sealed class FakeFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private static (CloudflareEdgeProvider Provider, List<string> Lines, ILoggerFactory Logs) Create(FakeHandler handler)
    {
        var lines = new List<string>();
        var logs = LoggerFactory.Create(b => b.AddProvider(new CaptureLoggerProvider(lines)));
        var options = new EdgeOptions { ZoneId = Zone, ApiToken = Token };
        return (new CloudflareEdgeProvider(new FakeFactory(handler), options, logs.CreateLogger<CloudflareEdgeProvider>()), lines, logs);
    }

    private static readonly string[] Urls = ["https://img.test/a-320.avif", "https://img.test/a-640.avif"];

    private static void AssertNoToken(IEnumerable<string> lines, IEnumerable<Captured> requests)
    {
        Assert.DoesNotContain(lines, l => l.Contains(Token, StringComparison.Ordinal));
        Assert.DoesNotContain(requests, r => r.Url.Contains(Token, StringComparison.Ordinal));
        Assert.DoesNotContain(requests, r => r.Body is not null && r.Body.Contains(Token, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Purge_posts_the_files_body_to_the_zone_url_with_exactly_the_bearer_header()
    {
        var handler = new FakeHandler();
        var (provider, lines, logs) = Create(handler);
        using var _ = logs;

        Assert.Equal("dev-zone", provider.Mode);
        var outcome = await provider.PurgeAsync(Urls, CancellationToken.None);

        Assert.Equal(EdgeOutcome.Ok, outcome);
        var req = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, req.Method);
        Assert.Equal(PurgeUrl, req.Url);
        Assert.Equal("Bearer " + Token, req.Authorization);
        Assert.Null(req.Cookie);
        Assert.Equal("""{"files":["https://img.test/a-320.avif","https://img.test/a-640.avif"]}""", req.Body);
        using var doc = JsonDocument.Parse(req.Body!);
        Assert.Equal(["files"], doc.RootElement.EnumerateObject().Select(p => p.Name).ToArray());

        Assert.Single(lines, l => l.StartsWith("op.timing edge.purge.call ", StringComparison.Ordinal) && l.EndsWith(" ok", StringComparison.Ordinal));
        AssertNoToken(lines, handler.Requests);
    }

    [Fact]
    public async Task A_429_then_200_sends_two_requests_and_is_ok()
    {
        var handler = new FakeHandler((HttpStatusCode.TooManyRequests, """{"success":false,"errors":[{"code":971,"message":"slow down"}]}"""));
        var (provider, lines, logs) = Create(handler);
        using var _ = logs;

        var outcome = await provider.PurgeAsync(Urls, CancellationToken.None);

        Assert.Equal(EdgeOutcome.Ok, outcome);
        Assert.Equal(2, handler.Requests.Count);
        Assert.All(handler.Requests, r => Assert.Equal(PurgeUrl, r.Url));
        Assert.All(handler.Requests, r => Assert.Equal("Bearer " + Token, r.Authorization));
        Assert.Equal(2, lines.Count(l => l.StartsWith("op.timing edge.purge.call ", StringComparison.Ordinal)));
        AssertNoToken(lines, handler.Requests);
    }

    [Fact]
    public async Task A_429_twice_is_rate_limited_after_exactly_one_retry()
    {
        var handler = new FakeHandler(
            (HttpStatusCode.TooManyRequests, "{}"),
            (HttpStatusCode.TooManyRequests, "{}"));
        var (provider, lines, logs) = Create(handler);
        using var _ = logs;

        var outcome = await provider.PurgeAsync(Urls, CancellationToken.None);

        Assert.Equal(EdgeOutcome.RateLimited, outcome);
        Assert.Equal(2, handler.Requests.Count);
        AssertNoToken(lines, handler.Requests);
    }

    [Fact]
    public async Task Error_code_1134_is_retried_like_a_429()
    {
        const string limited = """{"success":false,"errors":[{"code":1134,"message":"rate limited"}]}""";
        var handler = new FakeHandler((HttpStatusCode.OK, limited), (HttpStatusCode.OK, limited));
        var (provider, lines, logs) = Create(handler);
        using var _ = logs;

        Assert.Equal(EdgeOutcome.RateLimited, await provider.PurgeAsync(Urls, CancellationToken.None));
        Assert.Equal(2, handler.Requests.Count);
        AssertNoToken(lines, handler.Requests);
    }

    [Fact]
    public async Task Any_other_non_success_is_failed_without_a_retry()
    {
        var handler = new FakeHandler((HttpStatusCode.Forbidden, """{"success":false,"errors":[{"code":10000,"message":"Authentication error"}]}"""));
        var (provider, lines, logs) = Create(handler);
        using var _ = logs;

        Assert.Equal(EdgeOutcome.Failed, await provider.PurgeAsync(Urls, CancellationToken.None));
        Assert.Single(handler.Requests);
        Assert.Single(lines, l => l.StartsWith("op.timing edge.purge.call ", StringComparison.Ordinal) && l.EndsWith(" error", StringComparison.Ordinal));
        AssertNoToken(lines, handler.Requests);
    }

    [Fact]
    public async Task Warm_requests_are_anonymous_GETs_with_no_authorization_and_no_cookie()
    {
        var handler = new FakeHandler();
        var (provider, lines, logs) = Create(handler);
        using var _ = logs;

        var outcome = await provider.WarmAsync(Urls, CancellationToken.None);

        Assert.Equal(EdgeOutcome.Ok, outcome);
        Assert.Equal(Urls, handler.Requests.Select(r => r.Url).ToArray());
        Assert.All(handler.Requests, r => Assert.Equal(HttpMethod.Get, r.Method));
        Assert.All(handler.Requests, r => Assert.Null(r.Authorization));
        Assert.All(handler.Requests, r => Assert.Null(r.Cookie));
        Assert.Equal(2, lines.Count(l => l.StartsWith("op.timing edge.warm.request ", StringComparison.Ordinal) && l.EndsWith(" ok", StringComparison.Ordinal)));
        AssertNoToken(lines, handler.Requests);
    }

    [Fact]
    public async Task A_transport_exception_is_failed_and_its_message_is_not_logged()
    {
        var (provider, lines, logs) = CreateThrowing();
        using var _ = logs;

        Assert.Equal(EdgeOutcome.Failed, await provider.PurgeAsync(Urls, CancellationToken.None));
        Assert.DoesNotContain(lines, l => l.Contains("boom", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, l => l.Contains(Token, StringComparison.Ordinal));
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            throw new HttpRequestException("boom " + request.Headers.Authorization);
    }

    private static (CloudflareEdgeProvider, List<string>, ILoggerFactory) CreateThrowing()
    {
        var lines = new List<string>();
        var logs = LoggerFactory.Create(b => b.AddProvider(new CaptureLoggerProvider(lines)));
        var options = new EdgeOptions { ZoneId = Zone, ApiToken = Token };
        return (new CloudflareEdgeProvider(new FakeFactory(new ThrowingHandler()), options, logs.CreateLogger<CloudflareEdgeProvider>()), lines, logs);
    }

    [Fact]
    public void The_options_and_provider_never_print_the_token()
    {
        var options = new EdgeOptions { ZoneId = Zone, ApiToken = Token };
        Assert.DoesNotContain(Token, options.ToString() ?? "", StringComparison.Ordinal);
        var (provider, _, logs) = Create(new FakeHandler());
        using var __ = logs;
        Assert.DoesNotContain(Token, provider.ToString() ?? "", StringComparison.Ordinal);
        Assert.DoesNotContain(Token, provider.PurgeUrl, StringComparison.Ordinal);
    }

    /// <summary>
    /// Through the real registration (FR-042): with both credentials set, AddEdge picks the dev-zone
    /// provider; a work item goes through the worker and the named clients over a fake primary handler,
    /// and no log line from ANY category (HttpClient factory included) carries the token.
    /// </summary>
    [Fact]
    public async Task Through_DI_the_dev_zone_worker_purges_and_warms_and_no_log_line_carries_the_token()
    {
        var handler = new FakeHandler();
        var lines = new List<string>();
        var config = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Edge:ZoneId"] = Zone,
                ["Edge:ApiToken"] = Token,
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Trace).AddProvider(new CaptureLoggerProvider(lines)));
        services.AddSingleton(TimeProvider.System);
        services.AddEdge(config);
        services.PostConfigureAll<HttpClientFactoryOptions>(o =>
            o.HttpMessageHandlerBuilderActions.Add(b => b.PrimaryHandler = handler));

        await using var sp = services.BuildServiceProvider();
        Assert.IsType<CloudflareEdgeProvider>(sp.GetRequiredService<IEdgeProvider>());

        var worker = sp.GetServices<Microsoft.Extensions.Hosting.IHostedService>().OfType<EdgeWorker>().Single();
        await worker.StartAsync(CancellationToken.None);
        try
        {
            sp.GetRequiredService<IEdgeCache>().Enqueue(new EdgeWork(
                "t.di", ["https://localhost:5081/"], [Urls[0]], [Urls[1]], DateTimeOffset.UtcNow));
            Assert.True(await sp.GetRequiredService<EdgeQueue>().WhenIdleAsync(TimeSpan.FromSeconds(10)));
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }

        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(PurgeUrl, handler.Requests[0].Url);
        Assert.Equal("Bearer " + Token, handler.Requests[0].Authorization);
        Assert.Equal(Urls[1], handler.Requests[1].Url);
        Assert.Null(handler.Requests[1].Authorization);
        Assert.Null(handler.Requests[1].Cookie);

        List<string> snapshot;
        lock (lines) snapshot = lines.ToList();
        Assert.Contains(snapshot, l => l.StartsWith("edge.purge t.di dev-zone 1 ", StringComparison.Ordinal) && l.EndsWith(" ok " + Urls[0], StringComparison.Ordinal));
        Assert.Contains("edge.purge t.di dev-zone 1 0 skipped https://localhost:5081/", snapshot);
        AssertNoToken(snapshot, handler.Requests);
    }

    [Fact]
    public void Without_both_credentials_AddEdge_registers_the_log_only_provider()
    {
        foreach (var (zone, token) in new[] { (null, null), (Zone, null), (null, Token), ("", Token), (Zone, " ") })
        {
            var config = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["Edge:ZoneId"] = zone, ["Edge:ApiToken"] = token })
                .Build();
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddEdge(config);
            using var sp = services.BuildServiceProvider();
            var provider = sp.GetRequiredService<IEdgeProvider>();
            Assert.IsType<LogOnlyEdgeProvider>(provider);
            Assert.Equal("log-only", provider.Mode);
        }
    }
}
