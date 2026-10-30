using Gallery.Core.Images;
using Gallery.Web.Components.Molecules;
using Gallery.Web.Ui.Pages;

namespace Gallery.Web.Tests;

public class MoleculePageTests
{
    private const string Sep = "<span class=\"sep\"";

    [Fact]
    public async Task Tile_page_shows_landscape_portrait_and_long_title()
    {
        var html = await Render.Html<TilePage>();
        Assert.Equal(3, Guards.Count(html, "<a class=\"tile\""));
        Assert.Equal(3, Guards.Count(html, "<div class=\"cell\""));
        Assert.Contains("Mountain Lake", html);
        Assert.Contains("Long title", html);
        Guards.AssertNoInlineStyle(html);
        Guards.AssertNoScript(html);
    }

    [Fact]
    public async Task MetadataBar_separates_only_present_fields_and_renders_nothing_when_empty()
    {
        var full = Render.Samples.Landscape[0].Exif;
        var partial = Render.Samples.PartialExifPortrait.Exif;

        var fullHtml = await Render.Html<MetadataBar>(new { Exif = full });
        Assert.Equal(5, Guards.Count(fullHtml, Sep));

        var partialHtml = await Render.Html<MetadataBar>(new { Exif = partial });
        Assert.Contains("Canon EOS R5", partialHtml);
        Assert.Contains("ISO 800", partialHtml);
        Assert.Equal(1, Guards.Count(partialHtml, Sep));

        Assert.Equal("", (await Render.Html<MetadataBar>(new { Exif = (Exif?)null })).Trim());

        var page = await Render.Html<MetadataBarPage>();
        foreach (var label in new[] { "All fields", "Some fields", "No fields" }) Assert.Contains(label, page);
        Guards.AssertNoInlineStyle(page);
        Guards.AssertNoScript(page);
    }

    [Fact]
    public async Task Pager_omits_the_missing_direction()
    {
        var first = await Render.Html<Pager>(new { NextHref = "#" });
        Assert.DoesNotContain("Previous", first);
        Assert.Contains("Next", first);

        var last = await Render.Html<Pager>(new { PrevHref = "#" });
        Assert.Contains("Previous", last);
        Assert.DoesNotContain("Next", last);

        var middle = await Render.Html<Pager>(new { PrevHref = "#", NextHref = "#" });
        Assert.Contains("Previous", middle);
        Assert.Contains("Next", middle);

        var page = await Render.Html<PagerPage>();
        Guards.AssertNoInlineStyle(page);
        Guards.AssertNoScript(page);
    }

    [Fact]
    public async Task Brand_and_Nav_pages_render()
    {
        var brand = await Render.Html<BrandPage>();
        Assert.Contains("class=\"brand\"", brand);

        var nav = await Render.Html<NavPage>();
        Assert.Contains("aria-current=\"page\"", nav);

        foreach (var html in new[] { brand, nav })
        {
            Guards.AssertNoInlineStyle(html);
            Guards.AssertNoScript(html);
        }
    }
}
