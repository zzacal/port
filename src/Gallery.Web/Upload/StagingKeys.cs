using Gallery.Core.Ids;

namespace Gallery.Web.Upload;

/// <summary>
/// Staged object keys in the private bucket (data-model "Staged keys"). Every key is built from
/// stored ids; the browser never sends a key (FR-019, FR-020). Every id must pass
/// <see cref="OpaqueId.IsValid"/>, or the method throws.
/// <code>
/// staging/{batchId}/{itemId}/{attemptId}/master.jpg
/// staging/{batchId}/{itemId}/{attemptId}/{w}.avif
/// staging/{batchId}/{itemId}/{attemptId}/{w}.webp
/// </code>
/// </summary>
public static class StagingKeys
{
    private const string Root = "staging/";

    public static string BatchPrefix(string batchId) =>
        $"{Root}{Id(batchId, nameof(batchId))}/";

    public static string ItemPrefix(string batchId, string itemId) =>
        $"{BatchPrefix(batchId)}{Id(itemId, nameof(itemId))}/";

    public static string AttemptPrefix(string batchId, string itemId, string attemptId) =>
        $"{ItemPrefix(batchId, itemId)}{Id(attemptId, nameof(attemptId))}/";

    public static string Master(string batchId, string itemId, string attemptId) =>
        $"{AttemptPrefix(batchId, itemId, attemptId)}master.jpg";

    public static string Size(string batchId, string itemId, string attemptId, int w, string ext)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(w);
        if (ext is not ("avif" or "webp"))
            throw new ArgumentException("Size extension must be avif or webp.", nameof(ext));
        return $"{AttemptPrefix(batchId, itemId, attemptId)}{w}.{ext}";
    }

    private static string Id(string id, string name) =>
        OpaqueId.IsValid(id) ? id : throw new ArgumentException("Not a valid opaque id.", name);
}
