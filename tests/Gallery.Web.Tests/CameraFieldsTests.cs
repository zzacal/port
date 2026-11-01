using Gallery.Web.Upload;
using Gallery.Web.Tests.Fixtures;
using MetadataExtractor.Formats.Exif;

namespace Gallery.Web.Tests;

/// <summary>
/// T028: CameraFields tests. Uses tests/fixtures/gps-camera.jpg.
/// Precondition: MetadataExtractor sees a GpsDirectory with latitude in the fixture.
/// </summary>
public class CameraFieldsTests
{
    private static readonly string FixtureGps = Path.Combine(Render.RepoRoot, "tests", "fixtures", "gps-camera.jpg");

    private static Stream OpenFixture(string path)
    {
        var resolved = Path.GetFullPath(path);
        if (!File.Exists(resolved))
            throw new FileNotFoundException($"Fixture not found: {resolved}");
        return File.OpenRead(resolved);
    }

    [Fact]
    public void Precondition_fixture_has_GpsDirectory_with_latitude()
    {
        using var fs = OpenFixture(FixtureGps);
        var dirs = MetadataExtractor.ImageMetadataReader.ReadMetadata(fs);
        var gpsDir = dirs.OfType<GpsDirectory>().FirstOrDefault();
        Assert.NotNull(gpsDir);
        // The fixture has GPS Latitude
        Assert.True(gpsDir.ContainsTag(GpsDirectory.TagLatitude) ||
                    gpsDir.ContainsTag(GpsDirectory.TagLatitudeRef),
            "Fixture should contain GPS latitude data to ensure the test is non-vacuous");
    }

    [Fact]
    public void Read_extracts_Make_and_Model()
    {
        using var fs = OpenFixture(FixtureGps);
        var exif = CameraFields.Read(fs);
        Assert.NotNull(exif);
        Assert.Equal("TESTCAM", exif.Make);
        Assert.Equal("T1", exif.Model);
    }

    /// <summary>
    /// Owner report: the Camera field was missing on every seeded photo. The owner's Leica and
    /// Fujifilm exports store Make/Model in the Exif SubIFD, not IFD0 (the only place the reader
    /// looked; gps-camera.jpg has them in IFD0, so the test above stayed green). This fixture
    /// reproduces that layout: IFD0 holds only the ExifOffset pointer.
    /// </summary>
    [Fact]
    public void Read_extracts_Make_Model_and_Lens_stored_in_the_SubIFD()
    {
        var path = Path.Combine(Render.RepoRoot, "tests", "fixtures", "camera-subifd.jpg");
        using (var probe = OpenFixture(path))
        {
            var dirs = MetadataExtractor.ImageMetadataReader.ReadMetadata(probe);
            Assert.False(dirs.OfType<ExifIfd0Directory>().Single().ContainsTag(ExifDirectoryBase.TagMake),
                "precondition: Make must NOT be in IFD0 for this fixture");
        }

        using var fs = OpenFixture(path);
        var exif = CameraFields.Read(fs);
        Assert.NotNull(exif);
        Assert.Equal("SUBCAM", exif.Make);
        Assert.Equal("S2 PRO", exif.Model);
        Assert.Equal("SUBLENS 35", exif.Lens);
    }

    [Fact]
    public void Serialized_Exif_contains_no_gps_latitude_longitude_altitude()
    {
        using var fs = OpenFixture(FixtureGps);
        var exif = CameraFields.Read(fs);
        Assert.NotNull(exif);

        // Serialize the Exif record to JSON
        var json = System.Text.Json.JsonSerializer.Serialize(exif,
            new System.Text.Json.JsonSerializerOptions { WriteIndented = false });
        var lower = json.ToLowerInvariant();

        Assert.DoesNotContain("gps", lower);
        Assert.DoesNotContain("latitude", lower);
        Assert.DoesNotContain("longitude", lower);
        Assert.DoesNotContain("altitude", lower);
    }

    [Fact]
    public void Serialized_ImageDoc_contains_no_gps_latitude_longitude_altitude()
    {
        using var fs = OpenFixture(FixtureGps);
        var exif = CameraFields.Read(fs);
        Assert.NotNull(exif);

        // Build an ImageDoc and serialize to BSON/JSON
        var cameraDoc = new Gallery.Web.Data.CameraDoc
        {
            Make = exif.Make,
            Model = exif.Model,
            Lens = exif.Lens,
            FocalMm = exif.FocalLengthMm,
            Focal35Mm = exif.FocalLength35Mm,
            FNumber = exif.FNumber,
            Exposure = exif.ExposureSeconds,
            Iso = exif.Iso,
        };
        var imageDoc = new Gallery.Web.Data.ImageDoc
        {
            Id = "testid",
            SeedKey = "test-photo.jpg",  // Do not use 'gps' in seedKey to avoid false positive
            Camera = cameraDoc,
        };

        var json2 = System.Text.Json.JsonSerializer.Serialize(imageDoc,
            new System.Text.Json.JsonSerializerOptions { WriteIndented = false });
        var bsonStr = json2.ToLowerInvariant();

        Assert.DoesNotContain("gps", bsonStr);
        Assert.DoesNotContain("latitude", bsonStr);
        Assert.DoesNotContain("longitude", bsonStr);
        Assert.DoesNotContain("altitude", bsonStr);
    }

    [Fact]
    public void Source_CameraFields_does_not_contain_Gps()
    {
        // Read our own source code to assert no GpsDirectory reference
        var seedDir = Path.GetFullPath(Path.Combine(Render.RepoRoot, "src", "Gallery.Web", "Upload"));
        var cameraFieldsPath = Path.Combine(seedDir, "CameraFields.cs");
        Assert.True(File.Exists(cameraFieldsPath), $"CameraFields.cs not found at: {cameraFieldsPath}");

        var source = File.ReadAllText(cameraFieldsPath);
        // Per task contract: "it never references GpsDirectory"
        Assert.DoesNotContain("GpsDirectory", source);
    }
}
