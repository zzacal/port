using System.Text.RegularExpressions;
using Gallery.Web.Ui.Pages;

namespace Gallery.Web.Tests;

/// <summary>
/// Organism and template /ui pages (feature 005, T028/T029). Labels are matched as Specimen
/// headings, not loose substrings, so a label can't pass by appearing in body copy.
/// </summary>
public class OrganismStatePageTests
{
    public static TheoryData<Type, string[]> Pages() => new()
    {
        { typeof(OwnerBarPage), ["Upload current, with primary action", "Library current", "Galleries current, viewing a gallery", "Phone width"] },
        { typeof(SidePanelPage), ["At rest", "Saving", "Saved", "With danger area"] },
        { typeof(UploadDropzonePage), ["Idle", "Drag-over", "Files refused"] },
        { typeof(BatchFooterPage), ["In progress (12 of 40)", "Ready (40)", "Blocked (38 ready, 2 failed)", "Discard confirmation"] },
        { typeof(GalleryBuilderPage), ["At rest (cover pinned on photo 3)", "Dragging photo 2 to position 5", "Photo 3 lifted for keyboard moving", "Publish warning", "Empty gallery"] },
        { typeof(SignInCardPage), ["Sign in", "Claim an unclaimed site", "Refused (not the owner)", "No cover photo"] },
    };

    [Theory]
    [MemberData(nameof(Pages))]
    public async Task Page_shows_every_contract_state_as_a_specimen(Type page, string[] labels)
    {
        var html = await Render.Html(page);
        var headings = Regex.Matches(html, "<h2 class=\"specimen-label\"[^>]*>([^<]*)</h2>")
            .Select(m => System.Net.WebUtility.HtmlDecode(m.Groups[1].Value)).ToList();
        Assert.Equal(labels, headings);
    }

    [Fact]
    public async Task ContactSheet_page_admin_usages_keep_public_column_counts()
    {
        var html = await Render.Html<ContactSheetPage>();
        var headings = Regex.Matches(html, "<h2 class=\"specimen-label\"[^>]*>([^<]*)</h2>").Select(m => m.Groups[1].Value).ToList();
        Assert.Equal(["Admin library, 3 selected", "Admin gallery list, 2 private", "Admin upload batch"], headings.Skip(3));
        Assert.Equal(4, Guards.Count(html, "cs cs-l4 cs-m3 cs-s1"));   // 2 public + library + batch
        Assert.Equal(2, Guards.Count(html, "cs cs-l3 cs-m3 cs-s1"));   // home + gallery list
        Assert.Equal(3, Regex.Matches(html, "class=\"tile is-selected\"").Count);
        Assert.Equal(2, Regex.Matches(html, "class=\"tile is-private\"").Count);
    }

    [Fact]
    public async Task AdminLayout_page_is_a_full_document_with_bar_and_aside_and_no_script()
    {
        var html = await Render.Html<AdminLayoutPage>();
        Assert.StartsWith("<!doctype html>", html.TrimStart(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, Guards.Count(html, "<style"));
        Assert.Matches("class=\"owner-bar\"", html);
        Assert.Matches("class=\"admin-aside\"", html);
        Assert.Contains("aria-current=\"page\"", html);
        Assert.Matches("<meta name=\"robots\" content=\"noindex\"[^>]*>", html);
        Guards.AssertNoScript(html);
        Guards.AssertNoInlineStyle(html);
    }
}
