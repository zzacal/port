using System.Net;
using Gallery.Web.Data;
using Gallery.Web.Edge;
using Gallery.Web.Library;
using Gallery.Web.Tests.Fixtures;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Gallery.Web.Tests;

/// <summary>
/// 009 T020 + T025 (US1, US3, US4; FR-012, FR-018–FR-033, FR-036–FR-038; SC-004, SC-009) and the
/// service-level half of T022 [SAFETY] (II.5, II.6). Every gallery is built the way the owner builds
/// one, through <see cref="GalleryService"/> (create, add, cover, publish), never by writing the
/// gate-satisfying state into the database. After every operation <see cref="LibraryHost.SettleAsync"/>
/// waits for the edge queue, runs <see cref="InvariantChecker"/>, and the recorder's purged set is
/// compared for EQUALITY with the contracts/edge.md set.
/// </summary>
[Collection("s3")]
public class GalleryServiceTests(S3ProxyFixture s3, MongoFixture mongo) : IClassFixture<MongoFixture>
{
    private Task<LibraryHost> NewHostAsync() => LibraryHost.CreateAsync(mongo, s3);

    private static string Gp(string gid, string slug) => LibraryHost.Page(PurgeSets.GalleryPath(gid, slug));
    private static string Pp(string gid, string slug, string iid) => LibraryHost.Page(PurgeSets.PhotoPath(gid, slug, iid));
    private static readonly string Home = LibraryHost.Page("/");

    private static void SetEq(IEnumerable<string> expected, IEnumerable<string> actual) =>
        Assert.Equal(expected.Distinct().Order(StringComparer.Ordinal).ToArray(), actual.Order(StringComparer.Ordinal).ToArray());

    private static Task<OperationResult> Gs(LibraryHost h, Func<GalleryService, Task<OperationResult>> call) =>
        h.RunAsync(call);

    private static async Task<string> CreateAsync(LibraryHost h, string title)
    {
        var r = await Gs(h, s => s.CreateAsync(title));
        Assert.Equal(OperationStatus.Ok, r.Status);
        return r.GalleryId!;
    }

    private static async Task<List<ImageDoc>> ImagesAsync(LibraryHost h, int n)
    {
        var list = new List<ImageDoc>();
        for (var i = 0; i < n; i++)
            list.Add(await h.InsertUploadedImageAsync(DateTime.UtcNow.AddSeconds(i), files: false));
        return list;
    }

    private static Task<GalleryDoc> GalleryAsync(LibraryHost h, string gid) =>
        h.Galleries.Find(g => g.Id == gid).FirstAsync();

    private static Task<SiteDoc> SiteAsync(LibraryHost h) => h.SiteAsync();

    // ── T020: the fixture itself ─────────────────────────────────────────────

    [Fact]
    public async Task LibraryHost_signs_in_caches_a_token_and_records_the_edge()
    {
        await using var h = await NewHostAsync();

        var resp = await h.GetAsync("/admin");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.False(string.IsNullOrEmpty(h.CsrfToken));
        Assert.Same(h.Recorder, h.Services.GetRequiredService<IEdgeProvider>());
    }

    // ── create (L-10) ────────────────────────────────────────────────────────

    [Fact]
    public async Task Create_is_private_last_in_homeOrder_has_no_seedKey_and_purges_nothing()
    {
        await using var h = await NewHostAsync();
        var img = (await ImagesAsync(h, 1))[0];
        var pub = await CreateAsync(h, "Existing");
        Assert.True((await Gs(h, s => s.AddAsync(pub, [img.Id]))).IsOk);
        Assert.True((await Gs(h, s => s.PublishAsync(pub, confirmed: true))).IsOk);
        await h.SettleAsync();

        var first = await CreateAsync(h, "  Autumn Walk  ");
        var second = await CreateAsync(h, "Second");
        await h.SettleAsync();

        var g = await GalleryAsync(h, first);
        Assert.Equal("Autumn Walk", g.Title);
        Assert.Equal("autumn-walk", g.Slug);
        Assert.Equal("private", g.Visibility);
        Assert.Empty(g.Items);
        Assert.Equal(new[] { pub, first, second }, (await SiteAsync(h)).HomeOrder.ToArray());
        var raw = await h.Db.GetCollection<BsonDocument>("galleries").Find(new BsonDocument("_id", first)).FirstAsync();
        Assert.False(raw.Contains("seedKey"));
        Assert.Equal(0, h.Recorder.CallCount);
        // II.5: no gallery's visibility changed.
        Assert.Equal("public", (await GalleryAsync(h, pub)).Visibility);
        Assert.Equal("private", (await GalleryAsync(h, second)).Visibility);
    }

