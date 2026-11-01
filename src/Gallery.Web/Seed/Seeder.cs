using Gallery.Core.Ids;
using Gallery.Core.Text;
using Gallery.Web.Data;
using Gallery.Web.Storage;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Gallery.Web.Seed;

/// <summary>
/// Summary returned after seeding.
/// </summary>
public sealed record SeedSummary(
    int GalleriesUpserted,
    int GalleriesDeleted,
    int ImagesUpserted,
    int ImagesDeleted,
    int FilesUploaded,
    int FilesSkipped,
    int UnmatchedManifest,
    int UnmatchedPrepared,
    int Rejected
);

/// <summary>
/// Seeder: reconciles the manifest + prepared photos into Mongo and S3 (public bucket only).
/// Implements contracts/seed.md steps 2–6 and data-model.md state transitions.
/// </summary>
public sealed class Seeder
{
    private const string CacheControlImmutable = "public, max-age=31536000, immutable";
    private const string ContentTypeAvif = "image/avif";
    private const string ContentTypeWebp = "image/webp";

    private readonly Manifest _manifest;
    private readonly string _preparedRoot;
    private readonly string _photosDir;
    private readonly ImageStore _imageStore;
    private readonly IMongoDatabase _db;
    private readonly ILogger _logger;
    private readonly string _publicBucket;

    public Seeder(
        Manifest manifest,
        string preparedRoot,
        string photosDir,
        ImageStore imageStore,
        IMongoDatabase db,
        ILogger logger,
        string publicBucket)
    {
        _manifest = manifest;
        _preparedRoot = preparedRoot;
        _photosDir = photosDir;
        _imageStore = imageStore;
        _db = db;
        _logger = logger;
        _publicBucket = publicBucket;
    }

    public async Task<SeedSummary> SeedAsync(CancellationToken ct = default)
    {
        // ── Step 2: map source file name → prepared folder by hashing dev-pictures/<name> ──
        // Key: sourceName (from photo.json), Value: (sourceHash, PhotoJson, directory path)
        var prepared = new Dictionary<string, (string hash, PhotoJson photo, string dir)>(StringComparer.OrdinalIgnoreCase);

        if (Directory.Exists(_preparedRoot))
        {
            foreach (var hashDir in Directory.EnumerateDirectories(_preparedRoot))
            {
                var photoJsonPath = Path.Combine(hashDir, "photo.json");
                if (!File.Exists(photoJsonPath)) continue;
                var photoJson = PhotoJsonLoader.Load(photoJsonPath);
                prepared[photoJson.SourceName] = (photoJson.SourceHash, photoJson, hashDir);
            }
        }

        // Collect all photo names referenced in the manifest
        var manifestPhotoNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var g in _manifest.Galleries)
            foreach (var p in g.Photos)
                manifestPhotoNames.Add(p);

        // ── Step 3: report unmatched names in both directions ──
        var unmatchedManifest = manifestPhotoNames.Where(n => !prepared.ContainsKey(n)).ToList();
        var unmatchedPrepared = prepared.Keys.Where(n => !manifestPhotoNames.Contains(n)).ToList();

        foreach (var n in unmatchedManifest)
            _logger.LogInformation("Unmatched manifest entry (no prepared folder): {name}", n);
        foreach (var n in unmatchedPrepared)
            _logger.LogInformation("Unmatched prepared entry (not in manifest): {name}", n);

        // Seed only the matched ones
        var matchedNames = manifestPhotoNames.Where(prepared.ContainsKey).ToHashSet(StringComparer.OrdinalIgnoreCase);

        // ── Ensure indexes ──
        await EnsureIndexesAsync(ct);

        // ── Step 4: validate derivatives and upload ──
        // Process each matched photo: validate ALL derivatives BEFORE uploading any.
        int filesUploaded = 0, filesSkipped = 0, rejected = 0;

        // Map: sourceName → (camera Exif, PhotoJson, already validated)
        var validatedPhotos = new Dictionary<string, (Gallery.Core.Images.Exif? camera, PhotoJson photo, string dir)>(StringComparer.OrdinalIgnoreCase);

