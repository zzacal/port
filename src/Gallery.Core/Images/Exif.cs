namespace Gallery.Core.Images;

public record Exif(
    string? Make,
    string? Model,
    string? Lens,
    decimal? FocalLengthMm,
    int? FocalLength35Mm,
    decimal? FNumber,
    decimal? ExposureSeconds,
    int? Iso
);
