using Gallery.Web.Admin;
using Gallery.Web.Admin.Auth;
using Gallery.Web.Data;
using Gallery.Web.Edge;
using Gallery.Web.Library;
using Gallery.Web.Observability;
using Gallery.Web.Site;
using Gallery.Web.Storage;
using Gallery.Web.Styles;
using Gallery.Web.Ui;
using Gallery.Web.Upload;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MongoDB.Driver;

namespace Gallery.Web;

public static class AppBuilder
{
    public static WebApplication Build(
        string[] args,
        IDictionary<string, string?>? overrides = null,
        Action<WebApplicationBuilder>? configure = null)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.Logging.ClearProviders();
        builder.Logging.AddJsonConsole(o => o.JsonWriterOptions = new() { Indented = false });

        // Apply test overrides last so they win over all other configuration sources.
        if (overrides is not null)
            builder.Configuration.AddInMemoryCollection(overrides);

        // Test seam: e.g. a log-capturing provider.
        configure?.Invoke(builder);

        // ── UI / component services ──────────────────────────────────────────
        builder.Services.AddRazorComponents();
        builder.Services.AddSingleton<InlineStyles>();
        builder.Services.AddSingleton<SampleCatalog>();

        // ── Observability ───────────────────────────────────────────────────
        builder.Services.AddScoped<OpTimer>();

        // ── Storage ─────────────────────────────────────────────────────────
        var storageOptions = builder.Configuration.GetSection("Storage").Get<StorageOptions>()
                             ?? new StorageOptions();
        builder.Services.AddSingleton(storageOptions);
        builder.Services.AddSingleton<S3ClientFactory>();
        builder.Services.AddSingleton<ImageStore>();

        // ── Data (Mongo) ─────────────────────────────────────────────────────
        // Startup MUST NOT ping Mongo; ServerSelectionTimeout=2s limits hung requests only.
        var mongoOptions = builder.Configuration.GetSection("Mongo").Get<MongoOptions>()
                           ?? new MongoOptions();
        builder.Services.AddSingleton(mongoOptions);

        builder.Services.AddSingleton<IMongoClient>(_ =>
        {
            var settings = MongoClientSettings.FromConnectionString(
                string.IsNullOrWhiteSpace(mongoOptions.ConnectionString)
                    ? "mongodb://localhost:27017"
                    : mongoOptions.ConnectionString);
            settings.ServerSelectionTimeout = TimeSpan.FromSeconds(2);
            return new MongoClient(settings);
        });

        builder.Services.AddScoped<IMongoDatabase>(sp =>
        {
            var client = sp.GetRequiredService<IMongoClient>();
            var dbName = string.IsNullOrWhiteSpace(mongoOptions.Database)
                ? "gallery"
                : mongoOptions.Database;
            return client.GetDatabase(dbName);
        });

        builder.Services.AddScoped<ContentReader>();
        builder.Services.AddScoped<OwnerStore>();

        // ── Auth (owner cookie + optional OIDC) ─────────────────────────────
        builder.Services.AddOwnerAuth(builder.Configuration);

        // ── Site options ─────────────────────────────────────────────────────
        var siteOptions = builder.Configuration.GetSection("Site").Get<SiteOptions>() ?? new SiteOptions();
        builder.Services.AddSingleton(siteOptions);

        // ── Output cache ─────────────────────────────────────────────────────
        builder.Services.AddPublicOutputCache();

        // ── Upload pipeline (008; all registrations live in UploadSetup) ─────
        builder.Services.AddUpload(builder.Configuration);
        // ── Library and edge (009; all registrations live in LibrarySetup / EdgeSetup) ──
        builder.Services.AddLibrary(builder.Configuration);
        builder.Services.AddEdge(builder.Configuration);
        builder.Services.TryAddSingleton(TimeProvider.System);
        // ── Build ────────────────────────────────────────────────────────────
        var app = builder.Build();

        app.UseRouting();
        app.UseMiddleware<RequestTimingMiddleware>();

        // Redirect admin and OIDC paths to HTTPS (R-09)
        app.UseWhen(
            c => c.Request.Path.StartsWithSegments("/admin")
                 || c.Request.Path.StartsWithSegments("/signin-oidc"),
            a => a.UseHttpsRedirection());

        app.UseAdminHeaders();
        app.UseAuthentication();
        app.UseAuthorization();

        app.UseOutputCache();

        app.UseStaticFiles(new StaticFileOptions
        {
            OnPrepareResponse = ctx =>
            {
                var request = ctx.Context.Request;
                if (request.Path.StartsWithSegments("/samples"))
                    ctx.Context.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
                // 008 R-12: vendored and content-versioned (?v=) admin assets are immutable;
                // bare /admin-assets URLs revalidate so new markup never pairs with an old script.
                else if (request.Path.StartsWithSegments("/admin-assets"))
                    ctx.Context.Response.Headers.CacheControl =
                        request.Path.StartsWithSegments("/admin-assets/vendor") || request.Query.ContainsKey("v")
                            ? "public, max-age=31536000, immutable"
                            : "no-cache";
            },
        });

        app.MapPublic();
        app.MapAdmin();

        // MapUi in every environment (R-12 / T024)
        // Eager fixture loads at startup (Development only)
        if (app.Environment.IsDevelopment())
        {
            _ = app.Services.GetRequiredService<InlineStyles>();
            _ = app.Services.GetRequiredService<SampleCatalog>();
        }
        app.MapUi();

        return app;
    }
}
