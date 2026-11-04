using Gallery.Web.Hosting;
using Gallery.Web.Maintenance;
using Microsoft.Extensions.Configuration;

namespace Gallery.Web.Tests;

/// <summary>
/// T010 [SAFETY] (012 contracts/hosting.md "ProductionGuard", FR-015, III.9/III.10): a complete
/// production configuration passes; flipping any single rule yields exactly one message naming that
/// key and never its value; and a production-target host refuses to build.
/// </summary>
public class ProductionGuardTests
{
    private static Dictionary<string, string?> Valid() => new()
    {
        ["Mongo:ConnectionString"] = "mongodb+srv://gallery-app:pw-prod-0000@cluster0.example.mongodb.net/",
        ["Mongo:Database"] = "gallery",
        ["Storage:ServiceUrl"] = "https://acct.r2.cloudflarestorage.com",
        ["Storage:AccessKeyId"] = "prod-access-key-id",
        ["Storage:SecretAccessKey"] = "prod-secret-access-key",
        ["Storage:PublicBucket"] = "gallery-public",
        ["Storage:PrivateBucket"] = "gallery-private",
        ["Storage:ImageBaseUrl"] = "https://img.example.com",
        ["Authentication:Google:ClientId"] = "prod-client.apps.googleusercontent.com",
        ["Authentication:Google:ClientSecret"] = "prod-google-client-secret",
        ["Edge:ZoneId"] = "prod-zone-id",
        ["Edge:ApiToken"] = "prod-api-token",
        ["Edge:PurgePages"] = "true",
        ["Edge:SiteBaseUrl"] = "https://gallery.example.com",
        ["Site:OutputCache"] = "true",
        ["Hosting:Edge"] = "cloudflare",
        ["Hosting:OriginSecret"] = "pg-origin-secret-0123456789abcdefghijklm",
        ["Hosting:DataProtectionKeysPath"] = "/home/data/keys",
        ["APPLICATIONINSIGHTS_CONNECTION_STRING"] =
            "InstrumentationKey=00000000-0000-0000-0000-000000000000;IngestionEndpoint=https://127.0.0.1:9/",
    };

    private static IConfiguration Config(IDictionary<string, string?> d) =>
        new ConfigurationBuilder().AddInMemoryCollection(d).Build();

    [Fact]
    public void Valid_dictionary_has_no_dev_markers()
    {
        var d = Valid();
        Assert.False(d["Storage:PublicBucket"]!.StartsWith(DevTargetGuard.DevBucketPrefix, StringComparison.Ordinal));
        Assert.False(d["Storage:PrivateBucket"]!.StartsWith(DevTargetGuard.DevBucketPrefix, StringComparison.Ordinal));
        Assert.False(d["Mongo:Database"]!.EndsWith(DevTargetGuard.DevDatabaseSuffix, StringComparison.Ordinal));
        Assert.All(d.Values, v => Assert.DoesNotContain(DevTargetGuard.DevBucketPrefix, v!, StringComparison.Ordinal));
    }

    [Fact]
    public void Complete_valid_production_configuration_passes()
    {
        Assert.Empty(ProductionGuard.Check(Config(Valid())));
    }

    public static TheoryData<string, string?> Flips() => new()
    {
        { "Mongo:ConnectionString", null },
        { "Mongo:ConnectionString", "mongodb://localhost:27017/?pw=flip-local" },
        { "Mongo:ConnectionString", "mongodb://127.0.0.1:27017/?pw=flip-loop" },
        { "Mongo:Database", null },
        { "Mongo:Database", "gallery" + DevTargetGuard.DevDatabaseSuffix },
        { "Storage:ServiceUrl", "" },
        { "Storage:AccessKeyId", "" },
        { "Storage:SecretAccessKey", "" },
        { "Storage:PublicBucket", "" },
        { "Storage:PublicBucket", DevTargetGuard.DevBucketPrefix + "public" },
        { "Storage:PrivateBucket", "" },
        { "Storage:PrivateBucket", DevTargetGuard.DevBucketPrefix + "private" },
        { "Storage:ImageBaseUrl", null },
        { "Storage:ImageBaseUrl", "http://img.example.com" },
        { "Storage:ImageBaseUrl", "https://img-dev.example.com" },
        { "Authentication:Google:ClientId", "" },
        { "Authentication:Google:ClientSecret", "" },
        { "Edge:ZoneId", "" },
        { "Edge:ApiToken", "" },
        { "Edge:PurgePages", "false" },
        { "Edge:PurgePages", null },
        { "Edge:SiteBaseUrl", "http://gallery.example.com" },
        { "Edge:SiteBaseUrl", "https://localhost:5081" },
        { "Site:OutputCache", "false" },
        { "Site:OutputCache", null },
        { "Hosting:Edge", "none" },
        { "Hosting:Edge", null },
        { "Hosting:DataProtectionKeysPath", "" },
        { "APPLICATIONINSIGHTS_CONNECTION_STRING", "" },
    };

    [Theory]
    [MemberData(nameof(Flips))]
    public void One_flipped_rule_yields_exactly_one_message_naming_the_key(string key, string? bad)
    {
        var d = Valid();
        d[key] = bad;

        var failures = ProductionGuard.Check(Config(d));

        Assert.Single(failures);
        Assert.Contains(key, failures[0], StringComparison.Ordinal);
        if (!string.IsNullOrEmpty(bad))
            Assert.DoesNotContain(bad, failures[0], StringComparison.Ordinal);
    }

    [Fact]
    public void Production_target_host_with_one_bad_key_throws_at_Build_naming_key_not_value()
    {
        const string bad = DevTargetGuard.DevBucketPrefix + "public-hostcheck";
        var keys = Path.Combine(Path.GetTempPath(), "gallery-pg-" + Guid.NewGuid().ToString("N"));
        try
        {
            var d = Valid();
            d["Hosting:Target"] = "production";
            d["Hosting:DataProtectionKeysPath"] = keys;
            d["Storage:PublicBucket"] = bad;

            var ex = Assert.Throws<InvalidOperationException>(() =>
                Gallery.Web.AppBuilder.Build(["--urls", "http://127.0.0.1:0"], d));

            Assert.StartsWith("Production configuration invalid: ", ex.Message, StringComparison.Ordinal);
            Assert.Contains("Storage:PublicBucket", ex.Message, StringComparison.Ordinal);
            Assert.DoesNotContain(bad, ex.Message, StringComparison.Ordinal);
            Assert.DoesNotContain(d["Hosting:OriginSecret"]!, ex.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("Storage:PrivateBucket", ex.Message, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(keys)) Directory.Delete(keys, recursive: true);
        }
    }
}
