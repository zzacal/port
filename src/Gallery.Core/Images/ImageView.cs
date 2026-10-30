namespace Gallery.Core.Images;

public record Variant(int Width, string AvifUrl, string WebpUrl);

public record ImageView(
    string Id,
    string Title,
    int Width,
    int Height,
    IReadOnlyList<Variant> Variants,
    string PlaceholderDataUri,
    string DominantColor,
    Exif? Exif
)
{
    public double AspectRatio => (double)Width / Height;

    public Variant ThumbVariant =>
        Variants.FirstOrDefault(v => v.Width >= 640)
        ?? Variants[^1];
}
