using Gallery.Web.Data;
using Gallery.Web.Edge;

namespace Gallery.Web.Tests;

/// <summary>
/// 009 T014 (SC-004, FR-038, FR-039): one fact per contracts/edge.md row plus the stated edge cases.
/// Every assertion compares the computed set for EQUALITY with the expected set: none missing,
/// none extra. Pure: no host, no datastore.
/// </summary>
public class PurgeSetTests
{
    private static readonly EdgeBases Bases = new("https://site.test", "https://img.test");

    private static GallerySnapshot G(string id, string slug, string vis, params string[] items) =>
        new(id, slug, vis, items);

    private static string[] P(params string[] paths) =>
        paths.Select(p => "https://site.test" + p).Order(StringComparer.Ordinal).ToArray();

    private static string[] F(params string[] keys) =>
        keys.Select(k => "https://img.test/" + k).Order(StringComparer.Ordinal).ToArray();

    private static void Eq(IEnumerable<string> expected, IEnumerable<string> actual) =>
        Assert.Equal(expected.Order(StringComparer.Ordinal).ToArray(), actual.ToArray());

    // ── definitions ──────────────────────────────────────────────────────────

    [Fact]
    public void HasPage_and_Listed_follow_the_contract_definitions()
    {
        Assert.True(PurgeSets.HasPage(G("g", "s", "private", "a")));
        Assert.False(PurgeSets.HasPage(G("g", "s", "public")));
        Assert.False(PurgeSets.HasPage(null));
        Assert.True(PurgeSets.Listed(G("g", "s", "public", "a")));
        Assert.False(PurgeSets.Listed(G("g", "s", "private", "a")));
        Assert.False(PurgeSets.Listed(G("g", "s", "public")));
    }

    [Fact]
    public void Changed_is_added_removed_and_neighbour_changed_ids()
    {
        var b = G("g", "s", "public", "a", "b", "c", "d");
        var a = G("g", "s", "public", "b", "a", "c", "d", "e");
        // a: (-,b)->(b,c); b: (a,c)->(-,a); c: (b,d)->(a,d); d: (c,-)->(c,e); e added.
        Eq(["a", "b", "c", "d", "e"], PurgeSets.Changed(b, a));
        Eq([], PurgeSets.Changed(b, b));
    }

    // ── P-MEMBERS ────────────────────────────────────────────────────────────

    [Fact]
    public void P_MEMBERS_add_purges_every_photo_page_when_the_count_changes()
    {
        var set = PurgeSets.Members(G("g1", "s", "public", "a", "b", "c"), G("g1", "s", "public", "a", "b", "c", "e"), Bases);
        // 010 E1 rule 4 (stated change, decisions row 27): every photo page shows "N / total".
        Eq(P("/g", "/g/g1/s", "/g/g1/s/a", "/g/g1/s/b", "/g/g1/s/c", "/g/g1/s/e"), set.Pages);
        Assert.Empty(set.Files);
    }

    [Fact]
    public void P_MEMBERS_remove_purges_the_removed_photo_and_both_neighbours()
    {
        var set = PurgeSets.Members(G("g1", "s", "public", "a", "b", "c"), G("g1", "s", "public", "a", "c"), Bases);
        Eq(P("/g", "/g/g1/s", "/g/g1/s/a", "/g/g1/s/b", "/g/g1/s/c"), set.Pages);
        Assert.Empty(set.Files);
    }

    [Fact]
    public void P_MEMBERS_reorder_purges_only_photos_whose_neighbours_changed()
    {
        var set = PurgeSets.Members(G("g1", "s", "public", "a", "b", "c", "d"), G("g1", "s", "public", "b", "a", "c", "d"), Bases);
        Eq(P("/g", "/g/g1/s", "/g/g1/s/a", "/g/g1/s/b", "/g/g1/s/c"), set.Pages);
    }

    [Fact]
    public void P_MEMBERS_cover_pin_purges_gallery_page_and_home_only()
    {
        var b = new GallerySnapshot("g1", "s", "public", ["a", "b"]);
        var a = new GallerySnapshot("g1", "s", "public", ["a", "b"], PinnedCover: "b");
        Eq(P("/g", "/g/g1/s"), PurgeSets.Members(b, a, Bases).Pages);
        Eq(P("/g", "/g/g1/s"), PurgeSets.Members(a, b, Bases).Pages); // unpin
    }

