using Gallery.Web.Data;
using Gallery.Web.Edge;
using Gallery.Web.Library;
using Gallery.Web.Observability;
using Gallery.Web.Site;
using Gallery.Web.Storage;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Gallery.Web.Maintenance;

/// <summary>The seven counts the removal command prints (contracts/removal-command.md).</summary>
public sealed record RemovalSummary(
    int GalleriesRemoved,
    int ImagesRemoved,
    int OwnerGalleriesChanged,
    int PublicFilesDeleted,
    int PublicFilesKept,
    int Leftovers)
{
    /// <summary>The seven output lines, verbatim.</summary>
    public IReadOnlyList<string> Lines() =>
    [
        $"seeded galleries removed: {GalleriesRemoved}",
        $"seeded images removed: {ImagesRemoved}",
        $"owner galleries changed: {OwnerGalleriesChanged}",
        $"public files deleted: {PublicFilesDeleted}",
        $"public files kept (shared with an uploaded photo): {PublicFilesKept}",
        $"leftover files (logged): {Leftovers}",
        "The running app drops its page cache within 60 seconds.",
    ];
}

/// <summary>
/// [SAFETY] <c>dotnet run --project src/Gallery.Web -- remove-seeded</c> (009 T045, FR-045–FR-048,
/// SC-010, contracts/removal-command.md, research R-11, decisions row 43). Builds the retired seed's
/// configuration, runs <see cref="DevTargetGuard"/> BEFORE any host is built (exit 4, nothing
/// touched), then starts a generic host with the app's registrations so <see cref="EdgeWorker"/>
/// sends the purge through the configured provider, and deletes every seeded gallery and image by
/// CALLING <see cref="GalleryService.DeleteAsync"/> and <see cref="ImageDeleteService.DeleteAsync"/>
/// (the normal delete rules; no second delete path). Owner content (no <c>seedKey</c>, or an empty
/// one) is never selected.
/// </summary>
public static class RemoveSeededCommand
{
    public const string OpName = "maintenance.remove-seeded";

    /// <summary>
    /// [SAFETY] The only selection used on both collections:
    /// <c>{seedKey: {$exists: true, $nin: [null, ""]}}</c>.
    /// </summary>
    public static BsonDocument SeededFilter() =>
        new("seedKey", new BsonDocument
        {
            { "$exists", true },
            { "$nin", new BsonArray { BsonNull.Value, "" } },
        });

    public static Task<int> RunAsync(string[] args, TextWriter output, IDictionary<string, string?>? overrides = null) =>
        RunAsync(args, output, overrides, configure: null);

