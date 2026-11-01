using Gallery.Web.Upload;
using static Gallery.Web.Tests.Fixtures.UploadFixtures;

namespace Gallery.Web.Tests;

/// <summary>
/// T024 [SAFETY] (FR-041, SC-002): reader 2 alone and the combined validator on the GPS fixtures,
/// agreement with the stored name and the master's shape, and the placeholder rules.
/// </summary>
public class UploadValidatorTests
{
    private static readonly UploadOptions Options = new();

    // ---------------------------------------------------------------- FR-041: reader 2 alone

    [Fact]
    public void Reader2_alone_rejects_gps_avif()
    {
        using var s = new MemoryStream(Bytes("gps-sips.avif"));
        var r = DerivativeValidator.Validate(s, "avif", 400, 2.0);
        Assert.False(r.IsValid);
        Assert.StartsWith("Disallowed metadata directory: ", r.Reason);
    }

    [Fact]
    public void Reader2_alone_rejects_gps_webp()
    {
        using var s = new MemoryStream(Bytes("gps-cwebp.webp"));
        var r = DerivativeValidator.Validate(s, "webp", 400, 2.0);
        Assert.False(r.IsValid);
        Assert.StartsWith("Disallowed metadata directory: ", r.Reason);
    }

    // ---------------------------------------------------------------- FR-041: combined

    [Fact]
    public void Combined_rejects_gps_avif()
    {
        var v = UploadValidator.ValidateSize(Bytes("gps-sips.avif"), "400.avif", 400, 400, 200);
        Assert.False(v.Ok);
        Assert.False(v.Reader1Ok);
        Assert.False(v.Reader2Ok);
        Assert.Equal("400.avif: AVIF item Exif", v.Reason);
    }

    [Fact]
    public void Combined_rejects_gps_webp()
    {
        var v = UploadValidator.ValidateSize(Bytes("gps-cwebp.webp"), "400.webp", 400, 400, 200);
        Assert.False(v.Ok);
        Assert.False(v.Reader1Ok);
        Assert.False(v.Reader2Ok);
        Assert.Equal("400.webp: VP8X EXIF flag", v.Reason);
    }

    [Fact]
    public void Combined_rejects_gps_320_avif_with_exif_item_added()
    {
        var v = UploadValidator.ValidateSize(AvifWithItem("Exif", Bytes("gps-320.avif")), "320.avif", 320, 400, 200);
        Assert.False(v.Ok);
        Assert.False(v.Reader1Ok);
        Assert.Equal("320.avif: AVIF item Exif", v.Reason);
    }

    [Fact]
    public void Combined_rejects_gps_320_webp_with_gps_exif_added_by_both_readers()
    {
        var v = UploadValidator.ValidateSize(WebpWithGpsExif(), "320.webp", 320, 400, 200);
        Assert.False(v.Ok);
        Assert.False(v.Reader1Ok);
        Assert.False(v.Reader2Ok);
        Assert.Equal("320.webp: VP8X EXIF flag", v.Reason);
    }

    // ---------------------------------------------------------------- accepts

