using Gallery.Core.Images;
using Gallery.Web.Components.Atoms;
using Gallery.Web.Components.Molecules;
using Gallery.Web.Components.Organisms;
using Gallery.Web.Components.Templates;

namespace Gallery.Web.Tests;

/// <summary>Tests for the optional parameters added to existing components in feature 006.</summary>
public class ComponentAdditionsTests
{
    // ── T018: Picture TransitionId ──────────────────────────────────────────

    [Fact]
    public async Task Picture_TransitionId_renders_id_on_pic_div()
    {
        var html = await Render.Html<Picture>(new
        {
            Image = Render.Samples.Landscape[0],
            Sizes = "320px",
            TransitionId = "x"
        });
        Assert.Contains("id=\"p-x\"", html);
        Assert.Contains("class=\"pic", html);
    }

    [Fact]
    public async Task Picture_no_TransitionId_renders_no_id_attribute()
    {
        var html = await Render.Html<Picture>(new
        {
            Image = Render.Samples.Landscape[0],
            Sizes = "320px"
        });
        Assert.DoesNotContain("id=", html);
    }

    // ── T018: Tile PreloadSrcSet / PreloadSizes ──────────────────────────────

    [Fact]
    public async Task Tile_Preload_gives_data_preload_attributes()
    {
        var html = await Render.Html<Tile>(new
        {
            Image = Render.Samples.Landscape[0],
            Href = "/g/abc",
            Sizes = "320px",
            PreloadSrcSet = "foo.webp 320w",
            PreloadSizes = "320px"
        });
        Assert.Contains("data-preload-srcset=\"foo.webp 320w\"", html);
        Assert.Contains("data-preload-sizes=\"320px\"", html);
    }

    // ── T018: Tile empty state ───────────────────────────────────────────────

    [Fact]
    public async Task Tile_null_Image_with_EmptyTitle_renders_tile_empty()
    {
        var html = await Render.Html<Tile>(new
        {
            Image = (ImageView?)null,
            Href = "/g/abc",
            Sizes = "320px",
            EmptyTitle = "Coming soon"
        });
        Assert.Contains("class=\"tile tile-empty\"", html);
        Assert.Contains("class=\"cell\"", html);
        Assert.Contains("Coming soon", html);
    }

    [Fact]
    public async Task Tile_null_Image_no_EmptyTitle_throws()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            Render.Html<Tile>(new
            {
                Image = (ImageView?)null,
                Href = "/g/abc",
                Sizes = "320px"
            }));
    }

    // ── T018: SoloViewer Caption ─────────────────────────────────────────────

    [Fact]
    public async Task SoloViewer_Caption_renders_p_solo_caption_after_h1()
    {
        var html = await Render.Html<SoloViewer>(new
        {
            Image = Render.Samples.Landscape[0],
            Caption = "A beautiful scene"
        });
        var h1Pos = html.IndexOf("<h1", StringComparison.Ordinal);
        var captionPos = html.IndexOf("class=\"solo-caption\"", StringComparison.Ordinal);
        Assert.True(h1Pos >= 0, "h1 should be present");
        Assert.True(captionPos > h1Pos, "solo-caption should appear after h1");
        Assert.Contains("<p class=\"solo-caption\">A beautiful scene</p>", html);
    }

    [Fact]
    public async Task SoloViewer_no_Caption_renders_no_solo_caption()
    {
        var html = await Render.Html<SoloViewer>(new
        {
            Image = Render.Samples.Landscape[0]
        });
        Assert.DoesNotContain("solo-caption", html);
    }

    // ── T018: SoloViewer PrefetchUrls ───────────────────────────────────────

    [Fact]
    public async Task SoloViewer_PrefetchUrls_gives_data_prefetch()
    {
        var html = await Render.Html<SoloViewer>(new
        {
            Image = Render.Samples.Landscape[0],
            PrefetchUrls = "https://img.test/a.webp https://img.test/b.webp"
        });
        Assert.Contains("data-prefetch=\"https://img.test/a.webp https://img.test/b.webp\"", html);
    }

    // ── T019: PublicLayout Enhance ───────────────────────────────────────────

    [Fact]
    public async Task PublicLayout_Enhance_false_has_no_script()
    {
        var html = await Render.Html<PublicLayout>(new
        {
            Title = "Test",
            Enhance = false
        });
        Guards.AssertNoScript(html);
    }

    [Fact]
    public async Task PublicLayout_Enhance_true_has_exactly_2_scripts()
    {
        var html = await Render.Html<PublicLayout>(new
        {
            Title = "Test",
            Enhance = true
        });
        Assert.Equal(2, Guards.Count(html, "<script"));
        Assert.Contains("speculationrules", html);
        Assert.Contains("\"eagerness\":\"moderate\"", html);
        Assert.Contains("saveData", html);
        Assert.DoesNotContain("unload", html);
    }

    [Fact]
    public async Task PublicLayout_HeadExtra_renders_inside_head()
    {
        Microsoft.AspNetCore.Components.RenderFragment extra = b =>
            b.AddMarkupContent(0, "<style>#p-abc{view-transition-name:img-abc}</style>");

        var html = await Render.Html<PublicLayout>(new
        {
            Title = "Test",
            HeadExtra = extra
        });
        var headClose = html.IndexOf("</head>", StringComparison.OrdinalIgnoreCase);
        var extraPos = html.IndexOf("view-transition-name:img-abc", StringComparison.Ordinal);
        Assert.True(extraPos >= 0, "HeadExtra content should be rendered");
        Assert.True(extraPos < headClose, "HeadExtra should appear before </head>");
    }
}
