using Gallery.Web.Ui;

namespace Gallery.Web.Tests;

/// <summary>
/// SC-010: feature 006 adds optional parameters to public components and must not move any /ui/
/// page. Every catalogue page and the /ui/ index are compared byte for byte, with the inlined
/// stylesheet stripped, to snapshots captured BEFORE 006 changed any component
/// (specs/006-public-site-local/evidence/ui-baseline/). A failure means the component change is
/// wrong; do not regenerate the baseline to make it pass.
/// </summary>
public class UiPagesUnchangedTests
{
    private static readonly string Dir = Path.Combine(
        Render.RepoRoot, "specs", "006-public-site-local", "evidence", "ui-baseline");

    public static TheoryData<string> Slugs()
    {
        var data = new TheoryData<string> { "index" };
        foreach (var e in UiCatalog.Entries) data.Add(e.Slug);
        return data;
    }

    [Theory]
    [MemberData(nameof(Slugs))]
    public async Task Ui_page_markup_is_byte_identical_to_the_pre_006_baseline(string slug)
    {
        var type = slug == "index"
            ? typeof(Gallery.Web.Ui.Pages.Index)
            : UiCatalog.Entries.Single(e => e.Slug == slug).PageType;
        var html = Guards.StripStyle(await Render.Html(type));
        var path = Path.Combine(Dir, slug + ".html");

        if (!File.Exists(path))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, html);
            Assert.Fail($"Baseline {slug}.html was missing and has been written; re-run.");
        }

        Assert.Equal(await File.ReadAllTextAsync(path), html);
    }
}
