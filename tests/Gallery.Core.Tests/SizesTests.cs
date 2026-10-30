using Gallery.Core.Images;

namespace Gallery.Core.Tests;

public class SizesTests
{
    [Fact]
    public void ForGrid_3_3_1_ReturnsExpectedString()
    {
        var result = Sizes.ForGrid(3, 3, 1);
        Assert.Equal(
            "(min-width: 1200px) calc((100vw - 192px) / 3), (min-width: 600px) calc((100vw - 128px) / 3), calc((100vw - 32px) / 1)",
            result);
    }

    [Fact]
    public void ForGrid_4_3_1_ReturnsExpectedString()
    {
        var result = Sizes.ForGrid(4, 3, 1);
        Assert.Equal(
            "(min-width: 1200px) calc((100vw - 240px) / 4), (min-width: 600px) calc((100vw - 128px) / 3), calc((100vw - 32px) / 1)",
            result);
    }

    [Theory]
    [InlineData(0, 3, 1)]
    [InlineData(7, 3, 1)]
    [InlineData(3, 0, 1)]
    [InlineData(3, 7, 1)]
    [InlineData(3, 3, 0)]
    [InlineData(3, 3, 7)]
    public void ForGrid_OutOfRange_Throws(int large, int medium, int small)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Sizes.ForGrid(large, medium, small));
    }

    [Fact]
    public void ForGrid_1_1_1_ReturnsExpectedString()
    {
        // large: 2*48 + 0*48 = 96; medium: 2*32 + 0*32 = 64; small: 2*16 + 0*16 = 32
        var result = Sizes.ForGrid(1, 1, 1);
        Assert.Equal(
            "(min-width: 1200px) calc((100vw - 96px) / 1), (min-width: 600px) calc((100vw - 64px) / 1), calc((100vw - 32px) / 1)",
            result);
    }
}
