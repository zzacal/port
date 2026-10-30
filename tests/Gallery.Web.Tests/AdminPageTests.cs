using Gallery.Web.Ui.Pages;

namespace Gallery.Web.Tests;

/// <summary>Tests for admin atom and molecule /ui pages: one test per page asserting every
/// contract state label appears (feature 005, T027/T029).</summary>
public class AdminPageTests
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

    // ── BatchItemPage ─────────────────────────────────────────────────────────

    [Fact]
    public async Task BatchItemPage_contains_all_state_labels()
    {
        var html = await Render.Html<BatchItemPage>();
        Guards.AssertNoInlineStyle(html);
        Guards.AssertNoScript(html);
        Assert.Contains("Queued, no preview yet", html);
        Assert.Contains("Processing", html);
        Assert.Contains("Uploading 64%", html);
        Assert.Contains("Succeeded", html);
        Assert.Contains("Failed", html);
        Assert.Contains("Interrupted", html);
        Assert.Contains("Long file name", html);
    }

    // ── CoverMarkerPage ───────────────────────────────────────────────────────

    [Fact]
    public async Task CoverMarkerPage_contains_all_state_labels()
    {
        var html = await Render.Html<CoverMarkerPage>();
        Guards.AssertNoInlineStyle(html);
        Guards.AssertNoScript(html);
        Assert.Contains("Pinned", html);
        Assert.Contains("Automatic (first photo, nothing pinned)", html);
        Assert.Contains("Unpinned", html);
    }

    // ── DragHandlePage ────────────────────────────────────────────────────────

    [Fact]
    public async Task DragHandlePage_contains_all_state_labels()
    {
        var html = await Render.Html<DragHandlePage>();
        Guards.AssertNoInlineStyle(html);
        Guards.AssertNoScript(html);
        Assert.Contains("At rest", html);
        Assert.Contains("Focused", html);
        Assert.Contains("Lifted for keyboard moving (3 of 12)", html);
    }

    // ── SelectionBarPage ──────────────────────────────────────────────────────

    [Fact]
    public async Task SelectionBarPage_contains_all_state_labels()
    {
        var html = await Render.Html<SelectionBarPage>();
        Guards.AssertNoInlineStyle(html);
        Guards.AssertNoScript(html);
        Assert.Contains("None selected (renders nothing)", html);
        Assert.Contains("One selected", html);
        Assert.Contains("Many selected (5 of 8)", html);
    }

    // ── DangerConfirmPage ─────────────────────────────────────────────────────

    [Fact]
    public async Task DangerConfirmPage_contains_all_state_labels()
    {
        var html = await Render.Html<DangerConfirmPage>();
        Guards.AssertNoInlineStyle(html);
        Guards.AssertNoScript(html);
        Assert.Contains("Delete image, closed", html);
        Assert.Contains("Delete image, open", html);
        Assert.Contains("Delete gallery, open", html);
    }

    // ── VisibilityTogglePage ──────────────────────────────────────────────────

    [Fact]
    public async Task VisibilityTogglePage_contains_all_state_labels()
    {
        var html = await Render.Html<VisibilityTogglePage>();
        Guards.AssertNoInlineStyle(html);
        Guards.AssertNoScript(html);
        Assert.Contains("Private", html);
        Assert.Contains("Public", html);
        Assert.Contains("Publish warning", html);
    }
}
