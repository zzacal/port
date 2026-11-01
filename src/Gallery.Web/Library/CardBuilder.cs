using Gallery.Web.Data;

namespace Gallery.Web.Library;

/// <summary>
/// THE gallery card function (data-model "GalleryCard"). Every write path that changes a gallery's
/// items, pin, title or a member's title calls <see cref="Build"/>; there is no second copy.
/// The shape is the existing <see cref="GalleryCardDoc"/> the public pages read.
/// </summary>
public static class CardBuilder
{
    /// <summary>
    /// The id of the gallery's cover: <c>pinnedCover</c> when it is set and is a member, else
    /// <c>items[0]</c>, else null (an empty gallery has no cover).
    /// </summary>
    public static string? CoverId(GalleryDoc g)
    {
        if (!string.IsNullOrEmpty(g.PinnedCover) && g.Items.Contains(g.PinnedCover))
            return g.PinnedCover;
        return g.Items.Count > 0 ? g.Items[0] : null;
    }

    /// <summary>
    /// Builds the card of <paramref name="g"/>. <paramref name="cover"/> is the image document
    /// whose id is <see cref="CoverId"/> (callers read it once), or null when the gallery has no
    /// cover or the cover image no longer exists; either way the card then has no cover.
    /// </summary>
    /// <exception cref="ArgumentException">The cover passed is not the gallery's cover image.</exception>
    public static GalleryCardDoc Build(GalleryDoc g, ImageDoc? cover)
    {
        var coverId = CoverId(g);
        if (cover is not null && cover.Id != coverId)
            throw new ArgumentException(
                $"Image {cover.Id} is not the cover of gallery {g.Id} (expected {coverId ?? "none"}).", nameof(cover));

        return new GalleryCardDoc
        {
            GalleryId = g.Id,
            Slug = g.Slug,
            Title = g.Title,
            ImageCount = g.Items.Count,
            Cover = cover is null ? null : new CoverImageDoc
            {
                ImageId = cover.Id,
                Width = cover.Width,
                Height = cover.Height,
                Placeholder = cover.Placeholder,
                Derivatives = [.. cover.Derivatives],
            },
        };
    }
}
