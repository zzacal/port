using System.Net;
using System.Text.RegularExpressions;
using Gallery.Core.Images;
using Gallery.Web.Components;
using Gallery.Web.Components.Atoms;
using Gallery.Web.Components.Molecules;
using Gallery.Web.Components.Organisms;
using Gallery.Web.Ui.Pages;

namespace Gallery.Web.Tests;

/// <summary>
/// Feature 015 K6 (T022): the DragHandle and CoverMarker <c>Icon</c> modes, the GalleryBuilder icon
/// row with the title on the photo, and the K8 specimens appended to their catalogue pages. Every
/// assertion reads elements and attributes, never bare substrings.
/// </summary>
public class BuilderIconTests
{
    private static string D(string html) => WebUtility.HtmlDecode(html);

    /// <summary>The value of attribute <paramref name="name"/> on <paramref name="tag"/>; "" for a bare attribute, null when absent.</summary>
    private static string? Attr(string tag, string name)
    {
        var m = Regex.Match(tag, $"\\s{Regex.Escape(name)}(=\"([^\"]*)\")?(?=[\\s/>])");
        return !m.Success ? null : m.Groups[1].Success ? m.Groups[2].Value : "";
    }

    /// <summary>The opening tags of the top-level child elements of <paramref name="element"/> (its first tag is the root).</summary>
    private static List<string> Children(string element)
    {
        var tags = Regex.Matches(element, "<(/?)([a-zA-Z][a-zA-Z0-9-]*)\\b[^>]*?(/?)>").ToList();
        var children = new List<string>();
        var depth = 0;
        foreach (var t in tags.Skip(1))
        {
            var closing = t.Groups[1].Value == "/";
            var selfClosing = t.Groups[3].Value == "/";
            if (closing) { depth--; if (depth < 0) break; continue; }
            if (depth == 0) children.Add(t.Value);
            if (!selfClosing) depth++;
        }
        return children;
    }

    private static List<string> Svgs(string html) => Regex.Matches(html, "<svg\\b[^>]*>").Select(m => m.Value).ToList();

    private static List<string> Labels(string html) =>
        Regex.Matches(html, "<h2 class=\"specimen-label\"[^>]*>([^<]*)</h2>").Select(m => D(m.Groups[1].Value)).ToList();

    private static void Clean(string html)
    {
        Guards.AssertNoInlineStyle(html);
        Guards.AssertNoScript(html);
    }

    // ── DragHandle Icon mode ────────────────────────────────────────────────

    public static TheoryData<HandleState> HandleStates() => new() { HandleState.Rest, HandleState.Focused, HandleState.Lifted };

    [Theory]
    [MemberData(nameof(HandleStates))]
    public async Task DragHandle_Icon_false_is_byte_identical_to_omitted(HandleState state)
    {
        var omitted = await Render.Html<DragHandle>(new { Label = "Move Mountain Lake", State = state, Position = 3, Total = 12 });
        var off = await Render.Html<DragHandle>(new { Label = "Move Mountain Lake", State = state, Position = 3, Total = 12, Icon = false });
        Assert.Equal(omitted, off);
    }

