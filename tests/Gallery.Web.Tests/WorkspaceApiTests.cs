using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Gallery.Core.Ids;
using Gallery.Web.Admin.Pages;
using Gallery.Web.Data;
using Gallery.Web.Library;
using Gallery.Web.Tests.Fixtures;
using Gallery.Web.Workspace;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Gallery.Web.Tests;

/// <summary>
/// 020 T016 [SAFETY] (contracts/http.md "Writes", A1–A11; tasks.md T016): every A-route's status,
/// JSON shape and exact message over HTTP through <see cref="LibraryHost"/>, with JSON compared for
/// EQUALITY. Carries the TI-62 facts of the retired page tests with their stored and purge
/// assertions copied verbatim (GalleryListTests G2/G3/publish → A3/A4/A6; BuilderTests B2 rename,
/// order/cover, remove, delete → A5, A9/A10, A8, A11; LibraryPageTests bulk cap → A2/A3/A7/A8).
/// Purged sets are fixed literal sets; no expectation is computed by calling an old route.
/// </summary>
[Collection("s3")]
public class WorkspaceApiTests(S3ProxyFixture s3, MongoFixture mongo) : IClassFixture<MongoFixture>
{
    private static string Gp(string gid, string slug) => GalleryHtml.Gp(gid, slug);
    private static string Pp(string gid, string slug, string iid) => GalleryHtml.Pp(gid, slug, iid);
    private static readonly string Home = GalleryHtml.Home;
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    /// <summary>Asserts the status and that the JSON body EQUALS <paramref name="expected"/> (every field, no extra).</summary>
    private static async Task<JsonObject> AssertJsonAsync(HttpResponseMessage resp, HttpStatusCode status, object expected)
    {
        var text = await resp.Content.ReadAsStringAsync();
        Assert.True(status == resp.StatusCode, $"expected {(int)status}, got {(int)resp.StatusCode}: {text}");
        var actual = JsonNode.Parse(text)!.AsObject();
        var want = JsonSerializer.SerializeToNode(expected, Web)!;
        Assert.True(JsonNode.DeepEquals(want, actual), $"expected {want.ToJsonString()}\n  actual {actual.ToJsonString()}");
        return actual;
    }

    private static object Err(string code, string message) => new { error = code, message };

