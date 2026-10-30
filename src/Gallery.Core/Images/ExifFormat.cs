using System.Globalization;

namespace Gallery.Core.Images;

public static class ExifFormat
{
    public static string? Camera(Exif? exif)
    {
        if (exif is null) return null;
        var make = exif.Make;
        var model = exif.Model;
        if (make is null && model is null) return null;
        if (make is null) return model;
        if (model is null) return make;
        // Drop Make when Model already starts with Make (case-insensitive)
        if (model.StartsWith(make, StringComparison.OrdinalIgnoreCase))
            return model;
        return $"{make} {model}";
    }

    public static string? Lens(Exif? exif)
    {
        if (exif is null) return null;
        return exif.Lens;
    }

    public static string? FocalLength(Exif? exif)
    {
        if (exif is null) return null;
        if (exif.FocalLengthMm is null) return null;
        var mm = exif.FocalLengthMm.Value;
        var mmStr = mm.ToString("0.#", CultureInfo.InvariantCulture);
        var result = $"{mmStr}mm";
        if (exif.FocalLength35Mm is int eq && (decimal)eq != mm)
            result += $" ({eq}mm eq.)";
        return result;
    }

    public static string? Aperture(Exif? exif)
    {
        if (exif is null) return null;
        if (exif.FNumber is null) return null;
        return $"f/{exif.FNumber.Value.ToString("0.0", CultureInfo.InvariantCulture)}";
    }

    public static string? Shutter(Exif? exif)
    {
        if (exif is null) return null;
        if (exif.ExposureSeconds is null) return null;
        var s = exif.ExposureSeconds.Value;
        if (s < 1m)
        {
            var denom = (long)Math.Round((double)(1m / s));
            return $"1/{denom}s";
        }
        return $"{s.ToString("0.#", CultureInfo.InvariantCulture)}s";
    }

    public static string? Iso(Exif? exif)
    {
        if (exif is null) return null;
        if (exif.Iso is null) return null;
        return $"ISO {exif.Iso.Value}";
    }
}
