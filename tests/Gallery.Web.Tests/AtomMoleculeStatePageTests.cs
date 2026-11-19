using Gallery.Web.Ui.Pages;

namespace Gallery.Web.Tests;

/// <summary>State-label tests for the feature 005 atom and molecule /ui pages: one test per page asserting every
/// contract state label appears (feature 005, T027/T029).</summary>
public class AtomMoleculeStatePageTests
{
    // ── ButtonPage ────────────────────────────────────────────────────────────

    [Fact]
    public async Task ButtonPage_contains_all_state_labels()
    {
        var html = await Render.Html<ButtonPage>();
        Guards.AssertNoInlineStyle(html);
        Guards.AssertNoScript(html);
        Assert.Contains("Variants", html);
        Assert.Contains("Link as button in a row", html);
        Assert.Contains("Disabled", html);
        Assert.Contains("Busy", html);
    }

    // ── InputPage ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task InputPage_contains_all_state_labels()
    {
        var html = await Render.Html<InputPage>();
        Guards.AssertNoInlineStyle(html);
        Guards.AssertNoScript(html);
        Assert.Contains("Single-line, empty", html);
        Assert.Contains("Single-line, filled", html);
        Assert.Contains("Too long", html);
        Assert.Contains("Multi-line, empty", html);
        Assert.Contains("Saving", html);
        Assert.Contains("Saved", html);
    }

    // ── SwitchPage ────────────────────────────────────────────────────────────

    [Fact]
    public async Task SwitchPage_contains_all_state_labels()
    {
        var html = await Render.Html<SwitchPage>();
        Guards.AssertNoInlineStyle(html);
        Guards.AssertNoScript(html);
        Assert.Contains("Off", html);
        Assert.Contains("On", html);
        Assert.Contains("Disabled", html);
    }

    // ── SelectMarkPage ────────────────────────────────────────────────────────

    [Fact]
    public async Task SelectMarkPage_contains_all_state_labels()
    {
        var html = await Render.Html<SelectMarkPage>();
        Guards.AssertNoInlineStyle(html);
        Guards.AssertNoScript(html);
        Assert.Contains("Not selected", html);
        Assert.Contains("Selected", html);
    }

    // ── DangerConfirmPage ─────────────────────────────────────────────────────

    [Fact]
    public async Task DangerConfirmPage_contains_all_state_labels()
    {
        var html = await Render.Html<DangerConfirmPage>();
        Guards.AssertNoInlineStyle(html);
        Guards.AssertNoScript(html);
        Assert.Contains("Delete photo, closed", html);
        Assert.Contains("Delete photo, open", html);
        Assert.Contains("Delete gallery, open", html);
        Assert.Contains("Bulk delete (opened from the selection bar), open", html);
        Assert.Contains("Discard batch, open", html);
        // Every specimen is a modal dialog with a Cancel; the open ones render open, the closed one does not.
        Assert.Equal(5, Guards.Count(html, "class=\"danger-dialog\""));
        Assert.Equal(4, System.Text.RegularExpressions.Regex.Matches(html, "<dialog[^>]*class=\"danger-dialog\"[^>]* open[ >]").Count);
        Assert.Equal(5, System.Text.RegularExpressions.Regex.Matches(html, ">\\s*Cancel\\s*</button>").Count);
        Assert.DoesNotContain("<details", html);
    }
}
