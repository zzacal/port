using Gallery.Core.Design;

namespace Gallery.Core.Tests;

/// <summary>Colour tokens added for the admin components (feature 005, research R-08).</summary>
public class AdminTokensTests
{
    [Theory]
    [InlineData("--color-danger:#b3261e")]
    [InlineData("--color-success:#1e7a3c")]
    [InlineData("--color-surface:#f4f4f4")]
    public void ToCssRoot_contains_admin_colour(string expected)
        => Assert.Contains(expected, Tokens.ToCssRoot());

    [Theory]
    [InlineData("ColorDanger")]
    [InlineData("ColorSuccess")]
    [InlineData("ColorSurface")]
    public void All_lists_admin_colour(string name)
        => Assert.Contains(Tokens.All(), t => t.Group == "Color" && t.Name == name);
}
