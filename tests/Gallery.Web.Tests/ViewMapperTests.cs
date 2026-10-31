using Gallery.Core.Images;
using Gallery.Web.Data;
using Gallery.Web.Site;

namespace Gallery.Web.Tests;

public class ViewMapperTests
{
    // ── helpers ─────────────────────────────────────────────────────────────

    private static DerivativeDoc MakeDeriv(int w, string hash) =>
        new() { W = w, Avif = $"{hash}.avif", Webp = $"{hash}.webp", BytesAvif = 100, BytesWebp = 80 };

    private static PlaceholderDoc Placeholder() =>
        new() { Color = "#aabbcc", Preview = "data:image/webp;base64,AAAA" };

    private static ImageDoc MakeImage(string id = "abc123") => new()
    {
        Id = id,
        Title = "Test Image",
        Width = 1920,
        Height = 1080,
        Placeholder = Placeholder(),
        Derivatives = [MakeDeriv(640, "sha1"), MakeDeriv(1280, "sha2")],
        Camera = new CameraDoc
        {
            Make = "Fujifilm",
            Model = "X100VI",
            Lens = "XF23mm",
            FocalMm = 23m,
            Focal35Mm = 35,
            FNumber = 2.0m,
            Exposure = 0.0025m,
            Iso = 400,
        },
    };

    private static string Url(string key) => $"https://img.test/{key}";

    // ── ToView ───────────────────────────────────────────────────────────────

    [Fact]
    public void ToView_maps_derivatives_to_variants_with_url_function()
    {
        var doc = MakeImage();
        var view = ViewMapper.ToView(doc, Url);

        Assert.Equal("abc123", view.Id);
        Assert.Equal("Test Image", view.Title);
        Assert.Equal(1920, view.Width);
        Assert.Equal(1080, view.Height);
        Assert.Equal(2, view.Variants.Count);
        Assert.Equal("https://img.test/sha1.avif", view.Variants[0].AvifUrl);
        Assert.Equal("https://img.test/sha2.webp", view.Variants[1].WebpUrl);
    }

    [Fact]
    public void ToView_maps_placeholder_and_color()
    {
        var view = ViewMapper.ToView(MakeImage(), Url);
        Assert.Equal("data:image/webp;base64,AAAA", view.PlaceholderDataUri);
        Assert.Equal("#aabbcc", view.DominantColor);
    }

    [Fact]
    public void ToView_maps_camera_to_exif()
    {
        var view = ViewMapper.ToView(MakeImage(), Url);
        Assert.NotNull(view.Exif);
        Assert.Equal("Fujifilm", view.Exif.Make);
        Assert.Equal("X100VI", view.Exif.Model);
        Assert.Equal("XF23mm", view.Exif.Lens);
        Assert.Equal(23m, view.Exif.FocalLengthMm);
        Assert.Equal(35, view.Exif.FocalLength35Mm);
        Assert.Equal(2.0m, view.Exif.FNumber);
        Assert.Equal(0.0025m, view.Exif.ExposureSeconds);
        Assert.Equal(400, view.Exif.Iso);
    }

    [Fact]
    public void ToView_returns_null_exif_when_camera_is_null()
    {
        var doc = MakeImage();
        doc.Camera = null;
        var view = ViewMapper.ToView(doc, Url);
        Assert.Null(view.Exif);
    }

    [Fact]
    public void ToView_returns_null_exif_when_all_camera_fields_are_null()
    {
        var doc = MakeImage();
        doc.Camera = new CameraDoc(); // all properties null
        var view = ViewMapper.ToView(doc, Url);
        Assert.Null(view.Exif);
    }

    // ── ToCard ───────────────────────────────────────────────────────────────

    [Fact]
    public void ToCard_returns_null_when_cover_is_null()
    {
        var card = new GalleryCardDoc
        {
            GalleryId = "gid",
            Slug = "sept-5",
            Title = "September 5",
            ImageCount = 0,
            Cover = null,
        };
        Assert.Null(ViewMapper.ToCard(card, Url));
    }

    [Fact]
    public void ToCard_builds_href_and_image_when_cover_present()
    {
        var card = new GalleryCardDoc
        {
            GalleryId = "galleryid001",
            Slug = "september-5",
            Title = "September 5",
            ImageCount = 3,
            Cover = new CoverImageDoc
            {
                ImageId = "imgid001",
                Width = 800,
                Height = 600,
                Placeholder = Placeholder(),
                Derivatives = [MakeDeriv(800, "coverhash")],
            },
        };

        var item = ViewMapper.ToCard(card, Url);
        Assert.NotNull(item);
        Assert.Equal("/g/galleryid001/september-5", item.Href);
        Assert.NotNull(item.Image);
        Assert.Equal("https://img.test/coverhash.webp", item.Image.Variants[0].WebpUrl);
    }

    // ── PreloadLink ──────────────────────────────────────────────────────────

    [Fact]
    public void PreloadLink_contains_fallback_webp_srcset_and_sizes()
    {
        var view = ViewMapper.ToView(MakeImage(), Url);
        var link = ViewMapper.PreloadLink(view, "100vw");

        Assert.Contains("rel=preload", link);
        Assert.Contains("as=image", link);
        Assert.Contains("fetchpriority=high", link);
        Assert.Contains("imagesrcset=", link);
        Assert.Contains("imagesizes=\"100vw\"", link);
        // FallbackWebp picks the last variant at or below 1280
        Assert.StartsWith("<https://img.test/sha2.webp>", link);
    }
}
