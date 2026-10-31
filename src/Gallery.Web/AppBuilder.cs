using Gallery.Web.Data;
using Gallery.Web.Observability;
using Gallery.Web.Storage;
using Gallery.Web.Styles;
using Gallery.Web.Ui;
using MongoDB.Driver;

namespace Gallery.Web;

public static class AppBuilder
{
    public static WebApplication Build(string[] args, IDictionary<string, string?>? overrides = null)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.Logging.ClearProviders();
        builder.Logging.AddJsonConsole(o => o.JsonWriterOptions = new() { Indented = false });

        // Apply test overrides last so they win over all other configuration sources.
        if (overrides is not null)
            builder.Configuration.AddInMemoryCollection(overrides);

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

        // ── Build ────────────────────────────────────────────────────────────
        var app = builder.Build();

        app.UseRouting();
        app.UseMiddleware<RequestTimingMiddleware>();

        app.UseStaticFiles(new StaticFileOptions
        {
            OnPrepareResponse = ctx =>
            {
                if (ctx.Context.Request.Path.StartsWithSegments("/samples"))
                    ctx.Context.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
            },
        });

        app.MapGet("/", () => "Hello World!");

        if (app.Environment.IsDevelopment())
        {
            // Load fixtures eagerly so startup timing is logged once, not on the first /ui request.
            _ = app.Services.GetRequiredService<InlineStyles>();
            _ = app.Services.GetRequiredService<SampleCatalog>();
            app.MapUi();
        }

        return app;
    }
}
