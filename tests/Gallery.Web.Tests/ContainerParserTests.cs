using Gallery.Web.Tests.Fixtures;
using Gallery.Web.Upload;
using static Gallery.Web.Tests.Fixtures.UploadFixtures;

namespace Gallery.Web.Tests;

/// <summary>
/// T020 [SAFETY]: reader 1 (<see cref="ContainerParser"/>) on real jSquash output, on the GPS
/// fixtures (FR-041), and on every hostile structure T018 builds. Reasons are asserted exactly.
/// </summary>
public class ContainerParserTests
{
    // ---------------------------------------------------------------- accepts real encoder output

    [Fact]
    public void Accepts_orient6_avif_200x400()
    {
        var r = ContainerParser.Parse(Bytes("orient6-200.avif"));
        Assert.Equal(new ParseResult(true, "avif", 200, 400, null), r);
    }

    [Fact]
    public void Accepts_orient6_webp_200x400()
    {
        var r = ContainerParser.Parse(Bytes("orient6-200.webp"));
        Assert.Equal(new ParseResult(true, "webp", 200, 400, null), r);
    }

    [Fact]
    public void Accepts_gps_320_avif_jsquash_output_320x160()
    {
        var r = ContainerParser.Parse(Bytes("gps-320.avif"));
        Assert.Equal(new ParseResult(true, "avif", 320, 160, null), r);
    }

    [Fact]
    public void Accepts_gps_320_webp_jsquash_output_320x160()
    {
        var r = ContainerParser.Parse(Bytes("gps-320.webp"));
        Assert.Equal(new ParseResult(true, "webp", 320, 160, null), r);
    }

    [Fact]
    public void Accepts_gps_placeholder_32x18()
    {
        var r = ContainerParser.Parse(Bytes("gps-placeholder.webp"));
        Assert.Equal(new ParseResult(true, "webp", 32, 18, null), r);
    }

    [Fact]
    public void Accepts_orient6_placeholder_18x32()
    {
        var r = ContainerParser.Parse(Bytes("orient6-placeholder.webp"));
        Assert.Equal(new ParseResult(true, "webp", 18, 32, null), r);
    }

    [Fact]
    public void Accepts_vp8x_with_no_flags_and_matching_canvas()
    {
        var r = ContainerParser.Parse(WebpWithVp8xFlags(0));
        Assert.Equal(new ParseResult(true, "webp", 200, 400, null), r);
    }

    [Fact]
    public void Accepts_avif_with_largesize_mdat()
    {
        var r = ContainerParser.Parse(AvifWithLargesizeMdat());
        Assert.Equal(new ParseResult(true, "avif", 200, 400, null), r);
    }

    [Fact]
    public void Accepts_avif_with_size_zero_mdat()
    {
        var r = ContainerParser.Parse(AvifWithSizeZeroMdat());
        Assert.Equal(new ParseResult(true, "avif", 200, 400, null), r);
    }

    [Fact]
    public void Rejects_avif_largesize_beyond_end_of_file()
    {
        var b = AvifWithLargesizeMdat();
        // orient6-200.avif: ftyp 0x20 + meta 0xf2 => mdat starts at 0x112; largesize at +8.
        Assert.Equal("mdat", System.Text.Encoding.ASCII.GetString(b, 0x112 + 4, 4)); // precondition
        System.Buffers.Binary.BinaryPrimitives.WriteUInt64BigEndian(b.AsSpan(0x112 + 8, 8), ulong.MaxValue);
        AssertRejected(b, "malformed mdat");
    }

    [Fact]
    public void Fixture_helper_roundtrip_is_byte_identical()
    {
        // Precondition for every AVIF mutation below: parse + serialise changes nothing.
        var src = Bytes("orient6-200.avif");
        Assert.Equal(src, Serialize(ParseAvif(src)));
    }

    // ---------------------------------------------------------------- FR-041: reader 1 alone

    [Fact]
    public void Reader1_alone_rejects_gps_avif()
    {
        var r = ContainerParser.Parse(Bytes("gps-sips.avif"));
        Assert.False(r.Ok);
        Assert.Equal("AVIF item Exif", r.Reason);
        Assert.Contains("Exif", r.Reason);
    }

    [Fact]
    public void Reader1_alone_rejects_gps_webp()
    {
        var r = ContainerParser.Parse(Bytes("gps-cwebp.webp"));
        Assert.False(r.Ok);
        Assert.Equal("VP8X EXIF flag", r.Reason);
        Assert.Contains("EXIF", r.Reason);
    }

    // ---------------------------------------------------------------- AVIF mutations (T018)

    [Fact]
    public void Rejects_avif_second_av01_item_embedded_thumbnail() =>
        AssertRejected(AvifWithSecondAv01(), "AVIF second av01 item");

    [Fact]
    public void Rejects_avif_exif_item() =>
        AssertRejected(AvifWithItem("Exif"), "AVIF item Exif");

    [Fact]
    public void Rejects_avif_exif_item_added_to_gps_320() =>
        AssertRejected(AvifWithItem("Exif", Bytes("gps-320.avif")), "AVIF item Exif");

    [Fact]
    public void Rejects_avif_mime_item() =>
        AssertRejected(AvifWithItem("mime"), "AVIF item mime");

