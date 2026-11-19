using System.Net;
using System.Text.RegularExpressions;
using Gallery.Core.Images;
using Gallery.Web.Components;
using Gallery.Web.Components.Atoms;
using Gallery.Web.Components.Molecules;
using Gallery.Web.Components.Organisms;
using Microsoft.AspNetCore.Components;

namespace Gallery.Web.Tests;

/// <summary>Feature 020 T028 (Batch B1 part): K2 SidePanel <c>Save</c>, K3 ContactSheet <c>Fill</c> (defaults
/// byte-identical), K4 Mark, K7 UploadRow, K8 Toast, K11 PhotoStage: every state as a named fact; no
/// <c>&lt;script</c>, no <c>style=</c>, no icon-only control in any new component. K6 is in GalleryChecklistTests.</summary>
public class WorkspaceComponentTests
{
    private static RenderFragment Frag(string text) => b => b.AddContent(0, text);

    // ── helpers ─────────────────────────────────────────────────────────────

    /// <summary>Drops the CSS isolation attribute (b-xxxxxxxxxx) so assertions can match exact tags.</summary>
    internal static string Unscoped(string html) => Regex.Replace(html, "\\sb-[a-z0-9]{10}(?=[\\s/>])", "");

    internal static List<string> Tags(string html, string tag)
        => Regex.Matches(html, $"<{tag}\\b[^>]*>").Select(m => m.Value).ToList();

    internal static string? Attr(string tag, string name)
    {
        var m = Regex.Match(tag, $"\\s{Regex.Escape(name)}(?:=\"([^\"]*)\")?(?=[\\s/>])");
        return m.Success ? WebUtility.HtmlDecode(m.Groups[1].Value) : null;
    }

    internal static bool Has(string tag, string name) => Attr(tag, name) is not null;

    /// <summary>Visible text: tags stripped, entities decoded, whitespace collapsed.</summary>
    internal static string Text(string html)
        => Regex.Replace(WebUtility.HtmlDecode(Regex.Replace(html, "<[^>]+>", " ")), "\\s+", " ").Trim();

    /// <summary>Every button as (opening tag, visible text), in document order.</summary>
    internal static List<(string Tag, string Text)> Buttons(string html)
        => Regex.Matches(html, "(<button\\b[^>]*>)(.*?)</button>", RegexOptions.Singleline)
            .Select(m => (m.Groups[1].Value, Text(m.Groups[2].Value))).ToList();

    /// <summary>The no-script, no-inline-style and no-icon-only guards every new component meets (FR-010).</summary>
    internal static void AssertWorkspaceGuards(string html)
    {
        Guards.AssertNoInlineStyle(html);
        Guards.AssertNoScript(html);
        foreach (Match m in Regex.Matches(html, "<(\\w+)\\b[^>]*\\saria-label=\"[^\"]*\"[^>]*>", RegexOptions.Singleline))
        {
            var tag = m.Groups[1].Value;
            var end = html.IndexOf($"</{tag}>", m.Index + m.Length, StringComparison.Ordinal);
            Assert.True(end > 0, $"unclosed {m.Value}");
            Assert.NotEqual("", Text(html[(m.Index + m.Length)..end]));
        }
        Assert.DoesNotContain("class=\"icon", html);
        Assert.DoesNotContain("<svg", html);
    }

    private static async Task<string> Html<T>(object? parameters = null) where T : IComponent
    {
        var raw = await Render.Html<T>(parameters);
        AssertWorkspaceGuards(raw);
        return Unscoped(raw);
    }

    // ── K2 SidePanel Save ───────────────────────────────────────────────────

    public static TheoryData<SaveState, bool> SidePanelVariants => new()
    {
        { SaveState.Idle, false }, { SaveState.Saving, false }, { SaveState.Saved, false }, { SaveState.Idle, true },
    };

    private static Dictionary<string, object?> PanelParams(SaveState state, bool danger) => new()
    {
        ["Heading"] = "Photo",
        ["ChildContent"] = Frag("fields"),
        ["State"] = state,
        ["Danger"] = danger ? Frag("danger") : null,
    };

    [Theory]
    [MemberData(nameof(SidePanelVariants))]
    public async Task SidePanel_default_render_is_byte_identical_to_Save_true(SaveState state, bool danger)
    {
        var dflt = await Render.Html<SidePanel>(PanelParams(state, danger));
        var p = PanelParams(state, danger);
        p["Save"] = true;
        Assert.Equal(dflt, await Render.Html<SidePanel>(p));
    }

    [Theory]
    [MemberData(nameof(SidePanelVariants))]
    public async Task SidePanel_Save_false_renders_the_default_without_its_save_row(SaveState state, bool danger)
    {
        var dflt = Unscoped(await Render.Html<SidePanel>(PanelParams(state, danger)));
        var p = PanelParams(state, danger);
        p["Save"] = false;
        var html = await Html<SidePanel>(p);

        Assert.Single(Regex.Matches(dflt, "class=\"sp-save\""));
        Assert.Empty(Regex.Matches(html, "class=\"sp-save\""));
        Assert.Empty(Regex.Matches(html, "class=\"sp-status\""));
        Assert.Empty(Buttons(html));
        var expected = Regex.Replace(dflt, "<div class=\"sp-save\">.*?</div>", "", RegexOptions.Singleline);
        Assert.Equal(Regex.Replace(expected, "\\s+", " "), Regex.Replace(html, "\\s+", " "));
    }

