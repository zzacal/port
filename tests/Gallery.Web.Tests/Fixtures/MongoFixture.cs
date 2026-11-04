using DotNet.Testcontainers.Builders;
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

    private readonly MongoDbContainer _container =
        new MongoDbBuilder(Image)
            .Build();

    public async Task InitializeAsync() => await _container.StartAsync();
    public async Task DisposeAsync() => await _container.DisposeAsync();

    /// <summary>Returns a fresh <c>gallery_test_&lt;guid&gt;</c> database for isolated tests.</summary>
    public IMongoDatabase NewDatabase()
    {
        var client = new MongoClient(_container.GetConnectionString());
        var dbName = $"gallery_test_{Guid.NewGuid():N}";
        return client.GetDatabase(dbName);
    }

    /// <summary>The connection string for the running Mongo container.</summary>
    public string ConnectionString => _container.GetConnectionString();
}
