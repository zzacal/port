using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace Gallery.Web.Hosting;

/// <summary>
/// [SAFETY] Origin lock-down (012 contracts/hosting.md "UseHosting" step 1, FR-007). Only requests that
/// carry <c>X-Gallery-Origin</c> equal to <see cref="HostingOptions.OriginSecret"/> reach the app; others
/// get 403 with an empty body. The header is removed before the next middleware, and neither the
/// secret nor the received value is ever logged.
/// </summary>
public sealed class OriginGuardMiddleware(RequestDelegate next, HostingOptions options, ILoggerFactory loggers)
{
    public const string HeaderName = "X-Gallery-Origin";

    private readonly ILogger _log = loggers.CreateLogger("Gallery.Origin");
    private readonly byte[] _secret = Encoding.UTF8.GetBytes(options.OriginSecret ?? string.Empty);

    public Task InvokeAsync(HttpContext context)
    {
        var start = Stopwatch.GetTimestamp();
        var headers = context.Request.Headers;

        string? reason = null;
        if (!headers.TryGetValue(HeaderName, out var received) || received.Count == 0)
            reason = "missing";
        else if (!CryptographicOperations.FixedTimeEquals(
                     Encoding.UTF8.GetBytes(received.ToString()), _secret))
            reason = "mismatch";

        // No later code or log can read the header.
        headers.Remove(HeaderName);

        if (reason is null)
            return next(context);

        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        context.Response.Headers.CacheControl = "no-store";
        context.Response.ContentLength = 0;

        var ms = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        _log.LogInformation(
            "origin.refused {method} {path} {status} {duration_ms} {reason}",
            context.Request.Method,
            context.Request.Path.Value ?? "/",
            context.Response.StatusCode,
            Math.Round(ms, 2),
            reason);

        return Task.CompletedTask;
    }
}
