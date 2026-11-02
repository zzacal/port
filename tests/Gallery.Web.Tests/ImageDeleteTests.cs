using Gallery.Web.Data;
using Gallery.Web.Edge;
using Gallery.Web.Library;
using Gallery.Web.Storage;
using Gallery.Web.Tests.Fixtures;
using Gallery.Web.Upload;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Gallery.Web.Tests;

/// <summary>
/// 009 T026 [SAFETY] (SC-008, FR-014–FR-017; research R-02; decisions rows 3 and 16): image delete
/// keeps a public file another live image references (the shared-file rule), counts a missing
/// object as deleted, never lets one failed storage delete stop the others, and is finished by a
/// repeat after a crash. Galleries are built through <see cref="GalleryService"/>; after every delete
/// <see cref="InvariantChecker"/> runs and the recorder's set is compared for EQUALITY.
/// </summary>
[Collection("s3")]
public class ImageDeleteTests(S3ProxyFixture s3, MongoFixture mongo) : IClassFixture<MongoFixture>
{
    private static string Gp(string gid, string slug) => LibraryHost.Page(PurgeSets.GalleryPath(gid, slug));
    private static string Pp(string gid, string slug, string iid) => LibraryHost.Page(PurgeSets.PhotoPath(gid, slug, iid));
    private static readonly string Home = LibraryHost.Page("/g");

    private static void SetEq(IEnumerable<string> expected, IEnumerable<string> actual) =>
        Assert.Equal(expected.Distinct().Order(StringComparer.Ordinal).ToArray(), actual.Order(StringComparer.Ordinal).ToArray());

    private static List<string> PublicKeys(ImageDoc i) =>
        i.Derivatives.SelectMany(d => new[] { d.Avif, d.Webp }).ToList();

    private static Task<OperationResult> Delete(LibraryHost h, string iid) =>
        h.RunAsync<ImageDeleteService, OperationResult>(s => s.DeleteAsync(iid));

    private static Task<OperationResult> Gs(LibraryHost h, Func<GalleryService, Task<OperationResult>> call) =>
        h.RunAsync(call);