    [Fact]
    public void P_MEMBERS_a_gallery_emptied_by_the_change_is_still_purged()
    {
        var set = PurgeSets.Members(G("g1", "s", "public", "a"), G("g1", "s", "public"), Bases);
        Eq(P("/g", "/g/g1/s", "/g/g1/s/a"), set.Pages);

        var priv = PurgeSets.Members(G("g1", "s", "private", "a"), G("g1", "s", "private"), Bases);
        Eq(P("/g/g1/s", "/g/g1/s/a"), priv.Pages);
    }

    [Fact]
    public void P_MEMBERS_private_gallery_with_photos_is_purged_and_home_is_not()
    {
        var set = PurgeSets.Members(G("g1", "s", "private", "a"), G("g1", "s", "private", "a", "b"), Bases);
        Eq(P("/g/g1/s", "/g/g1/s/a", "/g/g1/s/b"), set.Pages);
        Assert.DoesNotContain("https://site.test/", set.Pages);
    }

    [Fact]
    public void P_MEMBERS_first_photo_into_an_empty_public_gallery_lists_it_on_home()
    {
        var set = PurgeSets.Members(G("g1", "s", "public"), G("g1", "s", "public", "a"), Bases);
        Eq(P("/g", "/g/g1/s", "/g/g1/s/a"), set.Pages);
    }

    // ── P-VIS ────────────────────────────────────────────────────────────────

    [Fact]
    public void P_VIS_publish_and_unpublish_purge_every_page_and_home()
    {
        var priv = G("g1", "s", "private", "a", "b");
        var pub = G("g1", "s", "public", "a", "b");
        var expected = P("/g", "/g/g1/s", "/g/g1/s/a", "/g/g1/s/b");
        Eq(expected, PurgeSets.Visibility(priv, pub, Bases).Pages);
        Eq(expected, PurgeSets.Visibility(pub, priv, Bases).Pages);
        Assert.Empty(PurgeSets.Visibility(priv, pub, Bases).Files);
    }

    // ── P-RENAME ─────────────────────────────────────────────────────────────

    [Fact]
    public void P_RENAME_purges_every_page_under_both_slugs_and_home()
    {
        var set = PurgeSets.Rename(G("g1", "old", "public", "a", "b"), G("g1", "new", "public", "a", "b"), Bases);
        Eq(P("/g", "/g/g1/old", "/g/g1/old/a", "/g/g1/old/b", "/g/g1/new", "/g/g1/new/a", "/g/g1/new/b"), set.Pages);
    }

    [Fact]
    public void P_RENAME_private_gallery_does_not_purge_home()
    {
        var set = PurgeSets.Rename(G("g1", "old", "private", "a"), G("g1", "new", "private", "a"), Bases);
        Eq(P("/g/g1/old", "/g/g1/old/a", "/g/g1/new", "/g/g1/new/a"), set.Pages);
    }

    [Fact]
    public void P_RENAME_to_the_same_slug_gives_one_slugs_paths_only()
    {
        var set = PurgeSets.Rename(G("g1", "s", "public", "a", "b"), G("g1", "s", "public", "a", "b"), Bases);
        Eq(P("/g", "/g/g1/s", "/g/g1/s/a", "/g/g1/s/b"), set.Pages);
    }

    // ── P-DESC ───────────────────────────────────────────────────────────────

    [Fact]
    public void P_DESC_purges_the_gallery_page_only_when_it_has_one()
    {
        Eq(P("/g/g1/s"), PurgeSets.Description(G("g1", "s", "public", "a"), G("g1", "s", "public", "a"), Bases).Pages);
        Assert.True(PurgeSets.Description(G("g1", "s", "public"), G("g1", "s", "public"), Bases).IsEmpty);
    }

    // ── P-ORDER ──────────────────────────────────────────────────────────────

    private static readonly GallerySnapshot[] Three =
    [
        G("g1", "a", "public", "x"),
        G("g2", "b", "public", "y"),
        G("g3", "c", "private", "z"),
        G("g4", "d", "public"),
    ];

    [Fact]
    public void P_ORDER_purges_home_when_listed_order_changes()
    {
        var set = PurgeSets.Order(["g1", "g2", "g3", "g4"], ["g2", "g1", "g3", "g4"], Three, Bases);
        Eq(P("/g"), set.Pages);
        Assert.Empty(set.Files);
    }

