using System.Text.RegularExpressions;

namespace Gallery.Web.Tests.Fixtures;

/// <summary>
/// 011 T001 (SC-004): a fixed library for the listing's before/after capture. Three listed
/// galleries (Alpha, Bravo, Charlie, in that home order, two photos each), one private gallery with a
/// photo and one empty gallery, all built through the services the way the owner does. Image ids,
/// titles and dates are fixed, so derivative keys are stable; gallery ids are random, so
/// <see cref="ListingRegion"/> replaces each with a placeholder before comparing.
/// </summary>
public static class HomeCaptureLibrary
{
    /// <summary>Fixed image ids (valid opaque ids: 26 chars of a-z2-7).</summary>
    public static readonly string[] ImageIds =
    [
        "aaaaaaaaaaaaaaaaaaaaaaaaa2", "aaaaaaaaaaaaaaaaaaaaaaaaa3",
        "bbbbbbbbbbbbbbbbbbbbbbbbb2", "bbbbbbbbbbbbbbbbbbbbbbbbb3",
        "ccccccccccccccccccccccccc2", "ccccccccccccccccccccccccc3",
        "ddddddddddddddddddddddddd2",
    ];

    /// <summary>The gallery ids in creation order: Alpha, Bravo, Charlie, Private, Empty.</summary>
    public sealed record Built(IReadOnlyList<string> GalleryIds)
    {
        public IReadOnlyList<string> Listed => GalleryIds.Take(3).ToList();
        public string Private => GalleryIds[3];
        public string Empty => GalleryIds[4];
    }

    public static async Task<Built> BuildAsync(LibraryHost h)
    {
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        for (var i = 0; i < ImageIds.Length; i++)
        {
            var id = ImageIds[i];
            await h.InsertUploadedImageAsync(start.AddMinutes(i), title: $"Photo {i + 1}", files: false,
                shape: img =>
                {
                    img.Id = id;
                    img.Derivatives = SeedShapes.Derivatives(id);
                });
        }

        var alpha = await GalleryHtml.GalleryAsync(h, "Alpha", [ImageIds[0], ImageIds[1]], publish: true);
        var bravo = await GalleryHtml.GalleryAsync(h, "Bravo", [ImageIds[2], ImageIds[3]], publish: true);
        var charlie = await GalleryHtml.GalleryAsync(h, "Charlie", [ImageIds[4], ImageIds[5]], publish: true);
        var priv = await GalleryHtml.GalleryAsync(h, "Private", [ImageIds[6]]);
        var empty = await GalleryHtml.GalleryAsync(h, "Empty", []);
        await h.SettleAsync();
        return new Built([alpha, bravo, charlie, priv, empty]);
    }

    /// <summary>
    /// The ContactSheet region of <paramref name="rawHtml"/> (<c>&lt;div class="cs </c> through its
    /// balanced close), with Razor's scoped-CSS attributes stripped and each gallery id replaced by
    /// <c>G{n}</c> (its index in <paramref name="built"/>).
    /// </summary>
    public static string ListingRegion(string rawHtml, Built built)
    {
        var start = rawHtml.IndexOf("<div class=\"cs ", StringComparison.Ordinal);
        if (start < 0) throw new InvalidOperationException("no ContactSheet region in the page");
        var region = LibraryHtml.Element(rawHtml, "div", start);
        region = Regex.Replace(region, @" b-[a-z0-9]{10}", "");
        for (var i = 0; i < built.GalleryIds.Count; i++)
            region = region.Replace(built.GalleryIds[i], $"G{i}", StringComparison.Ordinal);
        return region;
    }
}
