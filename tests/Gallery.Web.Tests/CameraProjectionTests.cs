using Gallery.Web.Data;
using Gallery.Web.Library;
using Gallery.Web.Tests.Fixtures;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Gallery.Web.Tests;

/// <summary>
/// 013 T007 [SAFETY] II.2 / II.5 (data-model "Visibility", "Pure projection", "Write"; research
/// R-02, R-03, R-05): the pure <see cref="SiteProjector.Cameras"/> facts, then the stored
/// <c>cameras</c> collection written by <see cref="SiteProjector.ProjectWithNavAsync"/> through
/// <see cref="LibraryHost"/> with galleries built by <see cref="GalleryService"/>.
/// </summary>
[Collection("s3")]
public class CameraProjectionTests(S3ProxyFixture s3, MongoFixture mongo) : IClassFixture<MongoFixture>
{
    private static readonly DateTime T0 = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
    private static readonly IReadOnlyDictionary<string, string> NoSlugs = new Dictionary<string, string>();

    private static ImageDoc Img(string? make, string? model, int minutes = 0, string? id = null)
    {
        var i = SeedShapes.UploadedImage(id: id, createdAt: T0.AddMinutes(minutes));
        i.Camera = make is null && model is null ? null : new CameraDoc { Make = make, Model = model };
        return i;
    }

    private static GalleryDoc Pub(params ImageDoc[] members) => SeedShapes.Gallery(members, visibility: "public");
    private static GalleryDoc Priv(params ImageDoc[] members) => SeedShapes.Gallery(members, visibility: "private");

    /// <summary>The listed galleries, by the same rule <see cref="SiteProjector.Cards"/> uses.</summary>
    private static IReadOnlyList<GalleryDoc> Listed(params GalleryDoc[] galleries) =>
        SiteProjector.Listed(galleries.Select(g => g.Id), galleries);

    private static List<CameraGroupDoc> Project(GalleryDoc[] galleries, ImageDoc[] images,
        IReadOnlyDictionary<string, string>? slugs = null) =>
        SiteProjector.Cameras(Listed(galleries), images, slugs ?? NoSlugs);

    private static HashSet<string> Ids(CameraGroupDoc c) => new(c.Items, StringComparer.Ordinal);

    // ── pure: visibility ─────────────────────────────────────────────────────

    [Fact]
    public void A_private_only_photo_is_excluded()
    {
        var pub = Img("Canon", "Canon EOS R5", 1);
        var hidden = Img("Sony", "ILCE-7M4", 2);
        var hiddenSame = Img("Canon", "Canon EOS R5", 3);
        var cams = Project([Pub(pub), Priv(hidden, hiddenSame)], [pub, hidden, hiddenSame]);

        var only = Assert.Single(cams);
        Assert.Equal("canon\u001fcanon eos r5", only.Id);
        Assert.True(Ids(only).SetEquals([pub.Id]));
    }

    [Fact]
    public void A_photo_in_no_gallery_is_excluded()
    {
        var listedImg = Img("Canon", "Canon EOS R5", 1);
        var loose = Img("Canon", "Canon EOS R5", 2);
        var looseOther = Img("Sony", "ILCE-7M4", 3);
        var cams = Project([Pub(listedImg)], [listedImg, loose, looseOther]);

        var only = Assert.Single(cams);
        Assert.True(Ids(only).SetEquals([listedImg.Id]));
    }

    [Fact]
    public void A_photo_in_a_private_and_a_public_gallery_is_included_once()
    {
        var both = Img("Canon", "Canon EOS R5", 1);
        var cams = Project([Priv(both), Pub(both)], [both]);

        var only = Assert.Single(cams);
        Assert.Equal(new[] { both.Id }, only.Items.ToArray());
        Assert.True(only.Card.ImageCount == 1);
    }

    [Fact]
    public void A_photo_in_two_public_galleries_is_included_once()
    {
        var both = Img("Canon", "Canon EOS R5", 1);
        var other = Img("Canon", "Canon EOS R5", 2);
        // The same document twice in the images list must not double-count either.
        var cams = Project([Pub(both, other), Pub(both)], [both, other, both]);

        var only = Assert.Single(cams);
        Assert.Equal(new[] { other.Id, both.Id }, only.Items.ToArray());
        Assert.True(only.Card.ImageCount == 2);
    }

