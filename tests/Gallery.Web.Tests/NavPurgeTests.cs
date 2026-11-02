using Gallery.Web.Edge;
using Gallery.Web.Library;
using Gallery.Web.Tests.Fixtures;
using MongoDB.Driver;

namespace Gallery.Web.Tests;

/// <summary>
/// 010 T011 (FR-003, FR-009, SC-005; contracts/edge.md E1, E2): (a) the widened
/// <see cref="PurgeSets.Changed"/> table, row by row; (b) every "Expected sets per owner action"
/// row end to end through <see cref="LibraryHost"/>'s recorder: the purged set EQUALS the pre-010
/// set kinds for that action (computed by calling <see cref="PurgeSets"/> on the before/after
/// snapshots) plus exactly the P-NAV pages of the table; (c) the "none" rows add nothing.
/// </summary>
[Collection("s3")]
public class NavPurgeTests(S3ProxyFixture s3, MongoFixture mongo) : IClassFixture<MongoFixture>
{
    private static readonly EdgeBases Bases = new(LibraryHost.SiteBaseUrl, LibraryHost.ImageBaseUrl);

    private static GallerySnapshot G(params string[] items) => new("g", "s", "public", items);

    // ── (a) E1 ───────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("a b c", "a b c e", "a b c e")]      // rule 4 (count)
    [InlineData("a b c", "a c", "a b c")]            // rules 1, 2, 4
    [InlineData("a b c d", "b a c d", "a b c")]      // rule 2 only
    [InlineData("a b c d e", "b c d a e", "a b c d e")] // c only by rule 3
    public void Changed_follows_the_E1_table(string before, string after, string expected)
    {
        var changed = PurgeSets.Changed(G(before.Split(' ')), G(after.Split(' ')));
        Assert.Equal(expected.Split(' '), changed.ToArray());
    }

    [Fact]
    public void Changed_names_c_by_its_index_alone()
    {
        // c keeps neighbours b and d but moves from index 3 to 2 (rule 3).
        var b = G("a", "b", "c", "d", "e");
        var a = G("b", "c", "d", "a", "e");
        Assert.Equal((("b", "d")), (Prev(b, "c"), Next(b, "c")));
        Assert.Equal((("b", "d")), (Prev(a, "c"), Next(a, "c")));
        Assert.Contains("c", PurgeSets.Changed(b, a));

        static string? Prev(GallerySnapshot g, string id) => Gallery.Core.Content.GalleryNav.Neighbours(g.Items, id).Prev;
        static string? Next(GallerySnapshot g, string id) => Gallery.Core.Content.GalleryNav.Neighbours(g.Items, id).Next;
    }

    [Fact]
    public void Nav_is_the_gallery_page_of_each_target_only()
    {
        var set = PurgeSets.Nav([new GalleryLinkTarget("g1", "one"), new GalleryLinkTarget("g2", "two")], Bases);
        Assert.Equal(new[] { "/g/g1/one", "/g/g2/two" }, set.PagePaths.ToArray());
        Assert.Empty(set.FileKeys);
        Assert.Empty(PurgeSets.Nav([], Bases).PagePaths);
    }

    // ── (b), (c) expected sets per owner action ─────────────────────────────

    private static async Task<GallerySnapshot?> SnapAsync(LibraryHost h, string id)
    {
        var g = await h.Galleries.Find(x => x.Id == id).FirstOrDefaultAsync();
        return g is null ? null : GallerySnapshot.From(g);
    }

    private static async Task<Dictionary<string, GallerySnapshot>> SnapsAsync(LibraryHost h, NavLibrary lib)
    {
        var all = new Dictionary<string, GallerySnapshot>(StringComparer.Ordinal);
        foreach (var id in new[] { lib.A, lib.B, lib.C, lib.D, lib.P })
            if (await SnapAsync(h, id) is { } s) all[id] = s;
        return all;
    }

    private static string Gp(GallerySnapshot g) => PurgeSets.GalleryPath(g.Id, g.Slug);

    private static void SetEq(PurgeSet expected, IReadOnlyList<string> purged) =>
        Assert.Equal(expected.Pages.Concat(expected.Files).Order(StringComparer.Ordinal).ToArray(),
            purged.Order(StringComparer.Ordinal).ToArray());

    private static PurgeSet Union(params PurgeSet[] sets) => PurgeSet.Union(Bases, sets);

    private static PurgeSet NavPages(params GallerySnapshot[] galleries) =>
        PurgeSets.Nav(galleries.Select(g => new GalleryLinkTarget(g.Id, g.Slug)), Bases);

    private async Task<(LibraryHost H, NavLibrary Lib)> NewAsync()
    {
        var h = await LibraryHost.CreateAsync(mongo, s3);
        return (h, await NavLibrary.BuildAsync(h));
    }

    [Fact]
    public async Task Reorder_home_to_B_A_C_D_purges_home_and_A_B_C()
    {
        var (h, lib) = await NewAsync();
        await using var _ = h;
        var before = await SnapsAsync(h, lib);
        var order = await NavLibrary.OrderAsync(h);
        List<string> next = [lib.B, lib.A, lib.C, lib.D, lib.P];

        Assert.True((await NavLibrary.Gs(h, s => s.ReorderHomeAsync(next))).IsOk);
        var purged = await h.SettleAsync();

        var after = await SnapsAsync(h, lib);
        SetEq(Union(
            PurgeSets.Order(order, next, before.Values.ToList(), Bases),
            NavPages(after[lib.A], after[lib.B], after[lib.C])), purged);
    }

