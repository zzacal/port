using Gallery.Web.Ui;
using Gallery.Web.Ui.Pages;

namespace Gallery.Web.Tests;

public class IndexTests
{
    [Fact]
    public async Task Lists_every_catalog_entry_in_order_grouped_by_level()
    {
        var html = await Render.Html<Gallery.Web.Ui.Pages.Index>();

        var hrefs = System.Text.RegularExpressions.Regex.Matches(html, "href=\"/ui/([^\"]+)\"")
            .Select(m => m.Groups[1].Value).ToList();
        Assert.Equal(UiCatalog.Entries.Select(e => e.Slug), hrefs);
        Assert.Equal(31, hrefs.Count); // 14 from feature 003 + 17 from 005, one list (owner: no admin split)

        foreach (var heading in new[] { "Tokens", "Atoms", "Molecules", "Organisms", "Templates" })
            Assert.Matches($"<h2[^>]*>{heading}</h2>", html);
        Assert.Equal(5, System.Text.RegularExpressions.Regex.Matches(html, "<h2[^>]*>").Count);
        Assert.DoesNotContain("/ui/admin/", html);

        Assert.Matches("<meta name=\"robots\" content=\"noindex\"[^>]*>", html);
        Guards.AssertNoInlineStyle(html);
        Guards.AssertNoScript(html);
    }
}
