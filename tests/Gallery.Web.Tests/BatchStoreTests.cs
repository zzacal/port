using System.Net;
using Gallery.Core.Ids;
using Gallery.Web.Data;
using Gallery.Web.Observability;
using Gallery.Web.Tests.Fixtures;
using Gallery.Web.Upload;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Gallery.Web.Tests;

/// <summary>
/// 008 T012: <see cref="BatchStore"/> against real Mongo (research R-08; FR-007, US5 AS-3).
/// </summary>
[Collection("mongo")]
public class BatchStoreTests(MongoFixture mongo)
{
    private static BatchStore NewStore(IMongoDatabase db, TimeProvider? clock = null) =>
        new(db, new OpTimer(NullLogger<OpTimer>.Instance), clock ?? new MutableTimeProvider(), new UploadOptions());

    private static BatchItemDoc NewItem(string? attemptId = null) => new()
    {
        ItemId = OpaqueId.NewId(),
        AttemptId = attemptId ?? OpaqueId.NewId(),
        FileName = "a.jpg",
        FileSize = 1234,
        State = ItemStates.Queued,
        HeartbeatAt = DateTime.UtcNow,
    };

    private static Task<List<BsonDocument>> AllBatches(IMongoDatabase db) =>
        db.GetCollection<BsonDocument>(BatchStore.CollectionName).Find(FilterDefinition<BsonDocument>.Empty).ToListAsync();

    [Fact]
    public async Task Fifty_concurrent_GetOrCreateOpen_make_exactly_one_open_batch()
    {
        var db = mongo.NewDatabase();

        // One store per call, as fifty concurrent requests would each get their own scope.
        var results = await Task.WhenAll(Enumerable.Range(0, 50).Select(_ => NewStore(db).GetOrCreateOpenAsync()));

        var docs = await AllBatches(db);
        var doc = Assert.Single(docs);
        Assert.Equal("open", doc["state"].AsString);
        var id = doc["_id"].AsString;
        Assert.Equal(50, results.Count(r => r.Id == id));
    }

    [Fact]
    public async Task Fifty_concurrent_appends_of_three_stop_at_exactly_one_hundred()
    {
        var db = mongo.NewDatabase();
        var batch = await NewStore(db).GetOrCreateOpenAsync();

        var results = await Task.WhenAll(Enumerable.Range(0, 50).Select(_ =>
            NewStore(db).AppendAsync(batch.Id, [NewItem(), NewItem(), NewItem()])));

        var stored = await NewStore(db).GetByIdAsync(batch.Id);
        Assert.NotNull(stored);
        Assert.Equal(100, stored.Items.Count);
        Assert.Equal(100, results.Sum(r => r.Accepted.Count));
        Assert.Equal(50, results.Sum(r => r.Refused.Count));
        Assert.Equal(100, stored.Items.Select(i => i.ItemId).Distinct().Count());
    }

    [Fact]
    public async Task UpdateItem_with_a_stale_attempt_matches_nothing_and_changes_nothing()
    {
        var db = mongo.NewDatabase();
        var store = NewStore(db);
        var batch = await store.GetOrCreateOpenAsync();
        var item = NewItem();
        await store.AppendAsync(batch.Id, [item]);
        var before = (await AllBatches(db)).Single().ToJson();

        var matched = await store.UpdateItemAsync(batch.Id, item.ItemId, OpaqueId.NewId(),
            Builders<BatchDoc>.Update.Set("items.$.state", ItemStates.Processing).Set("items.$.percent", 50));

        Assert.False(matched);
        Assert.Equal(before, (await AllBatches(db)).Single().ToJson());
    }

    [Fact]
    public async Task UpdateItem_with_the_current_attempt_updates_that_item()
    {
        var db = mongo.NewDatabase();
        var store = NewStore(db);
        var batch = await store.GetOrCreateOpenAsync();
        var item = NewItem();
        await store.AppendAsync(batch.Id, [NewItem(), item]);

        var matched = await store.UpdateItemAsync(batch.Id, item.ItemId, item.AttemptId,
            Builders<BatchDoc>.Update.Set("items.$.state", ItemStates.Processing));

        Assert.True(matched);
        var stored = await store.GetByIdAsync(batch.Id);
        Assert.Equal([ItemStates.Queued, ItemStates.Processing], stored!.Items.Select(i => i.State));
    }

    [Fact]
    public async Task Append_after_TryStartConfirm_refuses_every_item()
    {
        var db = mongo.NewDatabase();
        var store = NewStore(db);
        var batch = await store.GetOrCreateOpenAsync();
        var item = NewItem();
        await store.AppendAsync(batch.Id, [item]);
        Assert.True(await store.UpdateItemAsync(batch.Id, item.ItemId, item.AttemptId,
            Builders<BatchDoc>.Update.Set("items.$.state", ItemStates.Succeeded)));
        Assert.True(await store.TryStartConfirmAsync(batch.Id));

        var result = await store.AppendAsync(batch.Id, [NewItem(), NewItem()]);

        Assert.Empty(result.Accepted);
        Assert.Equal(2, result.Refused.Count);
        var stored = await store.GetByIdAsync(batch.Id);
        Assert.Single(stored!.Items);
        Assert.NotNull(stored.ConfirmStartedAt);
    }

    [Fact]
    public async Task TryStartConfirm_refuses_an_empty_batch_and_one_with_an_unready_item()
    {
        var db = mongo.NewDatabase();
        var store = NewStore(db);
        var batch = await store.GetOrCreateOpenAsync();
        Assert.False(await store.TryStartConfirmAsync(batch.Id));

        await store.AppendAsync(batch.Id, [NewItem()]);
        Assert.False(await store.TryStartConfirmAsync(batch.Id));

        var stored = await store.GetByIdAsync(batch.Id);
        Assert.Null(stored!.ConfirmStartedAt);
    }

    [Fact]
    public async Task A_second_open_insert_throws_DuplicateKey_once_the_index_exists()
    {
        var db = mongo.NewDatabase();
        await NewStore(db).GetOrCreateOpenAsync();

        var indexes = await (await db.GetCollection<BsonDocument>(BatchStore.CollectionName).Indexes.ListAsync()).ToListAsync();
        Assert.Contains(indexes, i => i["name"].AsString == BatchStore.OpenIndexName);

        var ex = await Assert.ThrowsAsync<MongoWriteException>(() =>
            db.GetCollection<BatchDoc>(BatchStore.CollectionName).InsertOneAsync(new BatchDoc
            {
                Id = OpaqueId.NewId(), State = BatchStates.Open, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
            }));
        Assert.Equal(ServerErrorCategory.DuplicateKey, ex.WriteError.Category);
    }
}

/// <summary>008 T013: proves the <see cref="UploadHost"/> fixture once before later batches build on it.</summary>
[Collection("s3")]
public class UploadHostFixtureTests : IClassFixture<MongoFixture>, IAsyncLifetime
{
    private readonly S3ProxyFixture _s3;
    private readonly MongoFixture _mongo;
    private readonly FakeGoogle _google = new();

    public UploadHostFixtureTests(S3ProxyFixture s3, MongoFixture mongo)
    {
        _s3 = s3;
        _mongo = mongo;
    }

    public Task InitializeAsync() => _google.InitializeAsync();
    public Task DisposeAsync() => _google.DisposeAsync();

    [Fact]
    public async Task UploadHost_signs_in_and_caches_a_csrf_token()
    {
        await using var host = await UploadHost.CreateAsync(_mongo, _google, _s3);

        var resp = await host.GetAsync("/admin");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.False(string.IsNullOrEmpty(host.CsrfToken));
    }
}
