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
        { typeof(GalleryLinkPage), ["Short title", "Long title (truncates)"] },
        { typeof(GalleryNeighboursPage), ["First gallery", "Middle gallery", "Last gallery", "Long titles"] },
        { typeof(MembershipStatusPage), ["In no gallery", "In one gallery", "In three galleries", "In two, public in one"] },
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
          ["With gallery link, middle photo", "With gallery link, first photo", "With gallery link, last photo",
           "One-photo gallery", "Long gallery title (stacks on small screens)"] },
        { typeof(SiteHeaderPage), ["Default"], ["Public site (brand only)"] },
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
    public void Catalogue_lists_the_three_new_pages_at_the_end_of_the_molecules()
    {
        var molecules = UiCatalog.Entries.Where(e => e.Level == UiLevel.Molecules).TakeLast(3)
            .Select(e => (e.Name, e.Slug, e.PageType)).ToArray();
        Assert.Equal(
            [
                ("GalleryLink", "molecules/gallery-link", typeof(GalleryLinkPage)),
                ("GalleryNeighbours", "molecules/gallery-neighbours", typeof(GalleryNeighboursPage)),
                ("MembershipStatus", "molecules/membership-status", typeof(MembershipStatusPage)),
            ],
            molecules);
    }

    [Fact]
    public async Task Pager_specimens_show_the_gallery_link_and_the_long_title_stacks()
    {
        var html = WebUtility.HtmlDecode(await Page(typeof(PagerPage)));
        Assert.Equal(5, Guards.Count(html, "<div class=\"here\""));
        Assert.Equal(1, Guards.Count(html, "class=\"pager pager-stack\""));
        Assert.Equal(0, Guards.Count(html, "pager-stack-all"));
        Assert.Contains("← Alpine lakes and the high pass", html);
        Assert.Contains(" · 1 / 1", html);
    }

    [Fact]
    public async Task SiteHeader_brand_only_specimen_has_no_nav()
    {
        var html = await Page(typeof(SiteHeaderPage));
        Assert.Equal(2, Guards.Count(html, "<header class=\"site-header\""));
        Assert.Equal(1, Guards.Count(html, "<nav class=\"nav\""));
    }

    [Fact]
    public async Task TextLink_tap_area_specimen_uses_the_tap_class()
    {
        var html = await Page(typeof(TextLinkPage));
        Assert.Equal(1, Guards.Count(html, "<a class=\"text-link tap\""));
        Assert.Equal(1, Guards.Count(html, "<a class=\"text-link\""));
    }

    [Fact]
    public async Task MembershipStatus_page_shows_the_fixed_copy()
    {
        var html = WebUtility.HtmlDecode(await Page(typeof(MembershipStatusPage)));
        Assert.Contains("Not in a gallery", html);
        Assert.Contains(">alpine</a> +2", html);
        Assert.Contains(">Birds</a> +1 · public in 1", html);
    }

    [Fact]
    public async Task GalleryNeighbours_page_has_one_nav_per_specimen()
    {
        var html = await Page(typeof(GalleryNeighboursPage));
        Assert.Equal(4, Guards.Count(html, "aria-label=\"Galleries\""));
    }
}
