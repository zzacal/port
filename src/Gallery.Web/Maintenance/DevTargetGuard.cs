using Gallery.Web.Data;
using Gallery.Web.Storage;

namespace Gallery.Web.Maintenance;

/// <summary>
/// [SAFETY] The development-target guard of the removal command (009 T044,
/// contracts/removal-command.md, FR-045, III.10). All three checks must pass before anything is
/// touched: the environment is <c>Development</c>, <c>Storage:PublicBucket</c> starts with
/// <c>gallery-dev-</c>, and <c>Mongo:Database</c> ends with <c>_dev</c>. Each failed check is one
/// message, phrased as the retired seed command phrased it; an empty list means pass.
/// </summary>
public static class DevTargetGuard
{
    public const string DevBucketPrefix = "gallery-dev-";
    public const string DevDatabaseSuffix = "_dev";

    public static IReadOnlyList<string> Check(string environment, StorageOptions s, MongoOptions m)
    {
        var failed = new List<string>();
        if (!string.Equals(environment, "Development", StringComparison.OrdinalIgnoreCase))
            failed.Add($"Environment must be Development (got: {environment})");
        if (!(s.PublicBucket?.StartsWith(DevBucketPrefix, StringComparison.Ordinal) ?? false))
            failed.Add($"Storage:PublicBucket must start with {DevBucketPrefix} (got: {s.PublicBucket})");
        if (!(m.Database?.EndsWith(DevDatabaseSuffix, StringComparison.Ordinal) ?? false))
            failed.Add($"Mongo:Database must end with {DevDatabaseSuffix} (got: {m.Database})");
        return failed;
    }
}
