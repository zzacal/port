using Gallery.Core.Design;

namespace Gallery.Core.Tests;

public class TokensTests
{
    [Fact]
    public void ToCssRoot_ContainsSpaceL()
    {
        var css = Tokens.ToCssRoot();
        Assert.Contains("--space-l:48px", css);
    }

    [Fact]
    public void ToCssRoot_ContainsSpaceM()
    {
        var css = Tokens.ToCssRoot();
        Assert.Contains("--space-m:32px", css);
    }

    [Fact]
    public void ToCssRoot_ContainsSpaceS()
    {
        var css = Tokens.ToCssRoot();
        Assert.Contains("--space-s:16px", css);
    }

    [Fact]
    public void ToCssRoot_ContainsCellRatio()
    {
        var css = Tokens.ToCssRoot();
        Assert.Contains("--cell-ratio:4/3", css);
    }

    [Fact]
    public void ToCssRoot_ContainsTypeCaption()
    {
        var css = Tokens.ToCssRoot();
        Assert.Contains("--type-caption:12px", css);
    }

    [Fact]
    public void ToCssRoot_ContainsTypeBody()
    {
        var css = Tokens.ToCssRoot();
        Assert.Contains("--type-body:14px", css);
    }

    [Fact]
    public void ToCssRoot_ContainsTypeBrand()
    {
        var css = Tokens.ToCssRoot();
        Assert.Contains("--type-brand:16px", css);
    }

    [Fact]
    public void ToCssRoot_ContainsColorBg()
    {
        var css = Tokens.ToCssRoot();
        Assert.Contains("--color-bg:#fff", css);
    }

    [Fact]
    public void ToCssRoot_ContainsColorText()
    {
        var css = Tokens.ToCssRoot();
        Assert.Contains("--color-text:#000", css);
    }

    [Fact]
    public void ToCssRoot_ContainsColorMuted()
    {
        var css = Tokens.ToCssRoot();
        Assert.Contains("--color-muted:#666", css);
    }

    [Fact]
    public void ToCssRoot_ContainsColorRule()
    {
        var css = Tokens.ToCssRoot();
        Assert.Contains("--color-rule:#ddd", css);
    }

    [Fact]
    public void ToCssRoot_StartsWithRootBlock()
    {
        var css = Tokens.ToCssRoot();
        Assert.StartsWith(":root{", css);
        Assert.EndsWith("}", css);
    }

    [Fact]
    public void All_HasEntryForEveryConstant()
    {
        var all = Tokens.All();
        var names = all.Select(e => e.Name).ToHashSet();

        Assert.Contains("SpaceL", names);
        Assert.Contains("SpaceM", names);
        Assert.Contains("SpaceS", names);
        Assert.Contains("BreakLarge", names);
        Assert.Contains("BreakMedium", names);
        Assert.Contains("CellRatioW", names);
        Assert.Contains("CellRatioH", names);
        Assert.Contains("TypeCaption", names);
        Assert.Contains("TypeBody", names);
        Assert.Contains("TypeBrand", names);
        Assert.Contains("ColorBg", names);
        Assert.Contains("ColorText", names);
        Assert.Contains("ColorMuted", names);
        Assert.Contains("ColorRule", names);
        Assert.Contains("FontStack", names);
    }

    [Fact]
    public void All_IsNonEmpty()
    {
        Assert.NotEmpty(Tokens.All());
    }
}