    private static async Task<HttpResponseMessage> PostRawAsync(LibraryHost h, string path, string json)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, path) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        req.Headers.TryAddWithoutValidation("X-CSRF-TOKEN", h.CsrfToken);
        return await h.Host.SendAsync(req);
    }

    private static async Task<string?> HomeCoverAsync(LibraryHost h, string gid) =>
        (await h.SiteAsync()).HomeCards.SingleOrDefault(c => c.GalleryId == gid)?.Cover?.ImageId;

    private static List<string> Snapshot(LibraryHost h)
    {
        lock (h.LogLines) return h.LogLines.ToList();
    }

    private static int Count(LibraryHost h, string prefix) =>
        Snapshot(h).Count(l => l.StartsWith(prefix, StringComparison.Ordinal));

    private static async Task<string> DumpAsync(LibraryHost h)
    {
        var parts = new List<string>();
        foreach (var name in new[] { "images", "galleries", "site" })
        {
            var docs = await h.Db.GetCollection<BsonDocument>(name).Find(FilterDefinition<BsonDocument>.Empty)
                .Sort(Builders<BsonDocument>.Sort.Ascending("_id")).ToListAsync();
            parts.Add(name + ":" + string.Join("\n", docs.Select(d => d.ToJson())));
        }
        return string.Join("\n---\n", parts);
    }

    // ── A1 ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A1_saves_title_and_caption_and_an_absent_field_keeps_the_stored_value()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3);
        var img = await h.InsertUploadedImageAsync(files: false, title: "Pier");

        await AssertJsonAsync(await h.PostJsonAsync($"/admin/api/photos/{img.Id}", new { title = " Pier at dusk ", caption = "Low tide" }),
            HttpStatusCode.OK, new { title = "Pier at dusk", caption = "Low tide", message = "Saved" });
        await AssertJsonAsync(await h.PostJsonAsync($"/admin/api/photos/{img.Id}", new { caption = "" }),
            HttpStatusCode.OK, new { title = "Pier at dusk", caption = (string?)null, message = "Saved" });
        await AssertJsonAsync(await h.PostJsonAsync($"/admin/api/photos/{img.Id}", new { }),
            HttpStatusCode.OK, new { title = "Pier at dusk", caption = (string?)null, message = "Saved" });

        await AssertJsonAsync(await h.PostJsonAsync($"/admin/api/photos/{img.Id}", new { title = "   ", caption = "x" }),
            HttpStatusCode.UnprocessableEntity, Err("refused", "Enter a title."));
        var stored = await h.Images.Find(i => i.Id == img.Id).FirstAsync();
        Assert.Equal(("Pier at dusk", (string?)null), (stored.Title, stored.Caption));

        await AssertJsonAsync(await h.PostJsonAsync($"/admin/api/photos/{OpaqueId.NewId()}", new { title = "x" }),
            HttpStatusCode.NotFound, Err("not-found", "That photo no longer exists."));
        await AssertJsonAsync(await h.PostJsonAsync("/admin/api/photos/NOT-AN-ID", new { title = "x" }),
            HttpStatusCode.NotFound, Err("not-found", "That photo no longer exists."));
    }

    // ── A2 ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A2_deletes_the_photos_and_says_how_many()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3);
        var a = await h.InsertUploadedImageAsync(title: "A");
        var b = await h.InsertUploadedImageAsync(title: "B");
        var c = await h.InsertUploadedImageAsync(title: "C");

        await AssertJsonAsync(await h.PostJsonAsync("/admin/api/photos/delete", new { ids = new[] { a.Id, b.Id } }),
            HttpStatusCode.OK, new { deleted = 2, leftovers = 0, failed = 0, message = "2 photos deleted." });
        await AssertJsonAsync(await h.PostJsonAsync("/admin/api/photos/delete", new { ids = new[] { c.Id } }),
            HttpStatusCode.OK, new { deleted = 1, leftovers = 0, failed = 0, message = "1 photo deleted." });
        Assert.Equal(0, await h.Images.CountDocumentsAsync(FilterDefinition<ImageDoc>.Empty));
        await h.SettleAsync();
    }

    // ── A3 (TI-62: GalleryListTests.G2_creates_a_private_gallery_last_in_home_order…) ──

    [Fact]
    public async Task A3_creates_a_private_gallery_last_in_home_order_with_no_purge_and_a_bad_title_is_422()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3);
        var first = await GalleryHtml.GalleryAsync(h, "First", []);
        await h.SettleAsync();

        var resp = await h.PostJsonAsync("/admin/api/galleries", new { title = "Coast" });
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        var gid = JsonNode.Parse(await resp.Content.ReadAsStringAsync())!["id"]!.GetValue<string>();
        Assert.Matches("^[0-9a-z]{26}$", gid);
        await AssertJsonAsync(await Replay(resp), HttpStatusCode.Created,
            new { id = gid, slug = "coast", title = "Coast", added = 0, message = "Coast created. It is private." });
        var g = await GalleryHtml.LoadAsync(h, gid);
        Assert.Equal(("Coast", "private"), (g.Title, g.Visibility));
        Assert.Empty(g.Items);
        Assert.Equal([first, gid], await GalleryHtml.HomeOrderAsync(h));
        Assert.Empty(await h.SettleAsync());

        await AssertJsonAsync(await h.PostJsonAsync("/admin/api/galleries", new { title = "   " }),
            HttpStatusCode.UnprocessableEntity, Err("refused", "Enter a title."));
        Assert.Equal(2, await h.Galleries.CountDocumentsAsync(FilterDefinition<GalleryDoc>.Empty));
    }

    /// <summary>The same response again (its content read once already).</summary>
    private static async Task<HttpResponseMessage> Replay(HttpResponseMessage resp) =>
        new(resp.StatusCode) { Content = new StringContent(await resp.Content.ReadAsStringAsync(), Encoding.UTF8, "application/json") };

    [Fact]
    public async Task A3_with_ids_creates_the_private_gallery_holding_them()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3);
        var ids = await GalleryHtml.ImageIdsAsync(h, 2);

        var resp = await h.PostJsonAsync("/admin/api/galleries", new { title = "Coast", ids });
        var gid = JsonNode.Parse(await resp.Content.ReadAsStringAsync())!["id"]!.GetValue<string>();
        await AssertJsonAsync(await Replay(resp), HttpStatusCode.Created,
            new { id = gid, slug = "coast", title = "Coast", added = 2, message = "Coast created with 2 photos. It is private." });
        var g = await GalleryHtml.LoadAsync(h, gid);
        Assert.Equal(("Coast", "private"), (g.Title, g.Visibility));
        Assert.Equal(ids, g.Items);
        Assert.Equal([gid], await GalleryHtml.HomeOrderAsync(h));
        // P-MEMBERS of the new private gallery (its noindex pages exist).
        GalleryHtml.SetEq([Gp(gid, "coast"), Pp(gid, "coast", ids[0]), Pp(gid, "coast", ids[1])], await h.SettleAsync());
    }

    // ── A4 (TI-62: GalleryListTests.G3_stores_public_and_private_alike…) ──────

    [Fact]
    public async Task A4_stores_public_and_private_alike_purges_home_only_when_listed_order_changes_and_stale_is_409()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3);
        var ids = await GalleryHtml.ImageIdsAsync(h, 2);
        var a = await GalleryHtml.GalleryAsync(h, "Alpha", [ids[0]], publish: true);
        var b = await GalleryHtml.GalleryAsync(h, "Bravo", [ids[1]], publish: true);
        var p = await GalleryHtml.GalleryAsync(h, "Private", []);
        await h.SettleAsync();

        Task<HttpResponseMessage> Order(params string[] order) => h.PostJsonAsync("/admin/api/galleries/order", new { order });

        // Moving only the private gallery: stored, no purge.
        await AssertJsonAsync(await Order(p, a, b), HttpStatusCode.OK, new { message = "Gallery order saved." });
        Assert.Equal([p, a, b], await GalleryHtml.HomeOrderAsync(h));
        Assert.Empty(await h.SettleAsync());

        // Swapping the listed galleries: stored, "/" plus both gallery pages (010 P-NAV).
        await AssertJsonAsync(await Order(p, b, a), HttpStatusCode.OK, new { message = "Gallery order saved." });
        Assert.Equal([p, b, a], await GalleryHtml.HomeOrderAsync(h));
        GalleryHtml.SetEq([GalleryHtml.Home, GalleryHtml.Gp(a, "alpha"), GalleryHtml.Gp(b, "bravo")], await h.SettleAsync());

        // Stale (another tab added a gallery): 409 with the reason, nothing stored, nothing purged.
        await AssertJsonAsync(await Order(a, b), HttpStatusCode.Conflict,
            Err("stale", "The gallery list changed in another tab. Reload to see the current order."));
        Assert.Equal([p, b, a], await GalleryHtml.HomeOrderAsync(h));
        Assert.Empty(await h.SettleAsync());
    }

    // ── A5 (TI-62: BuilderTests.Rename_redirects…, B2_with_a_too_long_title…) ──

    [Fact]
    public async Task A5_rename_redirects_the_old_address_and_an_empty_body_keeps_title_and_description()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3);
        var ids = await GalleryHtml.ImageIdsAsync(h, 2);
        var (a, b) = (ids[0], ids[1]);
        var gid = await GalleryHtml.GalleryAsync(h, "Coast", ids, publish: true);
        Assert.True((await GalleryHtml.Gs(h, s => s.UpdateTextAsync(gid, null, "Winter walks."))).IsOk);
        await h.SettleAsync();

        await AssertJsonAsync(await h.PostJsonAsync($"/admin/api/galleries/{gid}", new { title = "Coastline", description = "Winter walks." }),
            HttpStatusCode.OK, new { title = "Coastline", slug = "coastline", description = "Winter walks.", message = "Saved" });
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

        // An empty body changes nothing and purges nothing.
        await AssertJsonAsync(await h.PostJsonAsync($"/admin/api/galleries/{gid}", new { }),
            HttpStatusCode.OK, new { title = "Coastline", slug = "coastline", description = "Winter walks.", message = "Saved" });
        var g = await GalleryHtml.LoadAsync(h, gid);
        Assert.Equal(("Coastline", "coastline", "Winter walks."), (g.Title, g.Slug, g.Description));
        Assert.Empty(await h.SettleAsync());

        // A blank title is 422 with the reason, nothing stored.
        await AssertJsonAsync(await h.PostJsonAsync($"/admin/api/galleries/{gid}", new { title = "   ", description = "x" }),
            HttpStatusCode.UnprocessableEntity, Err("refused", "Enter a title."));
        g = await GalleryHtml.LoadAsync(h, gid);
        Assert.Equal(("Coastline", "Winter walks."), (g.Title, g.Description));
        Assert.Empty(await h.SettleAsync());

        await AssertJsonAsync(await h.PostJsonAsync($"/admin/api/galleries/{OpaqueId.NewId()}", new { title = "x" }),
            HttpStatusCode.NotFound, Err("not-found", "That gallery no longer exists."));
    }

    [Fact]
    public async Task A5_with_a_too_long_title_is_422_and_stores_nothing()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3, extraOverrides: new() { ["Library:TitleMax"] = "60" });
        var ids = await GalleryHtml.ImageIdsAsync(h, 2);
        var gid = await GalleryHtml.GalleryAsync(h, "Coast", ids);
        await h.SettleAsync();
        var home = await GalleryHtml.HomeRawAsync(h);

        await AssertJsonAsync(await h.PostJsonAsync($"/admin/api/galleries/{gid}", new { title = new string('t', 61), description = "Typed notes." }),
            HttpStatusCode.UnprocessableEntity, Err("refused", "The title can be at most 60 characters."));
        var g = await GalleryHtml.LoadAsync(h, gid);
        Assert.Equal(("Coast", (string?)null, "private"), (g.Title, g.Description, g.Visibility));
        Assert.Empty(await h.SettleAsync());
        Assert.Equal(home, await GalleryHtml.HomeRawAsync(h));
    }

    // ── A6 (TI-62: GalleryListTests.Publish_from_the_list_and_unpublish…) [SAFETY] II.6 ──

    [Fact]
    public async Task A6_publish_and_unpublish_hide_at_once_keep_home_order_and_log_owner_actions()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3);
        var ids = await GalleryHtml.ImageIdsAsync(h, 2);
        var other = await GalleryHtml.GalleryAsync(h, "Other", [], publish: false);
        var gid = await GalleryHtml.GalleryAsync(h, "Coast", ids);
        await h.SettleAsync();
        var orderBefore = await GalleryHtml.HomeOrderAsync(h);

        await AssertJsonAsync(await h.PostJsonAsync($"/admin/api/galleries/{gid}/visibility", new { @public = true }),
            HttpStatusCode.OK, new { @public = true, message = "Coast is now public. Visitors can see it." });
        Assert.Equal("public", (await GalleryHtml.LoadAsync(h, gid)).Visibility);
        var pvis = ids.Select(i => GalleryHtml.Pp(gid, "coast", i)).Append(GalleryHtml.Gp(gid, "coast")).Append(GalleryHtml.Home).ToList();
        GalleryHtml.SetEq(pvis, await h.SettleAsync());
        Assert.Contains($"/g/{gid}/coast", await GalleryHtml.HomeRawAsync(h));
        var publicPage = await (await h.GetAsync($"/g/{gid}/coast")).Content.ReadAsStringAsync();
        Assert.DoesNotMatch(GalleryHtml.NoIndexMeta, publicPage);

        await AssertJsonAsync(await h.PostJsonAsync($"/admin/api/galleries/{gid}/visibility", new { @public = false }),
            HttpStatusCode.OK, new { @public = false, message = "Coast is now private." });
        Assert.Equal("private", (await GalleryHtml.LoadAsync(h, gid)).Visibility);
        GalleryHtml.SetEq(pvis, await h.SettleAsync());
        Assert.DoesNotContain($"/g/{gid}/coast", await GalleryHtml.HomeRawAsync(h));
        Assert.Equal(orderBefore, await GalleryHtml.HomeOrderAsync(h));
        var privatePage = await h.GetAsync($"/g/{gid}/coast");
        Assert.Equal(HttpStatusCode.OK, privatePage.StatusCode);
        Assert.Matches(GalleryHtml.NoIndexMeta, await privatePage.Content.ReadAsStringAsync());

        Assert.Single(h.LogLines, l => l.StartsWith($"owner.action gallery.publish {gid} ", StringComparison.Ordinal));
        Assert.Single(h.LogLines, l => l.StartsWith($"owner.action gallery.unpublish {gid} ", StringComparison.Ordinal));
        _ = other;
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A6_empty_private_gallery_is_409_EmptyPublishReason_whatever_is_sent_and_nothing_changes(bool wantPublic)
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3);
        var gid = await GalleryHtml.GalleryAsync(h, "Empty", []);
        await h.SettleAsync();
        var before = await DumpAsync(h);

        await AssertJsonAsync(await h.PostJsonAsync($"/admin/api/galleries/{gid}/visibility", new { @public = wantPublic }),
            HttpStatusCode.Conflict, Err("refused", GalleryService.EmptyPublishReason));

        await h.EdgeIdleAsync();
        Assert.Equal(before, await DumpAsync(h));
        Assert.Empty(h.Recorder.AllPurged());
        Assert.DoesNotContain(Snapshot(h), l => l.StartsWith("owner.action gallery.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A6_public_false_on_a_private_gallery_with_photos_is_200_with_no_write()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3);
        var ids = await GalleryHtml.ImageIdsAsync(h, 1);
        var gid = await GalleryHtml.GalleryAsync(h, "Coast", ids);
        await h.SettleAsync();
        var before = await DumpAsync(h);

        await AssertJsonAsync(await h.PostJsonAsync($"/admin/api/galleries/{gid}/visibility", new { @public = false }),
            HttpStatusCode.OK, new { @public = false, message = "Coast is now private." });
        await h.EdgeIdleAsync();
        Assert.Equal(before, await DumpAsync(h));
        Assert.Empty(h.Recorder.AllPurged());

        await AssertJsonAsync(await h.PostJsonAsync($"/admin/api/galleries/{OpaqueId.NewId()}/visibility", new { @public = true }),
            HttpStatusCode.NotFound, Err("not-found", "That gallery no longer exists."));
    }

    // ── A7 ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A7_adds_photos_and_says_what_happened_and_who_can_see_them()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3);
        var ids = await GalleryHtml.ImageIdsAsync(h, 4);
        var (a, b, c, d) = (ids[0], ids[1], ids[2], ids[3]);
        var priv = await GalleryHtml.GalleryAsync(h, "Coast", []);
        var pub = await GalleryHtml.GalleryAsync(h, "Shore", [d], publish: true);
        await h.SettleAsync();

        Task<HttpResponseMessage> Add(string gid, params string[] add) => h.PostJsonAsync($"/admin/api/galleries/{gid}/photos", new { ids = add });

        await AssertJsonAsync(await Add(priv, a, b), HttpStatusCode.OK,
            new { added = 2, skipped = 0, @public = false, message = "2 photos added to Coast." });
        await AssertJsonAsync(await Add(priv, a, c), HttpStatusCode.OK,
            new { added = 1, skipped = 1, @public = false, message = "1 photo added to Coast. 1 were already in it." });
        await AssertJsonAsync(await Add(priv, a), HttpStatusCode.OK,
            new { added = 0, skipped = 1, @public = false, message = "Already in Coast." });
        Assert.Equal([a, b, c], (await GalleryHtml.LoadAsync(h, priv)).Items);

        await AssertJsonAsync(await Add(pub, a), HttpStatusCode.OK,
            new { added = 1, skipped = 0, @public = true, message = "1 photo added to Shore. Visitors can see it now." });
        await AssertJsonAsync(await Add(pub, b, c), HttpStatusCode.OK,
            new { added = 2, skipped = 0, @public = true, message = "2 photos added to Shore. Visitors can see them now." });
        Assert.Equal([d, a, b, c], (await GalleryHtml.LoadAsync(h, pub)).Items);

        await AssertJsonAsync(await Add(OpaqueId.NewId(), a), HttpStatusCode.NotFound,
            Err("not-found", "That gallery no longer exists."));
        await h.SettleAsync();
    }

    // ── A8 (TI-62: BuilderTests.Removing_photos_clears_the_pin…) ──────────────

    [Fact]
    public async Task A8_removing_photos_clears_the_pin_and_the_last_one_hides_a_public_gallery_until_a_photo_returns()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3);
        var ids = await GalleryHtml.ImageIdsAsync(h, 4);
        var (a, b, c, d) = (ids[0], ids[1], ids[2], ids[3]);
        var gid = await GalleryHtml.GalleryAsync(h, "Coast", [a, b, c], publish: true);
        var later = await GalleryHtml.GalleryAsync(h, "Later", [d], publish: true);
        Assert.True((await GalleryHtml.Gs(h, s => s.SetCoverAsync(gid, b))).IsOk);
        await h.SettleAsync();

        Task<HttpResponseMessage> Remove(params string[] remove) => h.PostJsonAsync($"/admin/api/galleries/{gid}/photos/remove", new { ids = remove });

        // Remove the pinned cover b: the pin is cleared, b stays in the library.
        await AssertJsonAsync(await Remove(b), HttpStatusCode.OK,
            new { removed = 1, @public = true, listed = true, message = "1 photo removed from Coast." });
        GalleryHtml.SetEq([Gp(gid, "coast"), Pp(gid, "coast", a), Pp(gid, "coast", b), Pp(gid, "coast", c), Home], await h.SettleAsync());
        var g = await GalleryHtml.LoadAsync(h, gid);
        Assert.Null(g.PinnedCover);
        Assert.Equal([a, c], g.Items);
        var bDoc = await h.Images.Find(i => i.Id == b).FirstAsync();
        Assert.Null(bDoc.DeletingAt);
        Assert.Empty(bDoc.GalleryIds);
        Assert.Equal(a, await HomeCoverAsync(h, gid));

        // Remove c, then the last photo a: still public, gone from "/g", its page 404.
        await AssertJsonAsync(await Remove(c), HttpStatusCode.OK,
            new { removed = 1, @public = true, listed = true, message = "1 photo removed from Coast." });
        GalleryHtml.SetEq([Gp(gid, "coast"), Pp(gid, "coast", a), Pp(gid, "coast", c), Home], await h.SettleAsync());
        await AssertJsonAsync(await Remove(a), HttpStatusCode.OK,
            new { removed = 1, @public = true, listed = false, message = "1 photo removed from Coast. Visitors can no longer see Coast until it has a photo." });
        // 010 P-NAV: Coast is unlisted, so Later loses its prev link.
        GalleryHtml.SetEq([Gp(gid, "coast"), Pp(gid, "coast", a), Gp(later, "later"), Home], await h.SettleAsync());

        g = await GalleryHtml.LoadAsync(h, gid);
        Assert.Equal("public", g.Visibility);
        Assert.Empty(g.Items);
        var home = await GalleryHtml.HomeRawAsync(h);
        Assert.DoesNotContain($"/g/{gid}/", home);
        Assert.Contains($"/g/{later}/later", home);
        Assert.Equal(HttpStatusCode.NotFound, (await h.GetAsync($"/g/{gid}/coast")).StatusCode);

        // Adding a photo (A7) brings it back at its home position with no publish.
        await AssertJsonAsync(await h.PostJsonAsync($"/admin/api/galleries/{gid}/photos", new { ids = new[] { a } }), HttpStatusCode.OK,
            new { added = 1, skipped = 0, @public = true, message = "1 photo added to Coast. Visitors can see it now." });
        GalleryHtml.SetEq([Gp(gid, "coast"), Pp(gid, "coast", a), Gp(later, "later"), Home], await h.SettleAsync());
        home = await GalleryHtml.HomeRawAsync(h);
        var at = home.IndexOf($"/g/{gid}/coast", StringComparison.Ordinal);
        Assert.True(at >= 0 && at < home.IndexOf($"/g/{later}/later", StringComparison.Ordinal), "Coast is back before Later");
        Assert.Equal([gid, later], (await h.SiteAsync()).HomeCards.Select(x => x.GalleryId).ToList());
        Assert.Single(h.LogLines, l => l.StartsWith($"owner.action gallery.publish {gid} ", StringComparison.Ordinal));
        Assert.Equal("public", (await GalleryHtml.LoadAsync(h, gid)).Visibility);
    }

    [Fact]
    public async Task A8_on_a_private_gallery_answers_listed_false_and_an_unknown_gallery_is_404()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3);
        var ids = await GalleryHtml.ImageIdsAsync(h, 2);
        var gid = await GalleryHtml.GalleryAsync(h, "Coast", ids);
        await h.SettleAsync();

        await AssertJsonAsync(await h.PostJsonAsync($"/admin/api/galleries/{gid}/photos/remove", new { ids }), HttpStatusCode.OK,
            new { removed = 2, @public = false, listed = false, message = "2 photos removed from Coast." });
        Assert.Empty((await GalleryHtml.LoadAsync(h, gid)).Items);
        await AssertJsonAsync(await h.PostJsonAsync($"/admin/api/galleries/{OpaqueId.NewId()}/photos/remove", new { ids }),
            HttpStatusCode.NotFound, Err("not-found", "That gallery no longer exists."));
        await h.SettleAsync();
    }

    // ── RemoveManyAsync (T011) ───────────────────────────────────────────────

    [Fact]
    public async Task RemoveMany_flushes_once_for_three_ids()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3);
        var ids = await GalleryHtml.ImageIdsAsync(h, 4);
        var gid = await GalleryHtml.GalleryAsync(h, "Coast", ids, publish: true);
        await h.SettleAsync();
        var purges0 = Count(h, "edge.purge ");
        var projects0 = Count(h, "op.timing db.site.project ");

        await AssertJsonAsync(await h.PostJsonAsync($"/admin/api/galleries/{gid}/photos/remove", new { ids = ids.Take(3).ToArray() }),
            HttpStatusCode.OK, new { removed = 3, @public = true, listed = true, message = "3 photos removed from Coast." });
        await h.EdgeIdleAsync();

        Assert.Equal(1, Count(h, "edge.purge ") - purges0);
        Assert.Equal(1, Count(h, "edge.purge gallery.remove.bulk "));
        Assert.Equal(1, Count(h, "op.timing db.site.project ") - projects0);
        Assert.Single(h.Recorder.Purges);
        Assert.Single(Snapshot(h), l => l.StartsWith("op.timing library.bulk.remove ", StringComparison.Ordinal) && l.EndsWith(" ok", StringComparison.Ordinal));
        GalleryHtml.SetEq(
            [Gp(gid, "coast"), Pp(gid, "coast", ids[0]), Pp(gid, "coast", ids[1]), Pp(gid, "coast", ids[2]), Pp(gid, "coast", ids[3]), Home],
            await h.SettleAsync());
        Assert.Equal([ids[3]], (await GalleryHtml.LoadAsync(h, gid)).Items);
    }

    [Fact]
    public async Task RemoveMany_skips_non_members_and_repeated_ids()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3);
        var ids = await GalleryHtml.ImageIdsAsync(h, 3);
        var gid = await GalleryHtml.GalleryAsync(h, "Coast", [ids[0], ids[1]]);
        await h.SettleAsync();

        var r = await GalleryHtml.Gs(h, s => s.RemoveManyAsync(gid, [ids[0], ids[0], ids[2]]));
        Assert.Equal((OperationStatus.Ok, 1, gid, "Coast"), (r.Status, r.Removed, r.GalleryId, r.GalleryTitle));
        Assert.Equal([ids[1]], (await GalleryHtml.LoadAsync(h, gid)).Items);
        Assert.Empty((await h.Images.Find(i => i.Id == ids[0]).FirstAsync()).GalleryIds);
        Assert.Equal([gid], (await h.Images.Find(i => i.Id == ids[1]).FirstAsync()).GalleryIds);
        await h.SettleAsync();
    }

    [Fact]
    public async Task RemoveMany_refuses_no_ids_and_more_than_PageSize_and_an_unknown_gallery_is_NotFound()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3, extraOverrides: new() { ["Library:PageSize"] = "2" });
        var ids = await GalleryHtml.ImageIdsAsync(h, 3);
        var gid = await GalleryHtml.GalleryAsync(h, "Coast", ids);
        await h.SettleAsync();
        var before = await DumpAsync(h);

        var none = await GalleryHtml.Gs(h, s => s.RemoveManyAsync(gid, []));
        Assert.Equal((OperationStatus.Refused, 422, "Select at least one photo."), (none.Status, none.StatusCode, none.Reason));
        var many = await GalleryHtml.Gs(h, s => s.RemoveManyAsync(gid, ids));
        Assert.Equal((OperationStatus.Refused, 422, "Select at most 2 photos."), (many.Status, many.StatusCode, many.Reason));
        var gone = await GalleryHtml.Gs(h, s => s.RemoveManyAsync(OpaqueId.NewId(), [ids[0]]));
        Assert.Equal((OperationStatus.NotFound, 404), (gone.Status, gone.StatusCode));

        Assert.Equal(before, await DumpAsync(h));
        Assert.Empty(await h.SettleAsync());
    }

    // ── A9 / A10 (TI-62: BuilderTests.Order_and_cover_steps_set_the_home_card_cover) ──

    [Fact]
    public async Task A9_and_A10_order_and_cover_steps_set_the_home_card_cover()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3);
        var ids = new List<string>();
        foreach (var t in new[] { "Pier", "Rocks", "Sand" })
            ids.Add((await h.InsertUploadedImageAsync(DateTime.UtcNow.AddSeconds(ids.Count), files: false, title: t)).Id);
        var (a, b, c) = (ids[0], ids[1], ids[2]);
        var gid = await GalleryHtml.GalleryAsync(h, "Coast", ids, publish: true);
        await h.SettleAsync();
        Assert.Equal(a, await HomeCoverAsync(h, gid));

        // No pin: moving c first makes it the home-card cover. Every neighbour changed.
        await AssertJsonAsync(await h.PostJsonAsync($"/admin/api/galleries/{gid}/order", new { order = new[] { c, a, b } }),
            HttpStatusCode.OK, new { message = "Order saved." });
        GalleryHtml.SetEq([Gp(gid, "coast"), Pp(gid, "coast", a), Pp(gid, "coast", b), Pp(gid, "coast", c), Home], await h.SettleAsync());
        Assert.Equal(c, await HomeCoverAsync(h, gid));

        // Stale order → 409 with the reason, nothing stored.
        await AssertJsonAsync(await h.PostJsonAsync($"/admin/api/galleries/{gid}/order", new { order = new[] { a, b } }),
            HttpStatusCode.Conflict, Err("stale", "This gallery changed in another tab. Reload to see the current order."));
        Assert.Equal([c, a, b], (await GalleryHtml.LoadAsync(h, gid)).Items);
        Assert.Empty(await h.SettleAsync());

        // Make cover on b pins it; the home card follows.
        await AssertJsonAsync(await h.PostJsonAsync($"/admin/api/galleries/{gid}/cover", new { image = b }),
            HttpStatusCode.OK, new { cover = b, message = "Rocks is now the cover of Coast." });
        GalleryHtml.SetEq([Gp(gid, "coast"), Home], await h.SettleAsync());
        Assert.Equal(b, (await GalleryHtml.LoadAsync(h, gid)).PinnedCover);
        Assert.Equal(b, await HomeCoverAsync(h, gid));

        // Unpin (image null) returns to the first photo.
        await AssertJsonAsync(await h.PostJsonAsync($"/admin/api/galleries/{gid}/cover", new { image = (string?)null }),
            HttpStatusCode.OK, new { cover = (string?)null, message = "Coast uses its first photo as the cover." });
        GalleryHtml.SetEq([Gp(gid, "coast"), Home], await h.SettleAsync());
        Assert.Null((await GalleryHtml.LoadAsync(h, gid)).PinnedCover);
        Assert.Equal(c, await HomeCoverAsync(h, gid));

        // Pinning a non-member is 422 with the reason.
        var other = await h.InsertUploadedImageAsync(files: false);
        await AssertJsonAsync(await h.PostJsonAsync($"/admin/api/galleries/{gid}/cover", new { image = other.Id }),
            HttpStatusCode.UnprocessableEntity, Err("refused", "That photo is not in this gallery."));
        Assert.Empty(await h.SettleAsync());
    }

    // ── A11 (TI-62: BuilderTests.Delete_gallery_keeps_its_photos…) ────────────

    [Fact]
    public async Task A11_delete_gallery_keeps_its_photos_in_the_library_and_their_other_galleries()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3);
        var ids = await GalleryHtml.ImageIdsAsync(h, 2);
        var (a, b) = (ids[0], ids[1]);
        var gid = await GalleryHtml.GalleryAsync(h, "Coast", [a, b], publish: true);
        var other = await GalleryHtml.GalleryAsync(h, "Other", [a]);
        await h.SettleAsync();

        await AssertJsonAsync(await h.PostJsonAsync($"/admin/api/galleries/{gid}/delete", new { }),
            HttpStatusCode.OK, new { message = "Coast deleted. Its photos are still in All photos." });
        GalleryHtml.SetEq([Gp(gid, "coast"), Pp(gid, "coast", a), Pp(gid, "coast", b), Home], await h.SettleAsync());

        Assert.Equal(HttpStatusCode.NotFound, (await h.GetAsync($"/g/{gid}/coast")).StatusCode);
        Assert.Equal(0, await h.Galleries.CountDocumentsAsync(g => g.Id == gid));
        Assert.Equal([other], (await h.Images.Find(i => i.Id == a).FirstAsync()).GalleryIds);
        Assert.Empty((await h.Images.Find(i => i.Id == b).FirstAsync()).GalleryIds);
        Assert.Equal([a], (await GalleryHtml.LoadAsync(h, other)).Items);
        Assert.Equal([other], await GalleryHtml.HomeOrderAsync(h));
        Assert.Single(h.LogLines, l => l.StartsWith($"owner.action gallery.delete {gid} ", StringComparison.Ordinal));

        // Already deleted → 200 with the already-deleted sentence, nothing purged.
        await AssertJsonAsync(await h.PostJsonAsync($"/admin/api/galleries/{gid}/delete", new { }),
            HttpStatusCode.OK, new { message = "That gallery was already deleted." });
        Assert.Empty(await h.SettleAsync());
    }

    // ── bulk caps (TI-62: LibraryPageTests.Bulk_ids_above_PageSize…) ──────────

    public static TheoryData<string> BulkRoutes() => new() { "A2", "A3", "A7", "A8" };

    private static (string Path, object Body) Bulk(string route, string gid, IReadOnlyList<string> ids) => route switch
    {
        "A2" => ("/admin/api/photos/delete", new { ids }),
        "A3" => ("/admin/api/galleries", new { title = "Coast", ids }),
        "A7" => ($"/admin/api/galleries/{gid}/photos", new { ids }),
        _ => ($"/admin/api/galleries/{gid}/photos/remove", new { ids }),
    };

    [Theory]
    [MemberData(nameof(BulkRoutes))]
    public async Task Bulk_routes_refuse_more_than_PageSize_ids(string route)
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3);
        var img = await h.InsertUploadedImageAsync(files: false);
        var gid = await GalleryHtml.GalleryAsync(h, "Shore", [img.Id]);
        await h.SettleAsync();
        var before = await DumpAsync(h);

        var tooMany = Enumerable.Range(0, 100).Select(_ => OpaqueId.NewId()).Append(img.Id).ToList();
        Assert.Equal(101, tooMany.Count);
        var (path, body) = Bulk(route, gid, tooMany);
        await AssertJsonAsync(await h.PostJsonAsync(path, body), HttpStatusCode.UnprocessableEntity,
            Err("refused", "Select at most 100 photos."));

        (path, body) = Bulk(route, gid, []);
        await AssertJsonAsync(await h.PostJsonAsync(path, body), HttpStatusCode.UnprocessableEntity,
            Err("refused", "Select at least one photo."));

        Assert.Equal(before, await DumpAsync(h));
        Assert.Empty(await h.SettleAsync());
    }

    // ── body limits and required fields ──────────────────────────────────────

    /// <summary>A JSON body of exactly <paramref name="bytes"/> bytes: <paramref name="head"/> plus a pad field.</summary>
    private static string Padded(string head, int bytes)
    {
        var shell = $"{{{head},\"pad\":\"\"}}";
        var json = $"{{{head},\"pad\":\"{new string('x', bytes - shell.Length)}\"}}";
        Assert.Equal(bytes, Encoding.UTF8.GetByteCount(json));
        return json;
    }

    [Fact]
    public async Task A9_body_limit_is_64_KiB_and_A2_is_16_KiB()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3);
        var ids = await GalleryHtml.ImageIdsAsync(h, 1);
        var gid = await GalleryHtml.GalleryAsync(h, "Coast", ids);
        await h.SettleAsync();

        await AssertJsonAsync(await PostRawAsync(h, $"/admin/api/galleries/{gid}/order", Padded("\"order\":[]", 64 * 1024)),
            HttpStatusCode.Conflict, Err("stale", GalleryService.StaleOrderReason));
        await AssertJsonAsync(await PostRawAsync(h, $"/admin/api/galleries/{gid}/order", Padded("\"order\":[]", 64 * 1024 + 1)),
            HttpStatusCode.RequestEntityTooLarge, Err("too-large", "The request body is too large."));

        await AssertJsonAsync(await PostRawAsync(h, "/admin/api/photos/delete", Padded("\"ids\":[]", 16 * 1024)),
            HttpStatusCode.UnprocessableEntity, Err("refused", "Select at least one photo."));
        await AssertJsonAsync(await PostRawAsync(h, "/admin/api/photos/delete", Padded("\"ids\":[]", 16 * 1024 + 1)),
            HttpStatusCode.RequestEntityTooLarge, Err("too-large", "The request body is too large."));
        Assert.Empty(await h.SettleAsync());
    }

    public static TheoryData<string, string, string> RequiredFields() => new()
    {
        { "A2", "/admin/api/photos/delete", "ids is required." },
        { "A3", "/admin/api/galleries", "title is required." },
        { "A4", "/admin/api/galleries/order", "order is required." },
        { "A6", "/admin/api/galleries/{gid}/visibility", "public is required." },
        { "A7", "/admin/api/galleries/{gid}/photos", "ids is required." },
        { "A8", "/admin/api/galleries/{gid}/photos/remove", "ids is required." },
        { "A9", "/admin/api/galleries/{gid}/order", "order is required." },
        { "A10", "/admin/api/galleries/{gid}/cover", "The request body is not valid JSON." },
    };

    [Theory]
    [MemberData(nameof(RequiredFields))]
    public async Task A_body_missing_a_required_field_is_400_bad_request_and_stores_nothing(string route, string path, string message)
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3);
        var ids = await GalleryHtml.ImageIdsAsync(h, 1);
        var gid = await GalleryHtml.GalleryAsync(h, "Coast", ids);
        await h.SettleAsync();
        var before = await DumpAsync(h);

        await AssertJsonAsync(await h.PostJsonAsync(path.Replace("{gid}", gid), new { }), HttpStatusCode.BadRequest,
            Err("bad-request", message));
        Assert.Equal(before, await DumpAsync(h));
        Assert.Empty(await h.SettleAsync());
        _ = route;
    }

    // ── Messages (contracts/http.md "Messages"; C-07; K12) ─────────────────────

    [Fact]
    public void Messages_A1_A5_A4_A9_A11_constants()
    {
        Assert.Equal("Saved", Messages.Saved);
        Assert.Equal("Gallery order saved.", Messages.GalleryOrderSaved);
        Assert.Equal("Order saved.", Messages.OrderSaved);
        Assert.Equal("That gallery was already deleted.", Messages.GalleryAlreadyDeleted);
        Assert.Equal("That photo no longer exists.", Messages.PhotoGone);
        Assert.Equal("Coast deleted. Its photos are still in All photos.", Messages.GalleryDeleted("Coast"));
    }

    [Fact]
    public void Messages_A7_added()
    {
        Assert.Equal("1 photo added to Coast.", Messages.Added(1, 0, "Coast", false));
        Assert.Equal("3 photos added to Coast. 2 were already in it.", Messages.Added(3, 2, "Coast", false));
        Assert.Equal("1 photo added to Coast. Visitors can see it now.", Messages.Added(1, 0, "Coast", true));
        Assert.Equal("2 photos added to Coast. 1 were already in it. Visitors can see them now.", Messages.Added(2, 1, "Coast", true));
        Assert.Equal("Already in Coast.", Messages.Added(0, 4, "Coast", true));
    }

    [Fact]
    public void Messages_A3_created()
    {
        Assert.Equal("Coast created. It is private.", Messages.Created("Coast", null));
        Assert.Equal("Coast created with 1 photo. It is private.", Messages.Created("Coast", 1));
        Assert.Equal("Coast created with 5 photos. It is private.", Messages.Created("Coast", 5));
    }

    [Fact]
    public void Messages_A8_removed()
    {
        Assert.Equal("1 photo removed from Coast.", Messages.Removed(1, "Coast", false));
        Assert.Equal("4 photos removed from Coast. Visitors can no longer see Coast until it has a photo.", Messages.Removed(4, "Coast", true));
    }

    [Fact]
    public void Messages_A6_A10_visibility_and_cover()
    {
        Assert.Equal("Coast is now public. Visitors can see it.", Messages.NowPublic("Coast"));
        Assert.Equal("Coast is now private.", Messages.NowPrivate("Coast"));
        Assert.Equal("Pier is now the cover of Coast.", Messages.Cover("Pier", "Coast"));
        Assert.Equal("Coast uses its first photo as the cover.", Messages.FirstPhotoCover("Coast"));
    }

    [Fact]
    public void Messages_A2_deleted_per_C07()
    {
        Assert.Equal("1 photo deleted.", Messages.Deleted(1, 0, 0));
        Assert.Equal("3 photos deleted.", Messages.Deleted(3, 0, 0));
        Assert.Equal("2 photos deleted. 4 files could not be removed and were logged.", Messages.Deleted(2, 4, 0));
        Assert.Equal("2 photos deleted. 1 could not be deleted. Try again.", Messages.Deleted(2, 0, 1));
        Assert.Equal("0 photos deleted. 4 files could not be removed and were logged. 1 could not be deleted. Try again.", Messages.Deleted(0, 4, 1));
    }

    [Fact]
    public void Messages_upload_selection_and_empty_collections()
    {
        Assert.Equal("Coast is public. Visitors will see these photos.", Messages.DestinationPublic("Coast"));
        Assert.Equal("Wait for the photos that are still working, or remove them.", Messages.StillWorking);
        Assert.Equal("3 were not added.", Messages.NotAdded(3));
        Assert.Equal("You can select up to 100 photos at a time.", Messages.SelectionCap(100));
        Assert.Equal("No photos yet. Use Add photos, or drop files anywhere on this page.", Messages.LibraryEmpty);
        Assert.Equal("Every photo is in a gallery.", Messages.UnplacedEmpty);
        Assert.Equal("This gallery has no photos yet. Use Add photos to upload straight into it, or open All photos and drag photos onto its name.",
            Messages.GalleryEmpty);
    }

    [Fact]
    public void Messages_DeleteConsequence_moved_verbatim_and_the_photo_page_calls_it()
    {
        Assert.Equal("It is in no gallery. This removes it from every gallery and deletes the original file. It can't be undone.",
            Messages.DeleteConsequence(0));
        Assert.Equal("It is in 1 gallery. This removes it from every gallery and deletes the original file. It can't be undone.",
            Messages.DeleteConsequence(1));
        Assert.Equal("It is in 3 galleries. This removes it from every gallery and deletes the original file. It can't be undone.",
            Messages.DeleteConsequence(3));
        foreach (var n in new[] { 0, 1, 3 })
            Assert.Equal(Messages.DeleteConsequence(n), PhotoEditPage.DeleteConsequence(n));
    }
}
