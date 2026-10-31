using Gallery.Core.Images;
using Gallery.Web.Components.Organisms;
using Gallery.Web.Site.Pages;

namespace Gallery.Web.Tests;

/// <summary>Tests for the new page components in src/Gallery.Web/Site/Pages/ (feature 006 T020).</summary>
public class PageComponentTests
{
    private static ImageView MakeImage(string id = "img001") => new(
        Id: id,
        Title: "Test Image",
        Width: 1920,
        Height: 1080,
        Variants: [new Variant(640, $"https://img.test/{id}.avif", $"https://img.test/{id}.webp"),
                   new Variant(1280, $"https://img.test/{id}b.avif", $"https://img.test/{id}b.webp")],
        PlaceholderDataUri: "data:image/webp;base64,AAAA",
        DominantColor: "#aabbcc",
        Exif: null
    );

    // ── HomePage ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task HomePage_zero_cards_renders_layout_only()
    {
        var html = await Render.Html<HomePage>(new
        {
            Cards = (IReadOnlyList<ContactSheetItem>)[]
        });
        Assert.Contains("<body", html);
        Assert.DoesNotContain("class=\"cs ", html);
        Guards.AssertNoInlineStyle(html);
    }

    // ── GalleryPage ──────────────────────────────────────────────────────────

    [Fact]
    public async Task GalleryPage_shows_title_as_h1()
    {
        var items = new List<ContactSheetItem>
        {
            new(MakeImage("img001"), "/g/gid/slug")
        };
        var html = await Render.Html<GalleryPage>(new
        {
            Title = "September 5",
            NoIndex = false,
            Items = (IReadOnlyList<ContactSheetItem>)items
        });
        Assert.Contains("<h1", html);
        Assert.Contains("September 5", html);
        Guards.AssertNoInlineStyle(html);
    }

    [Fact]
    public async Task GalleryPage_shows_description_only_when_set()
    {
        var items = (IReadOnlyList<ContactSheetItem>)new List<ContactSheetItem>
        {
            new(MakeImage(), "/g/gid/slug")
        };
        var withDesc = await Render.Html<GalleryPage>(new
        {
            Title = "Gallery",
            Description = "A beautiful collection",
            NoIndex = false,
            Items = items
        });
        Assert.Contains("class=\"gallery-desc\"", withDesc);
        Assert.Contains("A beautiful collection", withDesc);

        var withoutDesc = await Render.Html<GalleryPage>(new
        {
            Title = "Gallery",
            NoIndex = false,
            Items = items
        });
        Assert.DoesNotContain("gallery-desc", withoutDesc);
    }

    [Fact]
    public async Task GalleryPage_uses_cs_l4_m3_s1()
    {
        var items = (IReadOnlyList<ContactSheetItem>)new List<ContactSheetItem>
        {
            new(MakeImage("a"), "/g/gid/slug"),
            new(MakeImage("b"), "/g/gid/slug2"),
        };
        var html = await Render.Html<GalleryPage>(new
        {
            Title = "Gallery",
            NoIndex = false,
            Items = items
        });
        Assert.Contains("cs-l4 cs-m3 cs-s1", html);
    }

    [Fact]
    public async Task GalleryPage_no_ContactSheet_when_no_items()
    {
        var html = await Render.Html<GalleryPage>(new
        {
            Title = "Empty",
            NoIndex = false,
            Items = (IReadOnlyList<ContactSheetItem>)[]
        });
        Assert.DoesNotContain("class=\"cs ", html);
    }

    // ── PhotoPage ─────────────────────────────────────────────────────────────

    /// <summary>
    /// The photo page owns the prefetch of its neighbours (IV.11), which only the Enhance loader
    /// performs. Found missing in the live browser check: the page rendered data-prefetch with no
    /// script to act on it. Every public page type carries the same two scripts.
    /// </summary>
    [Fact]
    public async Task Every_public_page_type_carries_the_enhancement_scripts()
    {
        var pages = new[]
        {
            await Render.Html<HomePage>(new { Cards = (IReadOnlyList<ContactSheetItem>)[] }),
            await Render.Html<GalleryPage>(new { Title = "G", NoIndex = false, Items = (IReadOnlyList<ContactSheetItem>)[] }),
            await Render.Html<PhotoPage>(new { Image = MakeImage(), GalleryTitle = "G", NoIndex = false }),
        };
        foreach (var html in pages)
        {
            Assert.Equal(2, Guards.Count(html, "<script"));
            Assert.Contains("speculationrules", html);
            Assert.Contains("rel: 'prefetch'", html);
        }
    }

    [Fact]
    public async Task Predictive_loader_is_under_30_lines()
    {
        var html = await Render.Html<HomePage>(new { Cards = (IReadOnlyList<ContactSheetItem>)[] });
        var start = html.IndexOf("<script type=\"module\"", StringComparison.Ordinal); // scoped CSS may add b-xxxx
        var end = html.IndexOf("</script>", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        var lines = html[start..end].Split('\n').Length;
        Assert.True(lines < 30, $"loader is {lines} lines (contract: under 30)");
    }

    [Fact]
    public async Task PhotoPage_title_is_photo_dash_gallery()
    {
        var html = await Render.Html<PhotoPage>(new
        {
            Image = MakeImage(),
            GalleryTitle = "September 5",
            NoIndex = false
        });
        Assert.Contains("Test Image &#x2014; September 5", html);
        Guards.AssertNoInlineStyle(html);
    }

    [Fact]
    public async Task PhotoPage_view_transition_style_has_one_rule_per_image()
    {
        var image = MakeImage("img001");
        var html = await Render.Html<PhotoPage>(new
        {
            Image = image,
            GalleryTitle = "Gallery",
            NoIndex = false
        });
        Assert.Contains("#p-img001{view-transition-name:img-img001}", html);
    }

    [Fact]
    public async Task GalleryPage_view_transition_style_has_one_rule_per_image()
    {
        var items = (IReadOnlyList<ContactSheetItem>)new List<ContactSheetItem>
        {
            new(MakeImage("img001"), "/g/gid/slug", TransitionId: "img001"),
            new(MakeImage("img002"), "/g/gid/slug2", TransitionId: "img002"),
        };
        var html = await Render.Html<GalleryPage>(new
        {
            Title = "Gallery",
            NoIndex = false,
            Items = items
        });
        Assert.Contains("#p-img001{view-transition-name:img-img001}", html);
        Assert.Contains("#p-img002{view-transition-name:img-img002}", html);
    }

    [Fact]
    public async Task Every_page_passes_AssertNoInlineStyle()
    {
        var image = MakeImage();

        var home = await Render.Html<HomePage>(new
        {
            Cards = (IReadOnlyList<ContactSheetItem>)[]
        });
        Guards.AssertNoInlineStyle(home);

        var gallery = await Render.Html<GalleryPage>(new
        {
            Title = "G",
            NoIndex = false,
            Items = (IReadOnlyList<ContactSheetItem>)[]
        });
        Guards.AssertNoInlineStyle(gallery);

        var photo = await Render.Html<PhotoPage>(new
        {
            Image = image,
            GalleryTitle = "G",
            NoIndex = false
        });
        Guards.AssertNoInlineStyle(photo);

        var notFound = await Render.Html<NotFoundPage>();
        Guards.AssertNoInlineStyle(notFound);
    }
}
