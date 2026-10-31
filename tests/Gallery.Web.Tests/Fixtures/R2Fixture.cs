using Gallery.Web.Storage;
using Microsoft.Extensions.Configuration;

namespace Gallery.Web.Tests.Fixtures;

/// <summary>
/// Reads real R2 credentials from user-secrets and exposes a ready-to-use StorageOptions.
/// Tests are only executed when the environment variable GALLERY_R2_TESTS=1.
/// </summary>
public sealed class R2Fixture
{
    public bool Enabled { get; }
    public StorageOptions StorageOptions { get; }

    public R2Fixture()
    {
        Enabled = Environment.GetEnvironmentVariable("GALLERY_R2_TESTS") == "1";

        var config = new ConfigurationBuilder()
            .AddUserSecrets(typeof(Gallery.Web.Storage.StorageOptions).Assembly)
            .Build();

        // Read the dev bucket names from appsettings (non-secret).
        StorageOptions = new StorageOptions
        {
            ServiceUrl    = config["Storage:ServiceUrl"],
            AccessKeyId   = config["Storage:AccessKeyId"],
            SecretAccessKey = config["Storage:SecretAccessKey"],
            PublicBucket  = "gallery-dev-public",
            PrivateBucket = "gallery-dev-private",
            ImageBaseUrl  = config["Storage:ImageBaseUrl"] ?? "https://img-dev.zacal.me",
        };
    }
}
