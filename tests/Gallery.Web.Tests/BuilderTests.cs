using System.Net;
using System.Text.RegularExpressions;
using Gallery.Core.Ids;
using Gallery.Web.Data;
using Gallery.Web.Tests.Fixtures;
using MongoDB.Driver;

namespace Gallery.Web.Tests;

/// <summary>
/// 009 T042 (US4; FR-021–FR-028, FR-050) and the T039 acceptance criteria (UI-1, UI-2, UI-3, UI-4,
/// UI-7, UI-8): the gallery builder B1 and its writes B2–B6 over HTTP through <see cref="LibraryHost"/>.
/// Each step is followed by <see cref="LibraryHost.SettleAsync"/> (edge queue drained,
/// <see cref="InvariantChecker"/>) and an EQUALITY check of the recorder's set (contracts/edge.md).
/// </summary>
[Collection("s3")]
public class BuilderTests(S3ProxyFixture s3, MongoFixture mongo) : IClassFixture<MongoFixture>
{
    private static KeyValuePair<string, string> F(string k, string v) => GalleryHtml.F(k, v);
    private static string Gp(string gid, string slug) => GalleryHtml.Gp(gid, slug);
    private static string Pp(string gid, string slug, string iid) => GalleryHtml.Pp(gid, slug, iid);
    private static readonly string Home = GalleryHtml.Home;

    /// <summary>The builder tile whose Remove button carries <paramref name="iid"/>.</summary>
    private static string TileOf(string html, string iid) =>
        LibraryHtml.Blocks(html, "div", "tile")
            .Single(t => Regex.IsMatch(t, $"<button\\b[^>]*name=\"image\"[^>]*value=\"{iid}\"[^>]*>Remove</button>"));

    private static string CoverText(string tile) =>
        Regex.Match(tile, "<span class=\"cover-tag[^\"]*\"[^>]*>([^<]*)</span>").Groups[1].Value;

    private static async Task<string?> HomeCoverAsync(LibraryHost h, string gid) =>
        (await h.SiteAsync()).HomeCards.SingleOrDefault(c => c.GalleryId == gid)?.Cover?.ImageId;

