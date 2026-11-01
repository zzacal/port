using Gallery.Web.Seed;

namespace Gallery.Web.Tests;

/// <summary>
/// T032: SeedCommand tests (L4, no containers needed).
/// Tests the entry point's option parsing and guard logic.
/// </summary>
public class SeedCommandTests
{
    // Base configuration for a valid Development environment
    private static readonly Dictionary<string, string?> BaseConfig = new()
    {
        ["ASPNETCORE_ENVIRONMENT"] = "Development",
        ["Storage:ServiceUrl"] = "http://localhost:9000",
        ["Storage:AccessKeyId"] = "test-key",
        ["Storage:SecretAccessKey"] = "test-secret",
        ["Storage:PublicBucket"] = "gallery-dev-public",
        ["Storage:PrivateBucket"] = "gallery-dev-private",
        ["Storage:ImageBaseUrl"] = "https://img.test",
        ["Mongo:ConnectionString"] = "mongodb://localhost:27017",
        ["Mongo:Database"] = "gallery_dev",
    };

    private static Dictionary<string, string?> OverrideWith(params (string key, string? value)[] overrides)
    {
        var config = new Dictionary<string, string?>(BaseConfig);
        foreach (var (key, value) in overrides)
            config[key] = value;
        return config;
    }

    [Fact]
    public async Task Missing_AccessKeyId_exits_2_and_names_key_not_value()
    {
        var config = OverrideWith(("Storage:AccessKeyId", null));
        var output = new StringWriter();
        var exit = await SeedCommand.RunAsync(["seed"], config, output);

        Assert.Equal(2, exit);
        var text = output.ToString();
        Assert.Contains("Storage:AccessKeyId", text);
        // Must not print null value
        Assert.DoesNotContain("null", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Missing_ServiceUrl_exits_2_and_names_key()
    {
        var config = OverrideWith(("Storage:ServiceUrl", null));
        var output = new StringWriter();
        var exit = await SeedCommand.RunAsync(["seed"], config, output);

        Assert.Equal(2, exit);
        Assert.Contains("Storage:ServiceUrl", output.ToString());
    }

    [Fact]
    public async Task Production_bucket_exits_4_with_message()
    {
        var config = OverrideWith(("Storage:PublicBucket", "gallery-prod-public"));
        var output = new StringWriter();
        var exit = await SeedCommand.RunAsync(["seed"], config, output);

        Assert.Equal(4, exit);
        var text = output.ToString();
        Assert.Contains("gallery-prod-public", text);
    }

    [Fact]
    public async Task Production_database_exits_4_with_message()
    {
        var config = OverrideWith(("Mongo:Database", "gallery"));
        var output = new StringWriter();
        var exit = await SeedCommand.RunAsync(["seed"], config, output);

        Assert.Equal(4, exit);
        var text = output.ToString();
        Assert.Contains("gallery", text);
    }

    [Fact]
    public async Task Non_development_environment_exits_4_with_message()
    {
        var config = OverrideWith(("ASPNETCORE_ENVIRONMENT", "Production"));
        var output = new StringWriter();
        var exit = await SeedCommand.RunAsync(["seed"], config, output);

        Assert.Equal(4, exit);
        var text = output.ToString();
        Assert.Contains("Development", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Missing_manifest_file_exits_3()
    {
        var config = new Dictionary<string, string?>(BaseConfig);
        var output = new StringWriter();
        // Use a non-existent manifest path
        var exit = await SeedCommand.RunAsync(
            ["seed", "--manifest", "/nonexistent/path/manifest.json"],
            config, output);

        Assert.Equal(3, exit);
        var text = output.ToString();
        Assert.Contains("error", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FindRepoRoot_finds_Gallery_slnx()
    {
        var root = SeedCommand.FindRepoRoot();
        Assert.True(File.Exists(Path.Combine(root, "Gallery.slnx")),
            $"Gallery.slnx not found at: {root}");
    }
}
