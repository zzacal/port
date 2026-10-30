using Gallery.Core.Images;
using Gallery.Web.Components.Atoms;
using Gallery.Web.Ui.Pages;

namespace Gallery.Web.Tests;

public class AtomPageTests
{
    [Fact]
    public async Task Picture_page_shows_every_state_with_both_formats_and_dimensions()
    {
        var html = await Render.Html<PicturePage>();

        Assert.Equal(5, Guards.Count(html, "<picture"));
        Assert.Equal(5, Guards.Count(html, "type=\"image/avif\""));
        Assert.Equal(5, Guards.Count(html, "type=\"image/webp\""));
        foreach (var label in new[] { "Landscape", "Portrait", "Square", "Panorama", "High priority" })
            Assert.Contains(label, html);

        Assert.Equal(1, Guards.Count(html, "fetchpriority=\"high\""));
        Assert.Equal(1, Guards.Count(html, "loading=\"eager\""));
        Assert.Equal(4, Guards.Count(html, "loading=\"lazy\""));

        // placeholder layer on every picture, hidden from assistive tech
        Assert.Equal(5, Guards.Count(html, "src=\"data:image/webp;base64,"));
        Assert.Equal(5, Guards.Count(html, "aria-hidden=\"true\""));

        // every <img> carries width and height (IV.8)
        foreach (System.Text.RegularExpressions.Match img in System.Text.RegularExpressions.Regex.Matches(html, "<img[^>]*>"))
        {
            Assert.Contains("width=\"", img.Value);
            Assert.Contains("height=\"", img.Value);
        }

        Guards.AssertNoInlineStyle(html);
        Guards.AssertNoScript(html);
    }

    [Fact]
    public async Task Title_page_has_short_and_truncated_states()
    {
        var html = await Render.Html<TitlePage>();
        Assert.Contains("Short", html);
        Assert.Contains("Long, truncated", html);
        Assert.Contains("title=\"An evening walk", html);
        Guards.AssertNoInlineStyle(html);
        Guards.AssertNoScript(html);
    }

    [Fact]
    public async Task TextLink_page_renders_a_link()
    {
        var html = await Render.Html<TextLinkPage>();
        Assert.Contains("class=\"text-link\"", html);
        Guards.AssertNoInlineStyle(html);
        Guards.AssertNoScript(html);
    }

    [Fact]
    public async Task ExifField_renders_nothing_without_a_value()
    {
        Assert.Equal("", (await Render.Html<ExifField>(new { Label = "Lens", Value = (string?)null })).Trim());
        Assert.Equal("", (await Render.Html<ExifField>(new { Label = "Lens", Value = "" })).Trim());
    }

    [Fact]
    public async Task ExifField_page_shows_each_field_and_long_shutter()
    {
        var html = await Render.Html<ExifFieldPage>();
        Assert.Contains("FUJIFILM X-T5", html);
        Assert.Contains("XF33mmF1.4 R LM WR", html);
        Assert.Contains("33mm (50mm eq.)", html);
        Assert.Contains("f/2.0", html);
        Assert.Contains("1/250s", html);
        Assert.Contains("1.5s", html);
        Assert.Contains("ISO 400", html);
        Assert.Contains("No value", html);
        Guards.AssertNoInlineStyle(html);
        Guards.AssertNoScript(html);
    }
}