    [Fact]
    public void A_tombstoned_photo_is_excluded()
    {
        var live = Img("Canon", "Canon EOS R5", 1);
        var dying = Img("Canon", "Canon EOS R5", 2);
        dying.DeletingAt = T0;
        var dyingAlone = Img("Sony", "ILCE-7M4", 3);
        dyingAlone.DeletingAt = T0;
        var cams = Project([Pub(live, dying, dyingAlone)], [live, dying, dyingAlone]);

        var only = Assert.Single(cams);
        Assert.True(Ids(only).SetEquals([live.Id]));
    }

    [Fact]
    public void A_photo_without_make_or_model_is_excluded()
    {
        var none = Img(null, null, 1);
        var blank = Img("  ", "", 2);
        var cams = Project([Pub(none, blank)], [none, blank]);
        Assert.Empty(cams);
    }

    [Fact]
    public void Case_and_space_variants_form_one_camera()
    {
        var a = Img("Canon", "Canon EOS R5", 1);
        var b = Img("CANON ", "canon  eos r5", 2);
        var c = Img(" canon", "CANON EOS R5 ", 3);
        var cams = Project([Pub(a, b, c)], [a, b, c]);

        var only = Assert.Single(cams);
        Assert.True(Ids(only).SetEquals([a.Id, b.Id, c.Id]));
        // Name from the newest photo (c), whitespace collapsed, case kept.
        Assert.Equal("CANON EOS R5", only.Name);
        Assert.Equal("canon-eos-r5", only.Slug);
    }

    // ── pure: order, rank, cover (R-03) ──────────────────────────────────────

