using Gallery.Web.Ui;

namespace Gallery.Web.Tests;

/// <summary>Covers every /ui page automatically, including ones added later (SC-008, FR-004).</summary>
public class AllPagesTests
{
    public static TheoryData<string> Slugs()
    {
        var data = new TheoryData<string>();
        foreach (var e in UiCatalog.Entries) data.Add(e.Slug);
        return data;
    }

    [Theory]
    [MemberData(nameof(Slugs))]
    public async Task Page_renders_without_inline_style_or_script(string slug)
    {
        var entry = UiCatalog.Entries.Single(e => e.Slug == slug);
        var html = await Render.Html(entry.PageType);

        Assert.False(string.IsNullOrWhiteSpace(html));
        Assert.StartsWith("<!doctype html>", html.TrimStart(), StringComparison.OrdinalIgnoreCase);
        Guards.AssertNoInlineStyle(html);
        Guards.AssertNoScript(html);
    }
}
