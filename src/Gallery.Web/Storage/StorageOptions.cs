namespace Gallery.Web.Storage;

public sealed class StorageOptions
{
    public string? ServiceUrl { get; set; }
    public string? AccessKeyId { get; set; }
    public string? SecretAccessKey { get; set; }
    public string? PublicBucket { get; set; }
    public string? PrivateBucket { get; set; }
    public string? ImageBaseUrl { get; set; }

    /// <summary>Returns the names of required settings that are null or empty. Never returns values.</summary>
    public IReadOnlyList<string> Validate()
    {
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(ServiceUrl))    missing.Add("Storage:ServiceUrl");
        if (string.IsNullOrWhiteSpace(AccessKeyId))   missing.Add("Storage:AccessKeyId");
        if (string.IsNullOrWhiteSpace(SecretAccessKey)) missing.Add("Storage:SecretAccessKey");
        if (string.IsNullOrWhiteSpace(PublicBucket))  missing.Add("Storage:PublicBucket");
        if (string.IsNullOrWhiteSpace(PrivateBucket)) missing.Add("Storage:PrivateBucket");
        if (string.IsNullOrWhiteSpace(ImageBaseUrl))  missing.Add("Storage:ImageBaseUrl");
        return missing;
    }
}
