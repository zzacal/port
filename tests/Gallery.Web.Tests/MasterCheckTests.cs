using System.Text.Json;
using Gallery.Web.Upload;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using static Gallery.Web.Tests.Fixtures.UploadFixtures;

namespace Gallery.Web.Tests;

/// <summary>T022: header-only master check (research R-06) on the committed JPEG fixtures.</summary>
public class MasterCheckTests
{
    private const long Max = 20_971_520;

    private static MasterResult CheckWhole(string name)
    {
        var b = Bytes(name);
        return MasterCheck.Check(b, b.LongLength, Max);
    }

    [Fact]
    public void Orientation_6_swaps_to_upright_200x400()
    {
        var r = CheckWhole("orientation-6.jpg");
        Assert.True(r.Ok, r.Reason);
        Assert.Equal(200, r.UprightWidth);
        Assert.Equal(400, r.UprightHeight);
        Assert.False(r.NeedsMoreBytes);
        Assert.Null(r.Reason);
    }

    [Fact]
    public void Gps_camera_gives_400x200_with_camera_and_no_gps_value()
    {
        var bytes = Bytes("gps-camera.jpg");
        var r = MasterCheck.Check(bytes, bytes.LongLength, Max);
        Assert.True(r.Ok, r.Reason);
        Assert.Equal(400, r.UprightWidth);
        Assert.Equal(200, r.UprightHeight);
        Assert.NotNull(r.Camera);
        Assert.Equal("TESTCAM", r.Camera.Make);
        Assert.Equal("T1", r.Camera.Model);

        var json = JsonSerializer.Serialize(r);
        Assert.DoesNotContain("gps", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("latitude", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("longitude", json, StringComparison.OrdinalIgnoreCase);

        // Non-vacuous: every GPS tag's value in the fixture is absent from the result.
        using var s = new MemoryStream(bytes);
        var gps = ImageMetadataReader.ReadMetadata(s).OfType<GpsDirectory>().Single();
        var values = gps.Tags.Select(t => t.Description).OfType<string>().Where(v => v.Length >= 3).ToList();
        Assert.NotEmpty(values);
        foreach (var v in values)
            Assert.DoesNotContain(v, json, StringComparison.Ordinal);
    }

    [Fact]
    public void Camera_in_subifd_is_read()
    {
        var r = CheckWhole("camera-subifd.jpg");
        Assert.True(r.Ok, r.Reason);
        Assert.NotNull(r.Camera);
        Assert.NotNull(r.Camera.Make);
        Assert.NotNull(r.Camera.Model);
    }

    [Fact]
    public void Not_a_jpeg_is_rejected()
    {
        var r = CheckWhole("not-a-jpeg.jpg");
        Assert.False(r.Ok);
        Assert.False(r.NeedsMoreBytes);
        Assert.Equal("not a JPEG", r.Reason);
    }

    [Fact]
    public void Avif_bytes_are_not_a_jpeg()
    {
        var r = CheckWhole("orient6-200.avif");
        Assert.False(r.Ok);
        Assert.Equal("not a JPEG", r.Reason);
    }

    [Fact]
    public void Short_prefix_of_large_jpeg_needs_more_bytes()
    {
        var full = Bytes("fixture-6000x4000.jpg");
        var r = MasterCheck.Check(full[..200], full.LongLength, Max);
        Assert.False(r.Ok);
        Assert.True(r.NeedsMoreBytes);
        Assert.Equal("unreadable JPEG header", r.Reason);
    }

    [Fact]
    public void Short_prefix_of_gps_camera_without_sof_needs_more_bytes()
    {
        // Probe P6: a 200-byte prefix of gps-camera.jpg omits the JPEG directory without throwing.
        var full = Bytes("gps-camera.jpg");
        var r = MasterCheck.Check(full[..200], full.LongLength, Max);
        Assert.False(r.Ok);
        Assert.True(r.NeedsMoreBytes);
        Assert.Equal("unreadable JPEG header", r.Reason);
    }

    [Fact]
    public void Header_prefix_of_large_jpeg_gives_6000x4000()
    {
        var full = Bytes("fixture-6000x4000.jpg");
        var prefix = full[..Math.Min(full.Length, 262_144)];
        var r = MasterCheck.Check(prefix, full.LongLength, Max);
        Assert.True(r.Ok, r.Reason);
        Assert.Equal(6000, r.UprightWidth);
        Assert.Equal(4000, r.UprightHeight);
    }

    [Fact]
    public void Full_large_jpeg_gives_6000x4000()
    {
        var r = CheckWhole("fixture-6000x4000.jpg");
        Assert.True(r.Ok, r.Reason);
        Assert.Equal(6000, r.UprightWidth);
        Assert.Equal(4000, r.UprightHeight);
    }

    [Fact]
    public void Whole_file_with_no_sof_is_unreadable_not_retry()
    {
        // The prefix IS the whole object, so a retry cannot help.
        var head = Bytes("gps-camera.jpg")[..200];
        var r = MasterCheck.Check(head, head.LongLength, Max);
        Assert.False(r.Ok);
        Assert.False(r.NeedsMoreBytes);
        Assert.Equal("unreadable JPEG header", r.Reason);
    }

    [Fact]
    public void Over_limit_length_is_rejected_with_size_reason()
    {
        var b = Bytes("gps-camera.jpg");
        var r = MasterCheck.Check(b, 20_971_521, Max);
        Assert.False(r.Ok);
        Assert.False(r.NeedsMoreBytes);
        Assert.Equal("20.0 MB, over the 20 MB limit", r.Reason);
    }

    [Fact]
    public void Exactly_the_limit_is_accepted()
    {
        var b = Bytes("gps-camera.jpg");
        var r = MasterCheck.Check(b, 20_971_520, Max);
        Assert.True(r.Ok, r.Reason);
    }
}