    [Fact]
    public void Items_rank_and_cover_follow_R03()
    {
        // Items: createdAt desc, ties by _id ordinal.
        var tieA = Img("Sony", "ILCE-7M4", 5, id: "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
        var tieB = Img("Sony", "ILCE-7M4", 5, id: "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb");
        var older = Img("Sony", "ILCE-7M4", 1);
        // Two one-photo cameras: name "alpha" vs "Beta" ordinal ignore-case puts alpha first.
        var beta = Img(null, "Beta", 9);
        var alpha = Img(null, "alpha", 2);
        var cams = Project([Pub(older, tieB, beta, tieA, alpha)], [older, tieB, beta, tieA, alpha]);

        Assert.Equal(new[] { "Sony ILCE-7M4", "alpha", "Beta" }, cams.Select(c => c.Name).ToArray());
        Assert.Equal(new[] { 0, 1, 2 }, cams.Select(c => c.Rank).ToArray());
        var sony = cams[0];
        Assert.Equal(new[] { tieA.Id, tieB.Id, older.Id }, sony.Items.ToArray());

        Assert.Equal(sony.Slug, sony.Card.Slug);
        Assert.Equal(sony.Name, sony.Card.Name);
        Assert.True(sony.Card.ImageCount == 3);
        var expectedCover = CardBuilder.Build(Pub(tieA), tieA).Cover!;
        Assert.Equal(expectedCover.ToBsonDocument(), sony.Card.Cover.ToBsonDocument());
    }

    [Fact]
    public void Equal_counts_and_equal_names_rank_by_key()
    {
        // Same display name "X100V", different keys (make-only vs model-only), one photo each.
        var makeOnly = Img("X100V", null, 1);
        var modelOnly = Img(null, "X100V", 2);
        var cams = Project([Pub(makeOnly, modelOnly)], [makeOnly, modelOnly]);

        Assert.Equal(new[] { "\u001fx100v", "x100v\u001f" }, cams.Select(c => c.Id).ToArray());
        // Slugs are given in ordinal key order: the first key takes the bare slug.
        Assert.Equal(new[] { "x100v", "x100v-2" }, cams.Select(c => c.Slug).ToArray());
    }

    // ── pure: slug stability, determinism ────────────────────────────────────

    [Fact]
    public void A_stored_slug_is_kept_and_a_new_colliding_key_gets_dash_2()
    {
        // "x100v\u001f" already lives at /c/x100v; the new key "\u001fx100v" sorts first but must not take it.
        var makeOnly = Img("X100V", null, 1);
        var modelOnly = Img(null, "X100V", 2);
        var stored = new Dictionary<string, string> { ["x100v\u001f"] = "x100v" };
        var cams = Project([Pub(makeOnly, modelOnly)], [makeOnly, modelOnly], stored);

        var bySlug = cams.ToDictionary(c => c.Id, c => c.Slug);
        Assert.Equal("x100v", bySlug["x100v\u001f"]);
        Assert.Equal("x100v-2", bySlug["\u001fx100v"]);
    }

    [Fact]
    public void The_same_input_twice_gives_BSON_equal_output()
    {
        var images = new[]
        {
            Img("Canon", "Canon EOS R5", 1), Img("NIKON CORPORATION", "NIKON Z 8", 2),
            Img("Canon", "Canon EOS R5", 3), Img(null, "X100V", 4),
        };
        GalleryDoc[] galleries = [Pub(images[0], images[1]), Pub(images[2], images[3])];

        var first = new BsonArray(Project(galleries, images).Select(c => c.ToBsonDocument()));
        var second = new BsonArray(Project(galleries, images.Reverse().ToArray()).Select(c => c.ToBsonDocument()));
        Assert.Equal(first, second);
        Assert.True(first.Count == 3);
    }

    // ── LibraryHost: the stored collection ───────────────────────────────────

    private static Task<OperationResult> Gs(LibraryHost h, Func<GalleryService, Task<OperationResult>> call) =>
        h.RunAsync(call);

    private static Task<ImageDoc> CamImageAsync(LibraryHost h, string make, string model, int minutes) =>
        h.InsertUploadedImageAsync(T0.AddMinutes(minutes), files: false,
            shape: i => i.Camera = new CameraDoc { Make = make, Model = model });

    private static async Task<string> GalleryAsync(LibraryHost h, string title, IReadOnlyList<string> ids)
    {
        var created = await Gs(h, s => s.CreateAsync(title));
        Assert.True(created.IsOk);
        var gid = created.GalleryId!;
        Assert.True((await Gs(h, s => s.AddAsync(gid, ids))).IsOk);
        return gid;
    }

    private static Task<List<CameraGroupDoc>> StoredAsync(LibraryHost h) =>
        h.Db.GetCollection<CameraGroupDoc>("cameras").Find(FilterDefinition<CameraGroupDoc>.Empty)
            .SortBy(c => c.Rank).ToListAsync();

    private static int Count(LibraryHost h, string prefix)
    {
        lock (h.LogLines) return h.LogLines.Count(l => l.StartsWith(prefix, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Publish_writes_the_expected_cameras_and_unpublish_removes_them()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3);
        var r5a = await CamImageAsync(h, "Canon", "Canon EOS R5", 1);
        var r5b = await CamImageAsync(h, "CANON", "Canon  EOS R5", 2);
        var z8 = await CamImageAsync(h, "NIKON CORPORATION", "NIKON Z 8", 3);
        var hidden = await CamImageAsync(h, "Sony", "ILCE-7M4", 4);

        var g1 = await GalleryAsync(h, "Alpha", [r5a.Id, z8.Id]);
        var g2 = await GalleryAsync(h, "Bravo", [r5b.Id, r5a.Id]);
        await GalleryAsync(h, "Secret", [hidden.Id, z8.Id]); // stays private
        await h.SettleAsync();
        Assert.Empty(await StoredAsync(h));

        Assert.True((await Gs(h, s => s.PublishAsync(g1, confirmed: true))).IsOk);
        Assert.True((await Gs(h, s => s.PublishAsync(g2, confirmed: true))).IsOk);
        await h.SettleAsync();

        var stored = await StoredAsync(h);
        Assert.Equal(new[] { "canon\u001fcanon eos r5", "nikon corporation\u001fnikon z 8" }, stored.Select(c => c.Id).ToArray());
        var canon = stored[0];
        Assert.Equal("canon-eos-r5", canon.Slug);
        Assert.Equal("Canon EOS R5", canon.Name);
        Assert.Equal(new[] { r5b.Id, r5a.Id }, canon.Items.ToArray());
        Assert.True(canon.Rank == 0 && canon.Card.ImageCount == 2);
        Assert.Equal(r5b.Id, canon.Card.Cover.ImageId);
        var nikon = stored[1];
        Assert.Equal(("nikon-z-8", "NIKON Z 8", 1), (nikon.Slug, nikon.Name, nikon.Rank));
        Assert.Equal(new[] { z8.Id }, nikon.Items.ToArray());

        // Every stored document is BSON-equal to the pure projection over the full documents.
        var galleries = await h.Galleries.Find(FilterDefinition<GalleryDoc>.Empty).ToListAsync();
        var images = await h.Images.Find(FilterDefinition<ImageDoc>.Empty).ToListAsync();
        var order = (await h.SiteAsync()).HomeOrder;
        var expected = SiteProjector.Cameras(SiteProjector.Listed(order, galleries), images, NoSlugs);
        Assert.Equal(new BsonArray(expected.Select(c => c.ToBsonDocument())), new BsonArray(stored.Select(c => c.ToBsonDocument())));

        // Unpublish Bravo: the Canon keeps only r5a; unpublish Alpha too and every camera is gone.
        Assert.True((await Gs(h, s => s.UnpublishAsync(g2))).IsOk);
        await h.SettleAsync();
        stored = await StoredAsync(h);
        Assert.Equal(new[] { r5a.Id }, stored.Single(c => c.Id == "canon\u001fcanon eos r5").Items.ToArray());
        Assert.Equal("Canon EOS R5", stored.Single(c => c.Id == "canon\u001fcanon eos r5").Name);

        Assert.True((await Gs(h, s => s.UnpublishAsync(g1))).IsOk);
        await h.SettleAsync();
        Assert.Empty(await StoredAsync(h));
    }

    [Fact]
    public async Task A_projection_with_no_change_writes_no_camera_and_logs_one_site_project()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3);
        var a = await CamImageAsync(h, "Canon", "Canon EOS R5", 1);
        var b = await CamImageAsync(h, "FUJIFILM", "X100V", 2);
        var gid = await GalleryAsync(h, "Alpha", [a.Id, b.Id]);
        Assert.True((await Gs(h, s => s.PublishAsync(gid, confirmed: true))).IsOk);
        await h.SettleAsync();
        var before = new BsonArray((await StoredAsync(h)).Select(c => c.ToBsonDocument()));
        Assert.True(before.Count == 2);

        var writes0 = Count(h, "op.timing db.cameras.write ");
        var loads0 = Count(h, "op.timing db.cameras.load ");
        var projects0 = Count(h, "op.timing db.site.project ");

        var result = await h.RunAsync<SiteProjector, ProjectionResult>(p => p.ProjectWithNavAsync());

        Assert.Empty(result.Cameras);
        Assert.True(Count(h, "op.timing db.cameras.write ") - writes0 == 0);
        Assert.True(Count(h, "op.timing db.cameras.load ") - loads0 == 1);
        Assert.True(Count(h, "op.timing db.site.project ") - projects0 == 1);
        Assert.Equal(before, new BsonArray((await StoredAsync(h)).Select(c => c.ToBsonDocument())));
    }

