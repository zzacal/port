using Gallery.Web.Library;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Gallery.Web.Edge;

/// <summary>
/// The 009 edge purge/warm registration seam (tasks.md T003). AppBuilder calls <see cref="AddEdge"/>
/// once. Later batches add to this file only: one registration line per change.
/// </summary>
public static class EdgeSetup
{
    public static IServiceCollection AddEdge(this IServiceCollection s, IConfiguration c)
    {
        var options = EdgeOptions.Load(c);
        s.AddSingleton(options);
        // FR-042, decisions row 6: dev-zone only when BOTH credentials are set, else log-only.
        // TryAdd: AppBuilder runs the test `configure` hook first, so a recorder registered there wins.
        if (options.HasCredentials)
            s.TryAddSingleton<IEdgeProvider, CloudflareEdgeProvider>();
        else
            s.TryAddSingleton<IEdgeProvider, LogOnlyEdgeProvider>();
        // [SAFETY] FR-043: no factory request loggers on either client, so no request line or header
        // is ever logged by the framework; the warm client never stores or sends cookies.
        s.AddHttpClient(CloudflareEdgeProvider.ApiClientName).RemoveAllLoggers();
        s.AddHttpClient(CloudflareEdgeProvider.WarmClientName).RemoveAllLoggers()
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { UseCookies = false, AllowAutoRedirect = false });
        s.AddSingleton<EdgeQueue>();
        s.AddSingleton<IEdgeCache>(sp => sp.GetRequiredService<EdgeQueue>());
        s.AddHostedService<EdgeWorker>();
        s.AddScoped<PageCacheEvictor>();
        return s;
    }
}
