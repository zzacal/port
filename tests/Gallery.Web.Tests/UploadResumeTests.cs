using System.Net;
using System.Text.Json;
using Gallery.Core.Ids;
using Gallery.Web.Data;
using Gallery.Web.Tests.Fixtures;
using MongoDB.Driver;
using static Gallery.Web.Tests.Fixtures.UploadFixtures;

namespace Gallery.Web.Tests;

/// <summary>
/// 008 T039 [US5] (SC-005, FR-008, FR-010): an open batch survives the page going away. Items
/// that stopped hearing from the page for over 2 minutes read as <c>interrupted</c> on every read
/// (U2 and the page) and can be restarted; a second tab joins the same batch; an open batch never
/// changes a public page (US5 AS-4).
/// </summary>
[Collection("s3")]
public class UploadResumeTests : IClassFixture<MongoFixture>, IAsyncLifetime
{
    private readonly S3ProxyFixture _s3;
    private readonly MongoFixture _mongo;
    private readonly FakeGoogle _google = new();

    public UploadResumeTests(S3ProxyFixture s3, MongoFixture mongo)
    {
        _s3 = s3;
        _mongo = mongo;
    }

    public Task InitializeAsync() => _google.InitializeAsync();
    public Task DisposeAsync() => _google.DisposeAsync();

    private Task<UploadHost> NewHostAsync() => UploadHost.CreateAsync(_mongo, _google, _s3);

    /// <summary>Four items, in order queued, processing, uploading (master stored) and succeeded.</summary>
    private static async Task<UploadApi.Reg[]> FourStatesAsync(UploadHost host)
    {
        var queued = await UploadApi.RegisterOneAsync(host, "queued.jpg", 1000);
        var processing = await UploadApi.RegisterOneAsync(host, "processing.jpg", 1000);
        Assert.Equal(HttpStatusCode.NoContent, (await UploadApi.ItemPost(host, processing.ItemId, "progress",
            new { attemptId = processing.AttemptId, state = "processing", percent = 0 })).StatusCode);
        var (uploading, _) = await UploadApi.StageMasterAsync(host, Bytes("gps-camera.jpg"), "uploading.jpg");
        Assert.Equal(HttpStatusCode.NoContent, (await UploadApi.ItemPost(host, uploading.ItemId, "progress",
            new { attemptId = uploading.AttemptId, state = "uploading", percent = 50 })).StatusCode);
        var succeeded = await UploadApi.StageSucceededAsync(host, "succeeded.jpg");
        Assert.Equal(queued.BatchId, succeeded.BatchId);
        return [queued, processing, uploading, succeeded];
    }

    private static List<(string ItemId, string State)> StatesOf(JsonDocument state) =>
        state.RootElement.GetProperty("batch").GetProperty("items").EnumerateArray()
            .Select(i => (i.GetProperty("itemId").GetString()!, i.GetProperty("state").GetString()!)).ToList();