    [Fact]
    public async Task Create_refuses_a_blank_or_too_long_title_and_stores_nothing()
    {
        await using var h = await NewHostAsync();

        var blank = await Gs(h, s => s.CreateAsync("   "));
        var tooLong = await Gs(h, s => s.CreateAsync(new string('a', 201)));

        Assert.Equal(OperationStatus.Refused, blank.Status);
        Assert.Equal(422, blank.StatusCode);
        Assert.Equal("Enter a title.", blank.Reason);
        Assert.Equal(OperationStatus.Refused, tooLong.Status);
        Assert.Equal(422, tooLong.StatusCode);
        Assert.Equal("The title can be at most 200 characters.", tooLong.Reason);
        Assert.Equal(0, await h.Galleries.CountDocumentsAsync(FilterDefinition<GalleryDoc>.Empty));
    }

    // ── add (P-MEMBERS) ──────────────────────────────────────────────────────

    [Fact]
    public async Task Add_40_then_40_with_5_overlapping_counts_exactly_and_keeps_the_given_order()
    {
        await using var h = await NewHostAsync();
        var imgs = await ImagesAsync(h, 75);
        var first = imgs.Take(40).Select(i => i.Id).ToList();
        var fresh = imgs.Skip(40).Select(i => i.Id).ToList();
        var gid = await CreateAsync(h, "Harbour");
        await h.SettleAsync();

        var r1 = await Gs(h, s => s.AddAsync(gid, first));
        var purged1 = await h.SettleAsync();

        Assert.Equal((40, 0), (r1.Added, r1.Skipped));
        Assert.Equal(first, (await GalleryAsync(h, gid)).Items);
        // Private gallery that gained a page: its page and every photo page; not listed → no "/".
        SetEq(first.Select(i => Pp(gid, "harbour", i)).Append(Gp(gid, "harbour")), purged1);

        // The 5 overlapping ids sit in the middle of the second request.
        var second = fresh.Take(17).Concat(first.Skip(35)).Concat(fresh.Skip(17)).ToList();
        var r2 = await Gs(h, s => s.AddAsync(gid, second));
        var purged2 = await h.SettleAsync();

        Assert.Equal((35, 5), (r2.Added, r2.Skipped));
        Assert.Equal(first.Concat(fresh).ToList(), (await GalleryAsync(h, gid)).Items);
        // Changed = the 35 added + the old last photo (its next neighbour changed).
        SetEq(fresh.Append(first[39]).Select(i => Pp(gid, "harbour", i)).Append(Gp(gid, "harbour")), purged2);
        foreach (var i in imgs)
            Assert.Equal(new[] { gid }, (await h.Images.Find(x => x.Id == i.Id).FirstAsync()).GalleryIds.ToArray());
    }

    [Fact]
    public async Task Add_skips_unknown_and_tombstoned_images()
    {
        await using var h = await NewHostAsync();
        var imgs = await ImagesAsync(h, 2);
        await h.Images.UpdateOneAsync(i => i.Id == imgs[1].Id, Builders<ImageDoc>.Update.Set(i => i.DeletingAt, DateTime.UtcNow));
        var gid = await CreateAsync(h, "Gate");

        var r = await Gs(h, s => s.AddAsync(gid, [imgs[0].Id, imgs[1].Id, "zzzzzzzzzzzzzzzzzzzzzzzzzz"]));

        Assert.Equal((1, 2), (r.Added, r.Skipped));
        Assert.Equal(new[] { imgs[0].Id }, (await GalleryAsync(h, gid)).Items.ToArray());
        Assert.Empty((await h.Images.Find(x => x.Id == imgs[1].Id).FirstAsync()).GalleryIds);
    }

    // ── reorder, cover, remove ───────────────────────────────────────────────