        foreach (var name in matchedNames)
        {
            var (hash, photoJson, dir) = prepared[name];

            // Read camera allowlist from original JPEG in photos dir
            Gallery.Core.Images.Exif? camera = null;
            var originalPath = Path.Combine(_photosDir, name);
            if (File.Exists(originalPath))
            {
                try
                {
                    using var fs = File.OpenRead(originalPath);
                    camera = CameraFields.Read(fs);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("Could not read camera fields from {name}: {ex}", name, ex.Message);
                }
            }

            // Validate every derivative before uploading any
            double aspectRatio = photoJson.Width > 0 && photoJson.Height > 0
                ? (double)photoJson.Width / photoJson.Height
                : 1.0;

            bool allValid = true;
            string? failReason = null;
            string? failFile = null;

            foreach (var d in photoJson.Derivatives)
            {
                foreach (var (fileName, ext) in new[] { (d.Avif, "avif"), (d.Webp, "webp") })
                {
                    var filePath = Path.Combine(dir, fileName);
                    if (!File.Exists(filePath))
                    {
                        allValid = false;
                        failReason = $"File not found: {fileName}";
                        failFile = fileName;
                        break;
                    }

                    using var fs = File.OpenRead(filePath);
                    var result = DerivativeValidator.Validate(fs, ext, d.W, aspectRatio);
                    if (!result.IsValid)
                    {
                        allValid = false;
                        failReason = result.Reason;
                        failFile = fileName;
                        break;
                    }
                }
                if (!allValid) break;
            }

            if (!allValid)
            {
                _logger.LogWarning("Photo {name} rejected: {file} — {reason}", name, failFile, failReason);
                rejected++;
                continue;
            }

            validatedPhotos[name] = (camera, photoJson, dir);
        }

        // Upload files for validated photos
        foreach (var (name, (camera, photoJson, dir)) in validatedPhotos)
        {
            foreach (var d in photoJson.Derivatives)
            {
                var (u1, s1) = await UploadIfMissingAsync(dir, d.Avif, ContentTypeAvif, ct);
                var (u2, s2) = await UploadIfMissingAsync(dir, d.Webp, ContentTypeWebp, ct);
                filesUploaded += u1 + u2;
                filesSkipped += s1 + s2;
            }
        }

        // ── Step 5: upsert images ──
        var imagesCol = _db.GetCollection<BsonDocument>("images");
        int imagesUpserted = 0;

        // Load existing image docs by seedKey
        var existingImageDocs = await imagesCol
            .Find(Builders<BsonDocument>.Filter.In("seedKey", matchedNames))
            .ToListAsync(ct);
        var existingByKey = existingImageDocs.ToDictionary(
            d => d["seedKey"].AsString,
            d => d,
            StringComparer.OrdinalIgnoreCase);

