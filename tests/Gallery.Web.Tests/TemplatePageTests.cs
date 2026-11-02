using Gallery.Core.Design;
using Gallery.Web.Ui.Pages;

namespace Gallery.Web.Tests;

public class TemplatePageTests
{
    [Fact]
    public async Task PublicLayout_inlines_one_stylesheet_and_ships_no_script()
    {
        var html = await Render.Html<PublicLayoutPage>();
        Assert.Equal(1, Guards.Count(html, "<style"));
        Assert.Contains("--space-l:48px", html);
        Assert.Matches("<meta name=\"robots\" content=\"noindex\"[^>]*>", html);
        Assert.DoesNotContain("<link rel=\"stylesheet\"", html);
        Assert.Contains("Empty page body", html);
        Assert.Matches("class=\"site-header\"", html);
        Guards.AssertNoInlineStyle(html);
        Guards.AssertNoScript(html);
    }

    [Fact]
    public async Task Tokens_page_lists_every_token()
    {
        var html = await Render.Html<TokensPage>();
        foreach (var (_, name, _) in Tokens.All())
            Assert.Contains(name, html);
        Guards.AssertNoInlineStyle(html);
        Guards.AssertNoScript(html);
    }

    [Fact]
    public async Task SiteHeader_page_renders_the_breadcrumb_trails()
    {
        var html = await Render.Html<SiteHeaderPage>();
        Assert.Contains("Galleries", html);
        Assert.Contains("Harbour at dusk", html);
        Guards.AssertNoInlineStyle(html);
        Guards.AssertNoScript(html);
    }
}