    [Fact]
    public async Task B1_renders_the_builder_and_an_unknown_gallery_is_404()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3, extraOverrides: new()
        {
            ["Library:TitleMax"] = "60",
            ["Library:DescriptionMax"] = "300",
        });
        var ids = await GalleryHtml.ImageIdsAsync(h, 2);
        var gid = await GalleryHtml.GalleryAsync(h, "Coast", ids);
        await h.SettleAsync();

        var html = await GalleryHtml.GetOkAsync(h, $"/admin/galleries/{gid}");
        Assert.Matches("<a href=\"/admin/galleries\" aria-current=\"page\"", html);
        Assert.Matches("<title[^>]*>Coast</title>", html);

        // UI-1: View gallery in the panel and View ↗ on every tile. 010 owner review 1 (stated change,
        // decisions row 47): the owner bar is the same on every admin page, so its link is View site ↗ to "/".
        Assert.Matches("<a class=\"btn btn-quiet\" href=\"/\"[^>]*>View site ↗</a>", html);
        Assert.Matches($"<a class=\"btn btn-quiet\" href=\"/g/{gid}/coast\"[^>]*>View gallery ↗</a>", html);
        foreach (var iid in ids)
        {
            var tile = TileOf(html, iid);
            // UI-2: handle, cover, Remove and View in .b-ctl under the title; nothing over the photo.
            var row = LibraryHtml.Blocks(tile, "div", "b-ctl").Single();
            var cell = LibraryHtml.Blocks(tile, "div", "cell").Single();
            Assert.True(tile.IndexOf(cell, StringComparison.Ordinal) + cell.Length <= tile.IndexOf(row, StringComparison.Ordinal));
            Assert.DoesNotContain("class=\"overlay\"", tile);
            Assert.Matches($"<a class=\"text-link\" href=\"/g/{gid}/coast/{iid}\"[^>]*>View ↗</a>", row);
            // UI-4: Remove is Quiet, posts b-remove directly, with no confirmation step.
            var remove = Regex.Match(row, "<button\\b[^>]*>Remove</button>").Value;
            Assert.Matches("class=\"btn btn-quiet\"", remove);
            Assert.Matches("type=\"submit\"[^>]*form=\"b-remove\"", remove);
            Assert.DoesNotContain("danger-confirm", row);
        }
        // Hidden forms: each per-item control posts an external form (no nesting).
        foreach (var (id, action) in new[] { ("b-order", "/order"), ("b-cover", "/cover"), ("b-unpin", "/cover"), ("b-remove", "/remove"), ("b-delete", "/delete") })
            Assert.Matches($"<form id=\"{id}\" method=\"post\" action=\"/admin/galleries/{gid}{action}\" hidden[ >]", html);
        Assert.Matches($"<form id=\"b-panel\" method=\"post\" action=\"/admin/galleries/{gid}\"[ >]", html);
        // The switch is a field of the Save form (owner feedback): no separate visibility form.
        Assert.DoesNotContain("<form id=\"b-vis\"", html);
        Assert.DoesNotContain("/visibility\"", html);
        Assert.Contains("<reorder-grid data-form=\"b-order\">", html);
        var order = Regex.Match(html, "<form id=\"b-order\"[^>]*>.*?</form>", RegexOptions.Singleline).Value;
        Assert.Equal(ids, Regex.Matches(order, "name=\"order\"[^>]*value=\"([^\"]+)\"").Select(m => m.Groups[1].Value).ToList());

        // UI-3: Save is the one primary. UI-4: Delete gallery… is outlined Danger on b-delete.
        Assert.Equal(["Save"], GalleryHtml.PrimaryButtons(html));
        Assert.Matches("<button [^>]*commandfor=\"b-delete-confirm\"[^>]*class=\"btn btn-danger danger-trigger\" type=\"button\"[^>]*>(Delete gallery…|Delete gallery&#x2026;)</button>", html);
        Assert.Matches("<dialog id=\"b-delete-confirm\" class=\"danger-dialog\"", html);
        Assert.Matches("<button class=\"btn btn-danger\"[^>]*form=\"b-delete\"[^>]*>\\s*Delete gallery\\s*</button>", html);
        Assert.Contains("Its 2 photos stay in your library. This can't be undone.", html);

        // UI-7: limits from LibraryOptions.
        var title = Regex.Match(html, "<input[^>]*name=\"title\"[^>]*>").Value;
        Assert.Contains("maxlength=\"60\"", title);
        Assert.True(GalleryHtml.HasAttr(title, "required"), title);
        Assert.Contains("pattern=\".*\\S.*\"", title);
        Assert.Contains("maxlength=\"300\"", Regex.Match(html, "<textarea[^>]*name=\"description\"[^>]*>").Value);

        // UI-8: no validation nags.
        var text = LibraryHtml.Text(html);
        foreach (var nag in new[] { "required", "missing", "No caption", "needs", "warning" })
            Assert.DoesNotContain(nag, text, StringComparison.OrdinalIgnoreCase);

        Assert.Equal(HttpStatusCode.NotFound, (await h.GetAsync($"/admin/galleries/{OpaqueId.NewId()}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await h.GetAsync("/admin/galleries/NOT-AN-ID")).StatusCode);
    }

    /// <summary>The value of the builder's title input.</summary>
    private static string TitleValue(string html) =>
        System.Net.WebUtility.HtmlDecode(Regex.Match(Regex.Match(html, "<input[^>]*name=\"title\"[^>]*>").Value, "value=\"([^\"]*)\"").Groups[1].Value);

    /// <summary>The text of the builder's description textarea.</summary>
    private static string DescriptionValue(string html) =>
        System.Net.WebUtility.HtmlDecode(Regex.Match(html, "<textarea[^>]*name=\"description\"[^>]*>(.*?)</textarea>", RegexOptions.Singleline).Groups[1].Value);

    private static string Aside(string html) =>
        Regex.Match(html, "<aside class=\"side-panel\".*?</aside>", RegexOptions.Singleline).Value;

    [Fact]
    public async Task The_switch_belongs_to_the_Save_form_and_the_warning_has_no_buttons()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3);
        var ids = await GalleryHtml.ImageIdsAsync(h, 3);
        var gid = await GalleryHtml.GalleryAsync(h, "Coast", ids);
        var pub = await GalleryHtml.GalleryAsync(h, "Shore", [ids[0]], publish: true);
        await h.SettleAsync();

        var html = await GalleryHtml.GetOkAsync(h, $"/admin/galleries/{gid}");
        var aside = Aside(html);
        var sw = GalleryHtml.SwitchOf(aside);
        Assert.Matches("\\sform=\"b-panel\"", sw);
        Assert.Matches("\\sname=\"public\"", sw);
        Assert.Matches("\\svalue=\"on\"", sw);
        Assert.False(GalleryHtml.HasAttr(sw, "checked"), sw);
        // The vis marker tells B2 to read the switch (an unchecked box sends nothing).
        Assert.Matches("<input type=\"hidden\" name=\"vis\" value=\"1\" form=\"b-panel\"", aside);

        // The Save-time warning is rendered for the private gallery (CSS shows it while the switch is
        // on) and holds no button; Save is the one primary.
        var warning = LibraryHtml.Blocks(aside, "div", "publish-warning").Single();
        Assert.Contains("on-save", warning);
        Assert.Contains("When you save, “Coast” becomes public: it will be listed on the home page with its 3 photos, and anyone can see them.",
            System.Net.WebUtility.HtmlDecode(warning));
        Assert.DoesNotContain("<button", warning);
        Assert.DoesNotContain("Make public", html);
        Assert.DoesNotContain("Keep private", html);
        Assert.DoesNotContain("name=\"confirm\"", html);
        Assert.Equal(["Save"], GalleryHtml.PrimaryButtons(html));
        Assert.Matches("<button class=\"btn btn-primary\"[^>]*form=\"b-panel\"[^>]*>\\s*Save\\s*</button>", html);

        // A public gallery: switch on, no warning (turning it off needs none).
        var pubHtml = await GalleryHtml.GetOkAsync(h, $"/admin/galleries/{pub}");
        Assert.True(GalleryHtml.HasAttr(GalleryHtml.SwitchOf(Aside(pubHtml)), "checked"));
        Assert.DoesNotContain("publish-warning", pubHtml);
        Assert.Empty(await h.SettleAsync());
    }

    [Fact]
    public async Task B2_saves_title_description_and_public_in_one_request()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3);
        var ids = await GalleryHtml.ImageIdsAsync(h, 2);
        var (a, b) = (ids[0], ids[1]);
        var gid = await GalleryHtml.GalleryAsync(h, "Coast", ids);
        await h.SettleAsync();

        var save = await h.PostFormAsync($"/admin/galleries/{gid}",
            [F("title", "Coastline"), F("description", "Winter walks."), F("vis", "1"), F("public", "on")]);
        Assert.Equal(HttpStatusCode.SeeOther, save.StatusCode);
        Assert.Equal($"/admin/galleries/{gid}?saved=1&visibility=public", save.Headers.Location?.ToString());
        var g = await GalleryHtml.LoadAsync(h, gid);
        Assert.Equal(("Coastline", "coastline", "Winter walks.", "public"), (g.Title, g.Slug, g.Description, g.Visibility));
        // P-VIS ∪ P-RENAME in one flush (ONE edge call): every page under both slugs and "/".
        await h.EdgeIdleAsync();
        Assert.Single(h.Recorder.Purges);
        GalleryHtml.SetEq(
        [
            Gp(gid, "coast"), Pp(gid, "coast", a), Pp(gid, "coast", b),
            Gp(gid, "coastline"), Pp(gid, "coastline", a), Pp(gid, "coastline", b),
            Home,
        ], await h.SettleAsync());
        Assert.Contains($"/g/{gid}/coastline", await GalleryHtml.HomeRawAsync(h));
        Assert.Single(h.LogLines, l => l.StartsWith($"owner.action gallery.publish {gid} ", StringComparison.Ordinal));
        Assert.Equal("Saved. The gallery is public",
            LibraryHtml.Status(await GalleryHtml.GetOkAsync(h, $"/admin/galleries/{gid}?saved=1&visibility=public")));
        h.Recorder.Clear();

        // Save with the switch off (vis=1, no public field) and a new description: unpublished, P-VIS.
        var off = await h.PostFormAsync($"/admin/galleries/{gid}",
            [F("title", "Coastline"), F("description", "Spring walks."), F("vis", "1")]);
        Assert.Equal(HttpStatusCode.SeeOther, off.StatusCode);
        Assert.Equal($"/admin/galleries/{gid}?saved=1&visibility=private", off.Headers.Location?.ToString());
        g = await GalleryHtml.LoadAsync(h, gid);
        Assert.Equal(("Coastline", "Spring walks.", "private"), (g.Title, g.Description, g.Visibility));
        GalleryHtml.SetEq([Gp(gid, "coastline"), Pp(gid, "coastline", a), Pp(gid, "coastline", b), Home], await h.SettleAsync());
        Assert.DoesNotContain($"/g/{gid}/coastline", await GalleryHtml.HomeRawAsync(h));
        Assert.Single(h.LogLines, l => l.StartsWith($"owner.action gallery.unpublish {gid} ", StringComparison.Ordinal));

        // Save with the switch as stored (off) and text unchanged: nothing written, nothing purged.
        var same = await h.PostFormAsync($"/admin/galleries/{gid}",
            [F("title", "Coastline"), F("description", "Spring walks."), F("vis", "1")]);
        Assert.Equal($"/admin/galleries/{gid}?saved=1", same.Headers.Location?.ToString());
        Assert.Empty(await h.SettleAsync());
    }

    [Fact]
    public async Task B2_with_a_too_long_title_and_public_on_rerenders_422_keeping_everything_typed()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3, extraOverrides: new() { ["Library:TitleMax"] = "60" });
        var ids = await GalleryHtml.ImageIdsAsync(h, 2);
        var gid = await GalleryHtml.GalleryAsync(h, "Coast", ids);
        await h.SettleAsync();
        var home = await GalleryHtml.HomeRawAsync(h);

        var longTitle = new string('t', 61);
        var resp = await h.PostFormAsync($"/admin/galleries/{gid}",
            [F("title", longTitle), F("description", "Typed notes."), F("vis", "1"), F("public", "on")]);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
        var html = await LibraryHtml.BodyAsync(resp);
        Assert.Equal("The title can be at most 60 characters.", LibraryHtml.Status(html));
        Assert.Equal(longTitle, TitleValue(html));
        Assert.Equal("Typed notes.", DescriptionValue(html));
        var aside = Aside(html);
        Assert.True(GalleryHtml.HasAttr(GalleryHtml.SwitchOf(aside), "checked"), "the switch keeps the owner's choice");
        Assert.Contains("on-save", LibraryHtml.Blocks(aside, "div", "publish-warning").Single());

        var g = await GalleryHtml.LoadAsync(h, gid);
        Assert.Equal(("Coast", (string?)null, "private"), (g.Title, g.Description, g.Visibility));
        Assert.Empty(await h.SettleAsync());
        Assert.Equal(home, await GalleryHtml.HomeRawAsync(h));
        Assert.DoesNotContain(h.LogLines, l => l.StartsWith($"owner.action gallery.publish {gid} ", StringComparison.Ordinal));
    }

    [Fact]
    public async Task B2_without_the_vis_marker_never_changes_visibility()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3);
        var ids = await GalleryHtml.ImageIdsAsync(h, 1);
        var priv = await GalleryHtml.GalleryAsync(h, "Coast", ids);
        var pub = await GalleryHtml.GalleryAsync(h, "Shore", ids, publish: true);
        await h.SettleAsync();

        // public=on without the marker: the text saves, the private gallery stays private.
        var on = await h.PostFormAsync($"/admin/galleries/{priv}", [F("title", "Coast"), F("description", "Notes."), F("public", "on")]);
        Assert.Equal($"/admin/galleries/{priv}?saved=1", on.Headers.Location?.ToString());
        Assert.Equal(("Notes.", "private"), ((await GalleryHtml.LoadAsync(h, priv)).Description, (await GalleryHtml.LoadAsync(h, priv)).Visibility));

        // No switch and no marker (e.g. an older form): the public gallery stays public.
        var off = await h.PostFormAsync($"/admin/galleries/{pub}", [F("title", "Shore"), F("description", "More.")]);
        Assert.Equal($"/admin/galleries/{pub}?saved=1", off.Headers.Location?.ToString());
        Assert.Equal("public", (await GalleryHtml.LoadAsync(h, pub)).Visibility);

        // A bodiless POST: nothing changes.
        var bare = await h.PostBareAsync($"/admin/galleries/{pub}", csrfHeader: true);
        Assert.Equal(HttpStatusCode.SeeOther, bare.StatusCode);
        Assert.Equal("public", (await GalleryHtml.LoadAsync(h, pub)).Visibility);
        await h.SettleAsync();
        Assert.DoesNotContain(h.LogLines, l => l.Contains("owner.action gallery.publish " + priv, StringComparison.Ordinal));
        Assert.DoesNotContain(h.LogLines, l => l.Contains("owner.action gallery.unpublish", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Order_and_cover_steps_set_the_home_card_cover()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3);
        var ids = await GalleryHtml.ImageIdsAsync(h, 3);
        var (a, b, c) = (ids[0], ids[1], ids[2]);
        var gid = await GalleryHtml.GalleryAsync(h, "Coast", ids, publish: true);
        await h.SettleAsync();
        Assert.Equal(a, await HomeCoverAsync(h, gid));

        // No pin: moving c first makes it the home-card cover. Every neighbour changed.
        var reorder = await h.PostFormAsync($"/admin/galleries/{gid}/order", [F("order", c), F("order", a), F("order", b)]);
        Assert.Equal(HttpStatusCode.SeeOther, reorder.StatusCode);
        Assert.Equal($"/admin/galleries/{gid}?ordered=1", reorder.Headers.Location?.ToString());
        GalleryHtml.SetEq([Gp(gid, "coast"), Pp(gid, "coast", a), Pp(gid, "coast", b), Pp(gid, "coast", c), Home], await h.SettleAsync());
        Assert.Equal(c, await HomeCoverAsync(h, gid));
        var html = await GalleryHtml.GetOkAsync(h, $"/admin/galleries/{gid}?ordered=1");
        Assert.Equal("Order saved", LibraryHtml.Status(html));
        Assert.Equal("Cover · first photo", CoverText(TileOf(html, c)));

        // Stale order → 409 with the reason, nothing stored.
        var stale = await h.PostFormAsync($"/admin/galleries/{gid}/order", [F("order", a), F("order", b)]);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal("This gallery changed in another tab. Reload to see the current order.", LibraryHtml.Status(await LibraryHtml.BodyAsync(stale)));
        Assert.Equal([c, a, b], (await GalleryHtml.LoadAsync(h, gid)).Items);
        Assert.Empty(await h.SettleAsync());

        // Make cover on b pins it: the control shows "Cover" and "Unpin"; the home card follows.
        var pin = await h.PostFormAsync($"/admin/galleries/{gid}/cover", [F("image", b)]);
        Assert.Equal(HttpStatusCode.SeeOther, pin.StatusCode);
        Assert.Equal($"/admin/galleries/{gid}", pin.Headers.Location?.ToString());
        GalleryHtml.SetEq([Gp(gid, "coast"), Home], await h.SettleAsync());
        Assert.Equal(b, (await GalleryHtml.LoadAsync(h, gid)).PinnedCover);
        Assert.Equal(b, await HomeCoverAsync(h, gid));
        html = await GalleryHtml.GetOkAsync(h, $"/admin/galleries/{gid}");
        var pinned = TileOf(html, b);
        Assert.Equal("Cover", CoverText(pinned));
        Assert.Matches("<button\\b[^>]*type=\"submit\"[^>]*form=\"b-unpin\"[^>]*>Unpin</button>", pinned);
        Assert.Matches($"<button\\b[^>]*form=\"b-cover\"[^>]*name=\"image\"[^>]*value=\"{c}\"[^>]*>Make cover</button>", TileOf(html, c));
        Assert.Equal("", CoverText(TileOf(html, c)));

        // Unpin (the b-unpin form carries no image) returns to "Cover · first photo".
        var unpin = await h.PostFormAsync($"/admin/galleries/{gid}/cover", []);
        Assert.Equal(HttpStatusCode.SeeOther, unpin.StatusCode);
        GalleryHtml.SetEq([Gp(gid, "coast"), Home], await h.SettleAsync());
        Assert.Null((await GalleryHtml.LoadAsync(h, gid)).PinnedCover);
        Assert.Equal(c, await HomeCoverAsync(h, gid));
        html = await GalleryHtml.GetOkAsync(h, $"/admin/galleries/{gid}");
        Assert.Equal("Cover · first photo", CoverText(TileOf(html, c)));
        Assert.DoesNotContain(">Unpin<", html);

        // Pinning a non-member is 422 with the reason.
        var other = await h.InsertUploadedImageAsync(files: false);
        var bad = await h.PostFormAsync($"/admin/galleries/{gid}/cover", [F("image", other.Id)]);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, bad.StatusCode);
        Assert.Equal("That photo is not in this gallery.", LibraryHtml.Status(await LibraryHtml.BodyAsync(bad)));
        Assert.Empty(await h.SettleAsync());
    }

    [Fact]
    public async Task Removing_photos_clears_the_pin_and_the_last_one_hides_a_public_gallery_until_a_photo_returns()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3);
        var ids = await GalleryHtml.ImageIdsAsync(h, 4);
        var (a, b, c, d) = (ids[0], ids[1], ids[2], ids[3]);
        var gid = await GalleryHtml.GalleryAsync(h, "Coast", [a, b, c], publish: true);
        var later = await GalleryHtml.GalleryAsync(h, "Later", [d], publish: true);
        Assert.True((await GalleryHtml.Gs(h, s => s.SetCoverAsync(gid, b))).IsOk);
        await h.SettleAsync();

        // Remove the pinned cover b: the pin is cleared, b stays in the library.
        var rb = await h.PostFormAsync($"/admin/galleries/{gid}/remove", [F("image", b)]);
        Assert.Equal(HttpStatusCode.SeeOther, rb.StatusCode);
        Assert.Equal($"/admin/galleries/{gid}?removed=1", rb.Headers.Location?.ToString());
        GalleryHtml.SetEq([Gp(gid, "coast"), Pp(gid, "coast", a), Pp(gid, "coast", b), Pp(gid, "coast", c), Home], await h.SettleAsync());
        var g = await GalleryHtml.LoadAsync(h, gid);
        Assert.Null(g.PinnedCover);
        Assert.Equal([a, c], g.Items);
        var bDoc = await h.Images.Find(i => i.Id == b).FirstAsync();
        Assert.Null(bDoc.DeletingAt);
        Assert.Empty(bDoc.GalleryIds);
        Assert.Equal(a, await HomeCoverAsync(h, gid));

        // Remove c, then the last photo a: still public, gone from "/", its page 404.
        Assert.Equal(HttpStatusCode.SeeOther, (await h.PostFormAsync($"/admin/galleries/{gid}/remove", [F("image", c)])).StatusCode);
        GalleryHtml.SetEq([Gp(gid, "coast"), Pp(gid, "coast", a), Pp(gid, "coast", c), Home], await h.SettleAsync());
        Assert.Equal(HttpStatusCode.SeeOther, (await h.PostFormAsync($"/admin/galleries/{gid}/remove", [F("image", a)])).StatusCode);
        // 010 P-NAV (stated change, decisions row 27): Coast is unlisted, so Later loses its prev link.
        GalleryHtml.SetEq([Gp(gid, "coast"), Pp(gid, "coast", a), Gp(later, "later"), Home], await h.SettleAsync());

        g = await GalleryHtml.LoadAsync(h, gid);
        Assert.Equal("public", g.Visibility);
        Assert.Empty(g.Items);
        var home = await GalleryHtml.HomeRawAsync(h);
        Assert.DoesNotContain($"/g/{gid}/", home);
        Assert.Contains($"/g/{later}/later", home);
        Assert.Equal(HttpStatusCode.NotFound, (await h.GetAsync($"/g/{gid}/coast")).StatusCode);
        var list = await GalleryHtml.GetOkAsync(h, "/admin/galleries");
        Assert.Equal("Public · hidden until it has a photo",
            Regex.Match(GalleryHtml.CardOf(list, gid), "<span class=\"gal-status\"[^>]*>([^<]*)</span>").Groups[1].Value);
        // An empty PUBLIC gallery keeps a working switch (checked, not disabled) so it can be unpublished.
        var sw = GalleryHtml.SwitchOf(GalleryHtml.CardOf(list, gid));
        Assert.True(GalleryHtml.HasAttr(sw, "checked"), sw);
        Assert.False(GalleryHtml.HasAttr(sw, "disabled"), sw);

        // The empty builder: the existing copy with the library link. 010 owner review 1 (stated change,
        // decisions row 47): no "Back to galleries" and no View link; the breadcrumb leads back.
        var builder = await GalleryHtml.GetOkAsync(h, $"/admin/galleries/{gid}");
        Assert.Matches("<p class=\"b-empty\"[^>]*>No photos in this gallery yet. <a href=\"/admin/library\"[^>]*>Add photos from the library</a></p>", builder);
        Assert.DoesNotContain("Back to galleries", builder);
        Assert.Matches("<nav class=\"breadcrumb\"[^>]*><ol[^>]*><li[^>]*><a class=\"text-link tap\" href=\"/admin/galleries\"", builder);
        Assert.DoesNotContain($"/g/{gid}", builder);

        // Adding a photo (library L3) brings it back at its home position with no publish.
        var add = await h.PostFormAsync("/admin/library/add", [F("ids", a), F("gallery", gid), F("page", "1")]);
        Assert.Equal(HttpStatusCode.SeeOther, add.StatusCode);
        // 010 P-NAV (stated change, decisions row 27): Coast is listed again, so Later regains its prev link.
        GalleryHtml.SetEq([Gp(gid, "coast"), Pp(gid, "coast", a), Gp(later, "later"), Home], await h.SettleAsync());
        home = await GalleryHtml.HomeRawAsync(h);
        var at = home.IndexOf($"/g/{gid}/coast", StringComparison.Ordinal);
        Assert.True(at >= 0 && at < home.IndexOf($"/g/{later}/later", StringComparison.Ordinal), "Coast is back before Later");
        Assert.Equal([gid, later], (await h.SiteAsync()).HomeCards.Select(x => x.GalleryId).ToList());
        // No publish happened on the way back: the one publish line is the original one.
        Assert.Single(h.LogLines, l => l.StartsWith($"owner.action gallery.publish {gid} ", StringComparison.Ordinal));
        Assert.Equal("public", (await GalleryHtml.LoadAsync(h, gid)).Visibility);
    }

    [Fact]
    public async Task Rename_redirects_the_old_address_and_a_bare_save_keeps_title_and_description()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3);
        var ids = await GalleryHtml.ImageIdsAsync(h, 2);
        var (a, b) = (ids[0], ids[1]);
        var gid = await GalleryHtml.GalleryAsync(h, "Coast", ids, publish: true);
        Assert.True((await GalleryHtml.Gs(h, s => s.UpdateTextAsync(gid, null, "Winter walks."))).IsOk);
        await h.SettleAsync();

        var save = await h.PostFormAsync($"/admin/galleries/{gid}", [F("title", "Coastline"), F("description", "Winter walks.")]);
        Assert.Equal(HttpStatusCode.SeeOther, save.StatusCode);
        Assert.Equal($"/admin/galleries/{gid}?saved=1", save.Headers.Location?.ToString());
        GalleryHtml.SetEq(
        [
            Gp(gid, "coast"), Pp(gid, "coast", a), Pp(gid, "coast", b),
            Gp(gid, "coastline"), Pp(gid, "coastline", a), Pp(gid, "coastline", b),
            Home,
        ], await h.SettleAsync());
        var old = await h.GetAsync($"/g/{gid}/coast");
        Assert.Equal(HttpStatusCode.MovedPermanently, old.StatusCode);
        Assert.Equal($"/g/{gid}/coastline", old.Headers.Location?.ToString());
        Assert.Equal(HttpStatusCode.OK, (await h.GetAsync($"/g/{gid}/coastline")).StatusCode);
        Assert.Equal("Saved", LibraryHtml.Status(await GalleryHtml.GetOkAsync(h, $"/admin/galleries/{gid}?saved=1")));

        // A bare POST (no body, CSRF header only) changes nothing and purges nothing.
        var bare = await h.PostBareAsync($"/admin/galleries/{gid}", csrfHeader: true);
        Assert.Equal(HttpStatusCode.SeeOther, bare.StatusCode);
        var g = await GalleryHtml.LoadAsync(h, gid);
        Assert.Equal(("Coastline", "coastline", "Winter walks."), (g.Title, g.Slug, g.Description));
        Assert.Empty(await h.SettleAsync());

        // A blank title is 422 with the reason, the submitted value kept in the input, nothing stored.
        var blank = await h.PostFormAsync($"/admin/galleries/{gid}", [F("title", "   "), F("description", "x")]);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, blank.StatusCode);
        Assert.Equal("Enter a title.", LibraryHtml.Status(await LibraryHtml.BodyAsync(blank)));
        g = await GalleryHtml.LoadAsync(h, gid);
        Assert.Equal(("Coastline", "Winter walks."), (g.Title, g.Description));
        Assert.Empty(await h.SettleAsync());
    }

    [Fact]
    public async Task Delete_gallery_keeps_its_photos_in_the_library_and_their_other_galleries()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3);
        var ids = await GalleryHtml.ImageIdsAsync(h, 2);
        var (a, b) = (ids[0], ids[1]);
        var gid = await GalleryHtml.GalleryAsync(h, "Coast", [a, b], publish: true);
        var other = await GalleryHtml.GalleryAsync(h, "Other", [a]);
        await h.SettleAsync();

        var del = await h.PostFormAsync($"/admin/galleries/{gid}/delete", []);
        Assert.Equal(HttpStatusCode.SeeOther, del.StatusCode);
        Assert.Equal("/admin/galleries?deleted=1", del.Headers.Location?.ToString());
        GalleryHtml.SetEq([Gp(gid, "coast"), Pp(gid, "coast", a), Pp(gid, "coast", b), Home], await h.SettleAsync());

        Assert.Equal(HttpStatusCode.NotFound, (await h.GetAsync($"/g/{gid}/coast")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await h.GetAsync($"/admin/galleries/{gid}")).StatusCode);
        Assert.Equal(0, await h.Galleries.CountDocumentsAsync(g => g.Id == gid));
        Assert.Equal([other], (await h.Images.Find(i => i.Id == a).FirstAsync()).GalleryIds);
        Assert.Empty((await h.Images.Find(i => i.Id == b).FirstAsync()).GalleryIds);
        Assert.Equal([a], (await GalleryHtml.LoadAsync(h, other)).Items);
        Assert.Equal([other], await GalleryHtml.HomeOrderAsync(h));
        Assert.Single(h.LogLines, l => l.StartsWith($"owner.action gallery.delete {gid} ", StringComparison.Ordinal));
        Assert.Equal("Gallery deleted", LibraryHtml.Status(await GalleryHtml.GetOkAsync(h, "/admin/galleries?deleted=1")));

        // Already deleted → 303 deleted=0, nothing purged.
        var again = await h.PostFormAsync($"/admin/galleries/{gid}/delete", []);
        Assert.Equal("/admin/galleries?deleted=0", again.Headers.Location?.ToString());
        Assert.Empty(await h.SettleAsync());
    }
}
