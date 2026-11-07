using System.Net;
using System.Text.RegularExpressions;
using Gallery.Web.Components.Molecules;
using Gallery.Web.Library;
using Gallery.Web.Tests.Fixtures;

namespace Gallery.Web.Tests;

/// <summary>
/// Feature 015 K4 (PhotoActions molecule: the pure rules, the markup, the FR-020 source guard)
/// and K5 (the library page wiring, over the HTTP host: SC-002 and the edit toggle hrefs).
/// </summary>
[Collection("s3")]
public partial class PhotoActionsTests(S3ProxyFixture s3, MongoFixture mongo) : IClassFixture<MongoFixture>
{
    private static readonly string MoleculeDir =
        Path.Combine(Render.RepoRoot, "src", "Gallery.Web", "Components", "Molecules");

    // ── pure rules (PhotoActions.cs) ──────────────────────────────────────────

    [Theory]
    [InlineData(-1, Membership.None, "folder-plus", "Not in a gallery")]
    [InlineData(0, Membership.None, "folder-plus", "Not in a gallery")]
    [InlineData(1, Membership.One, "folder", "In 1 gallery")]
    [InlineData(2, Membership.Many, "folders", "In 2 galleries")]
    [InlineData(37, Membership.Many, "folders", "In 37 galleries")]
    public void Count_maps_to_membership_icon_and_label(int count, Membership membership, string icon, string label)
    {
        Assert.Equal(membership, PhotoActions.MembershipOf(count));
        Assert.Equal(icon, PhotoActions.IconOf(PhotoActions.MembershipOf(count)));
        Assert.Equal(label, PhotoActions.CountLabel(count));
    }

    // ── markup (K4) ──────────────────────────────────────────────────────────

    private static Task<string> RenderAsync(int count = 1, string? view = "/v/1", bool active = false, string title = "Pier") =>
        Render.Html<PhotoActions>(new
        {
            Count = count, GroupHref = "?g", ViewHref = view, EditHref = "?e", Active = active, ItemLabel = title,
        });

