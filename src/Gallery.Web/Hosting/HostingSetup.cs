using Azure.Monitor.OpenTelemetry.AspNetCore;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;

namespace Gallery.Web.Hosting;

/// <summary>
/// The 012 production hosting seam (contracts/hosting.md). AppBuilder calls <see cref="AddHosting"/>
/// after AddEdge and <see cref="UseHosting"/> as the first middleware.
/// </summary>
public static class HostingSetup
{
    public const string AppInsightsKey = "APPLICATIONINSIGHTS_CONNECTION_STRING";

    public static IServiceCollection AddHosting(this IServiceCollection s, IConfiguration c)
    {
        // 1. Bind and register.
        var options = c.GetSection(HostingOptions.Section).Get<HostingOptions>() ?? new HostingOptions();
        s.AddSingleton(options);

        // 2. The origin secret is required behind the edge (message names the key, never the value).
        if (options.IsCloudflareEdge && (options.OriginSecret?.Length ?? 0) < 32)
            throw new InvalidOperationException(
                "Hosting:OriginSecret must be at least 32 characters when Hosting:Edge is cloudflare.");

        // 3. Data Protection keys survive restarts and slot swaps.
        if (!string.IsNullOrWhiteSpace(options.DataProtectionKeysPath))
        {
            var dir = Directory.CreateDirectory(options.DataProtectionKeysPath);
            s.AddDataProtection().PersistKeysToFileSystem(dir).SetApplicationName("gallery");
        }

        // 4. [SAFETY] I.5, FR-008: the visitor IP is Cloudflare's CF-Connecting-IP, one hop only.
        if (options.IsCloudflareEdge)
        {
            s.Configure<ForwardedHeadersOptions>(o =>
            {
                o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
                o.ForwardedForHeaderName = "CF-Connecting-IP";
                o.ForwardLimit = 1;
                o.KnownIPNetworks.Clear();
                o.KnownProxies.Clear();
            });
        }

        // 5. Telemetry export only when a connection string is configured; no sampling.
        var cs = c[AppInsightsKey];
        if (!string.IsNullOrWhiteSpace(cs))
        {
            s.AddOpenTelemetry().UseAzureMonitor(o =>
            {
                o.ConnectionString = cs;
                o.SamplingRatio = 1.0F;
                o.TracesPerSecond = null;
                o.EnableTraceBasedLogsSampler = false;
                o.EnableLiveMetrics = false;
            });
        }

        // 6. [SAFETY] FR-015: production must not point at dev resources.
        if (options.IsProductionTarget)
        {
            var failures = ProductionGuard.Check(c);
            if (failures.Count > 0)
                throw new InvalidOperationException(
                    "Production configuration invalid: " + string.Join("; ", failures));
        }

        return s;
    }

    public static WebApplication UseHosting(this WebApplication app)
    {
        var options = app.Services.GetRequiredService<HostingOptions>();
        if (options.IsCloudflareEdge)
        {
            app.UseMiddleware<OriginGuardMiddleware>();
            app.UseForwardedHeaders();
        }
        return app;
    }
}
