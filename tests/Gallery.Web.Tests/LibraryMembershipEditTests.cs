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

    /// <summary>The JSON answer's root element (020 TI-13).</summary>
    private static async Task<System.Text.Json.JsonElement> JsonAsync(HttpResponseMessage resp)
    {
        using var doc = await UploadHost.ParseAsync(resp);
        Assert.NotNull(doc);
        return doc!.RootElement.Clone();
    }

    [Fact]
    public async Task Add_to_an_existing_gallery_is_303_and_purges_exactly_what_the_bulk_add_purges()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3);
        var cover = await h.InsertUploadedImageAsync(files: false, title: "Cover");
        var img = await h.InsertUploadedImageAsync(files: false, title: "Pier");
        var gid = await GalleryAsync(h, "Coast", [cover.Id], publish: true);
        await h.SettleAsync();

        var resp = await h.PostJsonAsync($"/admin/api/galleries/{gid}/photos", new { ids = new[] { img.Id } });
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var json = await JsonAsync(resp);
        Assert.Equal((1, 0), (json.GetProperty("added").GetInt32(), json.GetProperty("skipped").GetInt32()));
        Assert.Equal([cover.Id, img.Id], (await LoadAsync(h, gid)).Items);
        Assert.Equal([gid], (await ImageAsync(h, img.Id)).GalleryIds);
        var panel = await h.SettleAsync();
        Assert.NotEmpty(panel);
        Assert.Single(Lines(h, "library.bulk.add"), l => l.EndsWith(" ok"));

        // Same host, same state again (A8 takes it back out), then A7 adds the same photo to the same gallery.
        var removed = await h.PostJsonAsync($"/admin/api/galleries/{gid}/photos/remove", new { ids = new[] { img.Id } });
        Assert.Equal(HttpStatusCode.OK, removed.StatusCode);
        Assert.Equal(1, (await JsonAsync(removed)).GetProperty("removed").GetInt32());
        await h.SettleAsync();
        var bulk = await h.PostJsonAsync($"/admin/api/galleries/{gid}/photos", new { ids = new[] { img.Id } });
        Assert.Equal(HttpStatusCode.OK, bulk.StatusCode);
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

        var resp = await h.PostJsonAsync("/admin/api/galleries", new { title = "Night walks", ids = new[] { img.Id } });
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);

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
    public async Task A_201_char_create_title_is_422_and_writes_nothing()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3);
        var img = await h.InsertUploadedImageAsync(files: false, title: "Stored title");
        var gid = await GalleryAsync(h, "Coast", []);
        await h.SettleAsync();
        var before = await SnapAsync(h);

        var resp = await h.PostJsonAsync("/admin/api/galleries", new { title = new string('t', 201), ids = new[] { img.Id } });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
        Assert.Equal("The title can be at most 200 characters.", (await JsonAsync(resp)).GetProperty("message").GetString());
        AssertSame(before, await SnapAsync(h));
        Assert.Equal("Stored title", (await ImageAsync(h, img.Id)).Title);
        Assert.Empty(await h.SettleAsync());
    }

    [Fact]
    public async Task A_malformed_add_id_is_404_and_writes_nothing()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3);
        var img = await h.InsertUploadedImageAsync(files: false, title: "Pier");
        await GalleryAsync(h, "Coast", []);
        await h.SettleAsync();
        var before = await SnapAsync(h);

        var resp = await h.PostJsonAsync("/admin/api/galleries/not-an-id/photos", new { ids = new[] { img.Id } });

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
        AssertSame(before, await SnapAsync(h));
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

        var add = await h.PostJsonAsync($"/admin/api/galleries/{gid}/photos", new { ids = new[] { img.Id } }, csrf: false);
        var create = await h.PostJsonAsync("/admin/api/galleries", new { title = "Hacked", ids = new[] { img.Id } }, csrf: false);

        Assert.Equal(HttpStatusCode.BadRequest, add.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, create.StatusCode);
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
    public async Task Adding_an_already_held_gallery_is_303_with_no_duplicate()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3);
        var img = await h.InsertUploadedImageAsync(files: false, title: "Pier");
        var gid = await GalleryAsync(h, "Coast", [img.Id]);
        await h.SettleAsync();

        var resp = await h.PostJsonAsync($"/admin/api/galleries/{gid}/photos", new { ids = new[] { img.Id } });

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var json = await JsonAsync(resp);
        Assert.Equal((0, 1, "Already in Coast."),
            (json.GetProperty("added").GetInt32(), json.GetProperty("skipped").GetInt32(), json.GetProperty("message").GetString()));
        Assert.Equal([img.Id], (await LoadAsync(h, gid)).Items);
        Assert.Equal([gid], (await ImageAsync(h, img.Id)).GalleryIds);
        await h.SettleAsync();
    }

    // ── T017 US3: remove a gallery token (leave) ───────────────────────────────────────────

    [Fact]
    public async Task Leave_takes_the_photo_out_clears_its_pinned_cover_and_purges_exactly_what_B5_purges()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3);
        var other = await h.InsertUploadedImageAsync(files: false, title: "Other");
        var img = await h.InsertUploadedImageAsync(files: false, title: "Pier");
        var gid = await GalleryAsync(h, "Coast", [other.Id, img.Id], publish: true);
        var keep = await GalleryAsync(h, "Harbour", [img.Id]);
        var cover = await h.PostJsonAsync($"/admin/api/galleries/{gid}/cover", new { image = img.Id });
        Assert.Equal(HttpStatusCode.OK, cover.StatusCode);
        Assert.Equal(img.Id, (await LoadAsync(h, gid)).PinnedCover);
        await h.SettleAsync();

        var resp = await h.PostJsonAsync($"/admin/api/galleries/{gid}/photos/remove", new { ids = new[] { img.Id } });
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal(1, (await JsonAsync(resp)).GetProperty("removed").GetInt32());
        var g = await LoadAsync(h, gid);
        Assert.Equal([other.Id], g.Items);
        Assert.Null(g.PinnedCover);
        Assert.Equal([keep], (await ImageAsync(h, img.Id)).GalleryIds);
        Assert.Equal([img.Id], (await LoadAsync(h, keep)).Items);
        Assert.Null((await ImageAsync(h, img.Id)).DeletingAt);
        var panel = await h.SettleAsync();
        Assert.NotEmpty(panel);

        // Same host, same state again (the photo back in, pinned as the cover), then B5 removes it.
        Assert.True((await Gs(h, s => s.AddAsync(gid, [img.Id]))).IsOk);
        Assert.True((await Gs(h, s => s.SetCoverAsync(gid, img.Id))).IsOk);
        await h.SettleAsync();
        Assert.Equal([other.Id, img.Id], (await LoadAsync(h, gid)).Items);
        Assert.Equal(img.Id, (await LoadAsync(h, gid)).PinnedCover);
        var b5 = await h.PostJsonAsync($"/admin/api/galleries/{gid}/photos/remove", new { ids = new[] { img.Id } });
        Assert.Equal(HttpStatusCode.OK, b5.StatusCode);
        Assert.Equal(Sorted(await h.SettleAsync()), Sorted(panel));
    }

    [Fact]
    public async Task Leaving_a_public_gallery_s_last_photo_keeps_it_public_and_drops_it_from_g()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3);
        var img = await h.InsertUploadedImageAsync(files: false, title: "Pier");
        var later = await h.InsertUploadedImageAsync(files: false, title: "Later");
        var gid = await GalleryAsync(h, "Coast", [img.Id], publish: true);
        var laterGid = await GalleryAsync(h, "Later", [later.Id], publish: true);
        await h.SettleAsync();
        var slug = (await LoadAsync(h, gid)).Slug;
        var laterSlug = (await LoadAsync(h, laterGid)).Slug;
        var homeBefore = await GalleryHtml.HomeRawAsync(h);
        Assert.Contains($"href=\"/g/{gid}/{slug}\"", homeBefore);
        Assert.Contains($"href=\"/g/{laterGid}/{laterSlug}\"", homeBefore);

        var resp = await h.PostJsonAsync($"/admin/api/galleries/{gid}/photos/remove", new { ids = new[] { img.Id } });
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var json = await JsonAsync(resp);
        Assert.Equal((1, false), (json.GetProperty("removed").GetInt32(), json.GetProperty("listed").GetBoolean()));
        Assert.EndsWith(" Visitors can no longer see Coast until it has a photo.", json.GetProperty("message").GetString());
        await h.SettleAsync();

        var g = await LoadAsync(h, gid);
        Assert.Equal("public", g.Visibility);
        Assert.Empty(g.Items);
        Assert.Empty((await ImageAsync(h, img.Id)).GalleryIds);
        var home = await GalleryHtml.HomeRawAsync(h);
        Assert.DoesNotContain($"/g/{gid}/", home);
        Assert.Contains($"href=\"/g/{laterGid}/{laterSlug}\"", home);
        Assert.Equal(HttpStatusCode.NotFound, (await h.GetAsync($"/g/{gid}/{slug}")).StatusCode);
    }
}