    /// <summary>
    /// As <see cref="RunAsync(string[], TextWriter, IDictionary{string, string?}?)"/>, with a test
    /// seam (like <c>AppBuilder.Build</c>'s <c>configure</c>) that runs on the host builder before
    /// the app's registrations, e.g. to capture logs or register a recording edge provider. It runs
    /// only after the guard has passed.
    /// </summary>
    public static async Task<int> RunAsync(
        string[] args, TextWriter output, IDictionary<string, string?>? overrides,
        Action<HostApplicationBuilder>? configure)
    {
        // Configuration: the retired seed's sources and order, test overrides last.
        var repoRoot = FindRepoRoot();
        var configBuilder = new ConfigurationBuilder()
            .SetBasePath(repoRoot)
            .AddJsonFile("src/Gallery.Web/appsettings.json", optional: true)
            .AddJsonFile("src/Gallery.Web/appsettings.Development.json", optional: true)
            .AddEnvironmentVariables()
            .AddUserSecrets(typeof(AppBuilder).Assembly, optional: true);
        if (overrides is not null)
            configBuilder.AddInMemoryCollection(overrides);
        var config = configBuilder.Build();

        var storageOptions = config.GetSection("Storage").Get<StorageOptions>() ?? new StorageOptions();
        var mongoOptions = config.GetSection("Mongo").Get<MongoOptions>() ?? new MongoOptions();

        // Exit 2: name each missing key, never a value.
        var missing = storageOptions.Validate().Concat(mongoOptions.Validate()).ToList();
        if (missing.Count > 0)
        {
            foreach (var key in missing)
                await output.WriteLineAsync($"Missing required setting: {key}");
            return 2;
        }

        // Exit 4 [SAFETY]: the guard runs before the host is built or started; nothing is touched.
        var environment = config["ASPNETCORE_ENVIRONMENT"] ?? "Production";
        var failed = DevTargetGuard.Check(environment, storageOptions, mongoOptions);
        if (failed.Count > 0)
        {
            foreach (var check in failed)
                await output.WriteLineAsync($"Target guard failed: {check}");
            return 4;
        }

        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            DisableDefaults = true,
            EnvironmentName = environment,
            ContentRootPath = Path.Combine(repoRoot, "src", "Gallery.Web"),
        });
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddConfiguration(config);
        builder.Logging.ClearProviders();
        builder.Logging.AddConfiguration(config.GetSection("Logging"));
        builder.Logging.AddJsonConsole(o => o.JsonWriterOptions = new() { Indented = false });

        configure?.Invoke(builder);
        Register(builder.Services, builder.Configuration, storageOptions, mongoOptions);

        using var host = builder.Build();
        await host.StartAsync();
        try
        {
            RemovalSummary summary;
            try
            {
                summary = await RemoveAsync(host.Services);
            }
            catch (Exception ex) when (ex is MongoException or TimeoutException)
            {
                await output.WriteLineAsync($"Stopped: {ex.GetType().Name}. Run the command again to finish.");
                return 5;
            }

            // The purge goes through the configured provider: let the worker drain (up to 30 s).
            await host.Services.GetRequiredService<EdgeQueue>().WhenIdleAsync(TimeSpan.FromSeconds(30));

            foreach (var line in summary.Lines())
                await output.WriteLineAsync(line);
            return 0;
        }
        finally
        {
            await host.StopAsync();
        }
    }

    /// <summary>The registrations the app uses for these services (AppBuilder), minus the web parts.</summary>
    private static void Register(IServiceCollection s, IConfiguration c, StorageOptions storage, MongoOptions mongo)
    {
        s.AddScoped<OpTimer>();
        s.AddSingleton(storage);
        s.AddSingleton<S3ClientFactory>();
        s.AddSingleton<ImageStore>();
        s.AddSingleton(mongo);
        s.AddSingleton<IMongoClient>(_ =>
        {
            var settings = MongoClientSettings.FromConnectionString(mongo.ConnectionString);
            settings.ServerSelectionTimeout = TimeSpan.FromSeconds(2);
            return new MongoClient(settings);
        });
        s.AddScoped<IMongoDatabase>(sp => sp.GetRequiredService<IMongoClient>().GetDatabase(mongo.Database));
        // PageCacheEvictor needs the output-cache store; in this separate process its evictions are
        // no-ops, which is why the output says the running app drops its cache within 60 s.
        s.AddPublicOutputCache();
        s.AddLibrary(c);
        s.AddEdge(c);
        s.TryAddSingleton(TimeProvider.System);
    }

    /// <summary>
    /// The post-guard body: <see cref="Indexes.EnsureAsync"/>, then every seeded gallery through
    /// <see cref="GalleryService.DeleteAsync"/>, then every seeded image through
    /// <see cref="ImageDeleteService.DeleteAsync"/>. Does NOT check the target; callers that are not
    /// <see cref="RunAsync(string[], TextWriter, IDictionary{string, string?}?)"/> must be tests.
    /// </summary>
    public static async Task<RemovalSummary> RemoveAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var sp = scope.ServiceProvider;
        var timer = sp.GetRequiredService<OpTimer>();
        var log = sp.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(RemoveSeededCommand).FullName!);

        return await timer.Time(OpName, async () =>
        {
            var db = sp.GetRequiredService<IMongoDatabase>();
            await Indexes.EnsureAsync(db);

            var images = db.GetCollection<ImageDoc>("images");
            var galleries = db.GetCollection<GalleryDoc>("galleries");
            var seeded = SeededFilter();
            FilterDefinition<ImageDoc> seededImages = seeded;
            FilterDefinition<GalleryDoc> seededGalleries = seeded;

            var galleryIds = await galleries.Find(seededGalleries).Project(g => g.Id).ToListAsync();
            var seededImageDocs = await images.Find(seededImages).ToListAsync();
            var imageIds = seededImageDocs.Select(i => i.Id).ToList();

            // Owner galleries (not selected by the filter) that hold a seeded image.
            var ownerChanged = imageIds.Count == 0
                ? 0
                : (int)await galleries.CountDocumentsAsync(
                    Builders<GalleryDoc>.Filter.Not(seededGalleries)
                    & Builders<GalleryDoc>.Filter.AnyIn(g => g.Items, imageIds));

            // Public keys of seeded images that a live non-seeded image also references: the
            // shared-file rule in ImageDeleteService keeps them; counted here for the summary.
            var seededKeys = seededImageDocs
                .SelectMany(i => i.Derivatives).SelectMany(d => new[] { d.Avif, d.Webp })
                .Where(k => !string.IsNullOrEmpty(k)).ToHashSet(StringComparer.Ordinal);
            var kept = new HashSet<string>(StringComparer.Ordinal);
            if (seededKeys.Count > 0)
            {
                var keyArray = new BsonArray(seededKeys);
                var owners = await images.Find(
                        Builders<ImageDoc>.Filter.Not(seededImages)
                        & Builders<ImageDoc>.Filter.Eq(i => i.DeletingAt, null)
                        & new BsonDocument("$or", new BsonArray
                        {
                            new BsonDocument("derivatives.avif", new BsonDocument("$in", keyArray)),
                            new BsonDocument("derivatives.webp", new BsonDocument("$in", keyArray)),
                        }))
                    .Project(i => i.Derivatives).ToListAsync();
                foreach (var d in owners.SelectMany(x => x))
                {
                    if (seededKeys.Contains(d.Avif)) kept.Add(d.Avif);
                    if (seededKeys.Contains(d.Webp)) kept.Add(d.Webp);
                }
            }

            var galleryService = sp.GetRequiredService<GalleryService>();
            var removedGalleries = 0;
            foreach (var gid in galleryIds)
                removedGalleries += (await galleryService.DeleteAsync(gid)).Deleted;

            // The app's ImageDeleteService, with the app's IObjectDeleter wrapped only to count the
            // public deletes that succeeded (the decorator calls through; it never deletes itself).
            var storage = sp.GetRequiredService<StorageOptions>();
            var counting = new CountingDeleter(sp.GetRequiredService<IObjectDeleter>(), storage.PublicBucket ?? string.Empty);
            var imageDelete = ActivatorUtilities.CreateInstance<ImageDeleteService>(sp, counting);
            int removedImages = 0, leftovers = 0;
            foreach (var iid in imageIds)
            {
                var r = await imageDelete.DeleteAsync(iid);
                removedImages += r.Deleted;
                leftovers += r.Leftovers;
            }

            var summary = new RemovalSummary(
                removedGalleries, removedImages, ownerChanged, counting.PublicDeleted, kept.Count, leftovers);
            log.LogInformation("owner.action {action} {target} {count} {detail}",
                "seeded.remove", "seeded", removedGalleries + removedImages,
                $"galleries={summary.GalleriesRemoved} images={summary.ImagesRemoved} " +
                $"ownerGalleries={summary.OwnerGalleriesChanged} deleted={summary.PublicFilesDeleted} " +
                $"kept={summary.PublicFilesKept} leftovers={summary.Leftovers}");
            return summary;
        });
    }

    private sealed class CountingDeleter(IObjectDeleter inner, string publicBucket) : IObjectDeleter
    {
        private int _publicDeleted;
        public int PublicDeleted => Volatile.Read(ref _publicDeleted);

        public async Task DeleteAsync(string bucket, string key)
        {
            await inner.DeleteAsync(bucket, key);
            if (bucket == publicBucket) Interlocked.Increment(ref _publicDeleted);
        }
    }

    private static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir, "Gallery.slnx")))
                return dir;
            dir = Path.GetDirectoryName(dir);
        }
        return Directory.GetCurrentDirectory();
    }
}