        // Map: seedKey → imageId (for use in gallery upserts)
        var imageIdByKey = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (name, (camera, photoJson, _)) in validatedPhotos)
        {
            var imageId = existingByKey.TryGetValue(name, out var existing)
                ? existing["_id"].AsString
                : OpaqueId.NewId();

            imageIdByKey[name] = imageId;

            var now = DateTime.UtcNow;
            var createdAt = existing?["createdAt"].ToUniversalTime() ?? now;

            var derivatives = new BsonArray(photoJson.Derivatives.Select(d => new BsonDocument
            {
                ["w"] = d.W,
                ["avif"] = d.Avif,
                ["webp"] = d.Webp,
                ["bytesAvif"] = d.BytesAvif,
                ["bytesWebp"] = d.BytesWebp,
            }));

            var placeholderDoc = new BsonDocument
            {
                ["color"] = photoJson.Color,
                ["preview"] = photoJson.Preview,
            };

            // Determine title from manifest
            _manifest.Photos.TryGetValue(name, out var manifestPhoto);
            var title = ManifestLoader.EffectiveTitle(name, manifestPhoto);
            var caption = manifestPhoto?.Caption;

            BsonDocument? cameraDoc = null;
            if (camera is not null)
            {
                cameraDoc = new BsonDocument();
                if (camera.Make is not null)        cameraDoc["make"]      = camera.Make;
                if (camera.Model is not null)       cameraDoc["model"]     = camera.Model;
                if (camera.Lens is not null)        cameraDoc["lens"]      = camera.Lens;
                if (camera.FocalLengthMm is not null) cameraDoc["focalMm"] = (double)camera.FocalLengthMm.Value;
                if (camera.FocalLength35Mm is not null) cameraDoc["focal35Mm"] = camera.FocalLength35Mm.Value;
                if (camera.FNumber is not null)     cameraDoc["fNumber"]   = (double)camera.FNumber.Value;
                if (camera.ExposureSeconds is not null) cameraDoc["exposure"] = (double)camera.ExposureSeconds.Value;
                if (camera.Iso is not null)         cameraDoc["iso"]       = camera.Iso.Value;
            }

            var filter = Builders<BsonDocument>.Filter.Eq("seedKey", name);
            var update = Builders<BsonDocument>.Update
                .SetOnInsert("_id", imageId)
                .SetOnInsert("createdAt", createdAt)
                .Set("seedKey", name)
                .Set("sourceHash", photoJson.SourceHash)
                .Set("title", title)
                .Set("width", photoJson.Width)
                .Set("height", photoJson.Height)
                .Set("placeholder", placeholderDoc)
                .Set("derivatives", derivatives);

            if (caption is not null)
                update = update.Set("caption", caption);
            else
                update = update.Unset("caption");

            if (cameraDoc is not null)
                update = update.Set("camera", cameraDoc);
            else
                update = update.Unset("camera");

            await imagesCol.UpdateOneAsync(filter, update, new UpdateOptions { IsUpsert = true }, ct);
            imagesUpserted++;
        }

        // Build imageId lookup for ALL seeded photos (including rejected ones that have existing docs)
        // Re-query to also get existing imageIds for photos we just seeded
        var allSeededImageDocs = await imagesCol
            .Find(Builders<BsonDocument>.Filter.In("seedKey", matchedNames.ToList()))
            .ToListAsync(ct);
        var allImageIdByKey = allSeededImageDocs.ToDictionary(
            d => d["seedKey"].AsString,
            d => d["_id"].AsString,
            StringComparer.OrdinalIgnoreCase);

        // ── Step 5b: upsert galleries ──
        var galleriesCol = _db.GetCollection<BsonDocument>("galleries");
        int galleriesUpserted = 0;

        var existingGalleryDocs = await galleriesCol
            .Find(Builders<BsonDocument>.Filter.Exists("seedKey"))
            .ToListAsync(ct);
        var existingGalleryByKey = existingGalleryDocs.ToDictionary(
            d => d["seedKey"].AsString,
            d => d,
            StringComparer.OrdinalIgnoreCase);

        var galleryIdByKey = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var mg in _manifest.Galleries)
        {
            var galleryId = existingGalleryByKey.TryGetValue(mg.Key, out var existingG)
                ? existingG["_id"].AsString
                : OpaqueId.NewId();

            galleryIdByKey[mg.Key] = galleryId;

            var title = mg.Title ?? mg.Key;
            var slug = Slug.From(title);
            var visibility = ManifestLoader.EffectiveVisibility(mg);

            // Build items list (only matched, validated photos)
            var items = mg.Photos
                .Where(p => allImageIdByKey.ContainsKey(p))
                .Select(p => allImageIdByKey[p])
                .ToList();

            var itemsBson = new BsonArray(items.Select(id => (BsonValue)id));

            var now = DateTime.UtcNow;

            var filter = Builders<BsonDocument>.Filter.Eq("seedKey", mg.Key);
            var update = Builders<BsonDocument>.Update
                .SetOnInsert("_id", galleryId)
                .Set("seedKey", mg.Key)
                .Set("slug", slug)
                .Set("title", title)
                .Set("visibility", visibility)
                .Set("items", itemsBson)
                .Set("updatedAt", now);

            if (mg.Description is not null)
                update = update.Set("description", mg.Description);
            else
                update = update.Unset("description");

            if (mg.Cover is not null)
            {
                if (allImageIdByKey.TryGetValue(mg.Cover, out var coverId))
                    update = update.Set("pinnedCover", coverId);
            }
            else
            {
                update = update.Unset("pinnedCover");
            }

            await galleriesCol.UpdateOneAsync(filter, update, new UpdateOptions { IsUpsert = true }, ct);
            galleriesUpserted++;
        }

        // Update galleryIds on image documents
        // First, clear all galleryIds for matched images, then re-add
        var galleryMembership = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var mg in _manifest.Galleries)
        {
            if (!galleryIdByKey.TryGetValue(mg.Key, out var gid)) continue;
            foreach (var photoName in mg.Photos)
            {
                if (!allImageIdByKey.TryGetValue(photoName, out var iid)) continue;
                if (!galleryMembership.TryGetValue(iid, out var list))
                    galleryMembership[iid] = list = [];
                if (!list.Contains(gid))
                    list.Add(gid);
            }
        }

        foreach (var (imageId, galleryIds) in galleryMembership)
        {
            var filter = Builders<BsonDocument>.Filter.Eq("_id", imageId);
            var update = Builders<BsonDocument>.Update.Set("galleryIds", new BsonArray(galleryIds.Select(id => (BsonValue)id)));
            await imagesCol.UpdateOneAsync(filter, update, cancellationToken: ct);
        }

        // ── Step 5c: recompute gallery cards ──
        // We need image docs to build cover images
        var allImageIds = allImageIdByKey.Values.ToList();
        var imageDocsByIdList = await imagesCol
            .Find(Builders<BsonDocument>.Filter.In("_id", allImageIds))
            .ToListAsync(ct);
        var imageDocsById = imageDocsByIdList.ToDictionary(d => d["_id"].AsString);

        foreach (var mg in _manifest.Galleries)
        {
            if (!galleryIdByKey.TryGetValue(mg.Key, out var gid)) continue;

            var items = mg.Photos
                .Where(p => allImageIdByKey.ContainsKey(p))
                .Select(p => allImageIdByKey[p])
                .ToList();

            // Cover: pinned or items[0]
            string? coverId = null;
            if (mg.Cover is not null && allImageIdByKey.TryGetValue(mg.Cover, out var pinnedId))
                coverId = pinnedId;
            else if (items.Count > 0)
                coverId = items[0];

            BsonDocument? coverDoc = null;
            if (coverId is not null && imageDocsById.TryGetValue(coverId, out var coverImg))
            {
                coverDoc = new BsonDocument
                {
                    ["imageId"] = coverId,
                    ["width"] = coverImg["width"].AsInt32,
                    ["height"] = coverImg["height"].AsInt32,
                    ["placeholder"] = coverImg["placeholder"].AsBsonDocument,
                    ["derivatives"] = coverImg["derivatives"].AsBsonArray,
                };
            }

            var title = mg.Title ?? mg.Key;
            var slug = Slug.From(title);

            var cardDoc = new BsonDocument
            {
                ["galleryId"] = gid,
                ["slug"] = slug,
                ["title"] = title,
                ["imageCount"] = items.Count,
            };
            if (coverDoc is not null) cardDoc["cover"] = coverDoc;

            var filter = Builders<BsonDocument>.Filter.Eq("_id", gid);
            var update = Builders<BsonDocument>.Update.Set("card", cardDoc);
            await galleriesCol.UpdateOneAsync(filter, update, cancellationToken: ct);
        }

        // ── Step 5d: delete galleries whose seedKey is not in the manifest ──
        var manifestGalleryKeys = _manifest.Galleries.Select(g => g.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var deleteGalleryFilter = Builders<BsonDocument>.Filter.And(
            Builders<BsonDocument>.Filter.Exists("seedKey"),
            Builders<BsonDocument>.Filter.Nin("seedKey", manifestGalleryKeys));
        var toDeleteGalleries = await galleriesCol.Find(deleteGalleryFilter).ToListAsync(ct);
        int galleriesDeleted = 0;
        foreach (var gDoc in toDeleteGalleries)
        {
            await galleriesCol.DeleteOneAsync(
                Builders<BsonDocument>.Filter.Eq("_id", gDoc["_id"].AsString), ct);
            galleriesDeleted++;
        }

        // ── Step 5e: delete images whose seedKey is not in the manifest ──
        var deleteImageFilter = Builders<BsonDocument>.Filter.And(
            Builders<BsonDocument>.Filter.Exists("seedKey"),
            Builders<BsonDocument>.Filter.Nin("seedKey", manifestPhotoNames));
        var toDeleteImages = await imagesCol.Find(deleteImageFilter).ToListAsync(ct);
        int imagesDeleted = 0;
        foreach (var iDoc in toDeleteImages)
        {
            await imagesCol.DeleteOneAsync(
                Builders<BsonDocument>.Filter.Eq("_id", iDoc["_id"].AsString), ct);
            imagesDeleted++;
        }

        // Update galleryIds for any images that had their gallery deleted
        if (toDeleteGalleries.Count > 0)
        {
            var deletedGalleryIds = toDeleteGalleries.Select(g => g["_id"].AsString).ToHashSet();
            var affectedImageFilter = Builders<BsonDocument>.Filter.AnyIn("galleryIds", deletedGalleryIds);
            var affectedImages = await imagesCol.Find(affectedImageFilter).ToListAsync(ct);
            foreach (var img in affectedImages)
            {
                var currentIds = img["galleryIds"].AsBsonArray
                    .Select(v => v.AsString)
                    .Where(id => !deletedGalleryIds.Contains(id))
                    .ToList();
                var update = Builders<BsonDocument>.Update.Set("galleryIds",
                    new BsonArray(currentIds.Select(id => (BsonValue)id)));
                await imagesCol.UpdateOneAsync(
                    Builders<BsonDocument>.Filter.Eq("_id", img["_id"].AsString),
                    update, cancellationToken: ct);
            }
        }

        // ── Step 5f: update site document ──
        // homeOrder = all gallery IDs in manifest order (public and private)
        // homeCards = cards for public galleries in homeOrder that hold at least one image (II.6)
        var siteCol = _db.GetCollection<BsonDocument>("site");

        // Re-query galleries to get final state
        var finalGalleryDocs = await galleriesCol
            .Find(Builders<BsonDocument>.Filter.In("seedKey", manifestGalleryKeys.ToList()))
            .ToListAsync(ct);
        var finalGalleryByKey = finalGalleryDocs.ToDictionary(
            d => d["seedKey"].AsString,
            d => d,
            StringComparer.OrdinalIgnoreCase);

        var homeOrder = new BsonArray();
        var homeCards = new BsonArray();

        foreach (var mg in _manifest.Galleries)
        {
            if (!finalGalleryByKey.TryGetValue(mg.Key, out var gDoc)) continue;
            var gid = gDoc["_id"].AsString;
            homeOrder.Add(gid);

            var visibility = ManifestLoader.EffectiveVisibility(mg);
            // II.6: an empty gallery is never listed, even when public.
            if (visibility == "public" && gDoc.Contains("card") && gDoc["card"].AsBsonDocument.Contains("cover"))
                homeCards.Add(gDoc["card"].AsBsonDocument);
        }

        var siteFilter = Builders<BsonDocument>.Filter.Eq("_id", "site");
        var siteUpdate = Builders<BsonDocument>.Update
            .Set("homeOrder", homeOrder)
            .Set("homeCards", homeCards)
            .Set("updatedAt", DateTime.UtcNow)
            .SetOnInsert("claimedSubject", BsonNull.Value)
            .SetOnInsert("claimedAt", BsonNull.Value)
            .SetOnInsert("claimedIp", BsonNull.Value);

        await siteCol.UpdateOneAsync(siteFilter, siteUpdate, new UpdateOptions { IsUpsert = true }, ct);

        return new SeedSummary(
            GalleriesUpserted: galleriesUpserted,
            GalleriesDeleted: galleriesDeleted,
            ImagesUpserted: imagesUpserted,
            ImagesDeleted: imagesDeleted,
            FilesUploaded: filesUploaded,
            FilesSkipped: filesSkipped,
            UnmatchedManifest: unmatchedManifest.Count,
            UnmatchedPrepared: unmatchedPrepared.Count,
            Rejected: rejected
        );
    }

    /// <summary>Returns (uploaded: 1|0, skipped: 1|0)</summary>
    private async Task<(int uploaded, int skipped)> UploadIfMissingAsync(
        string dir, string fileName, string contentType,
        CancellationToken ct)
    {
        var exists = await _imageStore.ExistsAsync(_publicBucket, fileName);
        if (exists) return (0, 1);

        var filePath = Path.Combine(dir, fileName);
        using var fs = File.OpenRead(filePath);
        await _imageStore.PutAsync(_publicBucket, fileName, fs, contentType, CacheControlImmutable);
        return (1, 0);
    }

    private Task EnsureIndexesAsync(CancellationToken ct) => EnsureSeedKeyIndexesAsync(_db, ct);

    /// <summary>
    /// Creates (or upgrades from the pre-008 non-partial form) the partial unique <c>seedKey_1</c>
    /// index on images and galleries. Also called by upload confirm, so a dev database seeded
    /// before 008 accepts a second uploaded image without waiting for the next seed run.
    /// </summary>
    public static async Task EnsureSeedKeyIndexesAsync(IMongoDatabase db, CancellationToken ct = default)
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
            Name = "seedKey_1",
            PartialFilterExpression = Builders<BsonDocument>.Filter.Exists("seedKey"),
        };

        await EnsureSeedKeyIndexAsync(imagesCol, indexKeysDef, indexOptions, ct);
        await EnsureSeedKeyIndexAsync(galleriesCol, indexKeysDef, indexOptions, ct);
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
            await col.Indexes.DropOneAsync("seedKey_1", ct);
            await col.Indexes.CreateOneAsync(new CreateIndexModel<BsonDocument>(keys, options), cancellationToken: ct);
        }
    }
}
