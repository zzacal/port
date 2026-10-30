using Gallery.Web.Components;
using Gallery.Web.Components.Atoms;
using Microsoft.AspNetCore.Components;

namespace Gallery.Web.Tests;

/// <summary>Tests for the feature 005 atoms: Button, Input, Switch, SelectMark (feature 005, T009).</summary>
public class AtomComponentTests
{
    private static RenderFragment Frag(string text) => b => b.AddContent(0, text);

    // ── Button ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Button_primary_has_btn_primary_class()
    {
        var html = await Render.Html<Button>(new { Variant = ButtonVariant.Primary, ChildContent = Frag("Go") });
        Assert.Contains("class=\"btn btn-primary\"", html);
    }

    [Fact]
    public async Task Button_secondary_has_btn_secondary_class()
    {
        var html = await Render.Html<Button>(new { Variant = ButtonVariant.Secondary, ChildContent = Frag("Go") });
        Assert.Contains("class=\"btn btn-secondary\"", html);
    }

    [Fact]
    public async Task Button_quiet_has_btn_quiet_class()
    {
        var html = await Render.Html<Button>(new { Variant = ButtonVariant.Quiet, ChildContent = Frag("Go") });
        Assert.Contains("class=\"btn btn-quiet\"", html);
    }

    [Fact]
    public async Task Button_danger_has_btn_danger_class()
    {
        var html = await Render.Html<Button>(new { Variant = ButtonVariant.Danger, ChildContent = Frag("Go") });
        Assert.Contains("class=\"btn btn-danger\"", html);
    }

    [Fact]
    public async Task Button_link_element_renders_anchor_with_btn_class()
    {
        var html = await Render.Html<Button>(new { Element = ButtonElement.Link, Variant = ButtonVariant.Secondary, Href = "/go", ChildContent = Frag("View gallery") });
        Assert.StartsWith("<a class=\"btn", html.TrimStart());
    }

    [Fact]
    public async Task Button_summary_element_renders_summary_with_btn_class()
    {
        var html = await Render.Html<Button>(new { Element = ButtonElement.Summary, Variant = ButtonVariant.Danger, ChildContent = Frag("Delete…") });
        Assert.StartsWith("<summary class=\"btn", html.TrimStart());
    }

    [Fact]
    public async Task Button_busy_renders_aria_busy_true_disabled_and_spinner()
    {
        var html = await Render.Html<Button>(new { Variant = ButtonVariant.Primary, Busy = true, ChildContent = Frag("Saving") });
        Assert.Contains("aria-busy=\"true\"", html);
        Assert.Contains("disabled", html);
        Assert.Contains("class=\"spinner\"", html);
    }

    [Fact]
    public async Task Button_not_busy_has_no_aria_busy_and_no_disabled()
    {
        var html = await Render.Html<Button>(new { Variant = ButtonVariant.Primary, Busy = false, ChildContent = Frag("Save") });
        Assert.DoesNotContain("aria-busy", html);
        Assert.DoesNotContain("disabled", html);
    }

    // ── Input ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Input_too_long_renders_aria_invalid_true_and_error_message()
    {
        var longValue = new string('a', 212);
        var html = await Render.Html<Input>(new { Label = "Title", Name = "title", Value = longValue, MaxLength = 200 });
        Assert.Contains("aria-invalid=\"true\"", html);
        Assert.Contains("212 characters; the limit is 200.", html);
    }

    [Fact]
    public async Task Input_valid_value_has_no_aria_invalid()
    {
        var html = await Render.Html<Input>(new { Label = "Title", Name = "title", Value = "Hello", MaxLength = 200 });
        Assert.DoesNotContain("aria-invalid", html);
    }

    [Fact]
    public async Task Input_saving_state_renders_saving_message()
    {
        var html = await Render.Html<Input>(new { Label = "Title", Name = "title", State = SaveState.Saving });
        // Razor HTML-encodes the ellipsis to &#x2026;
        Assert.True(html.Contains("Saving\u2026") || html.Contains("Saving&#x2026;"), $"Expected Saving… in: {html}");
    }

    [Fact]
    public async Task Input_saved_state_renders_saved_message()
    {
        var html = await Render.Html<Input>(new { Label = "Title", Name = "title", State = SaveState.Saved });
        Assert.Contains("Saved", html);
    }

    // ── Switch ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Switch_checked_renders_checked_attribute()
    {
        var html = await Render.Html<Switch>(new { Label = "Public", Name = "public", Checked = true });
        Assert.Contains("checked", html);
    }

    [Fact]
    public async Task Switch_unchecked_has_no_checked_attribute()
    {
        var html = await Render.Html<Switch>(new { Label = "Public", Name = "public", Checked = false });
        Assert.DoesNotContain("checked", html);
    }

    [Fact]
    public async Task Switch_disabled_renders_disabled_attribute()
    {
        var html = await Render.Html<Switch>(new { Label = "Public", Name = "public", Disabled = true });
        Assert.Contains("disabled", html);
    }

    [Fact]
    public async Task Switch_not_disabled_has_no_disabled_attribute()
    {
        var html = await Render.Html<Switch>(new { Label = "Public", Name = "public", Disabled = false });
        Assert.DoesNotContain("disabled", html);
    }

    [Fact]
    public async Task Switch_has_role_switch()
    {
        var html = await Render.Html<Switch>(new { Label = "Public", Name = "public" });
        Assert.Contains("role=\"switch\"", html);
    }

    // ── SelectMark ───────────────────────────────────────────────────────────

    [Fact]
    public async Task SelectMark_checked_renders_checked_attribute()
    {
        var html = await Render.Html<SelectMark>(new { Label = "Select Mountain Lake", Checked = true });
        Assert.Contains("checked", html);
    }

    [Fact]
    public async Task SelectMark_unchecked_has_no_checked_attribute()
    {
        var html = await Render.Html<SelectMark>(new { Label = "Select Mountain Lake", Checked = false });
        Assert.DoesNotContain("checked", html);
    }

    [Fact]
    public async Task SelectMark_label_text_is_in_vh_span()
    {
        var html = await Render.Html<SelectMark>(new { Label = "Select Mountain Lake" });
        // The vh span has a CSS-isolation scoped attribute appended, so check class and content separately
        Assert.Contains("class=\"vh\"", html);
        Assert.Contains("Select Mountain Lake", html);
    }
}
