using Gallery.Web.Maintenance;

namespace Gallery.Web.Hosting;

/// <summary>
/// [SAFETY] The production-not-dev configuration guard (012 contracts/hosting.md, FR-015, III.9/III.10).
/// One message per failed rule, naming the key and the rule, never the value; an empty list means pass.
/// The dev markers come from <see cref="DevTargetGuard"/> and are never repeated here.
/// </summary>
public static class ProductionGuard
{
    public static IReadOnlyList<string> Check(IConfiguration c)
    {
        var failed = new List<string>();

        var mongo = c["Mongo:ConnectionString"];
        if (Missing(mongo))
            failed.Add("Mongo:ConnectionString must be present");
        else if (mongo!.Contains("localhost", StringComparison.OrdinalIgnoreCase)
                 || mongo.Contains("127.0.0.1", StringComparison.Ordinal))
            failed.Add("Mongo:ConnectionString must not contain localhost or 127.0.0.1");

        var database = c["Mongo:Database"];
        if (Missing(database))
            failed.Add("Mongo:Database must be present");
        else if (database!.EndsWith(DevTargetGuard.DevDatabaseSuffix, StringComparison.Ordinal))
            failed.Add($"Mongo:Database must not end with {DevTargetGuard.DevDatabaseSuffix}");

        foreach (var key in new[] { "Storage:ServiceUrl", "Storage:AccessKeyId", "Storage:SecretAccessKey" })
            if (Missing(c[key]))
                failed.Add($"{key} must be present");

        foreach (var key in new[] { "Storage:PublicBucket", "Storage:PrivateBucket" })
        {
            var bucket = c[key];
            if (Missing(bucket))
                failed.Add($"{key} must be present");
            else if (bucket!.StartsWith(DevTargetGuard.DevBucketPrefix, StringComparison.Ordinal))
                failed.Add($"{key} must not start with {DevTargetGuard.DevBucketPrefix}");
        }

        var imageBase = c["Storage:ImageBaseUrl"];
        if (!StartsWithHttps(imageBase))
            failed.Add("Storage:ImageBaseUrl must start with https://");
        else if (imageBase!.Contains("img-dev.", StringComparison.OrdinalIgnoreCase))
            failed.Add("Storage:ImageBaseUrl must not contain img-dev.");

        foreach (var key in new[]
                 {
                     "Authentication:Google:ClientId", "Authentication:Google:ClientSecret",
                     "Edge:ZoneId", "Edge:ApiToken",
                 })
            if (Missing(c[key]))
                failed.Add($"{key} must be present");

        if (!IsTrue(c["Edge:PurgePages"]))
            failed.Add("Edge:PurgePages must be true");

        var siteBase = c["Edge:SiteBaseUrl"];
        if (!StartsWithHttps(siteBase))
            failed.Add("Edge:SiteBaseUrl must start with https://");
        else if (siteBase!.Contains("localhost", StringComparison.OrdinalIgnoreCase))
            failed.Add("Edge:SiteBaseUrl must not contain localhost");

        if (!IsTrue(c["Site:OutputCache"]))
            failed.Add("Site:OutputCache must be true");

        if (!string.Equals(c["Hosting:Edge"], "cloudflare", StringComparison.OrdinalIgnoreCase))
            failed.Add("Hosting:Edge must be cloudflare");

        if (Missing(c["Hosting:DataProtectionKeysPath"]))
            failed.Add("Hosting:DataProtectionKeysPath must be present");

        if (Missing(c["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
            failed.Add("APPLICATIONINSIGHTS_CONNECTION_STRING must be present");

        return failed;
    }

    private static bool Missing(string? value) => string.IsNullOrWhiteSpace(value);

    private static bool IsTrue(string? value) => bool.TryParse(value, out var b) && b;

    private static bool StartsWithHttps(string? value) =>
        value?.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ?? false;
}
