namespace Gallery.Core.Images;

/// <summary>
/// 013 research R-02: the camera grouping key and display name. Pure. The key is
/// <c>Normalise(make) + "\u001f" + Normalise(model)</c>; the display name is
/// <see cref="ExifFormat.Camera"/> over the whitespace-collapsed (case kept) make and model, the
/// same formatter the photo page uses (FR-005).
/// </summary>
public static class CameraKey
{
    /// <summary>The separator between the normalised make and model in a key.</summary>
    public const string Separator = "\u001f";

    /// <summary>Trims, collapses whitespace runs to one space and lower-cases invariantly; null or blank gives <c>""</c>.</summary>
    public static string Normalise(string? value) => (Collapse(value) ?? string.Empty).ToLowerInvariant();

    /// <summary>Trims and collapses whitespace runs to one space, keeping case; null or blank gives null.</summary>
    public static string? Collapse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>The grouping key, or null when both make and model are blank (no camera).</summary>
    public static string? Key(string? make, string? model)
    {
        var m = Normalise(make);
        var d = Normalise(model);
        return m.Length == 0 && d.Length == 0 ? null : m + Separator + d;
    }

    /// <summary>The display name: <see cref="ExifFormat.Camera"/> over the collapsed make and model.</summary>
    public static string? DisplayName(string? make, string? model) =>
        ExifFormat.Camera(new Exif(Collapse(make), Collapse(model), null, null, null, null, null, null));
}
