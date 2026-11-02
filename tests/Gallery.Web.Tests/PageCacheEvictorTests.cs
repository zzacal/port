using Gallery.Web.Data;
using Gallery.Web.Library;
using Gallery.Web.Tests.Fixtures;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;

namespace Gallery.Web.Tests;

/// <summary>
/// 009 T019 (FR-044, research R-07): <see cref="PageCacheEvictor"/> drops exactly the cached pages
/// whose paths it is given. With <c>Site:OutputCache=true</c> a re-read shows up as one more
/// <c>op.timing db.gallery.read</c> / <c>db.site.read</c> line, the same measure as T002.
/// </summary>
[Collection("mongo")]
public class PageCacheEvictorTests(MongoFixture mongo)
{
    private const string GalleryId = "hhhhhhhhhhhhhhhhhhhhhhhh33";
    private const string Slug = "salt-marsh";
    private const string ImgA = "jjjjjjjjjjjjjjjjjjjjjjjj33";
    private const string ImgB = "kkkkkkkkkkkkkkkkkkkkkkkk33";
    private static string GalleryPath => $"/g/{GalleryId}/{Slug}";
    private static string PhotoPath => $"{GalleryPath}/{ImgA}";

    private static readonly Dictionary<string, string?> CacheOn = new() { ["Site:OutputCache"] = "true" };

    private static int Count(AdminHost host, string op)
    {
        lock (host.LogLines)
            return host.LogLines.Count(l => l.StartsWith($"op.timing {op} ", StringComparison.Ordinal));
    }

    private static async Task GetOk(AdminHost host, string path) =>
        Assert.Equal(System.Net.HttpStatusCode.OK, (await host.GetAsync(path)).StatusCode);

    private static async Task EvictAsync(AdminHost host, params string[] paths)
    {
        using var scope = host.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<PageCacheEvictor>().EvictAsync(paths);
    }

    [Fact]
    public async Task Evicting_a_gallery_path_re_reads_only_that_page()
    {
        await using var host = await AdminHost.CreateAsync(mongo, extraOverrides: CacheOn);
        await InsertPublicGalleryAsync(host);

        await GetOk(host, "/g");
        await GetOk(host, GalleryPath);
        await GetOk(host, PhotoPath);
        Assert.Equal(1, Count(host, "db.site.read"));
        Assert.Equal(2, Count(host, "db.gallery.read")); // gallery page + photo page

        // the same path twice is evicted once (distinct paths)
        await EvictAsync(host, GalleryPath, GalleryPath);
        Assert.Equal(1, host.LogLines.ToList().Count(l => l.StartsWith("op.timing cache.evict ", StringComparison.Ordinal) && l.EndsWith(" ok", StringComparison.Ordinal)));

        await GetOk(host, GalleryPath);
        await GetOk(host, PhotoPath);
        await GetOk(host, "/g");
        Assert.Equal(3, Count(host, "db.gallery.read")); // the gallery page only was re-read
        Assert.Equal(1, Count(host, "db.site.read"));

        await EvictAsync(host, "/g", PhotoPath);
        await GetOk(host, "/g");
        await GetOk(host, PhotoPath);
        await GetOk(host, GalleryPath);
        Assert.Equal(2, Count(host, "db.site.read"));
        Assert.Equal(4, Count(host, "db.gallery.read"));
    }

    [Fact]
    public async Task Evicting_an_unknown_path_is_a_no_op()
    {
        await using var host = await AdminHost.CreateAsync(mongo, extraOverrides: CacheOn);
        await InsertPublicGalleryAsync(host);

        await GetOk(host, "/g");
        await GetOk(host, GalleryPath);

        await EvictAsync(host, "/g/nope/nothing", "/g/nope/nothing/x");
        await EvictAsync(host);

        await GetOk(host, "/g");
        await GetOk(host, GalleryPath);
        Assert.Equal(1, Count(host, "db.site.read"));
        Assert.Equal(1, Count(host, "db.gallery.read"));
    }

    // ── inline data helper (document shape as in PublicRoutesTests / OutputCacheEvictionTests) ──

    private static DerivativeDoc Deriv(int w) => new()
    {
        W = w,
        Avif = $"hash{w}.avif",
        Webp = $"hash{w}.webp",
        BytesAvif = 100,
        BytesWebp = 80,
    };

    private static PlaceholderDoc Placeholder() => new() { Color = "#aabbcc", Preview = "data:image/webp;base64,AAAA" };

    private static ImageDoc Image(string id, string title) => new()
    {
        Id = id,
        SeedKey = id,
        SourceHash = id,
        Title = title,
        Width = 1280,
        Height = 960,
        Placeholder = Placeholder(),
        Derivatives = [Deriv(320), Deriv(640), Deriv(1280)],
        GalleryIds = [GalleryId],
        CreatedAt = DateTime.UtcNow,
    };

    private static GalleryCardDoc Card() => new()
    {
        GalleryId = GalleryId,
        Slug = Slug,
        Title = "Salt Marsh",
        ImageCount = 2,
        Cover = new CoverImageDoc
        {
            ImageId = ImgA,
            Width = 1280,
            Height = 960,
            Placeholder = Placeholder(),
            Derivatives = [Deriv(320), Deriv(640), Deriv(1280)],
        },
    };

    private static async Task InsertPublicGalleryAsync(AdminHost host)
    {
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IMongoDatabase>();
        await db.GetCollection<ImageDoc>("images").InsertManyAsync([Image(ImgA, "Image A"), Image(ImgB, "Image B")]);
        await db.GetCollection<GalleryDoc>("galleries").InsertOneAsync(new GalleryDoc
        {
            Id = GalleryId,
            SeedKey = "pce1",
            Slug = Slug,
            Title = "Salt Marsh",
            Visibility = "public",
            Items = [ImgA, ImgB],
            Card = Card(),
            UpdatedAt = DateTime.UtcNow,
        });
        await db.GetCollection<SiteDoc>("site").InsertOneAsync(new SiteDoc
        {
            Id = "site",
            HomeOrder = [GalleryId],
            HomeCards = [Card()],
            UpdatedAt = DateTime.UtcNow,
        });
    }
}
