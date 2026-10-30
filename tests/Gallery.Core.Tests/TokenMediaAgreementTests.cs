using System.Text.RegularExpressions;
using Gallery.Core.Design;

namespace Gallery.Core.Tests;

/// <summary>
/// CSS media queries can't read custom properties, so breakpoints are literals in the
/// component .razor.css files. This keeps every literal equal to the Tokens breakpoints
/// (or their -1 complements for max-width queries). Research R-02.
/// </summary>
public partial class TokenMediaAgreementTests
{
    private static readonly int[] Allowed =
        [Tokens.BreakLarge, Tokens.BreakMedium, Tokens.BreakLarge - 1, Tokens.BreakMedium - 1];

    [Fact]
    public void Every_media_query_width_equals_a_token_breakpoint()
    {
        var webSrc = Path.Combine(FindRepoRoot(), "src", "Gallery.Web");
        var offenders = new List<string>();

        foreach (var file in Directory.EnumerateFiles(webSrc, "*.razor.css", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") ||
                file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
                continue;

            var css = File.ReadAllText(file);
            foreach (Match media in MediaRule().Matches(css))
            foreach (Match px in WidthPx().Matches(media.Value))
            {
                var value = int.Parse(px.Groups[1].Value);
                if (!Allowed.Contains(value))
                    offenders.Add($"{Path.GetRelativePath(webSrc, file)}: {media.Value.Trim()}");
            }
        }

        Assert.True(offenders.Count == 0,
            "Media query widths must equal Tokens.BreakLarge/BreakMedium (or -1):\n" + string.Join("\n", offenders));
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Gallery.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Gallery.slnx not found");
    }

    [GeneratedRegex(@"@media[^{]*")]
    private static partial Regex MediaRule();

    [GeneratedRegex(@"(?:min|max)-width\s*:\s*(\d+)px")]
    private static partial Regex WidthPx();
}
