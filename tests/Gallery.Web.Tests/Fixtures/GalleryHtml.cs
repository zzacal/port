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
