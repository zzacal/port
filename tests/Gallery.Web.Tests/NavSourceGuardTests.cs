using System.Text.RegularExpressions;

namespace Gallery.Web.Tests;

/// <summary>
/// 010 T012 [SAFETY] II.2 (data-model "Gallery.nav" rule 1, single writer): only
/// <c>Library/SiteProjector.cs</c> writes a gallery's <c>nav</c>, and only <c>Data/Documents.cs</c>
/// declares it. A source scan like <see cref="OwnerWriteGuardTests"/>: in every <c>src/**/*.cs</c>
/// outside <c>bin</c>/<c>obj</c>, a line naming the literal <c>"nav"</c>, a <c>.Set(</c>/<c>.Unset(</c>
/// update that names <c>.Nav</c>, or a <c>Nav =</c> assignment fails with its <c>path:line</c>.
/// Reading <c>gallery.Nav</c> (the public gallery route) is allowed. Do not widen the allow-list to
/// make a new writer pass.
/// </summary>
public class NavSourceGuardTests
{
    private static readonly string[] Allowed =
    [
        "Gallery.Web/Library/SiteProjector.cs",
        "Gallery.Web/Data/Documents.cs",
    ];

    private static readonly Regex NavLiteral = new("\"nav\"", RegexOptions.CultureInvariant);
    private static readonly Regex NavUpdate = new(@"\.(Set|Unset)\([^;]*\.Nav\b", RegexOptions.CultureInvariant);
    private static readonly Regex NavAssign = new(@"\bNav\s*=(?![=>])", RegexOptions.CultureInvariant);

    /// <summary>The violations in one file's lines, as <c>path:line: text</c>.</summary>
    internal static IEnumerable<string> Violations(string rel, IReadOnlyList<string> lines)
    {
        if (Allowed.Contains(rel)) yield break;
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            if (NavLiteral.IsMatch(line) || NavUpdate.IsMatch(line) || NavAssign.IsMatch(line))
                yield return $"{rel}:{i + 1}: {line.Trim()}";
        }
    }

    [Fact]
    public void Only_the_projector_writes_nav()
    {
        var src = Path.Combine(Render.RepoRoot, "src");
        var violations = new List<string>();

        foreach (var file in Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(src, file).Replace('\\', '/');
            if (rel.Contains("/bin/") || rel.Contains("/obj/")) continue;
            violations.AddRange(Violations(rel, File.ReadAllLines(file)));
        }

        Assert.True(violations.Count == 0,
            "nav written outside SiteProjector.cs (II.2 single writer):\n" + string.Join("\n", violations));
    }

    [Fact]
    public void The_projector_itself_writes_nav_so_the_scan_is_not_vacuous()
    {
        var projector = File.ReadAllLines(Path.Combine(Render.RepoRoot, "src", "Gallery.Web", "Library", "SiteProjector.cs"));
        Assert.Contains(projector, l => NavUpdate.IsMatch(l));
    }

    [Theory]
    [InlineData("var u = Builders<GalleryDoc>.Update.Unset(g => g.Nav);")]
    [InlineData("    .Set(x => x.Nav, nav)")]
    [InlineData("doc.Nav = new GalleryNavDoc();")]
    [InlineData("var g = new GalleryDoc { Nav = n };")]
    [InlineData("update.Set(\"nav\", value);")]
    public void A_writer_line_elsewhere_is_reported_with_its_path_and_line(string line)
    {
        var found = Violations("Gallery.Web/Library/GalleryService.cs", ["// header", line]).ToList();
        Assert.Equal(new[] { $"Gallery.Web/Library/GalleryService.cs:2: {line.Trim()}" }, found.ToArray());
    }

    [Theory]
    [InlineData("if (gallery.Visibility == \"public\" && gallery.Nav is { } nav)")]
    [InlineData("var prev = gallery.Nav?.Prev;")]
    [InlineData("new(UiLevel.Molecules, \"Nav\", \"molecules/nav\", typeof(NavPage)),")]
    [InlineData("if (gallery.Nav == null) return;")]
    public void A_read_is_allowed(string line)
    {
        Assert.Empty(Violations("Gallery.Web/Site/PublicEndpoints.cs", [line]));
    }
}