    [Fact]
    public void P_ORDER_private_only_move_gives_an_empty_set()
    {
        Assert.True(PurgeSets.Order(["g3", "g1", "g2"], ["g1", "g2", "g3"], Three, Bases).IsEmpty);
        // an empty public gallery is not listed either
        Assert.True(PurgeSets.Order(["g4", "g1", "g2"], ["g1", "g4", "g2"], Three, Bases).IsEmpty);
    }

    // ── P-GDELETE ────────────────────────────────────────────────────────────

    [Fact]
    public void P_GDELETE_purges_every_page_before_and_home_when_listed()
    {
        Eq(P("/g", "/g/g1/s", "/g/g1/s/a", "/g/g1/s/b"), PurgeSets.GalleryDelete(G("g1", "s", "public", "a", "b"), Bases).Pages);
        Eq(P("/g/g1/s", "/g/g1/s/a"), PurgeSets.GalleryDelete(G("g1", "s", "private", "a"), Bases).Pages);
        Assert.True(PurgeSets.GalleryDelete(G("g1", "s", "public"), Bases).IsEmpty);
    }

    // ── P-TEXT ───────────────────────────────────────────────────────────────

    [Fact]
    public void P_TEXT_purges_gallery_and_photo_page_in_each_gallery_plus_home_for_a_listed_cover()
    {
        var galleries = new[] { G("g1", "s", "public", "a", "b"), G("g2", "t", "private", "c", "a") };
        Eq(P("/g", "/g/g1/s", "/g/g1/s/a", "/g/g2/t", "/g/g2/t/a"), PurgeSets.Text("a", galleries, Bases).Pages);
    }

    [Fact]
    public void P_TEXT_adds_home_only_when_the_image_is_the_cover_of_a_listed_gallery()
    {
        var notCover = new[] { G("g1", "s", "public", "a", "b") };
        Eq(P("/g/g1/s", "/g/g1/s/b"), PurgeSets.Text("b", notCover, Bases).Pages);

        var pinned = new[] { new GallerySnapshot("g1", "s", "public", ["a", "b"], PinnedCover: "b") };
        Eq(P("/g", "/g/g1/s", "/g/g1/s/b"), PurgeSets.Text("b", pinned, Bases).Pages);

        var privateCover = new[] { G("g2", "t", "private", "a") };
        Eq(P("/g/g2/t", "/g/g2/t/a"), PurgeSets.Text("a", privateCover, Bases).Pages);
    }

    [Fact]
    public void P_TEXT_image_in_no_gallery_with_a_page_gives_an_empty_set()
    {
        Assert.True(PurgeSets.Text("a", [G("g1", "s", "public", "b")], Bases).IsEmpty);
        Assert.True(PurgeSets.Text("a", [], Bases).IsEmpty);
    }

    // ── P-DELETE ─────────────────────────────────────────────────────────────

    [Fact]
    public void P_DELETE_is_the_members_union_plus_exactly_the_deleted_keys()
    {
        var set = PurgeSets.Delete(
            [
                (G("g1", "s", "public", "a", "b"), G("g1", "s", "public", "b")),
                (G("g2", "t", "private", "a"), G("g2", "t", "private")),
            ],
            ["a-320.avif", "a-320.webp", "a-640.avif"],
            Bases);

        Eq(P("/g", "/g/g1/s", "/g/g1/s/a", "/g/g1/s/b", "/g/g2/t", "/g/g2/t/a"), set.Pages);
        Eq(F("a-320.avif", "a-320.webp", "a-640.avif"), set.Files);
    }

    [Fact]
    public void P_DELETE_image_in_no_gallery_purges_only_its_deleted_files()
    {
        var set = PurgeSets.Delete([], ["a-320.avif"], Bases);
        Assert.Empty(set.Pages);
        Eq(F("a-320.avif"), set.Files);

        // shared files kept (decisions row 3): nothing deleted -> no file purged
        Assert.True(PurgeSets.Delete([], [], Bases).IsEmpty);
    }

