using Amazon.S3;
using Amazon.Runtime;

namespace Gallery.Web.Storage;

/// <summary>
/// The ONLY place an AmazonS3Client is built (R-02). Reads settings from StorageOptions.
/// </summary>
public sealed class S3ClientFactory
{
    public S3ClientFactory(StorageOptions options)
    {
        Options = options;
        IsHttps = !string.IsNullOrWhiteSpace(options.ServiceUrl) &&
                  options.ServiceUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
    }

    public StorageOptions Options { get; }

    /// <summary>True when the ServiceUrl scheme is HTTPS.</summary>
    public bool IsHttps { get; }

    public AmazonS3Client CreateClient()
    {
        var config = new AmazonS3Config
        {
            ServiceURL = Options.ServiceUrl,
            ForcePathStyle = true,
            AuthenticationRegion = "auto",
            RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
            ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED,
        };

        var credentials = new BasicAWSCredentials(Options.AccessKeyId, Options.SecretAccessKey);
        return new AmazonS3Client(credentials, config);
    }
}
