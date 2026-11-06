using Gallery.Web.Data;
using Gallery.Web.Edge;

namespace Gallery.Web.Tests;

/// <summary>
/// 013 T022 (contracts/edge.md E1, E2, E4; research R-06): the camera purge sets as pure facts.
/// Every row of the edge.md worked-examples table, a slug change, P-CAMTEXT never naming <c>/c</c>,
/// [SAFETY] no camera set ever naming a <c>/g/</c> path or <c>/</c>, and <see cref="PurgeSets.WarmFiles"/>
/// with <c>cameraCards</c> null giving today's result on the <see cref="PurgeSetTests"/> inputs.
/// Sets compare for EQUALITY.
/// </summary>
public class CameraPurgeSetTests
{
    private static readonly EdgeBases Bases = new("https://site.test", "https://img.test");

    private static CameraSnapshot K(params string[] items) => new("canon\u001fcanon eos r5", "k", items);

    private static void Eq(IEnumerable<string> expected, PurgeSet actual)
    {
        Assert.Equal(expected.Order(StringComparer.Ordinal).ToArray(), actual.PagePaths.ToArray());
        Assert.Empty(actual.FileKeys);
    }

    // ── the worked-examples table ────────────────────────────────────────────

    [Fact]
    public void Publish_a_gallery_holding_a_and_b_purges_the_listing_the_camera_and_both_photos()
    {
        var set = PurgeSets.Cameras([(null, K("b", "a"))], cardsChanged: true, Bases);
        Eq(["/c", "/c/k", "/c/k/a", "/c/k/b"], set);
        Assert.Equal(new[] { "https://site.test/c", "https://site.test/c/k", "https://site.test/c/k/a", "https://site.test/c/k/b" },
            set.Pages.ToArray());
    }

    [Fact]
    public void Unpublish_the_only_listed_holder_purges_the_listing_the_camera_and_both_photos()
    {
        Eq(["/c", "/c/k", "/c/k/a", "/c/k/b"], PurgeSets.Cameras([(K("b", "a"), null)], cardsChanged: true, Bases));
    }

    [Fact]
    public void Adding_a_newest_photo_purges_the_listing_the_camera_and_every_photo()
    {
        Eq(["/c", "/c/k", "/c/k/a", "/c/k/b", "/c/k/c"],
            PurgeSets.Cameras([(K("b", "a"), K("c", "b", "a"))], cardsChanged: true, Bases));
    }

    [Fact]
    public void Retitling_a_purges_the_camera_page_and_its_photo_page_only()
    {
        Eq(["/c/k", "/c/k/a"], PurgeSets.CameraText("a", [K("b", "a")], Bases));
    }

    [Fact]
    public void Reordering_inside_a_gallery_changes_no_camera_and_purges_nothing()
    {
        // The scope passes only keys whose document changed; a reorder changes none.
        Assert.True(PurgeSets.Cameras([], cardsChanged: false, Bases).IsEmpty);
    }

    // ── more facts ───────────────────────────────────────────────────────────

    [Fact]
    public void A_slug_change_purges_both_slugs()
    {
        var before = new CameraSnapshot("key", "old", ["a", "b"]);
        var after = new CameraSnapshot("key", "new", ["a", "b"]);
        Eq(["/c", "/c/new", "/c/old"], PurgeSets.Cameras([(before, after)], cardsChanged: true, Bases));

        // With a photo leaving as well: its page under the old slug, the moved ones under both.
        var shrunk = new CameraSnapshot("key", "new", ["b"]);
        Eq(["/c/new", "/c/new/b", "/c/old", "/c/old/a", "/c/old/b"],
            PurgeSets.Cameras([(before, shrunk)], cardsChanged: false, Bases));
    }

