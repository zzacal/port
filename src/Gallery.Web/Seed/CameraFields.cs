using Gallery.Core.Images;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;

namespace Gallery.Web.Seed;

/// <summary>
/// Reads only the allowlisted camera fields from a JPEG stream using MetadataExtractor.
/// Per FR-013 and R-05: only ExifIfd0Directory and ExifSubIfdDirectory are read.
/// No location data is ever read or returned. Returns null when all fields are absent.
/// </summary>
public static class CameraFields
{
    public static Exif? Read(Stream jpeg)
    {
        var directories = ImageMetadataReader.ReadMetadata(jpeg);

        string? make = null, model = null, lens = null;
        decimal? focalMm = null, fNumber = null, exposure = null;
        int? focal35Mm = null, iso = null;

        foreach (var dir in directories)
        {
            if (dir is ExifIfd0Directory ifd0)
            {
                if (ifd0.ContainsTag(ExifDirectoryBase.TagMake))
                    make = ifd0.GetDescription(ExifDirectoryBase.TagMake)?.Trim();
                if (ifd0.ContainsTag(ExifDirectoryBase.TagModel))
                    model = ifd0.GetDescription(ExifDirectoryBase.TagModel)?.Trim();
            }
            else if (dir is ExifSubIfdDirectory subIfd)
            {
                if (subIfd.ContainsTag(ExifDirectoryBase.TagLensModel))
                    lens = subIfd.GetDescription(ExifDirectoryBase.TagLensModel)?.Trim();

                if (subIfd.ContainsTag(ExifDirectoryBase.TagFocalLength))
                {
                    var obj = subIfd.GetObject(ExifDirectoryBase.TagFocalLength);
                    if (obj is Rational r) focalMm = (decimal)r.ToDouble();
                }

                if (subIfd.ContainsTag(ExifDirectoryBase.Tag35MMFilmEquivFocalLength))
                {
                    var obj = subIfd.GetObject(ExifDirectoryBase.Tag35MMFilmEquivFocalLength);
                    focal35Mm = Convert.ToInt32(obj);
                }

                if (subIfd.ContainsTag(ExifDirectoryBase.TagFNumber))
                {
                    var obj = subIfd.GetObject(ExifDirectoryBase.TagFNumber);
                    if (obj is Rational r) fNumber = (decimal)r.ToDouble();
                }

                if (subIfd.ContainsTag(ExifDirectoryBase.TagExposureTime))
                {
                    var obj = subIfd.GetObject(ExifDirectoryBase.TagExposureTime);
                    if (obj is Rational r) exposure = (decimal)r.ToDouble();
                }

                if (subIfd.ContainsTag(ExifDirectoryBase.TagIsoEquivalent))
                {
                    var obj = subIfd.GetObject(ExifDirectoryBase.TagIsoEquivalent);
                    iso = Convert.ToInt32(obj);
                }
            }
        }

        if (make is null && model is null && lens is null &&
            focalMm is null && focal35Mm is null && fNumber is null &&
            exposure is null && iso is null)
            return null;

        return new Exif(make, model, lens, focalMm, focal35Mm, fNumber, exposure, iso);
    }
}
