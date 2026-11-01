using System.Security.Cryptography;
using System.Text;
using Gallery.Core.Ids;
using Gallery.Core.Images;
using Gallery.Core.Text;
using Gallery.Web.Data;
using Gallery.Web.Library;

namespace Gallery.Web.Tests.Fixtures;

/// <summary>
/// Document builders with the field sets the app's two writers produce (009 T011), so tests can
/// insert realistic data without the retired seeder:
/// <list type="bullet">
/// <item>Seed-shaped (<see cref="SeededImage"/>, <see cref="SeededGallery"/>): what
///   <c>Seed/Seeder.cs</c> wrote: <c>seedKey</c> (non-empty), <c>sourceHash</c>, title, optional
///   caption, width/height, placeholder (color + preview), derivatives
///   (<c>w, avif, webp, bytesAvif, bytesWebp</c> with <c>{sha256-16hex}.{ext}</c> names), galleryIds,
///   createdAt; galleries with seedKey, slug (<see cref="Slug.From"/>), title, visibility, items,
///   optional pinnedCover, card and updatedAt.</item>
/// <item>Upload-shaped (<see cref="UploadedImage"/>): what 008 confirm writes: no <c>seedKey</c>,
///   no <c>sourceHash</c>, no caption, <c>galleryIds: []</c>.</item>
/// </list>
/// Cards come from <see cref="CardBuilder.Build"/> (called, never copied).
/// </summary>
public static class SeedShapes
{
    /// <summary>The derivative ladder of a 1280-wide master (IV.6).</summary>
    public static IReadOnlyList<int> Widths(int width = 1280) => Ladder.For(width);

    /// <summary>A content-hash style public key, <c>{sha256-16hex}.{ext}</c>, unique per seed.</summary>
    public static string Key(string seed, int w, string ext)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{seed}/{w}/{ext}"));
        return $"{Convert.ToHexStringLower(hash)[..16]}.{ext}";
    }

    public static List<DerivativeDoc> Derivatives(string seed, int width = 1280) =>
        Widths(width).Select(w => new DerivativeDoc
        {
            W = w,
            Avif = Key(seed, w, "avif"),
            Webp = Key(seed, w, "webp"),
            BytesAvif = 1000 + w,
            BytesWebp = 1200 + w,
        }).ToList();

    public static PlaceholderDoc Placeholder() => new()
    {
        Color = "#8899aa",
        Preview = "data:image/webp;base64,UklGRhIAAABXRUJQVlA4TAYAAAAvAAAAAAfQ//73v/+BiOh/AAA=",
    };

    /// <summary>A seed-shaped image: non-empty <c>seedKey</c> and <c>sourceHash</c>.</summary>
    public static ImageDoc SeededImage(
        string? id = null, string? seedKey = null, DateTime? createdAt = null,
        string? title = null, string? caption = null, IEnumerable<string>? galleryIds = null)
    {
        id ??= OpaqueId.NewId();
        seedKey ??= $"seed-{id[..8]}.jpg";
        return new ImageDoc
        {
            Id = id,
            SeedKey = seedKey,
            SourceHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(seedKey))),
            Title = title ?? $"Seeded {id[..6]}",
            Caption = caption,
            Width = 1280,
            Height = 960,
            Placeholder = Placeholder(),
            Derivatives = Derivatives(id),
            GalleryIds = galleryIds?.ToList() ?? [],
            CreatedAt = createdAt ?? DateTime.UtcNow,
        };
    }

    /// <summary>An upload-shaped image (008 confirm): no <c>seedKey</c>, no <c>sourceHash</c>.</summary>
    public static ImageDoc UploadedImage(
        string? id = null, DateTime? createdAt = null, string? title = null, IEnumerable<string>? galleryIds = null)
    {
        id ??= OpaqueId.NewId();
        return new ImageDoc
        {
            Id = id,
            Title = title ?? $"Upload {id[..6]}",
            Width = 1280,
            Height = 960,
            Placeholder = Placeholder(),
            Derivatives = Derivatives(id),
            GalleryIds = galleryIds?.ToList() ?? [],
            CreatedAt = createdAt ?? DateTime.UtcNow,
        };
    }

    /// <summary>
    /// A gallery over <paramref name="members"/> (in order) with its card built by
    /// <see cref="CardBuilder.Build"/>. <paramref name="seedKey"/> null gives an owner-shaped gallery
    /// (no <c>seedKey</c> element); the default is a seed-shaped one.
    /// </summary>
    public static GalleryDoc Gallery(
        IReadOnlyList<ImageDoc> members, string? id = null, string title = "Seeded Gallery",
        string visibility = "public", string? pinnedCover = null, string? seedKey = null, string? description = null)
    {
        var g = new GalleryDoc
        {
            Id = id ?? OpaqueId.NewId(),
            SeedKey = seedKey,
            Slug = Slug.From(title),
            Title = title,
            Description = description,
            Visibility = visibility,
            Items = members.Select(m => m.Id).ToList(),
            PinnedCover = pinnedCover,
            UpdatedAt = DateTime.UtcNow,
        };
        var coverId = CardBuilder.CoverId(g);
        g.Card = CardBuilder.Build(g, members.FirstOrDefault(m => m.Id == coverId));
        return g;
    }

    /// <summary>A seed-shaped gallery: as <see cref="Gallery"/> with a non-empty <c>seedKey</c>.</summary>
    public static GalleryDoc SeededGallery(
        IReadOnlyList<ImageDoc> members, string? id = null, string title = "Seeded Gallery",
        string visibility = "public", string? pinnedCover = null, string? seedKey = null, string? description = null) =>
        Gallery(members, id, title, visibility, pinnedCover, seedKey ?? $"g-{Slug.From(title)}", description);

    /// <summary>Sets every member's <c>galleryIds</c> from the galleries' <c>items</c>.</summary>
    public static void Link(IEnumerable<ImageDoc> images, IEnumerable<GalleryDoc> galleries)
    {
        var gs = galleries.ToList();
        foreach (var i in images)
            i.GalleryIds = gs.Where(g => g.Items.Contains(i.Id)).Select(g => g.Id).OrderBy(x => x, StringComparer.Ordinal).ToList();
    }

    /// <summary>The site document the seeder wrote: <c>homeOrder</c> = every gallery, <c>homeCards</c>
    /// = <see cref="SiteProjector.Cards"/> of them.</summary>
    public static SiteDoc Site(IReadOnlyList<GalleryDoc> galleries) => new()
    {
        Id = "site",
        HomeOrder = galleries.Select(g => g.Id).ToList(),
        HomeCards = SiteProjector.Cards(galleries.Select(g => g.Id), galleries),
        UpdatedAt = DateTime.UtcNow,
    };
}
