using System.Net;
using System.Text.RegularExpressions;
using Gallery.Core.Images;
using Gallery.Web.Components.Molecules;
using Gallery.Web.Components.Organisms;
using Gallery.Web.Data;
using Gallery.Web.Site;
using Gallery.Web.Site.Pages;

namespace Gallery.Web.Tests;

/// <summary>
/// 013 T015 (contracts/components.md K1, K2; routes.md "Breadcrumbs"): the camera listing and the
/// camera page rendered on their own. "No X" checks look for the element or attribute.
/// </summary>
public partial class CameraPageComponentTests
{
    [GeneratedRegex("<nav class=\"breadcrumb\"[^>]*>(.*?)</nav>", RegexOptions.Singleline)]
    private static partial Regex BreadcrumbRx();

    [GeneratedRegex("<li[^>]*>(?:<a [^>]*href=\"([^\"]*)\"[^>]*>(.*?)</a>|<span[^>]*>(.*?)</span>)</li>", RegexOptions.Singleline)]
    private static partial Regex CrumbRx();

    // A Tile with a link: <a class="tile..." href="...">…</a> (scoped CSS may add b-xxxx attributes).
    [GeneratedRegex("<a class=\"tile\"[^>]*href=\"([^\"]*)\"[^>]*>(.*?)</a>", RegexOptions.Singleline)]
    private static partial Regex TileRx();

    [GeneratedRegex("<div class=\"cs[ \"]")]
    private static partial Regex ContactSheetRx();

    [GeneratedRegex("<meta name=\"robots\"")]
    private static partial Regex RobotsMetaRx();

    [GeneratedRegex("class=\"gallery-desc\"")]
    private static partial Regex DescRx();

    [GeneratedRegex("<nav class=\"gallery-neighbours\"")]
    private static partial Regex NeighboursRx();

    private static ImageView Image(string id, string title = "Photo") => new(
        Id: id,
        Title: title,
        Width: 1600,
        Height: 1200,
        Variants: [new Variant(640, $"https://img.test/{id}.avif", $"https://img.test/{id}.webp")],
        PlaceholderDataUri: "data:image/webp;base64,AAAA",
        DominantColor: "#aabbcc",
        Exif: null);

    private static List<(string? Href, string Label)> Crumbs(string html)
    {
        var nav = BreadcrumbRx().Matches(html);
        Assert.True(nav.Count == 1, $"{nav.Count} breadcrumbs");
        return CrumbRx().Matches(nav[0].Groups[1].Value)
            .Select(m => m.Groups[1].Success
                ? ((string?)WebUtility.HtmlDecode(m.Groups[1].Value), WebUtility.HtmlDecode(m.Groups[2].Value))
                : (null, WebUtility.HtmlDecode(m.Groups[3].Value)))
            .ToList();
    }

    private static CameraCardDoc Card(string slug, string name, string imageId) => new()
    {
        Slug = slug,
        Name = name,
        ImageCount = 1,
        Cover = new CoverImageDoc
        {
            ImageId = imageId,
            Width = 1600,
            Height = 1200,
            Placeholder = new PlaceholderDoc { Preview = "data:image/webp;base64,AAAA", Color = "#112233" },
            Derivatives = [new DerivativeDoc { W = 640, Avif = $"d/{imageId}.avif", Webp = $"d/{imageId}.webp" }],
        },
    };

    // ── K1 ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Camera_listing_with_no_cards_has_no_grid_and_the_By_Camera_trail()
    {
        var html = await Render.Html<CameraListingPage>(new { Cards = (IReadOnlyList<ContactSheetItem>)[] });

        Assert.Empty(ContactSheetRx().Matches(html));
        Assert.DoesNotContain("contact-sheet", html, StringComparison.Ordinal);
        Assert.Equal([("/", "Home"), (null, "By Camera")], Crumbs(html));
        Assert.Matches("<title[^>]*>By Camera", html);
        Guards.AssertNoInlineStyle(html);
    }

    [Fact]
    public async Task Camera_listing_with_two_cards_has_two_camera_tiles_the_first_high_priority()
    {
        string Url(string key) => $"https://img.test/{key}";
        var cards = new[] { Card("canon-eos-r5", "Canon EOS R5", "img-a"), Card("nikon-z-8", "NIKON Z 8", "img-b") }
            .Select(c => ViewMapper.ToCameraCard(c, Url)).ToList();
        var html = await Render.Html<CameraListingPage>(new { Cards = (IReadOnlyList<ContactSheetItem>)cards });

        var tiles = TileRx().Matches(html);
        Assert.Equal(["/c/canon-eos-r5", "/c/nikon-z-8"], tiles.Select(m => m.Groups[1].Value).ToArray());
        Assert.Contains("fetchpriority=\"high\"", tiles[0].Groups[2].Value, StringComparison.Ordinal);
        Assert.Contains("Canon EOS R5", tiles[0].Groups[2].Value, StringComparison.Ordinal);
        Assert.Contains("cs-l3 cs-m3 cs-s1", html, StringComparison.Ordinal);
        Assert.DoesNotContain("href=\"/g/", html, StringComparison.Ordinal);
        Guards.AssertNoInlineStyle(html);
    }

    // ── K2 ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Camera_page_has_camera_photo_tiles_with_transitions_and_no_gallery_furniture()
    {
        var items = new[] { "i1", "i2" }.Select(id => new ContactSheetItem(
            Image: Image(id, $"Title {id}"),
            Href: $"/c/canon-eos-r5/{id}",
            Title: $"Title {id}",
            TransitionId: id,
            PreloadSrcSet: SrcSet.Avif(Image(id)),
            PreloadSizes: "100vw")).ToList();

        var html = await Render.Html<CameraPage>(new
        {
            Title = "Canon EOS R5",
            Items = (IReadOnlyList<ContactSheetItem>)items,
            Crumbs = Trails.UnderCameras(new Crumb("Canon EOS R5")),
        });

        Assert.Equal(["/c/canon-eos-r5/i1", "/c/canon-eos-r5/i2"], TileRx().Matches(html).Select(m => m.Groups[1].Value).ToArray());
        foreach (var id in new[] { "i1", "i2" })
        {
            Assert.Contains($"id=\"p-{id}\"", html, StringComparison.Ordinal);
            Assert.Contains($"#p-{id}{{view-transition-name:img-{id}}}", html, StringComparison.Ordinal);
        }
        Assert.Contains("cs-l4 cs-m3 cs-s1", html, StringComparison.Ordinal);
        Assert.Empty(DescRx().Matches(html));
        Assert.Empty(NeighboursRx().Matches(html));
        Assert.Empty(RobotsMetaRx().Matches(html));
        Assert.Equal([("/", "Home"), ("/c", "By Camera"), (null, "Canon EOS R5")], Crumbs(html));
        Assert.DoesNotContain("href=\"/g/", html, StringComparison.Ordinal);
        Guards.AssertNoInlineStyle(html);
    }
}