    // ── K3 ContactSheet Fill ────────────────────────────────────────────────

    /// <summary>ContactSheet is an existing component whose Tiles carry the Picture placeholder <c>&lt;svg</c>, so it
    /// gets the script and inline-style guards only.</summary>
    private static async Task<string> Sheet(object parameters)
    {
        var raw = await Render.Html<ContactSheet>(parameters);
        Guards.AssertNoInlineStyle(raw);
        Guards.AssertNoScript(raw);
        return Unscoped(raw);
    }

    private static IReadOnlyList<ContactSheetItem> SheetItems()
        => Render.Samples.Landscape.Take(3).Select(i => new ContactSheetItem(i, "#")).ToList();

    [Fact]
    public async Task ContactSheet_default_render_is_byte_identical_to_Fill_false()
    {
        var items = SheetItems();
        Assert.Equal(await Render.Html<ContactSheet>(new { Items = items }),
            await Render.Html<ContactSheet>(new { Items = items, Fill = false }));
        Assert.Equal(await Render.Html<ContactSheet>(new { Items = items, Large = 4, Medium = 2, Small = 2 }),
            await Render.Html<ContactSheet>(new { Items = items, Large = 4, Medium = 2, Small = 2, Fill = false }));
        Assert.Equal(await Render.Html<ContactSheet>(new { ChildContent = Frag("cells") }),
            await Render.Html<ContactSheet>(new { ChildContent = Frag("cells"), Fill = false }));
    }

    [Fact]
    public async Task ContactSheet_default_keeps_the_column_count_classes()
    {
        var html = await Sheet(new { Items = SheetItems() });
        Assert.Equal("cs cs-l3 cs-m3 cs-s1", Attr(Tags(html, "div")[0], "class"));
    }

    [Fact]
    public async Task ContactSheet_Fill_has_class_cs_cs_fill_and_no_column_count_classes()
    {
        var html = await Sheet(new { ChildContent = Frag("cells"), Fill = true, Large = 4, Medium = 2, Small = 2 });
        Assert.Equal("cs cs-fill", Attr(Tags(html, "div")[0], "class"));
        Assert.DoesNotMatch("cs-[lms]\\d", html);
    }

    [Fact]
    public async Task ContactSheet_Fill_sizes_its_tiles_with_Sizes_ForFill()
    {
        var html = await Sheet(new { Items = SheetItems(), Fill = true });
        var sizes = Regex.Matches(html, "\\ssizes=\"([^\"]*)\"").Select(m => WebUtility.HtmlDecode(m.Groups[1].Value)).ToList();
        Assert.NotEmpty(sizes);
        Assert.Equal([Sizes.ForFill()], sizes.Distinct().ToList());
    }

    [Fact]
    public void ContactSheet_css_fill_is_two_columns_below_600px_and_auto_fill_168px_from_600px()
    {
        var css = File.ReadAllText(Path.Combine(Render.RepoRoot, "src", "Gallery.Web", "Components", "Organisms", "ContactSheet.razor.css"));
        var rules = Regex.Matches(css, "\\.cs-fill\\s*\\{([^}]*)\\}").Select(m => m.Groups[1].Value.Trim()).ToList();
        Assert.Equal(["grid-template-columns: repeat(2, minmax(0, 1fr));", $"grid-template-columns: repeat(auto-fill, minmax({Sizes.FillMin}px, 1fr));"], rules);
        var medium = css.IndexOf("@media (min-width: 600px)", StringComparison.Ordinal);
        var large = css.IndexOf("@media (min-width: 1200px)", StringComparison.Ordinal);
        var first = css.IndexOf(".cs-fill", StringComparison.Ordinal);
        var second = css.IndexOf(".cs-fill", first + 1, StringComparison.Ordinal);
        Assert.True(first < medium, "two-column rule sits outside the media queries");
        Assert.True(medium < second && second < large, "auto-fill rule sits in the 600px query");
    }

    // ── K4 Mark ─────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Cover")]
    [InlineData("New")]
    [InlineData("Private")]
    public async Task Mark_renders_its_word_in_a_ws_mark_span(string text)
    {
        Assert.Equal($"<span class=\"ws-mark\">{text}</span>", await Html<Mark>(new { Text = text }));
    }

    // ── K8 Toast ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Toast_result_is_a_status_region_with_the_text()
    {
        Assert.Equal("<div class=\"ws-toast\" role=\"status\">1 photo deleted.</div>",
            await Html<Toast>(new { Text = "1 photo deleted." }));
    }

    [Fact]
    public async Task Toast_refusal_adds_ws_refusal()
    {
        Assert.Equal("<div class=\"ws-toast ws-refusal\" role=\"status\">Something went wrong.</div>",
            await Html<Toast>(new { Text = "Something went wrong.", Failed = true }));
    }

