using MetadataExtractor;
using MetadataExtractor.Formats.FileType;
using MetadataExtractor.Formats.WebP;
using MetadataExtractor.Formats.Heif;

namespace Gallery.Web.Seed;

/// <summary>
/// Result of validating a single derivative file.
/// </summary>
public sealed record ValidationResult(bool IsValid, string? Reason)
{
    public static ValidationResult Ok() => new(true, null);
    public static ValidationResult Fail(string reason) => new(false, reason);
}

/// <summary>
/// Second independent reader for derivative files (constitution III.5–III.7, III.10;
/// contracts/seed.md step 4; research R-04 "Second reader").
/// Uses MetadataExtractor to detect file type, validate allowed directories,
/// and verify declared dimensions.
/// </summary>
public static class DerivativeValidator
{
    /// <summary>
    /// Allowlisted MetadataExtractor directory names for clean encoder output.
    /// Anything else (Exif, GPS, XMP, IPTC, ICC Profile, ...) rejects the file.
    /// </summary>
    private static readonly HashSet<string> AllowedDirs = new(StringComparer.Ordinal)
    {
        "File Type",
        "File",
        "WebP",
        "HEIC Primary Item Properties",
        "QuickTime File Type",
    };

    public static ValidationResult Validate(Stream file, string declaredExt, int declaredWidth, double aspectRatio)
    {
        IReadOnlyList<MetadataExtractor.Directory> directories;
        try
        {
            directories = ImageMetadataReader.ReadMetadata(file);
        }
        catch (Exception ex)
        {
            return ValidationResult.Fail($"MetadataExtractor error: {ex.Message}");
        }

        // Check for disallowed directories
        foreach (var dir in directories)
        {
            if (!AllowedDirs.Contains(dir.Name))
                return ValidationResult.Fail($"Disallowed metadata directory: {dir.Name}");
        }

        // Find file type directory
        FileTypeDirectory? fileTypeDir = null;
        foreach (var dir in directories)
        {
            if (dir is FileTypeDirectory ft) { fileTypeDir = ft; break; }
        }

        if (fileTypeDir is null)
            return ValidationResult.Fail("Could not detect file type");

        var ext = declaredExt.TrimStart('.').ToLowerInvariant();
        var detectedTypeName = fileTypeDir.GetObject(FileTypeDirectory.TagDetectedFileTypeName) as string ?? "";

        if (ext == "avif")
        {
            if (!detectedTypeName.Equals("AVIF", StringComparison.OrdinalIgnoreCase))
                return ValidationResult.Fail($"Expected AVIF file type, detected: {detectedTypeName}");
        }
        else if (ext == "webp")
        {
            if (!detectedTypeName.Equals("WebP", StringComparison.OrdinalIgnoreCase))
                return ValidationResult.Fail($"Expected WebP file type, detected: {detectedTypeName}");
        }
        else
        {
            return ValidationResult.Fail($"Unknown extension: {declaredExt}");
        }

        // Check dimensions
        int? width = null, height = null;
        foreach (var dir in directories)
        {
            if (dir is WebPDirectory webp)
            {
                var wObj = webp.GetObject(WebPDirectory.TagImageWidth);
                var hObj = webp.GetObject(WebPDirectory.TagImageHeight);
                if (wObj is not null) width = Convert.ToInt32(wObj);
                if (hObj is not null) height = Convert.ToInt32(hObj);
                break;
            }
            if (dir is HeicImagePropertiesDirectory heic)
            {
                var wObj = heic.GetObject(HeicImagePropertiesDirectory.TagImageWidth);
                var hObj = heic.GetObject(HeicImagePropertiesDirectory.TagImageHeight);
                if (wObj is not null) width = Convert.ToInt32(wObj);
                if (hObj is not null) height = Convert.ToInt32(hObj);
                break;
            }
        }

        if (width is null || height is null)
            return ValidationResult.Fail("Could not read image dimensions");

        if (width.Value != declaredWidth)
            return ValidationResult.Fail($"Width mismatch: declared {declaredWidth}, actual {width.Value}");

        var expectedHeight = (int)Math.Round(declaredWidth / aspectRatio);
        if (Math.Abs(height.Value - expectedHeight) > 2)
            return ValidationResult.Fail($"Height mismatch: expected ~{expectedHeight} (±2), actual {height.Value}");

        return ValidationResult.Ok();
    }
}