    [Theory]
    [MemberData(nameof(HandleStates))]
    public async Task DragHandle_Icon_is_the_word_markup_with_the_glyph_replaced_by_grip_vertical(HandleState state)
    {
        var word = await Render.Html<DragHandle>(new { Label = "Move Mountain Lake", State = state, Position = 3, Total = 12 });
        var icon = await Render.Html<DragHandle>(new { Label = "Move Mountain Lake", State = state, Position = 3, Total = 12, Icon = true });
        var grip = await Render.Html<Icon>(new { Name = "grip-vertical" });
        Clean(icon);

        // Same button, same class (handle / is-focus / is-lifted), aria-label, aria-pressed, describedby.
        var glyph = Regex.Match(word, "<span aria-hidden=\"true\"[^>]*>&#x2807;</span>");
        Assert.True(glyph.Success, word);
        Assert.Equal(word[..glyph.Index] + grip + word[(glyph.Index + glyph.Length)..], icon);

        var button = Regex.Match(icon, "<button\\b[^>]*>").Value;
        Assert.Equal("button", Attr(button, "type"));
        Assert.Equal(state switch { HandleState.Focused => "handle is-focus", HandleState.Lifted => "handle is-lifted", _ => "handle" }, Attr(button, "class"));
        Assert.Equal("Move Mountain Lake", Attr(button, "aria-label"));
        Assert.Equal(state == HandleState.Lifted ? "true" : "false", Attr(button, "aria-pressed"));
        Assert.Equal(["grip-vertical"], Svgs(icon).Select(s => Attr(s, "data-icon")));
        Assert.DoesNotContain("&#x2807;", icon);
        Assert.DoesNotContain("\u2807", D(icon));

        var help = Regex.Matches(icon, "<p\\b[^>]*role=\"status\"[^>]*>").Select(m => m.Value).ToList();
        if (state == HandleState.Lifted)
        {
            Assert.Equal("lift-help-3", Attr(Assert.Single(help), "id"));
            Assert.Equal("lift-help-3", Attr(button, "aria-describedby"));
        }
        else
        {
            Assert.Empty(help);
            Assert.Null(Attr(button, "aria-describedby"));
        }
    }

    // ── CoverMarker Icon mode ───────────────────────────────────────────────

    public static TheoryData<CoverState, string?, string?, string?> CoverCases()
    {
        var d = new TheoryData<CoverState, string?, string?, string?>();
        foreach (var state in new[] { CoverState.Unpinned, CoverState.Automatic, CoverState.Pinned })
            foreach (var form in new[] { null, "b-cover" })
                foreach (var image in new[] { null, "id-a" })
                    foreach (var unpin in new[] { null, "b-unpin" })
                        if (form is not null || image is null) d.Add(state, form, image, unpin);
        return d;
    }

    private static Task<string> CoverAsync(CoverState state, string? form, string? image, string? unpin, bool? icon) =>
        Render.Html<CoverMarker>(new Dictionary<string, object?>
        {
            ["State"] = state, ["ImageTitle"] = "Mountain Lake", ["FormId"] = form, ["ImageId"] = image, ["UnpinFormId"] = unpin,
        }.Concat(icon is null ? [] : new Dictionary<string, object?> { ["Icon"] = icon.Value }).ToDictionary());

    [Theory]
    [MemberData(nameof(CoverCases))]
    public async Task CoverMarker_Icon_false_is_byte_identical_to_omitted(CoverState state, string? form, string? image, string? unpin)
    {
        Assert.Equal(await CoverAsync(state, form, image, unpin, null), await CoverAsync(state, form, image, unpin, false));
    }

