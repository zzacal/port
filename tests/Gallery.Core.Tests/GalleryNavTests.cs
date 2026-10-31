using Gallery.Core.Content;

namespace Gallery.Core.Tests;

public class GalleryNavTests
{
    private static readonly IReadOnlyList<string> Items = ["alpha", "beta", "gamma"];

    [Fact]
    public void Neighbours_FirstItem_NullPrevAndNextIsSecond()
    {
        var (prev, next) = GalleryNav.Neighbours(Items, "alpha");
        Assert.Null(prev);
        Assert.Equal("beta", next);
    }

    [Fact]
    public void Neighbours_MiddleItem_BothNeighboursSet()
    {
        var (prev, next) = GalleryNav.Neighbours(Items, "beta");
        Assert.Equal("alpha", prev);
        Assert.Equal("gamma", next);
    }

    [Fact]
    public void Neighbours_LastItem_PrevIsSecondAndNullNext()
    {
        var (prev, next) = GalleryNav.Neighbours(Items, "gamma");
        Assert.Equal("beta", prev);
        Assert.Null(next);
    }

    [Fact]
    public void Neighbours_SingleItem_BothNull()
    {
        var (prev, next) = GalleryNav.Neighbours(["only"], "only");
        Assert.Null(prev);
        Assert.Null(next);
    }

    [Fact]
    public void Neighbours_ItemNotPresent_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => GalleryNav.Neighbours(Items, "missing"));
    }
}
