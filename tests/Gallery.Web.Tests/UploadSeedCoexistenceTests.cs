using Gallery.Web.Data;
using Gallery.Web.Seed;
using Gallery.Web.Storage;
using Gallery.Web.Tests.Fixtures;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Gallery.Web.Tests;

/// <summary>
/// 008 T007 [SAFETY] (FR-035, plan item 7, research R-10): an uploaded image carries no
/// <c>seedKey</c>/<c>sourceHash</c> element, so a seed run's clean-up (step 5e: delete images whose
/// seedKey exists and is not in the manifest) leaves it untouched. Same class shape, fixtures and
/// Seeder construction as <see cref="SeederWithS3Tests"/>.
/// </summary>
[Collection("s3")]
public class UploadSeedCoexistenceTests : IClassFixture<MongoFixture>
{
    private readonly MongoFixture _mongo;
    private readonly S3ProxyFixture _s3;

    public UploadSeedCoexistenceTests(MongoFixture mongo, S3ProxyFixture s3)
    {
        _mongo = mongo;
        _s3 = s3;
    }

    /// <summary>An ImageDoc shaped as confirm writes it (data-model "Library image"): no seed fields.</summary>
    private static ImageDoc UploadedImage(string id) => new()
    {
        Id = id,
        Title = "IMG_0001",
        Width = 400,
        Height = 200,
        Placeholder = new PlaceholderDoc { Color = "#a1b2c3", Preview = "data:image/webp;base64,AAAA" },
        Derivatives =
        [
            new DerivativeDoc { W = 320, Avif = "0123456789abcdef.avif", Webp = "fedcba9876543210.webp", BytesAvif = 1000, BytesWebp = 1200 },
        ],
        GalleryIds = [],
        CreatedAt = new DateTime(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc),
    };

    [Fact]
    public void ImageDoc_without_seed_fields_serialises_no_seedKey()
    {
        var bson = UploadedImage(Gallery.Core.Ids.OpaqueId.NewId()).ToBsonDocument();

        Assert.False(bson.Contains("seedKey"));
        Assert.False(bson.Contains("sourceHash"));
    }

    [Fact]
    public async Task Seed_run_leaves_an_uploaded_image_untouched()
    {
        var db = _mongo.NewDatabase();
        var images = db.GetCollection<BsonDocument>("images");

        var uploadedId = Gallery.Core.Ids.OpaqueId.NewId();
        await db.GetCollection<ImageDoc>("images").InsertOneAsync(UploadedImage(uploadedId));
        var before = (await images.Find(Builders<BsonDocument>.Filter.Eq("_id", uploadedId)).SingleAsync()).ToJson();

        // Control: the pre-008 shape (seedKey "") IS deleted by the same run, which proves the run
        // reached the clean-up step that would otherwise have removed the uploaded image.
        var controlId = Gallery.Core.Ids.OpaqueId.NewId();
        await images.InsertOneAsync(new BsonDocument { ["_id"] = controlId, ["seedKey"] = "", ["title"] = "old shape" });

        var tmpDir = Path.Combine(Path.GetTempPath(), $"gallery-coexist-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmpDir);
        try
        {
            var manifest = new Manifest
            {
                Galleries = [new ManifestGallery { Key = "gallery-1", Title = "Gallery One", Visibility = "public", Photos = ["photo-a.jpg"] }],
                Photos = new Dictionary<string, ManifestPhoto> { ["photo-a.jpg"] = new ManifestPhoto { Title = "Photo One" } },
            };
            var seeder = new Seeder(manifest, tmpDir, "/dev/null", NewStore(), db, NullLogger.Instance, _s3.StorageOptions.PublicBucket!);
            var summary = await seeder.SeedAsync();

            Assert.Equal(1, summary.ImagesDeleted);
            Assert.Null(await images.Find(Builders<BsonDocument>.Filter.Eq("_id", controlId)).FirstOrDefaultAsync());

            var after = await images.Find(Builders<BsonDocument>.Filter.Eq("_id", uploadedId)).FirstOrDefaultAsync();
            Assert.NotNull(after);
            Assert.Equal(before, after.ToJson());
        }
        finally { if (Directory.Exists(tmpDir)) Directory.Delete(tmpDir, true); }
    }

    /// <summary>
    /// 008 decisions row 60: a dev database seeded before 008 has a plain unique <c>seedKey_1</c>
    /// index, which treats every missing seedKey as null. With two uploaded images present the
    /// seed run must replace it with the partial index and leave both images untouched.
    /// </summary>
    [Fact]
    public async Task Seed_run_replaces_legacy_seedKey_index_and_keeps_two_uploads()
    {
        var db = _mongo.NewDatabase();
        var images = db.GetCollection<BsonDocument>("images");
        await images.Indexes.CreateOneAsync(new CreateIndexModel<BsonDocument>(
            Builders<BsonDocument>.IndexKeys.Ascending("seedKey"),
            new CreateIndexOptions { Unique = true, Name = "seedKey_1" }));

        var id1 = Gallery.Core.Ids.OpaqueId.NewId();
        await db.GetCollection<ImageDoc>("images").InsertOneAsync(UploadedImage(id1));
        // The legacy index refuses a second document with no seedKey.
        await Assert.ThrowsAsync<MongoWriteException>(() =>
            db.GetCollection<ImageDoc>("images").InsertOneAsync(UploadedImage(Gallery.Core.Ids.OpaqueId.NewId())));

        var tmpDir = Path.Combine(Path.GetTempPath(), $"gallery-coexist-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmpDir);
        try
        {
            var manifest = new Manifest
            {
                Galleries = [new ManifestGallery { Key = "gallery-1", Title = "Gallery One", Visibility = "public", Photos = ["photo-a.jpg"] }],
                Photos = new Dictionary<string, ManifestPhoto> { ["photo-a.jpg"] = new ManifestPhoto { Title = "Photo One" } },
            };
            var seeder = new Seeder(manifest, tmpDir, "/dev/null", NewStore(), db, NullLogger.Instance, _s3.StorageOptions.PublicBucket!);
            await seeder.SeedAsync();

            var idx = (await (await images.Indexes.ListAsync()).ToListAsync())
                .Single(i => i["name"] == "seedKey_1");
            Assert.True(idx["unique"].AsBoolean);
            Assert.True(idx.Contains("partialFilterExpression"), $"seedKey_1 is not partial: {idx}");
            Assert.Equal(new BsonDocument("seedKey", new BsonDocument("$exists", true)), idx["partialFilterExpression"].AsBsonDocument);

            var id2 = Gallery.Core.Ids.OpaqueId.NewId();
            await db.GetCollection<ImageDoc>("images").InsertOneAsync(UploadedImage(id2));
            Assert.Equal(2, await images.CountDocumentsAsync(Builders<BsonDocument>.Filter.In("_id", new[] { id1, id2 })));
        }
        finally { if (Directory.Exists(tmpDir)) Directory.Delete(tmpDir, true); }
    }

    private ImageStore NewStore() =>
        new(new Gallery.Web.Storage.S3ClientFactory(_s3.StorageOptions),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<ImageStore>.Instance);
}
