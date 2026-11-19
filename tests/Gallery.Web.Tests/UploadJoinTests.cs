using System.Net;
using System.Text.Json;
using Amazon.S3;
using Amazon.S3.Model;
using Gallery.Core.Ids;
using Gallery.Web.Data;
using Gallery.Web.Edge;
using Gallery.Web.Library;
using Gallery.Web.Maintenance;
using Gallery.Web.Storage;
using Gallery.Web.Tests.Fixtures;
using Gallery.Web.Upload;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MongoDB.Bson;
using MongoDB.Driver;
using static Gallery.Web.Tests.Fixtures.UploadFixtures;

namespace Gallery.Web.Tests;

/// <summary>
/// 020 T044 [SAFETY]: U12 join, U13 clear and the U3 destination (contracts/http.md "Upload",
/// research R-06). The 13 facts of 008's UploadConfirmTests mapped per contracts/test-impact.md
/// "UploadConfirmTests → UploadJoinTests" (same seams, same stored-field and object assertions per
/// item), plus the aimed-upload guards II.5a–II.5d and the join claim.
/// </summary>
[Collection("s3")]
public class UploadJoinTests : IClassFixture<MongoFixture>, IAsyncLifetime
{
    private const string Pub = "pub";
    private const string Priv = "priv";
    private const string Immutable = "public, max-age=31536000, immutable";
    private const string Color = "#a1b2c3";

    private readonly S3ProxyFixture _s3;
    private readonly MongoFixture _mongo;
    private readonly FakeGoogle _google = new();

    public UploadJoinTests(S3ProxyFixture s3, MongoFixture mongo)
    {
        _s3 = s3;
        _mongo = mongo;
    }

    public Task InitializeAsync() => _google.InitializeAsync();
    public Task DisposeAsync() => _google.DisposeAsync();

    private Task<UploadHost> NewHostAsync() => UploadHost.CreateAsync(_mongo, _google, _s3);

    /// <summary>An upload host whose edge provider is a recorder with page purges on (LibraryHost's settings).</summary>
    private Task<UploadHost> NewEdgeHostAsync(RecordingEdgeProvider recorder) =>
        UploadHost.CreateAsync(_mongo, _google, _s3,
            new Dictionary<string, string?>
            {
                ["Storage:ImageBaseUrl"] = LibraryHost.ImageBaseUrl,
                ["Edge:SiteBaseUrl"] = LibraryHost.SiteBaseUrl,
                ["Edge:PurgePages"] = "true",
            },
            b =>
            {
                b.Services.RemoveAll<IEdgeProvider>();
                b.Services.AddSingleton<IEdgeProvider>(recorder);
            });

    // ── fixtures: two photos with distinct content ───────────────────────────

    private sealed record Photo(string Name, string Master, int W, string Avif, string Webp, string Placeholder);

    // gps-camera.jpg is 400×200 → ladder [320]; orientation-6.jpg is upright 200×400 → [200].
    private static readonly Photo PhotoA = new("IMG_0001.jpg", "gps-camera.jpg", 320, "gps-320.avif", "gps-320.webp", "gps-placeholder.webp");
    private static readonly Photo PhotoB = new("Harbour at dusk.jpg", "orientation-6.jpg", 200, "orient6-200.avif", "orient6-200.webp", "orient6-placeholder.webp");