    [Fact]
    public void Union_de_duplicates_and_sorts_the_sets_of_a_bulk_operation()
    {
        var one = PurgeSets.Members(G("g1", "s", "public", "a"), G("g1", "s", "public", "a", "b"), Bases);
        var two = PurgeSets.Members(G("g1", "s", "public", "a", "b"), G("g1", "s", "public", "a", "b", "c"), Bases);
        var union = PurgeSet.Union(Bases, [one, two]);
        Eq(P("/g", "/g/g1/s", "/g/g1/s/a", "/g/g1/s/b", "/g/g1/s/c"), union.Pages);
        Eq(["/g", "/g/g1/s", "/g/g1/s/a", "/g/g1/s/b", "/g/g1/s/c"], union.PagePaths);
    }

    [Fact]
    public void Addresses_are_absolute_on_the_configured_bases()
    {
        var bases = new EdgeBases("https://localhost:5081/", "https://images.example/");
        var set = PurgeSets.Delete([(G("g1", "s", "private", "a"), G("g1", "s", "private"))], ["k.avif"], bases);
        Assert.Equal(["https://localhost:5081/g/g1/s", "https://localhost:5081/g/g1/s/a"], set.Pages);
        Assert.Equal(["https://images.example/k.avif"], set.Files);
    }

    // ── WarmFiles (FR-039) ───────────────────────────────────────────────────

    private static IReadOnlyList<string>? Avif(string id) => [$"{id}-320.avif", $"{id}-640.avif"];

    [Fact]
    public void WarmFiles_gallery_page_of_six_photos_warms_exactly_its_first_four()
    {
        var g = G("g1", "s", "public", "p1", "p2", "p3", "p4", "p5", "p6");
        var purged = PurgeSets.Description(g, g, Bases);
        var warm = PurgeSets.WarmFiles(purged, [g], [], Avif);
        Eq(F("p1-320.avif", "p1-640.avif", "p2-320.avif", "p2-640.avif",
             "p3-320.avif", "p3-640.avif", "p4-320.avif", "p4-640.avif"), warm);
    }

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

    [Fact]
    public void WarmFiles_home_warms_the_first_three_cover_avifs()
    {
        var purged = PurgeSets.Order(["g1", "g2"], ["g2", "g1"], [G("g1", "a", "public", "x"), G("g2", "b", "public", "y")], Bases);
        var cards = new[] { Card("g1", "c1"), Card("g2", "c2"), Card("g3", "c3"), Card("g4", "c4") };
        var warm = PurgeSets.WarmFiles(purged, [], cards, Avif);
        Eq(F("c1-320.avif", "c2-320.avif", "c3-320.avif"), warm);
    }

    [Fact]
    public void WarmFiles_photo_page_warms_the_photo_itself()
    {
        var b = G("g1", "s", "private", "a", "b", "c");
        var purged = PurgeSets.Text("c", [b], Bases); // gallery page + photo page c
        var warm = PurgeSets.WarmFiles(purged, [b], [], Avif);
        Eq(F("a-320.avif", "a-640.avif", "b-320.avif", "b-640.avif", "c-320.avif", "c-640.avif"), warm);
    }

    [Fact]
    public void WarmFiles_a_page_that_404s_after_the_change_gives_none()
    {
        var b = G("g1", "s", "private", "a", "b");
        Assert.Empty(PurgeSets.WarmFiles(PurgeSets.GalleryDelete(b, Bases), [], [], Avif));

        // emptied gallery: its page no longer exists
        var emptied = PurgeSets.Members(G("g1", "s", "private", "a"), G("g1", "s", "private"), Bases);
        Assert.Empty(PurgeSets.WarmFiles(emptied, [G("g1", "s", "private")], [], Avif));

        // renamed: only the new slug's page exists
        var renamed = PurgeSets.Rename(G("g1", "old", "private", "a"), G("g1", "new", "private", "a"), Bases);
        Eq(F("a-320.avif", "a-640.avif"), PurgeSets.WarmFiles(renamed, [G("g1", "new", "private", "a")], [], Avif));
    }

    [Fact]
    public void WarmFiles_never_returns_a_deleted_key()
    {
        var before = G("g1", "s", "private", "a", "b");
        var after = G("g1", "s", "private", "b");
        // "b-320.avif" listed as deleted to prove the filter, whatever the lookup says.
        var purged = PurgeSets.Delete([(before, after)], ["a-320.avif", "a-640.avif", "b-320.avif"], Bases);
        var warm = PurgeSets.WarmFiles(purged, [after], [], Avif);
        Eq(F("b-640.avif"), warm);
    }
}
