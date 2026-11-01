using Gallery.Web.Upload;
using Gallery.Web.Tests.Fixtures;

namespace Gallery.Web.Tests;

/// <summary>
/// T030: DerivativeValidator tests.
/// Uses EncodedFixtures to generate test files via ffmpeg/cwebp.
/// Tests skip with a clear message when tools are unavailable.
/// </summary>
public class DerivativeValidatorTests
{
    // orientation-6.jpg is 400x200 with EXIF Orientation=6 (rotate 90 CW).
    // After ffmpeg applies orientation: corrected image is 200x400 (portrait).
    // Encoded at width 320: height = 320 * (400/200) = 640.
    // So the aspect ratio for validator = 320/640 = 0.5.
    private const double AspectRatio = 0.5;

    private static void CleanupDir(string? dir)
    {
        if (dir is not null && Directory.Exists(dir))
            try { Directory.Delete(dir, true); } catch { }
    }

    [Fact]
    public void Clean_AVIF_passes_validation()
    {
        string? tmpDir = null;
        try
        {
            tmpDir = EncodedFixtures.MakeCleanAvif(320, out var avifPath);
            using var fs = File.OpenRead(avifPath);
            var result = DerivativeValidator.Validate(fs, "avif", 320, AspectRatio);
            Assert.True(result.IsValid, $"Expected valid, got: {result.Reason}");
        }
        catch (SkipException ex) { Assert.Fail($"SKIP: {ex.Message}"); }
        finally { CleanupDir(tmpDir); }
    }

    [Fact]
    public void Clean_WebP_passes_validation()
    {
        string? tmpDir = null;
        try
        {
            tmpDir = EncodedFixtures.MakeCleanWebP(320, out var webpPath);
            using var fs = File.OpenRead(webpPath);
            var result = DerivativeValidator.Validate(fs, "webp", 320, AspectRatio);
            Assert.True(result.IsValid, $"Expected valid, got: {result.Reason}");
        }
        catch (SkipException ex) { Assert.Fail($"SKIP: {ex.Message}"); }
        finally { CleanupDir(tmpDir); }
    }

    [Fact]
    public void Tainted_WebP_with_metadata_fails_with_reason_naming_Exif_or_GPS()
    {
        string? tmpDir = null;
        try
        {
            // gps-camera.jpg has GPS data; -metadata all preserves it in WebP
            tmpDir = EncodedFixtures.MakeTaintedWebP(320, out var webpPath);
            using var fs = File.OpenRead(webpPath);
            var result = DerivativeValidator.Validate(fs, "webp", 320, AspectRatio);
            Assert.False(result.IsValid, "Expected tainted WebP to fail validation");
            Assert.NotNull(result.Reason);
            // Reason should mention Exif or GPS (the disallowed directory names)
            var lowerReason = result.Reason!.ToLowerInvariant();
            Assert.True(lowerReason.Contains("exif") || lowerReason.Contains("gps"),
                $"Expected reason to name Exif or GPS, got: {result.Reason}");
        }
        catch (SkipException ex) { Assert.Fail($"SKIP: {ex.Message}"); }
        finally { CleanupDir(tmpDir); }
    }

    [Fact]
    public void WebP_declared_as_avif_fails()
    {
        string? tmpDir = null;
        try
        {
            tmpDir = EncodedFixtures.MakeCleanWebP(320, out var webpPath);
            using var fs = File.OpenRead(webpPath);
            // Declare as avif, but file is actually WebP
            var result = DerivativeValidator.Validate(fs, "avif", 320, AspectRatio);
            Assert.False(result.IsValid, "Expected WebP declared as avif to fail");
            Assert.NotNull(result.Reason);
            Assert.Contains("AVIF", result.Reason!, StringComparison.OrdinalIgnoreCase);
        }
        catch (SkipException ex) { Assert.Fail($"SKIP: {ex.Message}"); }
        finally { CleanupDir(tmpDir); }
    }

    [Fact]
    public void Width_320_declared_as_640_fails()
    {
        string? tmpDir = null;
        try
        {
            tmpDir = EncodedFixtures.MakeCleanWebP(320, out var webpPath);
            using var fs = File.OpenRead(webpPath);
            // File is 320 wide but we declare 640
            var result = DerivativeValidator.Validate(fs, "webp", 640, AspectRatio);
            Assert.False(result.IsValid, "Expected width mismatch to fail");
            Assert.NotNull(result.Reason);
            Assert.Contains("Width", result.Reason!, StringComparison.OrdinalIgnoreCase);
        }
        catch (SkipException ex) { Assert.Fail($"SKIP: {ex.Message}"); }
        finally { CleanupDir(tmpDir); }
    }
}
