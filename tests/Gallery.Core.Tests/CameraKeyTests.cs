using Gallery.Core.Images;

namespace Gallery.Core.Tests;

/// <summary>013 T003 (research R-02): the camera grouping key and display name.</summary>
public class CameraKeyTests
{
    [Fact]
    public void Case_and_space_variants_give_equal_keys()
    {
        Assert.Equal(CameraKey.Key("Canon", "Canon  EOS R5"), CameraKey.Key("CANON ", "canon eos r5"));
        Assert.Equal("canon\u001fcanon eos r5", CameraKey.Key(" Canon\t", "Canon  EOS\n R5"));
    }

    [Fact]
    public void Make_only_and_model_only_keys()
    {
        Assert.Equal("fujifilm\u001f", CameraKey.Key("FUJIFILM", null));
        Assert.Equal("\u001fx100v", CameraKey.Key("  ", "X100V"));
        Assert.NotEqual(CameraKey.Key("X100V", null), CameraKey.Key(null, "X100V"));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", "")]
    [InlineData("  ", "\t")]
    [InlineData(null, " ")]
    public void Both_blank_is_no_camera(string? make, string? model) =>
        Assert.Null(CameraKey.Key(make, model));

    [Fact]
    public void Normalise_and_collapse()
    {
        Assert.Equal("", CameraKey.Normalise(null));
        Assert.Equal("", CameraKey.Normalise("   "));
        Assert.Equal("nikon z 8", CameraKey.Normalise("  NIKON   Z 8 "));
        Assert.Null(CameraKey.Collapse(" "));
        Assert.Null(CameraKey.Collapse(null));
        Assert.Equal("NIKON Z 8", CameraKey.Collapse("  NIKON   Z 8 "));
    }

    [Theory]
    [InlineData("Canon", "Canon EOS R5", "Canon EOS R5")]
    [InlineData("NIKON CORPORATION", "NIKON Z 8", "NIKON Z 8")]
    [InlineData("  Canon ", "Canon   EOS R5", "Canon EOS R5")]
    [InlineData("FUJIFILM", null, "FUJIFILM")]
    [InlineData(null, "X100V", "X100V")]
    [InlineData("OM Digital Solutions", "OM-1", "OM Digital Solutions OM-1")]
    public void DisplayName_is_ExifFormat_Camera_over_collapsed_values(string? make, string? model, string expected) =>
        Assert.Equal(expected, CameraKey.DisplayName(make, model));

    [Fact]
    public void DisplayName_of_no_camera_is_null() =>
        Assert.Null(CameraKey.DisplayName(" ", null));
}