    [Fact]
    public async Task Reorder_with_one_id_missing_is_stale_and_cover_of_a_non_member_is_422()
    {
        await using var h = await NewHostAsync();
        var imgs = await ImagesAsync(h, 4);
        var ids = imgs.Take(3).Select(i => i.Id).ToList();
        var gid = await CreateAsync(h, "Order");
        await Gs(h, s => s.AddAsync(gid, ids));
        await h.SettleAsync();

        var stale = await Gs(h, s => s.ReorderAsync(gid, [ids[2], ids[0]]));
        var dup = await Gs(h, s => s.ReorderAsync(gid, [ids[2], ids[0], ids[0]]));
        var cover = await Gs(h, s => s.SetCoverAsync(gid, imgs[3].Id));
        var purged = await h.SettleAsync();

        Assert.Equal(OperationStatus.Stale, stale.Status);
        Assert.Equal(409, stale.StatusCode);
        Assert.Equal("This gallery changed in another tab. Reload to see the current order.", stale.Reason);
        Assert.Equal(OperationStatus.Stale, dup.Status);
        Assert.Equal(OperationStatus.Refused, cover.Status);
        Assert.Equal(422, cover.StatusCode);
        var g = await GalleryAsync(h, gid);
        Assert.Equal(ids, g.Items);
        Assert.Null(g.PinnedCover);
        Assert.Empty(purged);
        Assert.Equal(2, h.LogLines.Count(l => l.StartsWith("op.timing db.gallery.update ", StringComparison.Ordinal) && l.EndsWith(" stale", StringComparison.Ordinal)));

        // A full-set reorder is stored and purges the moved photos' pages.
        var ok = await Gs(h, s => s.ReorderAsync(gid, [ids[1], ids[0], ids[2]]));
        var purgedOk = await h.SettleAsync();
        Assert.Equal(OperationStatus.Ok, ok.Status);
        Assert.Equal(new[] { ids[1], ids[0], ids[2] }, (await GalleryAsync(h, gid)).Items.ToArray());
        // a: (null,b)→(b,c); b: (a,c)→(null,a); c: (b,null)→(a,null) — all three changed.
        SetEq([Gp(gid, "order"), Pp(gid, "order", ids[0]), Pp(gid, "order", ids[1]), Pp(gid, "order", ids[2])], purgedOk);
    }

    // ── home order (G3) ──────────────────────────────────────────────────────

    [Fact]
    public async Task Home_reorder_needs_the_exact_set_and_purges_home_only_when_listed_order_changes()
    {
        await using var h = await NewHostAsync();
        var imgs = await ImagesAsync(h, 2);
        var a = await CreateAsync(h, "Alpha");
        var b = await CreateAsync(h, "Beta");
        var p = await CreateAsync(h, "Private");
        await Gs(h, s => s.AddAsync(a, [imgs[0].Id]));
        await Gs(h, s => s.AddAsync(b, [imgs[1].Id]));
        await Gs(h, s => s.PublishAsync(a, true));
        await Gs(h, s => s.PublishAsync(b, true));
        await h.SettleAsync();

        async Task<List<string>> OrderAsync() =>
            (await h.Db.GetCollection<BsonDocument>("site").Find(new BsonDocument("_id", "site")).FirstAsync())
                ["homeOrder"].AsBsonArray.Select(v => v.AsString).ToList();
        Assert.Equal(new[] { a, b, p }, (await OrderAsync()).ToArray());

        // Missing one id, or a duplicate: stale, nothing stored, nothing purged.
        var missing = await Gs(h, s => s.ReorderHomeAsync([b, a]));
        var dup = await Gs(h, s => s.ReorderHomeAsync([b, a, a]));
        Assert.Equal((OperationStatus.Stale, 409), (missing.Status, missing.StatusCode));
        Assert.Equal("The gallery list changed in another tab. Reload to see the current order.", missing.Reason);
        Assert.Equal(OperationStatus.Stale, dup.Status);
        Assert.Equal(new[] { a, b, p }, (await OrderAsync()).ToArray());
        Assert.Empty(await h.SettleAsync());

        // Moving only the private gallery changes no listed order: stored, no purge.
        var privateOnly = await Gs(h, s => s.ReorderHomeAsync([p, a, b]));
        Assert.Equal(OperationStatus.Ok, privateOnly.Status);
        Assert.Equal(new[] { p, a, b }, (await OrderAsync()).ToArray());
        Assert.Empty(await h.SettleAsync());

        // Swapping the listed galleries: stored, home purged, home cards follow the new order.
        var swap = await Gs(h, s => s.ReorderHomeAsync([p, b, a]));
        Assert.Equal(OperationStatus.Ok, swap.Status);
        Assert.Equal(new[] { p, b, a }, (await OrderAsync()).ToArray());
        SetEq([Home], await h.SettleAsync());
        var cards = (await h.Db.GetCollection<SiteDoc>("site").Find(s => s.Id == "site")
            .Project<SiteDoc>(Builders<SiteDoc>.Projection.Include(s => s.HomeCards)).FirstAsync()).HomeCards;
        Assert.Equal(new[] { b, a }, cards.Select(c => c.GalleryId).ToArray());
    }

