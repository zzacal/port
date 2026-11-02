using System.Text.RegularExpressions;

namespace Gallery.Web.Tests;

/// <summary>
/// 011 T019 (contracts/components.md C3): every public trail is built by <c>Site/Trails.cs</c>.
/// The string literals "Home" and "Galleries" may appear in the public site's source only there;
/// every other use goes through <c>Trails.HomeLabel</c>/<c>Trails.ListingLabel</c>. Scope: the
/// <c>.cs</c> and <c>.razor</c> files under <c>src/Gallery.Web/Site</c> and
/// <c>Components/Templates/PublicLayout.razor</c>. <c>Ui/</c> specimen pages are out of scope.
/// </summary>
public class TrailSourceGuardTests
{
    private static readonly Regex Literal = new("\"(Home|Galleries)\"", RegexOptions.Compiled);

    [Fact]
    public void Trail_labels_are_literal_only_in_Trails_cs()
    {
        var web = Path.Combine(Render.RepoRoot, "src", "Gallery.Web");
        var trails = Path.Combine(web, "Site", "Trails.cs");
        var files = Directory.EnumerateFiles(Path.Combine(web, "Site"), "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".cs", StringComparison.Ordinal) || f.EndsWith(".razor", StringComparison.Ordinal))
            .Append(Path.Combine(web, "Components", "Templates", "PublicLayout.razor"))
            .ToList();
        Assert.Contains(trails, files);
        Assert.True(files.Count >= 8, $"scanned only {files.Count} files");

        var violations = new List<string>();
        var inTrails = 0;
        foreach (var file in files)
        {
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
                foreach (Match m in Literal.Matches(lines[i]))
                {
                    if (file == trails) { inTrails++; continue; }
                    violations.Add($"{Path.GetRelativePath(web, file)}:{i + 1}: {m.Value}");
                }
        }

        // Non-vacuity: the scan does see the two labels where they are defined.
        Assert.Equal(2, inTrails);
        Assert.True(violations.Count == 0, "trail label literals outside Site/Trails.cs:\n" + string.Join("\n", violations));
    }
}
