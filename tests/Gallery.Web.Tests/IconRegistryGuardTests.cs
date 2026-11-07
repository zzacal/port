using System.Text.RegularExpressions;
using Gallery.Web.Components.Atoms;
using Gallery.Web.Components.Molecules;

namespace Gallery.Web.Tests;

/// <summary>
/// 015 T024 (FR-018, R-02 guard): the icon registry ships exactly what the product uses. The set of
/// icon names used in src/Gallery.Web (every <c>Icon="…"</c> on an <c>&lt;IconButton</c> or
/// <c>&lt;Icon</c>, every <c>Name="…"</c> on an <c>&lt;Icon</c>, in .razor and .cs files, plus every
/// <see cref="PhotoActions.IconOf"/> result) must EQUAL the key set of <see cref="Icons.Paths"/>:
/// nothing unused and nothing unknown. The catalogue pages under Ui/ do not count as use (they show
/// every icon by iterating the registry), and Icons.cs itself is the registry, not a use.
/// </summary>
public partial class IconRegistryGuardTests
{
    private static readonly string Src = Path.Combine(Render.RepoRoot, "src", "Gallery.Web");

    private static IEnumerable<string> SourceFiles() =>
        Directory.EnumerateFiles(Src, "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".razor", StringComparison.Ordinal) || f.EndsWith(".cs", StringComparison.Ordinal))
            .Where(f =>
            {
                var rel = Path.GetRelativePath(Src, f).Replace('\\', '/');
                return !rel.StartsWith("bin/", StringComparison.Ordinal) && !rel.StartsWith("obj/", StringComparison.Ordinal)
                    && !rel.StartsWith("Ui/", StringComparison.Ordinal) && rel != "Components/Atoms/Icons.cs";
            });

    /// <summary>(name, file) for every literal icon name in the product source.</summary>
    private static List<(string Name, string File)> Uses()
    {
        var uses = new List<(string, string)>();
        foreach (var f in SourceFiles())
        {
            var text = File.ReadAllText(f);
            foreach (Match tag in IconTag().Matches(text))
            {
                var attr = tag.Groups["tag"].Value == "Icon" ? NameAttr() : IconAttr();
                foreach (Match m in attr.Matches(tag.Value))
                    uses.Add((m.Groups[1].Value, Path.GetRelativePath(Render.RepoRoot, f)));
            }
        }
        foreach (var m in Enum.GetValues<Membership>())
            uses.Add((PhotoActions.IconOf(m), "PhotoActions.IconOf"));
        return uses;
    }

    [Fact]
    public void Every_registered_icon_is_used_and_every_used_icon_is_registered()
    {
        var uses = Uses();
        var used = uses.Select(u => u.Name).ToHashSet(StringComparer.Ordinal);
        var keys = Icons.Paths.Keys.ToHashSet(StringComparer.Ordinal);

        var problems = new List<string>();
        problems.AddRange(uses.Where(u => !keys.Contains(u.Name)).Select(u => $"unknown icon '{u.Name}' used in {u.File}"));
        problems.AddRange(keys.Where(k => !used.Contains(k)).Order(StringComparer.Ordinal).Select(k => $"registered icon '{k}' is used nowhere in the product"));
        Assert.True(problems.Count == 0, string.Join("\n", problems));
        Assert.Equal(keys.Order(StringComparer.Ordinal), used.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void The_scan_finds_literal_uses_so_the_guard_is_not_vacuous()
    {
        // At least one literal per kind of use: an IconButton Icon="…" and an Icon Name="…".
        var files = Uses().Where(u => u.File != "PhotoActions.IconOf").Select(u => u.Name).ToList();
        Assert.Contains("pencil", files);
        Assert.Contains("grip-vertical", files);
    }

    // An <Icon …> or <IconButton …> start tag, optionally namespace-qualified (DragHandle writes
    // <Gallery.Web.Components.Atoms.Icon …> because its own parameter is called Icon). Razor attribute
    // values can hold @(...) and quotes, so the tag ends at the first '>' outside a quoted value.
    [GeneratedRegex("<(?:[A-Za-z]+\\.)*(?<tag>IconButton|Icon)\\b(?:[^>\"]|\"[^\"]*\")*>", RegexOptions.Singleline)]
    private static partial Regex IconTag();

    [GeneratedRegex("\\sIcon=\"([a-z][a-z0-9-]*)\"")]
    private static partial Regex IconAttr();

    [GeneratedRegex("\\sName=\"([a-z][a-z0-9-]*)\"")]
    private static partial Regex NameAttr();
}
