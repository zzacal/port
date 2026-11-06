using Gallery.Core.Content;
using Gallery.Web.Data;
using Gallery.Web.Library;

namespace Gallery.Web.Edge;

/// <summary>
/// One gallery's state before or after an operation (contracts/edge.md). A gallery that does not
/// exist on that side (created, deleted) is passed as <c>null</c>. <see cref="PinnedCover"/> is
/// optional and only used to find the cover for P-TEXT; without it the cover is <c>items[0]</c>.
/// </summary>
public sealed record GallerySnapshot(
    string Id,
    string Slug,
    string Visibility,
    IReadOnlyList<string> Items,
    string? PinnedCover = null)
{
    /// <summary>The cover the card shows: the pin when it is a member, else the first item, else none.</summary>
    public string? Cover =>
        PinnedCover is not null && Items.Contains(PinnedCover) ? PinnedCover
        : Items.Count > 0 ? Items[0]
        : null;

    public static GallerySnapshot From(GalleryDoc g) =>
        new(g.Id, g.Slug, g.Visibility, g.Items.ToArray(), g.PinnedCover);
}

/// <summary>
/// One camera's state before or after an operation (013 contracts/edge.md E1): its key, slug and
/// visible items in stored order. A camera that does not exist on that side is passed as <c>null</c>.
/// </summary>
public sealed record CameraSnapshot(string Key, string Slug, IReadOnlyList<string> Items)
{
    public static CameraSnapshot From(CameraGroupDoc c) => new(c.Id, c.Slug, c.Items.ToArray());
}

/// <summary>Absolute-address roots: pages on <c>Edge:SiteBaseUrl</c>, files on <c>Storage:ImageBaseUrl</c>.</summary>
public sealed record EdgeBases(string SiteBaseUrl, string ImageBaseUrl)
{
    public string Page(string path) => SiteBaseUrl.TrimEnd('/') + path;
    public string File(string key) => ImageBaseUrl.TrimEnd('/') + "/" + key.TrimStart('/');
}

/// <summary>
/// The addresses one operation purges. <see cref="PagePaths"/> (no host) feed the local
/// <c>PageCacheEvictor</c>; <see cref="Pages"/> and <see cref="Files"/> are the absolute edge
/// addresses. All lists are de-duplicated and ordinally sorted.
/// </summary>
public sealed class PurgeSet
{
    public EdgeBases Bases { get; }
    public IReadOnlyList<string> PagePaths { get; }
    public IReadOnlyList<string> FileKeys { get; }
    public IReadOnlyList<string> Pages { get; }
    public IReadOnlyList<string> Files { get; }

    public PurgeSet(EdgeBases bases, IEnumerable<string> pagePaths, IEnumerable<string> fileKeys)
    {
        Bases = bases;
        PagePaths = PurgeSets.Sorted(pagePaths);
        FileKeys = PurgeSets.Sorted(fileKeys);
        Pages = PagePaths.Select(bases.Page).ToArray();
        Files = FileKeys.Select(bases.File).ToArray();
    }

    public bool IsEmpty => PagePaths.Count == 0 && FileKeys.Count == 0;

    /// <summary>Union of several sets (a bulk operation enqueues ONE work item, contracts/edge.md).</summary>
    public static PurgeSet Union(EdgeBases bases, IEnumerable<PurgeSet> sets)
    {
        var list = sets.ToList();
        if (list.Any(s => s.Bases != bases))
            throw new ArgumentException("Every set must use the same bases.", nameof(sets));
        return new PurgeSet(bases, list.SelectMany(s => s.PagePaths), list.SelectMany(s => s.FileKeys));
    }

    public EdgeWork ToWork(string op, IReadOnlyList<string> warmFiles, DateTimeOffset enqueuedAt) =>
        new(op, Pages, Files, warmFiles, enqueuedAt);
}

/// <summary>
/// Pure purge-set computation, exactly per contracts/edge.md (FR-038, SC-004, decisions rows 7, 24
/// and 32; 010 E1 widened <see cref="Changed"/>, E2 <see cref="Nav"/>) and the warm file list
/// (FR-039, decisions rows 25 and 45).
/// </summary>
public static class PurgeSets
{
    public const string ListingPath = "/g";

    public static string GalleryPath(string gid, string slug) => $"/g/{gid}/{slug}";
    public static string PhotoPath(string gid, string slug, string iid) => $"/g/{gid}/{slug}/{iid}";

    /// <summary>013 contracts/edge.md "Paths": the camera listing.</summary>
    public const string CameraListingPath = "/c";

    public static string CameraPath(string slug) => $"/c/{slug}";
    public static string CameraPhotoPath(string slug, string iid) => $"/c/{slug}/{iid}";

