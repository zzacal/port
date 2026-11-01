using Gallery.Core.Text;

namespace Gallery.Core.Tests;

public class SlugTests
{
    [Fact]
    public void From_September5_ReturnsExpected() =>
        Assert.Equal("september-5", Slug.From("September 5"));

    [Fact]
    public void From_CafeNoir_ReturnsExpected() =>
        Assert.Equal("cafe-noir", Slug.From("Café — Noir!"));

    [Fact]
    public void From_AllPunctuation_ReturnsGallery() =>
        Assert.Equal("gallery", Slug.From("!!!"));

    [Fact]
    public void From_100CharTitle_AtMost60CharsNoTrailingDash()
    {
        var title = new string('a', 50) + " " + new string('b', 49);
        var slug = Slug.From(title);
        Assert.True(slug.Length <= 60, $"Slug too long: {slug.Length}");
        Assert.False(slug.EndsWith('-'), $"Slug ends with dash: {slug}");
    }
}
