using System.Net;
using System.Text.RegularExpressions;
using Gallery.Web.Components;
using Gallery.Web.Components.Molecules;
using Gallery.Web.Library;
using Gallery.Web.Ui.Pages;

namespace Gallery.Web.Tests;

/// <summary>
/// Feature 016 G2/G2a (GalleryActions molecule: the pure rules and the markup), G1 (eye/eye-off in
/// use) and G6 (the catalogue page).
/// </summary>
public partial class GalleryActionsTests
{
    private static readonly string MoleculeDir =
        Path.Combine(Render.RepoRoot, "src", "Gallery.Web", "Components", "Molecules");

    private static readonly string[] RowClasses = ["handle", "ga-vis", "ga-view", "ga-edit"];

    // ── pure rules (GalleryActions.cs) ───────────────────────────────────────

    [Theory]
    [InlineData(true, true, "eye", true, "Public. Make private")]
    [InlineData(true, false, "eye", true, "Public, hidden until it has a photo. Make private")]
    [InlineData(false, true, "eye-off", true, "Private. Make public")]
    [InlineData(false, false, "eye-off", false, "Private. Add a photo before making this gallery public")]
    public void Visibility_rules_follow_the_data_model_table(bool isPublic, bool hasPhotos, string icon, bool canToggle, string label)
    {
        Assert.Equal(label, GalleryActions.VisibilityLabel(isPublic, hasPhotos));
        Assert.Equal(icon, GalleryActions.VisibilityIcon(isPublic));
        Assert.Equal(canToggle, GalleryActions.CanToggle(isPublic, hasPhotos));
    }

    [Fact]
    public void Empty_private_label_reuses_the_publish_refusal_reason()
    {
        Assert.Equal($"Private. {GalleryService.EmptyPublishReason}", GalleryActions.VisibilityLabel(false, false));
    }

    // ── markup (G2, G2a) ─────────────────────────────────────────────────────

    public static TheoryData<bool, bool> States => new()
    {
        { true, true }, { true, false }, { false, true }, { false, false },
    };

    private static Task<string> RenderAsync(bool isPublic = true, bool hasPhotos = true, string? view = "/g/abc/coast",
        string title = "Coast", int position = 2, int total = 7) =>
        Render.Html<GalleryActions>(new
        {
            Title = title, IsPublic = isPublic, HasPhotos = hasPhotos, VisibilityForm = "gal-vis-abc",
            ViewHref = view, EditHref = "/admin/galleries/abc", DragLabel = $"Move {title}",
            Position = position, Total = total,
        });

    /// <summary>The opening tags of the root's direct children, in order.</summary>
    private static List<string> Children(string html)
    {
        var flat = html.Replace("\r", "").Replace("\n", "").Trim();
        var root = RootRx().Match(flat);
        Assert.True(root.Success, $"no gallery-actions root: {flat}");
        Assert.EndsWith("</div>", flat);
        var inner = flat[root.Length..^"</div>".Length];
        var kids = new List<string>();
        var depth = 0;
        foreach (Match m in TagRx().Matches(inner))
        {
            var closing = m.Value.StartsWith("</", StringComparison.Ordinal);
            if (!closing && depth == 0) kids.Add(m.Value);
            if (m.Groups["name"].Value is "svg" or "a" or "span" or "button")
                depth += closing ? -1 : 1;
        }
        Assert.Equal(0, depth);
        return kids;
    }

    private static string? Attr(string tag, string name)
    {
        var m = Regex.Match(tag, $"\\s{Regex.Escape(name)}(=\"([^\"]*)\")?(?=[\\s/>])");
        return !m.Success ? null : m.Groups[1].Success ? m.Groups[2].Value : "";
    }

    private static bool HasClass(string tag, string cls) => (Attr(tag, "class") ?? "").Split(' ').Contains(cls);

    private static string ClassOf(string tag) => RowClasses.Single(c => HasClass(tag, c));

    [Theory]
    [MemberData(nameof(States))]
    public async Task Four_children_in_order_move_visibility_view_edit_with_edit_last(bool isPublic, bool hasPhotos)
    {
        var html = await RenderAsync(isPublic, hasPhotos, view: hasPhotos ? "/g/abc/coast" : null);
        var kids = Children(html);
        Assert.Equal(RowClasses, kids.Select(ClassOf).ToArray());
        Assert.Equal("ga-edit", ClassOf(kids[^1]));
        Assert.Equal(
            ["grip-vertical", GalleryActions.VisibilityIcon(isPublic), "square-arrow-out-up-right", "pencil"],
            IconsRx().Matches(html).Select(m => m.Groups[1].Value).ToArray());
        Assert.DoesNotMatch("\\saria-pressed=", string.Join("", kids.Skip(1)));
        Guards.AssertNoInlineStyle(html);
        Guards.AssertNoScript(html);
    }

