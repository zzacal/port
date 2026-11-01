using System.Globalization;
using Gallery.Core.Images;
using Gallery.Web.Seed;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using MetadataExtractor.Formats.FileType;
using MetadataExtractor.Formats.Jpeg;
using MetadataExtractor.Util;

namespace Gallery.Web.Upload;

/// <summary>
/// Outcome of <see cref="MasterCheck.Check"/>. When <c>NeedsMoreBytes</c> is true the caller retries
/// ONCE with a 1 MB prefix; if the retry still says <c>NeedsMoreBytes</c>, the caller rejects with
/// <c>Reason</c> ("unreadable JPEG header"), which is always set alongside it.
/// </summary>
public sealed record MasterResult(
    bool Ok, int UprightWidth, int UprightHeight, Exif? Camera, bool NeedsMoreBytes, string? Reason)
{
    internal static MasterResult Fail(string reason) => new(false, 0, 0, null, false, reason);
}

/// <summary>
/// Header-only check of a staged master JPEG from a byte prefix (research R-06). Pure over bytes;
/// no pixel decode (III.8). Camera fields come from <see cref="CameraFields.Read"/>, CALLED, inside a
/// try/catch so a throw means "no camera details" (decisions row 17).
/// </summary>
public static class MasterCheck
{
    public const string NotJpeg = "not a JPEG";
    public const string Unreadable = "unreadable JPEG header";

    /// <param name="prefix">The first bytes of the object (the whole object when it is short).</param>
    /// <param name="length">The object's full length, from HEAD.</param>
    /// <param name="maxBytes">The configured per-original limit (<c>UploadOptions.MaxFileBytes</c>).</param>
    public static MasterResult Check(byte[] prefix, long length, long maxBytes)
    {
        if (length > maxBytes)
            return MasterResult.Fail(OverLimit(length, maxBytes));
        if (length <= 0 || prefix.Length == 0)
            return MasterResult.Fail(Unreadable);

        FileType type;
        try
        {
            using var s = new MemoryStream(prefix, writable: false);
            type = FileTypeDetector.DetectFileType(s);
        }
        catch (Exception)
        {
            type = FileType.Unknown;
        }
        if (type != FileType.Jpeg)
            return MasterResult.Fail(NotJpeg);

        // More bytes exist when the prefix is shorter than the object.
        bool more = prefix.LongLength < length;

        IReadOnlyList<MetadataExtractor.Directory> dirs;
        try
        {
            using var s = new MemoryStream(prefix, writable: false);
            dirs = ImageMetadataReader.ReadMetadata(s);
        }
        catch (Exception)
        {
            return NeedMore(more);
        }

        var ft = dirs.OfType<FileTypeDirectory>().FirstOrDefault();
        if (ft is null || ft.GetObject(FileTypeDirectory.TagDetectedFileTypeName) as string != "JPEG")
            return MasterResult.Fail(NotJpeg);

        var jpeg = dirs.OfType<JpegDirectory>().FirstOrDefault();
        if (jpeg is null
            || !jpeg.TryGetInt32(JpegDirectory.TagImageWidth, out var w)
            || !jpeg.TryGetInt32(JpegDirectory.TagImageHeight, out var h)
            || w <= 0 || h <= 0)
            return NeedMore(more);

        int orientation = 1;
        var ifd0 = dirs.OfType<ExifIfd0Directory>().FirstOrDefault();
        if (ifd0 is not null && ifd0.TryGetInt32(ExifDirectoryBase.TagOrientation, out var o))
            orientation = o;
        if (orientation is >= 5 and <= 8)
            (w, h) = (h, w);

        Exif? camera;
        try
        {
            using var s = new MemoryStream(prefix, writable: false);
            camera = CameraFields.Read(s);
        }
        catch (Exception)
        {
            camera = null;
        }

        return new MasterResult(true, w, h, camera, false, null);
    }

    /// <summary>"{x.x} MB, over the {limit} MB limit" (contracts/http.md U3/U4).</summary>
    public static string OverLimit(long length, long maxBytes) =>
        string.Create(CultureInfo.InvariantCulture,
            $"{length / 1_048_576d:0.0} MB, over the {maxBytes / 1_048_576d:0.#} MB limit");

    private static MasterResult NeedMore(bool more) =>
        more ? new MasterResult(false, 0, 0, null, true, Unreadable) : MasterResult.Fail(Unreadable);
}
