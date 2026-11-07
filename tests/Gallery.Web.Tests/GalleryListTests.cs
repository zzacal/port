using System.Net;
using System.Text.RegularExpressions;
using Gallery.Web.Data;
using Gallery.Web.Edge;
using Gallery.Web.Library;
using Gallery.Web.Tests.Fixtures;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Gallery.Web.Tests;

/// <summary>Shared helpers for the 009 Batch E HTTP tests (gallery list and builder).</summary>
internal static class GalleryHtml
{
    public static KeyValuePair<string, string> F(string k, string v) => new(k, v);

    public static Task<OperationResult> Gs(LibraryHost h, Func<GalleryService, Task<OperationResult>> call) => h.RunAsync(call);

    public static string Gp(string gid, string slug) => LibraryHost.Page(PurgeSets.GalleryPath(gid, slug));
    public static string Pp(string gid, string slug, string iid) => LibraryHost.Page(PurgeSets.PhotoPath(gid, slug, iid));
    public static readonly string Home = LibraryHost.Page("/g"); // 011: the listing moved to /g

    /// <summary>Asserts set EQUALITY (none missing, none extra) of purged addresses.</summary>
    public static void SetEq(IEnumerable<string> expected, IEnumerable<string> actual) =>
        Assert.Equal(expected.Distinct().Order(StringComparer.Ordinal).ToArray(), actual.Order(StringComparer.Ordinal).ToArray());

    /// <summary>Creates a gallery through the services (the way the owner does), adds photos, optionally publishes.</summary>
    public static async Task<string> GalleryAsync(LibraryHost h, string title, IReadOnlyList<string> ids, bool publish = false)
    {
        var created = await Gs(h, s => s.CreateAsync(title));
        Assert.True(created.IsOk, created.Reason);
        var gid = created.GalleryId!;
        if (ids.Count > 0) Assert.True((await Gs(h, s => s.AddAsync(gid, ids))).IsOk);
        if (publish) Assert.True((await Gs(h, s => s.PublishAsync(gid, confirmed: true))).IsOk);
        return gid;
    }

    public static async Task<List<string>> ImageIdsAsync(LibraryHost h, int n)
    {
        var ids = new List<string>();
        for (var i = 0; i < n; i++) ids.Add((await h.InsertUploadedImageAsync(DateTime.UtcNow.AddSeconds(i), files: false)).Id);
        return ids;
    }

    public static async Task<string> GetOkAsync(LibraryHost h, string path)
    {
        var resp = await h.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        return await LibraryHtml.BodyAsync(resp);
    }

    public static Task<GalleryDoc> LoadAsync(LibraryHost h, string gid) => h.Galleries.Find(g => g.Id == gid).FirstAsync();

    public static async Task<List<string>> HomeOrderAsync(LibraryHost h) =>
        (await h.Db.GetCollection<BsonDocument>("site").Find(new BsonDocument("_id", "site")).FirstAsync())
            ["homeOrder"].AsBsonArray.Select(v => v.AsString).ToList();

    /// <summary>The raw (undecoded) body of the listing <c>/g</c> (011), for byte comparison.</summary>
    public static async Task<string> HomeRawAsync(LibraryHost h) => await (await h.GetAsync("/g")).Content.ReadAsStringAsync();

    /// <summary>The gallery-list card for <paramref name="gid"/> (the tile whose Edit link names it).</summary>
    public static string CardOf(string html, string gid) =>
        Cards(html).Single(t => t.Contains($"href=\"/admin/galleries/{gid}\"", StringComparison.Ordinal));

    /// <summary>Every gallery-list card in page order (a cover Tile, or the empty .gal-tile).</summary>
    public static List<string> Cards(string html) =>
        Regex.Matches(html, "<div class=\"(tile|gal-tile)\\b[^\"]*\"[^>]*>")
            .Select(m => LibraryHtml.Element(html, "div", m.Index)).ToList();

    /// <summary>The public page's robots meta (scoped components add a b-xxxx attribute).</summary>
    public const string NoIndexMeta = "<meta name=\"robots\" content=\"noindex\"[^>]*>";