    [Fact]
    public async Task After_121_seconds_U2_and_the_page_show_the_three_active_items_interrupted()
    {
        await using var host = await NewHostAsync();
        var items = await FourStatesAsync(host);

        // Before the threshold: the stored states.
        var (_, live) = await host.GetJsonAsync("/admin/upload/state");
        Assert.Equal(
            [(items[0].ItemId, "queued"), (items[1].ItemId, "processing"), (items[2].ItemId, "uploading"), (items[3].ItemId, "succeeded")],
            StatesOf(live!));

        host.Clock.Advance(TimeSpan.FromSeconds(121));

        var (resp, state) = await host.GetJsonAsync("/admin/upload/state");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal(
            [(items[0].ItemId, "interrupted"), (items[1].ItemId, "interrupted"), (items[2].ItemId, "interrupted"), (items[3].ItemId, "succeeded")],
            StatesOf(state!));

        var page = await host.GetAsync("/admin/upload");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        var html = await page.Content.ReadAsStringAsync();
        for (var i = 0; i < 3; i++)
            Assert.Equal(1, Guards.Count(html, $"<div class=\"batch-item state-interrupted\" data-item=\"{items[i].ItemId}\""));
        Assert.Equal(1, Guards.Count(html, $"<div class=\"batch-item state-succeeded\" data-item=\"{items[3].ItemId}\""));
        // Rendered tiles (outside the per-state <template>s) carry a 26-char item id.
        Assert.Equal(3, System.Text.RegularExpressions.Regex.Count(html, "<div class=\"batch-item state-interrupted\" data-item=\"[0-9a-z]{26}\""));
        Assert.Equal(1, System.Text.RegularExpressions.Regex.Count(html, "<div class=\"batch-item state-succeeded\" data-item=\"[0-9a-z]{26}\""));
        Assert.Equal(4, System.Text.RegularExpressions.Regex.Count(html, "<div class=\"batch-item state-[a-z]+\" data-item=\"[0-9a-z]{26}\""));

        // Interrupted is computed, never stored.
        var batch = await UploadApi.BatchAsync(host, items[0].BatchId);
        Assert.Equal(["queued", "processing", "uploading", "succeeded"],
            batch["items"].AsBsonArray.Select(i => i["state"].AsString));
    }

    [Fact]
    public async Task U5_on_an_interrupted_item_is_409_not_active_and_restart_works()
    {
        await using var host = await NewHostAsync();
        var items = await FourStatesAsync(host);
        host.Clock.Advance(TimeSpan.FromSeconds(121));

        foreach (var r in items[..3])
            await UploadApi.AssertErrorAsync(await UploadApi.ItemPost(host, r.ItemId, "progress",
                new { attemptId = r.AttemptId, state = "uploading", percent = 90 }), HttpStatusCode.Conflict, "not-active");

        // Restart the interrupted item whose master is stored: sizes straight away.
        var uploading = items[2];
        var resp = await UploadApi.ItemPost(host, uploading.ItemId, "restart", new { attemptId = uploading.AttemptId });
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var json = await UploadApi.JsonAsync(resp);
        var next = json.GetProperty("attemptId").GetString()!;
        Assert.NotEqual(uploading.AttemptId, next);
        Assert.False(json.GetProperty("needsFile").GetBoolean());
        Assert.Equal(320, Assert.Single(json.GetProperty("sizes").EnumerateArray()).GetProperty("w").GetInt32());

        // Restart the interrupted queued item: no master, so the file is asked for again.
        var queued = items[0];
        var q = await UploadApi.JsonAsync(await UploadApi.ItemPost(host, queued.ItemId, "restart", new { attemptId = queued.AttemptId }));
        Assert.True(q.GetProperty("needsFile").GetBoolean());
        Assert.Equal("queued.jpg", q.GetProperty("fileName").GetString());

        // Both are live again (heartbeat now), and the new attempt takes heartbeats.
        var (_, state) = await host.GetJsonAsync("/admin/upload/state");
        Assert.Equal(
            [(items[0].ItemId, "queued"), (items[1].ItemId, "interrupted"), (items[2].ItemId, "queued"), (items[3].ItemId, "succeeded")],
            StatesOf(state!));
        Assert.Equal(HttpStatusCode.NoContent, (await UploadApi.ItemPost(host, uploading.ItemId, "progress",
            new { attemptId = next, state = "processing", percent = 0 })).StatusCode);
    }

