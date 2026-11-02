using System.Net;
using System.Text.Json;
using Amazon.S3;
using Amazon.S3.Model;
using Gallery.Core.Ids;
using Gallery.Web.Data;
using Gallery.Web.Maintenance;
using Gallery.Web.Storage;
using Gallery.Web.Tests.Fixtures;
using Gallery.Web.Upload;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Bson;
using MongoDB.Driver;
using static Gallery.Web.Tests.Fixtures.UploadFixtures;

namespace Gallery.Web.Tests;

/// <summary>
/// 008 T031 [SAFETY]: U10 confirm (research R-09, contracts/http.md U10, FR-030–FR-035, plan
/// item 6). Every staged file is copied with if-match on its validated ETag; a file re-PUT after
/// validation publishes nothing and does not lock the batch; a repeat confirm (after a crash or a
/// success) creates nothing twice; uploaded images carry no seed key and survive a seed run; no
/// public page changes.
/// </summary>
[Collection("s3")]
public class UploadConfirmTests : IClassFixture<MongoFixture>, IAsyncLifetime
{
    private const string Pub = "pub";
    private const string Priv = "priv";
    private const string Immutable = "public, max-age=31536000, immutable";
    private const string Color = "#a1b2c3";

    private readonly S3ProxyFixture _s3;
    private readonly MongoFixture _mongo;
    private readonly FakeGoogle _google = new();

    public UploadConfirmTests(S3ProxyFixture s3, MongoFixture mongo)
    {
        _s3 = s3;
        _mongo = mongo;
    }

    public Task InitializeAsync() => _google.InitializeAsync();
    public Task DisposeAsync() => _google.DisposeAsync();

    private Task<UploadHost> NewHostAsync() => UploadHost.CreateAsync(_mongo, _google, _s3);

    // ── fixtures: two photos with distinct content ───────────────────────────

    private sealed record Photo(string Name, string Master, int W, string Avif, string Webp, string Placeholder);

    // gps-camera.jpg is 400×200 → ladder [320]; orientation-6.jpg is upright 200×400 → [200].
    private static readonly Photo PhotoA = new("IMG_0001.jpg", "gps-camera.jpg", 320, "gps-320.avif", "gps-320.webp", "gps-placeholder.webp");
    private static readonly Photo PhotoB = new("Harbour at dusk.jpg", "orientation-6.jpg", 200, "orient6-200.avif", "orient6-200.webp", "orient6-placeholder.webp");