    [Fact]
    public void Accepts_gps_320_avif_at_320_against_400x200_master()
    {
        var bytes = Bytes("gps-320.avif");
        var v = UploadValidator.ValidateSize(bytes, "320.avif", 320, 400, 200);
        Assert.True(v.Ok, v.Reason);
        Assert.True(v.Reader1Ok);
        Assert.True(v.Reader2Ok);
        Assert.Equal("avif", v.Format);
        Assert.Equal(320, v.Width);
        Assert.Equal(160, v.Height);
        Assert.Null(v.Reason);
        var expected = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes))[..16];
        Assert.Equal(expected, v.Sha256Hex16);
    }

    [Fact]
    public void Accepts_gps_320_webp_at_320_against_400x200_master()
    {
        var v = UploadValidator.ValidateSize(Bytes("gps-320.webp"), "320.webp", 320, 400, 200);
        Assert.True(v.Ok, v.Reason);
        Assert.Equal("webp", v.Format);
        Assert.Equal(320, v.Width);
        Assert.Equal(160, v.Height);
    }

    [Fact]
    public void Accepts_orient6_200_avif_against_upright_200x400_master()
    {
        var v = UploadValidator.ValidateSize(Bytes("orient6-200.avif"), "200.avif", 200, 200, 400);
        Assert.True(v.Ok, v.Reason);
    }

    // ---------------------------------------------------------------- rejects

    [Fact]
    public void Rejects_wrong_width_320_file_stored_as_640()
    {
        var v = UploadValidator.ValidateSize(Bytes("gps-320.webp"), "640.webp", 640, 6000, 3000);
        Assert.False(v.Ok);
        Assert.True(v.Reader1Ok);
        Assert.False(v.Reader2Ok);
        Assert.Equal("640.webp: width does not match the file name", v.Reason);
    }

    [Fact]
    public void Rejects_wrong_aspect_square_master()
    {
        var v = UploadValidator.ValidateSize(Bytes("gps-320.avif"), "320.avif", 320, 400, 400);
        Assert.False(v.Ok);
        Assert.True(v.Reader1Ok);
        Assert.False(v.Reader2Ok);
        Assert.Equal("320.avif: height does not match the original's shape", v.Reason);
    }

    [Fact]
    public void Rejects_jpeg_stored_as_avif()
    {
        var v = UploadValidator.ValidateSize(Bytes("gps-camera.jpg"), "320.avif", 320, 400, 200);
        Assert.False(v.Ok);
        Assert.False(v.Reader1Ok);
        Assert.False(v.Reader2Ok);
        Assert.Equal("320.avif: unsupported format", v.Reason);
    }

    [Fact]
    public void Rejects_webp_stored_as_avif()
    {
        var v = UploadValidator.ValidateSize(Bytes("gps-320.webp"), "320.avif", 320, 400, 200);
        Assert.False(v.Ok);
        Assert.True(v.Reader1Ok);
        Assert.False(v.Reader2Ok);
        Assert.Equal("320.avif: format does not match the file name", v.Reason);
    }

    [Fact]
    public void Rejects_embedded_thumbnail_second_av01()
    {
        var v = UploadValidator.ValidateSize(AvifWithSecondAv01(Bytes("gps-320.avif")), "320.avif", 320, 400, 200);
        Assert.False(v.Ok);
        Assert.False(v.Reader1Ok);
        Assert.Equal("320.avif: AVIF second av01 item", v.Reason);
    }

    [Fact]
    public void Rejects_iref_even_if_reader2_passes()
    {
        var v = UploadValidator.ValidateSize(AvifWithIref(Bytes("gps-320.avif")), "320.avif", 320, 400, 200);
        Assert.False(v.Ok);
        Assert.False(v.Reader1Ok);
        Assert.Equal("320.avif: AVIF iref box", v.Reason);
    }

    // ---------------------------------------------------------------- placeholder

    [Fact]
    public void Placeholder_orient6_accepted()
    {
        var bytes = Bytes("orient6-placeholder.webp");
        var v = UploadValidator.ValidatePlaceholder("#a1b2c3", DataUri(bytes), Options);
        Assert.True(v.Ok, v.Reason);
        Assert.True(v.Reader1Ok);
        Assert.True(v.Reader2Ok);
        Assert.Equal(18, v.Width);
        Assert.Equal(32, v.Height);
        Assert.Equal(bytes, v.Bytes);
        Assert.Null(v.Reason);
    }

    [Fact]
    public void Placeholder_gps_accepted()
    {
        var v = UploadValidator.ValidatePlaceholder("#000000", DataUri(Bytes("gps-placeholder.webp")), Options);
        Assert.True(v.Ok, v.Reason);
        Assert.Equal(32, v.Width);
        Assert.Equal(18, v.Height);
    }

    [Fact]
    public void Placeholder_with_exif_rejected()
    {
        var v = UploadValidator.ValidatePlaceholder("#a1b2c3", DataUri(Bytes("placeholder-exif.webp")), Options);
        Assert.False(v.Ok);
        Assert.False(v.Reader1Ok);
        Assert.Null(v.Bytes);
        Assert.Equal("placeholder: VP8X EXIF flag", v.Reason);
    }

    [Fact]
    public void Placeholder_64px_rejected()
    {
        var wide = WebpWithVp8Dimensions(Bytes("gps-placeholder.webp"), 64, 36);
        Assert.Equal((64, 36), Vp8Dimensions(wide)); // precondition
        var v = UploadValidator.ValidatePlaceholder("#a1b2c3", DataUri(wide), Options);
        Assert.False(v.Ok);
        Assert.Equal(64, v.Width);
        Assert.Equal("placeholder: over the edge limit", v.Reason);
    }

    [Fact]
    public void Placeholder_non_webp_data_uri_rejected()
    {
        var uri = "data:image/png;base64," + Convert.ToBase64String(Bytes("gps-placeholder.webp"));
        var v = UploadValidator.ValidatePlaceholder("#a1b2c3", uri, Options);
        Assert.False(v.Ok);
        Assert.Equal("placeholder: not a WebP data URI", v.Reason);
    }

    [Fact]
    public void Placeholder_over_1536_bytes_rejected()
    {
        var big = WebpWithChunk("ZZZZ", new byte[1500], Bytes("orient6-placeholder.webp"));
        Assert.True(big.Length > 1536); // precondition
        var v = UploadValidator.ValidatePlaceholder("#a1b2c3", DataUri(big), Options);
        Assert.False(v.Ok);
        Assert.Equal("placeholder: over the size limit", v.Reason);
    }

    [Theory]
    [InlineData("red")]
    [InlineData("#A1B2C3")]
    [InlineData("#a1b2c")]
    [InlineData("#a1b2c3 ")]
    [InlineData("")]
    public void Placeholder_bad_colour_rejected(string color)
    {
        var v = UploadValidator.ValidatePlaceholder(color, DataUri(Bytes("orient6-placeholder.webp")), Options);
        Assert.False(v.Ok);
        Assert.Equal("placeholder: colour is not #rrggbb", v.Reason);
    }

    [Fact]
    public void Placeholder_bad_base64_rejected()
    {
        var v = UploadValidator.ValidatePlaceholder("#a1b2c3", "data:image/webp;base64,!!!!", Options);
        Assert.False(v.Ok);
        Assert.Equal("placeholder: not valid base64", v.Reason);
    }
}