    [Fact]
    public async Task Removing_the_pinned_cover_clears_the_pin_and_the_card_falls_back_to_the_first_item()
    {
        await using var h = await NewHostAsync();
        var imgs = await ImagesAsync(h, 3);
        var (a, b, c) = (imgs[0].Id, imgs[1].Id, imgs[2].Id);
        var gid = await CreateAsync(h, "Pinned");
        await Gs(h, s => s.AddAsync(gid, [a, b, c]));
        await h.SettleAsync();

        var pin = await Gs(h, s => s.SetCoverAsync(gid, c));
        var purgedPin = await h.SettleAsync();
        Assert.Equal(OperationStatus.Ok, pin.Status);
        Assert.Equal(c, (await GalleryAsync(h, gid)).Card!.Cover!.ImageId);
        // A pin changes no membership or neighbours: the gallery page only (private, not listed).
        SetEq([Gp(gid, "pinned")], purgedPin);

        var removed = await Gs(h, s => s.RemoveAsync(gid, c));
        var purged = await h.SettleAsync();

        Assert.Equal(1, removed.Removed);
        var g = await GalleryAsync(h, gid);
        Assert.Null(g.PinnedCover);
        Assert.Equal(new[] { a, b }, g.Items.ToArray());
        Assert.Equal(a, g.Card!.Cover!.ImageId);
        Assert.Equal(2, g.Card.ImageCount);
        Assert.Empty((await h.Images.Find(x => x.Id == c).FirstAsync()).GalleryIds);
        SetEq([Gp(gid, "pinned"), Pp(gid, "pinned", b), Pp(gid, "pinned", c)], purged);

        // Idempotent: removing a non-member answers 0 and purges nothing.
        var again = await Gs(h, s => s.RemoveAsync(gid, c));
        Assert.Equal(0, again.Removed);
        Assert.Empty(await h.SettleAsync());
    }

    // ── rename / description ─────────────────────────────────────────────────

    [Fact]
    public async Task Rename_purges_both_slugs_and_a_same_slug_rename_purges_one()
    {
        await using var h = await NewHostAsync();
        var imgs = await ImagesAsync(h, 2);
        var (a, b) = (imgs[0].Id, imgs[1].Id);
        var gid = await CreateAsync(h, "Autumn Walk");
        await Gs(h, s => s.AddAsync(gid, [a, b]));
        await Gs(h, s => s.PublishAsync(gid, confirmed: true));
        await h.SettleAsync();

        var renamed = await Gs(h, s => s.UpdateTextAsync(gid, "Winter Light", null));
        var purged = await h.SettleAsync();

        Assert.Equal(OperationStatus.Ok, renamed.Status);
        var g = await GalleryAsync(h, gid);
        Assert.Equal("winter-light", g.Slug);
        Assert.Equal("Winter Light", g.Card!.Title);
        SetEq(
        [
            Gp(gid, "autumn-walk"), Pp(gid, "autumn-walk", a), Pp(gid, "autumn-walk", b),
            Gp(gid, "winter-light"), Pp(gid, "winter-light", a), Pp(gid, "winter-light", b),
            Home,
        ], purged);
        Assert.Equal("Winter Light", (await SiteAsync(h)).HomeCards.Single().Title);

        var same = await Gs(h, s => s.UpdateTextAsync(gid, "Winter light!", null));
        var purgedSame = await h.SettleAsync();

        Assert.Equal(OperationStatus.Ok, same.Status);
        Assert.Equal("winter-light", (await GalleryAsync(h, gid)).Slug);
        SetEq([Gp(gid, "winter-light"), Pp(gid, "winter-light", a), Pp(gid, "winter-light", b), Home], purgedSame);
    }