    [Fact]
    public async Task Two_concurrent_registers_in_one_session_share_one_batch()
    {
        await using var host = await NewHostAsync();

        Task<HttpResponseMessage> Register(string name) => host.PostJsonAsync("/admin/upload/items",
            new { files = new[] { new { name, size = 1000, type = "image/jpeg" } } });

        var responses = await Task.WhenAll(Register("tab-1.jpg"), Register("tab-2.jpg"));
        var ids = new List<string>();
        foreach (var resp in responses)
        {
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
            var json = await UploadApi.JsonAsync(resp);
            Assert.Equal(1, json.GetProperty("accepted").GetArrayLength());
            ids.Add(json.GetProperty("batchId").GetString()!);
        }
        Assert.Equal(ids[0], ids[1]);

        var batches = UploadApi.Db(host).GetCollection<BatchDoc>(Gallery.Web.Upload.BatchStore.CollectionName);
        var open = await batches.Find(b => b.State == BatchStates.Open).ToListAsync();
        var only = Assert.Single(open);
        Assert.Equal(ids[0], only.Id);
        Assert.Equal(["tab-1.jpg", "tab-2.jpg"], only.Items.Select(i => i.FileName).Order());

        // A later register (the page reloaded in another tab) joins it too.
        var later = await UploadApi.RegisterOneAsync(host, "tab-3.jpg", 1000);
        Assert.Equal(ids[0], later.BatchId);
    }

    [Fact]
    public async Task Public_pages_are_byte_identical_while_a_batch_is_open()
    {
        await using var host = await NewHostAsync();
        var db = UploadApi.Db(host);
        var galId = OpaqueId.NewId();
        var imgId = OpaqueId.NewId();
        var deriv = new DerivativeDoc { W = 320, Avif = "hash320.avif", Webp = "hash320.webp", BytesAvif = 100, BytesWebp = 80 };
        var ph = new PlaceholderDoc { Color = "#aabbcc", Preview = "data:image/webp;base64,AAAA" };
        var cover = new CoverImageDoc { ImageId = imgId, Width = 1280, Height = 960, Placeholder = ph, Derivatives = [deriv] };
        var card = new GalleryCardDoc { GalleryId = galId, Slug = "autumn-walk", Title = "Autumn Walk", ImageCount = 1, Cover = cover };
        await db.GetCollection<ImageDoc>("images").InsertOneAsync(new ImageDoc
        {
            Id = imgId, SeedKey = imgId, SourceHash = imgId, Title = "Image One", Width = 1280, Height = 960,
            Placeholder = ph, Derivatives = [deriv], GalleryIds = [galId], CreatedAt = DateTime.UtcNow,
        });
        await db.GetCollection<GalleryDoc>("galleries").InsertOneAsync(new GalleryDoc
        {
            Id = galId, SeedKey = "pub1", Slug = "autumn-walk", Title = "Autumn Walk", Visibility = "public",
            Items = [imgId], Card = card, UpdatedAt = DateTime.UtcNow,
        });
        await db.GetCollection<SiteDoc>("site").UpdateOneAsync(
            Builders<SiteDoc>.Filter.Eq(d => d.Id, "site"),
            Builders<SiteDoc>.Update.Set(d => d.HomeOrder, [galId]).Set(d => d.HomeCards, [card]).Set(d => d.UpdatedAt, DateTime.UtcNow),
            new UpdateOptions { IsUpsert = true });

        string[] paths = ["/", $"/g/{galId}/autumn-walk", $"/g/{galId}/autumn-walk/{imgId}"];

        async Task<List<string>> BodiesAsync()
        {
            var bodies = new List<string>();
            foreach (var p in paths)
            {
                var r = await host.GetAsync(p);
                Assert.True(r.StatusCode == HttpStatusCode.OK, $"{p} answered {(int)r.StatusCode}");
                bodies.Add(await r.Content.ReadAsStringAsync());
            }
            return bodies;
        }

        var noBatch = await BodiesAsync();
        Assert.Contains("Autumn Walk", noBatch[0]);

        // An open batch with an active, a succeeded and an interrupted item.
        await UploadApi.RegisterOneAsync(host, "a.jpg", 1000);
        await UploadApi.StageSucceededAsync(host, "b.jpg");
        var during = await BodiesAsync();
        host.Clock.Advance(TimeSpan.FromSeconds(121));
        var interrupted = await BodiesAsync();

        for (var i = 0; i < paths.Length; i++)
        {
            Assert.Equal(noBatch[i], during[i]);
            Assert.Equal(noBatch[i], interrupted[i]);
        }
    }
}
