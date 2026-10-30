using Gallery.Core.Images;

namespace Gallery.Core.Tests;

public class ExifFormatTests
{
    // Camera: null Exif
    [Fact]
    public void Camera_NullExif_ReturnsNull() =>
        Assert.Null(ExifFormat.Camera(null));

    // Camera: both null
    [Fact]
    public void Camera_BothNull_ReturnsNull() =>
        Assert.Null(ExifFormat.Camera(new Exif(null, null, null, null, null, null, null, null)));

    // Camera: Make only
    [Fact]
    public void Camera_MakeOnly_ReturnsMake() =>
        Assert.Equal("FUJIFILM", ExifFormat.Camera(new Exif("FUJIFILM", null, null, null, null, null, null, null)));

    // Camera: Model only
    [Fact]
    public void Camera_ModelOnly_ReturnsModel() =>
        Assert.Equal("X-T5", ExifFormat.Camera(new Exif(null, "X-T5", null, null, null, null, null, null)));

    // Camera: Make + Model that doesn't start with Make -> concatenated
    [Fact]
    public void Camera_FujifilmXt5_ConcatenatesMakeAndModel() =>
        Assert.Equal("FUJIFILM X-T5", ExifFormat.Camera(new Exif("FUJIFILM", "X-T5", null, null, null, null, null, null)));

    // Camera: de-duplication ("Canon" + "Canon EOS R5" -> "Canon EOS R5")
    [Fact]
    public void Camera_ModelStartsWithMake_DropsPrefix() =>
        Assert.Equal("Canon EOS R5", ExifFormat.Camera(new Exif("Canon", "Canon EOS R5", null, null, null, null, null, null)));

    // Camera: case-insensitive de-duplication
    [Fact]
    public void Camera_ModelStartsWithMakeCaseInsensitive_DropsPrefix() =>
        Assert.Equal("canon EOS R5", ExifFormat.Camera(new Exif("CANON", "canon EOS R5", null, null, null, null, null, null)));

    // Lens: null Exif
    [Fact]
    public void Lens_NullExif_ReturnsNull() =>
        Assert.Null(ExifFormat.Lens(null));

    // Lens: no lens
    [Fact]
    public void Lens_NoLens_ReturnsNull() =>
        Assert.Null(ExifFormat.Lens(new Exif(null, null, null, null, null, null, null, null)));

    // Lens: as given
    [Fact]
    public void Lens_ReturnsAsGiven() =>
        Assert.Equal("XF33mmF1.4 R LM WR", ExifFormat.Lens(new Exif(null, null, "XF33mmF1.4 R LM WR", null, null, null, null, null)));

    // FocalLength: null Exif
    [Fact]
    public void FocalLength_NullExif_ReturnsNull() =>
        Assert.Null(ExifFormat.FocalLength(null));

    // FocalLength: no FocalLengthMm
    [Fact]
    public void FocalLength_NoMm_ReturnsNull() =>
        Assert.Null(ExifFormat.FocalLength(new Exif(null, null, null, null, null, null, null, null)));

    // FocalLength: with mm only (no 35mm eq.)
    [Fact]
    public void FocalLength_MmOnly_NoSuffix() =>
        Assert.Equal("33mm", ExifFormat.FocalLength(new Exif(null, null, null, 33m, null, null, null, null)));

    // FocalLength: with mm and differing 35mm equivalent
    [Fact]
    public void FocalLength_MmAndEq_AppendsSuffix() =>
        Assert.Equal("33mm (50mm eq.)", ExifFormat.FocalLength(new Exif(null, null, null, 33m, 50, null, null, null)));

    // FocalLength: 35mm eq equals mm -> no suffix
    [Fact]
    public void FocalLength_EqEqualsMm_NoSuffix() =>
        Assert.Equal("50mm", ExifFormat.FocalLength(new Exif(null, null, null, 50m, 50, null, null, null)));

    // FocalLength: fractional mm formatted with "0.#"
    [Fact]
    public void FocalLength_FractionalMm_FormattedCorrectly() =>
        Assert.Equal("35.6mm", ExifFormat.FocalLength(new Exif(null, null, null, 35.6m, null, null, null, null)));

    // Aperture: null Exif
    [Fact]
    public void Aperture_NullExif_ReturnsNull() =>
        Assert.Null(ExifFormat.Aperture(null));

    // Aperture: no FNumber
    [Fact]
    public void Aperture_NoFNumber_ReturnsNull() =>
        Assert.Null(ExifFormat.Aperture(new Exif(null, null, null, null, null, null, null, null)));

    // Aperture: f/2.0
    [Fact]
    public void Aperture_F2_ReturnsF2_0() =>
        Assert.Equal("f/2.0", ExifFormat.Aperture(new Exif(null, null, null, null, null, 2.0m, null, null)));

    // Aperture: f/11.0
    [Fact]
    public void Aperture_F11_ReturnsF11_0() =>
        Assert.Equal("f/11.0", ExifFormat.Aperture(new Exif(null, null, null, null, null, 11.0m, null, null)));

    // Shutter: null Exif
    [Fact]
    public void Shutter_NullExif_ReturnsNull() =>
        Assert.Null(ExifFormat.Shutter(null));

    // Shutter: no ExposureSeconds
    [Fact]
    public void Shutter_NoExposure_ReturnsNull() =>
        Assert.Null(ExifFormat.Shutter(new Exif(null, null, null, null, null, null, null, null)));

    // Shutter: <1s -> "1/{n}s"
    [Fact]
    public void Shutter_0004_Returns1Over250s() =>
        Assert.Equal("1/250s", ExifFormat.Shutter(new Exif(null, null, null, null, null, null, 0.004m, null)));

    // Shutter: >=1s -> "{s:0.#}s"
    [Fact]
    public void Shutter_1_5_Returns1_5s() =>
        Assert.Equal("1.5s", ExifFormat.Shutter(new Exif(null, null, null, null, null, null, 1.5m, null)));

    // Shutter: whole seconds
    [Fact]
    public void Shutter_2_Returns2s() =>
        Assert.Equal("2s", ExifFormat.Shutter(new Exif(null, null, null, null, null, null, 2m, null)));

    // ISO: null Exif
    [Fact]
    public void Iso_NullExif_ReturnsNull() =>
        Assert.Null(ExifFormat.Iso(null));

    // ISO: no value
    [Fact]
    public void Iso_NoValue_ReturnsNull() =>
        Assert.Null(ExifFormat.Iso(new Exif(null, null, null, null, null, null, null, null)));

    // ISO: value
    [Fact]
    public void Iso_400_ReturnsISO400() =>
        Assert.Equal("ISO 400", ExifFormat.Iso(new Exif(null, null, null, null, null, null, null, 400)));
}