    // ── definitions ───────────────────────────────────────────────────────────

    /// <summary><c>items</c> non-empty, public or private (decisions row 7).</summary>
    public static bool HasPage(GallerySnapshot? g) => g is not null && g.Items.Count > 0;

    /// <summary>Public AND <c>items</c> non-empty.</summary>
    public static bool Listed(GallerySnapshot? g) => HasPage(g) && g!.Visibility == "public";

    /// <summary><c>/g</c> when the gallery is listed before or after, else nothing.</summary>
    public static IReadOnlyList<string> Listing(GallerySnapshot? before, GallerySnapshot? after) =>
        Listed(before) || Listed(after) ? [ListingPath] : [];

    /// <summary>
    /// Ids whose photo page changed (010 contracts/edge.md E1): ids in exactly one of the two item
    /// lists; ids in both whose neighbours differ; ids in both whose 1-based index differs (the
    /// "N / total" counter); and every id in both when the item count differs (the total changed).
    /// </summary>
    public static IReadOnlyList<string> Changed(GallerySnapshot? before, GallerySnapshot? after) =>
        ChangedItems(before?.Items ?? [], after?.Items ?? []);

    /// <summary>
    /// The <see cref="Changed"/> rule over two item lists; <see cref="Changed"/> (galleries) and
    /// <see cref="Cameras"/> (013 E1 <c>CamChanged</c>) both call it.
    /// </summary>
    private static IReadOnlyList<string> ChangedItems(IReadOnlyList<string> b, IReadOnlyList<string> a)
    {
        var inB = b.ToHashSet(StringComparer.Ordinal);
        var inA = a.ToHashSet(StringComparer.Ordinal);
        var countChanged = b.Count != a.Count;
        var changed = new List<string>();
        foreach (var id in inB.Union(inA))
        {
            if (inB.Contains(id) != inA.Contains(id))
                changed.Add(id);
            else if (countChanged)
                changed.Add(id);
            else if (GalleryNav.Neighbours(b, id) != GalleryNav.Neighbours(a, id))
                changed.Add(id);
            else if (IndexOf(b, id) != IndexOf(a, id))
                changed.Add(id);
        }
        return Sorted(changed);
    }

    /// <summary>
    /// For each snapshot that has a page: its gallery page plus the photo page of each id that
    /// snapshot holds, under that snapshot's slug.
    /// </summary>
    public static IReadOnlyList<string> Pages(GallerySnapshot? before, GallerySnapshot? after, IEnumerable<string> ids)
    {
        var idList = ids.ToList();
        var paths = new List<string>();
        foreach (var g in new[] { before, after })
        {
            if (!HasPage(g)) continue;
            paths.Add(GalleryPath(g!.Id, g.Slug));
            foreach (var id in idList)
                if (g.Items.Contains(id))
                    paths.Add(PhotoPath(g.Id, g.Slug, id));
        }
        return Sorted(paths);
    }

    // ── one method per set ───────────────────────────────────────────────────

    /// <summary>P-MEMBERS: add, remove, reorder, cover pin/unpin.</summary>
    public static PurgeSet Members(GallerySnapshot? before, GallerySnapshot? after, EdgeBases bases) =>
        new(bases, MembersPaths(before, after), []);

    /// <summary>P-VIS: publish / unpublish.</summary>
    public static PurgeSet Visibility(GallerySnapshot? before, GallerySnapshot? after, EdgeBases bases) =>
        new(bases, Pages(before, after, AllItems(before, after)).Append(ListingPath), []);

    /// <summary>P-RENAME: slug changed; every page under BOTH slugs.</summary>
    public static PurgeSet Rename(GallerySnapshot? before, GallerySnapshot? after, EdgeBases bases) =>
        new(bases, Pages(before, after, AllItems(before, after)).Concat(Listing(before, after)), []);

    /// <summary>P-DESC: description only; the gallery page when it has one.</summary>
    public static PurgeSet Description(GallerySnapshot? before, GallerySnapshot? after, EdgeBases bases)
    {
        var paths = new List<string>();
        foreach (var g in new[] { before, after })
            if (HasPage(g)) paths.Add(GalleryPath(g!.Id, g.Slug));
        return new(bases, paths, []);
    }

