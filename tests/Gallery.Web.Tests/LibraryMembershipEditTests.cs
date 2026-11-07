using System.Net;
using System.Text.RegularExpressions;
using Gallery.Core.Ids;
using Gallery.Web.Data;
using Gallery.Web.Library;
using Gallery.Web.Tests.Fixtures;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Gallery.Web.Tests;

/// <summary>
/// 014 T012 (US1/US2; FR-013, FR-014, II.5, I.6): the photo panel's gallery membership over L2
/// (contracts/http.md). <c>add</c>, <c>create</c> and <c>leave</c> are validated before any write,
/// applied in one <c>image.memberships</c> scope, and logged as <c>library.image.memberships</c>.
/// </summary>
[Collection("s3")]
public class LibraryMembershipEditTests(S3ProxyFixture s3, MongoFixture mongo) : IClassFixture<MongoFixture>
{
    private const string Op = "library.image.memberships";

    private static KeyValuePair<string, string> F(string k, string v) => new(k, v);

    private static Task<OperationResult> Gs(LibraryHost h, Func<GalleryService, Task<OperationResult>> call) => h.RunAsync(call);

    private static Task<ImageDoc> ImageAsync(LibraryHost h, string iid) => h.Images.Find(i => i.Id == iid).FirstAsync();

    private static Task<GalleryDoc> LoadAsync(LibraryHost h, string gid) => h.Galleries.Find(g => g.Id == gid).FirstAsync();

    private static async Task<string> GalleryAsync(LibraryHost h, string title, IReadOnlyList<string> ids, bool publish = false)
    {
        var created = await Gs(h, s => s.CreateAsync(title));
        Assert.True(created.IsOk);
        var gid = created.GalleryId!;
        if (ids.Count > 0) Assert.True((await Gs(h, s => s.AddAsync(gid, ids))).IsOk);
        if (publish) Assert.True((await Gs(h, s => s.PublishAsync(gid, confirmed: true))).IsOk);
        return gid;
    }

    private static Task<HttpResponseMessage> SaveAsync(LibraryHost h, string iid, params (string, string)[] fields) =>
        h.PostFormAsync($"/admin/library/images/{iid}", fields.Select(f => F(f.Item1, f.Item2)).Append(F("page", "1")));

    private static string Aside(string html) =>
        Regex.Match(html, "<aside class=\"side-panel\".*?</aside>", RegexOptions.Singleline).Value;

    private static List<string> Lines(LibraryHost h, string op)
    {
        lock (h.LogLines)
            return h.LogLines.Where(l => l.StartsWith($"op.timing {op} ", StringComparison.Ordinal)).ToList();
    }

    /// <summary>"Nothing written": every image's items-side fields, every gallery's items, the gallery count and homeOrder.</summary>
    private sealed record Snapshot(
        int GalleryCount,
        IReadOnlyList<string> HomeOrder,
        IReadOnlyDictionary<string, string> Items,
        IReadOnlyDictionary<string, string> GalleryIds,
        IReadOnlyDictionary<string, string> Titles);

    private static async Task<Snapshot> SnapAsync(LibraryHost h)
    {
        var galleries = await h.Galleries.Find(FilterDefinition<GalleryDoc>.Empty).ToListAsync();
        var images = await h.Images.Find(FilterDefinition<ImageDoc>.Empty).ToListAsync();
        var site = await h.SiteAsync();
        return new Snapshot(
            galleries.Count,
            site.HomeOrder,
            galleries.ToDictionary(g => g.Id, g => string.Join(",", g.Items)),
            images.ToDictionary(i => i.Id, i => string.Join(",", i.GalleryIds.Order(StringComparer.Ordinal))),
            images.ToDictionary(i => i.Id, i => i.Title + "|" + i.Caption));
    }

    private static void AssertSame(Snapshot before, Snapshot after)
    {
        Assert.Equal(before.GalleryCount, after.GalleryCount);
        Assert.Equal(before.HomeOrder, after.HomeOrder);
        Assert.Equal(before.Items.OrderBy(kv => kv.Key), after.Items.OrderBy(kv => kv.Key));
        Assert.Equal(before.GalleryIds.OrderBy(kv => kv.Key), after.GalleryIds.OrderBy(kv => kv.Key));
        Assert.Equal(before.Titles.OrderBy(kv => kv.Key), after.Titles.OrderBy(kv => kv.Key));
    }

    private static string[] Sorted(IEnumerable<string> s) => s.Order(StringComparer.Ordinal).ToArray();

