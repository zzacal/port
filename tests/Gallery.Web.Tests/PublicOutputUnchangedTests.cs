using System.Text.RegularExpressions;
using Gallery.Web.Ui.Pages;

namespace Gallery.Web.Tests;

/// <summary>
/// FR-002 / SC-004: the admin additions to Tile and ContactSheet must not move the public output.
/// Compares the rendered public component pages to snapshots captured BEFORE feature 005 changed
/// anything (specs/005-admin-ui-components/evidence/baseline/*.html). The inlined &lt;style&gt; is
/// excluded because the shared stylesheet legitimately grows with new components.
///
/// Pages may gain new specimens after the baseline's (the ContactSheet page shows the admin usages
/// of the same component). Every baseline specimen must still be byte-identical and in the same
/// position, and the page chrome before and after them must be unchanged. Appended specimens are
/// allowed; anything that alters an existing one fails.
/// Regenerate only by deleting a snapshot on a tree known to be correct.
/// </summary>
public partial class PublicOutputUnchangedTests
{
    private static readonly string Dir = Path.Combine(
        Render.RepoRoot, "specs", "005-admin-ui-components", "evidence", "baseline");

    public static TheoryData<string> Pages() => new()
    {
        nameof(TilePage), nameof(ContactSheetPage), nameof(SoloViewerPage),
        nameof(PicturePage), nameof(PublicLayoutPage),
    };

    [Theory]
    [MemberData(nameof(Pages))]
    public async Task Public_page_markup_is_byte_identical_to_the_pre_005_baseline(string page)
    {
        var type = typeof(TilePage).Assembly.GetType("Gallery.Web.Ui.Pages." + page)!;
        var html = StripStyle(await Render.Html(type));
        var path = Path.Combine(Dir, page + ".html");

        if (!File.Exists(path))
        {
            Directory.CreateDirectory(Dir);
            await File.WriteAllTextAsync(path, html);
            Assert.Fail($"Baseline {page}.html was missing and has been written; re-run.");
        }

        var (bHead, bSpecs, bTail) = Split(await File.ReadAllTextAsync(path));
        var (nHead, nSpecs, nTail) = Split(html);

        Assert.Equal(bHead, nHead);
        Assert.Equal(bTail, nTail);
        Assert.True(nSpecs.Count >= bSpecs.Count, $"{page}: specimens were removed");
        for (var i = 0; i < bSpecs.Count; i++)
            Assert.Equal(bSpecs[i], nSpecs[i]);
    }

    /// <summary>Head (up to the first specimen), each specimen section, and tail (after the last).</summary>
    private static (string Head, List<string> Specs, string Tail) Split(string html)
    {
        var specs = Specimen().Matches(html).Select(m => m.Value).ToList();
        if (specs.Count == 0) return (html, specs, "");
        var first = html.IndexOf(specs[0], StringComparison.Ordinal);
        var last = html.LastIndexOf(specs[^1], StringComparison.Ordinal) + specs[^1].Length;
        return (html[..first], specs, html[last..]);
    }

    private static string StripStyle(string html) => StyleBlock().Replace(html, "<style></style>");

    // Scoped CSS adds a b-xxxx attribute to the tag, so match <style ...>.
    [GeneratedRegex("<style[^>]*>.*?</style>", RegexOptions.Singleline)]
    private static partial Regex StyleBlock();

    [GeneratedRegex("<section class=\"specimen\"[^>]*>.*?</section>", RegexOptions.Singleline)]
    private static partial Regex Specimen();
}
