using Gallery.Web.Data;
using Gallery.Web.Site;
using Gallery.Web.Tests.Fixtures;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;

namespace Gallery.Web.Tests;

/// <summary>
/// 009 T002 (plan risk R-07, decisions rows 23 and 33): with <c>Site:OutputCache=true</c>, a second
/// GET of a public page is served from the origin output cache, so the datastore is read once.
/// The read count comes from the <c>op.timing db.site.read</c> / <c>db.gallery.read</c> log lines
/// that <see cref="ContentReader"/> writes, which a cached response never produces.
/// </summary>
[Collection("mongo")]
public class OutputCacheEvictionTests(MongoFixture mongo)
{
    private const string GalleryId = "hhhhhhhhhhhhhhhhhhhhhhhh22";
    private const string Slug = "harbour-light";
    private const string ImgA = "jjjjjjjjjjjjjjjjjjjjjjjj22";
    private const string ImgB = "kkkkkkkkkkkkkkkkkkkkkkkk22";

    private static readonly Dictionary<string, string?> CacheOn = new()
    {
        ["Site:OutputCache"] = "true",
    };

    private static int Count(AdminHost host, string op)
    {
        lock (host.LogLines)
            return host.LogLines.Count(l => l.StartsWith($"op.timing {op} ", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Listing_second_GET_is_served_from_the_output_cache()
    {
        await using var host = await AdminHost.CreateAsync(mongo, extraOverrides: CacheOn);
        await InsertPublicGalleryAsync(host);

        var first = await host.GetAsync("/g");
        Assert.Equal(System.Net.HttpStatusCode.OK, first.StatusCode);
        var second = await host.GetAsync("/g");
        Assert.Equal(System.Net.HttpStatusCode.OK, second.StatusCode);

        Assert.Equal(1, Count(host, "db.site.read"));
    }

    [Fact]
    public async Task Gallery_and_photo_second_GET_is_served_from_the_output_cache()
    {
        await using var host = await AdminHost.CreateAsync(mongo, extraOverrides: CacheOn);
        await InsertPublicGalleryAsync(host);

        var galleryPath = $"/g/{GalleryId}/{Slug}";
        Assert.Equal(System.Net.HttpStatusCode.OK, (await host.GetAsync(galleryPath)).StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.OK, (await host.GetAsync(galleryPath)).StatusCode);
        Assert.Equal(1, Count(host, "db.gallery.read"));

        var photoPath = $"{galleryPath}/{ImgA}";
        Assert.Equal(System.Net.HttpStatusCode.OK, (await host.GetAsync(photoPath)).StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.OK, (await host.GetAsync(photoPath)).StatusCode);
        // One read for the gallery page plus one for the photo page; the repeats add none.
        Assert.Equal(2, Count(host, "db.gallery.read"));
    }

    [Fact]
    public async Task Evicting_a_page_tag_drops_only_that_page()
    {
        await using var host = await AdminHost.CreateAsync(mongo, extraOverrides: CacheOn);
        await InsertPublicGalleryAsync(host);

        var galleryPath = $"/g/{GalleryId}/{Slug}";
        Assert.Equal(System.Net.HttpStatusCode.OK, (await host.GetAsync("/g")).StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.OK, (await host.GetAsync(galleryPath)).StatusCode);
        Assert.Equal(1, Count(host, "db.site.read"));
        Assert.Equal(1, Count(host, "db.gallery.read"));

        var store = host.Services.GetRequiredService<IOutputCacheStore>();
        await store.EvictByTagAsync(PublicCache.PageTag(galleryPath), CancellationToken.None);

        Assert.Equal(System.Net.HttpStatusCode.OK, (await host.GetAsync(galleryPath)).StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.OK, (await host.GetAsync("/g")).StatusCode);
        Assert.Equal(2, Count(host, "db.gallery.read"));
        Assert.Equal(1, Count(host, "db.site.read"));
    }

    // ── inline data helper (document shape as in PublicRoutesTests) ────────────

    private static DerivativeDoc Deriv(int w) => new()
    {
        W = w,
        Avif = $"hash{w}.avif",
        Webp = $"hash{w}.webp",
        BytesAvif = 100,
        BytesWebp = 80,
    };

    private static PlaceholderDoc Placeholder() => new()
    {
        Color = "#aabbcc",
        Preview = "data:image/webp;base64,AAAA",
    };

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
        Title = "Harbour Light",
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
            SeedKey = "oc1",
            Slug = Slug,
            Title = "Harbour Light",
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