    [Fact]
    public async Task Add_to_an_existing_gallery_is_303_and_purges_exactly_what_the_bulk_add_purges()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3);
        var cover = await h.InsertUploadedImageAsync(files: false, title: "Cover");
        var img = await h.InsertUploadedImageAsync(files: false, title: "Pier");
        var gid = await GalleryAsync(h, "Coast", [cover.Id], publish: true);
        await h.SettleAsync();

        var resp = await SaveAsync(h, img.Id, ("add", gid));
        Assert.Equal(HttpStatusCode.SeeOther, resp.StatusCode);
        Assert.Equal($"/admin/library?page=1&open={img.Id}&saved=1", resp.Headers.Location?.ToString());
        Assert.Equal([cover.Id, img.Id], (await LoadAsync(h, gid)).Items);
        Assert.Equal([gid], (await ImageAsync(h, img.Id)).GalleryIds);
        var panel = await h.SettleAsync();
        Assert.NotEmpty(panel);
        Assert.Single(Lines(h, Op), l => l.EndsWith(" ok", StringComparison.Ordinal));

        // Same host, same state again (B5 takes it back out), then L3 adds the same photo to the same gallery.
        Assert.Equal(1, (await Gs(h, s => s.RemoveAsync(gid, img.Id))).Removed);
        await h.SettleAsync();
        var bulk = await h.PostFormAsync("/admin/library/add", [F("ids", img.Id), F("gallery", gid), F("page", "1")]);
        Assert.Equal(HttpStatusCode.SeeOther, bulk.StatusCode);
        Assert.Equal(Sorted(await h.SettleAsync()), Sorted(panel));
    }

    [Fact]
    public async Task Create_makes_one_private_gallery_holding_the_photo_last_in_homeOrder_with_no_purge()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3);
        var img = await h.InsertUploadedImageAsync(files: false, title: "Pier");
        var existing = await GalleryAsync(h, "Coast", []);
        await h.SettleAsync();
        var before = await h.Galleries.Find(FilterDefinition<GalleryDoc>.Empty).Project(g => g.Id).ToListAsync();

        var resp = await SaveAsync(h, img.Id, ("create", "Night walks"));
        Assert.Equal(HttpStatusCode.SeeOther, resp.StatusCode);

        var created = await h.Galleries.Find(Builders<GalleryDoc>.Filter.Nin(g => g.Id, before)).ToListAsync();
        var g = Assert.Single(created);
        Assert.Equal("Night walks", g.Title);
        Assert.Equal("private", g.Visibility);
        Assert.Equal([img.Id], g.Items);
        Assert.Equal([existing, g.Id], (await h.SiteAsync()).HomeOrder);
        Assert.Equal([g.Id], (await ImageAsync(h, img.Id)).GalleryIds);
        // The create itself purges nothing (L-10). The add into it is THE add gate's P-MEMBERS of a
        // private gallery, which today purges the gallery page and the photo page exactly as the bulk
        // add into a new gallery does (LibraryFlowTests). tasks.md T012 (AMENDED): equal to L3's set.
        Assert.Equal(
            Sorted([LibraryHost.Page($"/g/{g.Id}/{g.Slug}"), LibraryHost.Page($"/g/{g.Id}/{g.Slug}/{img.Id}")]),
            Sorted(await h.SettleAsync()));
    }

    [Fact]
    public async Task Create_add_and_title_in_one_POST_are_all_applied_with_one_ok_line()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3);
        var img = await h.InsertUploadedImageAsync(files: false, title: "IMG_0001");
        var gid = await GalleryAsync(h, "Coast", []);
        await h.SettleAsync();

        var resp = await SaveAsync(h, img.Id, ("title", "Harbour"), ("add", gid), ("create", "Night walks"));
        Assert.Equal(HttpStatusCode.SeeOther, resp.StatusCode);

        var stored = await ImageAsync(h, img.Id);
        Assert.Equal("Harbour", stored.Title);
        var night = await h.Galleries.Find(x => x.Title == "Night walks").SingleAsync();
        Assert.Equal([img.Id], (await LoadAsync(h, gid)).Items);
        Assert.Equal([img.Id], night.Items);
        Assert.Equal(Sorted([gid, night.Id]), Sorted(stored.GalleryIds));
        var lines = Lines(h, Op);
        Assert.Single(lines);
        Assert.EndsWith(" ok", lines[0]);
        await h.SettleAsync();
    }

    [Fact]
    public async Task A_201_char_create_title_is_422_and_writes_nothing()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3);
        var img = await h.InsertUploadedImageAsync(files: false, title: "Stored title");
        var gid = await GalleryAsync(h, "Coast", []);
        await h.SettleAsync();
        var before = await SnapAsync(h);

        var resp = await SaveAsync(h, img.Id, ("title", "Changed"), ("add", gid), ("create", new string('t', 201)));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
        Assert.Equal("The title can be at most 200 characters.", LibraryHtml.Status(await LibraryHtml.BodyAsync(resp)));
        AssertSame(before, await SnapAsync(h));
        Assert.Equal("Stored title", (await ImageAsync(h, img.Id)).Title);
        Assert.Single(Lines(h, Op), l => l.EndsWith(" refused", StringComparison.Ordinal));
        Assert.Empty(await h.SettleAsync());
    }

    [Fact]
    public async Task An_unknown_add_id_with_a_create_is_404_and_creates_nothing()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3);
        var img = await h.InsertUploadedImageAsync(files: false, title: "Pier");
        await GalleryAsync(h, "Bravo", [img.Id]);
        await GalleryAsync(h, "Alpha", [img.Id]);
        await h.SettleAsync();
        var before = await SnapAsync(h);

        var resp = await SaveAsync(h, img.Id, ("add", OpaqueId.NewId()), ("create", "X"));

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
        var html = await LibraryHtml.BodyAsync(resp);
        Assert.Equal("That gallery no longer exists.", LibraryHtml.Status(html));
        AssertSame(before, await SnapAsync(h));
        Assert.Equal(0, await h.Galleries.CountDocumentsAsync(g => g.Title == "X"));
        Assert.Single(Lines(h, Op), l => l.EndsWith(" notfound", StringComparison.Ordinal));
        Assert.Empty(await h.SettleAsync());
    }

    /// <summary>
    /// The 404 gallery refusal's panel shows the CURRENT memberships (no pending token). Needs the
    /// TokenCombobox panel of Batch C (T013): red until then.
    /// </summary>
    [Fact]
    public async Task An_unknown_add_id_re_renders_the_panel_on_the_stored_holders_with_no_pending_token()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3);
        var img = await h.InsertUploadedImageAsync(files: false, title: "Pier");
        var bravo = await GalleryAsync(h, "Bravo", [img.Id]);
        var alpha = await GalleryAsync(h, "Alpha", [img.Id]);
        await h.SettleAsync();

        var resp = await SaveAsync(h, img.Id, ("add", OpaqueId.NewId()), ("create", "X"));
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
        var html = await LibraryHtml.BodyAsync(resp);

        // The panel re-renders on the photo's stored holders, in title order, with no pending token.
        var aside = Aside(html);
        var tokens = Regex.Match(aside, "<ul class=\"cb-tokens\".*?</ul>", RegexOptions.Singleline);
        Assert.True(tokens.Success, "no ul.cb-tokens in the panel");
        Assert.Equal([$"/admin/galleries/{alpha}", $"/admin/galleries/{bravo}"],
            Regex.Matches(tokens.Value, "href=\"([^\"]+)\"").Select(m => m.Groups[1].Value).ToList());
        Assert.DoesNotMatch("class=\"[^\"]*\\bis-pending\\b", aside);
    }

    [Fact]
    public async Task A_malformed_add_id_is_404_and_writes_nothing()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3);
        var img = await h.InsertUploadedImageAsync(files: false, title: "Pier");
        await GalleryAsync(h, "Coast", []);
        await h.SettleAsync();
        var before = await SnapAsync(h);

        var resp = await SaveAsync(h, img.Id, ("title", "Changed"), ("add", "not-an-id"));

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
        Assert.Equal("That gallery no longer exists.", LibraryHtml.Status(await LibraryHtml.BodyAsync(resp)));
        AssertSame(before, await SnapAsync(h));
        Assert.Empty(await h.SettleAsync());
    }

    [Fact]
    public async Task A_text_refusal_with_an_add_is_422_and_changes_no_membership()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3);
        var img = await h.InsertUploadedImageAsync(files: false, title: "Stored title");
        var gid = await GalleryAsync(h, "Coast", []);
        await h.SettleAsync();
        var before = await SnapAsync(h);

        var resp = await SaveAsync(h, img.Id, ("title", ""), ("add", gid));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
        Assert.Equal("Enter a title.", LibraryHtml.Status(await LibraryHtml.BodyAsync(resp)));
        AssertSame(before, await SnapAsync(h));
        Assert.Empty((await LoadAsync(h, gid)).Items);
        Assert.Empty(await h.SettleAsync());
    }

    [Fact]
    public async Task With_a_session_and_no_antiforgery_token_add_and_create_are_400_and_store_nothing()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3);
        var img = await h.InsertUploadedImageAsync(files: false, title: "Pier");
        var gid = await GalleryAsync(h, "Coast", []);
        await h.SettleAsync();
        var before = await DumpAsync(h);

        var resp = await h.PostFormAsync($"/admin/library/images/{img.Id}",
            [F("add", gid), F("create", "Hacked"), F("page", "1")], csrf: false);

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        await h.EdgeIdleAsync();
        Assert.Equal(before, await DumpAsync(h));
        Assert.Empty(h.Recorder.AllPurged());
    }

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

    [Fact]
    public async Task An_empty_edit_logs_no_memberships_line()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3);
        var img = await h.InsertUploadedImageAsync(files: false, title: "Before");

        var resp = await SaveAsync(h, img.Id, ("title", "After"));

        Assert.Equal(HttpStatusCode.SeeOther, resp.StatusCode);
        Assert.Equal("After", (await ImageAsync(h, img.Id)).Title);
        Assert.Empty(Lines(h, Op));
        await h.SettleAsync();
    }

    [Fact]
    public async Task Adding_an_already_held_gallery_is_303_with_no_duplicate()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3);
        var img = await h.InsertUploadedImageAsync(files: false, title: "Pier");
        var gid = await GalleryAsync(h, "Coast", [img.Id]);
        await h.SettleAsync();

        var resp = await SaveAsync(h, img.Id, ("add", gid));

        Assert.Equal(HttpStatusCode.SeeOther, resp.StatusCode);
        Assert.Equal([img.Id], (await LoadAsync(h, gid)).Items);
        Assert.Equal([gid], (await ImageAsync(h, img.Id)).GalleryIds);
        await h.SettleAsync();
    }
}
