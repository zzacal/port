using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Gallery.Core.Text;

public static class Slug
{
    private static readonly Regex NonAlphanumRun = new Regex("[^a-z0-9]+", RegexOptions.Compiled);

    public static string From(string title)
    {
        if (string.IsNullOrEmpty(title)) return "gallery";

        // Normalize to FormD to decompose diacritics, then drop NonSpacingMark
        var normalized = title.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);
        foreach (var c in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(char.ToLowerInvariant(c));
        }

        var lower = sb.ToString();

        // Collapse runs of non-[a-z0-9] to a single '-'
        var slug = NonAlphanumRun.Replace(lower, "-");

        // Trim leading/trailing '-'
        slug = slug.Trim('-');

        // Cut to 60 chars
        if (slug.Length > 60)
            slug = slug[..60];

        // Trim trailing '-' again after cut
        slug = slug.TrimEnd('-');

        return slug.Length == 0 ? "gallery" : slug;
    }
}
