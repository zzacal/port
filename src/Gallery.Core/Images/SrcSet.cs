namespace Gallery.Core.Images;

public static class SrcSet
{
    public static string Avif(ImageView image) =>
        string.Join(", ", image.Variants
            .OrderBy(v => v.Width)
            .Select(v => $"{v.AvifUrl} {v.Width}w"));

    public static string Webp(ImageView image) =>
        string.Join(", ", image.Variants
            .OrderBy(v => v.Width)
            .Select(v => $"{v.WebpUrl} {v.Width}w"));

    public static string FallbackWebp(ImageView image)
    {
        var sorted = image.Variants.OrderBy(v => v.Width).ToList();
        var candidate = sorted.LastOrDefault(v => v.Width <= 1280);
        return (candidate ?? sorted[0]).WebpUrl;
    }
}
