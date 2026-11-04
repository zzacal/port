using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using MongoDB.Driver;
using Testcontainers.MongoDb;

namespace Gallery.Web.Tests.Fixtures;

[CollectionDefinition("mongo")]
public class MongoCollection : ICollectionFixture<MongoFixture> { }

/// <summary>
/// One MongoDB container shared across the "mongo" test collection.
/// </summary>
public sealed class MongoFixture : IAsyncLifetime
{
    /// <summary>Test Mongo image; CI overrides it with GALLERY_MONGO_IMAGE (mongo:8 refuses some runner kernels).</summary>
    internal static readonly string Image =
        Environment.GetEnvironmentVariable("GALLERY_MONGO_IMAGE") is { Length: > 0 } i ? i : "mongo:8";

    /// <summary>
    /// Builds and starts a Mongo container, retrying a container that exits during startup. The
    /// image's entrypoint runs a temporary mongod to create the user, then starts the real one; on
    /// a loaded runner the real one can find 27017 still held and exit 48 ("Address already in
    /// use", seen on CI run 41926209399). A fresh container is the fix, not a longer wait.
    /// </summary>
    internal static async Task<MongoDbContainer> StartContainerAsync()
    {
        const int attempts = 3;
        for (var attempt = 1; ; attempt++)
        {
            var container = new MongoDbBuilder(Image).Build();
            try
            {
                await container.StartAsync();
                return container;
            }
            catch (ContainerNotRunningException) when (attempt < attempts)
            {
                await container.DisposeAsync();
            }
        }
    }

    private MongoDbContainer? _container;

    public async Task InitializeAsync() => _container = await StartContainerAsync();

    public async Task DisposeAsync()
    {
        if (_container is not null) await _container.DisposeAsync();
    }

    /// <summary>Returns a fresh <c>gallery_test_&lt;guid&gt;</c> database for isolated tests.</summary>
    public IMongoDatabase NewDatabase()
    {
        var client = new MongoClient(ConnectionString);
        var dbName = $"gallery_test_{Guid.NewGuid():N}";
        return client.GetDatabase(dbName);
    }

    /// <summary>The connection string for the running Mongo container.</summary>
    public string ConnectionString =>
        (_container ?? throw new InvalidOperationException("Mongo container not started")).GetConnectionString();
}
