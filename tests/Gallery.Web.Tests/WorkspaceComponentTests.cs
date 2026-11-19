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

    // ── K5 PhotoCell (Batch B2) ─────────────────────────────────────────────

    /// <summary>A PhotoCell holds a Tile whose Picture draws its <c>&lt;svg class="sizer"&gt;</c>; that one svg is
    /// removed before the workspace guards run, so any other svg (an icon) still fails.</summary>
    private static async Task<string> Cell(Dictionary<string, object?> parameters)
    {
        var raw = await Render.Html<PhotoCell>(parameters);
        Assert.Single(Regex.Matches(raw, "<svg class=\"sizer\""));
        AssertWorkspaceGuards(Regex.Replace(raw, "<svg class=\"sizer\"[^>]*></svg>", ""));
        return Unscoped(raw);
    }

    private static Dictionary<string, object?> CellParams(params (string Key, object? Value)[] extra)
    {
        var p = new Dictionary<string, object?>
        {
            ["Image"] = Render.Samples.Landscape[0],
            ["Caption"] = "Morning light",
            ["GalleryIds"] = (IReadOnlyList<string>)["g-1", "g-2"],
        };
        foreach (var (k, v) in extra) p[k] = v;
        return p;
    }

    private static List<string> MarkWords(string html)
        => Regex.Matches(html, "<span class=\"ws-mark\">([^<]*)</span>").Select(m => m.Groups[1].Value).ToList();

    [Fact]
    public async Task PhotoCell_plain_root_carries_the_contract_attributes()
    {
        var img = Render.Samples.Landscape[0];
        var html = await Cell(CellParams());
        var root = Tags(html, "div")[0];
        Assert.Equal("ws-cell", Attr(root, "class"));
        Assert.Equal("option", Attr(root, "role"));
        Assert.Equal("-1", Attr(root, "tabindex"));
        Assert.Equal("true", Attr(root, "draggable"));
        Assert.Equal(img.Id, Attr(root, "data-id"));
        Assert.Equal(img.Title, Attr(root, "data-title"));
        Assert.Equal("Morning light", Attr(root, "data-caption"));
        Assert.Equal("g-1 g-2", Attr(root, "data-galleries"));
        Assert.Equal(Gallery.Web.Workspace.Messages.DeleteConsequence(2), Attr(root, "data-consequence"));
        Assert.Equal("false", Attr(root, "aria-selected"));
        Assert.Empty(MarkWords(html));
        Assert.Empty(Tags(html, "a"));
        Assert.Equal(["tile"], Tags(html, "div").Where(t => Attr(t, "class")?.StartsWith("tile") == true).Select(t => Attr(t, "class")).ToList());
    }

    [Fact]
    public async Task PhotoCell_tile_has_no_link_and_is_sized_by_Sizes_ForFill()
    {
        var html = await Cell(CellParams());
        Assert.Empty(Tags(html, "a"));
        var sizes = Regex.Matches(html, "\\ssizes=\"([^\"]*)\"").Select(m => WebUtility.HtmlDecode(m.Groups[1].Value)).ToList();
        Assert.Equal(2, sizes.Count);
        Assert.Equal([Sizes.ForFill()], sizes.Distinct().ToList());
        Assert.Equal($"<p class=\"title\" title=\"{Render.Samples.Landscape[0].Title}\">{Render.Samples.Landscape[0].Title}</p>",
            Regex.Match(html, "<p class=\"title\"[^>]*>[^<]*</p>").Value);
    }

    [Fact]
    public async Task PhotoCell_overlay_is_the_select_mark_labelled_Select_title()
    {
        var img = Render.Samples.Landscape[0];
        var html = await Cell(CellParams());
        var overlay = Regex.Match(html, "<div class=\"overlay\">(.*?)</div>", RegexOptions.Singleline).Groups[1].Value;
        Assert.Equal($"Select {img.Title}", Text(overlay));
        var box = Assert.Single(Tags(overlay, "input"));
        Assert.Equal("checkbox", Attr(box, "type"));
        Assert.Equal(img.Id, Attr(box, "value"));
        Assert.False(Has(box, "checked"));
    }

    [Fact]
    public async Task PhotoCell_selected_has_is_selected_aria_selected_true_and_a_ticked_mark()
    {
        var html = await Cell(CellParams(("Selected", true)));
        var root = Tags(html, "div")[0];
        Assert.Equal("ws-cell is-selected", Attr(root, "class"));
        Assert.Equal("true", Attr(root, "aria-selected"));
        Assert.True(Has(Assert.Single(Tags(html, "input")), "checked"));
        Assert.Empty(MarkWords(html));
    }

    [Fact]
    public async Task PhotoCell_cover_shows_the_Cover_mark_on_the_photo()
    {
        var html = await Cell(CellParams(("Cover", true)));
        Assert.Equal("ws-cell", Attr(Tags(html, "div")[0], "class"));
        Assert.Equal(["Cover"], MarkWords(html));
        var overlay = Regex.Match(html, "<div class=\"overlay\">(.*?)</div>", RegexOptions.Singleline).Groups[1].Value;
        Assert.Equal(["Cover"], MarkWords(overlay));
    }

    [Fact]
    public async Task PhotoCell_new_shows_the_New_mark()
    {
        var html = await Cell(CellParams(("New", true)));
        Assert.Equal("ws-cell", Attr(Tags(html, "div")[0], "class"));
        Assert.Equal(["New"], MarkWords(html));
    }

    [Fact]
    public async Task PhotoCell_drop_position_adds_ws_drop()
    {
        var html = await Cell(CellParams(("Drop", true)));
        var root = Tags(html, "div")[0];
        Assert.Equal("ws-cell ws-drop", Attr(root, "class"));
        Assert.Equal("false", Attr(root, "aria-selected"));
        Assert.Empty(MarkWords(html));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    public async Task PhotoCell_data_consequence_follows_the_gallery_count(int count)
    {
        var ids = Enumerable.Range(1, count).Select(i => $"g-{i}").ToList();
        var html = await Cell(CellParams(("GalleryIds", (IReadOnlyList<string>)ids), ("Caption", null)));
        var root = Tags(html, "div")[0];
        Assert.Equal(Gallery.Web.Workspace.Messages.DeleteConsequence(count), Attr(root, "data-consequence"));
        Assert.Equal(string.Join(' ', ids), Attr(root, "data-galleries"));
        Assert.Equal("", Attr(root, "data-caption"));
    }

    [Theory]
    [InlineData(PicturePriority.Lazy, "lazy")]
    [InlineData(PicturePriority.High, "eager")]
    public async Task PhotoCell_passes_Priority_to_the_picture(PicturePriority priority, string loading)
    {
        var html = await Cell(CellParams(("Priority", priority)));
        var img = Tags(html, "img").Single(t => Has(t, "loading"));
        Assert.Equal(loading, Attr(img, "loading"));
    }

    // ── K9 CollectionList (Batch B2) ────────────────────────────────────────

    private static readonly FormToken ListToken = new("__RequestVerificationToken", "tok-123");

    private static readonly IReadOnlyList<CollectionGallery> ListGalleries =
    [
        new("g-1", "coast", "Coast", true, 42),
        new("g-2", "desert", "Desert", false, 9),
        new("g-3", "birds", "Birds", true, 31),
    ];

    private static Task<string> List(params (string Key, object? Value)[] extra)
    {
        var p = new Dictionary<string, object?>
        {
            ["Current"] = "all", ["Total"] = 128, ["Unplaced"] = 14, ["Galleries"] = ListGalleries, ["Csrf"] = ListToken,
        };
        foreach (var (k, v) in extra) p[k] = v;
        return Html<CollectionList>(p);
    }

    private static List<string> Links(string html) => Tags(html, "a").Select(t => Attr(t, "href")!).ToList();

    private static List<string> GalleryRows(string html) => Tags(html, "a").Where(t => Has(t, "data-gid")).ToList();

    [Fact]
    public async Task CollectionList_markup_order_and_hrefs()
    {
        var html = await List();
        Assert.Equal("ws-collections", Attr(Tags(html, "div")[0], "class"));
        Assert.Equal(["/admin", "/admin", "/admin/unplaced", "/admin/g/g-1", "/admin/g/g-2", "/admin/g/g-3", "/"], Links(html));
        Assert.Equal("Gallery admin All photos 128 Not in a gallery 14 Galleries in home page order Coast 42 Desert Private 9 Birds 31 New gallery View site ↗ Sign out",
            Text(html));
        Assert.Equal(["New gallery", "Sign out"], Buttons(html).Select(b => b.Text).ToList());
        Assert.Equal(["hidden"], Tags(html, "input").Select(t => Attr(t, "type")).ToList());
    }

    [Fact]
    public async Task CollectionList_gallery_rows_carry_gid_slug_public_and_draggable()
    {
        var rows = GalleryRows(await List());
        Assert.Equal(["g-1", "g-2", "g-3"], rows.Select(r => Attr(r, "data-gid")).ToList());
        Assert.Equal(["coast", "desert", "birds"], rows.Select(r => Attr(r, "data-slug")).ToList());
        Assert.Equal(["true", "false", "true"], rows.Select(r => Attr(r, "data-public")).ToList());
        Assert.All(rows, r => Assert.Equal("true", Attr(r, "draggable")));
        Assert.All(rows, r => Assert.Equal("ws-coll-row", Attr(r, "class")));
    }

    [Fact]
    public async Task CollectionList_private_gallery_has_the_Private_mark_and_public_ones_none()
    {
        var html = await List();
        Assert.Equal(["Private"], MarkWords(html));
        var desert = Regex.Match(html, "<a [^>]*data-gid=\"g-2\"[^>]*>(.*?)</a>", RegexOptions.Singleline).Groups[1].Value;
        Assert.Equal(["Private"], MarkWords(desert));
    }

    [Theory]
    [InlineData("all", "/admin")]
    [InlineData("unplaced", "/admin/unplaced")]
    [InlineData("g-2", "/admin/g/g-2")]
    public async Task CollectionList_current_row_has_aria_current_page(string current, string href)
    {
        var html = await List(("Current", current));
        var marked = Tags(html, "a").Where(t => Has(t, "aria-current")).ToList();
        var row = Assert.Single(marked);
        Assert.Equal("page", Attr(row, "aria-current"));
        Assert.Equal(href, Attr(row, "href"));
        Assert.NotEqual("ws-brand", Attr(row, "class"));
    }

    [Fact]
    public async Task CollectionList_drag_target_row_has_ws_over()
    {
        var rows = GalleryRows(await List(("Over", "g-3")));
        Assert.Equal(["ws-coll-row", "ws-coll-row", "ws-coll-row ws-over"], rows.Select(r => Attr(r, "class")).ToList());
    }

    [Fact]
    public async Task CollectionList_reorder_position_row_has_ws_before()
    {
        var rows = GalleryRows(await List(("Before", "g-1")));
        Assert.Equal(["ws-coll-row ws-before", "ws-coll-row", "ws-coll-row"], rows.Select(r => Attr(r, "class")).ToList());
    }

    [Fact]
    public async Task CollectionList_naming_shows_the_labelled_input_instead_of_New_gallery()
    {
        var html = await List(("Naming", true));
        Assert.Equal(["Sign out"], Buttons(html).Select(b => b.Text).ToList());
        var label = Regex.Match(html, "<label class=\"ws-naming\">(.*?)</label>", RegexOptions.Singleline);
        Assert.True(label.Success);
        Assert.Equal("New gallery name", Text(label.Groups[1].Value));
        var input = Assert.Single(Tags(label.Groups[1].Value, "input"));
        Assert.Equal("text", Attr(input, "type"));
        Assert.Equal(["text", "hidden"], Tags(html, "input").Select(t => Attr(t, "type")).ToList());
        Assert.EndsWith("Birds 31 New gallery name View site ↗ Sign out", Text(html));
    }

    [Fact]
    public async Task CollectionList_view_site_opens_a_new_tab()
    {
        var html = await List();
        var link = Assert.Single(Tags(html, "a"), t => Attr(t, "href") == "/");
        Assert.Equal("_blank", Attr(link, "target"));
        Assert.Equal("noopener", Attr(link, "rel"));
        Assert.Contains(">View site ↗</a>", html);
    }

    [Fact]
    public async Task CollectionList_sign_out_form_carries_the_token()
    {
        var html = await List();
        var form = Regex.Match(html, "(<form\\b[^>]*>)(.*?)</form>", RegexOptions.Singleline);
        Assert.True(form.Success);
        Assert.Single(Tags(html, "form"));
        Assert.Equal("post", Attr(form.Groups[1].Value, "method"));
        Assert.Equal("/admin/signout", Attr(form.Groups[1].Value, "action"));
        var hidden = Assert.Single(Tags(form.Groups[2].Value, "input"));
        Assert.Equal("hidden", Attr(hidden, "type"));
        Assert.Equal(ListToken.FieldName, Attr(hidden, "name"));
        Assert.Equal(ListToken.Value, Attr(hidden, "value"));
        var button = Assert.Single(Buttons(form.Groups[2].Value));
        Assert.Equal("Sign out", button.Text);
        Assert.Equal("submit", Attr(button.Tag, "type"));
    }

    // ── K10 UploadTray (Batch B2) ───────────────────────────────────────────

    private static readonly IReadOnlyList<UploadTrayRow> BusyRows =
    [
        new("a.jpg", UploadRowState.Added, ItemId: "i-1"),
        new("b.jpg", UploadRowState.Uploading, 40, ItemId: "i-2"),
        new("c.jpg", UploadRowState.Waiting, ItemId: "i-3"),
    ];

    private static readonly IReadOnlyList<UploadTrayRow> DoneRows =
    [
        new("a.jpg", UploadRowState.Added, ItemId: "i-1"),
        new("b.jpg", UploadRowState.Added, ItemId: "i-2"),
    ];

    private static readonly IReadOnlyList<UploadTrayRow> FailedRows =
    [
        new("a.jpg", UploadRowState.Added, ItemId: "i-1"),
        new("b.jpg", UploadRowState.Failed, Reason: "Connection lost", ItemId: "i-2"),
        new("c.jpg", UploadRowState.Interrupted, ItemId: "i-3"),
    ];

    private static (IReadOnlyList<UploadTrayRow> Rows, int Added, int Total) TrayData(TrayState state) => state switch
    {
        TrayState.Empty => ([], 0, 0),
        TrayState.Working or TrayState.Hidden => (BusyRows, 1, 3),
        TrayState.Finished => (DoneRows, 2, 2),
        _ => (FailedRows, 1, 3),
    };

    private static Task<string> Tray(TrayState state, string? destination = null, bool isPublic = false, IReadOnlyList<UploadTrayRow>? rows = null)
    {
        var (r, added, total) = TrayData(state);
        return Html<UploadTray>(new Dictionary<string, object?>
        {
            ["State"] = state, ["Destination"] = destination, ["DestinationPublic"] = isPublic,
            ["Rows"] = rows ?? r, ["Added"] = added, ["Total"] = total, ["MaxMb"] = 20, ["MaxPhotos"] = 100,
        });
    }

    private static string Heading(string html) => Text(Regex.Match(html, "<h2 class=\"ws-tray-head\">(.*?)</h2>", RegexOptions.Singleline).Groups[1].Value);

    private static List<(string Text, string Value, bool Disabled)> TrayButtons(string html)
        => Buttons(Regex.Match(html, "<div class=\"ws-tray-ctl\">(.*?)</div>", RegexOptions.Singleline).Groups[1].Value)
            .Select(b => (b.Text, Attr(b.Tag, "value")!, Has(b.Tag, "disabled"))).ToList();

    private const string CoastNotice = "Coast is public. Visitors will see these photos as soon as each one is added.";

    [Theory]
    [InlineData(TrayState.Empty)]
    [InlineData(TrayState.Working)]
    [InlineData(TrayState.Hidden)]
    [InlineData(TrayState.Finished)]
    [InlineData(TrayState.FinishedWithFailures)]
    public async Task UploadTray_public_notice_is_plain_text_in_every_state(TrayState state)
    {
        var html = await Tray(state, "Coast", isPublic: true);
        // Directly after the heading: the </h2> is immediately followed by the notice paragraph.
        var after = Regex.Match(html, "</h2>(<p class=\"ws-notice\">([^<]*)</p>)");
        Assert.True(after.Success, "the notice does not directly follow the heading");
        Assert.Equal(CoastNotice, WebUtility.HtmlDecode(after.Groups[2].Value));
        Assert.Single(Regex.Matches(html, "class=\"ws-notice\""));
        Assert.Single(Regex.Matches(html, Regex.Escape("Visitors will see these photos")));
        Assert.Empty(Tags(html, "details"));
        Assert.Empty(Tags(html, "summary"));
        Assert.Empty(Tags(html, "template"));
        Assert.DoesNotMatch("\\stitle=\"[^\"]*Visitors", html);
        Assert.DoesNotMatch("<p class=\"ws-notice\"[^>]*\\s(hidden|aria-hidden)", html);
        Assert.Contains(CoastNotice, Text(html));
    }

    [Theory]
    [InlineData(TrayState.Empty)]
    [InlineData(TrayState.Working)]
    [InlineData(TrayState.Hidden)]
    [InlineData(TrayState.Finished)]
    [InlineData(TrayState.FinishedWithFailures)]
    public async Task UploadTray_private_or_library_destination_has_no_notice(TrayState state)
    {
        Assert.Empty(Regex.Matches(await Tray(state, "Desert"), "class=\"ws-notice\""));
        Assert.Empty(Regex.Matches(await Tray(state), "class=\"ws-notice\""));
    }

    [Fact]
    public async Task UploadTray_public_destination_without_a_title_is_refused()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Tray(TrayState.Working, null, isPublic: true));
    }

    [Fact]
    public async Task UploadTray_empty_offers_the_dropzone_and_Close()
    {
        var html = await Tray(TrayState.Empty);
        Assert.Equal("ws-tray", Attr(Tags(html, "section")[0], "class"));
        Assert.Equal("Add photos to your library", Heading(html));
        Assert.Equal("Add photos to Desert", Heading(await Tray(TrayState.Empty, "Desert")));
        Assert.Equal([("Close", "close", false)], TrayButtons(html));
        Assert.Single(Regex.Matches(html, "class=\"dropzone state-idle\""));
        Assert.Contains("JPEG only · up to 20 MB each · up to 100 photos per batch", Text(html));
        Assert.Empty(Tags(html, "progress"));
        Assert.Empty(Regex.Matches(html, "class=\"ws-up\""));
    }

    [Fact]
    public async Task UploadTray_working_shows_count_progress_rows_and_disables_Done()
    {
        var html = await Tray(TrayState.Working, "Coast");
        Assert.Equal("Adding to Coast · 1 of 3", Heading(html));
        Assert.Equal("Adding to your library · 1 of 3", Heading(await Tray(TrayState.Working)));
        var bar = Tags(html, "progress").Single(t => Attr(t, "class") == "ws-tray-bar");
        Assert.Equal("1", Attr(bar, "value"));
        Assert.Equal("3", Attr(bar, "max"));
        Assert.Equal([("Hide", "hide", false), ("Done", "done", true)], TrayButtons(html));
        Assert.Equal(["i-1", "i-2", "i-3"], Tags(html, "div").Where(t => Attr(t, "class") == "ws-up").Select(t => Attr(t, "data-item")).ToList());
        Assert.Empty(Regex.Matches(html, "class=\"dropzone"));
    }

    [Theory]
    [InlineData(UploadRowState.Waiting, true)]
    [InlineData(UploadRowState.Preparing, true)]
    [InlineData(UploadRowState.Uploading, true)]
    [InlineData(UploadRowState.Adding, true)]
    [InlineData(UploadRowState.Added, false)]
    [InlineData(UploadRowState.Failed, false)]
    [InlineData(UploadRowState.Interrupted, false)]
    public async Task UploadTray_Done_is_disabled_while_any_row_is_on_its_way(UploadRowState state, bool disabled)
    {
        IReadOnlyList<UploadTrayRow> rows = [new("a.jpg", UploadRowState.Added, ItemId: "i-1"), new("b.jpg", state, ItemId: "i-2")];
        var html = await Tray(TrayState.Working, "Coast", rows: rows);
        Assert.Equal([("Done", "done", disabled)], TrayButtons(html).Where(b => b.Value == "done").ToList());
    }

    [Fact]
    public async Task UploadTray_hidden_renders_the_heading_line_only()
    {
        var html = await Tray(TrayState.Hidden, "Coast");
        Assert.Equal("Adding to Coast · 1 of 3", Heading(html));
        Assert.Equal([("Show", "show", false), ("Done", "done", true)], TrayButtons(html));
        Assert.Empty(Regex.Matches(html, "class=\"ws-up\""));
        Assert.Empty(Regex.Matches(html, "class=\"dropzone"));
        Assert.Empty(Tags(html, "progress"));
        Assert.Equal("Adding to Coast · 1 of 3 Show Done", Text(html));
    }

    [Fact]
    public async Task UploadTray_hidden_after_finishing_shows_the_finished_line()
    {
        var html = await Tray(TrayState.Hidden, "Coast", rows: DoneRows);
        Assert.Equal("1 photo added to Coast", Heading(html));
        Assert.Equal([("Show", "show", false), ("Add more", "more", false), ("Done", "done", false)], TrayButtons(html));
    }

    [Fact]
    public async Task UploadTray_finished_shows_the_count_Add_more_and_Done()
    {
        var html = await Tray(TrayState.Finished, "Coast");
        Assert.Equal("2 photos added to Coast", Heading(html));
        Assert.Equal("2 photos added to your library", Heading(await Tray(TrayState.Finished)));
        Assert.Equal([("Hide", "hide", false), ("Add more", "more", false), ("Done", "done", false)], TrayButtons(html));
        Assert.Equal(2, Regex.Matches(html, "class=\"ws-up\"").Count);
        Assert.Empty(Tags(html, "progress"));
    }

    [Fact]
    public async Task UploadTray_finished_with_failures_counts_failed_and_interrupted_rows()
    {
        var html = await Tray(TrayState.FinishedWithFailures, "Coast");
        Assert.Equal("1 photo added · 2 failed", Heading(html));
        Assert.Equal([("Hide", "hide", false), ("Add more", "more", false), ("Done", "done", false)], TrayButtons(html));
        Assert.Equal(3, Regex.Matches(html, "class=\"ws-up\"").Count);
        Assert.Equal(["Retry", "Remove", "Retry", "Remove"],
            Buttons(Regex.Match(html, "<div class=\"ws-tray-body ws-tray-rows\">(.*)</div>", RegexOptions.Singleline).Groups[1].Value).Select(b => b.Text).ToList());
    }

    [Fact]
    public void TrayState_values_in_order()
    {
        Assert.Equal(["Empty", "Working", "Hidden", "Finished", "FinishedWithFailures"], Enum.GetNames<TrayState>());
    }

    // ── T027 catalogue pages: the state labels, exact and in order (Batch B2) ─

    public static TheoryData<Type, string[]> CataloguePages() => new()
    {
        { typeof(Gallery.Web.Ui.Pages.MarkPage), ["Cover", "New", "Private"] },
        { typeof(Gallery.Web.Ui.Pages.PhotoCellPage), ["Plain", "Selected", "Cover", "New", "Drop position", "Phone width"] },
        { typeof(Gallery.Web.Ui.Pages.GalleryChecklistPage), ["Unticked", "Ticked", "Partly ticked", "Narrowed", "Offering a new gallery", "No galleries"] },
        { typeof(Gallery.Web.Ui.Pages.UploadRowPage), ["Waiting", "Preparing", "Uploading", "Adding", "Added", "Failed", "Interrupted"] },
        { typeof(Gallery.Web.Ui.Pages.ToastPage), ["Result", "Refusal"] },
        { typeof(Gallery.Web.Ui.Pages.CollectionListPage), ["Collection current", "Private gallery", "Drag target", "Reorder position", "Naming", "Phone width"] },
        { typeof(Gallery.Web.Ui.Pages.UploadTrayPage), ["Empty", "Working", "Hidden", "Finished", "Finished with failures", "Public destination", "Phone width"] },
        { typeof(Gallery.Web.Ui.Pages.PhotoStagePage), ["First", "Middle", "Last"] },
    };

    [Theory]
    [MemberData(nameof(CataloguePages))]
    public async Task Catalogue_page_shows_every_state_label_in_order(Type page, string[] labels)
    {
        var html = await Render.Html(page);
        var headings = Regex.Matches(html, "<h2 class=\"specimen-label\"[^>]*>([^<]*)</h2>")
            .Select(m => WebUtility.HtmlDecode(m.Groups[1].Value)).ToList();
        Assert.Equal(labels, headings);
        Guards.AssertNoInlineStyle(html);
        Guards.AssertNoScript(html);
    }

    [Fact]
    public async Task Catalogue_phone_width_specimens_are_390px_wide()
    {
        foreach (var page in new[] { typeof(Gallery.Web.Ui.Pages.PhotoCellPage), typeof(Gallery.Web.Ui.Pages.CollectionListPage), typeof(Gallery.Web.Ui.Pages.UploadTrayPage) })
        {
            var html = Unscoped(await Render.Html(page));
            Assert.Single(Regex.Matches(html, "class=\"specimen-body w-390\""));
        }
    }

    [Fact]
    public async Task Catalogue_appended_specimens_close_SidePanel_and_ContactSheet_pages()
    {
        static List<string> Labels(string html) => Regex.Matches(html, "<h2 class=\"specimen-label\"[^>]*>([^<]*)</h2>").Select(m => m.Groups[1].Value).ToList();
        var side = Unscoped(await Render.Html<Gallery.Web.Ui.Pages.SidePanelPage>());
        Assert.Equal("Without a save row", Labels(side)[^1]);
        Assert.Equal(5, Regex.Matches(side, "class=\"sp-save\"").Count);
        var sheet = Unscoped(await Render.Html<Gallery.Web.Ui.Pages.ContactSheetPage>());
        Assert.Equal("Workspace fill grid", Labels(sheet)[^1]);
        var last = sheet[sheet.LastIndexOf("<section class=\"specimen\"", StringComparison.Ordinal)..];
        Assert.Single(Regex.Matches(last, "class=\"cs cs-fill\""));
        Assert.Equal(8, Regex.Matches(last, "<div class=\"ws-cell[ \"]").Count);
    }
}
