using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Gallery.Web.Observability;

namespace Gallery.Web.Edge;

/// <summary>
/// Dev-zone provider (FR-042, FR-043, research R-06), used when both <c>Edge:ZoneId</c> and
/// <c>Edge:ApiToken</c> are set. Purge: <c>POST https://api.cloudflare.com/client/v4/zones/{ZoneId}/purge_cache</c>
/// with <c>{"files":[…]}</c>; one retry after 2 s on HTTP 429 or Cloudflare error code 1134, then
/// <see cref="EdgeOutcome.RateLimited"/>; any other non-success is <see cref="EdgeOutcome.Failed"/>.
/// Warm: an anonymous GET per URL (no cookie, no Authorization header).
/// <para>
/// [SAFETY] III.9-style credential handling: the token goes ONLY into the outbound
/// <c>Authorization</c> header of the purge request. It is never placed in a URL, a log message, an
/// exception message or <c>ToString()</c>; the named clients have the factory's request loggers
/// removed, and no exception text or response body is ever logged.
/// </para>
/// </summary>
public sealed class CloudflareEdgeProvider : IEdgeProvider
{
    public const string ApiClientName = "edge-cloudflare-api";
    public const string WarmClientName = "edge-warm";
    public const string ApiBase = "https://api.cloudflare.com/client/v4/zones/";

    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(2);

    private readonly IHttpClientFactory _clients;
    private readonly EdgeOptions _options;
    private readonly ILogger<CloudflareEdgeProvider> _logger;

    public CloudflareEdgeProvider(IHttpClientFactory clients, EdgeOptions options, ILogger<CloudflareEdgeProvider> logger)
    {
        if (!options.HasCredentials)
            throw new InvalidOperationException("Edge:ZoneId and Edge:ApiToken must both be set for the dev-zone provider.");
        _clients = clients;
        _options = options;
        _logger = logger;
    }

    public string Mode => "dev-zone";

    /// <summary>The zone purge endpoint. Contains the zone id only, never the token.</summary>
    public string PurgeUrl => ApiBase + Uri.EscapeDataString(_options.ZoneId!.Trim()) + "/purge_cache";

    public async Task<EdgeOutcome> PurgeAsync(IReadOnlyList<string> urls, CancellationToken ct)
    {
        if (urls.Count == 0)
            return EdgeOutcome.Ok;

        var body = JsonSerializer.Serialize(new { files = urls });
        var client = _clients.CreateClient(ApiClientName);

        for (var attempt = 0; ; attempt++)
        {
            var (status, limited) = await SendPurgeAsync(client, body, ct);
            if (status == Status.Ok)
                return EdgeOutcome.Ok;
            if (!limited)
                return EdgeOutcome.Failed;
            if (attempt >= 1)
                return EdgeOutcome.RateLimited;
            await Task.Delay(RetryDelay, ct);
        }
    }

    public async Task<EdgeOutcome> WarmAsync(IReadOnlyList<string> urls, CancellationToken ct)
    {
        var client = _clients.CreateClient(WarmClientName);
        var allOk = true;
        foreach (var url in urls)
        {
            var start = Stopwatch.GetTimestamp();
            var outcome = "ok";
            try
            {
                // A fresh request with no headers beyond the defaults: anonymous, no cookie, no token.
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
                if (!response.IsSuccessStatusCode) { outcome = "error"; allOk = false; }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                outcome = "error";
                allOk = false;
            }
            OpTiming.Log(_logger, "edge.warm.request", Ms(start), outcome);
        }
        return allOk ? EdgeOutcome.Ok : EdgeOutcome.Failed;
    }

    private enum Status { Ok, NotOk }

    private async Task<(Status Status, bool RateLimited)> SendPurgeAsync(HttpClient client, string body, CancellationToken ct)
    {
        var start = Stopwatch.GetTimestamp();
        var outcome = "error";
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, PurgeUrl)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiToken!.Trim());

            using var response = await client.SendAsync(request, ct);
            var text = await response.Content.ReadAsStringAsync(ct);
            var limited = response.StatusCode == HttpStatusCode.TooManyRequests || HasErrorCode(text, 1134);
            if (response.IsSuccessStatusCode && !limited && !ReportsFailure(text))
            {
                outcome = "ok";
                return (Status.Ok, false);
            }
            return (Status.NotOk, limited);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // Exception text is never logged or rethrown: it is reduced to the outcome.
            return (Status.NotOk, false);
        }
        finally
        {
            OpTiming.Log(_logger, "edge.purge.call", Ms(start), outcome);
        }
    }

    private static bool HasErrorCode(string json, int code)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object
                || !doc.RootElement.TryGetProperty("errors", out var errors)
                || errors.ValueKind != JsonValueKind.Array)
                return false;
            foreach (var e in errors.EnumerateArray())
                if (e.ValueKind == JsonValueKind.Object
                    && e.TryGetProperty("code", out var c)
                    && c.ValueKind == JsonValueKind.Number
                    && c.TryGetInt32(out var n) && n == code)
                    return true;
            return false;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>A 2xx whose body says <c>"success": false</c> is a failure.</summary>
    private static bool ReportsFailure(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.ValueKind == JsonValueKind.Object
                   && doc.RootElement.TryGetProperty("success", out var s)
                   && s.ValueKind == JsonValueKind.False;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static double Ms(long start) => Math.Round(Stopwatch.GetElapsedTime(start).TotalMilliseconds, 2);
}
