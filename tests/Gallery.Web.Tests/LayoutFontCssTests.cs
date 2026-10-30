using System.Text.RegularExpressions;

namespace Gallery.Web.Tests;

/// <summary>
/// AdminLayout once shipped `font: inherit` on body. The root element has nothing to
/// inherit from, so every admin page rendered in the browser's default serif. The markup was
/// correct, so this guards the CSS rule itself (same approach as PictureLayoutCssTests).
/// </summary>
public class LayoutFontCssTests
{
    [Theory]
    [InlineData("Templates/AdminLayout.razor.css", "body.admin")]
    [InlineData("Templates/PublicLayout.razor.css", "body.public")]
    public void Document_body_takes_the_token_font_not_inherit(string file, string selector)
    {
        var css = File.ReadAllText(Path.Combine(Render.RepoRoot, "src", "Gallery.Web", "Components", file));
        var m = Regex.Match(css, Regex.Escape(selector) + @"\s*\{(?<b>[^}]*)\}");
        Assert.True(m.Success, $"{selector} rule missing in {file}");
        Assert.Matches(@"font-family:\s*var\(--font\)", m.Groups["b"].Value);
        Assert.DoesNotMatch(@"(^|;|\s)font:\s*inherit", m.Groups["b"].Value);
    }
}
