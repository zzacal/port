using System.Diagnostics;
using Gallery.Core.Observability;

namespace Gallery.Web.Observability;

/// <summary>
/// One structured <c>http.request</c> event per request plus a Server-Timing header
/// (Constitution IV.1, IV.3). Registered after UseRouting so the route pattern is known.
/// </summary>
public sealed class RequestTimingMiddleware(RequestDelegate next, ILoggerFactory loggers)
{
    private readonly ILogger _log = loggers.CreateLogger("Gallery.Request");

    public async Task InvokeAsync(HttpContext context)
    {
        var start = Stopwatch.GetTimestamp();

        context.Response.OnStarting(() =>
        {
            var ms = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            context.Response.Headers.Append("Server-Timing", ServerTiming.Format("app", ms));
            return Task.CompletedTask;
        });

        try
        {
            await next(context);
        }
        finally
        {
            var ms = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            var route = (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText
                        ?? context.Request.Path.Value
                        ?? "/";
            _log.LogInformation(
                "http.request {method} {route} {status} {duration_ms}",
                context.Request.Method,
                route,
                context.Response.StatusCode,
                Math.Round(ms, 2));
        }
    }
}