    [Theory]
    [MemberData(nameof(States))]
    public async Task No_aria_pressed_on_any_icon_control(bool isPublic, bool hasPhotos)
    {
        var html = await RenderAsync(isPublic, hasPhotos);
        // The DragHandle is a lift control and owns its aria-pressed; nothing else carries one.
        var tags = Regex.Matches(html, "<(a|span|button)\\b[^>]*>").Select(m => m.Value).ToList();
        Assert.Equal(["handle"], tags.Where(t => Attr(t, "aria-pressed") is not null).Select(t => Attr(t, "class")!).ToArray());
    }

    [Theory]
    [InlineData(true, true, "Public. Make private")]
    [InlineData(true, false, "Public, hidden until it has a photo. Make private")]
    public async Task Public_visibility_is_a_submit_button_with_no_public_field(bool isPublic, bool hasPhotos, string label)
    {
        var vis = Children(await RenderAsync(isPublic, hasPhotos))[1];
        Assert.StartsWith("<button ", vis);
        Assert.Equal("submit", Attr(vis, "type"));
        Assert.Equal("gal-vis-abc", Attr(vis, "form"));
        Assert.Null(Attr(vis, "name"));
        Assert.Null(Attr(vis, "value"));
        Assert.Null(Attr(vis, "aria-pressed"));
        Assert.Equal(label, Attr(vis, "aria-label"));
        Assert.Equal(label, Attr(vis, "title"));
    }

    [Fact]
    public async Task Private_with_photos_visibility_posts_public_on()
    {
        var vis = Children(await RenderAsync(isPublic: false, hasPhotos: true))[1];
        Assert.StartsWith("<button ", vis);
        Assert.Equal("submit", Attr(vis, "type"));
        Assert.Equal("gal-vis-abc", Attr(vis, "form"));
        Assert.Equal("public", Attr(vis, "name"));
        Assert.Equal("on", Attr(vis, "value"));
        Assert.Null(Attr(vis, "aria-pressed"));
        Assert.Equal("Private. Make public", Attr(vis, "aria-label"));
        Assert.Equal("Private. Make public", Attr(vis, "title"));
    }

    [Fact]
    public async Task Private_empty_visibility_is_an_unavailable_span_with_no_form()
    {
        var html = await RenderAsync(isPublic: false, hasPhotos: false, view: null);
        var vis = Children(html)[1];
        Assert.StartsWith("<span ", vis);
        Assert.Equal("img", Attr(vis, "role"));
        Assert.True(HasClass(vis, "is-unavailable"), vis);
        Assert.Null(Attr(vis, "form"));
        Assert.Null(Attr(vis, "name"));
        Assert.Null(Attr(vis, "value"));
        Assert.Null(Attr(vis, "type"));
        Assert.Equal("Private. Add a photo before making this gallery public", Attr(vis, "aria-label"));
        Assert.Equal("Private. Add a photo before making this gallery public", Attr(vis, "title"));
        // No control in the row submits anything: the only button is the drag handle (type="button").
        Assert.Equal(["button"], Regex.Matches(html, "<button\\b[^>]*>").Select(m => Attr(m.Value, "type")!).ToArray());
        Assert.DoesNotMatch("\\sname=\"public\"", html);
    }

    [Fact]
    public async Task View_with_an_href_is_a_new_tab_link()
    {
        var view = Children(await RenderAsync(title: "Coast"))[2];
        Assert.StartsWith("<a ", view);
        Assert.Equal("/g/abc/coast", Attr(view, "href"));
        Assert.Equal("_blank", Attr(view, "target"));
        Assert.Equal("noopener", Attr(view, "rel"));
        Assert.Equal("View Coast (opens in a new tab)", Attr(view, "aria-label"));
        Assert.Equal("View Coast (opens in a new tab)", Attr(view, "title"));
    }

    [Fact]
    public async Task View_without_an_href_is_an_unavailable_span()
    {
        var view = Children(await RenderAsync(view: null))[2];
        Assert.StartsWith("<span ", view);
        Assert.True(HasClass(view, "is-unavailable"), view);
        Assert.Equal("img", Attr(view, "role"));
        Assert.Equal("No public page", Attr(view, "aria-label"));
        Assert.Equal("No public page", Attr(view, "title"));
        Assert.Null(Attr(view, "href"));
        Assert.Null(Attr(view, "target"));
    }

