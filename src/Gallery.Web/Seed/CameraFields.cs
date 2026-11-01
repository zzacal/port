using Gallery.Core.Images;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;

namespace Gallery.Web.Seed;

/// <summary>
/// Reads only the allowlisted camera fields from a JPEG stream using MetadataExtractor.
/// Per FR-013 and R-05: only the main Exif directories (IFD0 and the Exif SubIFD) are read, and
/// within them only the eight allowlisted tags. No location data is ever read or returned.
/// Returns null when all fields are absent.
///
/// Each tag is looked up in BOTH directories. Cameras disagree on where Make and Model live:
/// the TIFF spec puts them in IFD0, but the owner's Leica and Fujifilm exports put them in the
/// Exif SubIFD (tests/fixtures/camera-subifd.jpg reproduces that layout). The SubIFD wins when
/// both carry a tag, since that is where these cameras write their own values.
/// </summary>
public static class CameraFields
{
    public static Exif? Read(Stream jpeg)
    {
        var directories = ImageMetadataReader.ReadMetadata(jpeg);
        var sources = new MetadataExtractor.Directory?[]
        {
            directories.OfType<ExifSubIfdDirectory>().FirstOrDefault(),
            directories.OfType<ExifIfd0Directory>().FirstOrDefault(),
        };

        var make = Text(sources, ExifDirectoryBase.TagMake);
        var model = Text(sources, ExifDirectoryBase.TagModel);
        var lens = Text(sources, ExifDirectoryBase.TagLensModel);
        var focalMm = Decimal(sources, ExifDirectoryBase.TagFocalLength);
        var focal35Mm = Int(sources, ExifDirectoryBase.Tag35MMFilmEquivFocalLength);
        var fNumber = Decimal(sources, ExifDirectoryBase.TagFNumber);
        var exposure = Decimal(sources, ExifDirectoryBase.TagExposureTime);
        var iso = Int(sources, ExifDirectoryBase.TagIsoEquivalent);

        if (make is null && model is null && lens is null &&
            focalMm is null && focal35Mm is null && fNumber is null &&
            exposure is null && iso is null)
            return null;

        return new Exif(make, model, lens, focalMm, focal35Mm, fNumber, exposure, iso);
    }

    private static MetadataExtractor.Directory? First(MetadataExtractor.Directory?[] sources, int tag)
        => sources.FirstOrDefault(d => d is not null && d.ContainsTag(tag));

    private static string? Text(MetadataExtractor.Directory?[] sources, int tag)
    {
        var value = First(sources, tag)?.GetDescription(tag)?.Trim();
        return string.IsNullOrEmpty(value) ? null : value;
    }

    private static decimal? Decimal(MetadataExtractor.Directory?[] sources, int tag)
        => First(sources, tag)?.GetObject(tag) is Rational r && r.Denominator != 0 ? (decimal)r.ToDouble() : null;

    private static int? Int(MetadataExtractor.Directory?[] sources, int tag)
    {
        var obj = First(sources, tag)?.GetObject(tag);
        return obj is null or Array ? null : Convert.ToInt32(obj);
    }
}
