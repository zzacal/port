using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Gallery.Web.Storage;

namespace Gallery.Web.Tests.Fixtures;

[CollectionDefinition("s3")]
public class S3Collection : ICollectionFixture<S3ProxyFixture> { }

/// <summary>
/// One S3Proxy container shared across the "s3" test collection, exposing buckets "pub" and "priv".
/// Digest pinned to ensure the presign behaviour (T013 safety check) is reliable.
/// </summary>
public sealed class S3ProxyFixture : IAsyncLifetime
{
    private const string Image =
        "andrewgaul/s3proxy@sha256:87662b2a5afcdfa5f478a1c61650bae4c87bbd15f6ff2823236bcfd66ef7fe04";

    private IContainer? _container;
    private int _mappedPort;

    public StorageOptions StorageOptions { get; private set; } = default!;

    public async Task InitializeAsync()
    {
        _container = new ContainerBuilder(Image)
            .WithPortBinding(80, true)
            .WithEnvironment("S3PROXY_AUTHORIZATION", "aws-v2-or-v4")
            .WithEnvironment("S3PROXY_IDENTITY", "local")
            .WithEnvironment("S3PROXY_CREDENTIAL", "localsecret")
            .WithEnvironment("JCLOUDS_PROVIDER", "transient")
            // An unauthenticated GET / answers 403 once S3Proxy is up; the default 2xx check would wait forever.
            .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r =>
                r.ForPort(80).ForStatusCode(System.Net.HttpStatusCode.Forbidden)))
            .Build();

        await _container.StartAsync();
        _mappedPort = _container.GetMappedPublicPort(80);

        StorageOptions = new StorageOptions
        {
            ServiceUrl = $"http://localhost:{_mappedPort}",
            AccessKeyId = "local",
            SecretAccessKey = "localsecret",
            PublicBucket = "pub",
            PrivateBucket = "priv",
            ImageBaseUrl = "https://img.test",
        };

        // Create the two buckets.
        var factory = new S3ClientFactory(StorageOptions);
        var client = factory.CreateClient();
        await client.PutBucketAsync("pub");
        await client.PutBucketAsync("priv");
    }

    public async Task DisposeAsync()
    {
        if (_container is not null)
            await _container.DisposeAsync();
    }
}
