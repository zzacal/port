using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Gallery.Web.Tests.Fixtures;

/// <summary>
/// 020 T004 (SC-011): the bytes of every public page type over <see cref="HomeCaptureLibrary"/>,
/// written to a directory so a capture taken before the change can be compared with one taken after
/// it (<c>evidence/compare-public.sh</c>). Each gallery id is replaced by <c>G0…G4</c> (creation
/// order). The inlined <c>&lt;style&gt;</c> is emptied, as in <c>PublicOutputUnchangedTests</c>:
/// it is the whole scoped-CSS bundle, which legitimately changes when admin components are added or
/// removed (decisions row 42). Nothing else is stripped.
/// </summary>
public static partial class PublicCapture
{
    public const string UnknownGalleryId = "zzzzzzzzzzzzzzzzzzzzzzzzz2";

    public sealed record Page(string Label, string Path, HttpStatusCode Status, string Body);

    public static async Task<IReadOnlyList<Page>> CaptureAsync(LibraryHost h, HomeCaptureLibrary.Built built)
    {
        var pages = new List<Page>();

        async Task<string> Get(string label, string path)
        {
            var resp = await h.GetAsync(path);
            var raw = await resp.Content.ReadAsStringAsync();
            pages.Add(new Page(label, Normalise(path, built), resp.StatusCode, Normalise(raw, built)));
            return raw;
        }

        await Get("home", "/");
        await Get("listing", "/g");
        for (var i = 0; i < built.Listed.Count; i++)
        {
            var g = await GalleryHtml.LoadAsync(h, built.Listed[i]);
            await Get($"gallery-{i}", $"/g/{g.Id}/{g.Slug}");
            await Get($"photo-{i}", $"/g/{g.Id}/{g.Slug}/{g.Items[0]}");
        }

        var cameras = await Get("cameras", "/c");
        var slugs = CameraLinkRx().Matches(cameras).Select(m => m.Groups[1].Value).Distinct().ToList();
        for (var i = 0; i < slugs.Count; i++)
        {
            var camera = await Get($"camera-{i}", $"/c/{slugs[i]}");
            var photo = CameraPhotoLinkRx(slugs[i]).Match(camera);
            if (i == 0 && photo.Success) await Get("camera-photo-0", $"/c/{slugs[i]}/{photo.Groups[1].Value}");
        }

        await Get("unknown-gallery", $"/g/{UnknownGalleryId}/x");
        return pages;
    }

    /// <summary>Writes each page to <c>{dir}/{label}.html</c> and the list to <c>{dir}/index.txt</c>.</summary>
    public static void Write(string dir, IReadOnlyList<Page> pages)
    {
        Directory.CreateDirectory(dir);
        var index = new StringBuilder();
        foreach (var p in pages)
        {
            File.WriteAllText(Path.Combine(dir, p.Label + ".html"), p.Body);
            index.Append(p.Label).Append('\t').Append(p.Path).Append('\t').Append((int)p.Status).Append('\n');
        }
        File.WriteAllText(Path.Combine(dir, "index.txt"), index.ToString());
    }

    private static string Normalise(string text, HomeCaptureLibrary.Built built)
    {
        text = StyleBlock().Replace(text, m => m.Groups[1].Value + "</style>");
        for (var i = 0; i < built.GalleryIds.Count; i++)
            text = text.Replace(built.GalleryIds[i], $"G{i}", StringComparison.Ordinal);
        return text;
    }

    // Scoped CSS may add a b-xxxx attribute to the tag, so the opener is kept as written.
    [GeneratedRegex("(<style[^>]*>).*?</style>", RegexOptions.Singleline)]
    private static partial Regex StyleBlock();

    [GeneratedRegex("href=\"/c/([^\"/]+)\"")]
    private static partial Regex CameraLinkRx();

    private static Regex CameraPhotoLinkRx(string slug) => new($"href=\"/c/{Regex.Escape(slug)}/([^\"/]+)\"");
}