    [Theory]
    [MemberData(nameof(CoverCases))]
    public async Task CoverMarker_Icon_mode_keeps_the_root_form_name_value_and_type_of_the_word_mode(CoverState state, string? form, string? image, string? unpin)
    {
        var word = await CoverAsync(state, form, image, unpin, null);
        var html = await CoverAsync(state, form, image, unpin, true);
        Clean(html);

        var root = Regex.Match(html.TrimStart(), "^<div\\b[^>]*>").Value;
        Assert.Equal("cover-marker", Attr(root, "class"));
        var child = Assert.Single(Children(html.Trim()));
        var star = Assert.Single(Svgs(html));
        Assert.Equal("star", Attr(star, "data-icon"));
        Assert.DoesNotContain("cover-tag", html);
        Assert.Empty(Regex.Matches(html, "<span\\b[^>]*class=\"cover-tag"));

        var wordButton = Regex.Match(word, "<button\\b[^>]*>").Value;
        switch (state)
        {
            case CoverState.Unpinned:
                Assert.StartsWith("<button", child);
                Assert.Equal("icon-button", Attr(child, "class"));
                Assert.Equal(("Make Mountain Lake the cover", "Make Mountain Lake the cover"), (Attr(child, "aria-label"), Attr(child, "title")));
                Assert.Equal("false", Attr(child, "aria-pressed"));
                Assert.Equal("none", Attr(star, "fill"));
                Assert.Equal(image is null ? "button" : "submit", Attr(child, "type"));
                Assert.Equal(Attr(wordButton, "type"), Attr(child, "type"));
                Assert.Equal((Attr(wordButton, "form"), Attr(wordButton, "name"), Attr(wordButton, "value")),
                             (Attr(child, "form"), Attr(child, "name"), Attr(child, "value")));
                Assert.Equal((form, image is null ? null : "image", image), (Attr(child, "form"), Attr(child, "name"), Attr(child, "value")));
                break;
            case CoverState.Automatic:
                Assert.StartsWith("<span", child);
                Assert.Equal("icon-button is-unavailable", Attr(child, "class"));
                Assert.Equal("img", Attr(child, "role"));
                Assert.Equal(("Cover: first photo", "Cover: first photo"), (Attr(child, "aria-label"), Attr(child, "title")));
                Assert.Null(Attr(child, "aria-pressed"));
                Assert.Equal("currentColor", Attr(star, "fill"));
                Assert.Empty(Regex.Matches(html, "<button\\b"));
                Assert.Empty(Regex.Matches(word, "<button\\b"));
                break;
            default:
                Assert.StartsWith("<button", child);
                Assert.Equal("icon-button is-active", Attr(child, "class"));
                Assert.Equal(("Unpin cover", "Unpin cover"), (Attr(child, "aria-label"), Attr(child, "title")));
                Assert.Equal("true", Attr(child, "aria-pressed"));
                Assert.Equal("currentColor", Attr(star, "fill"));
                Assert.Equal(unpin is null ? "button" : "submit", Attr(child, "type"));
                Assert.Equal(Attr(wordButton, "type"), Attr(child, "type"));
                Assert.Equal(Attr(wordButton, "form"), Attr(child, "form"));
                Assert.Equal(unpin ?? form, Attr(child, "form"));
                Assert.Null(Attr(child, "name"));
                Assert.Null(Attr(child, "value"));
                break;
        }
    }

    // ── GalleryBuilder (K6) ────────────────────────────────────────────────

    private static IReadOnlyList<ImageView> Three => Render.Samples.Landscape.Take(3).ToList();

    private static readonly string[] Ids = ["id-a", "id-b", "id-c"];

    private static async Task<string> BuilderAsync(int? pinned = 1) =>
        D(await Render.Html<GalleryBuilder>(new Dictionary<string, object?>
        {
            ["Title"] = "Coast", ["Items"] = Three, ["State"] = BuilderState.Rest, ["PinnedIndex"] = pinned,
            ["ItemIds"] = (IReadOnlyList<string>)Ids, ["FormId"] = "b-panel",
            ["RemoveFormId"] = "b-remove", ["CoverFormId"] = "b-cover", ["UnpinFormId"] = "b-unpin", ["OrderFormId"] = "b-order",
            ["PhotoHrefBase"] = "/g/gid/coast", ["TitleMax"] = 50, ["DescriptionMax"] = 70,
        }));

    private static List<string> Tiles(string html) =>
        Regex.Matches(html, "<div class=\"tile[^\"]*\"[^>]*>").Select(m => LibraryHtml.Element(html, "div", m.Index)).ToList();

    /// <summary>A child of the b-ctl row as "tag.class[data-icon]" (the first icon inside it).</summary>
    private static string Describe(string row, string open)
    {
        var tag = Regex.Match(open, "^<([a-z]+)").Groups[1].Value;
        var at = row.IndexOf(open, StringComparison.Ordinal);
        var element = LibraryHtml.Element(row, tag, at);
        var icon = Attr(Svgs(element).FirstOrDefault() ?? "", "data-icon");
        return $"{tag}.{Attr(open, "class")}[{icon}]";
    }