    private sealed record Staged(string BatchId, string ItemId, string AttemptId, Photo Photo, string AvifUrl, string WebpUrl);

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage resp) =>
        JsonDocument.Parse(await resp.Content.ReadAsStringAsync()).RootElement.Clone();

    /// <summary>Register, PUT master, U4, PUT both sizes. Optionally U7 to succeeded.</summary>
    private static async Task<Staged> StageAsync(UploadHost host, Photo p, bool complete = true)
    {
        var master = Bytes(p.Master);
        var reg = await host.PostJsonAsync("/admin/upload/items",
            new { files = new[] { new { name = p.Name, size = master.LongLength, type = "image/jpeg" } } });
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

    private static Task<HttpResponseMessage> ConfirmAsync(UploadHost host, string batchId) =>
        host.PostJsonAsync("/admin/upload/confirm", new { batchId });

    private static IMongoDatabase Db(UploadHost host) =>
        host.Services.GetRequiredService<IMongoClient>().GetDatabase(host.DatabaseName);

    private static IMongoCollection<BsonDocument> Images(UploadHost host) => Db(host).GetCollection<BsonDocument>("images");

    private static async Task<BsonDocument> BatchAsync(UploadHost host, string batchId) =>
        await Db(host).GetCollection<BsonDocument>(BatchStore.CollectionName)
            .Find(new BsonDocument("_id", batchId)).SingleAsync();

    private static async Task<BsonDocument> ValidatedAsync(UploadHost host, Staged s) =>
        (await BatchAsync(host, s.BatchId))["items"].AsBsonArray.Select(i => i.AsBsonDocument)
            .Single(i => i["itemId"] == s.ItemId)["validated"].AsBsonDocument;

    private static List<string> KeysOf(BsonDocument validated) =>
        validated["sizes"].AsBsonArray.SelectMany(s => new[] { s["avifKey"].AsString, s["webpKey"].AsString }).ToList();

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

    private static async Task AssertImageAsync(UploadHost host, Staged s, BsonDocument validated, string title)
    {
        var img = await Images(host).Find(new BsonDocument("_id", s.ItemId)).SingleAsync();
        Assert.Equal(title, img["title"].AsString);
        Assert.False(img.Contains("seedKey"));
        Assert.False(img.Contains("sourceHash"));
        Assert.False(img.Contains("caption"));
        Assert.Empty(img["galleryIds"].AsBsonArray);
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

    // ── gate ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Confirm_with_one_item_still_uploading_is_409_not_ready()
    {
        await using var host = await NewHostAsync();
        var a = await StageAsync(host, PhotoA);
        var b = await StageAsync(host, PhotoB, complete: false);
        Assert.Equal(HttpStatusCode.NoContent, (await host.PostJsonAsync($"/admin/upload/items/{b.ItemId}/progress",
            new { attemptId = b.AttemptId, state = "uploading", percent = 50 })).StatusCode);

        var resp = await ConfirmAsync(host, a.BatchId);
        Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
        var json = await JsonAsync(resp);
        Assert.Equal("not-ready", json.GetProperty("error").GetString());
        Assert.Equal(1, json.GetProperty("notReady").GetInt32());
        Assert.Equal("Restart or delete the 1 photo that isn't ready", json.GetProperty("message").GetString());

        var batch = await BatchAsync(host, a.BatchId);
        Assert.Equal("open", batch["state"].AsString);
        Assert.False(batch.Contains("confirmStartedAt"));
        Assert.Equal(0, await Images(host).CountDocumentsAsync(FilterDefinition<BsonDocument>.Empty));
    }

    [Fact]
    public async Task Confirm_with_two_not_ready_items_counts_both()
    {
        await using var host = await NewHostAsync();
        var a = await StageAsync(host, PhotoA, complete: false);
        await StageAsync(host, PhotoB, complete: false);

        var resp = await ConfirmAsync(host, a.BatchId);
        Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
        var json = await JsonAsync(resp);
        Assert.Equal("not-ready", json.GetProperty("error").GetString());
        Assert.Equal(2, json.GetProperty("notReady").GetInt32());
        Assert.Equal("Restart or delete the 2 photos that aren't ready", json.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Confirm_with_zero_items_is_409_not_ready()
    {
        await using var host = await NewHostAsync();
        var now = host.Clock.GetUtcNow().UtcDateTime;
        var batchId = OpaqueId.NewId();
        await Db(host).GetCollection<BatchDoc>(BatchStore.CollectionName).InsertOneAsync(new BatchDoc
        {
            Id = batchId, State = BatchStates.Open, CreatedAt = now, UpdatedAt = now,
        });

        var resp = await ConfirmAsync(host, batchId);
        Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
        var json = await JsonAsync(resp);
        Assert.Equal("not-ready", json.GetProperty("error").GetString());
        Assert.Equal(0, json.GetProperty("notReady").GetInt32());
        Assert.False((await BatchAsync(host, batchId)).Contains("confirmStartedAt"));
    }

    [Fact]
    public async Task Confirm_unknown_or_invalid_batch_is_404()
    {
        await using var host = await NewHostAsync();
        await StageAsync(host, PhotoA);

        foreach (var id in new[] { OpaqueId.NewId(), "not-an-id" })
        {
            var resp = await ConfirmAsync(host, id);
            Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
            Assert.Equal("not-found", (await JsonAsync(resp)).GetProperty("error").GetString());
        }
        Assert.Equal(0, await Images(host).CountDocumentsAsync(FilterDefinition<BsonDocument>.Empty));
    }

    [Fact]
    public async Task Confirm_without_csrf_is_400()
    {
        await using var host = await NewHostAsync();
        var a = await StageAsync(host, PhotoA);
        var resp = await host.PostJsonAsync("/admin/upload/confirm", new { batchId = a.BatchId }, csrf: false);
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        Assert.False((await BatchAsync(host, a.BatchId)).Contains("confirmStartedAt"));
    }

    [Fact]
    public async Task Mutations_on_a_confirming_batch_are_409_confirm_in_progress()
    {
        // U11 (discard) is Batch F's; the same shared rule is asserted here on U3 and U5.
        await using var host = await NewHostAsync();
        var a = await StageAsync(host, PhotoA);
        await Db(host).GetCollection<BatchDoc>(BatchStore.CollectionName).UpdateOneAsync(
            Builders<BatchDoc>.Filter.Eq(b => b.Id, a.BatchId),
            Builders<BatchDoc>.Update.Set(b => b.ConfirmStartedAt, host.Clock.GetUtcNow().UtcDateTime));

        var reg = await host.PostJsonAsync("/admin/upload/items",
            new { files = new[] { new { name = "c.jpg", size = 1000L, type = "image/jpeg" } } });
        Assert.Equal(HttpStatusCode.Conflict, reg.StatusCode);
        Assert.Equal("confirm-in-progress", (await JsonAsync(reg)).GetProperty("error").GetString());

        var prog = await host.PostJsonAsync($"/admin/upload/items/{a.ItemId}/progress",
            new { attemptId = a.AttemptId, state = "uploading", percent = 10 });
        Assert.Equal(HttpStatusCode.Conflict, prog.StatusCode);
        Assert.Equal("confirm-in-progress", (await JsonAsync(prog)).GetProperty("error").GetString());
    }

    // ── happy path ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Happy_confirm_of_two_items()
    {
        await using var host = await NewHostAsync();
        var a = await StageAsync(host, PhotoA);
        var b = await StageAsync(host, PhotoB);
        Assert.Equal(a.BatchId, b.BatchId);
        var va = await ValidatedAsync(host, a);
        var vb = await ValidatedAsync(host, b);

        var mark = LogMark(host);
        var resp = await ConfirmAsync(host, a.BatchId);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var json = await JsonAsync(resp);
        Assert.Equal(a.BatchId, json.GetProperty("batchId").GetString());
        Assert.Equal(2, json.GetProperty("count").GetInt32());
        Assert.Equal($"/admin/upload?confirmed={a.BatchId}", json.GetProperty("redirect").GetString());

        // Two library images, _id = itemId, title from the file name, no seed fields.
        Assert.Equal(2, await Images(host).CountDocumentsAsync(FilterDefinition<BsonDocument>.Empty));
        await AssertImageAsync(host, a, va, "IMG_0001");
        await AssertImageAsync(host, b, vb, "Harbour at dusk");
        Assert.Equal("TESTCAM", (await Images(host).Find(new BsonDocument("_id", a.ItemId)).SingleAsync())["camera"]["make"].AsString);

        // Public sizes with the immutable Cache-Control; masters private; staging empty (SC-003).
        foreach (var (v, _) in new[] { (va, a), (vb, b) })
        {
            foreach (var s in v["sizes"].AsBsonArray)
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
        }
        foreach (var s in new[] { a, b })
        {
            var m = await MetaAsync(Priv, $"masters/{s.ItemId}.jpg");
            Assert.NotNull(m);
            Assert.Equal("image/jpeg", m!.Headers.ContentType);
            Assert.Null(await MetaAsync(Pub, $"masters/{s.ItemId}.jpg"));
            Assert.Equal(Bytes(s.Photo.Master).LongLength, m.ContentLength);
        }
        Assert.Empty(await host.ListAsync(Priv, $"staging/{a.BatchId}/"));

        // Batch confirmed with the count.
        var batch = await BatchAsync(host, a.BatchId);
        Assert.Equal("confirmed", batch["state"].AsString);
        Assert.Equal(2, batch["confirmedCount"].AsInt32);

        // Timing and log lines (FR-038, FR-040).
        var lines = LinesSince(host, mark);
        var confirm = Assert.Single(lines, l => l.StartsWith("op.timing upload.confirm ", StringComparison.Ordinal));
        Assert.EndsWith(" ok", confirm);
        Assert.Equal(2, lines.Count(l => l.StartsWith("op.timing upload.confirm.item ", StringComparison.Ordinal) && l.EndsWith(" ok")));
        Assert.Equal(6, lines.Count(l => l.StartsWith("op.timing s3.copy ", StringComparison.Ordinal) && l.EndsWith(" ok")));
        Assert.Equal(0, lines.Count(l => l.StartsWith("op.timing s3.copy ", StringComparison.Ordinal) && !l.EndsWith(" ok")));
        Assert.Equal(2, lines.Count(l => l.StartsWith("op.timing db.image.upsert ", StringComparison.Ordinal) && l.EndsWith(" ok")));
        var confirmed = Assert.Single(lines, l => l.StartsWith("upload.confirmed ", StringComparison.Ordinal));
        Assert.Matches($"^upload\\.confirmed {a.BatchId} 2 [0-9]+(\\.[0-9]+)?$", confirmed);
        Assert.Single(lines, l => l.StartsWith("http.request POST /admin/upload/confirm 200 ", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.StartsWith("op.timing s3.delete ", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.StartsWith("op.timing s3.list ", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, l => l.Contains("purge", StringComparison.OrdinalIgnoreCase)
                                          || l.Contains("warm", StringComparison.OrdinalIgnoreCase));

        // A repeat after success is idempotent: 200 with the stored count, still 2 images.
        var again = await ConfirmAsync(host, a.BatchId);
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Equal(2, (await JsonAsync(again)).GetProperty("count").GetInt32());
        Assert.Equal(2, await Images(host).CountDocumentsAsync(FilterDefinition<BsonDocument>.Empty));

        // The upload page shows the result state.
        var page = await (await host.GetAsync(json.GetProperty("redirect").GetString()!)).Content.ReadAsStringAsync();
        Assert.Contains("2 photos added to your library.", page);
    }

    // ── crash and re-run (FR-032) ────────────────────────────────────────────

    [Fact]
    public async Task Confirm_twice_after_a_simulated_crash()
    {
        await using var host = await NewHostAsync();
        var a = await StageAsync(host, PhotoA);
        var b = await StageAsync(host, PhotoB);
        var expectedPublic = KeysOf(await ValidatedAsync(host, a)).Concat(KeysOf(await ValidatedAsync(host, b))).ToList();
        var pubBefore = (await host.ListAsync(Pub, "")).ToHashSet();

        // A ConfirmService whose afterItem throws after the first item's upsert.
        var calls = 0;
        Func<string, Task> crash = _ =>
            Interlocked.Increment(ref calls) == 1 ? throw new InvalidOperationException("simulated crash") : Task.CompletedTask;
        using (var scope = host.Services.CreateScope())
        {
            var service = ActivatorUtilities.CreateInstance<ConfirmService>(scope.ServiceProvider, crash);
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.ConfirmAsync(a.BatchId));
        }

        var crashed = await BatchAsync(host, a.BatchId);
        Assert.Equal("open", crashed["state"].AsString);
        Assert.True(crashed.Contains("confirmStartedAt"));
        Assert.Equal(2, crashed["items"].AsBsonArray.Count);
        // Items run concurrently and Task.WhenAll lets the other item finish, so both images exist;
        // the crashed item kept its staged files (its prefix delete never ran), the other did not.
        Assert.Equal(2, await Images(host).CountDocumentsAsync(FilterDefinition<BsonDocument>.Empty));
        var stagedLeft = 0;
        foreach (var s in new[] { a, b })
            if ((await host.ListAsync(Priv, $"staging/{s.BatchId}/{s.ItemId}/")).Count > 0) stagedLeft++;
        Assert.Equal(1, stagedLeft);
        var crashedFirst = (await Images(host).Find(FilterDefinition<BsonDocument>.Empty).ToListAsync())
            .ToDictionary(d => d["_id"].AsString, d => d.ToJson());

        // While confirming, other mutations are refused; U10 itself resumes.
        var reg = await host.PostJsonAsync("/admin/upload/items",
            new { files = new[] { new { name = "c.jpg", size = 1000L, type = "image/jpeg" } } });
        Assert.Equal(HttpStatusCode.Conflict, reg.StatusCode);

        var resp = await ConfirmAsync(host, a.BatchId);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal(2, (await JsonAsync(resp)).GetProperty("count").GetInt32());

        var images = await Images(host).Find(FilterDefinition<BsonDocument>.Empty).ToListAsync();
        Assert.Equal(2, images.Count);
        Assert.Equal(new[] { a.ItemId, b.ItemId }.OrderBy(x => x), images.Select(i => i["_id"].AsString).OrderBy(x => x));
        // An image created before the crash is unchanged by the re-run ($setOnInsert).
        foreach (var (id, jsonBefore) in crashedFirst)
            Assert.Equal(jsonBefore, images.Single(i => i["_id"] == id).ToJson());

        // No duplicate public keys: exactly the 4 content-addressed names were added.
        var pubAfter = await host.ListAsync(Pub, "");
        Assert.Equal(pubAfter.Count, pubAfter.Distinct().Count());
        Assert.Equal(expectedPublic.Where(k => !pubBefore.Contains(k)).OrderBy(k => k),
            pubAfter.Where(k => !pubBefore.Contains(k)).OrderBy(k => k));
        foreach (var k in expectedPublic) Assert.Contains(k, pubAfter);
        Assert.Empty(await host.ListAsync(Priv, $"staging/{a.BatchId}/"));

        var batch = await BatchAsync(host, a.BatchId);
        Assert.Equal("confirmed", batch["state"].AsString);
        Assert.Equal(2, batch["confirmedCount"].AsInt32);

        // A further U10 on the confirmed batch: 200, count 2, still 2 images.
        var again = await ConfirmAsync(host, a.BatchId);
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Equal(2, (await JsonAsync(again)).GetProperty("count").GetInt32());
        Assert.Equal(2, await Images(host).CountDocumentsAsync(FilterDefinition<BsonDocument>.Empty));
    }

    // ── copy refused by if-match (plan item 6, decisions row 45) ─────────────

    [Fact]
    public async Task Reput_after_validation_publishes_nothing()
    {
        await using var host = await NewHostAsync();
        var a = await StageAsync(host, PhotoA);
        var b = await StageAsync(host, PhotoB);
        var keysB = KeysOf(await ValidatedAsync(host, b));
        await DeletePublicAsync(keysB);

        // Re-PUT item 2's 200.avif through the still-valid presigned URL after it succeeded.
        Assert.Equal(HttpStatusCode.OK,
            (await UploadHost.PutToPresignedAsync(b.AvifUrl, Bytes("gps-320.avif"), "image/avif")).StatusCode);

        var mark = LogMark(host);
        var resp = await ConfirmAsync(host, a.BatchId);
        Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
        var json = await JsonAsync(resp);
        Assert.Equal("changed-after-validation", json.GetProperty("error").GetString());
        Assert.Equal("1 photo changed after it was checked. Restart or delete it.", json.GetProperty("message").GetString());
        Assert.Equal(1, json.GetProperty("added").GetInt32());
        Assert.Equal(1, json.GetProperty("failed").GetInt32());

        // Item 2: nothing public, no image, failed, item prefix empty, no master copy.
        foreach (var k in keysB) Assert.Null(await MetaAsync(Pub, k));
        Assert.Null(await Images(host).Find(new BsonDocument("_id", b.ItemId)).FirstOrDefaultAsync());
        Assert.Null(await MetaAsync(Priv, $"masters/{b.ItemId}.jpg"));
        Assert.Empty(await host.ListAsync(Priv, $"staging/{b.BatchId}/{b.ItemId}/"));

        // Item 1 has its image and left the batch.
        Assert.NotNull(await Images(host).Find(new BsonDocument("_id", a.ItemId)).FirstOrDefaultAsync());
        Assert.Equal(1, await Images(host).CountDocumentsAsync(FilterDefinition<BsonDocument>.Empty));

        // The batch is open with only item 2, failed, and no confirmStartedAt (no lock-out).
        var batch = await BatchAsync(host, a.BatchId);
        Assert.Equal("open", batch["state"].AsString);
        Assert.False(batch.Contains("confirmStartedAt"));
        var only = Assert.Single(batch["items"].AsBsonArray).AsBsonDocument;
        Assert.Equal(b.ItemId, only["itemId"].AsString);
        Assert.Equal("failed", only["state"].AsString);
        Assert.Equal("changed after validation", only["reason"].AsString);

        // The refused copy is logged as rejected; no upload.confirmed for an unfinished batch.
        var lines = LinesSince(host, mark);
        Assert.DoesNotContain(lines, l => l.StartsWith("upload.confirmed ", StringComparison.Ordinal));

        // No lock-out: a repeat confirm is now not-ready (the failed item), and the batch takes
        // new files again. (U9 delete is mapped by Batch F; its test owns that assertion.)
        var repeat = await ConfirmAsync(host, a.BatchId);
        Assert.Equal(HttpStatusCode.Conflict, repeat.StatusCode);
        var rj = await JsonAsync(repeat);
        Assert.Equal("not-ready", rj.GetProperty("error").GetString());
        Assert.Equal(1, rj.GetProperty("notReady").GetInt32());
        var reg = await host.PostJsonAsync("/admin/upload/items",
            new { files = new[] { new { name = "c.jpg", size = 1000L, type = "image/jpeg" } } });
        Assert.Equal(HttpStatusCode.OK, reg.StatusCode);
        Assert.Equal(a.BatchId, (await JsonAsync(reg)).GetProperty("batchId").GetString());
    }

    // ── no public page changes (SC-004) ──────────────────────────────────────

    [Fact]
    public async Task Public_pages_byte_identical_around_confirm()
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
        // The owner's sign-in already created the site document; set only the home fields.
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
        await StageAsync(host, PhotoB);
        Assert.Equal(HttpStatusCode.OK, (await ConfirmAsync(host, a.BatchId)).StatusCode);
        Assert.Equal(3, await Images(host).CountDocumentsAsync(FilterDefinition<BsonDocument>.Empty));

        var after = await BodiesAsync();
        Assert.Contains("Autumn Walk", before[1]); // 011: the listing is paths[1], "/g"
        for (var i = 0; i < paths.Length; i++)
            Assert.Equal(before[i], after[i]);
    }

    // ── seed coexistence (FR-035) ────────────────────────────────────────────

    [Fact]
    public async Task Removal_command_after_confirm_leaves_uploaded_images()
    {
        // A recording edge provider: the removal's purge never reaches a real edge.
        await using var host = await UploadHost.CreateAsync(_mongo, _google, _s3, configure: b =>
        {
            b.Services.RemoveAll<Gallery.Web.Edge.IEdgeProvider>();
            b.Services.AddSingleton<Gallery.Web.Edge.IEdgeProvider>(new RecordingEdgeProvider());
        });
        var a = await StageAsync(host, PhotoA);
        var b = await StageAsync(host, PhotoB);
        Assert.Equal(HttpStatusCode.OK, (await ConfirmAsync(host, a.BatchId)).StatusCode);

        var images = Images(host);
        var before = (await images.Find(FilterDefinition<BsonDocument>.Empty).ToListAsync())
            .ToDictionary(d => d["_id"].AsString, d => d.ToJson());
        Assert.Equal(new[] { a.ItemId, b.ItemId }.OrderBy(x => x), before.Keys.OrderBy(x => x));

        // Control: a seeded image (seedKey "seed-control") IS removed by the same run, proving the
        // run reached the delete step that would otherwise have removed the uploaded images.
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

    /// <summary>
    /// FR-035 the other way round: the dev database holds seeded content (and its <c>seedKey</c>
    /// index exists) before the owner confirms a batch of two.
    /// </summary>
    [Fact]
    public async Task Confirm_after_seeded_content_adds_both_images()
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
        var resp = await ConfirmAsync(host, a.BatchId);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal(2, (await JsonAsync(resp)).GetProperty("count").GetInt32());
        foreach (var id in new[] { a.ItemId, b.ItemId })
            Assert.NotNull(await Images(host).Find(new BsonDocument("_id", id)).FirstOrDefaultAsync());
    }

    /// <summary>
    /// 008 decisions row 60: a dev database seeded BEFORE 008 still has the plain unique
    /// seedKey_1 index. Confirm upgrades it to the partial form itself, so a 2-photo batch confirms
    /// without waiting for the next seed run.
    /// </summary>
    [Fact]
    public async Task Confirm_upgrades_a_legacy_seedKey_index_and_adds_both_images()
    {
        await using var host = await NewHostAsync();
        await Images(host).Indexes.CreateOneAsync(new CreateIndexModel<BsonDocument>(
            Builders<BsonDocument>.IndexKeys.Ascending("seedKey"),
            new CreateIndexOptions { Unique = true, Name = "seedKey_1" }));

        var a = await StageAsync(host, PhotoA);
        var b = await StageAsync(host, PhotoB);
        var resp = await ConfirmAsync(host, a.BatchId);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal(2, (await JsonAsync(resp)).GetProperty("count").GetInt32());
        foreach (var id in new[] { a.ItemId, b.ItemId })
            Assert.NotNull(await Images(host).Find(new BsonDocument("_id", id)).FirstOrDefaultAsync());
    }
}
