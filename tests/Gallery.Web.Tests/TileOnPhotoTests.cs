using System.Text.RegularExpressions;
using Gallery.Web.Components.Molecules;
using Microsoft.AspNetCore.Components;

namespace Gallery.Web.Tests;

/// <summary>Feature 015 K3: Tile.TitleOnPhoto and Tile.Id.</summary>
public class TileOnPhotoTests
{
    private static RenderFragment Markup(string s) => b => b.AddMarkupContent(0, s);

    private static string Flat(string html) => html.Replace("\n", "").Replace("\r", "");

    /// <summary>The cell's markup: from &lt;div class="cell"&gt; to the end of its own closing tag.</summary>
    private static string Cell(string html)
    {
        var start = html.IndexOf("<div class=\"cell\"", StringComparison.Ordinal);
        Assert.True(start >= 0, "no cell");
        var depth = 0;
        foreach (Match m in Regex.Matches(html[start..], "<div\\b|</div>"))
        {
            depth += m.Value == "</div>" ? -1 : 1;
            if (depth == 0) return html.Substring(start, m.Index + m.Length);
        }
        throw new InvalidOperationException("unbalanced cell");
    }

    private static string After(string html, string cell)
        => html[(html.IndexOf(cell, StringComparison.Ordinal) + cell.Length)..];

    [Theory]
    [InlineData("#")]
    [InlineData(null)]
    public async Task Title_on_photo_is_inside_the_cell_and_not_under_it(string? href)
    {
        var image = Render.Samples.Landscape[0];
        var html = Flat(await Render.Html<Tile>(new { Image = image, Href = href, Sizes = "320px", TitleOnPhoto = true }));
        var cell = Cell(html);

        Assert.Equal(1, Guards.Count(html, "class=\"title\""));
        Assert.Equal(1, Guards.Count(cell, "<div class=\"on-photo\""));
        Assert.Matches($"<div class=\"on-photo\"[^>]*><p class=\"title\" title=\"{Regex.Escape(image.Title)}\"[^>]*>{Regex.Escape(image.Title)}</p></div>", cell);
        Assert.DoesNotContain("class=\"title\"", After(html, cell));
        // With no Overlay passed there is no empty overlay layer.
        Assert.DoesNotContain("class=\"overlay\"", html);
        // The band is inside Picture's frame, after the main picture layer.
        Assert.Matches("<div class=\"frame\"[^>]*>.*</picture><div class=\"on-photo\"", cell);
        Guards.AssertNoInlineStyle(html);
        Guards.AssertNoScript(html);
    }

    [Fact]
    public async Task Title_on_photo_follows_the_overlay_as_a_sibling_not_inside_it()
    {
        var html = Flat(await Render.Html<Tile>(new
        {
            Image = Render.Samples.Landscape[0], Sizes = "320px", TitleOnPhoto = true,
            Overlay = Markup("<div class=\"pos-tl\"><b class=\"ctl\">x</b></div>"),
        }));
        var cell = Cell(html);
        Assert.Matches("<div class=\"overlay\"[^>]*><div class=\"pos-tl\"><b class=\"ctl\">x</b></div></div><div class=\"on-photo\"[^>]*><p class=\"title\"", cell);
        var overlayAt = cell.IndexOf("<div class=\"overlay\"", StringComparison.Ordinal);
        var onPhotoAt = cell.IndexOf("<div class=\"on-photo\"", StringComparison.Ordinal);
        Assert.True(overlayAt >= 0 && onPhotoAt > overlayAt);
        Assert.Equal(1, Guards.Count(html, "class=\"overlay\""));
        Assert.Equal(1, Guards.Count(html, "class=\"on-photo\""));
    }

    [Fact]
    public async Task Caption_still_renders_after_the_cell()
    {
        var html = Flat(await Render.Html<Tile>(new
        {
            Image = Render.Samples.Landscape[0], Sizes = "320px", TitleOnPhoto = true,
            Caption = Markup("<span class=\"cap\">Private</span>"),
        }));
        var cell = Cell(html);
        Assert.DoesNotContain("class=\"cap\"", cell);
        Assert.Matches("^\\s*<span class=\"cap\">Private</span>\\s*</div>\\s*$", After(html, cell));
    }

    [Fact]
    public async Task Id_renders_on_the_root_only_when_set()
    {
        var img = Render.Samples.Landscape[0];
        var div = (await Render.Html<Tile>(new { Image = img, Sizes = "320px", Id = "t-7" })).TrimStart();
        Assert.StartsWith("<div class=\"tile\" id=\"t-7\"", div);
        Assert.Equal(1, Guards.Count(div, "id=\"t-7\""));

        var link = (await Render.Html<Tile>(new { Image = img, Href = "#", Sizes = "320px", Id = "t-8" })).TrimStart();
        Assert.StartsWith("<a class=\"tile\" href=\"#\" id=\"t-8\"", link);

        var empty = (await Render.Html<Tile>(new { EmptyTitle = "Nothing yet", Href = "#", Sizes = "320px", Id = "t-9" })).TrimStart();
        Assert.StartsWith("<a class=\"tile tile-empty\" href=\"#\" id=\"t-9\"", empty);

        var none = (await Render.Html<Tile>(new { Image = img, Sizes = "320px" })).TrimStart();
        Assert.Matches("^<div class=\"tile\"( b-[a-z0-9]+)?>", none);
    }

    [Fact]
    public async Task Defaults_equal_omitted_parameters_byte_for_byte_for_an_image_tile()
    {
        var img = Render.Samples.Landscape[0];
        var overlay = Markup("<div class=\"pos-tl\">x</div>");
        var caption = Markup("<span class=\"cap\">c</span>");

        Assert.Equal(
            await Render.Html<Tile>(new { Image = img, Href = "#", Sizes = "320px" }),
            await Render.Html<Tile>(new { Image = img, Href = "#", Sizes = "320px", TitleOnPhoto = false, Id = (string?)null }));
        Assert.Equal(
            await Render.Html<Tile>(new { Image = img, Sizes = "320px", Overlay = overlay, Caption = caption }),
            await Render.Html<Tile>(new { Image = img, Sizes = "320px", Overlay = overlay, Caption = caption, TitleOnPhoto = false, Id = (string?)null }));

        var plain = Flat(await Render.Html<Tile>(new { Image = img, Href = "#", Sizes = "320px" }));
        Assert.DoesNotContain("on-photo", plain);
        Assert.DoesNotContain(" id=\"", plain[..plain.IndexOf('>')]);
        Assert.Equal(1, Guards.Count(After(plain, Cell(plain)), "class=\"title\""));
    }

    [Fact]
    public async Task Defaults_equal_omitted_parameters_byte_for_byte_for_an_empty_tile()
    {
        var omitted = await Render.Html<Tile>(new { EmptyTitle = "Nothing yet", Href = "#", Sizes = "320px" });
        var defaults = await Render.Html<Tile>(new { EmptyTitle = "Nothing yet", Href = "#", Sizes = "320px", TitleOnPhoto = false, Id = (string?)null });
        Assert.Equal(omitted, defaults);
        Assert.DoesNotContain(" id=", omitted);
        Assert.DoesNotContain("on-photo", omitted);
    }
}