    [Fact]
    public async Task GalleryBuilder_K6_row_children_are_handle_cover_remove_view_in_that_order()
    {
        var html = await BuilderAsync();
        var tiles = Tiles(html);
        Assert.Equal(3, tiles.Count);
        var cover = new[] { "button.icon-button", "button.icon-button is-active", "button.icon-button" };
        for (var i = 0; i < 3; i++)
        {
            var row = Assert.Single(LibraryHtml.Blocks(tiles[i], "div", "b-ctl"));
            var kids = Children(row).Select(k => Describe(row, k)).ToList();
            Assert.Equal(["button.handle[grip-vertical]", "div.cover-marker[star]", "button.icon-button[image-minus]", "a.icon-button[square-arrow-out-up-right]"], kids);
            var marker = Assert.Single(LibraryHtml.Blocks(row, "div", "cover-marker"));
            Assert.Equal($"{cover[i]}[star]", Describe(marker, Assert.Single(Children(marker))));
        }
    }

    [Fact]
    public async Task GalleryBuilder_K6_remove_button_posts_the_same_form_name_and_value_with_its_label()
    {
        var html = await BuilderAsync();
        var tiles = Tiles(html);
        for (var i = 0; i < 3; i++)
        {
            var row = Assert.Single(LibraryHtml.Blocks(tiles[i], "div", "b-ctl"));
            var remove = Regex.Matches(row, "<button\\b[^>]*>").Select(m => m.Value).Single(b => Attr(b, "form") == "b-remove");
            var title = Three[i].Title;
            Assert.Equal(("icon-button", "submit", "b-remove", "image", Ids[i]),
                         (Attr(remove, "class"), Attr(remove, "type"), Attr(remove, "form"), Attr(remove, "name"), Attr(remove, "value")));
            Assert.Equal(($"Remove {title} from this gallery", $"Remove {title} from this gallery"), (Attr(remove, "aria-label"), Attr(remove, "title")));
            Assert.Null(Attr(remove, "aria-pressed"));
            Assert.Empty(Regex.Matches(row, ">Remove</button>"));
        }
    }

    [Fact]
    public async Task GalleryBuilder_K6_view_is_an_icon_link_to_the_photo_page_in_a_new_tab()
    {
        var html = await BuilderAsync();
        var tiles = Tiles(html);
        for (var i = 0; i < 3; i++)
        {
            var row = Assert.Single(LibraryHtml.Blocks(tiles[i], "div", "b-ctl"));
            var link = Assert.Single(Regex.Matches(row, "<a\\b[^>]*>").Select(m => m.Value));
            Assert.Equal(("icon-button", $"/g/gid/coast/{Ids[i]}", "_blank", "noopener"),
                         (Attr(link, "class"), Attr(link, "href"), Attr(link, "target"), Attr(link, "rel")));
            Assert.Equal($"View {Three[i].Title} (opens in a new tab)", Attr(link, "aria-label"));
            Assert.DoesNotContain("View ↗", row);
        }
    }

    [Fact]
    public async Task GalleryBuilder_K6_cover_buttons_keep_their_forms_and_states()
    {
        var tiles = Tiles(await BuilderAsync(pinned: 1));
        string CoverChild(string tile) => Assert.Single(Children(Assert.Single(LibraryHtml.Blocks(tile, "div", "cover-marker"))));

        var pinned = CoverChild(tiles[1]);
        Assert.Equal(("submit", "b-unpin", "true", "Unpin cover", (string?)null, (string?)null),
                     (Attr(pinned, "type"), Attr(pinned, "form"), Attr(pinned, "aria-pressed"), Attr(pinned, "aria-label"), Attr(pinned, "name"), Attr(pinned, "value")));
        foreach (var i in new[] { 0, 2 })
        {
            var make = CoverChild(tiles[i]);
            Assert.Equal(("submit", "b-cover", "false", $"Make {Three[i].Title} the cover", "image", Ids[i]),
                         (Attr(make, "type"), Attr(make, "form"), Attr(make, "aria-pressed"), Attr(make, "aria-label"), Attr(make, "name"), Attr(make, "value")));
        }

        // Nothing pinned: the first tile is the automatic cover (a static star), the others pin their own id.
        var auto = Tiles(await BuilderAsync(pinned: null));
        var first = CoverChild(auto[0]);
        Assert.StartsWith("<span", first);
        Assert.Equal(("icon-button is-unavailable", "Cover: first photo"), (Attr(first, "class"), Attr(first, "aria-label")));
        Assert.Equal("b-cover", Attr(CoverChild(auto[1]), "form"));
    }