    [Fact]
    public void Rejects_avif_iref() =>
        AssertRejected(AvifWithIref(), "AVIF iref box");

    [Theory]
    [InlineData("irot", "AVIF property irot")]
    [InlineData("imir", "AVIF property imir")]
    [InlineData("clap", "AVIF property clap")]
    public void Rejects_avif_transform_property(string property, string reason) =>
        AssertRejected(AvifWithProperty(property), reason);

    [Fact]
    public void Rejects_avif_colr_prof() =>
        AssertRejected(AvifWithColrProf(), "AVIF colr prof");

    [Fact]
    public void Rejects_avif_brand_avis() =>
        AssertRejected(AvifWithBrandAvis(), "AVIF brand avis");

    [Fact]
    public void Rejects_avif_unknown_top_level_box() =>
        AssertRejected(AvifWithUnknownTopLevelBox(), "AVIF unknown top-level box");

    [Fact]
    public void Rejects_avif_truncated() =>
        AssertRejected(Truncated(Bytes("orient6-200.avif")), "malformed mdat");

    [Fact]
    public void Rejects_avif_truncated_inside_meta() =>
        AssertRejected(Bytes("orient6-200.avif")[..0x60], "malformed meta");

    // ---------------------------------------------------------------- WebP mutations (T018)

    [Fact]
    public void Rejects_webp_ICCP_chunk() =>
        AssertRejected(WebpWithChunk("ICCP"), "WebP ICCP chunk");

    [Fact]
    public void Rejects_webp_EXIF_chunk() =>
        AssertRejected(WebpWithChunk("EXIF", GpsTiffBlock()), "WebP EXIF chunk");

    [Fact]
    public void Rejects_webp_XMP_chunk() =>
        AssertRejected(WebpWithChunk("XMP "), "WebP XMP chunk");

    [Fact]
    public void Rejects_webp_ANIM_chunk() =>
        AssertRejected(WebpWithChunk("ANIM"), "WebP ANIM chunk");

    [Fact]
    public void Rejects_webp_unknown_chunk() =>
        AssertRejected(WebpWithChunk("ZZZZ"), "WebP unknown chunk");

    [Theory]
    [InlineData(FlagIcc, "VP8X ICC flag")]
    [InlineData(FlagExif, "VP8X EXIF flag")]
    [InlineData(FlagXmp, "VP8X XMP flag")]
    [InlineData(FlagAnim, "VP8X animation flag")]
    public void Rejects_webp_vp8x_flag(byte flag, string reason) =>
        AssertRejected(WebpWithVp8xFlags(flag), reason);

    [Fact]
    public void Rejects_webp_gps_exif_on_gps_320() =>
        AssertRejected(WebpWithGpsExif(), "VP8X EXIF flag");

    [Fact]
    public void Rejects_webp_wrong_riff_length() =>
        AssertRejected(WebpWithWrongRiffLength(), "malformed RIFF");

    [Fact]
    public void Rejects_webp_truncated() =>
        AssertRejected(Truncated(Bytes("orient6-200.webp")), "malformed RIFF");

    [Fact]
    public void Rejects_icc_webp() =>
        AssertRejected(Bytes("icc.webp"), "VP8X ICC flag");

    [Fact]
    public void Rejects_placeholder_exif_webp() =>
        AssertRejected(Bytes("placeholder-exif.webp"), "VP8X EXIF flag");

    [Fact]
    public void Rejects_jpeg_as_unsupported_format() =>
        AssertRejected(Bytes("gps-camera.jpg"), "unsupported format");

    [Fact]
    public void Rejects_empty_input() =>
        AssertRejected([], "unsupported format");

    // ---------------------------------------------------------------- fuzz: never throws

    [Fact]
    public void Fuzz_2000_truncations_and_byte_flips_never_throw()
    {
        byte[][] seeds =
        [
            Bytes("orient6-200.avif"), Bytes("orient6-200.webp"), Bytes("gps-320.avif"),
            Bytes("gps-320.webp"), Bytes("gps-placeholder.webp"), Bytes("orient6-placeholder.webp"),
            Bytes("gps-sips.avif"), Bytes("gps-cwebp.webp"),
        ];
        var rng = new Random(20261101);
        for (int i = 0; i < 2000; i++)
        {
            var src = seeds[i % seeds.Length];
            byte[] input;
            if (i % 2 == 0)
            {
                input = src[..rng.Next(0, src.Length)];
            }
            else
            {
                input = src.ToArray();
                int flips = rng.Next(1, 9);
                for (int f = 0; f < flips; f++)
                    input[rng.Next(input.Length)] = (byte)rng.Next(256);
            }

            var r = ContainerParser.Parse(input); // must not throw
            // Bounds checks, not the safety-net catch, must handle every case.
            Assert.NotEqual(ContainerParser.InternalErrorReason, r.Reason);
            if (!r.Ok) Assert.False(string.IsNullOrEmpty(r.Reason));
        }
    }

    private static void AssertRejected(byte[] file, string reason)
    {
        var r = ContainerParser.Parse(file);
        Assert.False(r.Ok);
        Assert.Equal(reason, r.Reason);
    }
}