    /// <summary>
    /// P-ORDER: <c>/g</c> only when the relative order of LISTED galleries changed (decisions row 32).
    /// <paramref name="galleries"/> gives each gallery's state (unchanged by a reorder); ids without a
    /// snapshot count as not listed.
    /// </summary>
    public static PurgeSet Order(
        IReadOnlyList<string> beforeOrder,
        IReadOnlyList<string> afterOrder,
        IEnumerable<GallerySnapshot> galleries,
        EdgeBases bases)
    {
        var listed = galleries.Where(Listed).Select(g => g.Id).ToHashSet(StringComparer.Ordinal);
        var b = beforeOrder.Where(listed.Contains).ToList();
        var a = afterOrder.Where(listed.Contains).ToList();
        return new(bases, b.SequenceEqual(a, StringComparer.Ordinal) ? [] : [ListingPath], []);
    }

    /// <summary>
    /// P-NAV (010 contracts/edge.md E2): the gallery page of every gallery whose stored <c>nav</c> the
    /// projection set or unset, under its slug after the change. No photo pages, no <c>/</c>.
    /// </summary>
    public static PurgeSet Nav(IEnumerable<GalleryLinkTarget> navChanged, EdgeBases bases) =>
        new(bases, navChanged.Select(g => GalleryPath(g.Id, g.Slug)), []);

    /// <summary>P-GDELETE: every page of the gallery before + Listing(before, ∅).</summary>
    public static PurgeSet GalleryDelete(GallerySnapshot before, EdgeBases bases) =>
        new(bases, Pages(before, null, before.Items).Concat(Listing(before, null)), []);

    /// <summary>
    /// P-TEXT: image title/caption. For every gallery holding the image that has a page: its gallery
    /// page and the image's photo page in it, plus <c>/g</c> when the image is the cover of a listed
    /// gallery (decisions row 24).
    /// </summary>
    public static PurgeSet Text(string imageId, IEnumerable<GallerySnapshot> galleries, EdgeBases bases)
    {
        var paths = new List<string>();
        foreach (var g in galleries)
        {
            if (!HasPage(g) || !g.Items.Contains(imageId)) continue;
            paths.Add(GalleryPath(g.Id, g.Slug));
            paths.Add(PhotoPath(g.Id, g.Slug, imageId));
            if (Listed(g) && g.Cover == imageId)
                paths.Add(ListingPath);
        }
        return new(bases, paths, []);
    }

    /// <summary>
    /// P-DELETE: the union of P-MEMBERS for every gallery that held the image (before → after the
    /// pull), <c>/g</c> when any was listed, and exactly the public object keys actually deleted.
    /// </summary>
    public static PurgeSet Delete(
        IEnumerable<(GallerySnapshot Before, GallerySnapshot? After)> galleries,
        IEnumerable<string> deletedKeys,
        EdgeBases bases)
    {
        var paths = new List<string>();
        foreach (var (b, a) in galleries)
            paths.AddRange(MembersPaths(b, a)); // includes Listing(b, a): "/g" when either side is listed
        return new(bases, paths, deletedKeys);
    }

    // ── camera pages (013 contracts/edge.md E1, E2) ──────────────────────────

    /// <summary>
    /// P-CAM (E1): <c>/c</c> when <paramref name="cardsChanged"/>; for each pair, the camera page
    /// under each non-null side's slug; and the camera photo page of every id the
    /// <see cref="Changed"/> rule names over the two item lists, under the slug of each side that
    /// holds it. Never a <c>/g/</c> path, never <c>/</c>.
    /// </summary>
    public static PurgeSet Cameras(
        IEnumerable<(CameraSnapshot? Before, CameraSnapshot? After)> changes,
        bool cardsChanged,
        EdgeBases bases)
    {
        var paths = new List<string>();
        if (cardsChanged) paths.Add(CameraListingPath);
        foreach (var (before, after) in changes)
        {
            var changed = ChangedItems(before?.Items ?? [], after?.Items ?? []);
            foreach (var c in new[] { before, after })
            {
                if (c is null) continue;
                paths.Add(CameraPath(c.Slug));
                foreach (var id in changed)
                    if (c.Items.Contains(id))
                        paths.Add(CameraPhotoPath(c.Slug, id));
            }
        }
        return new(bases, paths, []);
    }

    /// <summary>
    /// P-CAMTEXT (E2): for each camera in <paramref name="after"/> holding <paramref name="iid"/>,
    /// its camera page and the photo's camera photo page. Never <c>/c</c> (the card shows the
    /// camera name, not a photo title).
    /// </summary>
    public static PurgeSet CameraText(string iid, IEnumerable<CameraSnapshot> after, EdgeBases bases)
    {
        var paths = new List<string>();
        foreach (var c in after)
        {
            if (!c.Items.Contains(iid)) continue;
            paths.Add(CameraPath(c.Slug));
            paths.Add(CameraPhotoPath(c.Slug, iid));
        }
        return new(bases, paths, []);
    }

