using System.Net;
using System.Text.RegularExpressions;
using Gallery.Web.Data;
using Gallery.Web.Library;
using Gallery.Web.Storage;
using Gallery.Web.Tests.Fixtures;
using Gallery.Web.Upload;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MongoDB.Driver;

namespace Gallery.Web.Tests;

/// <summary>
/// 009 T043 (US5; FR-013, FR-014, FR-050; UI-4, UI-6): image delete over HTTP. L5 deletes one photo
/// everywhere; L4 deletes a selection by calling the single-item delete once per id (exactly one
/// <c>library.image.delete</c> timing line per image) and reports storage leftovers in the PRG
/// status; the selection cap is the page size; a repeat is idempotent.
/// </summary>
[Collection("s3")]
public class LibraryDeleteTests(S3ProxyFixture s3, MongoFixture mongo) : IClassFixture<MongoFixture>
{
    private static KeyValuePair<string, string> F(string k, string v) => GalleryHtml.F(k, v);

    /// <summary>An <see cref="IObjectDeleter"/> over the real store that throws for one chosen key (the T026 override).</summary>
    private sealed class FailOneKey(ImageStore store, FailOneKey.Target target) : IObjectDeleter
    {
        public sealed class Target { public string? Key { get; set; } }

        public Task DeleteAsync(string bucket, string key) =>
            key == target.Key
                ? throw new InvalidOperationException("simulated storage failure")
                : store.DeleteAsync(bucket, key);
    }

    private static int DeleteTimings(LibraryHost h) =>
        h.LogLines.Count(l => l.StartsWith("op.timing library.image.delete ", StringComparison.Ordinal));

    /// <summary>The JSON answer's root element (020 TI-11).</summary>
    private static async Task<System.Text.Json.JsonElement> JsonAsync(HttpResponseMessage resp)
    {
        using var doc = await UploadHost.ParseAsync(resp);
        Assert.NotNull(doc);
        return doc!.RootElement.Clone();
    }

    [Fact]
    public async Task L5_deletes_the_photo_everywhere_and_logs_one_owner_action()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3);
        var img = await h.InsertUploadedImageAsync(title: "Pier");
        var keep = await h.InsertUploadedImageAsync(title: "Rocks");
        var a = await GalleryHtml.GalleryAsync(h, "Coast", [img.Id, keep.Id], publish: true);
        var b = await GalleryHtml.GalleryAsync(h, "Studio", [img.Id]);
        await h.SettleAsync();

        var resp = await h.PostJsonAsync("/admin/api/photos/delete", new { ids = new[] { img.Id } });
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var json = await JsonAsync(resp);
        Assert.Equal((1, 0, 0, "1 photo deleted."),
            (json.GetProperty("deleted").GetInt32(), json.GetProperty("leftovers").GetInt32(), json.GetProperty("failed").GetInt32(),
             json.GetProperty("message").GetString()));
        await h.SettleAsync();

        Assert.Equal(0, await h.Images.CountDocumentsAsync(i => i.Id == img.Id));
        Assert.Equal([keep.Id], (await GalleryHtml.LoadAsync(h, a)).Items);
        Assert.Empty((await GalleryHtml.LoadAsync(h, b)).Items);
        var pub = (await h.ListAsync(LibraryHost.Pub)).ToHashSet();
        Assert.All(img.Derivatives.SelectMany(d => new[] { d.Avif, d.Webp }), k => Assert.DoesNotContain(k, pub));
        Assert.DoesNotContain(ConfirmService.MasterKey(img.Id), await h.ListAsync(LibraryHost.Priv, "masters/"));
        Assert.Equal(HttpStatusCode.NotFound, (await h.GetAsync($"/g/{a}/coast/{img.Id}")).StatusCode);
        Assert.Single(h.LogLines, l => l.StartsWith($"owner.action image.delete {img.Id} ", StringComparison.Ordinal));
        Assert.Equal(1, DeleteTimings(h));

        // Repeat on the already-deleted id → 200 deleted 0, nothing more logged or purged.
        var again = await h.PostJsonAsync("/admin/api/photos/delete", new { ids = new[] { img.Id } });
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Equal(0, (await JsonAsync(again)).GetProperty("deleted").GetInt32());
        Assert.Empty(await h.SettleAsync());
        Assert.Single(h.LogLines, l => l.StartsWith($"owner.action image.delete {img.Id} ", StringComparison.Ordinal));
    }

    [Fact]
    public async Task L4_of_three_with_one_failing_key_reports_the_leftover_and_calls_the_single_delete_three_times()
    {
        var target = new FailOneKey.Target();
        await using var h = await LibraryHost.CreateAsync(mongo, s3, configure: b =>
        {
            b.Services.RemoveAll<IObjectDeleter>();
            b.Services.AddScoped<IObjectDeleter>(sp => new FailOneKey(sp.GetRequiredService<ImageStore>(), target));
        });
        var imgs = new List<ImageDoc>();
        for (var i = 0; i < 3; i++) imgs.Add(await h.InsertUploadedImageAsync());
        var ids = imgs.Select(i => i.Id).ToList();
        await GalleryHtml.GalleryAsync(h, "Three", ids);
        await h.SettleAsync();
        target.Key = imgs[1].Derivatives[0].Avif;
        var before = DeleteTimings(h);

        var resp = await h.PostJsonAsync("/admin/api/photos/delete", new { ids });
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var json = await JsonAsync(resp);
        Assert.Equal((3, 1, 0), (json.GetProperty("deleted").GetInt32(), json.GetProperty("leftovers").GetInt32(), json.GetProperty("failed").GetInt32()));
        await h.SettleAsync();

        Assert.Equal(3, DeleteTimings(h) - before);
        Assert.Equal(0, await h.Images.CountDocumentsAsync(i => ids.Contains(i.Id)));
        Assert.Equal(3, h.LogLines.Count(l => l.StartsWith("owner.action image.delete ", StringComparison.Ordinal)));
        Assert.Single(h.LogLines, l => l.StartsWith("library.delete_leftover ", StringComparison.Ordinal));

        Assert.Equal("3 photos deleted. 1 files could not be removed and were logged.", json.GetProperty("message").GetString());
    }

    [Fact]
    public async Task L4_above_PageSize_is_422_and_deletes_nothing()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3, extraOverrides: new() { ["Library:PageSize"] = "2" });
        var ids = new List<string>();
        for (var i = 0; i < 3; i++) ids.Add((await h.InsertUploadedImageAsync(files: false)).Id);

        var resp = await h.PostJsonAsync("/admin/api/photos/delete", new { ids });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
        Assert.Equal("Select at most 2 photos.", (await JsonAsync(resp)).GetProperty("message").GetString());
        Assert.Equal(3, await h.Images.CountDocumentsAsync(i => ids.Contains(i.Id) && i.DeletingAt == null));
        Assert.Equal(0, DeleteTimings(h));
        Assert.DoesNotContain(h.LogLines, l => l.StartsWith("owner.action image.delete", StringComparison.Ordinal));
        Assert.Empty(await h.SettleAsync());
    }
}