    [Fact]
    public async Task A_changing_projection_writes_once_and_reports_each_changed_key()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3);
        var a = await CamImageAsync(h, "Canon", "Canon EOS R5", 1);
        var b = await CamImageAsync(h, "FUJIFILM", "X100V", 2);
        var gid = await GalleryAsync(h, "Alpha", [a.Id, b.Id]);
        await h.SettleAsync();

        // Make the gallery public behind the services' back, then project once by hand.
        await h.Galleries.UpdateOneAsync(g => g.Id == gid, Builders<GalleryDoc>.Update.Set(g => g.Visibility, "public"));
        var writes0 = Count(h, "op.timing db.cameras.write ");
        var projects0 = Count(h, "op.timing db.site.project ");

        var result = await h.RunAsync<SiteProjector, ProjectionResult>(p => p.ProjectWithNavAsync());

        Assert.True(Count(h, "op.timing db.cameras.write ") - writes0 == 1);
        Assert.True(Count(h, "op.timing db.site.project ") - projects0 == 1);
        Assert.True(result.Cameras.All(c => c.Before is null && c.After is not null));
        Assert.True(new HashSet<string>(result.Cameras.Select(c => c.After!.Id))
            .SetEquals(["canon\u001fcanon eos r5", "fujifilm\u001fx100v"]));

        // Back to private: both keys leave, After null, Before the stored document.
        await h.Galleries.UpdateOneAsync(g => g.Id == gid, Builders<GalleryDoc>.Update.Set(g => g.Visibility, "private"));
        var removed = await h.RunAsync<SiteProjector, ProjectionResult>(p => p.ProjectWithNavAsync());
        Assert.True(removed.Cameras.All(c => c.Before is not null && c.After is null));
        Assert.True(new HashSet<string>(removed.Cameras.Select(c => c.Before!.Id))
            .SetEquals(["canon\u001fcanon eos r5", "fujifilm\u001fx100v"]));
        Assert.Empty(await StoredAsync(h));
    }
}
