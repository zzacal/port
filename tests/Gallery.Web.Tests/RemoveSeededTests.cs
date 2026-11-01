using Amazon.S3;
using Amazon.S3.Model;
using Gallery.Web.Data;
using Gallery.Web.Edge;
using Gallery.Web.Library;
using Gallery.Web.Maintenance;
using Gallery.Web.Observability;
using Gallery.Web.Storage;
using Gallery.Web.Tests.Fixtures;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Gallery.Web.Tests;

/// <summary>
/// 009 T046 [SAFETY] (US6, FR-045–FR-050, SC-010; contracts/removal-command.md): the removal
/// command refuses every non-development target before touching anything (exit 4, dumps identical),
/// names a missing setting (exit 2), deletes seeded content through the delete services, NEVER
/// touches owner content (no <c>seedKey</c>, or an empty one), keeps the owner's claim (I.4; moved
/// here from the retired <c>OwnerClaimSeedTests.Reseed_keeps_claim</c>, decisions row 27), prints
/// zeros on a second run and logs its timing and owner action. Each fact uses its own database and
/// its own buckets on S3Proxy; the environment is passed through overrides only.
/// </summary>
[Collection("s3")]
public class RemoveSeededTests(S3ProxyFixture s3, MongoFixture mongo) : IClassFixture<MongoFixture>
{
    private const string ImageBaseUrl = "https://img.test";

    // ── target ───────────────────────────────────────────────────────────────

    private sealed record Target(IMongoDatabase Db, string Pub, string Priv, Dictionary<string, string?> Overrides);

    /// <summary>
    /// A fresh database and two fresh buckets. The names are dev-shaped unless a check is meant to
    /// fail, in which case the data lives in the non-dev target the command is pointed at.
    /// </summary>
    private async Task<Target> NewTargetAsync(bool devDb = true, bool devBucket = true, string environment = "Development")
    {
        var id = Guid.NewGuid().ToString("N")[..20];
        var dbName = devDb ? $"gallery_rs_{Guid.NewGuid():N}_dev" : $"gallery_rs_{Guid.NewGuid():N}";
        var db = new MongoClient(mongo.ConnectionString).GetDatabase(dbName);
        var pub = devBucket ? $"gallery-dev-pub-{id}" : $"gallery-prod-pub-{id}";
        var priv = $"gallery-dev-priv-{id}";
        using (var client = Client())
        {
            await client.PutBucketAsync(pub);
            await client.PutBucketAsync(priv);
        }

        var so = s3.StorageOptions;
        var overrides = new Dictionary<string, string?>
        {
            ["ASPNETCORE_ENVIRONMENT"] = environment,
            ["Storage:ServiceUrl"] = so.ServiceUrl,
            ["Storage:AccessKeyId"] = so.AccessKeyId,
            ["Storage:SecretAccessKey"] = so.SecretAccessKey,
            ["Storage:PublicBucket"] = pub,
            ["Storage:PrivateBucket"] = priv,
            ["Storage:ImageBaseUrl"] = ImageBaseUrl,
            ["Mongo:ConnectionString"] = mongo.ConnectionString,
            ["Mongo:Database"] = dbName,
            ["Edge:SiteBaseUrl"] = LibraryHost.SiteBaseUrl,
        };
        return new Target(db, pub, priv, overrides);
    }

    private AmazonS3Client Client() => new S3ClientFactory(s3.StorageOptions).CreateClient();