    [Fact]
    public void Photo_pages_follow_the_Changed_rule_over_the_two_item_lists()
    {
        // Same count: only ids whose neighbours or index moved (a and b swap; c keeps index 2 but
        // its prev changes from b to a).
        Eq(["/c/k", "/c/k/a", "/c/k/b", "/c/k/c"],
            PurgeSets.Cameras([(K("a", "b", "c", "d"), K("b", "a", "c", "d"))], cardsChanged: false, Bases));
        Eq(["/c/k", "/c/k/a", "/c/k/b", "/c/k/c"],
            PurgeSets.Cameras([(K("a", "b", "c", "d", "e"), K("b", "a", "c", "d", "e"))], cardsChanged: false, Bases));
        // d and e unchanged.
    }

    [Fact]
    public void Two_cameras_each_purge_their_own_pages()
    {
        var k = new CameraSnapshot("k1", "canon", ["a"]);
        var n = new CameraSnapshot("k2", "nikon", ["x", "y"]);
        Eq(["/c", "/c/canon", "/c/canon/a", "/c/nikon", "/c/nikon/x", "/c/nikon/y"],
            PurgeSets.Cameras([(k, null), (null, n)], cardsChanged: true, Bases));
    }

    [Fact]
    public void CameraText_never_contains_the_listing_and_ignores_cameras_without_the_photo()
    {
        var cams = new[]
        {
            new CameraSnapshot("k1", "canon", ["a", "b"]),
            new CameraSnapshot("k2", "nikon", ["c"]),
        };
        foreach (var iid in new[] { "a", "b", "c", "zz" })
            Assert.DoesNotContain("/c", PurgeSets.CameraText(iid, cams, Bases).PagePaths);
        Eq(["/c/canon", "/c/canon/b"], PurgeSets.CameraText("b", cams, Bases));
        Assert.True(PurgeSets.CameraText("zz", cams, Bases).IsEmpty);
        Assert.True(PurgeSets.CameraText("a", [], Bases).IsEmpty);
    }

    [Fact]
    public void Camera_pages_warm_their_first_four_photos_and_camera_photo_pages_their_photo()
    {
        // 013 E4 (parent addition): ids resolve to AVIF keys through avifKeysOf, as for /g pages.
        var cam = new CameraSnapshot("x", "k", ["a", "b", "c", "d", "e"]);
        var purged = new PurgeSet(Bases, ["/c/k", "/c/k/e", "/c/gone"], []);
        var warm = PurgeSets.WarmFiles(purged, [], [], iid => [$"{iid}.avif"], cameraCards: null, cameras: [cam]);
        Assert.Equal(
            new[] { "a.avif", "b.avif", "c.avif", "d.avif", "e.avif" }.Select(Bases.File).OrderBy(x => x, StringComparer.Ordinal),
            warm.OrderBy(x => x, StringComparer.Ordinal));

        // No AFTER camera for a slug: nothing to warm.
        Assert.Empty(PurgeSets.WarmFiles(new PurgeSet(Bases, ["/c/k"], []), [], [], iid => [$"{iid}.avif"]));
    }

    [Fact]
    public void Camera_path_helpers_have_the_contract_shapes()
    {
        Assert.Equal("/c", PurgeSets.CameraListingPath);
        Assert.Equal("/c/canon-eos-r5", PurgeSets.CameraPath("canon-eos-r5"));
        Assert.Equal("/c/canon-eos-r5/i1", PurgeSets.CameraPhotoPath("canon-eos-r5", "i1"));
    }

    [Fact]
    public void CameraSnapshot_From_copies_key_slug_and_items()
    {
        var doc = new CameraGroupDoc { Id = "key", Slug = "s", Name = "N", Items = ["b", "a"], Rank = 3 };
        var snap = CameraSnapshot.From(doc);
        Assert.Equal(("key", "s"), (snap.Key, snap.Slug));
        Assert.Equal(new[] { "b", "a" }, snap.Items.ToArray());
        doc.Items.Add("c");
        Assert.Equal(2, snap.Items.Count); // a copy, not a view
    }

    // ── [SAFETY] II.2 / II.5: no camera set names a gallery page or the home page ──

    public static TheoryData<string> Shapes() => ["publish", "unpublish", "add", "remove", "slug", "reorder", "two", "text"];

