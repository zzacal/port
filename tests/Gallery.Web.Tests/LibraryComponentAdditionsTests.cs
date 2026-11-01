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

    // ── K5 SelectionBar.Render / SelectAllLabel / Limit ──────────────────────

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public async Task SelectionBar_defaults_render_byte_identical_to_omitted_parameters(int count)
    {
        var omitted = await Render.Html<SelectionBar>(new { Count = count, Total = 8 });
        var defaults = await Render.Html<SelectionBar>(new Dictionary<string, object?>
        {
            ["Count"] = count, ["Total"] = 8, ["Render"] = false, ["SelectAllLabel"] = null, ["Limit"] = null,
        });
        Assert.Equal(omitted, defaults);
        if (count > 0)
        {
            var bar = Tag(D(omitted), "div", "class=\"selection-bar\"");
            Assert.False(HasAttr(bar, "hidden"), bar);
            Assert.False(HasAttr(bar, "data-limit"), bar);
        }
    }

    [Fact]
    public async Task SelectionBar_Render_at_count_zero_renders_the_bar_hidden()
    {
        var html = D(await Render.Html<SelectionBar>(new { Count = 0, Total = 8, Render = true }));
        var bar = Tag(html, "div", "class=\"selection-bar\"");
        Assert.True(HasAttr(bar, "hidden"), bar);
        Assert.DoesNotContain("hidden=\"True\"", bar, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("0 selected", html);
    }

    [Fact]
    public async Task SelectionBar_Render_with_a_selection_has_no_hidden_attribute()
    {
        var html = D(await Render.Html<SelectionBar>(new { Count = 2, Total = 8, Render = true }));
        var bar = Tag(html, "div", "class=\"selection-bar\"");
        Assert.False(HasAttr(bar, "hidden"), bar);
        Assert.DoesNotContain("hidden=\"False\"", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SelectionBar_Limit_renders_data_limit_and_SelectAllLabel_adds_a_quiet_button_after_Clear()
    {
        var html = D(await Render.Html<SelectionBar>(new
        {
            Count = 0, Total = 8, Render = true, Limit = 20, SelectAllLabel = "Select all on this page",
        }));
        var bar = Tag(html, "div", "class=\"selection-bar\"");
        Assert.Matches("\\sdata-limit=\"20\"", bar);

        var clear = html.IndexOf(">Clear<", StringComparison.Ordinal);
        var all = html.IndexOf("Select all on this page", StringComparison.Ordinal);
        Assert.True(clear > 0 && all > clear, html);
        var button = Regex.Matches(html, "<button\\b[^>]*>").Select(m => m.Value).ToList();
        Assert.Equal(2, button.Count);
        Assert.Contains("btn btn-quiet", button[1]);
        Assert.Contains("type=\"button\"", button[1]);
    }
    // ── K3 VisibilityToggle.FormId / DisabledReason (009 T037) ───────────────

    private const string Reason = "Add a photo before making this gallery public";

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task VisibilityToggle_defaults_render_byte_identical_to_omitted_parameters(bool isPublic, bool warning)
    {
        var omitted = await Render.Html<VisibilityToggle>(new { Public = isPublic, GalleryTitle = "Coast", PhotoCount = 3, Warning = warning });
        var defaults = await Render.Html<VisibilityToggle>(new Dictionary<string, object?>
        {
            ["Public"] = isPublic, ["GalleryTitle"] = "Coast", ["PhotoCount"] = 3, ["Warning"] = warning,
            ["FormId"] = null, ["DisabledReason"] = null,
        });
        Assert.Equal(omitted, defaults);
    }

    [Fact]
    public async Task VisibilityToggle_FormId_reaches_the_switch_with_value_on_and_the_warning_has_no_buttons()
    {
        var html = D(await Render.Html<VisibilityToggle>(new { Public = false, GalleryTitle = "Coast", PhotoCount = 3, Warning = true, FormId = "gal-vis-x" }));
        var input = Tag(html, "input", "role=\"switch\"");
        Assert.Matches("\\sform=\"gal-vis-x\"", input);
        Assert.Matches("\\svalue=\"on\"", input);
        // Owner feedback: no confirm step, so no "Make public"/"Keep private" and no confirm field.
        var warning = LibraryHtml.Blocks(html, "div", "publish-warning").Single();
        Assert.DoesNotContain("<button", warning);
        Assert.DoesNotContain("Make public", html);
        Assert.DoesNotContain("Keep private", html);
        Assert.DoesNotContain("name=\"confirm\"", html);
        // Without PartOfSave there is no vis marker and no CSS-driven Save-time warning.
        Assert.DoesNotContain("name=\"vis\"", html);
        Assert.DoesNotContain("on-save", html);
    }

    [Fact]
    public async Task VisibilityToggle_PartOfSave_adds_the_vis_marker_and_a_buttonless_warning_for_a_private_gallery()
    {
        var html = D(await Render.Html<VisibilityToggle>(new { Public = false, GalleryTitle = "Coast", PhotoCount = 3, FormId = "b-panel", PartOfSave = true }));
        var input = Tag(html, "input", "role=\"switch\"");
        Assert.Matches("\\sform=\"b-panel\"", input);
        Assert.False(HasAttr(input, "checked"), input);
        Assert.Matches("<input type=\"hidden\" name=\"vis\" value=\"1\" form=\"b-panel\"", html);
        var warning = LibraryHtml.Blocks(html, "div", "publish-warning").Single();
        Assert.Contains("publish-warning on-save", warning);
        Assert.Contains("When you save, “Coast” becomes public: it will be listed on the home page with its 3 photos, and anyone can see them.", warning);
        Assert.DoesNotContain("<button", warning);

        // Checked overrides the stored state (a refused save keeps the owner's choice).
        var kept = Tag(D(await Render.Html<VisibilityToggle>(new { Public = false, Checked = (bool?)true, GalleryTitle = "Coast", PhotoCount = 3, FormId = "b-panel", PartOfSave = true })),
            "input", "role=\"switch\"");
        Assert.True(HasAttr(kept, "checked"), kept);

        // A public gallery needs no warning; an empty private one (disabled) gets none either.
        Assert.DoesNotContain("publish-warning", await Render.Html<VisibilityToggle>(new { Public = true, GalleryTitle = "Coast", PhotoCount = 3, FormId = "b-panel", PartOfSave = true }));
        var empty = await Render.Html<VisibilityToggle>(new { Public = false, GalleryTitle = "Coast", PhotoCount = 0, FormId = "b-panel", PartOfSave = true, DisabledReason = Reason });
        Assert.DoesNotContain("publish-warning", empty);
        Assert.Contains("name=\"vis\"", empty);
    }

    [Fact]
    public async Task VisibilityToggle_Public_false_and_no_DisabledReason_render_no_checked_no_disabled_and_no_reason()
    {
        var html = D(await Render.Html<VisibilityToggle>(new { Public = false, GalleryTitle = "Coast", PhotoCount = 3, FormId = "f" }));
        var input = Tag(html, "input", "role=\"switch\"");
        Assert.False(HasAttr(input, "checked"), input);
        Assert.False(HasAttr(input, "disabled"), input);
        Assert.DoesNotContain("checked=\"False\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("disabled=\"False\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Reason, html);

        var on = Tag(D(await Render.Html<VisibilityToggle>(new { Public = true, GalleryTitle = "Coast", PhotoCount = 3, FormId = "f" })),
            "input", "role=\"switch\"");
        Assert.True(HasAttr(on, "checked"), on);
        Assert.False(HasAttr(on, "disabled"), on);
    }

    [Fact]
    public async Task VisibilityToggle_DisabledReason_disables_the_switch_and_replaces_the_hint_with_the_reason()
    {
        var html = D(await Render.Html<VisibilityToggle>(new { Public = false, GalleryTitle = "Empty", PhotoCount = 0, FormId = "f", DisabledReason = Reason }));
        var input = Tag(html, "input", "role=\"switch\"");
        Assert.True(HasAttr(input, "disabled"), input);
        Assert.DoesNotContain("disabled=\"True\"", input, StringComparison.OrdinalIgnoreCase);
        Assert.False(HasAttr(input, "checked"), input);
        var hint = Regex.Match(html, "<p class=\"hint\"[^>]*>([^<]*)</p>");
        Assert.True(hint.Success, html);
        Assert.Equal(Reason, hint.Groups[1].Value);
        Assert.DoesNotContain("Private: not listed anywhere.", html);
    }

    // ── K4 CoverMarker.ImageId / UnpinFormId ─────────────────────────────────

    [Theory]
    [InlineData(CoverState.Pinned)]
    [InlineData(CoverState.Automatic)]
    [InlineData(CoverState.Unpinned)]
    public async Task CoverMarker_defaults_render_byte_identical_to_omitted_parameters(CoverState state)
    {
        var omitted = await Render.Html<CoverMarker>(new { State = state, ImageTitle = "Pier" });
        var defaults = await Render.Html<CoverMarker>(new Dictionary<string, object?>
        {
            ["State"] = state, ["ImageTitle"] = "Pier", ["ImageId"] = null, ["UnpinFormId"] = null,
        });
        Assert.Equal(omitted, defaults);
    }

    [Fact]
    public async Task CoverMarker_ImageId_makes_Make_cover_submit_the_image_and_UnpinFormId_makes_Unpin_submit_its_form()
    {
        var make = Regex.Match(D(await Render.Html<CoverMarker>(new { State = CoverState.Unpinned, ImageTitle = "Pier", FormId = "b-cover", ImageId = "img1" })),
            "<button\\b[^>]*>Make cover</button>").Value;
        Assert.Matches("\\stype=\"submit\"", make);
        Assert.Matches("\\sform=\"b-cover\"", make);
        Assert.Matches("\\sname=\"image\"", make);
        Assert.Matches("\\svalue=\"img1\"", make);

        var unpin = Regex.Match(D(await Render.Html<CoverMarker>(new { State = CoverState.Pinned, ImageTitle = "Pier", FormId = "b-cover", ImageId = "img1", UnpinFormId = "b-unpin" })),
            "<button\\b[^>]*>Unpin</button>").Value;
        Assert.Matches("\\stype=\"submit\"", unpin);
        Assert.Matches("\\sform=\"b-unpin\"", unpin);
        Assert.False(HasAttr(unpin, "name"), unpin);
        Assert.False(HasAttr(unpin, "value"), unpin);
    }

    // ── K6 GalleryBuilder ────────────────────────────────────────────────────

    private static IReadOnlyList<ImageView> Three => Render.Samples.Landscape.Take(3).ToList();

    [Theory]
    [InlineData(BuilderState.Rest)]
    [InlineData(BuilderState.PublishWarning)]
    [InlineData(BuilderState.Empty)]
    public async Task GalleryBuilder_defaults_render_byte_identical_to_omitted_parameters(BuilderState state)
    {
        var items = state == BuilderState.Empty ? (IReadOnlyList<ImageView>)[] : Three;
        var omitted = await Render.Html<GalleryBuilder>(new { Title = "Coast", Items = items, State = state, PinnedIndex = (int?)1 });
        var defaults = await Render.Html<GalleryBuilder>(new Dictionary<string, object?>
        {
            ["Title"] = "Coast", ["Items"] = items, ["State"] = state, ["PinnedIndex"] = (int?)1,
            ["ItemIds"] = null, ["RemoveFormId"] = null, ["CoverFormId"] = null, ["UnpinFormId"] = null,
            ["OrderFormId"] = null, ["PhotoHrefBase"] = null, ["PublicDisabledReason"] = null,
            ["TitleMax"] = null, ["DescriptionMax"] = null, ["Status"] = null,
        });
        Assert.Equal(omitted, defaults);
    }

    private static Task<string> K6Async(string? photoHrefBase = "/g/gid/coast", string? reason = null, bool isPublic = false) =>
        Render.Html<GalleryBuilder>(new Dictionary<string, object?>
        {
            ["Title"] = "Coast", ["Items"] = Three, ["State"] = BuilderState.Rest, ["PinnedIndex"] = (int?)1, ["Public"] = isPublic,
            ["ItemIds"] = (IReadOnlyList<string>)["id-a", "id-b", "id-c"], ["FormId"] = "b-panel",
            ["RemoveFormId"] = "b-remove", ["CoverFormId"] = "b-cover", ["UnpinFormId"] = "b-unpin", ["OrderFormId"] = "b-order",
            ["PhotoHrefBase"] = photoHrefBase, ["PublicDisabledReason"] = reason, ["TitleMax"] = 50, ["DescriptionMax"] = 70,
        });

    private static List<string> Tiles(string html) =>
        Regex.Matches(html, "<div class=\"tile[^\"]*\"[^>]*>").Select(m => LibraryHtml.Element(html, "div", m.Index)).ToList();

    [Fact]
    public async Task GalleryBuilder_K6_row_under_each_title_holds_handle_cover_remove_and_view_in_order()
    {
        var html = D(await K6Async());
        var tiles = Tiles(html);
        Assert.Equal(3, tiles.Count);
        var ids = new[] { "id-a", "id-b", "id-c" };
        for (var i = 0; i < 3; i++)
        {
            var tile = tiles[i];
            var row = LibraryHtml.Blocks(tile, "div", "b-ctl").Single();
            var cell = LibraryHtml.Blocks(tile, "div", "cell").Single();
            Assert.True(tile.IndexOf(cell, StringComparison.Ordinal) + cell.Length <= tile.IndexOf(row, StringComparison.Ordinal), "row must be under the cell");
            Assert.True(tile.IndexOf("class=\"title\"", StringComparison.Ordinal) < tile.IndexOf(row, StringComparison.Ordinal), "row must be under the title");
            Assert.DoesNotContain("pos-", tile);
            Assert.DoesNotContain("class=\"overlay\"", tile);

            var handle = row.IndexOf("class=\"handle\"", StringComparison.Ordinal);
            var cover = row.IndexOf("class=\"cover-marker\"", StringComparison.Ordinal);
            var remove = Regex.Match(row, "<button\\b[^>]*>Remove</button>");
            var view = Regex.Match(row, "<a class=\"text-link\" href=\"([^\"]*)\"[^>]*>View ↗</a>");
            Assert.True(handle >= 0 && cover > handle && remove.Success && remove.Index > cover && view.Success && view.Index > remove.Index, row);

            Assert.Contains("btn btn-quiet", remove.Value);
            Assert.Matches("\\stype=\"submit\"", remove.Value);
            Assert.Matches("\\sform=\"b-remove\"", remove.Value);
            Assert.Matches("\\sname=\"image\"", remove.Value);
            Assert.Matches($"\\svalue=\"{ids[i]}\"", remove.Value);
            Assert.Equal($"/g/gid/coast/{ids[i]}", view.Groups[1].Value);
        }
        // K4 through K6: the pinned tile unpins on b-unpin, the others pin their own id on b-cover.
        Assert.Matches("<button\\b[^>]*form=\"b-unpin\"[^>]*>Unpin</button>", tiles[1]);
        Assert.Matches("<button\\b[^>]*form=\"b-cover\"[^>]*name=\"image\"[^>]*value=\"id-a\"[^>]*>Make cover</button>", tiles[0]);
        Assert.Matches("<button\\b[^>]*form=\"b-cover\"[^>]*name=\"image\"[^>]*value=\"id-c\"[^>]*>Make cover</button>", tiles[2]);
        // The grid sits in <reorder-grid data-form="b-order">.
        var open = html.IndexOf("<reorder-grid data-form=\"b-order\">", StringComparison.Ordinal);
        Assert.True(open >= 0 && open < html.IndexOf("class=\"cs ", StringComparison.Ordinal) && html.IndexOf("</reorder-grid>", StringComparison.Ordinal) > open, html);
    }

    [Fact]
    public async Task GalleryBuilder_K6_without_PhotoHrefBase_has_no_View_link_and_no_reorder_wrapper_without_OrderFormId()
    {
        var html = D(await K6Async(photoHrefBase: null));
        Assert.DoesNotContain("View ↗", html);
        Assert.DoesNotContain("/g/", html.Replace("View gallery", ""));
        var plain = D(await Render.Html<GalleryBuilder>(new { Title = "Coast", Items = Three }));
        Assert.DoesNotContain("reorder-grid", plain);
        Assert.DoesNotContain(">Remove<", plain);
    }

    [Fact]
    public async Task GalleryBuilder_K6_inputs_get_required_pattern_and_the_limits_and_the_toggle_gets_K3()
    {
        var html = D(await K6Async(reason: Reason));
        var title = Tag(html, "input", "name=\"title\"");
        Assert.True(HasAttr(title, "required"), title);
        Assert.Contains("pattern=\".*\\S.*\"", title);
        Assert.Contains("maxlength=\"50\"", title);
        var desc = Tag(html, "textarea", "name=\"description\"");
        Assert.Contains("maxlength=\"70\"", desc);
        Assert.False(HasAttr(desc, "required"), desc);

        var sw = Tag(html, "input", "role=\"switch\"");
        Assert.True(HasAttr(sw, "disabled"), sw);
        Assert.False(HasAttr(sw, "checked"), sw);
        Assert.Contains(Reason, html);
    }

    [Fact]
    public async Task GalleryBuilder_K6_FALSE_cases_Public_false_has_no_checked_and_no_reason_has_no_disabled()
    {
        var html = D(await K6Async(reason: null, isPublic: false));
        var sw = Tag(html, "input", "role=\"switch\"");
        Assert.False(HasAttr(sw, "checked"), sw);
        Assert.False(HasAttr(sw, "disabled"), sw);
        Assert.DoesNotContain("checked=\"False\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("disabled=\"False\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Reason, html);

        var pub = Tag(D(await K6Async(reason: null, isPublic: true)), "input", "role=\"switch\"");
        Assert.True(HasAttr(pub, "checked"), pub);
        Assert.False(HasAttr(pub, "disabled"), pub);
    }
}
