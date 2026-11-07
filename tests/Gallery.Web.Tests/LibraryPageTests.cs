using System.Net;
using System.Text.RegularExpressions;
using Gallery.Web.Data;
using Gallery.Web.Library;
using Gallery.Web.Tests.Fixtures;
using MongoDB.Driver;

namespace Gallery.Web.Tests;

/// <summary>Shared HTML helpers for the 009 library HTTP tests (Batch D files only).</summary>
internal static partial class LibraryHtml
{
    /// <summary>The body, HTML-decoded, with the inlined stylesheet removed.</summary>
    public static async Task<string> BodyAsync(HttpResponseMessage resp) =>
        WebUtility.HtmlDecode(Guards.StripStyle(await resp.Content.ReadAsStringAsync()));

    /// <summary>Every element (open tag through its matching close) whose class list contains <paramref name="cls"/>.</summary>
    public static List<string> Blocks(string html, string tag, string cls)
    {
        var list = new List<string>();
        foreach (Match m in Regex.Matches(html, $"<{tag}\\b[^>]*class=\"[^\"]*\\b{Regex.Escape(cls)}\\b[^\"]*\"[^>]*>"))
            list.Add(Element(html, tag, m.Index));
        return list;
    }

    /// <summary>The element starting at <paramref name="start"/>, through its balanced close tag.</summary>
    public static string Element(string html, string tag, int start)
    {
        var depth = 0;
        var open = new Regex($"<{tag}\\b|</{tag}>");
        foreach (Match m in open.Matches(html, start))
        {
            depth += m.Value.StartsWith("</", StringComparison.Ordinal) ? -1 : 1;
            if (depth == 0) return html[start..(m.Index + m.Length)];
        }
        throw new InvalidOperationException($"unbalanced <{tag}> at {start}");
    }

    /// <summary>The html with every <c>&lt;dialog&gt;…&lt;/dialog&gt;</c> removed.</summary>
    public static string WithoutDialogs(string html) => DialogBlock().Replace(html, "");

    /// <summary>Text outside tags (attribute names and values removed).</summary>
    public static string Text(string html) => TagRx().Replace(ScriptBlock().Replace(html, ""), " ");

    /// <summary>The ids of the library tiles in page order (from each tile's select mark).</summary>
    public static List<string> TileIds(string html) =>
        Blocks(html, "div", "lib-tile")
            .Select(t => Regex.Match(t, "name=\"ids\"[^>]*value=\"([^\"]+)\"").Groups[1].Value)
            .ToList();

    /// <summary>The tile whose select mark carries <paramref name="iid"/>.</summary>
    public static string TileOf(string html, string iid) =>
        Blocks(html, "div", "lib-tile").Single(t => t.Contains($"value=\"{iid}\"", StringComparison.Ordinal));

    /// <summary>The <c>role="status"</c> text of the page.</summary>
    public static string Status(string html)
    {
        var m = Regex.Match(html, "<p\\b[^>]*role=\"status\"[^>]*>(.*?)</p>", RegexOptions.Singleline);
        Assert.True(m.Success, "no role=\"status\" element");
        return m.Groups[1].Value.Trim();
    }

    [GeneratedRegex("<dialog\\b.*?</dialog>", RegexOptions.Singleline)]
    private static partial Regex DialogBlock();

    [GeneratedRegex("<script\\b.*?</script>", RegexOptions.Singleline)]
    private static partial Regex ScriptBlock();

    [GeneratedRegex("<[^>]*>")]
    private static partial Regex TagRx();
}

/// <summary>
/// 009 T032 (US1; FR-002, FR-005, FR-007, FR-011; UI-1, UI-2, UI-3, UI-4, UI-7, UI-8): the library
/// page L1 over HTTP through <see cref="LibraryHost"/>. Galleries are built through the services, the
/// way the owner builds them.
/// </summary>
[Collection("s3")]
public class LibraryPageTests(S3ProxyFixture s3, MongoFixture mongo) : IClassFixture<MongoFixture>
{
    private static Task<OperationResult> Gs(LibraryHost h, Func<GalleryService, Task<OperationResult>> call) => h.RunAsync(call);

