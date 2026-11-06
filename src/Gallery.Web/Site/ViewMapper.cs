using Gallery.Core.Images;
using Gallery.Web.Components.Organisms;
using Gallery.Web.Data;

namespace Gallery.Web.Site;

/// <summary>
/// Maps Mongo documents to the component model (data-model.md §Mapping).
/// </summary>
public static class ViewMapper
{
    /// <summary>Maps an <see cref="ImageDoc"/> to <see cref="ImageView"/>.</summary>
    /// <param name="doc">The image document from Mongo.</param>
    /// <param name="url">Function that converts an object key to a fully-qualified URL.</param>
    public static ImageView ToView(ImageDoc doc, Func<string, string> url)
    {
        var variants = doc.Derivatives
            .Select(d => new Variant(d.W, url(d.Avif), url(d.Webp)))
            .ToList();

        Exif? exif = null;
        if (doc.Camera is { } cam)
        {
            exif = new Exif(
                Make: cam.Make,
                Model: cam.Model,
                Lens: cam.Lens,
                FocalLengthMm: cam.FocalMm,
                FocalLength35Mm: cam.Focal35Mm,
                FNumber: cam.FNumber,
                ExposureSeconds: cam.Exposure,
                Iso: cam.Iso
            );
            // Return null when every field is absent.
            if (exif is { Make: null, Model: null, Lens: null, FocalLengthMm: null,
                          FocalLength35Mm: null, FNumber: null, ExposureSeconds: null, Iso: null })
                exif = null;
        }

        return new ImageView(
            Id: doc.Id,
            Title: doc.Title,
            Width: doc.Width,
            Height: doc.Height,
            Variants: variants,
            PlaceholderDataUri: doc.Placeholder.Preview,
            DominantColor: doc.Placeholder.Color,
            Exif: exif
        );
    }

    /// <summary>
    /// Maps a <see cref="GalleryCardDoc"/> to a <see cref="ContactSheetItem"/>.
    /// Always returns a ContactSheetItem; Image is null for empty galleries (E-16),
    /// Title carries the card title so the empty tile can render a "Coming soon"-style cell.
    /// </summary>
    public static ContactSheetItem ToCard(GalleryCardDoc card, Func<string, string> url)
    {
        var href = $"/g/{card.GalleryId}/{card.Slug}";

        if (card.Cover is null)
            return new ContactSheetItem(null, href, card.Title);

        return new ContactSheetItem(CoverView(card.Cover, card.Title, url), href, card.Title);
    }

    /// <summary>The cover of a card as an <see cref="ImageView"/> titled <paramref name="title"/> (gallery and camera cards).</summary>
    private static ImageView CoverView(CoverImageDoc cover, string title, Func<string, string> url)
    {
        var variants = cover.Derivatives
            .Select(d => new Variant(d.W, url(d.Avif), url(d.Webp)))
            .ToList();

        return new ImageView(
            Id: cover.ImageId,
            Title: title,
            Width: cover.Width,
            Height: cover.Height,
            Variants: variants,
            PlaceholderDataUri: cover.Placeholder.Preview,
            DominantColor: cover.Placeholder.Color,
            Exif: null
        );
    }

    /// <summary>
    /// 013 (contracts/components.md, camera listing tiles): maps a <see cref="CameraCardDoc"/> to a
    /// <see cref="ContactSheetItem"/>, the same shape as <see cref="ToCard"/>: href <c>/c/{slug}</c>,
    /// title = the camera name. A camera always has a cover, so there is no empty tile.
    /// </summary>
    public static ContactSheetItem ToCameraCard(CameraCardDoc card, Func<string, string> url) =>
        new(CoverView(card.Cover, card.Name, url), $"/c/{card.Slug}", card.Name);

    /// <summary>
    /// Builds the <c>Link</c> response header value for the primary image preload
    /// (contracts/http.md §Headers on 200 public pages).
    /// </summary>
    /// The candidates and sizes are the ones the page's first <c>&lt;source&gt;</c> (AVIF) offers, so the
    /// browser preloads exactly the file the &lt;picture&gt; then uses. <c>type</c> makes a browser without
    /// AVIF skip the hint instead of downloading a file it cannot use; the empty target is the
    /// documented form for an imagesrcset-only preload.
    public static string PreloadLink(ImageView image, string sizes)
        => $"<>; rel=preload; as=image; type=\"image/avif\"; imagesrcset=\"{SrcSet.Avif(image)}\"; imagesizes=\"{sizes}\"; fetchpriority=high";
}