    [Fact]
    public async Task Shared_public_file_is_kept()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3);
        var x = await h.InsertUploadedImageAsync();
        var shared = x.Derivatives[0].Avif;
        // Y is the same photo uploaded twice for its smallest AVIF: content-named, so one shared key.
        var y = await h.InsertUploadedImageAsync(shape: i => i.Derivatives[0].Avif = shared);
        var xOnly = PublicKeys(x).Where(k => k != shared).ToList();

        var r = await Delete(h, x.Id);
        var purged = await h.SettleAsync();

        Assert.Equal((OperationStatus.Ok, 1, 0), (r.Status, r.Deleted, r.Leftovers));
        var pub = (await h.ListAsync(LibraryHost.Pub)).ToHashSet();
        Assert.Contains(shared, pub);
        Assert.All(xOnly, k => Assert.DoesNotContain(k, pub));
        Assert.All(PublicKeys(y), k => Assert.Contains(k, pub));
        var priv = await h.ListAsync(LibraryHost.Priv, "masters/");
        Assert.DoesNotContain(ConfirmService.MasterKey(x.Id), priv);
        Assert.Contains(ConfirmService.MasterKey(y.Id), priv);
        Assert.Equal(0, await h.Images.CountDocumentsAsync(i => i.Id == x.Id));
        // In no gallery: P-DELETE is exactly X's unshared public files.
        SetEq(xOnly.Select(LibraryHost.File), purged);
        Assert.Single(h.LogLines, l => l == $"owner.action image.delete {x.Id} 1 leftovers=0 galleries=0");
    }

    [Fact]
    public async Task Missing_master_counts_as_deleted()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3);
        var seeded = await h.InsertSeededAsync(); // seed-shaped: public files, no master
        Assert.DoesNotContain(ConfirmService.MasterKey(seeded.Id), await h.ListAsync(LibraryHost.Priv, "masters/"));

        var r = await Delete(h, seeded.Id);
        var purged = await h.SettleAsync();

        Assert.Equal((OperationStatus.Ok, 1, 0), (r.Status, r.Deleted, r.Leftovers));
        Assert.Equal(0, await h.Images.CountDocumentsAsync(i => i.Id == seeded.Id));
        var pub = (await h.ListAsync(LibraryHost.Pub)).ToHashSet();
        Assert.All(PublicKeys(seeded), k => Assert.DoesNotContain(k, pub));
        SetEq(PublicKeys(seeded).Select(LibraryHost.File), purged);
        Assert.DoesNotContain(h.LogLines, l => l.StartsWith("library.delete_leftover", StringComparison.Ordinal));
    }

    /// <summary>An <see cref="IObjectDeleter"/> over the real store that throws for one chosen key.</summary>
    private sealed class FailOneKey(ImageStore store, FailOneKey.Target target) : IObjectDeleter
    {
        public sealed class Target { public string? Key { get; set; } }

        public Task DeleteAsync(string bucket, string key) =>
            key == target.Key
                ? throw new InvalidOperationException("simulated storage failure")
                : store.DeleteAsync(bucket, key);
    }

    [Fact]
    public async Task One_failed_storage_delete_of_three()
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
        var gid = (await Gs(h, s => s.CreateAsync("Three"))).GalleryId!;
        await Gs(h, s => s.AddAsync(gid, ids));
        await h.SettleAsync();
        var failing = imgs[1].Derivatives[1].Webp;
        target.Key = failing;

        var r = await h.RunAsync<ImageDeleteService, OperationResult>(s => s.DeleteManyAsync(ids));
        var purged = await h.SettleAsync();

        Assert.Equal((OperationStatus.Ok, 3, 1, 0), (r.Status, r.Deleted, r.Leftovers, r.Failed));
        Assert.Equal(0, await h.Images.CountDocumentsAsync(FilterDefinition<ImageDoc>.Empty));
        Assert.Empty((await h.Galleries.Find(g => g.Id == gid).FirstAsync()).Items);
        // The emulator buckets are shared by the "s3" collection: look only at these images' keys.
        var pub = (await h.ListAsync(LibraryHost.Pub)).ToHashSet();
        Assert.Equal(new[] { failing }, imgs.SelectMany(PublicKeys).Where(pub.Contains).ToArray());
        var masters = await h.ListAsync(LibraryHost.Priv, "masters/");
        Assert.DoesNotContain(masters, k => ids.Any(id => k == ConfirmService.MasterKey(id)));
        var leftover = Assert.Single(h.LogLines, l => l.StartsWith("library.delete_leftover", StringComparison.Ordinal));
        Assert.Equal($"library.delete_leftover {imgs[1].Id} pub {failing} InvalidOperationException", leftover);
        // ONE work item for the whole bulk request: one edge.lag line for its op.
        Assert.Single(h.LogLines, l => l.StartsWith("edge.lag image.delete.bulk ", StringComparison.Ordinal));
        Assert.DoesNotContain(h.LogLines, l => l.StartsWith("edge.lag image.delete ", StringComparison.Ordinal));
        var deletedFiles = imgs.SelectMany(PublicKeys).Where(k => k != failing).Select(LibraryHost.File);
        SetEq(ids.Select(i => Pp(gid, "three", i)).Append(Gp(gid, "three")).Concat(deletedFiles), purged);
        Assert.Equal(3, h.LogLines.Count(l => l.StartsWith("owner.action image.delete ", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Crash_after_galleries_then_repeat()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3);
        var img = await h.InsertUploadedImageAsync();
        var other = await h.InsertUploadedImageAsync();
        var gid = (await Gs(h, s => s.CreateAsync("Crash"))).GalleryId!;
        await Gs(h, s => s.AddAsync(gid, [img.Id, other.Id]));
        await h.SettleAsync();

        var crashes = 0;
        Func<string, Task> crashOnce = _ =>
            Interlocked.Increment(ref crashes) == 1 ? throw new InvalidOperationException("simulated crash") : Task.CompletedTask;
        using (var scope = h.Scope())
        {
            var service = ActivatorUtilities.CreateInstance<ImageDeleteService>(scope.ServiceProvider, crashOnce);
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.DeleteAsync(img.Id));
        }
        await h.SettleAsync();

        // The record survives the crash as a findable tombstone (R-02: it is removed last).
        Assert.Equal(1, await h.Images.CountDocumentsAsync(i => i.Id == img.Id));
        var tomb = await h.Images.Find(i => i.Id == img.Id).FirstAsync();
        Assert.NotNull(tomb.DeletingAt);
        Assert.Empty(tomb.GalleryIds);
        var g = await h.Galleries.Find(x => x.Id == gid).FirstAsync();
        Assert.Equal(new[] { other.Id }, g.Items.ToArray());
        Assert.Equal(other.Id, g.Card!.Cover!.ImageId);
        // The library query (research R-04) hides it.
        var libraryFilter = Builders<ImageDoc>.Filter.Exists(i => i.DeletingAt, false);
        Assert.Equal(new[] { other.Id }, (await h.Images.Find(libraryFilter).ToListAsync()).Select(i => i.Id).ToArray());
        Assert.Contains(ConfirmService.MasterKey(img.Id), await h.ListAsync(LibraryHost.Priv, "masters/"));

        var r = await Delete(h, img.Id);
        var purged = await h.SettleAsync();

        Assert.Equal((OperationStatus.Ok, 1, 0), (r.Status, r.Deleted, r.Leftovers));
        Assert.Equal(0, await h.Images.CountDocumentsAsync(i => i.Id == img.Id));
        Assert.DoesNotContain(ConfirmService.MasterKey(img.Id), await h.ListAsync(LibraryHost.Priv, "masters/"));
        var pub = (await h.ListAsync(LibraryHost.Pub)).ToHashSet();
        Assert.All(PublicKeys(img), k => Assert.DoesNotContain(k, pub));
        // The resumed run left no gallery (step 2 had finished): its set is the files it deleted.
        SetEq(PublicKeys(img).Select(LibraryHost.File), purged);
    }

    [Fact]
    public async Task Pinned_cover_of_a_public_gallery_and_member_of_a_private_one()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3);
        var a = await h.InsertUploadedImageAsync();
        var x = await h.InsertUploadedImageAsync();
        var b = await h.InsertUploadedImageAsync();
        var pubG = (await Gs(h, s => s.CreateAsync("Public One"))).GalleryId!;
        var privG = (await Gs(h, s => s.CreateAsync("Private One"))).GalleryId!;
        await Gs(h, s => s.AddAsync(pubG, [a.Id, x.Id]));
        Assert.True((await Gs(h, s => s.SetCoverAsync(pubG, x.Id))).IsOk);
        Assert.True((await Gs(h, s => s.PublishAsync(pubG, confirmed: true))).IsOk);
        await Gs(h, s => s.AddAsync(privG, [x.Id, b.Id]));
        await h.SettleAsync();
        Assert.Equal(x.Id, (await h.SiteAsync()).HomeCards.Single().Cover!.ImageId);

        var r = await Delete(h, x.Id);
        var purged = await h.SettleAsync();

        Assert.Equal((OperationStatus.Ok, 1, 0), (r.Status, r.Deleted, r.Leftovers));
        var pg = await h.Galleries.Find(g => g.Id == pubG).FirstAsync();
        Assert.Null(pg.PinnedCover);
        Assert.Equal(new[] { a.Id }, pg.Items.ToArray());
        Assert.Equal(new[] { b.Id }, (await h.Galleries.Find(g => g.Id == privG).FirstAsync()).Items.ToArray());
        var home = (await h.SiteAsync()).HomeCards.Single();
        Assert.Equal((pubG, a.Id, 1), (home.GalleryId, home.Cover!.ImageId, home.ImageCount));
        SetEq(
        [
            Gp(pubG, "public-one"), Pp(pubG, "public-one", x.Id), Pp(pubG, "public-one", a.Id), Home,
            Gp(privG, "private-one"), Pp(privG, "private-one", x.Id), Pp(privG, "private-one", b.Id),
            .. PublicKeys(x).Select(LibraryHost.File),
        ], purged);
        Assert.Single(h.LogLines, l => l == $"owner.action image.delete {x.Id} 1 leftovers=0 galleries=2");
    }

    [Fact]
    public async Task Unknown_image_is_not_found_and_bulk_limits_are_enforced()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3);
        var img = await h.InsertUploadedImageAsync(files: false);

        var unknown = await Delete(h, "zzzzzzzzzzzzzzzzzzzzzzzzzz");
        var none = await h.RunAsync<ImageDeleteService, OperationResult>(s => s.DeleteManyAsync([]));
        var tooMany = await h.RunAsync<ImageDeleteService, OperationResult>(s =>
            s.DeleteManyAsync(Enumerable.Repeat(img.Id, 101).ToList()));

        Assert.Equal(OperationStatus.NotFound, unknown.Status);
        Assert.Equal((OperationStatus.Refused, 422), (none.Status, none.StatusCode));
        Assert.Equal((OperationStatus.Refused, 422, "Select at most 100 photos."), (tooMany.Status, tooMany.StatusCode, tooMany.Reason));
        Assert.Null((await h.Images.Find(i => i.Id == img.Id).FirstAsync()).DeletingAt);
        Assert.Empty(await h.SettleAsync());
    }
}
