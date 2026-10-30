using System.Text.RegularExpressions;

namespace Gallery.Web.Tests;

/// <summary>
/// Guards the placeholder-stretch defect found in the browser: grid-stacked layers let a
/// decoded 32 px placeholder resize the shared box. That defect lived in CSS geometry and was
/// invisible to the rendered HTML, so this asserts the CSS rule itself (feature 003, decision #28).
/// Browser proof is in evidence/browser.md.
/// </summary>
public partial class PictureLayoutCssTests
{
    private static readonly string Css = File.ReadAllText(
        Path.Combine(Render.RepoRoot, "src", "Gallery.Web", "Components", "Atoms", "Picture.razor.css"));

    [Fact]
    public void Placeholder_and_thumbnail_are_out_of_flow_so_they_cannot_size_the_box()
    {
        var rule = RuleFor(@"\.ph,\s*\.thumb");
        Assert.Matches(@"position:\s*absolute", rule);
    }

    [Fact]
    public void Picture_does_not_stack_layers_in_a_shared_grid_cell()
    {
        Assert.DoesNotMatch(@"grid-area\s*:", Css);
        Assert.Matches(@"position:\s*relative", RuleFor(@"\.pic"));
    }

    private static string RuleFor(string selectorPattern)
    {
        var m = Regex.Match(Css, @"(^|\})\s*" + selectorPattern + @"\s*\{(?<body>[^}]*)\}", RegexOptions.Multiline);
        Assert.True(m.Success, $"rule {selectorPattern} not found in Picture.razor.css");
        return m.Groups["body"].Value;
    }
}
