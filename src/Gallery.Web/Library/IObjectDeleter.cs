using Gallery.Web.Storage;

namespace Gallery.Web.Library;

/// <summary>
/// The storage delete seam of image delete (tasks.md T024, decisions row 36): one object, by bucket
/// and key. A missing object is not an error (S3 semantics, research R-05 / probe P3). Tests replace
/// it to fail one key; the app uses <see cref="ImageStoreObjectDeleter"/>. Registered scoped.
/// </summary>
public interface IObjectDeleter
{
    Task DeleteAsync(string bucket, string key);
}

/// <summary>The default <see cref="IObjectDeleter"/>: calls the existing <see cref="ImageStore.DeleteAsync"/> (op <c>s3.delete</c>).</summary>
public sealed class ImageStoreObjectDeleter(ImageStore store) : IObjectDeleter
{
    public Task DeleteAsync(string bucket, string key) => store.DeleteAsync(bucket, key);
}
