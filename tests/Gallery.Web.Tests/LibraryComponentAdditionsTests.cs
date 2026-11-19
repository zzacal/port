using System.Net;
using System.Text.RegularExpressions;
using Gallery.Core.Images;
using Gallery.Web.Components;
using Gallery.Web.Components.Atoms;
using Gallery.Web.Components.Molecules;
using Gallery.Web.Components.Organisms;

namespace Gallery.Web.Tests;

/// <summary>
/// 009 T030 (contracts/components.md K1, K2, K5; UI-5, UI-7). Each addition is an optional
/// parameter whose default render is byte-identical to the render with the parameter omitted, and
/// each set parameter renders its attribute. Bound controls are tested in the FALSE case for the
/// ABSENCE of the attribute (no <c>checked</c>, no <c>checked="False"</c>). Assertions match
/// attributes by regex on HTML-decoded output, since scoped components add a <c>b-xxxx</c>
/// attribute and void elements self-close.
/// </summary>
public class LibraryComponentAdditionsTests
{
    private static string D(string html) => WebUtility.HtmlDecode(html);

    /// <summary>The first element of <paramref name="tag"/> whose attributes contain <paramref name="has"/>.</summary>
    private static string Tag(string html, string tag, string has = "")
    {
        var m = Regex.Match(html, $"<{tag}\\b[^>]*{Regex.Escape(has)}[^>]*>");
        Assert.True(m.Success, $"no <{tag}> with '{has}' in: {html}");
        return m.Value;
    }

    private static bool HasAttr(string tag, string name) => Regex.IsMatch(tag, $"\\s{Regex.Escape(name)}(=|\\s|/?>)");

    // ── K1 Switch.FormId / Value ─────────────────────────────────────────────

    [Fact]
    public async Task Switch_defaults_render_byte_identical_to_omitted_parameters()
    {
        var omitted = await Render.Html<Switch>(new { Label = "Public", Name = "public", Checked = true, Hint = "Shown on the home page" });
        var defaults = await Render.Html<Switch>(new Dictionary<string, object?>
        {
            ["Label"] = "Public", ["Name"] = "public", ["Checked"] = true, ["Hint"] = "Shown on the home page",
            ["FormId"] = null, ["Value"] = null,
        });
        Assert.Equal(omitted, defaults);
        var input = Tag(D(omitted), "input", "role=\"switch\"");
        Assert.False(HasAttr(input, "form"), input);
        Assert.False(HasAttr(input, "value"), input);
    }

    [Fact]
    public async Task Switch_FormId_and_Value_render_on_the_input()
    {
        var html = D(await Render.Html<Switch>(new { Label = "Public", Name = "public", FormId = "gal-vis-x", Value = "on" }));
        var input = Tag(html, "input", "role=\"switch\"");
        Assert.Matches("\\sform=\"gal-vis-x\"", input);
        Assert.Matches("\\svalue=\"on\"", input);
    }

    [Fact]
    public async Task Switch_Checked_false_renders_no_checked_attribute_at_all()
    {
        var html = D(await Render.Html<Switch>(new { Label = "Public", Name = "public", Checked = false, FormId = "f", Value = "on" }));
        var input = Tag(html, "input", "role=\"switch\"");
        Assert.False(HasAttr(input, "checked"), input);
        Assert.DoesNotContain("checked=\"False\"", html, StringComparison.OrdinalIgnoreCase);

        var on = Tag(D(await Render.Html<Switch>(new { Label = "Public", Name = "public", Checked = true, FormId = "f", Value = "on" })),
            "input", "role=\"switch\"");
        Assert.True(HasAttr(on, "checked"), on);
        Assert.DoesNotContain("checked=\"True\"", on, StringComparison.OrdinalIgnoreCase);
    }

    // ── K2 Input.Required / Pattern ──────────────────────────────────────────

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Input_defaults_render_byte_identical_to_omitted_parameters(bool multiline)
    {
        var omitted = await Render.Html<Input>(new { Label = "Title", Name = "title", Value = "Hello", MaxLength = 200, Multiline = multiline });
        var defaults = await Render.Html<Input>(new Dictionary<string, object?>
        {
            ["Label"] = "Title", ["Name"] = "title", ["Value"] = "Hello", ["MaxLength"] = 200, ["Multiline"] = multiline,
            ["Required"] = false, ["Pattern"] = null,
        });
        Assert.Equal(omitted, defaults);
    }

    [Fact]
    public async Task Input_Required_false_renders_no_required_attribute_at_all()
    {
        var html = D(await Render.Html<Input>(new { Label = "Title", Name = "title", Required = false }));
        var input = Tag(html, "input", "name=\"title\"");
        Assert.False(HasAttr(input, "required"), input);
        Assert.False(HasAttr(input, "pattern"), input);
        Assert.DoesNotContain("required=\"False\"", html, StringComparison.OrdinalIgnoreCase);

        var area = Tag(D(await Render.Html<Input>(new { Label = "Caption", Name = "caption", Multiline = true, Required = false })),
            "textarea", "name=\"caption\"");
        Assert.False(HasAttr(area, "required"), area);
    }

    [Fact]
    public async Task Input_Required_and_Pattern_render_on_the_input_and_required_on_the_textarea()
    {
        var html = D(await Render.Html<Input>(new { Label = "Title", Name = "title", Required = true, Pattern = ".*\\S.*", MaxLength = 50 }));
        var input = Tag(html, "input", "name=\"title\"");
        Assert.True(HasAttr(input, "required"), input);
        Assert.DoesNotContain("required=\"True\"", input, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("pattern=\".*\\S.*\"", input);
        Assert.Contains("maxlength=\"50\"", input);

        var area = Tag(D(await Render.Html<Input>(new { Label = "Caption", Name = "caption", Multiline = true, Required = true })),
            "textarea", "name=\"caption\"");
        Assert.True(HasAttr(area, "required"), area);
    }
}
