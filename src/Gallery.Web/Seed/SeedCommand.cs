using Gallery.Web.Data;
using Gallery.Web.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace Gallery.Web.Seed;

/// <summary>
/// Entry point for `dotnet run -- seed [--manifest path] [--prepared path]`.
/// Wired into Program.cs for the seed subcommand.
/// </summary>
public static class SeedCommand
{
    /// <summary>
    /// Main entry point called from Program.cs with real configuration.
    /// </summary>
    public static Task<int> RunAsync(string[] args, TextWriter @out)
        => RunAsync(args, overrides: null, @out);

    /// <summary>
    /// Testable overload: accepts configuration overrides (L4 tests).
    /// </summary>
    public static async Task<int> RunAsync(
        string[] args,
        IDictionary<string, string?>? overrides,
        TextWriter @out)
    {
        // Parse options
        var manifestPath = GetOption(args, "--manifest") ?? "seed/manifest.json";
        var preparedPath = GetOption(args, "--prepared") ?? ".dev-prepared";

        // Resolve paths relative to repo root (Gallery.slnx)
        var repoRoot = FindRepoRoot();
        if (!Path.IsPathRooted(manifestPath))
            manifestPath = Path.Combine(repoRoot, manifestPath);
        if (!Path.IsPathRooted(preparedPath))
            preparedPath = Path.Combine(repoRoot, preparedPath);

        // Build configuration WITHOUT starting a web server
        var configBuilder = new ConfigurationBuilder()
            .SetBasePath(repoRoot)
            .AddJsonFile("src/Gallery.Web/appsettings.json", optional: true)
            .AddJsonFile("src/Gallery.Web/appsettings.Development.json", optional: true)
            .AddEnvironmentVariables()
            // Always load user-secrets (M3), using AppBuilder's assembly
            .AddUserSecrets(typeof(AppBuilder).Assembly, optional: true);

        if (overrides is not null)
            configBuilder.AddInMemoryCollection(overrides);

        var config = configBuilder.Build();

        // Build DI container for seed (no web server)
        var services = new ServiceCollection();
        services.AddLogging(lb =>
        {
            lb.ClearProviders();
            lb.AddJsonConsole(o => o.JsonWriterOptions = new() { Indented = false });
        });

        var storageOptions = config.GetSection("Storage").Get<StorageOptions>() ?? new StorageOptions();
        var mongoOptions = config.GetSection("Mongo").Get<MongoOptions>() ?? new MongoOptions();

        services.AddSingleton(storageOptions);
        services.AddSingleton<S3ClientFactory>();
        services.AddSingleton<ImageStore>();
        services.AddSingleton<IMongoClient>(_ =>
        {
            var settings = MongoClientSettings.FromConnectionString(
                string.IsNullOrWhiteSpace(mongoOptions.ConnectionString)
                    ? "mongodb://localhost:27017"
                    : mongoOptions.ConnectionString);
            settings.ServerSelectionTimeout = TimeSpan.FromSeconds(2);
            return new MongoClient(settings);
        });

        var sp = services.BuildServiceProvider();
        var logger = sp.GetRequiredService<ILogger<object>>();

        // ── Validate storage and mongo options (exit 2) ──
        var missingStorage = storageOptions.Validate();
        var missingMongo = mongoOptions.Validate();
        var allMissing = missingStorage.Concat(missingMongo).ToList();
        if (allMissing.Count > 0)
        {
            foreach (var key in allMissing)
                await @out.WriteLineAsync($"Missing required setting: {key}");
            return 2;
        }

        // ── Target guard (FR-032, III.10, exit 4) ──
        var environment = config["ASPNETCORE_ENVIRONMENT"]
            ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
            ?? "Production";

        var targetGuardErrors = new List<string>();
        if (!string.Equals(environment, "Development", StringComparison.OrdinalIgnoreCase))
            targetGuardErrors.Add($"Environment must be Development (got: {environment})");
        if (!(storageOptions.PublicBucket?.StartsWith("gallery-dev-") ?? false))
            targetGuardErrors.Add($"Storage:PublicBucket must start with gallery-dev- (got: {storageOptions.PublicBucket})");
        if (!(mongoOptions.Database?.EndsWith("_dev") ?? false))
            targetGuardErrors.Add($"Mongo:Database must end with _dev (got: {mongoOptions.Database})");

        if (targetGuardErrors.Count > 0)
        {
            foreach (var err in targetGuardErrors)
                await @out.WriteLineAsync($"Target guard failed: {err}");
            return 4;
        }

        // ── Load manifest (exit 3 on error) ──
        var (manifest, manifestErrors) = ManifestLoader.Load(manifestPath);
        if (manifest is null || manifestErrors.Count > 0)
        {
            foreach (var err in manifestErrors)
                await @out.WriteLineAsync($"Manifest error: {err}");
            return 3;
        }

        // ── Run the seeder ──
        var imageStore = sp.GetRequiredService<ImageStore>();
        var mongoClient = sp.GetRequiredService<IMongoClient>();
        var db = mongoClient.GetDatabase(mongoOptions.Database!);

        // Determine photos dir (parallel to .dev-prepared, defaults to dev-pictures)
        var photosDir = Path.Combine(repoRoot, "dev-pictures");

        var seeder = new Seeder(
            manifest,
            preparedPath,
            photosDir,
            imageStore,
            db,
            logger,
            storageOptions.PublicBucket!);

        var summary = await seeder.SeedAsync();

        // ── Print summary lines ──
        await @out.WriteLineAsync($"galleries upserted: {summary.GalleriesUpserted}");
        await @out.WriteLineAsync($"galleries deleted: {summary.GalleriesDeleted}");
        await @out.WriteLineAsync($"images upserted: {summary.ImagesUpserted}");
        await @out.WriteLineAsync($"images deleted: {summary.ImagesDeleted}");
        await @out.WriteLineAsync($"uploaded: {summary.FilesUploaded}");
        await @out.WriteLineAsync($"skipped-unchanged: {summary.FilesSkipped}");
        await @out.WriteLineAsync($"unmatched manifest: {summary.UnmatchedManifest}");
        await @out.WriteLineAsync($"unmatched prepared: {summary.UnmatchedPrepared}");
        await @out.WriteLineAsync($"rejected: {summary.Rejected}");
        await @out.WriteLineAsync("Restart the running app to clear the output cache.");

        return 0;
    }

    private static string? GetOption(string[] args, string name)
    {
        for (int i = 0; i < args.Length - 1; i++)
            if (args[i] == name) return args[i + 1];
        return null;
    }

    /// <summary>
    /// Walks up from the current directory to find the repo root containing Gallery.slnx.
    /// </summary>
    public static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir, "Gallery.slnx")))
                return dir;
            dir = Path.GetDirectoryName(dir);
        }
        // Fallback to current directory
        return Directory.GetCurrentDirectory();
    }
}
