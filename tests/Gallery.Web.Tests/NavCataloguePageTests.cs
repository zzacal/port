using System.Net;
using System.Text.RegularExpressions;
using Gallery.Web.Ui;
using Gallery.Web.Ui.Pages;

namespace Gallery.Web.Tests;

/// <summary>Feature 010 (FR-012, T024): the navigation states are shown in /ui/ before any page uses
/// them. New pages render every contract label in order; appended pages keep their old labels
/// first and add the new ones after.</summary>
public class NavCataloguePageTests
{
    private static List<string> Labels(string html) =>
        Regex.Matches(html, "<h2 class=\"specimen-label\"[^>]*>([^<]*)</h2>")
            .Select(m => WebUtility.HtmlDecode(m.Groups[1].Value))
            .ToList();

    private static async Task<string> Page(Type type)
    {
        var html = await Render.Html(type);
        Guards.AssertNoInlineStyle(html);
        Guards.AssertNoScript(html);
        return html;
    }

    public static TheoryData<Type, string[]> NewPages => new()
    {
        { typeof(PhotoCountPage), ["Middle photo", "One-photo gallery"] },
        { typeof(GalleryNeighboursPage), ["First gallery", "Middle gallery", "Last gallery", "Long titles"] },
        { typeof(PhotoActionsPage), ["Not in a gallery", "In 1 gallery", "In 3 galleries", "No public page", "Edit active", "Long title"] },
        { typeof(BreadcrumbPage), ["Gallery page", "Photo page", "Admin gallery", "Long titles (narrow)"] },
        { typeof(PageHeadingPage), ["Title only", "With an action"] },
    };

    [Theory]
    [MemberData(nameof(NewPages))]
    public async Task New_page_renders_every_label_in_order(Type type, string[] labels)
    {
        var html = await Page(type);
        Assert.Equal(labels, Labels(html));
    }

    public static TheoryData<Type, string[], string[]> AppendedPages => new()
    {
        { typeof(PagerPage), ["First image", "Middle image", "Last image"],
          ["With count, middle photo", "With count, first photo", "With count, last photo", "One-photo gallery"] },
        { typeof(TextLinkPage), ["Normal"], ["Tap area"] },
    };

    [Theory]
    [MemberData(nameof(AppendedPages))]
    public async Task Appended_page_keeps_old_labels_then_adds_new_ones(Type type, string[] old, string[] added)
    {
        var html = await Page(type);
        Assert.Equal(old.Concat(added).ToArray(), Labels(html));
    }

    [Fact]
    public void Catalogue_lists_the_010_pages_at_the_end_of_the_molecules()
    {
        var molecules = UiCatalog.Entries.Where(e => e.Level == UiLevel.Molecules).TakeLast(5)
            .Select(e => (e.Name, e.Slug, e.PageType)).ToArray();
        Assert.Equal(
            [
                ("PhotoCount", "molecules/photo-count", typeof(PhotoCountPage)),
                ("GalleryNeighbours", "molecules/gallery-neighbours", typeof(GalleryNeighboursPage)),
                ("PhotoActions", "molecules/photo-actions", typeof(PhotoActionsPage)),
                ("Breadcrumb", "molecules/breadcrumb", typeof(BreadcrumbPage)),
                ("PageHeading", "molecules/page-heading", typeof(PageHeadingPage)),
            ],
            molecules);
    }

    [Fact]
    public async Task Pager_specimens_show_the_count_in_the_centre()
    {
        var html = WebUtility.HtmlDecode(await Page(typeof(PagerPage)));
        Assert.Equal(4, Guards.Count(html, "<div class=\"here\""));
        Assert.Equal(0, Guards.Count(html, "pager-stack"));
        Assert.Contains(">7 / 24</p>", html);
        Assert.Contains(">1 / 1</p>", html);
    }

    [Fact]
    public async Task Breadcrumb_page_marks_one_current_item_per_specimen()
    {
        var html = await Page(typeof(BreadcrumbPage));
        Assert.Equal(4, Guards.Count(html, "aria-label=\"Breadcrumb\""));
        Assert.Equal(4, Guards.Count(html, "aria-current=\"page\""));
    }

    // Owner review 2: SiteHeader's specimens are the three trails; none has a brand or a site nav.
    [Fact]
    public async Task SiteHeader_specimens_are_breadcrumb_trails()
    {
        var html = await Page(typeof(SiteHeaderPage));
        Assert.Equal(["Home", "Gallery page", "Photo page"], Labels(html));
        Assert.Equal(3, Guards.Count(html, "<header class=\"site-header\""));
        Assert.Equal(3, Guards.Count(html, "<nav class=\"breadcrumb\""));
        Assert.Equal(0, Guards.Count(html, "<nav class=\"nav\""));
        Assert.DoesNotContain("class=\"brand", html);
    }

    [Fact]
    public async Task TextLink_tap_area_specimen_uses_the_tap_class()
    {
        var html = await Page(typeof(TextLinkPage));
        Assert.Equal(1, Guards.Count(html, "<a class=\"text-link tap\""));
        Assert.Equal(1, Guards.Count(html, "<a class=\"text-link\""));
    }

    [Fact]
    public async Task PhotoActions_page_shows_the_fixed_copy()
    {
        var html = WebUtility.HtmlDecode(await Page(typeof(PhotoActionsPage)));
        Assert.Contains("aria-label=\"Not in a gallery\"", html);
        Assert.Contains("aria-label=\"In 1 gallery\"", html);
        Assert.Contains("aria-label=\"In 3 galleries\"", html);
    }

    [Fact]
    public async Task GalleryNeighbours_page_has_one_nav_per_specimen()
    {
        var html = await Page(typeof(GalleryNeighboursPage));
        Assert.Equal(4, Guards.Count(html, "aria-label=\"Galleries\""));
    }
}