    [Fact]
    public async Task Unpublish_B_purges_P_VIS_and_A_B_C()
    {
        var (h, lib) = await NewAsync();
        await using var _ = h;
        var b0 = await SnapAsync(h, lib.B);

        Assert.True((await NavLibrary.Gs(h, s => s.UnpublishAsync(lib.B))).IsOk);
        var purged = await h.SettleAsync();

        var after = await SnapsAsync(h, lib);
        SetEq(Union(
            PurgeSets.Visibility(b0, after[lib.B], Bases),
            NavPages(after[lib.A], after[lib.B], after[lib.C])), purged);
    }

    [Fact]
    public async Task Moving_P_within_homeOrder_purges_nothing_and_publishing_it_between_B_and_C_purges_B_P_C()
    {
        var (h, lib) = await NewAsync();
        await using var _ = h;

        // (c) "Move P within homeOrder": none.
        Assert.True((await NavLibrary.Gs(h, s => s.ReorderHomeAsync([lib.A, lib.B, lib.P, lib.C, lib.D]))).IsOk);
        Assert.Empty(await h.SettleAsync());

        var p0 = await SnapAsync(h, lib.P);
        Assert.True((await NavLibrary.Gs(h, s => s.PublishAsync(lib.P, confirmed: true))).IsOk);
        var purged = await h.SettleAsync();

        var after = await SnapsAsync(h, lib);
        SetEq(Union(
            PurgeSets.Visibility(p0, after[lib.P], Bases),
            NavPages(after[lib.B], after[lib.P], after[lib.C])), purged);
    }

    [Fact]
    public async Task Rename_B_purges_P_RENAME_and_A_C()
    {
        var (h, lib) = await NewAsync();
        await using var _ = h;
        var b0 = await SnapAsync(h, lib.B);

        Assert.True((await NavLibrary.Gs(h, s => s.UpdateTextAsync(lib.B, "Brook", null))).IsOk);
        var purged = await h.SettleAsync();

        var after = await SnapsAsync(h, lib);
        Assert.NotEqual(b0!.Slug, after[lib.B].Slug);
        SetEq(Union(
            PurgeSets.Rename(b0, after[lib.B], Bases),
            NavPages(after[lib.A], after[lib.C])), purged);
    }

    [Fact]
    public async Task Delete_C_purges_P_GDELETE_and_B_D()
    {
        var (h, lib) = await NewAsync();
        await using var _ = h;
        var c0 = await SnapAsync(h, lib.C);

        Assert.True((await NavLibrary.Gs(h, s => s.DeleteAsync(lib.C))).IsOk);
        var purged = await h.SettleAsync();

        var after = await SnapsAsync(h, lib);
        Assert.False(after.ContainsKey(lib.C));
        SetEq(Union(
            PurgeSets.GalleryDelete(c0!, Bases),
            NavPages(after[lib.B], after[lib.D])), purged);
    }

    [Fact]
    public async Task Removing_the_last_photo_of_B_purges_P_MEMBERS_and_A_B_C()
    {
        var (h, lib) = await NewAsync();
        await using var _ = h;
        var items = (await SnapAsync(h, lib.B))!.Items;

        // The first removal leaves B listed with the same neighbours: P-MEMBERS only.
        var b0 = await SnapAsync(h, lib.B);
        Assert.True((await NavLibrary.Gs(h, s => s.RemoveAsync(lib.B, items[0]))).IsOk);
        var first = await h.SettleAsync();
        SetEq(PurgeSets.Members(b0, await SnapAsync(h, lib.B), Bases), first);

        var b1 = await SnapAsync(h, lib.B);
        Assert.True((await NavLibrary.Gs(h, s => s.RemoveAsync(lib.B, items[1]))).IsOk);
        var purged = await h.SettleAsync();

        var after = await SnapsAsync(h, lib);
        Assert.Empty(after[lib.B].Items);
        SetEq(Union(
            PurgeSets.Members(b1, after[lib.B], Bases),
            NavPages(after[lib.A], after[lib.B], after[lib.C])), purged);
    }

    [Fact]
    public async Task Editing_As_description_adds_no_nav_page()
    {
        var (h, lib) = await NewAsync();
        await using var _ = h;
        var a0 = await SnapAsync(h, lib.A);

        Assert.True((await NavLibrary.Gs(h, s => s.UpdateTextAsync(lib.A, null, "New words"))).IsOk);
        var purged = await h.SettleAsync();

        SetEq(PurgeSets.Description(a0, await SnapAsync(h, lib.A), Bases), purged);
    }

    [Fact]
    public async Task Reordering_photos_inside_A_adds_no_nav_page()
    {
        var (h, lib) = await NewAsync();
        await using var _ = h;
        var a0 = (await SnapAsync(h, lib.A))!;

        Assert.True((await NavLibrary.Gs(h, s => s.ReorderAsync(lib.A, [a0.Items[1], a0.Items[0]]))).IsOk);
        var purged = await h.SettleAsync();

        SetEq(PurgeSets.Members(a0, await SnapAsync(h, lib.A), Bases), purged);
    }
}
