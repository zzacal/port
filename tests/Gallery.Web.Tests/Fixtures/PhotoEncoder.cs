using System.Security.Cryptography;
using Gallery.Web.Data;

namespace Gallery.Web.Tests.Fixtures;

/// <summary>
/// 010 T004: the photo encoding <see cref="BrowserSiteFixture"/> used privately, moved here
/// unchanged so a second browser fixture (<c>NavBrowserFixture</c>) encodes photos the same way.
/// A distinct 1600x1200 image per index, encoded like scripts/prepare-photos.sh, with
/// content-hash file names. Needs ffmpeg and cwebp.
/// </summary>
internal static class PhotoEncoder
{
    public static readonly int[] Ladder = [320, 640, 960, 1280];
    public static readonly string[] Colours = ["red", "green", "blue", "orange", "purple", "teal"];

    public sealed record Encoded(string Id, int Width, int Height, List<DerivativeDoc> Derivatives, string Preview);

    // A distinct 1600x1200 image per index, encoded like scripts/prepare-photos.sh.
    public static Encoded EncodePhoto(string dir, int index)
    {
        var png = Path.Combine(dir, $"src-{index}.png");
        Run("/opt/homebrew/bin/ffmpeg", $"-hide_banner -loglevel error -y -f lavfi -i \"color=c={Colours[index]}:s=1600x1200,drawbox=x={index * 200}:y=100:w=300:h=900:c=white:t=fill\" -frames:v 1 \"{png}\"");
        var derivatives = new List<DerivativeDoc>();
        foreach (var w in Ladder)
        {
            var avif = Named(dir, Path.Combine(dir, $"tmp-{index}-{w}.avif"), path =>
                Run("/opt/homebrew/bin/ffmpeg", $"-hide_banner -loglevel error -y -i \"{png}\" -vf \"scale={w}:-2,format=yuv420p\" -frames:v 1 -map_metadata -1 -c:v libsvtav1 -crf 32 -preset 8 \"{path}\""));
            var webp = Named(dir, Path.Combine(dir, $"tmp-{index}-{w}.webp"), path =>
                Run("/opt/homebrew/bin/cwebp", $"-quiet -q 78 -resize {w} 0 \"{png}\" -o \"{path}\""));
            derivatives.Add(new DerivativeDoc { W = w, Avif = avif, Webp = webp, BytesAvif = 1, BytesWebp = 1 });
        }
        var preview = Path.Combine(dir, $"preview-{index}.webp");
        Run("/opt/homebrew/bin/cwebp", $"-quiet -q 40 -resize 32 24 \"{png}\" -o \"{preview}\"");
        var id = new string((char)('a' + index), 25) + "2";
        return new Encoded(id, 1280, 960, derivatives, "data:image/webp;base64," + Convert.ToBase64String(File.ReadAllBytes(preview)));
    }

    // Content-hash name, as the real pipeline does: identical bytes would share a cache entry.
    public static string Named(string dir, string tmpPath, Action<string> encode)
    {
        encode(tmpPath);
        var ext = Path.GetExtension(tmpPath);
        var name = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(tmpPath)))[..16] + ext;
        File.Move(tmpPath, Path.Combine(dir, name), overwrite: true);
        return name;
    }

    public static void Run(string exe, string args)
    {
        using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe, args)
        {
            RedirectStandardError = true, RedirectStandardOutput = true, UseShellExecute = false,
        })!;
        var err = p.StandardError.ReadToEnd();
        p.WaitForExit();
        if (p.ExitCode != 0) throw new InvalidOperationException($"{exe} failed: {err}");
    }
}