    [Fact]
    public async Task Description_only_purges_the_gallery_page_and_null_fields_keep_stored_values()
    {
        await using var h = await NewHostAsync();
        var img = (await ImagesAsync(h, 1))[0];
        var gid = await CreateAsync(h, "Notes");
        await Gs(h, s => s.AddAsync(gid, [img.Id]));
        await h.SettleAsync();

        var r = await Gs(h, s => s.UpdateTextAsync(gid, null, "  A quiet morning.  "));
        var purged = await h.SettleAsync();
        Assert.Equal(OperationStatus.Ok, r.Status);
        var g = await GalleryAsync(h, gid);
        Assert.Equal("Notes", g.Title);
        Assert.Equal("A quiet morning.", g.Description);
        SetEq([Gp(gid, "notes")], purged);

        var bare = await Gs(h, s => s.UpdateTextAsync(gid, null, null));
        Assert.Equal(OperationStatus.Ok, bare.Status);
        Assert.Empty(await h.SettleAsync());
        var tooLong = await Gs(h, s => s.UpdateTextAsync(gid, null, new string('d', 5001)));
        Assert.Equal(422, tooLong.StatusCode);
        Assert.Equal("The description can be at most 5000 characters.", tooLong.Reason);
        var blank = await Gs(h, s => s.UpdateTextAsync(gid, "   ", null));
        Assert.Equal(422, blank.StatusCode);
        var after = await GalleryAsync(h, gid);
        Assert.Equal(("Notes", "A quiet morning."), (after.Title, after.Description));
        Assert.Empty(await h.SettleAsync());
    }

    // ── publish / unpublish [SAFETY] II.5, II.6 ──────────────────────────────