    private static async Task<string> GalleryAsync(LibraryHost h, string title, IReadOnlyList<string> ids, bool publish = false)
    {
        var created = await Gs(h, s => s.CreateAsync(title));
        Assert.True(created.IsOk);
        var gid = created.GalleryId!;
        if (ids.Count > 0) Assert.True((await Gs(h, s => s.AddAsync(gid, ids))).IsOk);
        if (publish) Assert.True((await Gs(h, s => s.PublishAsync(gid, confirmed: true))).IsOk);
        return gid;
    }

    private static async Task<string> GetOkAsync(LibraryHost h, string path)
    {
        var resp = await h.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        return await LibraryHtml.BodyAsync(resp);
    }

    [Fact]
    public async Task Owner_bar_marks_Library_current_and_not_Galleries()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3);
        var html = await GetOkAsync(h, "/admin/library");

        Assert.Matches("<a href=\"/admin/library\" aria-current=\"page\"", html);
        Assert.DoesNotMatch("<a href=\"/admin/galleries\"[^>]*aria-current", html);
        Assert.DoesNotMatch("<a href=\"/admin/upload\"[^>]*aria-current", html);
    }

    [Fact]
    public async Task Pages_hold_PageSize_newest_first_and_a_page_past_the_end_renders_the_last()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3);
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var images = new List<ImageDoc>();
        for (var i = 0; i < 101; i++)
            images.Add(await h.InsertUploadedImageAsync(t0.AddMinutes(i), files: false));
        var newestFirst = images.OrderByDescending(i => i.CreatedAt).Select(i => i.Id).ToList();

        var p1 = await GetOkAsync(h, "/admin/library");
        Assert.Equal(newestFirst.Take(100).ToList(), LibraryHtml.TileIds(p1));
        Assert.Contains("Page 1 of 2", p1);

        var p2 = await GetOkAsync(h, "/admin/library?page=2");
        Assert.Equal([newestFirst[100]], LibraryHtml.TileIds(p2));

        var p9 = await GetOkAsync(h, "/admin/library?page=9");
        Assert.Equal([newestFirst[100]], LibraryHtml.TileIds(p9));
        Assert.Contains("Page 2 of 2", p9);
    }

    [Fact]
    public async Task Tombstoned_images_are_not_listed()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3);
        var live = await h.InsertUploadedImageAsync(files: false);
        await h.InsertUploadedImageAsync(files: false, shape: i => i.DeletingAt = DateTime.UtcNow);

        var html = await GetOkAsync(h, "/admin/library");
        Assert.Equal([live.Id], LibraryHtml.TileIds(html));
    }

    [Fact]
    public async Task Empty_library_says_no_photos_yet_with_the_upload_link()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3);
        var html = await GetOkAsync(h, "/admin/library");

        Assert.Matches("<p class=\"lib-empty\"[^>]*>No photos yet. <a href=\"/admin/upload\"[^>]*>Upload photos</a></p>", html);
        Assert.Empty(LibraryHtml.TileIds(html));
    }

    [Fact]
    public async Task Status_lines_and_View_links_follow_home_order()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3);
        var none = await h.InsertUploadedImageAsync(files: false);
        var twoPrivate = await h.InsertUploadedImageAsync(files: false);
        var twoOnePublic = await h.InsertUploadedImageAsync(files: false);

        var a = await GalleryAsync(h, "Alpha", [twoPrivate.Id, twoOnePublic.Id]);           // private, first in home order
        var b = await GalleryAsync(h, "Bravo", [twoPrivate.Id]);                              // private
        var c = await GalleryAsync(h, "Charlie", [twoOnePublic.Id], publish: true);           // public
        await h.SettleAsync();

        var html = await GetOkAsync(h, "/admin/library");
        // 015 A1 (stated change, owner clarify: count only): the gallery icon's label is the count,
        // its glyph the membership state; no gallery title and no public count.
        string LabelOf(string iid) => Regex.Match(LibraryHtml.Blocks(LibraryHtml.TileOf(html, iid), "a", "pa-group").Single(), "aria-label=\"([^\"]*)\"").Groups[1].Value;
        string GlyphOf(string iid) => Regex.Match(LibraryHtml.Blocks(LibraryHtml.TileOf(html, iid), "a", "pa-group").Single(), "data-icon=\"([^\"]*)\"").Groups[1].Value;

        Assert.Equal(("Not in a gallery", "folder-plus"), (LabelOf(none.Id), GlyphOf(none.Id)));
        Assert.Equal(("In 2 galleries", "folders"), (LabelOf(twoPrivate.Id), GlyphOf(twoPrivate.Id)));
        Assert.Equal(("In 2 galleries", "folders"), (LabelOf(twoOnePublic.Id), GlyphOf(twoOnePublic.Id)));

        // UI-1: the view icon links the photo page of the FIRST gallery in home order holding it, in a new tab.
        Assert.Matches($"<a class=\"icon-button pa-view\" href=\"/g/{a}/alpha/{twoPrivate.Id}\"[^>]*target=\"_blank\"", LibraryHtml.TileOf(html, twoPrivate.Id));
        Assert.Matches($"<a class=\"icon-button pa-view\" href=\"/g/{a}/alpha/{twoOnePublic.Id}\"[^>]*target=\"_blank\"", LibraryHtml.TileOf(html, twoOnePublic.Id));
        // No gallery → the view icon is unavailable (a span, no link), and no /g/ link at all.
        Assert.Single(LibraryHtml.Blocks(LibraryHtml.TileOf(html, none.Id), "span", "pa-view"));
        Assert.Contains("is-unavailable", LibraryHtml.Blocks(LibraryHtml.TileOf(html, none.Id), "span", "pa-view").Single());
        Assert.Empty(LibraryHtml.Blocks(LibraryHtml.TileOf(html, none.Id), "a", "pa-view"));
        Assert.DoesNotContain("/g/", LibraryHtml.TileOf(html, none.Id));
        _ = (b, c);
    }

    [Fact]
    public async Task Only_the_select_mark_is_over_the_photo_and_the_row_is_under_the_title()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3);
        var img = await h.InsertUploadedImageAsync(files: false, title: "Pier");
        await GalleryAsync(h, "Coast", [img.Id]);
        await h.SettleAsync();

        var html = await GetOkAsync(h, "/admin/library");
        var tile = LibraryHtml.TileOf(html, img.Id);

        // The photo cell holds the picture and the overlay; the overlay holds exactly one pos-tl SelectMark.
        var overlay = LibraryHtml.Blocks(tile, "div", "overlay").Single();
        var overlayChildren = Regex.Matches(overlay, "<div class=\"([^\"]*)\"").Select(m => m.Groups[1].Value).ToList();
        Assert.Equal(["overlay", "pos-tl"], overlayChildren);
        Assert.Single(Regex.Matches(overlay, "class=\"select-mark\""));
        Assert.Matches("name=\"ids\"[^>]*value=\"" + img.Id + "\"[^>]*form=\"lib-select\"", overlay);
        Assert.DoesNotContain("photo-actions", overlay);
        Assert.DoesNotContain("text-link", overlay);

        var cell = LibraryHtml.Blocks(tile, "div", "cell").Single();
        var outsideOverlay = cell.Replace(overlay, "");
        Assert.DoesNotContain("<a ", outsideOverlay);
        Assert.DoesNotContain("<input", outsideOverlay);

        // 015 A2 (stated change): the title is INSIDE the cell, in .on-photo (not in the overlay), and
        // there is no title under the cell; gallery, view and edit are in .photo-actions, in that order,
        // after the cell, and edit opens this photo landing back on its tile.
        var onPhoto = LibraryHtml.Blocks(cell, "div", "on-photo").Single();
        Assert.Contains("class=\"title\"", onPhoto);
        Assert.DoesNotContain("on-photo", overlay);
        Assert.Single(Regex.Matches(tile, "class=\"title\""));
        var row = LibraryHtml.Blocks(tile, "div", "photo-actions").Single();
        var parts = Regex.Matches(row, "class=\"icon-button[^\"]*\\b(pa-[a-z]+)\\b").Select(m => m.Groups[1].Value).ToList();
        Assert.Equal(["pa-group", "pa-view", "pa-edit"], parts);
        Assert.Matches($"<a class=\"icon-button pa-edit\" href=\"\\?page=1&open={img.Id}#t-{img.Id}\"", row);
        Assert.True(tile.IndexOf(cell, StringComparison.Ordinal) + cell.Length <= tile.IndexOf(row, StringComparison.Ordinal));
    }

    private static List<string> PrimaryButtons(string html) =>
        Regex.Matches(LibraryHtml.WithoutDialogs(html), "<(button|a|summary)\\b[^>]*class=\"btn btn-primary[^\"]*\"[^>]*>(.*?)</\\1>", RegexOptions.Singleline)
            .Select(m => Regex.Replace(m.Groups[2].Value, "<[^>]*>", "").Trim()).ToList();

    [Fact]
    public async Task One_primary_per_state_and_deletes_are_outlined_danger()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3);
        var img = await h.InsertUploadedImageAsync(files: false, title: "Pier");

        // Without ?open: the selection bar's "Add to gallery…" is the one primary.
        var rest = await GetOkAsync(h, "/admin/library");
        Assert.Equal(["Add to gallery…"], PrimaryButtons(rest));
        Assert.Matches("<button class=\"btn btn-danger\"[^>]*>\\s*Delete…\\s*</button>", rest);
        Assert.DoesNotMatch("btn-primary[^>]*>\\s*Delete", rest);

        // With ?open: the panel's Save is the one primary and Add renders Secondary.
        var open = await GetOkAsync(h, $"/admin/library?open={img.Id}");
        Assert.Equal(["Save"], PrimaryButtons(open));
        Assert.Matches("<button class=\"btn btn-secondary\"[^>]*>\\s*Add to gallery…\\s*</button>", open);
        // UI-4: Delete photo (trigger and the confirm dialog's action) is outlined Danger, never Primary.
        Assert.Matches("<button [^>]*class=\"btn btn-danger danger-trigger\" type=\"button\"[^>]*>(Delete photo…|Delete photo&#x2026;)</button>", open);
        Assert.Matches("<button class=\"btn btn-danger\"[^>]*form=\"lib-del-one\"[^>]*>\\s*Delete photo\\s*</button>", open);
        Assert.DoesNotMatch("btn-primary[^>]*>\\s*Delete", open);

        // The picker dialog's own primary (Add) is inside the dialog only.
        var dialog = Regex.Match(rest, "<dialog id=\"lib-picker\".*?</dialog>", RegexOptions.Singleline).Value;
        Assert.Matches("<button class=\"btn btn-primary\"[^>]*>\\s*Add\\s*</button>", dialog);
    }

    [Fact]
    public async Task Limits_come_from_LibraryOptions()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3, extraOverrides: new()
        {
            ["Library:TitleMax"] = "50",
            ["Library:PageSize"] = "20",
        });
        var img = await h.InsertUploadedImageAsync(files: false);

        var html = await GetOkAsync(h, $"/admin/library?open={img.Id}");
        var aside = Regex.Match(html, "<aside class=\"side-panel\".*?</aside>", RegexOptions.Singleline).Value;
        var title = Regex.Match(aside, "<input[^>]*name=\"title\"[^>]*>").Value;
        Assert.Contains("maxlength=\"50\"", title);
        Assert.Matches("\\srequired(\\s|=\"\"|/?>)", title);
        Assert.Contains("pattern=\".*\\S.*\"", title);

        var caption = Regex.Match(aside, "<textarea[^>]*name=\"caption\"[^>]*>").Value;
        Assert.Contains("maxlength=\"2000\"", caption);
        Assert.DoesNotMatch("\\srequired", caption);

        var bar = Regex.Match(html, "<div class=\"selection-bar\"[^>]*>").Value;
        Assert.Contains("data-limit=\"20\"", bar);
        Assert.Matches("\\shidden(\\s|=\"\"|>)", bar);

        var picker = Regex.Match(html, "<input[^>]*name=\"newTitle\"[^>]*>").Value;
        Assert.Contains("maxlength=\"50\"", picker);
    }

    [Fact]
    public async Task No_validation_nags_on_the_library()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3);
        var img = await h.InsertUploadedImageAsync(files: false, title: "IMG_0001");

        foreach (var path in new[] { "/admin/library", $"/admin/library?open={img.Id}" })
        {
            var text = LibraryHtml.Text(await GetOkAsync(h, path));
            foreach (var nag in new[] { "required", "missing", "No caption", "needs", "warning" })
                Assert.DoesNotContain(nag, text, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task Bulk_ids_above_PageSize_are_422_with_the_reason_and_store_nothing()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3, extraOverrides: new() { ["Library:PageSize"] = "2" });
        var ids = new List<string>();
        for (var i = 0; i < 3; i++) ids.Add((await h.InsertUploadedImageAsync(files: false)).Id);

        var add = await h.PostFormAsync("/admin/library/add",
            ids.Select(id => new KeyValuePair<string, string>("ids", id)).Append(new("gallery", "new")).Append(new("newTitle", "Coast")));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, add.StatusCode);
        Assert.Equal("Select at most 2 photos.", LibraryHtml.Status(await LibraryHtml.BodyAsync(add)));

        var del = await h.PostFormAsync("/admin/library/delete", ids.Select(id => new KeyValuePair<string, string>("ids", id)));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, del.StatusCode);
        Assert.Equal("Select at most 2 photos.", LibraryHtml.Status(await LibraryHtml.BodyAsync(del)));

        Assert.Equal(0, await h.Galleries.CountDocumentsAsync(FilterDefinition<GalleryDoc>.Empty));
        Assert.Equal(3, await h.Images.CountDocumentsAsync(i => i.DeletingAt == null));
        Assert.Empty(await h.SettleAsync());
    }

    [Fact]
    public async Task Picker_lists_galleries_in_home_order_with_visibility_count_and_the_public_note()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3);
        var i1 = await h.InsertUploadedImageAsync(files: false);
        var i2 = await h.InsertUploadedImageAsync(files: false);
        var priv = await GalleryAsync(h, "Studio", [i1.Id]);
        var pub = await GalleryAsync(h, "Coast", [i1.Id, i2.Id], publish: true);
        var empty = await GalleryAsync(h, "Empty", []);
        await h.SettleAsync();

        var html = await GetOkAsync(h, "/admin/library");
        var dialog = Regex.Match(html, "<dialog id=\"lib-picker\".*?</dialog>", RegexOptions.Singleline).Value;
        var options = LibraryHtml.Blocks(dialog, "label", "lib-option");

        Assert.Equal([priv, pub, empty, "new"],
            options.Select(o => Regex.Match(o, "name=\"gallery\"[^>]*value=\"([^\"]+)\"").Groups[1].Value).ToList());
        string Text(string o) => Regex.Match(o, "<span class=\"lib-option-text\"[^>]*>([^<]*)</span>").Groups[1].Value;
        Assert.Equal("Studio · Private · 1 photo", Text(options[0]));
        Assert.Equal("Coast · Public · 2 photos", Text(options[1]));
        Assert.Equal("Empty · Private · 0 photos", Text(options[2]));

        const string note = "Visitors will see these photos once they're added.";
        Assert.Contains(note, options[1]);
        Assert.DoesNotContain(note, options[0]);
        Assert.DoesNotContain(note, options[2]);
        Assert.Single(Regex.Matches(dialog, Regex.Escape(note)));
    }
}
