namespace Gallery.Web.Tests.Fixtures;

/// <summary>
/// Generates encoded AVIF/WebP test fixtures in temp directories.
/// Fixtures are generated in-test and never committed.
/// Tests that need ffmpeg/cwebp check 'which' first and fail with a clear message.
/// </summary>
public static class EncodedFixtures
{
    private static readonly string FfmpegPath = "/opt/homebrew/bin/ffmpeg";
    private static readonly string CwebpPath = "/opt/homebrew/bin/cwebp";

    private static readonly string FixturesDir = Path.GetFullPath(
        Path.Combine(Render.RepoRoot, "tests", "fixtures"));

    public static readonly string Orientation6Jpg = Path.Combine(FixturesDir, "orientation-6.jpg");
    public static readonly string GpsCameraJpg = Path.Combine(FixturesDir, "gps-camera.jpg");

    /// <summary>
    /// Asserts ffmpeg is available, or skips with a clear message.
    /// </summary>
    public static void RequireFfmpeg()
    {
        if (!File.Exists(FfmpegPath))
            throw new SkipException($"ffmpeg not found at {FfmpegPath}; install with brew to run this test");
    }

    /// <summary>
    /// Asserts cwebp is available, or skips with a clear message.
    /// </summary>
    public static void RequireCwebp()
    {
        if (!File.Exists(CwebpPath))
            throw new SkipException($"cwebp not found at {CwebpPath}; install with brew to run this test");
    }

    /// <summary>
    /// Encodes a clean AVIF at the given width from orientation-6.jpg using ffmpeg.
    /// Returns the temp directory path (caller must delete when done).
    /// </summary>
    public static string MakeCleanAvif(int width, out string avifPath)
    {
        RequireFfmpeg();
        var tmpDir = Path.Combine(Path.GetTempPath(), $"gallery-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmpDir);
        avifPath = Path.Combine(tmpDir, $"clean-{width}.avif");

        Run(FfmpegPath, $"-hide_banner -loglevel error -y -i \"{Orientation6Jpg}\" -vf \"scale={width}:-2,format=yuv420p\" -frames:v 1 -map_metadata -1 -c:v libsvtav1 -crf 32 -preset 6 \"{avifPath}\"");
        Assert.True(File.Exists(avifPath), $"AVIF not created: {avifPath}");
        return tmpDir;
    }

    /// <summary>
    /// Encodes a clean WebP at the given width from orientation-6.jpg using cwebp.
    /// </summary>
    public static string MakeCleanWebP(int width, out string webpPath)
    {
        RequireFfmpeg();
        RequireCwebp();
        var tmpDir = Path.Combine(Path.GetTempPath(), $"gallery-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmpDir);

        // Decode to intermediate PNG first
        var intermediatePng = Path.Combine(tmpDir, "intermediate.png");
        Run(FfmpegPath, $"-hide_banner -loglevel error -y -i \"{Orientation6Jpg}\" -vf \"scale='min(2560,iw)':-2\" -frames:v 1 -map_metadata -1 \"{intermediatePng}\"");

        webpPath = Path.Combine(tmpDir, $"clean-{width}.webp");
        Run(CwebpPath, $"-quiet -q 78 -resize {width} 0 \"{intermediatePng}\" -o \"{webpPath}\"");
        Assert.True(File.Exists(webpPath), $"WebP not created: {webpPath}");
        return tmpDir;
    }

    /// <summary>
    /// Encodes a tainted WebP (with metadata) from gps-camera.jpg using cwebp -metadata all.
    /// </summary>
    public static string MakeTaintedWebP(int width, out string webpPath)
    {
        RequireCwebp();
        var tmpDir = Path.Combine(Path.GetTempPath(), $"gallery-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmpDir);
        webpPath = Path.Combine(tmpDir, $"tainted-{width}.webp");

        Run(CwebpPath, $"-quiet -metadata all -resize {width} 0 \"{GpsCameraJpg}\" -o \"{webpPath}\"");
        Assert.True(File.Exists(webpPath), $"Tainted WebP not created: {webpPath}");
        return tmpDir;
    }

    private static void Run(string exe, string args)
    {
        var psi = new System.Diagnostics.ProcessStartInfo(exe, args)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        using var p = System.Diagnostics.Process.Start(psi)!;
        var stderr = p.StandardError.ReadToEnd();
        p.WaitForExit();
        if (p.ExitCode != 0)
            throw new InvalidOperationException($"{exe} failed (exit {p.ExitCode}): {stderr}");
    }
}

/// <summary>
/// Used to signal a test should be skipped (xunit 2.9.3 has no dynamic skip,
/// so we throw a SkipException which xunit treats as a failure with a message;
/// for consistency with the task instructions we use Assert.Skip if available,
/// otherwise we fail with a clear message).
/// </summary>
public sealed class SkipException : Exception
{
    public SkipException(string message) : base(message) { }
}