    [Fact]
    public async Task Publish_of_an_empty_gallery_is_refused_first_even_when_confirmed()
    {
        await using var h = await NewHostAsync();
        var gid = await CreateAsync(h, "Empty");
        await h.SettleAsync();
        var homeBefore = new BsonArray((await SiteAsync(h)).HomeCards.Select(c => c.ToBsonDocument()));

        foreach (var confirmed in new[] { true, false })
        {
            var r = await Gs(h, s => s.PublishAsync(gid, confirmed));
            Assert.Equal(OperationStatus.Refused, r.Status);
            Assert.Equal(409, r.StatusCode);
            Assert.Equal("Add a photo before making this gallery public", r.Reason);
        }

        Assert.Empty(await h.SettleAsync());
        Assert.Equal("private", (await GalleryAsync(h, gid)).Visibility);
        Assert.Equal(homeBefore, new BsonArray((await SiteAsync(h)).HomeCards.Select(c => c.ToBsonDocument())));
        Assert.Equal(0, h.Recorder.CallCount);
        Assert.DoesNotContain(h.LogLines, l => l.StartsWith("owner.action gallery.publish", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Publish_without_confirmation_writes_nothing()
    {
        await using var h = await NewHostAsync();
        var img = (await ImagesAsync(h, 1))[0];
        var gid = await CreateAsync(h, "Unconfirmed");
        await Gs(h, s => s.AddAsync(gid, [img.Id]));
        await h.SettleAsync();

        var r = await Gs(h, s => s.PublishAsync(gid, confirmed: false));

        Assert.Equal(OperationStatus.NeedsConfirm, r.Status);
        Assert.Empty(await h.SettleAsync());
        Assert.Equal("private", (await GalleryAsync(h, gid)).Visibility);
        Assert.Empty((await SiteAsync(h)).HomeCards);
    }

    [Fact]
    public async Task Publish_races_a_remove_and_the_conditional_write_refuses()
    {
        await using var h = await NewHostAsync();
        var img = (await ImagesAsync(h, 1))[0];
        var gid = await CreateAsync(h, "Race");
        await Gs(h, s => s.AddAsync(gid, [img.Id]));
        await h.SettleAsync();

        // Another tab removes the only photo between the load and the write.
        Func<string, Task> emptyIt = async id =>
        {
            using var other = h.Scope();
            await other.ServiceProvider.GetRequiredService<GalleryService>().RemoveAsync(id, img.Id);
        };
        OperationResult r;
        using (var scope = h.Scope())
        {
            var service = ActivatorUtilities.CreateInstance<GalleryService>(scope.ServiceProvider, emptyIt);
            r = await service.PublishAsync(gid, confirmed: true);
        }
        await h.SettleAsync();

        Assert.Equal(OperationStatus.Refused, r.Status);
        Assert.Equal(409, r.StatusCode);
        Assert.Equal("Add a photo before making this gallery public", r.Reason);
        var g = await GalleryAsync(h, gid);
        Assert.Equal("private", g.Visibility);
        Assert.Empty(g.Items);
        Assert.Empty((await SiteAsync(h)).HomeCards);
        Assert.DoesNotContain(h.LogLines, l => l.StartsWith("owner.action gallery.publish", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Publish_then_unpublish_lists_and_unlists_with_P_VIS_and_owner_action_lines()
    {
        await using var h = await NewHostAsync();
        var imgs = await ImagesAsync(h, 2);
        var (a, b) = (imgs[0].Id, imgs[1].Id);
        var gid = await CreateAsync(h, "Shown");
        await Gs(h, s => s.AddAsync(gid, [a, b]));
        await h.SettleAsync();
        var expected = new[] { Gp(gid, "shown"), Pp(gid, "shown", a), Pp(gid, "shown", b), Home };

        var pub = await Gs(h, s => s.PublishAsync(gid, confirmed: true));
        var purgedPub = await h.SettleAsync();

        Assert.Equal(OperationStatus.Ok, pub.Status);
        Assert.Equal("public", (await GalleryAsync(h, gid)).Visibility);
        Assert.Equal(new[] { gid }, (await SiteAsync(h)).HomeCards.Select(c => c.GalleryId).ToArray());
        SetEq(expected, purgedPub);
        Assert.Single(h.LogLines, l => l == $"owner.action gallery.publish {gid} 1 photos=2");

        var unpub = await Gs(h, s => s.UnpublishAsync(gid));
        var purgedUnpub = await h.SettleAsync();

        Assert.Equal(OperationStatus.Ok, unpub.Status);
        Assert.Equal("private", (await GalleryAsync(h, gid)).Visibility);
        var site = await SiteAsync(h);
        Assert.Empty(site.HomeCards);
        Assert.Equal(new[] { gid }, site.HomeOrder.ToArray());
        SetEq(expected, purgedUnpub);
        Assert.Single(h.LogLines, l => l == $"owner.action gallery.unpublish {gid} 1 photos=2");
    }

    // ── delete (L-17) ────────────────────────────────────────────────────────

    [Fact]
    public async Task Delete_gallery_keeps_its_images_in_their_other_galleries()
    {
        await using var h = await NewHostAsync();
        var imgs = await ImagesAsync(h, 2);
        var (a, b) = (imgs[0].Id, imgs[1].Id);
        var doomed = await CreateAsync(h, "Doomed");
        var kept = await CreateAsync(h, "Kept");
        await Gs(h, s => s.AddAsync(doomed, [a, b]));
        await Gs(h, s => s.AddAsync(kept, [a]));
        await Gs(h, s => s.PublishAsync(doomed, confirmed: true));
        await h.SettleAsync();

        var r = await Gs(h, s => s.DeleteAsync(doomed));
        var purged = await h.SettleAsync();

        Assert.Equal(OperationStatus.Ok, r.Status);
        Assert.Equal(0, await h.Galleries.CountDocumentsAsync(g => g.Id == doomed));
        Assert.Equal(2, await h.Images.CountDocumentsAsync(FilterDefinition<ImageDoc>.Empty));
        Assert.Equal(new[] { kept }, (await h.Images.Find(x => x.Id == a).FirstAsync()).GalleryIds.ToArray());
        Assert.Empty((await h.Images.Find(x => x.Id == b).FirstAsync()).GalleryIds);
        Assert.Equal(new[] { a }, (await GalleryAsync(h, kept)).Items.ToArray());
        var site = await SiteAsync(h);
        Assert.Equal(new[] { kept }, site.HomeOrder.ToArray());
        Assert.Empty(site.HomeCards);
        SetEq([Gp(doomed, "doomed"), Pp(doomed, "doomed", a), Pp(doomed, "doomed", b), Home], purged);
        Assert.Single(h.LogLines, l => l == $"owner.action gallery.delete {doomed} 1 photos=2");

        var again = await Gs(h, s => s.DeleteAsync(doomed));
        Assert.Equal(OperationStatus.NotFound, again.Status);
        Assert.Empty(await h.SettleAsync());
    }
}