    [Fact]
    public async Task Edit_is_a_link_to_EditHref()
    {
        var edit = Children(await RenderAsync(title: "Coast"))[3];
        Assert.StartsWith("<a ", edit);
        Assert.Equal("/admin/galleries/abc", Attr(edit, "href"));
        Assert.Equal("Edit Coast", Attr(edit, "aria-label"));
        Assert.Equal("Edit Coast", Attr(edit, "title"));
        Assert.Null(Attr(edit, "target"));
    }

    [Fact]
    public async Task Move_is_the_icon_drag_handle_with_its_label_position_and_total()
    {
        var html = await RenderAsync(title: "Coast", position: 2, total: 7);
        var handle = Children(html)[0];
        Assert.StartsWith("<button ", handle);
        Assert.Equal("handle", Attr(handle, "class"));
        Assert.Equal("button", Attr(handle, "type"));
        Assert.Equal("Move Coast", Attr(handle, "aria-label"));
        Assert.Equal("false", Attr(handle, "aria-pressed"));

        // Position/Total reach the DragHandle: its lifted help text reads them.
        var lifted = await Render.Html<DragHandle>(new { Label = "Move Coast", Icon = true, Position = 2, Total = 7, State = HandleState.Lifted });
        Assert.Contains("Position 2 of 7.", lifted);
        Assert.Equal(
            Regex.Replace(await Render.Html<DragHandle>(new { Label = "Move Coast", Icon = true, Position = 2, Total = 7 }), "\\s+", " ").Trim(),
            Regex.Replace(HandleBlock(html), "\\s+", " ").Trim());
    }

    /// <summary>The DragHandle's whole button element, from the row.</summary>
    private static string HandleBlock(string html)
    {
        var start = html.IndexOf("<button type=\"button\" class=\"handle\"", StringComparison.Ordinal);
        Assert.True(start >= 0, html);
        var end = html.IndexOf("</button>", start, StringComparison.Ordinal) + "</button>".Length;
        return html[start..end];
    }

    // ── CSS guard (G2) ───────────────────────────────────────────────────────

    [Fact]
    public void Row_css_is_one_tap_high_row_that_never_wraps()
    {
        var css = File.ReadAllText(Path.Combine(MoleculeDir, "GalleryActions.razor.css"));
        Assert.Contains(".gallery-actions {", css);
        Assert.Contains("display: flex;", css);
        Assert.Contains("flex-wrap: nowrap;", css);
        Assert.Contains("justify-content: center;", css);
        Assert.Contains("gap: 4px;", css);
        Assert.Contains("height: var(--tap);", css);
        Assert.DoesNotContain("44px", css);
        Assert.DoesNotMatch("flex-wrap:\\s*wrap", css);
    }

    [Fact]
    public void The_molecule_builds_no_admin_address()
    {
        var files = Directory.GetFiles(MoleculeDir, "GalleryActions.*").Select(f => Path.GetFileName(f)).Order().ToArray();
        Assert.Equal(["GalleryActions.cs", "GalleryActions.razor", "GalleryActions.razor.css"], files);
        foreach (var f in files)
        {
            var text = File.ReadAllText(Path.Combine(MoleculeDir, f));
            Assert.DoesNotContain("/admin", text);
            Assert.DoesNotContain("/g/", text);
            Assert.DoesNotContain("<script", text);
        }
    }

    // ── catalogue page (G6) ──────────────────────────────────────────────────

    [Fact]
    public async Task Catalogue_page_renders_five_specimens_in_order()
    {
        var html = await Render.Html(typeof(GalleryActionsPage));
        Guards.AssertNoInlineStyle(html);
        Guards.AssertNoScript(html);
        var labels = Regex.Matches(html, "<h2 class=\"specimen-label\"[^>]*>([^<]*)</h2>")
            .Select(m => WebUtility.HtmlDecode(m.Groups[1].Value)).ToArray();
        Assert.Equal(["Public", "Private", "Public, no photos", "Private, no photos", "Long title"], labels);
        Assert.Equal(5, Regex.Matches(html, "<div class=\"specimen-body w-320\"").Count);
        Assert.Equal(5, Regex.Matches(html, "<div class=\"gallery-actions\"").Count);
        Assert.Equal(
            ["eye", "eye-off", "eye", "eye-off", "eye"],
            Regex.Matches(html, "class=\"icon-button[^\"]*\\bga-vis\\b[^\"]*\"[^>]*>\\s*<svg[^>]*data-icon=\"([^\"]+)\"")
                .Select(m => m.Groups[1].Value).ToArray());
    }

    [GeneratedRegex("^<div class=\"gallery-actions\"[^>]*>")]
    private static partial Regex RootRx();

    [GeneratedRegex("</?(?<name>[a-z]+)\\b[^>]*>")]
    private static partial Regex TagRx();

    [GeneratedRegex("data-icon=\"([^\"]+)\"")]
    private static partial Regex IconsRx();
}