    [Fact]
    public async Task Toast_without_text_is_an_empty_status_region()
    {
        Assert.Equal("<div class=\"ws-toast\" role=\"status\"></div>", await Html<Toast>());
    }

    [Fact]
    public void Toast_css_is_fixed_at_the_bottom_centre()
    {
        var css = File.ReadAllText(Path.Combine(Render.RepoRoot, "src", "Gallery.Web", "Components", "Molecules", "Toast.razor.css"));
        var root = Regex.Match(css, "\\.ws-toast\\s*\\{([^}]*)\\}").Groups[1].Value;
        var decls = root.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        Assert.Contains("position: fixed", decls);
        Assert.Contains("left: 50%", decls);
        Assert.Contains("transform: translateX(-50%)", decls);
        Assert.Contains("bottom: var(--space-s)", decls);
    }

    // ── K7 UploadRow ────────────────────────────────────────────────────────

    private static Task<string> Row(UploadRowState state, string? reason = null, int percent = 0)
        => Html<UploadRow>(new Dictionary<string, object?>
        {
            ["FileName"] = "IMG_0042.jpg", ["State"] = state, ["Percent"] = percent, ["Reason"] = reason, ["ItemId"] = "it-1",
        });

    [Theory]
    [InlineData(UploadRowState.Waiting, "IMG_0042.jpg Waiting")]
    [InlineData(UploadRowState.Preparing, "IMG_0042.jpg Preparing")]
    [InlineData(UploadRowState.Adding, "IMG_0042.jpg Adding")]
    [InlineData(UploadRowState.Added, "IMG_0042.jpg Added")]
    public async Task UploadRow_word_states_show_the_name_and_one_word_and_no_button(UploadRowState state, string text)
    {
        var html = await Row(state);
        Assert.Equal(text, Text(html));
        Assert.Empty(Buttons(html));
        Assert.Empty(Tags(html, "progress"));
    }

    [Fact]
    public async Task UploadRow_root_is_ws_up_with_the_item_id()
    {
        var root = Tags(await Row(UploadRowState.Waiting), "div")[0];
        Assert.Equal("ws-up", Attr(root, "class"));
        Assert.Equal("it-1", Attr(root, "data-item"));
    }

    [Fact]
    public async Task UploadRow_uploading_shows_a_progress_bar_at_Percent()
    {
        var html = await Row(UploadRowState.Uploading, percent: 40);
        var bar = Assert.Single(Tags(html, "progress"));
        Assert.Equal("40", Attr(bar, "value"));
        Assert.Equal("100", Attr(bar, "max"));
        Assert.Equal("IMG_0042.jpg 40%", Text(html));
        Assert.Empty(Buttons(html));
    }

    [Fact]
    public async Task UploadRow_failed_shows_the_reason_then_Retry_and_Remove()
    {
        var html = await Row(UploadRowState.Failed, "Connection lost");
        Assert.Equal("IMG_0042.jpg Connection lost Retry Remove", Text(html));
        var buttons = Buttons(html);
        Assert.Equal(["Retry", "Remove"], buttons.Select(b => b.Text).ToList());
        Assert.Equal(["retry", "remove"], buttons.Select(b => Attr(b.Tag, "value")).ToList());
        Assert.All(buttons, b => Assert.Equal("button", Attr(b.Tag, "type")));
        Assert.All(buttons, b => Assert.StartsWith("btn ", Attr(b.Tag, "class")));
    }

    [Fact]
    public async Task UploadRow_interrupted_shows_Interrupted_then_Retry_and_Remove()
    {
        var html = await Row(UploadRowState.Interrupted);
        Assert.Equal("IMG_0042.jpg Interrupted Retry Remove", Text(html));
        Assert.Equal(["Retry", "Remove"], Buttons(html).Select(b => b.Text).ToList());
    }

    [Fact]
    public void UploadRowState_values_in_order()
    {
        Assert.Equal(["Waiting", "Preparing", "Uploading", "Adding", "Added", "Failed", "Interrupted"], Enum.GetNames<UploadRowState>());
    }

    // ── K11 PhotoStage ──────────────────────────────────────────────────────

    [Theory]
    [InlineData(1, 12, "1 of 12", false, true)]
    [InlineData(5, 12, "5 of 12", true, true)]
    [InlineData(12, 12, "12 of 12", true, false)]
    public async Task PhotoStage_first_middle_last(int position, int total, string pos, bool previous, bool next)
    {
        var html = await Html<PhotoStage>(new { Position = position, Total = total, BackLabel = "All photos" });
        Assert.Equal("ws-stage", Attr(Tags(html, "div")[0], "class"));
        var buttons = Buttons(html);
        Assert.Equal(["← All photos", "Previous", "Next"], buttons.Select(b => b.Text).ToList());
        Assert.Equal(["back", "previous", "next"], buttons.Select(b => Attr(b.Tag, "value")).ToList());
        Assert.Equal([true, previous, next], buttons.Select(b => !Has(b.Tag, "disabled")).ToList());
        Assert.Equal($"← All photos {pos} Previous Next", Text(html));
        Assert.Contains("<div class=\"ws-stage-frame\"></div>", html);
    }
}
