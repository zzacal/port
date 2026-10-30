using Gallery.Core.Images;

namespace Gallery.Core.Tests;

public class SrcSetTests
{
    private static ImageView MakeImage(params (int w, string avif, string webp)[] variants)
    {
        var vlist = variants.Select(v => new Variant(v.w, v.avif, v.webp)).ToList();
        return new ImageView("id", "Title", 2560, 1440, vlist, "data:image/webp;base64,abc", "#fff", null);
    }

    [Fact]
    public void Avif_ReturnsAscendingWidths()
    {
        var img = MakeImage((320, "/a320.avif", "/a320.webp"), (640, "/a640.avif", "/a640.webp"), (1280, "/a1280.avif", "/a1280.webp"));
        Assert.Equal("/a320.avif 320w, /a640.avif 640w, /a1280.avif 1280w", SrcSet.Avif(img));
    }

    [Fact]
    public void Webp_ReturnsAscendingWidths()
    {
        var img = MakeImage((320, "/a320.avif", "/a320.webp"), (640, "/a640.avif", "/a640.webp"), (1280, "/a1280.avif", "/a1280.webp"));
        Assert.Equal("/a320.webp 320w, /a640.webp 640w, /a1280.webp 1280w", SrcSet.Webp(img));
    }

    [Fact]
    public void Avif_SingleVariant_ReturnsCorrectFormat()
    {
        var img = MakeImage((960, "/img.avif", "/img.webp"));
        Assert.Equal("/img.avif 960w", SrcSet.Avif(img));
    }

    [Fact]
    public void FallbackWebp_ReturnsLargestVariantAtOrBelow1280()
    {
        var img = MakeImage((320, "/a320.avif", "/a320.webp"), (640, "/a640.avif", "/a640.webp"), (1280, "/a1280.avif", "/a1280.webp"), (1920, "/a1920.avif", "/a1920.webp"));
        Assert.Equal("/a1280.webp", SrcSet.FallbackWebp(img));
    }

    [Fact]
    public void FallbackWebp_WhenNoVariantAt1280_ReturnsSmallesVariant()
    {
        // All variants > 1280 -> returns smallest
        var img = MakeImage((1920, "/a1920.avif", "/a1920.webp"), (2560, "/a2560.avif", "/a2560.webp"));
        Assert.Equal("/a1920.webp", SrcSet.FallbackWebp(img));
    }

    [Fact]
    public void FallbackWebp_ExactlyAt1280_ReturnsThat()
    {
        var img = MakeImage((960, "/a960.avif", "/a960.webp"), (1280, "/a1280.avif", "/a1280.webp"), (1920, "/a1920.avif", "/a1920.webp"));
        Assert.Equal("/a1280.webp", SrcSet.FallbackWebp(img));
    }

    [Fact]
    public void FallbackWebp_OnlySingleVariantBelow1280_ReturnsThat()
    {
        var img = MakeImage((640, "/a640.avif", "/a640.webp"));
        Assert.Equal("/a640.webp", SrcSet.FallbackWebp(img));
    }
}