    private static IEnumerable<PurgeSet> SetsFor(string shape) => shape switch
    {
        "publish" => [PurgeSets.Cameras([(null, K("b", "a"))], true, Bases)],
        "unpublish" => [PurgeSets.Cameras([(K("b", "a"), null)], true, Bases)],
        "add" => [PurgeSets.Cameras([(K("b", "a"), K("c", "b", "a"))], true, Bases)],
        "remove" => [PurgeSets.Cameras([(K("c", "b", "a"), K("c", "a"))], true, Bases)],
        "slug" => [PurgeSets.Cameras([(new CameraSnapshot("x", "old", ["a"]), new CameraSnapshot("x", "new", ["a", "b"]))], true, Bases)],
        "reorder" => [PurgeSets.Cameras([(K("a", "b"), K("b", "a"))], false, Bases)],
        "two" => [PurgeSets.Cameras([(K("a"), null), (null, new CameraSnapshot("y", "g", ["g"]))], true, Bases)],
        "text" => [PurgeSets.CameraText("a", [K("b", "a"), new CameraSnapshot("y", "g", ["a"])], Bases)],
        _ => throw new ArgumentOutOfRangeException(nameof(shape)),
    };

    [Theory]
    [MemberData(nameof(Shapes))]
    public void No_camera_set_ever_contains_a_gallery_path_or_the_home_page(string shape)
    {
        foreach (var set in SetsFor(shape))
        {
            Assert.NotEmpty(set.PagePaths); // non-vacuity
            Assert.Empty(set.FileKeys);
            Assert.DoesNotContain("/", set.PagePaths);
            Assert.DoesNotContain(set.PagePaths, p => p == "/g" || p.StartsWith("/g/", StringComparison.Ordinal));
            Assert.All(set.PagePaths, p => Assert.True(p == "/c" || p.StartsWith("/c/", StringComparison.Ordinal), p));
            Assert.All(set.Pages, p => Assert.StartsWith("https://site.test/c", p));
        }
    }

    // ── WarmFiles: cameraCards null is today's output (copied PurgeSetTests inputs) ──

    private static GallerySnapshot G(string id, string slug, string vis, params string[] items) =>
        new(id, slug, vis, items);

    private static IReadOnlyList<string>? Avif(string id) => [$"{id}-320.avif", $"{id}-640.avif"];

    private static string[] F(params string[] keys) =>
        keys.Select(k => "https://img.test/" + k).Order(StringComparer.Ordinal).ToArray();

    private static GalleryCardDoc Card(string gid, string cover) => new()
    {
        GalleryId = gid,
        Slug = gid,
        Title = gid,
        ImageCount = 1,
        Cover = new CoverImageDoc
        {
            ImageId = cover,
            Derivatives = [new DerivativeDoc { W = 320, Avif = $"{cover}-320.avif", Webp = $"{cover}-320.webp" }],
        },
    };

    private static CameraCardDoc CamCard(string slug, string cover) => new()
    {
        Slug = slug,
        Name = slug,
        ImageCount = 1,
        Cover = new CoverImageDoc
        {
            ImageId = cover,
            Derivatives =
            [
                new DerivativeDoc { W = 320, Avif = $"{cover}-320.avif", Webp = $"{cover}-320.webp" },
                new DerivativeDoc { W = 640, Avif = $"{cover}-640.avif", Webp = $"{cover}-640.webp" },
            ],
        },
    };

    /// <summary>The <see cref="PurgeSetTests"/> WarmFiles inputs and their expected outputs, verbatim.</summary>
    public static TheoryData<string> WarmCases() => ["six", "home", "photo", "deleted-gallery", "emptied", "renamed", "deleted-key"];