    [Fact]
    public async Task GalleryBuilder_K6_title_is_on_the_photo_and_not_under_the_cell()
    {
        var html = await BuilderAsync();
        var tiles = Tiles(html);
        for (var i = 0; i < 3; i++)
        {
            var cell = Assert.Single(LibraryHtml.Blocks(tiles[i], "div", "cell"));
            var onPhoto = Assert.Single(LibraryHtml.Blocks(cell, "div", "on-photo"));
            var title = Assert.Single(Regex.Matches(onPhoto, "<p\\b[^>]*class=\"title\"[^>]*>([^<]*)</p>"));
            Assert.Equal(Three[i].Title, title.Groups[1].Value);
            Assert.Single(Regex.Matches(tiles[i], "class=\"title\""));
            // The row sits under the cell; nothing of it is drawn over the photo.
            var row = Assert.Single(LibraryHtml.Blocks(tiles[i], "div", "b-ctl"));
            Assert.True(tiles[i].IndexOf(cell, StringComparison.Ordinal) + cell.Length <= tiles[i].IndexOf(row, StringComparison.Ordinal));
            Assert.Empty(LibraryHtml.Blocks(cell, "div", "b-ctl"));
        }
    }

    [Fact]
    public async Task GalleryBuilder_K6_lifted_tile_keeps_the_handle_hooks_and_the_lift_help()
    {
        var html = D(await Render.Html<GalleryBuilder>(new { Title = "Coast", Items = Three, State = BuilderState.KeyboardLifted, ActiveIndex = 1 }));
        var tiles = Tiles(html);
        var row = Assert.Single(LibraryHtml.Blocks(tiles[1], "div", "b-ctl"));
        var handle = Regex.Match(row, "<button\\b[^>]*class=\"handle[^\"]*\"[^>]*>").Value;
        Assert.Equal(("handle is-lifted", "true", "lift-help-2"), (Attr(handle, "class"), Attr(handle, "aria-pressed"), Attr(handle, "aria-describedby")));
        var help = Regex.Match(row, "<p\\b[^>]*role=\"status\"[^>]*>([^<]*)</p>");
        Assert.True(help.Success, row);
        Assert.StartsWith("Lifted. Position 2 of 3.", help.Groups[1].Value);
    }

    // ── K8 catalogue specimens ─────────────────────────────────────────────

    [Fact]
    public async Task DragHandlePage_appends_the_Icon_specimen()
    {
        var html = await Render.Html<DragHandlePage>();
        Clean(html);
        Assert.Equal(["At rest", "Focused", "Lifted for keyboard moving (3 of 12)", "Icon"], Labels(html));
        Assert.Single(Svgs(html), s => Attr(s, "data-icon") == "grip-vertical");
    }

    [Fact]
    public async Task CoverMarkerPage_appends_the_three_Icon_specimens()
    {
        var html = await Render.Html<CoverMarkerPage>();
        Clean(html);
        Assert.Equal(["Pinned", "Automatic (first photo, nothing pinned)", "Unpinned", "Icon, unpinned", "Icon, automatic", "Icon, pinned"], Labels(html));
        var stars = Svgs(html).Where(s => Attr(s, "data-icon") == "star").Select(s => Attr(s, "fill")).ToList();
        Assert.Equal(["none", "currentColor", "currentColor"], stars);
        Assert.Single(Regex.Matches(html, "<button\\b[^>]*aria-pressed=\"true\"[^>]*>"));
    }
}