    /// <summary>The visibility switch input inside <paramref name="html"/>.</summary>
    public static string SwitchOf(string html)
    {
        var m = Regex.Match(html, "<input\\b[^>]*role=\"switch\"[^>]*>");
        Assert.True(m.Success, "no switch in: " + html);
        return m.Value;
    }

    /// <summary>
    /// 016 A2: the gallery-list card's visibility control inside <paramref name="html"/>: the
    /// <c>ga-vis</c> submit button, or the unavailable <c>ga-vis</c> span (exactly one).
    /// </summary>
    public static string VisibilityOf(string html)
    {
        var all = LibraryHtml.Blocks(html, "button", "ga-vis").Concat(LibraryHtml.Blocks(html, "span", "ga-vis")).ToList();
        Assert.True(all.Count == 1, $"{all.Count} visibility controls in: " + html);
        return all[0];
    }

    public static bool HasAttr(string tag, string name) => Regex.IsMatch(tag, $"\\s{Regex.Escape(name)}(=|\\s|/?>)");

    /// <summary>Text of every filled (btn-primary) control outside &lt;dialog&gt; elements.</summary>
    public static List<string> PrimaryButtons(string html) =>
        Regex.Matches(LibraryHtml.WithoutDialogs(html), "<(button|a|summary)\\b[^>]*class=\"btn btn-primary[^\"]*\"[^>]*>(.*?)</\\1>", RegexOptions.Singleline)
            .Select(m => Regex.Replace(m.Groups[2].Value, "<[^>]*>", "").Trim()).ToList();
}

/// <summary>
/// 009 T040 (US3; FR-018–FR-020, FR-029–FR-032, FR-050; UI-1, UI-2, UI-3, UI-5): the gallery list
/// G1 and its writes G2–G4 over HTTP through <see cref="LibraryHost"/>. Galleries are built through the
/// services; recorder sets are compared for EQUALITY (contracts/edge.md).
/// </summary>
[Collection("s3")]
public class GalleryListTests(S3ProxyFixture s3, MongoFixture mongo) : IClassFixture<MongoFixture>
{
    private static KeyValuePair<string, string> F(string k, string v) => GalleryHtml.F(k, v);

