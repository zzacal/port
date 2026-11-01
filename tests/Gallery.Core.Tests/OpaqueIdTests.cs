using Gallery.Core.Ids;

namespace Gallery.Core.Tests;

public class OpaqueIdTests
{
    [Fact]
    public void NewId_1000Ids_AllValidAndDistinct()
    {
        var ids = Enumerable.Range(0, 1000).Select(_ => OpaqueId.NewId()).ToList();
        Assert.All(ids, id => Assert.True(OpaqueId.IsValid(id), $"Invalid id: {id}"));
        Assert.Equal(1000, ids.Distinct().Count());
    }

    [Fact]
    public void IsValid_Null_ReturnsFalse() =>
        Assert.False(OpaqueId.IsValid(null));

    [Fact]
    public void IsValid_25Chars_ReturnsFalse() =>
        Assert.False(OpaqueId.IsValid("abcdefghijklmnopqrstuvwxy"));

    [Fact]
    public void IsValid_27Chars_ReturnsFalse() =>
        Assert.False(OpaqueId.IsValid("abcdefghijklmnopqrstuvwxyzz"));

    [Fact]
    public void IsValid_Uppercase_ReturnsFalse() =>
        Assert.False(OpaqueId.IsValid("ABCDEFGHIJKLMNOPQRSTUVWXYZ"));

    [Fact]
    public void IsValid_Contains0_ReturnsFalse() =>
        Assert.False(OpaqueId.IsValid("abcdefghijklmnopqrstuvwx0y"));

    [Fact]
    public void IsValid_Contains1_ReturnsFalse() =>
        Assert.False(OpaqueId.IsValid("abcdefghijklmnopqrstuvwx1y"));

    [Fact]
    public void IsValid_Contains8_ReturnsFalse() =>
        Assert.False(OpaqueId.IsValid("abcdefghijklmnopqrstuvwx8y"));

    [Fact]
    public void IsValid_Contains9_ReturnsFalse() =>
        Assert.False(OpaqueId.IsValid("abcdefghijklmnopqrstuvwx9y"));
}
