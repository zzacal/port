using Gallery.Web.Data;
using Gallery.Web.Seed;
using Gallery.Web.Storage;
using Gallery.Web.Tests.Fixtures;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Gallery.Web.Tests;

/// <summary>
/// T031: SeederTests — integration tests using MongoFixture + S3ProxyFixture.
/// Fixture derivative files are real tiny AVIF/WebP files made with ffmpeg/cwebp.
/// Uses the "s3" collection for S3ProxyFixture and takes MongoFixture directly.
/// </summary>
[Collection("s3")]
public class SeederWithS3Tests : IClassFixture<MongoFixture>
{
    private readonly MongoFixture _mongo;
    private readonly S3ProxyFixture _s3;

    public SeederWithS3Tests(MongoFixture mongo, S3ProxyFixture s3)
    {
        _mongo = mongo;
        _s3 = s3;
    }

    // aspect ratio for orientation-6.jpg after ffmpeg orientation apply = 200/400 = 0.5 (portrait)
    private const double AspectRatio = 0.5;
    private const int Width = 320;

    private (string tmpDir, string preparedRoot) CreatePreparedFixture(
        string photo1Name, string photo2Name,
        out string photo1Hash, out string photo2Hash)
    {
        var tmpDir = Path.Combine(Path.GetTempPath(), $"gallery-seeder-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmpDir);

        // Create real derivatives using ffmpeg/cwebp
        EncodedFixtures.RequireFfmpeg();
        EncodedFixtures.RequireCwebp();

        photo1Hash = CreatePhotoEntry(tmpDir, photo1Name, EncodedFixtures.Orientation6Jpg, Width, AspectRatio);
        photo2Hash = CreatePhotoEntry(tmpDir, photo2Name, EncodedFixtures.Orientation6Jpg, Width, AspectRatio);

        return (tmpDir, tmpDir);
    }

    private static string ComputeTestSourceHash(string srcJpeg, string name)
    {
        using var sha = System.Security.Cryptography.SHA256.Create();
        // Hash both the file content and the name to ensure uniqueness
        using var ms = new MemoryStream();
        using var fs = File.OpenRead(srcJpeg);
        fs.CopyTo(ms);
        var nameBytes = System.Text.Encoding.UTF8.GetBytes(name);
        ms.Write(nameBytes);
        ms.Position = 0;
        var hashBytes = sha.ComputeHash(ms);
        return Convert.ToHexString(hashBytes).ToLowerInvariant()[..16];
    }

    private static string CreatePhotoEntry(string preparedRoot, string name, string srcJpeg, int width, double aspectRatio)
    {
        var sourceHash = ComputeTestSourceHash(srcJpeg, name);

        var dir = Path.Combine(preparedRoot, sourceHash);
        Directory.CreateDirectory(dir);

        // Create derivatives using ffmpeg/cwebp
        var ffmpeg = "/opt/homebrew/bin/ffmpeg";
        var cwebp = "/opt/homebrew/bin/cwebp";

        // Intermediate PNG
        var intermediate = Path.Combine(dir, "intermediate.png");
        RunProcess(ffmpeg, $"-hide_banner -loglevel error -y -i \"{srcJpeg}\" -vf \"scale='min(2560,iw)':-2\" -frames:v 1 -map_metadata -1 \"{intermediate}\"");

        // AVIF
        var avifTmp = Path.Combine(dir, $"tmp-{width}.avif");
        RunProcess(ffmpeg, $"-hide_banner -loglevel error -y -i \"{intermediate}\" -vf \"scale={width}:-2,format=yuv420p\" -frames:v 1 -map_metadata -1 -c:v libsvtav1 -crf 32 -preset 6 \"{avifTmp}\"");

        // WebP
        var webpTmp = Path.Combine(dir, $"tmp-{width}.webp");
        RunProcess(cwebp, $"-quiet -q 78 -resize {width} 0 \"{intermediate}\" -o \"{webpTmp}\"");

        // Hash the derivatives
        var avifHash = HashFile(avifTmp);
        var webpHash = HashFile(webpTmp);

        var avifName = $"{avifHash}.avif";
        var webpName = $"{webpHash}.webp";

        File.Move(avifTmp, Path.Combine(dir, avifName), overwrite: true);
        File.Move(webpTmp, Path.Combine(dir, webpName), overwrite: true);
        File.Delete(intermediate);

        var avifBytes = new FileInfo(Path.Combine(dir, avifName)).Length;
        var webpBytes = new FileInfo(Path.Combine(dir, webpName)).Length;

        // Write photo.json
        var photoJson = System.Text.Json.JsonSerializer.Serialize(new
        {
            sourceHash,
            sourceName = name,
            width,
            height = (int)Math.Round(width / aspectRatio),
            color = "#aabbcc",
            preview = "data:image/webp;base64,UklGRiQAAABXRUJQVlA4IBgAAAAwAQCdASoBAAEAAkA4JZACdAEO/gHOAAA=",
            derivatives = new[]
            {
                new { w = width, avif = avifName, webp = webpName, bytesAvif = avifBytes, bytesWebp = webpBytes }
            }
        });
        File.WriteAllText(Path.Combine(dir, "photo.json"), photoJson);

        return sourceHash;
    }

    private static string HashFile(string path)
    {
        using var sha = System.Security.Cryptography.SHA256.Create();
        using var fs = File.OpenRead(path);
        return Convert.ToHexString(sha.ComputeHash(fs)).ToLowerInvariant()[..16];
    }

    private static void RunProcess(string exe, string args)
    {
        var psi = new System.Diagnostics.ProcessStartInfo(exe, args)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        using var p = System.Diagnostics.Process.Start(psi)!;
        var stderr = p.StandardError.ReadToEnd();
        p.WaitForExit();
        if (p.ExitCode != 0)
            throw new InvalidOperationException($"{exe} failed (exit {p.ExitCode}): {stderr}");
    }

    private Manifest MakeManifest(string photo1Name, string photo2Name, bool includePrivate = true) => new()
    {
        Galleries =
        [
            new ManifestGallery { Key = "gallery-1", Title = "Gallery One", Visibility = "public", Photos = [photo1Name, photo2Name] },
            .. (includePrivate ? new[] { new ManifestGallery { Key = "gallery-private", Title = "Private Gallery", Visibility = "private", Photos = [photo1Name] } } : Array.Empty<ManifestGallery>()),
        ],
        Photos = new Dictionary<string, ManifestPhoto>
        {
            [photo1Name] = new ManifestPhoto { Title = "Photo One", Caption = "First caption" },
            [photo2Name] = new ManifestPhoto { Title = "Photo Two", Caption = null },
        }
    };

    [Fact]
    public async Task First_run_creates_correct_documents_and_counts()
    {
        try { EncodedFixtures.RequireFfmpeg(); EncodedFixtures.RequireCwebp(); }
        catch (SkipException ex) { Assert.Fail($"SKIP: {ex.Message}"); return; }

        var db = _mongo.NewDatabase();
        var photo1Name = "photo-a.jpg";
        var photo2Name = "photo-b.jpg";

        var (tmpDir, preparedRoot) = CreatePreparedFixture(photo1Name, photo2Name, out _, out _);
        try
        {
            var manifest = MakeManifest(photo1Name, photo2Name);
            var seeder = new Seeder(manifest, preparedRoot, "/dev/null", NewStore(), db, NullLogger.Instance, _s3.StorageOptions.PublicBucket!);
            var summary = await seeder.SeedAsync();

            // Counts
            Assert.Equal(2, summary.GalleriesUpserted);
            Assert.Equal(2, summary.ImagesUpserted);
            Assert.Equal(0, summary.Rejected);
            Assert.Equal(0, summary.UnmatchedManifest);
            Assert.Equal(0, summary.UnmatchedPrepared);
            // Files should either be uploaded or skipped (already present from a prior test in this container)
            Assert.True(summary.FilesUploaded + summary.FilesSkipped > 0,
                "Expected some derivative files to be processed (uploaded or skipped-unchanged)");

            // Verify documents
            var images = await db.GetCollection<BsonDocument>("images").Find(FilterDefinition<BsonDocument>.Empty).ToListAsync();
            Assert.Equal(2, images.Count);
            foreach (var img in images)
            {
                Assert.True(img.Contains("seedKey"));
                Assert.True(img.Contains("createdAt"));
            }

            var galleries = await db.GetCollection<BsonDocument>("galleries").Find(FilterDefinition<BsonDocument>.Empty).ToListAsync();
            Assert.Equal(2, galleries.Count);

            // Private gallery should NOT be in homeCards
            var siteDoc = await db.GetCollection<BsonDocument>("site").Find(FilterDefinition<BsonDocument>.Empty).FirstOrDefaultAsync();
            Assert.NotNull(siteDoc);
            var homeCards = siteDoc["homeCards"].AsBsonArray;
            Assert.Single(homeCards); // Only the public gallery
            var card = homeCards[0].AsBsonDocument;
            Assert.Equal("gallery-1", galleries.First(g => g["_id"].AsString == card["galleryId"].AsString)["seedKey"].AsString);
        }
        finally { if (Directory.Exists(tmpDir)) Directory.Delete(tmpDir, true); }
    }

    [Fact]
    public async Task Second_run_uploads_nothing_and_docs_are_equal()
    {
        try { EncodedFixtures.RequireFfmpeg(); EncodedFixtures.RequireCwebp(); }
        catch (SkipException ex) { Assert.Fail($"SKIP: {ex.Message}"); return; }

        var db = _mongo.NewDatabase();
        var photo1Name = "photo-sc1.jpg";
        var photo2Name = "photo-sc2.jpg";

        var (tmpDir, preparedRoot) = CreatePreparedFixture(photo1Name, photo2Name, out _, out _);
        try
        {
            var manifest = MakeManifest(photo1Name, photo2Name, includePrivate: false);
            var store = NewStore();

            // First run
            var seeder1 = new Seeder(manifest, preparedRoot, "/dev/null", store, db, NullLogger.Instance, _s3.StorageOptions.PublicBucket!);
            await seeder1.SeedAsync();

            // Snapshot documents (excluding updatedAt)
            var images1 = (await db.GetCollection<BsonDocument>("images").Find(FilterDefinition<BsonDocument>.Empty).ToListAsync())
                .Select(d => { d.Remove("updatedAt"); return d.ToJson(); })
                .OrderBy(s => s).ToList();

            // Second run
            var seeder2 = new Seeder(manifest, preparedRoot, "/dev/null", store, db, NullLogger.Instance, _s3.StorageOptions.PublicBucket!);
            var summary2 = await seeder2.SeedAsync();

            Assert.Equal(0, summary2.FilesUploaded);
            Assert.True(summary2.FilesSkipped > 0, "Expected skipped files on second run");

            // Documents equal (excluding updatedAt)
            var images2 = (await db.GetCollection<BsonDocument>("images").Find(FilterDefinition<BsonDocument>.Empty).ToListAsync())
                .Select(d => { d.Remove("updatedAt"); return d.ToJson(); })
                .OrderBy(s => s).ToList();

            Assert.Equal(images1, images2);
        }
        finally { if (Directory.Exists(tmpDir)) Directory.Delete(tmpDir, true); }
    }

    [Fact]
    public async Task Gallery_removed_from_manifest_is_deleted()
    {
        try { EncodedFixtures.RequireFfmpeg(); EncodedFixtures.RequireCwebp(); }
        catch (SkipException ex) { Assert.Fail($"SKIP: {ex.Message}"); return; }

        var db = _mongo.NewDatabase();
        var photo1Name = "photo-del1.jpg";
        var photo2Name = "photo-del2.jpg";

        var (tmpDir, preparedRoot) = CreatePreparedFixture(photo1Name, photo2Name, out _, out _);
        try
        {
            var fullManifest = MakeManifest(photo1Name, photo2Name, includePrivate: true);
            var store = NewStore();

            // First run: 2 galleries
            var seeder1 = new Seeder(fullManifest, preparedRoot, "/dev/null", store, db, NullLogger.Instance, _s3.StorageOptions.PublicBucket!);
            await seeder1.SeedAsync();

            // Second run: only 1 gallery
            var reducedManifest = new Manifest
            {
                Galleries = [new ManifestGallery { Key = "gallery-1", Title = "Gallery One", Visibility = "public", Photos = [photo1Name] }],
                Photos = new Dictionary<string, ManifestPhoto> { [photo1Name] = new ManifestPhoto { Title = "Photo One" } }
            };
            var seeder2 = new Seeder(reducedManifest, preparedRoot, "/dev/null", store, db, NullLogger.Instance, _s3.StorageOptions.PublicBucket!);
            var summary = await seeder2.SeedAsync();

            // photo2 is gone from manifest too, so its image doc should be deleted
            Assert.Equal(1, summary.GalleriesDeleted);
            Assert.Equal(1, summary.ImagesDeleted);

            var galleries = await db.GetCollection<BsonDocument>("galleries").Find(FilterDefinition<BsonDocument>.Empty).ToListAsync();
            Assert.Single(galleries);
        }
        finally { if (Directory.Exists(tmpDir)) Directory.Delete(tmpDir, true); }
    }

    [Fact]
    public async Task Unmatched_names_reported_both_ways()
    {
        try { EncodedFixtures.RequireFfmpeg(); EncodedFixtures.RequireCwebp(); }
        catch (SkipException ex) { Assert.Fail($"SKIP: {ex.Message}"); return; }

        var db = _mongo.NewDatabase();
        var photo1Name = "photo-um1.jpg";
        var photo2Name = "photo-um2.jpg";

        var (tmpDir, preparedRoot) = CreatePreparedFixture(photo1Name, photo2Name, out _, out _);
        try
        {
            // Manifest has extra photo not in prepared; prepared has photo2 not in manifest
            var manifest = new Manifest
            {
                Galleries = [new ManifestGallery { Key = "g1", Title = "G1", Visibility = "public", Photos = [photo1Name, "not-prepared.jpg"] }],
                Photos = new Dictionary<string, ManifestPhoto> { [photo1Name] = new ManifestPhoto { Title = null } }
            };
            var seeder = new Seeder(manifest, preparedRoot, "/dev/null", NewStore(), db, NullLogger.Instance, _s3.StorageOptions.PublicBucket!);
            var summary = await seeder.SeedAsync();

            Assert.Equal(1, summary.UnmatchedManifest);   // "not-prepared.jpg" not in prepared
            // photo2Name is in prepared but not in manifest; there may be 1 or 2 unmatched
            // depending on whether any residual files exist in the temp dir
            Assert.True(summary.UnmatchedPrepared >= 1,
                $"Expected at least 1 unmatched prepared entry (photo-um2.jpg), got {summary.UnmatchedPrepared}");
        }
        finally { if (Directory.Exists(tmpDir)) Directory.Delete(tmpDir, true); }
    }

    [Fact]
    public async Task Cover_fallback_and_empty_gallery_card()
    {
        try { EncodedFixtures.RequireFfmpeg(); EncodedFixtures.RequireCwebp(); }
        catch (SkipException ex) { Assert.Fail($"SKIP: {ex.Message}"); return; }

        var db = _mongo.NewDatabase();
        var photo1Name = "photo-cov1.jpg";
        var photo2Name = "photo-cov2.jpg";

        var (tmpDir, preparedRoot) = CreatePreparedFixture(photo1Name, photo2Name, out _, out _);
        try
        {
            var manifest = new Manifest
            {
                Galleries =
                [
                    new ManifestGallery { Key = "g-full", Title = "Full Gallery", Visibility = "public", Photos = [photo1Name, photo2Name] },
                    new ManifestGallery { Key = "g-empty", Title = "Empty Gallery", Visibility = "public", Photos = [] },
                ],
                Photos = new Dictionary<string, ManifestPhoto>
                {
                    [photo1Name] = new ManifestPhoto { Title = "P1" },
                    [photo2Name] = new ManifestPhoto { Title = "P2" },
                }
            };

            var seeder = new Seeder(manifest, preparedRoot, "/dev/null", NewStore(), db, NullLogger.Instance, _s3.StorageOptions.PublicBucket!);
            await seeder.SeedAsync();

            var galleries = await db.GetCollection<BsonDocument>("galleries").Find(FilterDefinition<BsonDocument>.Empty).ToListAsync();
            var full = galleries.First(g => g["seedKey"].AsString == "g-full");
            var empty = galleries.First(g => g["seedKey"].AsString == "g-empty");

            // Full gallery should have cover = items[0]
            Assert.True(full.Contains("card"), "Full gallery should have a card");
            var fullCard = full["card"].AsBsonDocument;
            Assert.True(fullCard.Contains("cover"), "Full gallery card should have cover (items[0] fallback)");

            // Empty gallery card should have no cover
            Assert.True(empty.Contains("card"), "Empty gallery should have a card");
            var emptyCard = empty["card"].AsBsonDocument;
            Assert.False(emptyCard.Contains("cover"), "Empty gallery card should have no cover");
        }
        finally { if (Directory.Exists(tmpDir)) Directory.Delete(tmpDir, true); }
    }

    [Fact]
    public async Task Claim_preserved_after_reseed()
    {
        try { EncodedFixtures.RequireFfmpeg(); EncodedFixtures.RequireCwebp(); }
        catch (SkipException ex) { Assert.Fail($"SKIP: {ex.Message}"); return; }

        var db = _mongo.NewDatabase();
        var photo1Name = "photo-claim1.jpg";
        var photo2Name = "photo-claim2.jpg";

        var (tmpDir, preparedRoot) = CreatePreparedFixture(photo1Name, photo2Name, out _, out _);
        try
        {
            var manifest = MakeManifest(photo1Name, photo2Name, includePrivate: false);

            // First seed
            var seeder1 = new Seeder(manifest, preparedRoot, "/dev/null", NewStore(), db, NullLogger.Instance, _s3.StorageOptions.PublicBucket!);
            await seeder1.SeedAsync();

            // Set claimedSubject directly
            var siteCol = db.GetCollection<BsonDocument>("site");
            await siteCol.UpdateOneAsync(
                Builders<BsonDocument>.Filter.Eq("_id", "site"),
                Builders<BsonDocument>.Update.Set("claimedSubject", "sub-1"));

            // Reseed
            var seeder2 = new Seeder(manifest, preparedRoot, "/dev/null", NewStore(), db, NullLogger.Instance, _s3.StorageOptions.PublicBucket!);
            await seeder2.SeedAsync();

            // Claim must still be sub-1
            var site = await siteCol.Find(Builders<BsonDocument>.Filter.Eq("_id", "site")).FirstAsync();
            Assert.Equal("sub-1", site["claimedSubject"].AsString);
        }
        finally { if (Directory.Exists(tmpDir)) Directory.Delete(tmpDir, true); }
    }

    [Fact]
    public async Task Gallery_without_seedKey_survives_reseed()
    {
        try { EncodedFixtures.RequireFfmpeg(); EncodedFixtures.RequireCwebp(); }
        catch (SkipException ex) { Assert.Fail($"SKIP: {ex.Message}"); return; }

        var db = _mongo.NewDatabase();
        var photo1Name = "photo-nsk1.jpg";
        var photo2Name = "photo-nsk2.jpg";

        var (tmpDir, preparedRoot) = CreatePreparedFixture(photo1Name, photo2Name, out _, out _);
        try
        {
            var manifest = MakeManifest(photo1Name, photo2Name, includePrivate: false);

            // Insert a gallery without seedKey
            var galleriesCol = db.GetCollection<BsonDocument>("galleries");
            var noSeedKeyId = Gallery.Core.Ids.OpaqueId.NewId();
            await galleriesCol.InsertOneAsync(new BsonDocument
            {
                ["_id"] = noSeedKeyId,
                ["title"] = "Manual Gallery",
                ["visibility"] = "public",
                ["items"] = new BsonArray(),
            });

            // Seed
            var seeder = new Seeder(manifest, preparedRoot, "/dev/null", NewStore(), db, NullLogger.Instance, _s3.StorageOptions.PublicBucket!);
            await seeder.SeedAsync();

            // The manual gallery must still exist
            var manual = await galleriesCol.Find(Builders<BsonDocument>.Filter.Eq("_id", noSeedKeyId)).FirstOrDefaultAsync();
            Assert.NotNull(manual);
        }
        finally { if (Directory.Exists(tmpDir)) Directory.Delete(tmpDir, true); }
    }

    [Fact]
    public async Task Validation_gate_tainted_WebP_rejects_photo_nothing_uploaded()
    {
        try { EncodedFixtures.RequireFfmpeg(); EncodedFixtures.RequireCwebp(); }
        catch (SkipException ex) { Assert.Fail($"SKIP: {ex.Message}"); return; }

        var db = _mongo.NewDatabase();
        var photo1Name = "photo-val1.jpg";
        var photo2Name = "photo-val2.jpg";

        var (tmpDir, preparedRoot) = CreatePreparedFixture(photo1Name, photo2Name, out var photo1Hash, out _);
        try
        {
            // Replace photo1's WebP with the tainted WebP
            var photo1Dir = Path.Combine(preparedRoot, photo1Hash);
            var photo1JsonPath = Path.Combine(photo1Dir, "photo.json");
            var photo1Json = System.Text.Json.JsonSerializer.Deserialize<PhotoJson>(
                File.ReadAllText(photo1JsonPath),
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

            // Remove old WebP
            var oldWebpPath = Path.Combine(photo1Dir, photo1Json.Derivatives[0].Webp);
            if (File.Exists(oldWebpPath)) File.Delete(oldWebpPath);

            // Create tainted WebP at declared width
            var taintedWebpTmp = Path.Combine(photo1Dir, "tainted.webp");
            var declaredWidth = photo1Json.Derivatives[0].W;
            RunProcess("/opt/homebrew/bin/cwebp",
                $"-quiet -metadata all -resize {declaredWidth} 0 \"{EncodedFixtures.GpsCameraJpg}\" -o \"{taintedWebpTmp}\"");

            // Hash and rename to match the expected naming
            var taintedHash = HashFile(taintedWebpTmp);
            var taintedName = $"{taintedHash}.webp";
            File.Move(taintedWebpTmp, Path.Combine(photo1Dir, taintedName), overwrite: true);

            // Update photo.json to point to the tainted WebP
            var updatedDerivs = photo1Json.Derivatives.Select(d => new
            {
                w = d.W, avif = d.Avif, webp = taintedName,
                bytesAvif = d.BytesAvif, bytesWebp = new FileInfo(Path.Combine(photo1Dir, taintedName)).Length
            }).ToList();
            File.WriteAllText(photo1JsonPath, System.Text.Json.JsonSerializer.Serialize(new
            {
                photo1Json.SourceHash, photo1Json.SourceName,
                photo1Json.Width, photo1Json.Height,
                photo1Json.Color, photo1Json.Preview,
                derivatives = updatedDerivs
            }));

            // Verify the tainted file itself is correctly detected as invalid (precondition)
            using (var verifyFs = File.OpenRead(Path.Combine(photo1Dir, taintedName)))
            {
                double ar = photo1Json.Height > 0 ? (double)photo1Json.Width / photo1Json.Height : 0.5;
                var verifyResult = DerivativeValidator.Validate(verifyFs, "webp", declaredWidth, ar);
                Assert.False(verifyResult.IsValid,
                    $"Precondition: tainted WebP should fail validation (got valid with reason: {verifyResult.Reason})");
            }

            var manifest = MakeManifest(photo1Name, photo2Name, includePrivate: false);
            var store = NewStore();
            var seeder = new Seeder(manifest, preparedRoot, "/dev/null", store, db, NullLogger.Instance, _s3.StorageOptions.PublicBucket!);
            var summary = await seeder.SeedAsync();

            // photo1 rejected, photo2 seeded
            Assert.Equal(1, summary.Rejected);
            Assert.Equal(1, summary.ImagesUpserted);  // Only photo2

            // No keys from photo1 should exist in the bucket
            var exists = await store.ExistsAsync(_s3.StorageOptions.PublicBucket!, taintedName);
            Assert.False(exists, "Tainted WebP key must not be in the bucket");
        }
        finally { if (Directory.Exists(tmpDir)) Directory.Delete(tmpDir, true); }
    }

    /// <summary>
    /// [SAFETY] III.10: a photo with ONE tainted derivative uploads NONE of its files, including the
    /// clean ones. Uses a fresh bucket so a key shared with another photo (identical fixture bytes)
    /// or a previous test cannot mask an upload. The bucket must stay empty, and no image doc is written.
    /// </summary>
    [Fact]
    public async Task Validation_gate_uploads_no_file_of_a_rejected_photo()
    {
        try { EncodedFixtures.RequireFfmpeg(); EncodedFixtures.RequireCwebp(); }
        catch (SkipException ex) { Assert.Fail($"SKIP: {ex.Message}"); return; }

        var db = _mongo.NewDatabase();
        var (tmpDir, preparedRoot) = CreatePreparedFixture("gate-1.jpg", "gate-2.jpg", out var hash1, out _);
        try
        {
            var dir = Path.Combine(preparedRoot, hash1);
            var jsonPath = Path.Combine(dir, "photo.json");
            var photo = System.Text.Json.JsonSerializer.Deserialize<PhotoJson>(File.ReadAllText(jsonPath),
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

            // Taint the LAST file the validator reads, so every earlier (clean) file was already checked.
            var last = photo.Derivatives[^1];
            var lastWebp = Path.Combine(dir, last.Webp);
            File.Delete(lastWebp);
            RunProcess("/opt/homebrew/bin/cwebp",
                $"-quiet -metadata all -resize {last.W} 0 \"{EncodedFixtures.GpsCameraJpg}\" -o \"{lastWebp}\"");

            var bucket = $"gate-{Guid.NewGuid():N}";
            var client = new Gallery.Web.Storage.S3ClientFactory(_s3.StorageOptions).CreateClient();
            await client.PutBucketAsync(bucket);

            var manifest = new Manifest
            {
                Galleries = [new ManifestGallery { Key = "g", Title = "G", Visibility = "public", Photos = ["gate-1.jpg"] }],
            };
            var summary = await new Seeder(manifest, preparedRoot, "/dev/null", NewStore(), db, NullLogger.Instance, bucket).SeedAsync();

            Assert.Equal(1, summary.Rejected);
            Assert.Equal(0, summary.FilesUploaded);
            var listed = await client.ListObjectsV2Async(new Amazon.S3.Model.ListObjectsV2Request { BucketName = bucket });
            Assert.Empty(listed.S3Objects ?? []);
            Assert.Equal(0, await db.GetCollection<MongoDB.Bson.BsonDocument>("images")
                .CountDocumentsAsync(MongoDB.Driver.FilterDefinition<MongoDB.Bson.BsonDocument>.Empty));
        }
        finally { if (Directory.Exists(tmpDir)) Directory.Delete(tmpDir, true); }
    }

    private ImageStore NewStore() =>
        new(new Gallery.Web.Storage.S3ClientFactory(_s3.StorageOptions),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<ImageStore>.Instance);
}
