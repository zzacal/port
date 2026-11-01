using System.Collections.Concurrent;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Gallery.Web.Data;

/// <summary>
/// Every index the app relies on (research R-11, data-model "Indexes"). One place creates them all:
/// the partial unique <c>seedKey_1</c> on <c>images</c> and <c>galleries</c> (moved from the
/// retired seeder, with the legacy plain-index upgrade), the library page sort
/// <c>{createdAt:-1,_id:-1}</c> and the multikey shared-file indexes on
/// <c>derivatives.avif</c> / <c>derivatives.webp</c>. Creating an index that already exists with
/// the same keys and options is a server-side no-op, so <see cref="EnsureAsync"/> is repeatable.
/// </summary>
public static class Indexes
{
    public const string SeedKeyIndex = "seedKey_1";
    public const string LibraryPageIndex = "library_page";
    public const string AvifIndex = "derivatives.avif_1";
    public const string WebpIndex = "derivatives.webp_1";

    /// <summary>
    /// Creates (or upgrades from the pre-008 non-partial form) the partial unique <c>seedKey_1</c>
    /// index on images and galleries, then the 009 library indexes on images.
    /// </summary>
    public static async Task EnsureAsync(IMongoDatabase db, CancellationToken ct = default)
    {
        var imagesCol = db.GetCollection<BsonDocument>("images");
        var galleriesCol = db.GetCollection<BsonDocument>("galleries");

        // Partial unique index: only seeded documents carry seedKey. Owner-created documents
        // (uploads in 008, galleries in 009) omit it, and a plain unique index would treat every
        // missing field as null and refuse the second one (008 decisions row 60).
        var indexKeysDef = Builders<BsonDocument>.IndexKeys.Ascending("seedKey");
        var indexOptions = new CreateIndexOptions<BsonDocument>
        {
            Unique = true,
            Name = SeedKeyIndex,
            PartialFilterExpression = Builders<BsonDocument>.Filter.Exists("seedKey"),
        };

        await EnsureSeedKeyIndexAsync(imagesCol, indexKeysDef, indexOptions, ct);
        await EnsureSeedKeyIndexAsync(galleriesCol, indexKeysDef, indexOptions, ct);

        var keys = Builders<BsonDocument>.IndexKeys;
        await imagesCol.Indexes.CreateManyAsync(
        [
            new CreateIndexModel<BsonDocument>(
                keys.Descending("createdAt").Descending("_id"),
                new CreateIndexOptions { Name = LibraryPageIndex }),
            new CreateIndexModel<BsonDocument>(
                keys.Ascending("derivatives.avif"),
                new CreateIndexOptions { Name = AvifIndex }),
            new CreateIndexModel<BsonDocument>(
                keys.Ascending("derivatives.webp"),
                new CreateIndexOptions { Name = WebpIndex }),
        ], ct);
    }

    private static async Task EnsureSeedKeyIndexAsync(
        IMongoCollection<BsonDocument> col,
        IndexKeysDefinition<BsonDocument> keys,
        CreateIndexOptions<BsonDocument> options,
        CancellationToken ct)
    {
        try
        {
            await col.Indexes.CreateOneAsync(new CreateIndexModel<BsonDocument>(keys, options), cancellationToken: ct);
        }
        catch (MongoCommandException ex) when (ex.Code is 85 or 86)
        {
            // An older non-partial seedKey_1 exists (IndexOptionsConflict / IndexKeySpecsConflict):
            // replace it with the partial one.
            await col.Indexes.DropOneAsync(SeedKeyIndex, ct);
            await col.Indexes.CreateOneAsync(new CreateIndexModel<BsonDocument>(keys, options), cancellationToken: ct);
        }
    }

    // A dev database seeded before 008 has a non-partial unique seedKey_1 index, which refuses a
    // second document with no seedKey. Every owner write path ensures the indexes once per database
    // per process before its first write (008 decisions row 60, 009 decisions row 44). A failed
    // attempt is forgotten so the next caller retries.
    private static readonly ConcurrentDictionary<string, Lazy<Task>> Ensured = new();

    /// <summary>
    /// <see cref="EnsureAsync"/> once per database per process (keyed by server and database
    /// name); retried after a failure.
    /// </summary>
    public static async Task EnsureOnceAsync(IMongoDatabase db)
    {
        var key = $"{db.Client.Settings.Server}/{db.DatabaseNamespace.DatabaseName}";
        var lazy = Ensured.GetOrAdd(key, _ => new Lazy<Task>(() => EnsureAsync(db)));
        try { await lazy.Value; }
        catch
        {
            Ensured.TryRemove(new KeyValuePair<string, Lazy<Task>>(key, lazy));
            throw;
        }
    }
}
