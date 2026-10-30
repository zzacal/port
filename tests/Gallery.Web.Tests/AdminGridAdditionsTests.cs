using Gallery.Web.Components.Molecules;
using Gallery.Web.Components.Organisms;
using Microsoft.AspNetCore.Components;

namespace Gallery.Web.Tests;

/// <summary>Optional admin additions to the public Tile and ContactSheet (feature 005, FR-002).</summary>
public class AdminGridAdditionsTests
{
    private static RenderFragment Text(string s) => b => b.AddContent(0, s);
    private static RenderFragment Markup(string s) => b => b.AddMarkupContent(0, s);

    [Fact]
    public async Task Tile_with_overlay_renders_overlay_inside_the_cell()
    {
        var html = await Render.Html<Tile>(new { Image = Render.Samples.Landscape[0], Href = "#", Sizes = "320px", Overlay = Markup("<b class=\"ctl\">x</b>") });
        Assert.Matches("<div class=\"cell\"[^>]*>.*<div class=\"overlay\"[^>]*><b class=\"ctl\">x</b></div>\\s*</div>", html.Replace("\n", ""));
    }

    [Fact]
    public async Task Tile_without_href_is_a_div_with_no_link()
    {
        var html = await Render.Html<Tile>(new { Image = Render.Samples.Landscape[0], Sizes = "320px" });
        Assert.StartsWith("<div class=\"tile\"", html.TrimStart());
        Assert.DoesNotContain("href=", html);
        Assert.DoesNotContain("<a ", html);
    }

    [Fact]
    public async Task Tile_css_class_is_appended_and_absent_when_unset()
    {
        var with = await Render.Html<Tile>(new { Image = Render.Samples.Landscape[0], Href = "#", Sizes = "320px", CssClass = "is-selected" });
        Assert.Contains("class=\"tile is-selected\"", with);

        var without = await Render.Html<Tile>(new { Image = Render.Samples.Landscape[0], Href = "#", Sizes = "320px" });
        Assert.Contains("class=\"tile\"", without);
        Assert.DoesNotContain("overlay", without);
    }

    [Fact]
    public async Task Tile_caption_follows_the_title()
    {
        var html = await Render.Html<Tile>(new { Image = Render.Samples.Landscape[0], Href = "#", Sizes = "320px", Caption = Markup("<span class=\"cap\">Private</span>") });
        Assert.True(html.IndexOf("class=\"title\"", StringComparison.Ordinal) < html.IndexOf("<span class=\"cap\">Private</span>", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ContactSheet_child_content_replaces_items_inside_the_grid()
    {
        var html = await Render.Html<ContactSheet>(new { Large = 4, Medium = 3, Small = 1, ChildContent = Markup("<p class=\"custom\">cells</p>") });
        Assert.Matches("<div class=\"cs cs-l4 cs-m3 cs-s1\"[^>]*>\\s*<p class=\"custom\">cells</p>\\s*</div>", html);
        Assert.DoesNotContain("class=\"tile", html);
    }
}
