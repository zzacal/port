using Gallery.Web.Data;
using Gallery.Web.Tests.Fixtures;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Gallery.Web.Tests;

/// <summary>
/// [SAFETY] 009 T006 (FR-048, research R-11, 008 decisions row 60): owner-created documents carry
/// no <c>seedKey</c>, so the <c>seedKey_1</c> unique index MUST be partial, or the second such
/// document fails with DuplicateKey. Tested with TWO documents per collection, and with a
/// pre-existing legacy plain index that <see cref="Indexes.EnsureAsync"/> must upgrade.
/// </summary>
[Collection("mongo")]
public class IndexesTests(MongoFixture mongo)
{
    private static readonly BsonDocument PartialSeedKey = new("seedKey", new BsonDocument("$exists", true));

    private static async Task<Dictionary<string, BsonDocument>> IndexesOf(IMongoDatabase db, string collection)
    {
        var list = await (await db.GetCollection<BsonDocument>(collection).Indexes.ListAsync()).ToListAsync();
        return list.ToDictionary(i => i["name"].AsString);
    }

    [Fact]
    public async Task Two_images_and_two_galleries_without_seedKey_insert_after_EnsureAsync()
    {
        var db = mongo.NewDatabase();
        await Indexes.EnsureAsync(db);

        var images = db.GetCollection<ImageDoc>("images");
        await images.InsertOneAsync(SeedShapes.UploadedImage());
        await images.InsertOneAsync(SeedShapes.UploadedImage());

        var galleries = db.GetCollection<GalleryDoc>("galleries");
        await galleries.InsertOneAsync(SeedShapes.Gallery([], title: "One", visibility: "private"));
        await galleries.InsertOneAsync(SeedShapes.Gallery([], title: "Two", visibility: "private"));

        Assert.Equal(2, await images.CountDocumentsAsync(FilterDefinition<ImageDoc>.Empty));
        Assert.Equal(2, await galleries.CountDocumentsAsync(FilterDefinition<GalleryDoc>.Empty));
        Assert.Equal(0, await images.CountDocumentsAsync(Builders<ImageDoc>.Filter.Exists("seedKey")));
        Assert.Equal(0, await galleries.CountDocumentsAsync(Builders<GalleryDoc>.Filter.Exists("seedKey")));
    }

    [Fact]
    public async Task Seed_keys_stay_unique_among_seeded_documents()
    {
        var db = mongo.NewDatabase();
        await Indexes.EnsureAsync(db);

        var galleries = db.GetCollection<GalleryDoc>("galleries");
        await galleries.InsertOneAsync(SeedShapes.SeededGallery([], seedKey: "same"));
        var ex = await Assert.ThrowsAsync<MongoWriteException>(() =>
            galleries.InsertOneAsync(SeedShapes.SeededGallery([], seedKey: "same")));
        Assert.Equal(ServerErrorCategory.DuplicateKey, ex.WriteError.Category);
    }

    [Theory]
    [InlineData("images")]
    [InlineData("galleries")]
    public async Task Legacy_plain_seedKey_index_is_replaced_by_the_partial_form(string collection)
    {
        var db = mongo.NewDatabase();
        var col = db.GetCollection<BsonDocument>(collection);
        await col.Indexes.CreateOneAsync(new CreateIndexModel<BsonDocument>(
            Builders<BsonDocument>.IndexKeys.Ascending("seedKey"),
            new CreateIndexOptions { Unique = true, Name = "seedKey_1" }));
        Assert.False((await IndexesOf(db, collection))["seedKey_1"].Contains("partialFilterExpression"));

        await Indexes.EnsureAsync(db);

        var idx = (await IndexesOf(db, collection))["seedKey_1"];
        Assert.Equal(new BsonDocument("seedKey", 1), idx["key"].AsBsonDocument);
        Assert.True(idx["unique"].AsBoolean);
        Assert.Equal(PartialSeedKey, idx["partialFilterExpression"].AsBsonDocument);

        // And the upgrade is real: two documents without seedKey now insert.
        await col.InsertOneAsync(new BsonDocument("_id", "a"));
        await col.InsertOneAsync(new BsonDocument("_id", "b"));
        Assert.Equal(2, await col.CountDocumentsAsync(FilterDefinition<BsonDocument>.Empty));
    }

    [Fact]
    public async Task The_three_009_indexes_exist_and_a_second_EnsureAsync_is_a_no_op()
    {
        var db = mongo.NewDatabase();
        await Indexes.EnsureAsync(db);

        var images = await IndexesOf(db, "images");
        Assert.Equal(new BsonDocument { { "createdAt", -1 }, { "_id", -1 } }, images["library_page"]["key"].AsBsonDocument);
        Assert.Equal(new BsonDocument("derivatives.avif", 1), images["derivatives.avif_1"]["key"].AsBsonDocument);
        Assert.Equal(new BsonDocument("derivatives.webp", 1), images["derivatives.webp_1"]["key"].AsBsonDocument);
        Assert.Equal(PartialSeedKey, images["seedKey_1"]["partialFilterExpression"].AsBsonDocument);
        Assert.Equal(
            new[] { "_id_", "derivatives.avif_1", "derivatives.webp_1", "library_page", "seedKey_1" },
            images.Keys.Order(StringComparer.Ordinal).ToArray());

        var galleries = await IndexesOf(db, "galleries");
        Assert.Equal(new[] { "_id_", "seedKey_1" }, galleries.Keys.Order(StringComparer.Ordinal).ToArray());
        Assert.Equal(PartialSeedKey, galleries["seedKey_1"]["partialFilterExpression"].AsBsonDocument);

        await Indexes.EnsureAsync(db);

        Assert.Equal(
            images.ToDictionary(kv => kv.Key, kv => kv.Value.ToJson()),
            (await IndexesOf(db, "images")).ToDictionary(kv => kv.Key, kv => kv.Value.ToJson()));
        Assert.Equal(
            galleries.ToDictionary(kv => kv.Key, kv => kv.Value.ToJson()),
            (await IndexesOf(db, "galleries")).ToDictionary(kv => kv.Key, kv => kv.Value.ToJson()));
    }

    [Fact]
    public async Task EnsureOnceAsync_creates_the_indexes()
    {
        var db = mongo.NewDatabase();
        await Indexes.EnsureOnceAsync(db);
        await Indexes.EnsureOnceAsync(db);

        var images = await IndexesOf(db, "images");
        Assert.Equal(
            new[] { "_id_", "derivatives.avif_1", "derivatives.webp_1", "library_page", "seedKey_1" },
            images.Keys.Order(StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void GalleryDoc_without_seed_key_serialises_no_seedKey_element()
    {
        var bson = new GalleryDoc { Id = "g", Slug = "s", Title = "T", Items = ["a"] }.ToBsonDocument();
        Assert.False(bson.Contains("seedKey"));

        var seeded = new GalleryDoc { Id = "g", SeedKey = "k" }.ToBsonDocument();
        Assert.Equal("k", seeded["seedKey"].AsString);
    }

    [Fact]
    public void ImageDoc_without_DeletingAt_serialises_no_deletingAt_element()
    {
        var bson = new ImageDoc { Id = "i", Title = "T" }.ToBsonDocument();
        Assert.False(bson.Contains("deletingAt"));

        var at = new DateTime(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);
        var tomb = new ImageDoc { Id = "i", DeletingAt = at }.ToBsonDocument();
        Assert.Equal(at, tomb["deletingAt"].ToUniversalTime());
    }
}