    private sealed record Staged(string BatchId, string ItemId, string AttemptId, Photo Photo, string AvifUrl, string WebpUrl);

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage resp) =>
        JsonDocument.Parse(await resp.Content.ReadAsStringAsync()).RootElement.Clone();

    private static Task<HttpResponseMessage> RegisterAsync(UploadHost host, Photo p, string? gallery = null, bool? visible = null)
    {
        var file = new { name = p.Name, size = Bytes(p.Master).LongLength, type = "image/jpeg" };
        object body = gallery is null && visible is null
            ? new { files = new[] { file } }
            : new { files = new[] { file }, gallery, visibleToVisitors = visible };
        return host.PostJsonAsync("/admin/upload/items", body);
    }

    /// <summary>Register (optionally aimed), PUT master, U4, PUT both sizes. Optionally U7 to succeeded.</summary>
    private static async Task<Staged> StageAsync(UploadHost host, Photo p, bool complete = true, string? gallery = null, bool? visible = null)
    {
        var master = Bytes(p.Master);
        var reg = await RegisterAsync(host, p, gallery, visible);
        Assert.Equal(HttpStatusCode.OK, reg.StatusCode);
        var r = await JsonAsync(reg);
        var a = r.GetProperty("accepted")[0];
        var batchId = r.GetProperty("batchId").GetString()!;
        var itemId = a.GetProperty("itemId").GetString()!;
        var attemptId = a.GetProperty("attemptId").GetString()!;
        Assert.Equal(HttpStatusCode.OK,
            (await UploadHost.PutToPresignedAsync(a.GetProperty("master").GetProperty("url").GetString()!, master, "image/jpeg")).StatusCode);

        var check = await host.PostJsonAsync($"/admin/upload/items/{itemId}/master", new { attemptId });
        Assert.Equal(HttpStatusCode.OK, check.StatusCode);
        var size = Assert.Single((await JsonAsync(check)).GetProperty("sizes").EnumerateArray());
        Assert.Equal(p.W, size.GetProperty("w").GetInt32());
        var avifUrl = size.GetProperty("avif").GetProperty("url").GetString()!;
        var webpUrl = size.GetProperty("webp").GetProperty("url").GetString()!;
        Assert.Equal(HttpStatusCode.OK, (await UploadHost.PutToPresignedAsync(avifUrl, Bytes(p.Avif), "image/avif")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await UploadHost.PutToPresignedAsync(webpUrl, Bytes(p.Webp), "image/webp")).StatusCode);

        if (complete)
        {
            var done = await host.PostJsonAsync($"/admin/upload/items/{itemId}/complete", new
            {
                attemptId,
                placeholder = new { color = Color, preview = DataUri(Bytes(p.Placeholder)) },
                timings = Array.Empty<object>(),
            });
            Assert.Equal(HttpStatusCode.OK, done.StatusCode);
        }
        return new Staged(batchId, itemId, attemptId, p, avifUrl, webpUrl);
    }

    private static Task<HttpResponseMessage> JoinAsync(UploadHost host, Staged s) =>
        host.PostJsonAsync($"/admin/upload/items/{s.ItemId}/join", new { attemptId = s.AttemptId });

    private static Task<HttpResponseMessage> ClearAsync(UploadHost host, string batchId) =>
        host.PostJsonAsync("/admin/upload/clear", new { batchId });

    private static async Task AssertErrorAsync(HttpResponseMessage resp, HttpStatusCode status, string code)
    {
        Assert.Equal(status, resp.StatusCode);
        Assert.Equal(code, (await JsonAsync(resp)).GetProperty("error").GetString());
    }

    private static IMongoDatabase Db(UploadHost host) =>
        host.Services.GetRequiredService<IMongoClient>().GetDatabase(host.DatabaseName);

    private static IMongoCollection<BsonDocument> Images(UploadHost host) => Db(host).GetCollection<BsonDocument>("images");

    private static async Task<BsonDocument> BatchAsync(UploadHost host, string batchId) =>
        await Db(host).GetCollection<BsonDocument>(BatchStore.CollectionName)
            .Find(new BsonDocument("_id", batchId)).SingleAsync();

    private static async Task<BsonDocument> OpenBatchAsync(UploadHost host) =>
        await Db(host).GetCollection<BsonDocument>(BatchStore.CollectionName)
            .Find(new BsonDocument("state", "open")).SingleAsync();

    private static async Task<BsonDocument?> ItemAsync(UploadHost host, Staged s) =>
        (await BatchAsync(host, s.BatchId))["items"].AsBsonArray.Select(i => i.AsBsonDocument)
            .SingleOrDefault(i => i["itemId"] == s.ItemId);

    private static async Task<BsonDocument> ValidatedAsync(UploadHost host, Staged s) =>
        (await ItemAsync(host, s))!["validated"].AsBsonDocument;

    private static List<string> KeysOf(BsonDocument validated) =>
        validated["sizes"].AsBsonArray.SelectMany(s => new[] { s["avifKey"].AsString, s["webpKey"].AsString }).ToList();

    private static List<string> AddedOf(BsonDocument batch) =>
        batch["added"].AsBsonArray.Select(v => v.AsString).ToList();

    private AmazonS3Client S3() => new S3ClientFactory(_s3.StorageOptions).CreateClient();

    private async Task<GetObjectMetadataResponse?> MetaAsync(string bucket, string key)
    {
        using var client = S3();
        try { return await client.GetObjectMetadataAsync(bucket, key); }
        catch (AmazonS3Exception e) when (e.StatusCode == HttpStatusCode.NotFound) { return null; }
    }

    /// <summary>The public bucket is shared by the whole "s3" collection and names are content
    /// hashes, so a test that asserts absence first removes keys other tests may have published.</summary>
    private async Task DeletePublicAsync(IEnumerable<string> keys)
    {
        using var client = S3();
        foreach (var k in keys) await client.DeleteObjectAsync(Pub, k);
    }

    private static List<string> LinesSince(UploadHost host, int from)
    {
        lock (host.LogLines) return host.LogLines.Skip(from).ToList();
    }

    private static int LogMark(UploadHost host)
    {
        lock (host.LogLines) return host.LogLines.Count;
    }

    private static async Task AssertImageAsync(UploadHost host, Staged s, BsonDocument validated, string title, params string[] galleryIds)
    {
        var img = await Images(host).Find(new BsonDocument("_id", s.ItemId)).SingleAsync();
        Assert.Equal(title, img["title"].AsString);
        Assert.False(img.Contains("seedKey"));
        Assert.False(img.Contains("sourceHash"));
        Assert.False(img.Contains("caption"));
        Assert.Equal(galleryIds, img["galleryIds"].AsBsonArray.Select(g => g.AsString).ToArray());
        Assert.Equal(validated["width"].AsInt32, img["width"].AsInt32);
        Assert.Equal(validated["height"].AsInt32, img["height"].AsInt32);
        Assert.Equal(validated["placeholder"], img["placeholder"]);
        var d = Assert.Single(img["derivatives"].AsBsonArray).AsBsonDocument;
        var v = validated["sizes"][0].AsBsonDocument;
        Assert.Equal(s.Photo.W, d["w"].AsInt32);
        Assert.Equal(v["avifKey"].AsString, d["avif"].AsString);
        Assert.Equal(v["webpKey"].AsString, d["webp"].AsString);
        Assert.Equal(v["bytesAvif"].ToInt64(), d["bytesAvif"].ToInt64());
        Assert.Equal(v["bytesWebp"].ToInt64(), d["bytesWebp"].ToInt64());
        Assert.Equal(BsonType.DateTime, img["createdAt"].BsonType);
    }

    /// <summary>A 200 join answer: <c>imageId</c>, <c>gallery</c> (null or id/title/public), <c>galleryGone</c>.</summary>
    private static async Task AssertJoinedAsync(HttpResponseMessage resp, string itemId,
        (string Id, string Title, bool Public)? gallery = null, bool galleryGone = false)
    {
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var json = await JsonAsync(resp);
        Assert.Equal(["imageId", "gallery", "galleryGone"], json.EnumerateObject().Select(p => p.Name).ToArray());
        Assert.Equal(itemId, json.GetProperty("imageId").GetString());
        Assert.Equal(galleryGone, json.GetProperty("galleryGone").GetBoolean());
        var g = json.GetProperty("gallery");
        if (gallery is null)
        {
            Assert.Equal(JsonValueKind.Null, g.ValueKind);
            return;
        }
        Assert.Equal((gallery.Value.Id, gallery.Value.Title, gallery.Value.Public),
            (g.GetProperty("id").GetString(), g.GetProperty("title").GetString(), g.GetProperty("public").GetBoolean()));
    }

    /// <summary>Claims <paramref name="s"/> through the store, as U12 does before publishing.</summary>
    private static async Task ClaimAsync(UploadHost host, Staged s)
    {
        using var scope = host.Services.CreateScope();
        Assert.True(await scope.ServiceProvider.GetRequiredService<BatchStore>().TryClaimJoinAsync(s.BatchId, s.ItemId, s.AttemptId));
    }

    /// <summary>A gallery made through the owner services: private and empty, or with one image (and public).</summary>
    private static async Task<string> GalleryAsync(UploadHost host, string title, bool withPhoto = false, bool makePublic = false)
    {
        using var scope = host.Services.CreateScope();
        var galleries = scope.ServiceProvider.GetRequiredService<GalleryService>();
        var created = await galleries.CreateAsync(title);
        Assert.True(created.IsOk);
        var gid = created.GalleryId!;
        if (withPhoto || makePublic)
        {
            var image = SeedShapes.UploadedImage();
            await Db(host).GetCollection<ImageDoc>("images").InsertOneAsync(image);
            Assert.Equal(1, (await galleries.AddAsync(gid, [image.Id])).Added);
        }
        if (makePublic)
            Assert.True((await galleries.PublishAsync(gid, confirmed: true)).IsOk);
        return gid;
    }

    private static async Task<GalleryDoc> StoredGalleryAsync(UploadHost host, string gid) =>
        await Db(host).GetCollection<GalleryDoc>("galleries").Find(g => g.Id == gid).SingleAsync();

    private static async Task<string> GalleriesDumpAsync(UploadHost host) =>
        string.Join("\n", (await Db(host).GetCollection<BsonDocument>("galleries")
            .Find(FilterDefinition<BsonDocument>.Empty).Sort(new BsonDocument("_id", 1)).ToListAsync()).Select(d => d.ToJson()));

    private static async Task EdgeIdleAsync(UploadHost host) =>
        Assert.True(await host.Services.GetRequiredService<EdgeQueue>().WhenIdleAsync(TimeSpan.FromSeconds(10)));

    // ── II.5d: only a validated item joins ───────────────────────────────────

    [Fact]
    public async Task Join_of_an_item_that_is_not_validated_is_409_not_active()
    {
        await using var host = await NewHostAsync();
        var queued = await StageAsync(host, PhotoA, complete: false);
        var processing = await StageAsync(host, PhotoB, complete: false);
        Assert.Equal(HttpStatusCode.NoContent, (await host.PostJsonAsync($"/admin/upload/items/{processing.ItemId}/progress",
            new { attemptId = processing.AttemptId, state = "processing", percent = 10 })).StatusCode);
        var uploading = await StageAsync(host, PhotoA, complete: false);
        Assert.Equal(HttpStatusCode.NoContent, (await host.PostJsonAsync($"/admin/upload/items/{uploading.ItemId}/progress",
            new { attemptId = uploading.AttemptId, state = "uploading", percent = 50 })).StatusCode);
        var failed = await StageAsync(host, PhotoB, complete: false);
        Assert.Equal(HttpStatusCode.NoContent, (await host.PostJsonAsync($"/admin/upload/items/{failed.ItemId}/fail",
            new { attemptId = failed.AttemptId, reason = "Out of memory" })).StatusCode);

        // The first three, refused while live.
        var before = (await BatchAsync(host, queued.BatchId))["items"].ToJson();
        foreach (var s in new[] { queued, processing, uploading, failed })
            await AssertErrorAsync(await JoinAsync(host, s), HttpStatusCode.Conflict, "not-active");
        Assert.Equal(before, (await BatchAsync(host, queued.BatchId))["items"].ToJson());

        // And once the three active ones are effectively interrupted.
        host.Clock.Advance(TimeSpan.FromSeconds(121));
        foreach (var s in new[] { queued, processing, uploading })
            await AssertErrorAsync(await JoinAsync(host, s), HttpStatusCode.Conflict, "not-active");

        var batch = await BatchAsync(host, queued.BatchId);
        Assert.Equal(before, batch["items"].ToJson());
        Assert.Equal(["queued", "processing", "uploading", "failed"], batch["items"].AsBsonArray.Select(i => i["state"].AsString).ToArray());
        Assert.Empty(AddedOf(batch));
        Assert.Equal(0, await Images(host).CountDocumentsAsync(FilterDefinition<BsonDocument>.Empty));
        foreach (var s in new[] { queued, processing, uploading, failed })
            Assert.Null(await MetaAsync(Priv, $"masters/{s.ItemId}.jpg"));
    }

    [Fact]
    public async Task Join_of_an_unknown_item_is_404()
    {
        await using var host = await NewHostAsync();
        var a = await StageAsync(host, PhotoA);

        foreach (var id in new[] { OpaqueId.NewId(), "not-an-id" })
        {
            var resp = await host.PostJsonAsync($"/admin/upload/items/{id}/join", new { attemptId = a.AttemptId });
            await AssertErrorAsync(resp, HttpStatusCode.NotFound, "not-found");
        }
        // A stale attempt for a known item is 409, as on the other item routes.
        await AssertErrorAsync(await host.PostJsonAsync($"/admin/upload/items/{a.ItemId}/join", new { attemptId = OpaqueId.NewId() }),
            HttpStatusCode.Conflict, "stale-attempt");
        Assert.Equal(0, await Images(host).CountDocumentsAsync(FilterDefinition<BsonDocument>.Empty));
        Assert.Equal("succeeded", (await ItemAsync(host, a))!["state"].AsString);
    }

    [Fact]
    public async Task Join_without_csrf_is_400()
    {
        await using var host = await NewHostAsync();
        var a = await StageAsync(host, PhotoA);
        var resp = await host.PostJsonAsync($"/admin/upload/items/{a.ItemId}/join", new { attemptId = a.AttemptId }, csrf: false);
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        Assert.Equal("succeeded", (await ItemAsync(host, a))!["state"].AsString);
        Assert.Equal(0, await Images(host).CountDocumentsAsync(FilterDefinition<BsonDocument>.Empty));
    }

    // ── the claim ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Delete_and_restart_of_a_joining_item_are_409_not_active()
    {
        await using var host = await NewHostAsync();
        var a = await StageAsync(host, PhotoA);
        await ClaimAsync(host, a);
        var before = (await BatchAsync(host, a.BatchId)).ToJson();
        var staged = await host.ListAsync(Priv, $"staging/{a.BatchId}/{a.ItemId}/");
        Assert.NotEmpty(staged);

        await AssertErrorAsync(await host.PostJsonAsync($"/admin/upload/items/{a.ItemId}/delete", new { attemptId = a.AttemptId }),
            HttpStatusCode.Conflict, "not-active");
        await AssertErrorAsync(await host.PostJsonAsync($"/admin/upload/items/{a.ItemId}/delete", new { }),
            HttpStatusCode.Conflict, "not-active");
        await AssertErrorAsync(await host.PostJsonAsync($"/admin/upload/items/{a.ItemId}/restart", new { attemptId = a.AttemptId }),
            HttpStatusCode.Conflict, "not-active");

        Assert.Equal(before, (await BatchAsync(host, a.BatchId)).ToJson());
        Assert.Equal("joining", (await ItemAsync(host, a))!["state"].AsString);
        Assert.Equal(staged, await host.ListAsync(Priv, $"staging/{a.BatchId}/{a.ItemId}/"));
    }

    [Fact]
    public async Task Fifty_concurrent_claims_of_one_item_let_exactly_one_join()
    {
        await using var host = await NewHostAsync();
        var a = await StageAsync(host, PhotoA);
        var mark = LogMark(host);

        var responses = await Task.WhenAll(Enumerable.Range(0, 50).Select(_ => JoinAsync(host, a)));

        var statuses = responses.Select(r => r.StatusCode).ToList();
        Assert.Equal(1, statuses.Count(s => s == HttpStatusCode.OK));
        Assert.Equal(49, statuses.Count(s => s == HttpStatusCode.NotFound));
        await AssertJoinedAsync(responses.Single(r => r.StatusCode == HttpStatusCode.OK), a.ItemId);

        var images = await Images(host).Find(FilterDefinition<BsonDocument>.Empty).ToListAsync();
        Assert.Equal([a.ItemId], images.Select(i => i["_id"].AsString).ToArray());
        var lines = LinesSince(host, mark);
        var joined = Assert.Single(lines, l => l.StartsWith("upload.joined ", StringComparison.Ordinal));
        Assert.Matches($"^upload\\.joined {a.BatchId} {a.ItemId} - - [0-9]+(\\.[0-9]+)?$", joined);

        var batch = await BatchAsync(host, a.BatchId);
        Assert.Empty(batch["items"].AsBsonArray);
        Assert.Equal([a.ItemId], AddedOf(batch));
        Assert.Empty(await host.ListAsync(Priv, $"staging/{a.BatchId}/"));
        Assert.NotNull(await MetaAsync(Priv, $"masters/{a.ItemId}.jpg"));
    }

    [Fact]
    public async Task Overlapping_joins_of_one_item_converge()
    {
        await using var host = await NewHostAsync();
        var a = await StageAsync(host, PhotoA);
        var keys = KeysOf(await ValidatedAsync(host, a));
        await ClaimAsync(host, a);

        // The first join pauses after its image upsert (the crash seam), the second runs whole.
        var paused = new TaskCompletionSource();
        var release = new TaskCompletionSource();
        Func<string, Task> hold = async _ => { paused.TrySetResult(); await release.Task; };
        using var scope = host.Services.CreateScope();
        var first = ActivatorUtilities.CreateInstance<ConfirmService>(scope.ServiceProvider, hold);
        var firstRun = first.JoinAsync(a.BatchId, a.ItemId, a.AttemptId);
        await paused.Task.WaitAsync(TimeSpan.FromSeconds(30));

        var mark = LogMark(host);
        await AssertJoinedAsync(await JoinAsync(host, a), a.ItemId);
        release.SetResult();
        var firstResult = await firstRun;

        // The second to finish answers 404 (the item is no longer in the batch): one image, one line.
        Assert.Equal(JoinStatus.NotFound, firstResult.Status);
        await AssertErrorAsync(await JoinAsync(host, a), HttpStatusCode.NotFound, "not-found");
        Assert.Equal([a.ItemId], (await Images(host).Find(FilterDefinition<BsonDocument>.Empty).ToListAsync())
            .Select(i => i["_id"].AsString).ToArray());
        Assert.Single(LinesSince(host, mark), l => l.StartsWith("upload.joined ", StringComparison.Ordinal));
        var batch = await BatchAsync(host, a.BatchId);
        Assert.Empty(batch["items"].AsBsonArray);
        Assert.Equal([a.ItemId], AddedOf(batch));
        Assert.Empty(await host.ListAsync(Priv, $"staging/{a.BatchId}/"));
        foreach (var k in keys) Assert.NotNull(await MetaAsync(Pub, k));
        Assert.NotNull(await MetaAsync(Priv, $"masters/{a.ItemId}.jpg"));
    }

    // ── happy path ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Join_publishes_one_item_and_leaves_the_other_untouched()
    {
        await using var host = await NewHostAsync();
        var a = await StageAsync(host, PhotoA);
        var b = await StageAsync(host, PhotoB);
        Assert.Equal(a.BatchId, b.BatchId);
        var va = await ValidatedAsync(host, a);
        var itemB = (await ItemAsync(host, b))!.ToJson();
        var stagedB = await host.ListAsync(Priv, $"staging/{b.BatchId}/{b.ItemId}/");

        var mark = LogMark(host);
        await AssertJoinedAsync(await JoinAsync(host, a), a.ItemId);

        // One library image, _id = itemId, title from the file name, no seed fields, no gallery.
        Assert.Equal(1, await Images(host).CountDocumentsAsync(FilterDefinition<BsonDocument>.Empty));
        await AssertImageAsync(host, a, va, "IMG_0001");
        Assert.Equal("TESTCAM", (await Images(host).Find(new BsonDocument("_id", a.ItemId)).SingleAsync())["camera"]["make"].AsString);

        // Public sizes with the immutable Cache-Control; the master private; A's staging empty.
        foreach (var s in va["sizes"].AsBsonArray)
        {
            var avif = await MetaAsync(Pub, s["avifKey"].AsString);
            Assert.NotNull(avif);
            Assert.Equal(Immutable, avif!.Headers.CacheControl);
            Assert.Equal("image/avif", avif.Headers.ContentType);
            var webp = await MetaAsync(Pub, s["webpKey"].AsString);
            Assert.NotNull(webp);
            Assert.Equal(Immutable, webp!.Headers.CacheControl);
            Assert.Equal("image/webp", webp.Headers.ContentType);
        }
        var m = await MetaAsync(Priv, $"masters/{a.ItemId}.jpg");
        Assert.NotNull(m);
        Assert.Equal("image/jpeg", m!.Headers.ContentType);
        Assert.Null(await MetaAsync(Pub, $"masters/{a.ItemId}.jpg"));
        Assert.Equal(Bytes(a.Photo.Master).LongLength, m.ContentLength);
        Assert.Empty(await host.ListAsync(Priv, $"staging/{a.BatchId}/{a.ItemId}/"));

        // B is untouched: still succeeded in the batch, staged files kept, no image, no master copy.
        var batch = await BatchAsync(host, a.BatchId);
        Assert.Equal("open", batch["state"].AsString);
        Assert.Equal(itemB, Assert.Single(batch["items"].AsBsonArray).ToJson());
        Assert.Equal([a.ItemId], AddedOf(batch));
        Assert.False(batch.Contains("destination"));
        Assert.False(batch.Contains("confirmStartedAt"));
        Assert.Equal(stagedB, await host.ListAsync(Priv, $"staging/{b.BatchId}/{b.ItemId}/"));
        Assert.Null(await MetaAsync(Priv, $"masters/{b.ItemId}.jpg"));

        // Timing and log lines.
        var lines = LinesSince(host, mark);
        var join = Assert.Single(lines, l => l.StartsWith("op.timing upload.join ", StringComparison.Ordinal));
        Assert.EndsWith(" ok", join);
        Assert.Equal(3, lines.Count(l => l.StartsWith("op.timing s3.copy ", StringComparison.Ordinal) && l.EndsWith(" ok")));
        Assert.Equal(0, lines.Count(l => l.StartsWith("op.timing s3.copy ", StringComparison.Ordinal) && !l.EndsWith(" ok")));
        Assert.Equal(1, lines.Count(l => l.StartsWith("op.timing db.image.upsert ", StringComparison.Ordinal) && l.EndsWith(" ok")));
        var joined = Assert.Single(lines, l => l.StartsWith("upload.joined ", StringComparison.Ordinal));
        Assert.Matches($"^upload\\.joined {a.BatchId} {a.ItemId} - - [0-9]+(\\.[0-9]+)?$", joined);
        Assert.Single(lines, l => l.StartsWith("http.request POST /admin/upload/items/{itemId}/join 200 ", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.StartsWith("op.timing s3.delete ", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.StartsWith("op.timing s3.list ", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.StartsWith("op.timing db.batch.update ", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, l => l.Contains("purge", StringComparison.OrdinalIgnoreCase)
                                          || l.Contains("warm", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(lines, l => l.StartsWith("upload.confirm", StringComparison.Ordinal));

        // A repeat after success is 404 (no longer in the batch); still one image.
        await AssertErrorAsync(await JoinAsync(host, a), HttpStatusCode.NotFound, "not-found");
        Assert.Equal(1, await Images(host).CountDocumentsAsync(FilterDefinition<BsonDocument>.Empty));
    }

    // ── crash and re-run (FR-034) ────────────────────────────────────────────

    [Fact]
    public async Task Join_twice_after_a_simulated_crash()
    {
        await using var host = await NewHostAsync();
        var a = await StageAsync(host, PhotoA);
        var b = await StageAsync(host, PhotoB);
        var expectedPublic = KeysOf(await ValidatedAsync(host, a));
        var pubBefore = (await host.ListAsync(Pub, "")).ToHashSet();
        var itemB = (await ItemAsync(host, b))!.ToJson();

        // A ConfirmService whose afterItem throws after the item's upsert.
        await ClaimAsync(host, a);
        Func<string, Task> crash = _ => throw new InvalidOperationException("simulated crash");
        using (var scope = host.Services.CreateScope())
        {
            var service = ActivatorUtilities.CreateInstance<ConfirmService>(scope.ServiceProvider, crash);
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.JoinAsync(a.BatchId, a.ItemId, a.AttemptId));
        }

        // The item is still joining with its staged files; its image exists.
        var crashed = await BatchAsync(host, a.BatchId);
        Assert.Equal("open", crashed["state"].AsString);
        Assert.Equal(2, crashed["items"].AsBsonArray.Count);
        Assert.Equal("joining", (await ItemAsync(host, a))!["state"].AsString);
        Assert.Empty(AddedOf(crashed));
        Assert.NotEmpty(await host.ListAsync(Priv, $"staging/{a.BatchId}/{a.ItemId}/"));
        var imageBefore = (await Images(host).Find(new BsonDocument("_id", a.ItemId)).SingleAsync()).ToJson();
        Assert.Equal(1, await Images(host).CountDocumentsAsync(FilterDefinition<BsonDocument>.Empty));

        // U12 again finishes it.
        await AssertJoinedAsync(await JoinAsync(host, a), a.ItemId);
        var images = await Images(host).Find(FilterDefinition<BsonDocument>.Empty).ToListAsync();
        Assert.Equal([a.ItemId], images.Select(i => i["_id"].AsString).ToArray());
        // The image created before the crash is unchanged by the re-run ($setOnInsert).
        Assert.Equal(imageBefore, images.Single().ToJson());

        // No duplicate public keys: exactly A's content-addressed names were added.
        var pubAfter = await host.ListAsync(Pub, "");
        Assert.Equal(pubAfter.Count, pubAfter.Distinct().Count());
        Assert.Equal(expectedPublic.Where(k => !pubBefore.Contains(k)).OrderBy(k => k),
            pubAfter.Where(k => !pubBefore.Contains(k)).OrderBy(k => k));
        foreach (var k in expectedPublic) Assert.Contains(k, pubAfter);
        Assert.Empty(await host.ListAsync(Priv, $"staging/{a.BatchId}/{a.ItemId}/"));

        var batch = await BatchAsync(host, a.BatchId);
        Assert.Equal(itemB, Assert.Single(batch["items"].AsBsonArray).ToJson());
        Assert.Equal([a.ItemId], AddedOf(batch));

        // A further U12: 404, still one image.
        await AssertErrorAsync(await JoinAsync(host, a), HttpStatusCode.NotFound, "not-found");
        Assert.Equal(1, await Images(host).CountDocumentsAsync(FilterDefinition<BsonDocument>.Empty));
    }

    [Fact]
    public async Task Joining_is_never_reported_interrupted()
    {
        await using var host = await NewHostAsync();
        var a = await StageAsync(host, PhotoA);
        await ClaimAsync(host, a);

        host.Clock.Advance(TimeSpan.FromMinutes(10));

        var (resp, state) = await host.GetJsonAsync("/admin/upload/state");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var si = Assert.Single(state!.RootElement.GetProperty("batch").GetProperty("items").EnumerateArray());
        Assert.Equal((a.ItemId, "joining"), (si.GetProperty("itemId").GetString(), si.GetProperty("state").GetString()));
        Assert.Equal("joining", (await ItemAsync(host, a))!["state"].AsString);

        // Restart is never offered for it (not-active), and U12 finishes the stuck join.
        await AssertErrorAsync(await host.PostJsonAsync($"/admin/upload/items/{a.ItemId}/restart", new { attemptId = a.AttemptId }),
            HttpStatusCode.Conflict, "not-active");
        await AssertJoinedAsync(await JoinAsync(host, a), a.ItemId);
    }

    // ── copy refused by if-match [SAFETY III.6] ──────────────────────────────

    /// <summary>Two succeeded items; A joined; B's size re-PUT after validation, its join refused.</summary>
    private async Task<(Staged A, Staged B, List<string> KeysB, int Mark, HttpResponseMessage Refused)> RefusedJoinAsync(UploadHost host)
    {
        var a = await StageAsync(host, PhotoA);
        var b = await StageAsync(host, PhotoB);
        var keysB = KeysOf(await ValidatedAsync(host, b));
        await DeletePublicAsync(keysB);
        await AssertJoinedAsync(await JoinAsync(host, a), a.ItemId);

        // Re-PUT item B's 200.avif through the still-valid presigned URL after it succeeded.
        Assert.Equal(HttpStatusCode.OK,
            (await UploadHost.PutToPresignedAsync(b.AvifUrl, Bytes("gps-320.avif"), "image/avif")).StatusCode);

        var mark = LogMark(host);
        return (a, b, keysB, mark, await JoinAsync(host, b));
    }

    [Fact]
    public async Task Reput_after_validation_publishes_nothing()
    {
        await using var host = await NewHostAsync();
        var (a, b, keysB, mark, resp) = await RefusedJoinAsync(host);

        Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
        var json = await JsonAsync(resp);
        Assert.Equal("changed-after-validation", json.GetProperty("error").GetString());
        Assert.Equal("1 photo changed after it was checked. Restart or delete it.", json.GetProperty("message").GetString());

        // Item B: nothing public, no image, failed, item prefix empty, no master copy.
        foreach (var k in keysB) Assert.Null(await MetaAsync(Pub, k));
        Assert.Null(await Images(host).Find(new BsonDocument("_id", b.ItemId)).FirstOrDefaultAsync());
        Assert.Null(await MetaAsync(Priv, $"masters/{b.ItemId}.jpg"));
        Assert.Empty(await host.ListAsync(Priv, $"staging/{b.BatchId}/{b.ItemId}/"));

        // Item A keeps its image; B stays in the open batch, failed; added holds only A.
        Assert.Equal([a.ItemId], (await Images(host).Find(FilterDefinition<BsonDocument>.Empty).ToListAsync())
            .Select(i => i["_id"].AsString).ToArray());
        var batch = await BatchAsync(host, a.BatchId);
        Assert.Equal("open", batch["state"].AsString);
        var only = Assert.Single(batch["items"].AsBsonArray).AsBsonDocument;
        Assert.Equal((b.ItemId, "failed", "changed after validation"),
            (only["itemId"].AsString, only["state"].AsString, only["reason"].AsString));
        Assert.Equal([a.ItemId], AddedOf(batch));

        var lines = LinesSince(host, mark);
        Assert.Equal([$"upload.join-refused {b.BatchId} {b.ItemId} changed after validation"],
            lines.Where(l => l.StartsWith("upload.join-refused ", StringComparison.Ordinal)).ToArray());
        Assert.DoesNotContain(lines, l => l.StartsWith("upload.joined ", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.StartsWith("op.timing s3.copy ", StringComparison.Ordinal) && l.EndsWith(" rejected"));
        Assert.DoesNotContain(lines, l => l.StartsWith("op.timing db.image.upsert ", StringComparison.Ordinal));

        // No lock-out: a repeat join is not-active (failed), and the batch takes new files again.
        await AssertErrorAsync(await JoinAsync(host, b), HttpStatusCode.Conflict, "not-active");
        var reg = await host.PostJsonAsync("/admin/upload/items",
            new { files = new[] { new { name = "c.jpg", size = 1000L, type = "image/jpeg" } } });
        Assert.Equal(HttpStatusCode.OK, reg.StatusCode);
        Assert.Equal(a.BatchId, (await JsonAsync(reg)).GetProperty("batchId").GetString());
    }

    [Fact]
    public async Task Delete_works_on_the_item_a_refused_join_copy_left_failed()
    {
        await using var host = await NewHostAsync();
        var (a, b, _, _, refused) = await RefusedJoinAsync(host);
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);

        var resp = await host.PostJsonAsync($"/admin/upload/items/{b.ItemId}/delete", new { attemptId = b.AttemptId });
        Assert.Equal(HttpStatusCode.NoContent, resp.StatusCode);

        var batch = await BatchAsync(host, a.BatchId);
        Assert.Equal("open", batch["state"].AsString);
        Assert.Empty(batch["items"].AsBsonArray);
        Assert.Empty(await host.ListAsync(Priv, StagingKeys.ItemPrefix(b.BatchId, b.ItemId)));
        Assert.Null(await Images(host).Find(new BsonDocument("_id", b.ItemId)).FirstOrDefaultAsync());

        // The set can be cleared (no lock-out): nothing left to drop.
        var clear = await ClearAsync(host, a.BatchId);
        Assert.Equal(HttpStatusCode.OK, clear.StatusCode);
        var cj = await JsonAsync(clear);
        Assert.Equal(["dropped"], cj.EnumerateObject().Select(p => p.Name).ToArray());
        Assert.Equal(0, cj.GetProperty("dropped").GetInt32());
    }

    [Fact]
    public async Task Restart_of_the_item_a_refused_join_copy_left_failed_asks_for_the_file()
    {
        await using var host = await NewHostAsync();
        var (_, b, _, _, refused) = await RefusedJoinAsync(host);
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);

        // Its staged master was deleted with the item prefix, so there is nothing to copy.
        var resp = await host.PostJsonAsync($"/admin/upload/items/{b.ItemId}/restart", new { attemptId = b.AttemptId });
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var json = await JsonAsync(resp);
        Assert.True(json.GetProperty("needsFile").GetBoolean());
        Assert.Equal("Harbour at dusk.jpg", json.GetProperty("fileName").GetString());
        var item = (await ItemAsync(host, b))!;
        Assert.Equal("queued", item["state"].AsString);
        Assert.True(item["master"].IsBsonNull);
        Assert.True(item["validated"].IsBsonNull);
    }

    // ── II.5b: no destination → no gallery call, no edge work ───────────────

    [Fact]
    public async Task Join_with_no_destination_calls_no_gallery_method_and_enqueues_no_edge_work()
    {
        var recorder = new RecordingEdgeProvider();
        await using var host = await NewEdgeHostAsync(recorder);
        // A public gallery heads site.homeOrder, so a stray add would have somewhere to go.
        var pub = await GalleryAsync(host, "Coast", makePublic: true);
        var a = await StageAsync(host, PhotoA);
        await EdgeIdleAsync(host);
        recorder.Clear();
        var galleriesBefore = await GalleriesDumpAsync(host);
        var siteBefore = (await Db(host).GetCollection<BsonDocument>("site").Find(FilterDefinition<BsonDocument>.Empty).SingleAsync()).ToJson();

        var mark = LogMark(host);
        await AssertJoinedAsync(await JoinAsync(host, a), a.ItemId);
        await EdgeIdleAsync(host);

        Assert.Empty(recorder.AllPurged());
        Assert.Empty(recorder.AllWarmed());
        Assert.Equal(galleriesBefore, await GalleriesDumpAsync(host));
        Assert.Equal(siteBefore, (await Db(host).GetCollection<BsonDocument>("site").Find(FilterDefinition<BsonDocument>.Empty).SingleAsync()).ToJson());
        Assert.Empty((await Images(host).Find(new BsonDocument("_id", a.ItemId)).SingleAsync())["galleryIds"].AsBsonArray);
        var lines = LinesSince(host, mark);
        Assert.DoesNotContain(lines, l => l.StartsWith("owner.action gallery.", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, l => l.StartsWith("op.timing db.gallery.", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, l => l.StartsWith("op.timing db.galleries.", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, l => l.StartsWith("op.timing db.site.project ", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, l => l.StartsWith("op.timing cache.evict ", StringComparison.Ordinal));
        Assert.Single(lines, l => l.StartsWith($"upload.joined {a.BatchId} {a.ItemId} - - ", StringComparison.Ordinal));
        Assert.Equal("public", (await StoredGalleryAsync(host, pub)).Visibility);
    }

    [Fact]
    public async Task Public_pages_byte_identical_around_a_join_with_no_destination()
    {
        await using var host = await NewHostAsync();
        var db = Db(host);
        const string galId = "aaaaaaaaaaaaaaaaaaaaaaaa22";
        const string emptyId = "dddddddddddddddddddddddd22";
        const string imgId = "eeeeeeeeeeeeeeeeeeeeeeee22";
        var deriv = new DerivativeDoc { W = 320, Avif = "hash320.avif", Webp = "hash320.webp", BytesAvif = 100, BytesWebp = 80 };
        var ph = new PlaceholderDoc { Color = "#aabbcc", Preview = "data:image/webp;base64,AAAA" };
        var cover = new CoverImageDoc { ImageId = imgId, Width = 1280, Height = 960, Placeholder = ph, Derivatives = [deriv] };
        var card = new GalleryCardDoc { GalleryId = galId, Slug = "autumn-walk", Title = "Autumn Walk", ImageCount = 1, Cover = cover };
        var emptyCard = new GalleryCardDoc { GalleryId = emptyId, Slug = "coming-soon", Title = "Coming Soon" };
        await db.GetCollection<ImageDoc>("images").InsertOneAsync(new ImageDoc
        {
            Id = imgId, SeedKey = imgId, SourceHash = imgId, Title = "Image One", Width = 1280, Height = 960,
            Placeholder = ph, Derivatives = [deriv], GalleryIds = [galId], CreatedAt = DateTime.UtcNow,
        });
        await db.GetCollection<GalleryDoc>("galleries").InsertManyAsync(
        [
            new GalleryDoc { Id = galId, SeedKey = "pub1", Slug = "autumn-walk", Title = "Autumn Walk", Visibility = "public", Items = [imgId], Card = card, UpdatedAt = DateTime.UtcNow },
            new GalleryDoc { Id = emptyId, SeedKey = "empty1", Slug = "coming-soon", Title = "Coming Soon", Visibility = "public", Items = [], Card = emptyCard, UpdatedAt = DateTime.UtcNow },
        ]);
        await db.GetCollection<SiteDoc>("site").UpdateOneAsync(
            Builders<SiteDoc>.Filter.Eq(d => d.Id, "site"),
            Builders<SiteDoc>.Update.Set(d => d.HomeOrder, [galId, emptyId]).Set(d => d.HomeCards, [card, emptyCard])
                .Set(d => d.UpdatedAt, DateTime.UtcNow),
            new UpdateOptions { IsUpsert = true });

        // The empty gallery answers 404 by design (II.6); its body must not move either.
        (string Path, HttpStatusCode Status)[] paths =
        [
            ("/", HttpStatusCode.OK),
            ("/g", HttpStatusCode.OK),
            ($"/g/{galId}/autumn-walk", HttpStatusCode.OK),
            ($"/g/{galId}/autumn-walk/{imgId}", HttpStatusCode.OK),
            ($"/g/{emptyId}/coming-soon", HttpStatusCode.NotFound),
        ];

        async Task<List<string>> BodiesAsync()
        {
            var bodies = new List<string>();
            foreach (var (p, status) in paths)
            {
                var r = await host.GetAsync(p);
                Assert.True(r.StatusCode == status, $"{p} answered {(int)r.StatusCode}");
                bodies.Add(await r.Content.ReadAsStringAsync());
            }
            return bodies;
        }

        var before = await BodiesAsync();
        var a = await StageAsync(host, PhotoA);
        var b = await StageAsync(host, PhotoB);
        await AssertJoinedAsync(await JoinAsync(host, a), a.ItemId);
        await AssertJoinedAsync(await JoinAsync(host, b), b.ItemId);
        Assert.Equal(3, await Images(host).CountDocumentsAsync(FilterDefinition<BsonDocument>.Empty));

        var after = await BodiesAsync();
        Assert.Contains("Autumn Walk", before[1]);
        for (var i = 0; i < paths.Length; i++)
            Assert.Equal(before[i], after[i]);
    }

    // ── II.5c: a join never writes visibility ────────────────────────────────

    [Fact]
    public async Task Join_never_changes_visibility()
    {
        var recorder = new RecordingEdgeProvider();
        await using var host = await NewEdgeHostAsync(recorder);
        var priv = await GalleryAsync(host, "Private with one", withPhoto: true);
        var empty = await GalleryAsync(host, "Empty private");
        var pub = await GalleryAsync(host, "Public with one", makePublic: true);
        var mark = LogMark(host);

        async Task<Staged> JoinIntoAsync(string gid, string title, bool isPublic, Photo p)
        {
            var s = await StageAsync(host, p, gallery: gid, visible: isPublic ? true : null);
            await AssertJoinedAsync(await JoinAsync(host, s), s.ItemId, (gid, title, isPublic));
            Assert.Single(LinesSince(host, mark), l => System.Text.RegularExpressions.Regex.IsMatch(l,
                $"^upload\\.joined {s.BatchId} {s.ItemId} {gid} {(isPublic ? "true" : "false")} [0-9]+(\\.[0-9]+)?$"));
            var batch = await BatchAsync(host, s.BatchId);
            Assert.Equal(gid, batch["destination"].AsString);
            Assert.Equal([s.ItemId], AddedOf(batch));
            // Close the set so the next one can aim elsewhere.
            var clear = await ClearAsync(host, s.BatchId);
            Assert.Equal(HttpStatusCode.OK, clear.StatusCode);
            return s;
        }

        var a = await JoinIntoAsync(priv, "Private with one", false, PhotoA);
        var b = await JoinIntoAsync(empty, "Empty private", false, PhotoB);
        var c = await JoinIntoAsync(pub, "Public with one", true, PhotoA);

        var gp = await StoredGalleryAsync(host, priv);
        Assert.Equal("private", gp.Visibility);
        Assert.Equal(a.ItemId, gp.Items[^1]);
        var ge = await StoredGalleryAsync(host, empty);
        Assert.Equal("private", ge.Visibility);
        Assert.Equal([b.ItemId], ge.Items.ToArray());
        var gq = await StoredGalleryAsync(host, pub);
        Assert.Equal("public", gq.Visibility);
        Assert.Equal(c.ItemId, gq.Items[^1]);
        Assert.Equal([priv], (await Images(host).Find(new BsonDocument("_id", a.ItemId)).SingleAsync())["galleryIds"].AsBsonArray.Select(x => x.AsString).ToArray());

        var lines = LinesSince(host, mark);
        Assert.DoesNotContain(lines, l => l.StartsWith("owner.action gallery.publish", StringComparison.Ordinal)
                                          || l.StartsWith("owner.action gallery.unpublish", StringComparison.Ordinal));
        // Joining into a gallery is one ordinary add: edge work is queued for it.
        await EdgeIdleAsync(host);
        Assert.NotEmpty(recorder.AllPurged());
    }

    // ── II.5a and the destination at U3 ──────────────────────────────────────

    [Fact]
    public async Task Register_into_a_public_gallery_without_the_flag_is_409_destination_public()
    {
        await using var host = await NewHostAsync();
        var pub = await GalleryAsync(host, "Harbour", makePublic: true);

        foreach (var flag in new bool?[] { null, false })
        {
            var mark = LogMark(host);
            var resp = await RegisterAsync(host, PhotoA, pub, flag);
            Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
            var json = await JsonAsync(resp);
            Assert.Equal(["error", "message"], json.EnumerateObject().Select(p => p.Name).ToArray());
            Assert.Equal("destination-public", json.GetProperty("error").GetString());
            Assert.Equal("Harbour is public. Visitors will see these photos.", json.GetProperty("message").GetString());

            var batch = await OpenBatchAsync(host);
            Assert.Empty(batch["items"].AsBsonArray);
            Assert.Empty(AddedOf(batch));
            Assert.Empty(await host.ListAsync(Priv, StagingKeys.BatchPrefix(batch["_id"].AsString)));
            Assert.Equal([$"upload.destination-refused {batch["_id"].AsString} {pub}"],
                LinesSince(host, mark).Where(l => l.StartsWith("upload.destination-refused ", StringComparison.Ordinal)).ToArray());
            Assert.DoesNotContain(LinesSince(host, mark), l => l.StartsWith("op.timing s3.presign ", StringComparison.Ordinal));
        }

        var (_, state) = await host.GetJsonAsync("/admin/upload/state");
        Assert.Equal(JsonValueKind.Null, state!.RootElement.GetProperty("batch").ValueKind);
        Assert.Equal(0, await Images(host).CountDocumentsAsync(new BsonDocument("title", "IMG_0001")));
    }

    [Fact]
    public async Task Register_into_a_public_gallery_with_the_flag_names_the_destination()
    {
        await using var host = await NewHostAsync();
        var pub = await GalleryAsync(host, "Harbour", makePublic: true);

        var resp = await RegisterAsync(host, PhotoA, pub, true);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var json = await JsonAsync(resp);
        Assert.Equal(["batchId", "destination", "accepted", "refused"], json.EnumerateObject().Select(p => p.Name).ToArray());
        var d = json.GetProperty("destination");
        Assert.Equal((pub, "Harbour", true), (d.GetProperty("id").GetString(), d.GetProperty("title").GetString(), d.GetProperty("public").GetBoolean()));
        Assert.Single(json.GetProperty("accepted").EnumerateArray());

        var (_, state) = await host.GetJsonAsync("/admin/upload/state");
        var b = state!.RootElement.GetProperty("batch");
        Assert.False(b.GetProperty("confirming").GetBoolean());
        Assert.Equal(pub, b.GetProperty("destination").GetProperty("id").GetString());
        Assert.Equal("Harbour", b.GetProperty("destination").GetProperty("title").GetString());
        Assert.True(b.GetProperty("destination").GetProperty("public").GetBoolean());
        Assert.Equal(0, b.GetProperty("added").GetArrayLength());
        Assert.Equal(pub, (await OpenBatchAsync(host))["destination"].AsString);
    }

    [Fact]
    public async Task Destination_is_set_only_on_an_empty_set_and_the_stored_one_wins()
    {
        await using var host = await NewHostAsync();
        var first = await GalleryAsync(host, "First");
        var second = await GalleryAsync(host, "Second");

        // An unknown gallery on an empty set: 404 gallery-gone, nothing registered.
        foreach (var unknown in new[] { OpaqueId.NewId(), "not-an-id" })
        {
            var gone = await RegisterAsync(host, PhotoA, unknown);
            await AssertErrorAsync(gone, HttpStatusCode.NotFound, "gallery-gone");
            Assert.Empty((await OpenBatchAsync(host))["items"].AsBsonArray);
        }
        Assert.False((await OpenBatchAsync(host)).Contains("destination"));

        static string? DestinationOf(JsonElement json) =>
            json.GetProperty("destination").ValueKind == JsonValueKind.Null ? null : json.GetProperty("destination").GetProperty("id").GetString();

        // The empty set takes "First"; afterwards the stored one wins whatever is sent.
        var r1 = await JsonAsync(await RegisterAsync(host, PhotoA, first));
        Assert.Equal(first, DestinationOf(r1));
        var r2 = await JsonAsync(await RegisterAsync(host, PhotoB, second));
        Assert.Equal(first, DestinationOf(r2));
        var r3 = await JsonAsync(await RegisterAsync(host, PhotoB));
        Assert.Equal(first, DestinationOf(r3));
        var r4 = await RegisterAsync(host, PhotoB, OpaqueId.NewId());
        Assert.Equal(HttpStatusCode.OK, r4.StatusCode);
        Assert.Equal(first, DestinationOf(await JsonAsync(r4)));
        var batch = await OpenBatchAsync(host);
        Assert.Equal(first, batch["destination"].AsString);
        Assert.Equal(4, batch["items"].AsBsonArray.Count);

        // Once every item is removed (and nothing was added) the set is empty again.
        foreach (var item in batch["items"].AsBsonArray)
            Assert.Equal(HttpStatusCode.NoContent, (await host.PostJsonAsync($"/admin/upload/items/{item["itemId"].AsString}/delete", new { })).StatusCode);
        var r5 = await JsonAsync(await RegisterAsync(host, PhotoA, second));
        Assert.Equal(second, DestinationOf(r5));
        Assert.Equal(second, (await OpenBatchAsync(host))["destination"].AsString);

        // With an added photo the set is not empty: the stored destination stays.
        var s = await StageAsync(host, PhotoB);
        await AssertJoinedAsync(await JoinAsync(host, s), s.ItemId, (second, "Second", false));
        foreach (var item in (await OpenBatchAsync(host))["items"].AsBsonArray)
            Assert.Equal(HttpStatusCode.NoContent, (await host.PostJsonAsync($"/admin/upload/items/{item["itemId"].AsString}/delete", new { })).StatusCode);
        var r6 = await JsonAsync(await RegisterAsync(host, PhotoA, first));
        Assert.Equal(second, DestinationOf(r6));
        Assert.Equal([s.ItemId], AddedOf(await OpenBatchAsync(host)));
    }

    // ── gallery gone, added, clear ───────────────────────────────────────────

    [Fact]
    public async Task Gallery_deleted_mid_upload_joins_the_library_only()
    {
        await using var host = await NewHostAsync();
        var gid = await GalleryAsync(host, "Short lived");
        var a = await StageAsync(host, PhotoA, gallery: gid);
        Assert.Equal(gid, (await BatchAsync(host, a.BatchId))["destination"].AsString);
        using (var scope = host.Services.CreateScope())
            Assert.True((await scope.ServiceProvider.GetRequiredService<GalleryService>().DeleteAsync(gid)).IsOk);

        var mark = LogMark(host);
        await AssertJoinedAsync(await JoinAsync(host, a), a.ItemId, galleryGone: true);

        Assert.Empty((await Images(host).Find(new BsonDocument("_id", a.ItemId)).SingleAsync())["galleryIds"].AsBsonArray);
        var batch = await BatchAsync(host, a.BatchId);
        Assert.False(batch.Contains("destination"));
        Assert.Equal([a.ItemId], AddedOf(batch));
        Assert.Single(LinesSince(host, mark), l => l.StartsWith($"upload.joined {a.BatchId} {a.ItemId} - - ", StringComparison.Ordinal));

        var (_, state) = await host.GetJsonAsync("/admin/upload/state");
        Assert.Equal(JsonValueKind.Null, state!.RootElement.GetProperty("batch").GetProperty("destination").ValueKind);
    }

    [Fact]
    public async Task Added_survives_a_reload_in_U2()
    {
        await using var host = await NewHostAsync();
        var a = await StageAsync(host, PhotoA);
        var b = await StageAsync(host, PhotoB);
        await AssertJoinedAsync(await JoinAsync(host, b), b.ItemId);
        await AssertJoinedAsync(await JoinAsync(host, a), a.ItemId);

        // No items left, two added: U2 still reports the set (it is not "no batch").
        var (resp, state) = await host.GetJsonAsync("/admin/upload/state");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var batch = state!.RootElement.GetProperty("batch");
        Assert.Equal(["id", "confirming", "destination", "added", "limits", "items"],
            batch.EnumerateObject().Select(p => p.Name).ToArray());
        Assert.Equal(a.BatchId, batch.GetProperty("id").GetString());
        Assert.False(batch.GetProperty("confirming").GetBoolean());
        Assert.Equal(JsonValueKind.Null, batch.GetProperty("destination").ValueKind);
        Assert.Equal([b.ItemId, a.ItemId], batch.GetProperty("added").EnumerateArray().Select(x => x.GetString()!).ToArray());
        Assert.Equal(0, batch.GetProperty("items").GetArrayLength());

        // Clearing empties the set; U2 then answers no batch.
        Assert.Equal(HttpStatusCode.OK, (await ClearAsync(host, a.BatchId)).StatusCode);
        var (_, cleared) = await host.GetJsonAsync("/admin/upload/state");
        Assert.Equal(JsonValueKind.Null, cleared!.RootElement.GetProperty("batch").ValueKind);
    }

    [Fact]
    public async Task Clear_refuses_while_working_and_drops_failed_and_interrupted()
    {
        await using var host = await NewHostAsync();
        var gid = await GalleryAsync(host, "Trip");
        var done = await StageAsync(host, PhotoA, gallery: gid);
        var stalled = await StageAsync(host, PhotoB, complete: false);
        var failed = await StageAsync(host, PhotoB, complete: false);
        Assert.Equal(HttpStatusCode.NoContent, (await host.PostJsonAsync($"/admin/upload/items/{failed.ItemId}/fail",
            new { attemptId = failed.AttemptId, reason = "Out of memory" })).StatusCode);

        // Unknown or not-open batch: 404.
        foreach (var id in new[] { OpaqueId.NewId(), "nope" })
            await AssertErrorAsync(await ClearAsync(host, id), HttpStatusCode.NotFound, "not-found");

        // A succeeded (not yet joined) and a live queued item: still working.
        async Task AssertStillWorkingAsync(int items)
        {
            var resp = await ClearAsync(host, done.BatchId);
            await AssertErrorAsync(resp, HttpStatusCode.Conflict, "still-working");
            Assert.Equal("Wait for the photos that are still working, or remove them.", (await JsonAsync(resp)).GetProperty("message").GetString());
            Assert.Equal(items, (await BatchAsync(host, done.BatchId))["items"].AsBsonArray.Count);
        }
        await AssertStillWorkingAsync(3);
        await ClaimAsync(host, done); // joining also counts as working
        await AssertStillWorkingAsync(3);
        await AssertJoinedAsync(await JoinAsync(host, done), done.ItemId, (gid, "Trip", false));
        await AssertStillWorkingAsync(2); // the stalled item is still live

        host.Clock.Advance(TimeSpan.FromSeconds(121)); // the stalled item is now interrupted
        Assert.NotEmpty(await host.ListAsync(Priv, StagingKeys.ItemPrefix(stalled.BatchId, stalled.ItemId)));
        Assert.NotEmpty(await host.ListAsync(Priv, StagingKeys.ItemPrefix(failed.BatchId, failed.ItemId)));

        var mark = LogMark(host);
        var ok = await ClearAsync(host, done.BatchId);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        var json = await JsonAsync(ok);
        Assert.Equal(["dropped", "message"], json.EnumerateObject().Select(p => p.Name).ToArray());
        Assert.Equal(2, json.GetProperty("dropped").GetInt32());
        Assert.Equal("2 were not added.", json.GetProperty("message").GetString());

        Assert.Empty(await host.ListAsync(Priv, StagingKeys.BatchPrefix(done.BatchId)));
        var batch = await BatchAsync(host, done.BatchId);
        Assert.Equal("open", batch["state"].AsString);
        Assert.Empty(batch["items"].AsBsonArray);
        Assert.Empty(AddedOf(batch));
        Assert.False(batch.Contains("destination"));
        Assert.Equal([$"upload.cleared {done.BatchId} 2"],
            LinesSince(host, mark).Where(l => l.StartsWith("upload.cleared ", StringComparison.Ordinal)).ToArray());

        // The joined photo stays where it went.
        Assert.Equal([gid], (await Images(host).Find(new BsonDocument("_id", done.ItemId)).SingleAsync())["galleryIds"].AsBsonArray.Select(x => x.AsString).ToArray());
        var (_, state) = await host.GetJsonAsync("/admin/upload/state");
        Assert.Equal(JsonValueKind.Null, state!.RootElement.GetProperty("batch").ValueKind);
    }

    // ── seed coexistence (FR-035), through U12 ───────────────────────────────

    [Fact]
    public async Task Removal_command_after_join_leaves_uploaded_images()
    {
        // A recording edge provider: the removal's purge never reaches a real edge.
        await using var host = await UploadHost.CreateAsync(_mongo, _google, _s3, configure: b =>
        {
            b.Services.RemoveAll<IEdgeProvider>();
            b.Services.AddSingleton<IEdgeProvider>(new RecordingEdgeProvider());
        });
        var a = await StageAsync(host, PhotoA);
        var b = await StageAsync(host, PhotoB);
        await AssertJoinedAsync(await JoinAsync(host, a), a.ItemId);
        await AssertJoinedAsync(await JoinAsync(host, b), b.ItemId);

        var images = Images(host);
        var before = (await images.Find(FilterDefinition<BsonDocument>.Empty).ToListAsync())
            .ToDictionary(d => d["_id"].AsString, d => d.ToJson());
        Assert.Equal(new[] { a.ItemId, b.ItemId }.OrderBy(x => x), before.Keys.OrderBy(x => x));

        // Control: a seeded image IS removed by the same run.
        var controlId = OpaqueId.NewId();
        await images.InsertOneAsync(new BsonDocument { ["_id"] = controlId, ["seedKey"] = "seed-control", ["title"] = "old shape" });

        var summary = await RemoveSeededCommand.RemoveAsync(host.Services);
        Assert.Equal(1, summary.ImagesRemoved);

        Assert.Null(await images.Find(new BsonDocument("_id", controlId)).FirstOrDefaultAsync());
        foreach (var (id, json) in before)
        {
            var after = await images.Find(new BsonDocument("_id", id)).FirstOrDefaultAsync();
            Assert.NotNull(after);
            Assert.Equal(json, after.ToJson());
        }
    }

    [Fact]
    public async Task Join_after_seeded_content_adds_both_images()
    {
        await using var host = await NewHostAsync();
        var seededImage = SeedShapes.SeededImage();
        var seededGallery = SeedShapes.SeededGallery([seededImage], title: "Gallery One");
        SeedShapes.Link([seededImage], [seededGallery]);
        await Db(host).GetCollection<ImageDoc>("images").InsertOneAsync(seededImage);
        await Db(host).GetCollection<GalleryDoc>("galleries").InsertOneAsync(seededGallery);
        await Indexes.EnsureAsync(Db(host));

        var a = await StageAsync(host, PhotoA);
        var b = await StageAsync(host, PhotoB);
        await AssertJoinedAsync(await JoinAsync(host, a), a.ItemId);
        await AssertJoinedAsync(await JoinAsync(host, b), b.ItemId);
        foreach (var id in new[] { a.ItemId, b.ItemId })
            Assert.NotNull(await Images(host).Find(new BsonDocument("_id", id)).FirstOrDefaultAsync());
    }

    /// <summary>
    /// 008 decisions row 60: a dev database seeded BEFORE 008 still has the plain unique seedKey_1
    /// index. The join upgrades it to the partial form itself.
    /// </summary>
    [Fact]
    public async Task Join_upgrades_a_legacy_seedKey_index_and_adds_both_images()
    {
        await using var host = await NewHostAsync();
        await Images(host).Indexes.CreateOneAsync(new CreateIndexModel<BsonDocument>(
            Builders<BsonDocument>.IndexKeys.Ascending("seedKey"),
            new CreateIndexOptions { Unique = true, Name = "seedKey_1" }));

        var a = await StageAsync(host, PhotoA);
        var b = await StageAsync(host, PhotoB);
        await AssertJoinedAsync(await JoinAsync(host, a), a.ItemId);
        await AssertJoinedAsync(await JoinAsync(host, b), b.ItemId);
        foreach (var id in new[] { a.ItemId, b.ItemId })
            Assert.NotNull(await Images(host).Find(new BsonDocument("_id", id)).FirstOrDefaultAsync());
    }
}