    /// <summary>
    /// Runs the command with a recording edge provider (no real edge call; it also wins over any
    /// edge credential in user-secrets) and a log capture.
    /// </summary>
    private static async Task<(int Exit, string[] Output, List<string> Logs, RecordingEdgeProvider Recorder)> RunAsync(Target t)
    {
        var output = new StringWriter();
        var logs = new List<string>();
        var recorder = new RecordingEdgeProvider();
        var exit = await RemoveSeededCommand.RunAsync(["remove-seeded"], output, t.Overrides, b =>
        {
            b.Services.AddSingleton<IEdgeProvider>(recorder);
            b.Logging.AddProvider(new CaptureLoggerProvider(logs));
        });
        var lines = output.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.TrimEnd('\r')).ToArray();
        return (exit, lines, logs, recorder);
    }

    // ── data ─────────────────────────────────────────────────────────────────

    private sealed record World(
        ImageDoc[] Seeded, ImageDoc Upload, ImageDoc SharingUpload, ImageDoc EmptySeedKey,
        GalleryDoc[] SeededGalleries, GalleryDoc OwnerGallery, string SharedKey);

    private async Task PutFilesAsync(Target t, ImageDoc image, bool master)
    {
        using var client = Client();
        var puts = image.Derivatives.SelectMany(d => new[] { d.Avif, d.Webp }).Select(k => (t.Pub, k)).ToList();
        if (master) puts.Add((t.Priv, Gallery.Web.Upload.ConfirmService.MasterKey(image.Id)));
        foreach (var (bucket, key) in puts)
            await client.PutObjectAsync(new PutObjectRequest
            {
                BucketName = bucket,
                Key = key,
                ContentBody = key,
                DisableDefaultChecksumValidation = true,
            });
    }

    /// <summary>
    /// 3 seed-shaped images, 2 seed-shaped galleries, 2 uploaded images (one sharing a derivative
    /// key with a seeded image), 1 owner gallery holding a seeded and an uploaded image, an image
    /// with <c>seedKey: ""</c>, and a claimed site document.
    /// </summary>
    private async Task<World> BuildWorldAsync(Target t)
    {
        await Indexes.EnsureAsync(t.Db);

        var seeded = Enumerable.Range(0, 3).Select(_ => SeedShapes.SeededImage()).ToArray();
        var upload = SeedShapes.UploadedImage(title: "Owner upload");
        var sharing = SeedShapes.UploadedImage(title: "Owner twin");
        var sharedKey = seeded[0].Derivatives[0].Avif;
        sharing.Derivatives[0].Avif = sharedKey;
        var empty = SeedShapes.UploadedImage(title: "Empty seedKey");
        empty.SeedKey = "";

        var gA = SeedShapes.SeededGallery([seeded[0], seeded[1]], title: "Seeded A");
        var gB = SeedShapes.SeededGallery([seeded[2]], title: "Seeded B");
        var gO = SeedShapes.Gallery([seeded[1], upload], title: "Owner Gallery");
        Assert.Null(gO.SeedKey);
        var allImages = seeded.Concat([upload, sharing, empty]).ToList();
        SeedShapes.Link(allImages, [gA, gB, gO]);

        foreach (var i in seeded) await PutFilesAsync(t, i, master: false);
        await PutFilesAsync(t, upload, master: true);
        await PutFilesAsync(t, sharing, master: true);
        await PutFilesAsync(t, empty, master: true);

        await t.Db.GetCollection<ImageDoc>("images").InsertManyAsync(allImages);
        await t.Db.GetCollection<GalleryDoc>("galleries").InsertManyAsync([gA, gB, gO]);
        await t.Db.GetCollection<SiteDoc>("site").InsertOneAsync(SeedShapes.Site([gA, gB, gO]));

        var owner = new OwnerStore(t.Db, new OpTimer(NullLogger<OpTimer>.Instance), NullLogger<OwnerStore>.Instance);
        Assert.Equal(ClaimResult.Won, await owner.TryClaimAsync("owner-1", "127.0.0.1", DateTime.UtcNow));

        return new World(seeded, upload, sharing, empty, [gA, gB], gO, sharedKey);
    }

    // ── dumps ────────────────────────────────────────────────────────────────

    private static async Task<List<string>> DumpAsync(IMongoDatabase db, string collection) =>
        (await db.GetCollection<BsonDocument>(collection).Find(FilterDefinition<BsonDocument>.Empty)
            .Sort(Builders<BsonDocument>.Sort.Ascending("_id")).ToListAsync())
        .Select(d => d.ToJson()).ToList();

    private async Task<List<string>> DumpBucketAsync(string bucket)
    {
        using var client = Client();
        var entries = new List<string>();
        var request = new ListObjectsV2Request { BucketName = bucket };
        ListObjectsV2Response response;
        do
        {
            response = await client.ListObjectsV2Async(request);
            foreach (var o in response.S3Objects ?? [])
            {
                using var get = await client.GetObjectAsync(bucket, o.Key);
                using var reader = new StreamReader(get.ResponseStream);
                entries.Add($"{o.Key}={await reader.ReadToEndAsync()}");
            }
            request.ContinuationToken = response.NextContinuationToken;
        } while (response.IsTruncated == true);
        entries.Sort(StringComparer.Ordinal);
        return entries;
    }

    private async Task<List<string>> DumpAllAsync(Target t)
    {
        var all = new List<string>();
        foreach (var c in new[] { "images", "galleries", "site" })
            all.AddRange((await DumpAsync(t.Db, c)).Select(x => $"{c}: {x}"));
        all.AddRange((await DumpBucketAsync(t.Pub)).Select(x => $"pub: {x}"));
        all.AddRange((await DumpBucketAsync(t.Priv)).Select(x => $"priv: {x}"));
        return all;
    }

    private static async Task<long> SeededCountAsync(IMongoDatabase db, string collection) =>
        await db.GetCollection<BsonDocument>(collection).CountDocumentsAsync(RemoveSeededCommand.SeededFilter());

    // ── guard (exit 4, nothing touched) ──────────────────────────────────────

    private async Task AssertRefusedAsync(Target t, params string[] expectedChecks)
    {
        await BuildWorldAsync(t);
        var before = await DumpAllAsync(t);
        Assert.NotEmpty(before);

        var (exit, output, _, recorder) = await RunAsync(t);

        Assert.Equal(4, exit);
        Assert.Equal(expectedChecks.Select(c => $"Target guard failed: {c}").ToArray(), output);
        Assert.Equal(0, recorder.CallCount);
        Assert.Equal(before, await DumpAllAsync(t));
    }

    [Fact]
    public async Task Guard_refuses_non_development_environment()
    {
        var t = await NewTargetAsync(environment: "Production");
        await AssertRefusedAsync(t, "Environment must be Development (got: Production)");
    }

    [Fact]
    public async Task Guard_defaults_to_Production_when_environment_unset()
    {
        var t = await NewTargetAsync();
        t.Overrides["ASPNETCORE_ENVIRONMENT"] = null;
        // A null override hides any process value (the last source wins), so the default applies.
        await AssertRefusedAsync(t, "Environment must be Development (got: Production)");
    }

    [Fact]
    public async Task Guard_refuses_non_dev_public_bucket()
    {
        var t = await NewTargetAsync(devBucket: false);
        await AssertRefusedAsync(t, $"Storage:PublicBucket must start with gallery-dev- (got: {t.Pub})");
    }

    [Fact]
    public async Task Guard_refuses_non_dev_database()
    {
        var t = await NewTargetAsync(devDb: false);
        await AssertRefusedAsync(t, $"Mongo:Database must end with _dev (got: {t.Overrides["Mongo:Database"]})");
    }

    [Fact]
    public async Task Guard_refuses_all_three_and_names_each()
    {
        var t = await NewTargetAsync(devDb: false, devBucket: false, environment: "Staging");
        await AssertRefusedAsync(t,
            "Environment must be Development (got: Staging)",
            $"Storage:PublicBucket must start with gallery-dev- (got: {t.Pub})",
            $"Mongo:Database must end with _dev (got: {t.Overrides["Mongo:Database"]})");
    }

    [Fact]
    public void DevTargetGuard_passes_only_a_dev_target()
    {
        var s = new StorageOptions { PublicBucket = "gallery-dev-public" };
        var m = new MongoOptions { Database = "gallery_dev" };
        Assert.Empty(DevTargetGuard.Check("Development", s, m));
        Assert.Single(DevTargetGuard.Check("Production", s, m));
        Assert.Single(DevTargetGuard.Check("Development", new StorageOptions { PublicBucket = "gallery-public" }, m));
        Assert.Single(DevTargetGuard.Check("Development", new StorageOptions(), m));
        Assert.Single(DevTargetGuard.Check("Development", s, new MongoOptions { Database = "gallery" }));
        Assert.Single(DevTargetGuard.Check("Development", s, new MongoOptions { Database = "gallery_dev_x" }));
    }

    // ── missing setting (exit 2) ─────────────────────────────────────────────

    [Fact]
    public async Task Missing_mongo_connection_string_exits_2_naming_the_key()
    {
        var t = await NewTargetAsync();
        t.Overrides["Mongo:ConnectionString"] = null;
        var (exit, output, _, _) = await RunAsync(t);

        Assert.Equal(2, exit);
        Assert.Equal(["Missing required setting: Mongo:ConnectionString"], output);
    }

    // ── removal (exit 0) ─────────────────────────────────────────────────────

    [Fact]
    public async Task Removes_seeded_and_never_touches_owner_content()
    {
        var t = await NewTargetAsync();
        var w = await BuildWorldAsync(t);
        var images = t.Db.GetCollection<BsonDocument>("images");
        var galleries = t.Db.GetCollection<BsonDocument>("galleries");
        var siteCol = t.Db.GetCollection<BsonDocument>("site");

        async Task<string> RawAsync(IMongoCollection<BsonDocument> c, string id) =>
            (await c.Find(Builders<BsonDocument>.Filter.Eq("_id", id)).FirstAsync()).ToJson();

        var uploadBefore = await RawAsync(images, w.Upload.Id);
        var sharingBefore = await RawAsync(images, w.SharingUpload.Id);
        var emptyBefore = await RawAsync(images, w.EmptySeedKey.Id);
        var ownerGalleryBefore = await galleries.Find(Builders<BsonDocument>.Filter.Eq("_id", w.OwnerGallery.Id)).FirstAsync();
        var siteBefore = await siteCol.Find(Builders<BsonDocument>.Filter.Eq("_id", "site")).FirstAsync();
        var claimedAtBefore = siteBefore["claimedAt"].ToUniversalTime();
        var claimedIpBefore = siteBefore["claimedIp"].AsString;
        var ownerKeys = new[] { w.Upload, w.SharingUpload, w.EmptySeedKey }
            .SelectMany(i => i.Derivatives).SelectMany(d => new[] { d.Avif, d.Webp }).ToHashSet();
        var seededKeys = w.Seeded.SelectMany(i => i.Derivatives).SelectMany(d => new[] { d.Avif, d.Webp }).ToHashSet();

        var (exit, output, logs, recorder) = await RunAsync(t);

        Assert.Equal(0, exit);
        var perImage = SeedShapes.Widths().Count * 2;
        Assert.Equal(
        [
            "seeded galleries removed: 2",
            "seeded images removed: 3",
            "owner galleries changed: 1",
            $"public files deleted: {3 * perImage - 1}",
            "public files kept (shared with an uploaded photo): 1",
            "leftover files (logged): 0",
            "The running app drops its page cache within 60 seconds.",
        ], output);

        // Nothing seeded is left; the selection never matched owner content.
        Assert.Equal(0, await SeededCountAsync(t.Db, "images"));
        Assert.Equal(0, await SeededCountAsync(t.Db, "galleries"));
        Assert.Equal(3, await images.CountDocumentsAsync(FilterDefinition<BsonDocument>.Empty));
        Assert.Equal(1, await galleries.CountDocumentsAsync(FilterDefinition<BsonDocument>.Empty));

        // [SAFETY] owner images (no seedKey, and seedKey "") are byte-identical.
        Assert.Equal(uploadBefore, await RawAsync(images, w.Upload.Id));
        Assert.Equal(sharingBefore, await RawAsync(images, w.SharingUpload.Id));
        Assert.Equal(emptyBefore, await RawAsync(images, w.EmptySeedKey.Id));

        // [SAFETY] the owner gallery lost only its seeded member.
        var ownerGallery = await t.Db.GetCollection<GalleryDoc>("galleries").Find(g => g.Id == w.OwnerGallery.Id).FirstAsync();
        Assert.Equal([w.Upload.Id], ownerGallery.Items);
        Assert.Null(ownerGallery.SeedKey);
        var ownerGalleryAfter = await galleries.Find(Builders<BsonDocument>.Filter.Eq("_id", w.OwnerGallery.Id)).FirstAsync();
        foreach (var field in ownerGalleryBefore.Names.Except(["items", "card", "updatedAt"]))
            Assert.Equal(ownerGalleryBefore[field], ownerGalleryAfter[field]);
        Assert.False(ownerGalleryAfter.Contains("seedKey"));

        // homeCards recomputed from the surviving gallery; the claim is unchanged (I.4).
        var siteAfter = await siteCol.Find(Builders<BsonDocument>.Filter.Eq("_id", "site")).FirstAsync();
        Assert.Equal(new BsonArray { w.OwnerGallery.Id }, siteAfter["homeOrder"].AsBsonArray);
        var expectedCards = SiteProjector.Cards([ownerGallery.Id], [ownerGallery]);
        Assert.Equal(
            new BsonArray(expectedCards.Select(c => c.ToBsonDocument())),
            siteAfter["homeCards"].AsBsonArray);
        Assert.Equal("owner-1", siteAfter["claimedSubject"].AsString);
        Assert.Equal(claimedAtBefore, siteAfter["claimedAt"].ToUniversalTime());
        Assert.Equal(claimedIpBefore, siteAfter["claimedIp"].AsString);
        await InvariantChecker.AssertAsync(t.Db);

        // Storage: every owner file stays, including the shared key; every other seeded key is gone.
        var pub = (await DumpBucketAsync(t.Pub)).Select(e => e[..e.IndexOf('=')]).ToHashSet();
        Assert.Subset(pub, ownerKeys);
        Assert.Contains(w.SharedKey, pub);
        Assert.Empty(pub.Intersect(seededKeys.Except([w.SharedKey])));
        var priv = (await DumpBucketAsync(t.Priv)).Select(e => e[..e.IndexOf('=')]).ToHashSet();
        Assert.Equal(
            new[] { w.Upload, w.SharingUpload, w.EmptySeedKey }.Select(i => Gallery.Web.Upload.ConfirmService.MasterKey(i.Id)).ToHashSet(),
            priv);

        // The purge went through the configured provider, never naming the kept shared file.
        var purged = recorder.AllPurged();
        Assert.Contains($"{ImageBaseUrl}/{seededKeys.Except([w.SharedKey]).First()}", purged);
        Assert.DoesNotContain($"{ImageBaseUrl}/{w.SharedKey}", purged);

        // FR-049 / FR-050: one owner action with the counts, one timing line.
        var action = Assert.Single(logs, l => l.StartsWith("owner.action seeded.remove ", StringComparison.Ordinal));
        Assert.Equal(
            $"owner.action seeded.remove seeded 5 galleries=2 images=3 ownerGalleries=1 deleted={3 * perImage - 1} kept=1 leftovers=0",
            action);
        Assert.Single(logs, l => l.StartsWith("op.timing maintenance.remove-seeded ", StringComparison.Ordinal) && l.EndsWith(" ok", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Second_run_prints_zeros()
    {
        var t = await NewTargetAsync();
        await BuildWorldAsync(t);
        var (first, _, _, _) = await RunAsync(t);
        Assert.Equal(0, first);
        var afterFirst = await DumpAllAsync(t);

        var (exit, output, _, _) = await RunAsync(t);

        Assert.Equal(0, exit);
        Assert.Equal(
        [
            "seeded galleries removed: 0",
            "seeded images removed: 0",
            "owner galleries changed: 0",
            "public files deleted: 0",
            "public files kept (shared with an uploaded photo): 0",
            "leftover files (logged): 0",
            "The running app drops its page cache within 60 seconds.",
        ], output);
        Assert.Equal(afterFirst, await DumpAllAsync(t));
    }
}