    private static (PurgeSet Purged, GallerySnapshot[] After, GalleryCardDoc[] Home, string[] Expected) WarmCase(string name)
    {
        switch (name)
        {
            case "six":
            {
                var g = G("g1", "s", "public", "p1", "p2", "p3", "p4", "p5", "p6");
                return (PurgeSets.Description(g, g, Bases), [g], [],
                    F("p1-320.avif", "p1-640.avif", "p2-320.avif", "p2-640.avif", "p3-320.avif", "p3-640.avif", "p4-320.avif", "p4-640.avif"));
            }
            case "home":
                return (PurgeSets.Order(["g1", "g2"], ["g2", "g1"], [G("g1", "a", "public", "x"), G("g2", "b", "public", "y")], Bases), [],
                    [Card("g1", "c1"), Card("g2", "c2"), Card("g3", "c3"), Card("g4", "c4")],
                    F("c1-320.avif", "c2-320.avif", "c3-320.avif"));
            case "photo":
            {
                var b = G("g1", "s", "private", "a", "b", "c");
                return (PurgeSets.Text("c", [b], Bases), [b], [],
                    F("a-320.avif", "a-640.avif", "b-320.avif", "b-640.avif", "c-320.avif", "c-640.avif"));
            }
            case "deleted-gallery":
                return (PurgeSets.GalleryDelete(G("g1", "s", "private", "a", "b"), Bases), [], [], []);
            case "emptied":
                return (PurgeSets.Members(G("g1", "s", "private", "a"), G("g1", "s", "private"), Bases), [G("g1", "s", "private")], [], []);
            case "renamed":
                return (PurgeSets.Rename(G("g1", "old", "private", "a"), G("g1", "new", "private", "a"), Bases),
                    [G("g1", "new", "private", "a")], [], F("a-320.avif", "a-640.avif"));
            case "deleted-key":
            {
                var before = G("g1", "s", "private", "a", "b");
                var after = G("g1", "s", "private", "b");
                return (PurgeSets.Delete([(before, after)], ["a-320.avif", "a-640.avif", "b-320.avif"], Bases), [after], [],
                    F("b-640.avif"));
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(name));
        }
    }

    [Theory]
    [MemberData(nameof(WarmCases))]
    public void WarmFiles_with_cameraCards_null_or_omitted_equals_todays_result(string name)
    {
        var (purged, after, home, expected) = WarmCase(name);
        var omitted = PurgeSets.WarmFiles(purged, after, home, Avif);
        var nulled = PurgeSets.WarmFiles(purged, after, home, Avif, cameraCards: null);
        Assert.Equal(expected, omitted.ToArray());
        Assert.Equal(expected, nulled.ToArray());
        // No /c page in these sets: camera cards change nothing either.
        Assert.Equal(expected, PurgeSets.WarmFiles(purged, after, home, Avif, [CamCard("k", "z")]).ToArray());
    }

    [Fact]
    public void WarmFiles_camera_listing_warms_the_first_three_camera_card_covers()
    {
        var purged = PurgeSets.Cameras([(null, K("b", "a"))], cardsChanged: true, Bases);
        var cards = new[] { CamCard("k", "c1"), CamCard("n", "c2"), CamCard("s", "c3"), CamCard("f", "c4") };
        // The /c/k pages are not warmed by WarmFiles (the scope supplies their ids): use a lookup
        // that knows none of them, so only the card covers appear.
        var warm = PurgeSets.WarmFiles(purged, [], [], _ => null, cards);
        Assert.Equal(F("c1-320.avif", "c1-640.avif", "c2-320.avif", "c2-640.avif", "c3-320.avif", "c3-640.avif"), warm.ToArray());

        // Without cards the listing warms nothing.
        Assert.Empty(PurgeSets.WarmFiles(purged, [], [], _ => null));
    }

    [Fact]
    public void WarmFiles_skips_camera_pages_and_never_returns_a_deleted_key()
    {
        var text = PurgeSets.CameraText("a", [K("b", "a")], Bases);
        Assert.Empty(PurgeSets.WarmFiles(text, [], [], Avif, [CamCard("k", "b")]));

        var listing = PurgeSet.Union(Bases,
        [
            PurgeSets.Cameras([(null, K("b"))], true, Bases),
            PurgeSets.Delete([], ["c1-640.avif"], Bases),
        ]);
        Assert.Equal(F("c1-320.avif"), PurgeSets.WarmFiles(listing, [], [], Avif, [CamCard("k", "c1")]).ToArray());
    }
}