    /// <summary>The opening tags of the root's direct children, in order.</summary>
    private static List<string> Children(string html)
    {
        var flat = html.Replace("\r", "").Replace("\n", "").Trim();
        var root = RootRx().Match(flat);
        Assert.True(root.Success, $"no photo-actions root: {flat}");
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

    private static string Classes(string tag) => Attr(tag, "class") ?? "";

    private static bool HasClass(string tag, string cls) => Classes(tag).Split(' ').Contains(cls);

    [Fact]
    public async Task Three_children_in_order_group_view_edit()
    {
        var html = await RenderAsync();
        var kids = Children(html);
        Assert.Equal(3, kids.Count);
        Assert.Equal(["pa-group", "pa-view", "pa-edit"],
            kids.Select(k => new[] { "pa-group", "pa-view", "pa-edit" }.Single(c => HasClass(k, c))).ToArray());
        Assert.All(kids, k => Assert.True(HasClass(k, "icon-button"), k));
        Assert.Equal(["folder", "square-arrow-out-up-right", "pencil"],
            IconsRx().Matches(html).Select(m => m.Groups[1].Value).ToArray());
        Guards.AssertNoInlineStyle(html);
        Guards.AssertNoScript(html);
        Assert.DoesNotContain("btn-primary", html);
    }

    [Fact]
    public async Task Labels_are_verbatim_and_the_view_link_opens_a_new_tab()
    {
        var kids = Children(await RenderAsync(count: 2, title: "Pier"));
        var (group, view, edit) = (kids[0], kids[1], kids[2]);

        Assert.StartsWith("<a ", group);
        Assert.Equal("In 2 galleries", Attr(group, "aria-label"));
        Assert.Equal("In 2 galleries", Attr(group, "title"));
        Assert.Equal("?g", Attr(group, "href"));

        Assert.StartsWith("<a ", view);
        Assert.Equal("View Pier (opens in a new tab)", Attr(view, "aria-label"));
        Assert.Equal("View Pier (opens in a new tab)", Attr(view, "title"));
        Assert.Equal("/v/1", Attr(view, "href"));
        Assert.Equal("_blank", Attr(view, "target"));
        Assert.Equal("noopener", Attr(view, "rel"));

        Assert.StartsWith("<a ", edit);
        Assert.Equal("Edit Pier", Attr(edit, "aria-label"));
        Assert.Equal("Edit Pier", Attr(edit, "title"));
        Assert.Equal("?e", Attr(edit, "href"));
        Assert.Null(Attr(edit, "target"));
    }

    [Fact]
    public async Task No_view_href_renders_an_unavailable_span_with_no_href()
    {
        var html = await RenderAsync(count: 0, view: null);
        var view = Children(html)[1];
        Assert.StartsWith("<span ", view);
        Assert.True(HasClass(view, "is-unavailable"), view);
        Assert.True(HasClass(view, "pa-view"), view);
        Assert.Equal("No public page", Attr(view, "aria-label"));
        Assert.Equal("img", Attr(view, "role"));
        Assert.Null(Attr(view, "href"));
        Assert.Null(Attr(view, "tabindex"));
        Assert.Null(Attr(view, "target"));
        Assert.Equal(2, Regex.Matches(html, "<a\\b").Count);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Active_marks_both_group_and_edit_and_flips_the_edit_label(bool active)
    {
        var kids = Children(await RenderAsync(active: active, title: "Pier"));
        var (group, view, edit) = (kids[0], kids[1], kids[2]);
        Assert.Equal(active ? "true" : null, Attr(group, "aria-current"));
        Assert.Equal(active ? "true" : null, Attr(edit, "aria-current"));
        Assert.Null(Attr(view, "aria-current"));
        Assert.Equal(active, HasClass(group, "is-active"));
        Assert.Equal(active, HasClass(edit, "is-active"));
        Assert.False(HasClass(view, "is-active"));
        Assert.Equal(active ? "Close the editor for Pier" : "Edit Pier", Attr(edit, "aria-label"));
        Assert.Equal(active ? "Close the editor for Pier" : "Edit Pier", Attr(edit, "title"));
    }

    [Fact]
    public void FR020_source_guard_the_molecule_builds_no_admin_address_and_knows_no_library_type()
    {
        var files = Directory.GetFiles(MoleculeDir, "PhotoActions.*").Select(f => Path.GetFileName(f)).Order().ToArray();
        Assert.Equal(["PhotoActions.cs", "PhotoActions.razor", "PhotoActions.razor.css"], files);
        foreach (var f in files)
        {
            var text = File.ReadAllText(Path.Combine(MoleculeDir, f));
            Assert.DoesNotContain("/admin", text);
            Assert.DoesNotContain("Gallery.Web.Library", text);
            Assert.DoesNotContain("open=", text);
        }
    }

    [Fact]
    public void Row_css_is_one_tap_high_row_that_never_wraps()
    {
        var css = File.ReadAllText(Path.Combine(MoleculeDir, "PhotoActions.razor.css"));
        Assert.Contains("height: var(--tap);", css);
        Assert.DoesNotContain("44px", css);
        Assert.DoesNotMatch("flex-wrap:\\s*wrap", css);
    }

    // ── library page wiring over HTTP (K5, SC-002) ───────────────────────────

    private static async Task<string> GalleryAsync(LibraryHost h, string title, IReadOnlyList<string> ids, bool publish = false)
    {
        var created = await h.RunAsync<GalleryService, OperationResult>(s => s.CreateAsync(title));
        Assert.True(created.IsOk);
        var gid = created.GalleryId!;
        if (ids.Count > 0) Assert.True((await h.RunAsync<GalleryService, OperationResult>(s => s.AddAsync(gid, ids))).IsOk);
        if (publish) Assert.True((await h.RunAsync<GalleryService, OperationResult>(s => s.PublishAsync(gid, confirmed: true))).IsOk);
        return gid;
    }

    private static async Task<string> GetOkAsync(LibraryHost h, string path)
    {
        var resp = await h.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        return await LibraryHtml.BodyAsync(resp);
    }

    /// <summary>The opening tag of the single element in the tile with class <paramref name="cls"/>.</summary>
    private static string ControlOf(string tile, string cls) =>
        Assert.Single(Regex.Matches(tile, "<(a|span|button)\\b[^>]*>").Select(m => m.Value), t => HasClass(t, cls));

    private sealed record Arranged(string None, string One, string Three);

    private static async Task<Arranged> ArrangeAsync(LibraryHost h)
    {
        var none = await h.InsertUploadedImageAsync(files: false);
        var one = await h.InsertUploadedImageAsync(files: false);
        var three = await h.InsertUploadedImageAsync(files: false);
        await GalleryAsync(h, "coast", [three.Id], publish: true);
        await GalleryAsync(h, "Alpine", [three.Id]);
        await GalleryAsync(h, "birds", [one.Id, three.Id]);
        await h.SettleAsync();
        return new Arranged(none.Id, one.Id, three.Id);
    }

    [Fact]
    public async Task SC002_each_tile_names_its_gallery_count_by_icon_and_label()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3);
        var a = await ArrangeAsync(h);
        var html = await GetOkAsync(h, "/admin/library");

        foreach (var (iid, icon, label) in new[]
                 {
                     (a.None, "folder-plus", "Not in a gallery"),
                     (a.One, "folder", "In 1 gallery"),
                     (a.Three, "folders", "In 3 galleries"),
                 })
        {
            var tile = LibraryHtml.TileOf(html, iid);
            Assert.Equal($"t-{iid}", Attr(Regex.Match(tile, "^<div\\b[^>]*>").Value, "id"));
            var row = Assert.Single(LibraryHtml.Blocks(tile, "div", "photo-actions"));
            var group = ControlOf(row, "pa-group");
            Assert.Equal(label, Attr(group, "aria-label"));
            var groupBlock = LibraryHtml.Element(row, "a", row.IndexOf(group, StringComparison.Ordinal));
            Assert.Equal([icon], IconsRx().Matches(groupBlock).Select(m => m.Groups[1].Value).ToArray());
            // Count only: no label or text in the row names a gallery or a public count. (The view
            // href is the photo's public address, which carries its gallery slug by design: K5.)
            var said = LibraryHtml.Text(row) + " " + string.Join(" ", Regex.Matches(row, "(aria-label|title)=\"([^\"]*)\"").Select(m => m.Groups[2].Value));
            Assert.DoesNotContain("coast", said, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("alpine", said, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("birds", said, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("public in", said, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("public", Attr(group, "aria-label")!, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("/admin/galleries/", row);
        }
        Assert.Empty(CurrentPhotoControls(html));
    }

    [Fact]
    public async Task Open_photo_is_the_one_active_tile_and_its_icons_close_the_editor()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3);
        var a = await ArrangeAsync(h);
        var html = await GetOkAsync(h, $"/admin/library?open={a.One}");
        var ids = LibraryHtml.TileIds(html);
        Assert.Equal(3, ids.Count);

        var active = new List<string>();
        foreach (var iid in ids)
        {
            var row = Assert.Single(LibraryHtml.Blocks(LibraryHtml.TileOf(html, iid), "div", "photo-actions"));
            var group = ControlOf(row, "pa-group");
            var edit = ControlOf(row, "pa-edit");
            var expected = iid == a.One ? $"?page=1#t-{iid}" : $"?page=1&open={iid}#t-{iid}";
            Assert.Equal(expected, Attr(edit, "href"));
            Assert.Equal(expected, Attr(group, "href"));
            Assert.Equal(Attr(group, "aria-current"), Attr(edit, "aria-current"));
            if (Attr(edit, "aria-current") == "true") active.Add(iid);
        }
        Assert.Equal([a.One], active);
        // On the whole page: exactly the open tile's group and edit icons are current.
        Assert.Equal([$"pa-group t-{a.One}", $"pa-edit t-{a.One}"], CurrentPhotoControls(html));
    }

    /// <summary>"{pa-class} {tile id}" for every photo-actions control carrying aria-current, in page order.</summary>
    private static List<string> CurrentPhotoControls(string html)
    {
        var list = new List<string>();
        foreach (var iid in LibraryHtml.TileIds(html))
        {
            var row = Assert.Single(LibraryHtml.Blocks(LibraryHtml.TileOf(html, iid), "div", "photo-actions"));
            foreach (var cls in new[] { "pa-group", "pa-view", "pa-edit" })
                if (Attr(ControlOf(row, cls), "aria-current") is not null) list.Add($"{cls} t-{iid}");
        }
        return list;
    }

    [GeneratedRegex("^<div class=\"photo-actions\"[^>]*>")]
    private static partial Regex RootRx();

    [GeneratedRegex("</?(?<name>[a-z]+)\\b[^>]*>")]
    private static partial Regex TagRx();

    [GeneratedRegex("data-icon=\"([^\"]+)\"")]
    private static partial Regex IconsRx();
}