    // ── warm (FR-039) ─────────────────────────────────────────────────────────

    /// <summary>
    /// Absolute AVIF URLs to warm after <paramref name="purged"/>: for each purged page that still
    /// exists AFTER the change, every AVIF width of its first-row images (listing: the first 3
    /// <paramref name="homeCards"/> covers; gallery page: the first 4 items; photo page: the photo).
    /// A key in <paramref name="purged"/>'s deleted files is never returned.
    /// </summary>
    /// <param name="after">AFTER snapshots of the affected galleries; a gallery absent here no longer exists.</param>
    /// <param name="homeCards">The listing cards as projected after the change (covers carry their derivatives).</param>
    /// <param name="avifKeysOf">Image id → its AVIF keys, filled by the caller from the documents it read.</param>
    /// <param name="cameraCards">
    /// 013 E4: the camera cards after the change, in rank order. A purged <c>/c</c> warms the first
    /// 3 card covers' AVIF keys, read from the card covers exactly as <c>/g</c> does from
    /// <paramref name="homeCards"/>. <c>/c/{slug}</c> pages are skipped here; the caller supplies
    /// their ids. Null or empty: today's output.
    /// </param>
    public static IReadOnlyList<string> WarmFiles(
        PurgeSet purged,
        IEnumerable<GallerySnapshot> after,
        IReadOnlyList<GalleryCardDoc> homeCards,
        Func<string, IReadOnlyList<string>?> avifKeysOf,
        IReadOnlyList<CameraCardDoc>? cameraCards = null,
        IReadOnlyList<CameraSnapshot>? cameras = null)
    {
        var galleries = after.ToDictionary(g => g.Id, StringComparer.Ordinal);
        var camerasBySlug = (cameras ?? []).ToDictionary(c => c.Slug, StringComparer.Ordinal);
        var deleted = purged.FileKeys.ToHashSet(StringComparer.Ordinal);
        var keys = new List<string>();

        void AddImage(string iid)
        {
            var k = avifKeysOf(iid);
            if (k is not null) keys.AddRange(k);
        }

        foreach (var path in purged.PagePaths)
        {
            if (path == ListingPath)
            {
                foreach (var card in homeCards.Take(3))
                    if (card.Cover is not null)
                        keys.AddRange(card.Cover.Derivatives.Select(d => d.Avif));
                continue;
            }

            if (path == CameraListingPath)
            {
                foreach (var card in (cameraCards ?? []).Take(3))
                    if (card.Cover is not null)
                        keys.AddRange(card.Cover.Derivatives.Select(d => d.Avif));
                continue;
            }

            var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);

            // 013 E4: a camera page warms its first 4 photos, a camera photo page that photo, from
            // the AFTER cameras (a camera with no AFTER document has no page to warm).
            if (parts.Length is 2 or 3 && parts[0] == "c" && camerasBySlug.TryGetValue(parts[1], out var cam))
            {
                if (parts.Length == 2)
                    foreach (var iid in cam.Items.Take(4)) AddImage(iid);
                else if (cam.Items.Contains(parts[2]))
                    AddImage(parts[2]);
                continue;
            }

            if (parts.Length < 3 || parts[0] != "g") continue;
            if (!galleries.TryGetValue(parts[1], out var g) || !HasPage(g) || g.Slug != parts[2]) continue;

            if (parts.Length == 3)
                foreach (var iid in g.Items.Take(4)) AddImage(iid);
            else if (parts.Length == 4 && g.Items.Contains(parts[3]))
                AddImage(parts[3]);
        }

        return Sorted(keys.Where(k => !string.IsNullOrEmpty(k) && !deleted.Contains(k)))
            .Select(purged.Bases.File).ToArray();
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static IEnumerable<string> MembersPaths(GallerySnapshot? before, GallerySnapshot? after)
    {
        var paths = new List<string>();
        foreach (var g in new[] { before, after })
            if (HasPage(g)) paths.Add(GalleryPath(g!.Id, g.Slug));
        paths.AddRange(Pages(before, after, Changed(before, after)));
        paths.AddRange(Listing(before, after));
        return paths;
    }

    private static int IndexOf(IReadOnlyList<string> items, string id)
    {
        for (var i = 0; i < items.Count; i++)
            if (items[i] == id) return i;
        return -1;
    }

    private static IEnumerable<string> AllItems(GallerySnapshot? before, GallerySnapshot? after) =>
        (before?.Items ?? []).Concat(after?.Items ?? []).Distinct(StringComparer.Ordinal);

    internal static IReadOnlyList<string> Sorted(IEnumerable<string> values) =>
        values.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
}
