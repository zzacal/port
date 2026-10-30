using System.Text.RegularExpressions;
using Gallery.Web.Ui.Pages;

namespace Gallery.Web.Tests;

/// <summary>
/// FR-002 / SC-004: the admin additions to Tile and ContactSheet must not move the public output.
/// Compares the rendered public component pages to snapshots captured BEFORE feature 005 changed
/// anything (specs/005-admin-ui-components/evidence/baseline/*.html). The inlined &lt;style&gt; is
/// excluded because the shared stylesheet legitimately grows with new components.
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

        Assert.Equal(await File.ReadAllTextAsync(path), html);
    }

    private static string StripStyle(string html) => StyleBlock().Replace(html, "<style></style>");

    // Scoped CSS adds a b-xxxx attribute to the tag, so match <style ...>.
    [GeneratedRegex("<style[^>]*>.*?</style>", RegexOptions.Singleline)]
    private static partial Regex StyleBlock();
}
