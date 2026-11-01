using Gallery.Core.Images;

namespace Gallery.Core.Tests;

/// <summary>008 T008: the ladder rule (data-model "Ladder rule", IV.6, FR-015, E-11).</summary>
public class LadderTests
{
    [Fact]
    public void Widths_are_the_IV6_ladder() =>
        Assert.Equal([320, 640, 960, 1280, 1920, 2560], Ladder.Widths);

    [Theory]
    [InlineData(200, new[] { 200 })]
    [InlineData(320, new[] { 320 })]
    [InlineData(1000, new[] { 320, 640, 960 })]
    [InlineData(2560, new[] { 320, 640, 960, 1280, 1920, 2560 })]
    [InlineData(6000, new[] { 320, 640, 960, 1280, 1920, 2560 })]
    public void For_returns_every_rung_not_above_the_source(int width, int[] expected) =>
        Assert.Equal(expected, Ladder.For(width));

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void For_rejects_zero_or_negative(int width) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Ladder.For(width));

    [Fact]
    public void HeightFor_rounds() => Assert.Equal(427, Ladder.HeightFor(640, 6000, 4000));
}
