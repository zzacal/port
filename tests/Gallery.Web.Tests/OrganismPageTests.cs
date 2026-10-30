using System.Text.RegularExpressions;
using Gallery.Core.Images;
using Gallery.Web.Components.Organisms;
using Gallery.Web.Ui.Pages;

namespace Gallery.Web.Tests;

public class OrganismPageTests
{
    [Fact]
    public async Task ContactSheet_page_shows_home_gallery_and_short_row_configurations()
    {
        var html = await Render.Html<ContactSheetPage>();
        Assert.Contains("cs cs-l3 cs-m3 cs-s1", html);
        Assert.Equal(2, Guards.Count(html, "cs cs-l4 cs-m3 cs-s1"));
        Assert.Contains("Fewer than one row", html);
        Guards.AssertNoInlineStyle(html);
        Guards.AssertNoScript(html);
    }

    [Fact]
    public async Task ContactSheet_gives_first_row_high_priority_and_every_tile_matching_sizes()
    {
        var items = Render.Samples.All.Select(i => new ContactSheetItem(i, "#")).ToList();
        var html = await Render.Html<ContactSheet>(new { Items = (IReadOnlyList<ContactSheetItem>)items, Large = 3, Medium = 3, Small = 1 });

        var mains = Regex.Matches(html, "<picture class=\"layer main\"[^>]*>.*?</picture>", RegexOptions.Singleline)
            .Select(m => m.Value).ToList();
        Assert.Equal(8, mains.Count);
        Assert.All(mains.Take(3), m => Assert.Contains("fetchpriority=\"high\"", m));
        Assert.All(mains.Skip(3), m => Assert.Contains("loading=\"lazy\"", m));

        var expected = System.Net.WebUtility.HtmlEncode(Sizes.ForGrid(3, 3, 1));
        var sizes = Regex.Matches(html, "sizes=\"([^\"]+)\"").Select(m => m.Groups[1].Value).Distinct().ToList();
        Assert.Equal([expected], sizes);
    }

    [Fact]
    public async Task ContactSheet_rejects_column_counts_outside_1_to_6()
    {
        var items = (IReadOnlyList<ContactSheetItem>)Render.Samples.All.Take(1).Select(i => new ContactSheetItem(i, "#")).ToList();
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Render.Html<ContactSheet>(new { Items = items, Large = 7, Medium = 3, Small = 1 }));
    }

    [Fact]
    public async Task SoloViewer_page_has_thumb_layer_priority_and_metadata()
    {
        var html = await Render.Html<SoloViewerPage>();
        Assert.Equal(2, Guards.Count(html, "class=\"layer thumb\""));
        Assert.Equal(2, Guards.Count(html, "fetchpriority=\"high\""));
        Assert.Contains("FUJIFILM X-T5", html);
        Assert.Contains("Canon EOS R5", html);
        Guards.AssertNoInlineStyle(html);
        Guards.AssertNoScript(html);
    }
}