    [Fact]
    public async Task Owner_bar_marks_Galleries_current_and_cards_follow_home_order()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3);
        var ids = await GalleryHtml.ImageIdsAsync(h, 1);
        var a = await GalleryHtml.GalleryAsync(h, "Alpha", ids);
        var b = await GalleryHtml.GalleryAsync(h, "Bravo", []);
        await h.SettleAsync();

        var html = await GalleryHtml.GetOkAsync(h, "/admin/galleries");
        Assert.Matches("<a href=\"/admin/galleries\" aria-current=\"page\"", html);
        Assert.DoesNotMatch("<a href=\"/admin/library\"[^>]*aria-current", html);
        Assert.DoesNotMatch("<a href=\"/admin/upload\"[^>]*aria-current", html);

        var cards = GalleryHtml.Cards(html);
        Assert.Equal(2, cards.Count);
        Assert.Contains($"/admin/galleries/{a}\"", cards[0]);
        Assert.Contains($"/admin/galleries/{b}\"", cards[1]);

        // The hidden G3 form holds one order input per gallery, in home order, inside <reorder-grid data-form="gal-order">.
        var order = Regex.Match(html, "<form id=\"gal-order\"[^>]*>.*?</form>", RegexOptions.Singleline).Value;
        Assert.Equal([a, b], Regex.Matches(order, "name=\"order\"[^>]*value=\"([^\"]+)\"").Select(m => m.Groups[1].Value).ToList());
        Assert.Matches("<reorder-grid data-form=\"gal-order\"[^>]*>", html);
    }

    [Fact]
    public async Task Card_states_dimming_and_icon_row()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3);
        var ids = await GalleryHtml.ImageIdsAsync(h, 3);
        var pub = await GalleryHtml.GalleryAsync(h, "Coast", [ids[0]], publish: true);
        var priv = await GalleryHtml.GalleryAsync(h, "Studio", [ids[1]]);
        var hidden = await GalleryHtml.GalleryAsync(h, "Harbour", [ids[2]], publish: true);
        Assert.True((await GalleryHtml.Gs(h, s => s.RemoveAsync(hidden, ids[2]))).IsOk);
        var empty = await GalleryHtml.GalleryAsync(h, "Empty", []);
        await h.SettleAsync();

        var html = await GalleryHtml.GetOkAsync(h, "/admin/galleries");

        // 016 A1 (stated change, FR-004–FR-008): no status words, no switch, no text links; one icon
        // row per card (GalleryActions): move, visibility, view, edit. The state reads from the
        // visibility icon and its label (data-model.md).
        static string Tag(string el) => Regex.Match(el, "^<[^>]*>").Value;
        static string? Attr(string el, string name)
        {
            var m = Regex.Match(Tag(el), $"\\s{Regex.Escape(name)}=\"([^\"]*)\"");
            return m.Success ? m.Groups[1].Value : null;
        }
        static string IconOf(string el) => Regex.Match(el, "data-icon=\"([^\"]*)\"").Groups[1].Value;

        var expected = new Dictionary<string, (string Icon, string Label, bool Button, string? Public)>
        {
            [pub] = ("eye", "Public. Make private", true, null),
            [priv] = ("eye-off", "Private. Make public", true, "on"),
            [hidden] = ("eye", "Public, hidden until it has a photo. Make private", true, null),
            [empty] = ("eye-off", "Private. Add a photo before making this gallery public", false, null),
        };
        foreach (var (gid, want) in expected)
        {
            var vis = GalleryHtml.VisibilityOf(GalleryHtml.CardOf(html, gid));
            Assert.Equal(want.Icon, IconOf(vis));
            Assert.Equal(want.Label, Attr(vis, "aria-label"));
            Assert.Equal(want.Label, Attr(vis, "title"));
            Assert.Null(Attr(vis, "aria-pressed"));
            if (want.Button)
            {
                Assert.StartsWith("<button ", vis);
                Assert.Equal("submit", Attr(vis, "type"));
                Assert.Equal($"gal-vis-{gid}", Attr(vis, "form"));
                Assert.Equal(want.Public is null ? null : "public", Attr(vis, "name"));
                Assert.Equal(want.Public, Attr(vis, "value"));
            }
            else
            {
                Assert.StartsWith("<span ", vis);
                Assert.Equal("img", Attr(vis, "role"));
                Assert.Null(Attr(vis, "form"));
                Assert.Null(Attr(vis, "name"));
            }
        }
        Assert.Empty(LibraryHtml.Blocks(html, "span", "gal-status"));
        Assert.Empty(LibraryHtml.Blocks(html, "div", "gal-row"));
        Assert.DoesNotMatch("<input\\b[^>]*role=\"switch\"", string.Concat(GalleryHtml.Cards(html)));

        // Dimmed is-private on the private cards only.
        Assert.Matches("^<div class=\"tile is-private\"", GalleryHtml.CardOf(html, priv));
        Assert.Matches("^<div class=\"gal-tile tile-empty is-private\"", GalleryHtml.CardOf(html, empty));
        Assert.DoesNotContain("is-private", Regex.Match(GalleryHtml.CardOf(html, pub), "^<div[^>]*>").Value);
        Assert.DoesNotContain("is-private", Regex.Match(GalleryHtml.CardOf(html, hidden), "^<div[^>]*>").Value);

        // UI-1: the view icon links the public page (new tab) only when the gallery has photos; it is
        // unavailable in the same place otherwise. Edit links the builder on every card.
        foreach (var (gid, slug) in new[] { (pub, "coast"), (priv, "studio") })
        {
            var view = Assert.Single(LibraryHtml.Blocks(GalleryHtml.CardOf(html, gid), "a", "ga-view"));
            Assert.Equal($"/g/{gid}/{slug}", Attr(view, "href"));
            Assert.Equal("_blank", Attr(view, "target"));
            Assert.Equal("noopener", Attr(view, "rel"));
        }
        foreach (var gid in new[] { hidden, empty })
        {
            var card = GalleryHtml.CardOf(html, gid);
            Assert.Empty(LibraryHtml.Blocks(card, "a", "ga-view"));
            var view = Assert.Single(LibraryHtml.Blocks(card, "span", "ga-view"));
            Assert.Equal("No public page", Attr(view, "aria-label"));
            Assert.DoesNotContain("/g/", card);
        }

        // UI-2: the row sits under the cell, in the order move, visibility, view, edit (edit last);
        // the title is on the cover (or the blank cell's band); no overlay.
        foreach (var gid in new[] { pub, priv, hidden, empty })
        {
            var card = GalleryHtml.CardOf(html, gid);
            var row = LibraryHtml.Blocks(card, "div", "gallery-actions").Single();
            var cell = LibraryHtml.Blocks(card, "div", "cell").Single();
            Assert.True(card.IndexOf(cell, StringComparison.Ordinal) + cell.Length <= card.IndexOf(row, StringComparison.Ordinal));
            var parts = Regex.Matches(row, "<(?:button|a|span)\\b[^>]*\\sclass=\"([^\"]*)\"")
                .Select(m => m.Groups[1].Value.Split(' ').First(c => c == "handle" || c.StartsWith("ga-", StringComparison.Ordinal)))
                .ToList();
            Assert.Equal(["handle", "ga-vis", "ga-view", "ga-edit"], parts);
            var edit = Assert.Single(LibraryHtml.Blocks(row, "a", "ga-edit"));
            Assert.Equal($"/admin/galleries/{gid}", Attr(edit, "href"));
            Assert.DoesNotContain("class=\"overlay\"", card);
            Assert.DoesNotContain("pos-", card);
        }
        foreach (var gid in new[] { pub, priv })
            Assert.Contains("class=\"title\"", LibraryHtml.Blocks(LibraryHtml.Blocks(GalleryHtml.CardOf(html, gid), "div", "cell").Single(), "div", "on-photo").Single());
        foreach (var (gid, title) in new[] { (hidden, "Harbour"), (empty, "Empty") })
        {
            var band = Assert.Single(LibraryHtml.Blocks(LibraryHtml.Blocks(GalleryHtml.CardOf(html, gid), "div", "cell").Single(), "span", "gal-band"));
            Assert.Equal(title, Regex.Replace(band, "<[^>]*>", "").Trim());
            Assert.Equal(title, Attr(band, "title"));
        }
        Assert.DoesNotContain("checked=\"False\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("disabled=\"False\"", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task New_gallery_is_the_one_primary_and_the_dialog_holds_Create()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3, extraOverrides: new() { ["Library:TitleMax"] = "60" });
        var ids = await GalleryHtml.ImageIdsAsync(h, 1);
        var gid = await GalleryHtml.GalleryAsync(h, "Coast", ids);
        await h.SettleAsync();

        var html = await GalleryHtml.GetOkAsync(h, "/admin/galleries");
        Assert.Equal(["New gallery"], GalleryHtml.PrimaryButtons(html));
        var dialog = Regex.Match(html, "<dialog id=\"gal-new\".*?</dialog>", RegexOptions.Singleline).Value;
        Assert.Matches("<form method=\"post\" action=\"/admin/galleries\"", dialog);
        Assert.Matches("<button class=\"btn btn-primary\"[^>]*>\\s*Create\\s*</button>", dialog);
        var title = Regex.Match(dialog, "<input[^>]*name=\"title\"[^>]*>").Value;
        Assert.True(GalleryHtml.HasAttr(title, "required"), title);
        Assert.Contains("maxlength=\"60\"", title);
        Assert.Contains("pattern=\".*\\S.*\"", title);

        // No confirmation step on the list (owner feedback): the card has no publish warning and no
        // "Make public"; New gallery stays the one primary.
        var card = GalleryHtml.CardOf(html, gid);
        Assert.DoesNotContain("publish-warning", card);
        // 016 A10 (stated change): the confirm-step guard reads visible text; the eye's name
        // ("Private. Make public") is an attribute, not a button's text.
        var visible = Regex.Replace(html, "<[^>]*>", "");
        Assert.DoesNotContain("Make public", visible);
        Assert.DoesNotContain("Keep private", visible);
        Assert.DoesNotContain("name=\"vis\"", card);
        Assert.Empty(await h.SettleAsync());
    }

    [Fact]
    public async Task The_list_switch_publishes_at_once_with_no_confirmation_and_no_warning()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3);
        var ids = await GalleryHtml.ImageIdsAsync(h, 1);
        var gid = await GalleryHtml.GalleryAsync(h, "Coast", ids);
        await h.SettleAsync();

        // The form the switch submits: CSRF token, from=list and the switch (public=on). No confirm.
        var pub = await h.PostFormAsync($"/admin/galleries/{gid}/visibility", [F("public", "on"), F("from", "list")]);
        Assert.Equal(HttpStatusCode.SeeOther, pub.StatusCode);
        Assert.Equal("/admin/galleries?visibility=public", pub.Headers.Location?.ToString());
        Assert.Equal("public", (await GalleryHtml.LoadAsync(h, gid)).Visibility);
        GalleryHtml.SetEq([GalleryHtml.Pp(gid, "coast", ids[0]), GalleryHtml.Gp(gid, "coast"), GalleryHtml.Home], await h.SettleAsync());

        var after = await GalleryHtml.GetOkAsync(h, "/admin/galleries?visibility=public");
        Assert.Equal("The gallery is public", LibraryHtml.Status(after));
        Assert.Equal(["New gallery"], GalleryHtml.PrimaryButtons(after));
        Assert.DoesNotContain("publish-warning", after);
    }

    [Fact]
    public async Task G2_creates_a_private_gallery_last_in_home_order_with_no_purge_and_a_bad_title_is_422()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3);
        var first = await GalleryHtml.GalleryAsync(h, "First", []);
        await h.SettleAsync();

        var resp = await h.PostFormAsync("/admin/galleries", [F("title", "Coast")]);
        Assert.Equal(HttpStatusCode.SeeOther, resp.StatusCode);
        var loc = resp.Headers.Location!.ToString();
        Assert.Matches("^/admin/galleries/[0-9a-z]{26}$", loc);
        var gid = loc["/admin/galleries/".Length..];
        var g = await GalleryHtml.LoadAsync(h, gid);
        Assert.Equal(("Coast", "private"), (g.Title, g.Visibility));
        Assert.Empty(g.Items);
        Assert.Equal([first, gid], await GalleryHtml.HomeOrderAsync(h));
        Assert.Empty(await h.SettleAsync());

        var bad = await h.PostFormAsync("/admin/galleries", [F("title", "   ")]);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, bad.StatusCode);
        Assert.Equal("Enter a title.", LibraryHtml.Status(await LibraryHtml.BodyAsync(bad)));
        Assert.Equal(2, await h.Galleries.CountDocumentsAsync(FilterDefinition<GalleryDoc>.Empty));
    }

    [Fact]
    public async Task G3_stores_public_and_private_alike_purges_home_only_when_listed_order_changes_and_stale_is_409()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3);
        var ids = await GalleryHtml.ImageIdsAsync(h, 2);
        var a = await GalleryHtml.GalleryAsync(h, "Alpha", [ids[0]], publish: true);
        var b = await GalleryHtml.GalleryAsync(h, "Bravo", [ids[1]], publish: true);
        var p = await GalleryHtml.GalleryAsync(h, "Private", []);
        await h.SettleAsync();

        Task<HttpResponseMessage> Order(params string[] order) =>
            h.PostFormAsync("/admin/galleries/order", order.Select(id => F("order", id)));

        // Moving only the private gallery: stored, no purge.
        var r1 = await Order(p, a, b);
        Assert.Equal(HttpStatusCode.SeeOther, r1.StatusCode);
        Assert.Equal("/admin/galleries?ordered=1", r1.Headers.Location?.ToString());
        Assert.Equal([p, a, b], await GalleryHtml.HomeOrderAsync(h));
        Assert.Empty(await h.SettleAsync());

        // Swapping the listed galleries: stored, "/" plus both gallery pages (010 P-NAV, stated
        // change, decisions row 27: their neighbour links swap).
        var r2 = await Order(p, b, a);
        Assert.Equal(HttpStatusCode.SeeOther, r2.StatusCode);
        Assert.Equal([p, b, a], await GalleryHtml.HomeOrderAsync(h));
        GalleryHtml.SetEq([GalleryHtml.Home, GalleryHtml.Gp(a, "alpha"), GalleryHtml.Gp(b, "bravo")], await h.SettleAsync());

        // Stale (another tab added a gallery): 409 with the reason, nothing stored, nothing purged.
        var stale = await Order(a, b);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal("The gallery list changed in another tab. Reload to see the current order.",
            LibraryHtml.Status(await LibraryHtml.BodyAsync(stale)));
        Assert.Equal([p, b, a], await GalleryHtml.HomeOrderAsync(h));
        Assert.Empty(await h.SettleAsync());

        // The PRG status on the list.
        Assert.Equal("Order saved", LibraryHtml.Status(await GalleryHtml.GetOkAsync(h, "/admin/galleries?ordered=1")));
    }

    [Fact]
    public async Task Publish_from_the_list_and_unpublish_hide_at_once_keep_home_order_and_log_owner_actions()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3);
        var ids = await GalleryHtml.ImageIdsAsync(h, 2);
        var other = await GalleryHtml.GalleryAsync(h, "Other", [], publish: false);
        var gid = await GalleryHtml.GalleryAsync(h, "Coast", ids);
        await h.SettleAsync();
        var orderBefore = await GalleryHtml.HomeOrderAsync(h);

        // Publish from the list (the switch, no confirm) → 303 to the list with ?visibility=public, P-VIS.
        var pub = await h.PostFormAsync($"/admin/galleries/{gid}/visibility", [F("public", "on"), F("from", "list")]);
        Assert.Equal(HttpStatusCode.SeeOther, pub.StatusCode);
        Assert.Equal("/admin/galleries?visibility=public", pub.Headers.Location?.ToString());
        Assert.Equal("public", (await GalleryHtml.LoadAsync(h, gid)).Visibility);
        var pvis = ids.Select(i => GalleryHtml.Pp(gid, "coast", i)).Append(GalleryHtml.Gp(gid, "coast")).Append(GalleryHtml.Home).ToList();
        GalleryHtml.SetEq(pvis, await h.SettleAsync());
        Assert.Contains($"/g/{gid}/coast", await GalleryHtml.HomeRawAsync(h));
        var publicPage = await (await h.GetAsync($"/g/{gid}/coast")).Content.ReadAsStringAsync();
        Assert.DoesNotMatch(GalleryHtml.NoIndexMeta, publicPage);

        // Unpublish (switch off: no public field) → gone from "/" at once, homeOrder unchanged, noindex, P-VIS.
        var unpub = await h.PostFormAsync($"/admin/galleries/{gid}/visibility", [F("from", "list")]);
        Assert.Equal(HttpStatusCode.SeeOther, unpub.StatusCode);
        Assert.Equal("/admin/galleries?visibility=private", unpub.Headers.Location?.ToString());
        Assert.Equal("private", (await GalleryHtml.LoadAsync(h, gid)).Visibility);
        GalleryHtml.SetEq(pvis, await h.SettleAsync());
        Assert.DoesNotContain($"/g/{gid}/coast", await GalleryHtml.HomeRawAsync(h));
        Assert.Equal(orderBefore, await GalleryHtml.HomeOrderAsync(h));
        var privatePage = await h.GetAsync($"/g/{gid}/coast");
        Assert.Equal(HttpStatusCode.OK, privatePage.StatusCode);
        Assert.Matches(GalleryHtml.NoIndexMeta, await privatePage.Content.ReadAsStringAsync());

        Assert.Single(h.LogLines, l => l.StartsWith($"owner.action gallery.publish {gid} ", StringComparison.Ordinal));
        Assert.Single(h.LogLines, l => l.StartsWith($"owner.action gallery.unpublish {gid} ", StringComparison.Ordinal));
        Assert.Equal("The gallery is private", LibraryHtml.Status(await GalleryHtml.GetOkAsync(h, "/admin/galleries?visibility=private")));
        _ = other;
    }
}
