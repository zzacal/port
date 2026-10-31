using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Gallery.Web.Seed;

// ── Manifest model ────────────────────────────────────────────────────────────

public sealed class ManifestGallery
{
    [JsonPropertyName("key")]         public string Key { get; set; } = string.Empty;
    [JsonPropertyName("title")]       public string? Title { get; set; }
    [JsonPropertyName("description")] public string? Description { get; set; }
    [JsonPropertyName("visibility")]  public string? Visibility { get; set; }
    [JsonPropertyName("cover")]       public string? Cover { get; set; }
    [JsonPropertyName("photos")]      public List<string> Photos { get; set; } = [];
}

public sealed class ManifestPhoto
{
    [JsonPropertyName("title")]   public string? Title { get; set; }
    [JsonPropertyName("caption")] public string? Caption { get; set; }
}

public sealed class Manifest
{
    [JsonPropertyName("galleries")] public List<ManifestGallery> Galleries { get; set; } = [];
    [JsonPropertyName("photos")]    public Dictionary<string, ManifestPhoto> Photos { get; set; } = [];
}

// ── Loader/validator ──────────────────────────────────────────────────────────

public static partial class ManifestLoader
{
    [GeneratedRegex(@"^[a-z0-9\-]{1,40}$")]
    private static partial Regex KeyPattern();

    private static readonly JsonSerializerOptions _opts = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    /// <summary>
    /// Loads the manifest from <paramref name="path"/> and returns it plus any validation errors.
    /// Returns all errors at once so callers can print them all.
    /// </summary>
    public static (Manifest? manifest, IReadOnlyList<string> errors) Load(string path)
    {
        if (!File.Exists(path))
            return (null, [$"Manifest file not found: {path}"]);

        Manifest manifest;
        try
        {
            var json = File.ReadAllText(path);
            manifest = JsonSerializer.Deserialize<Manifest>(json, _opts)
                       ?? new Manifest();
        }
        catch (JsonException ex)
        {
            return (null, [$"Manifest JSON parse error: {ex.Message}"]);
        }

        var errors = Validate(manifest);
        return errors.Count == 0 ? (manifest, []) : (null, errors);
    }

    public static IReadOnlyList<string> Validate(Manifest manifest)
    {
        var errors = new List<string>();
        var seenKeys = new HashSet<string>(StringComparer.Ordinal);

        foreach (var g in manifest.Galleries)
        {
            // Duplicate key
            if (!seenKeys.Add(g.Key))
                errors.Add($"Gallery key \"{g.Key}\": duplicate key");

            // Bad key pattern
            if (!KeyPattern().IsMatch(g.Key))
                errors.Add($"Gallery key \"{g.Key}\": key must match ^[a-z0-9-]{{1,40}}$");

            // Visibility
            var vis = g.Visibility ?? "private";
            if (vis != "public" && vis != "private")
                errors.Add($"Gallery key \"{g.Key}\": visibility must be \"public\" or \"private\", got \"{g.Visibility}\"");

            // Cover not in photos
            if (g.Cover is not null && !g.Photos.Contains(g.Cover))
                errors.Add($"Gallery key \"{g.Key}\": cover \"{g.Cover}\" is not in photos list");

            // Photo listed twice in same gallery
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var p in g.Photos)
            {
                if (!seen.Add(p))
                    errors.Add($"Gallery key \"{g.Key}\": photo \"{p}\" appears more than once");
            }

            // Title length (1–200 chars; null becomes filename without ext — OK at this point)
            // Resolved title is validated; a null title is filled from filename at seed time
            var resolvedTitle = g.Title;
            if (resolvedTitle is not null && (resolvedTitle.Length < 1 || resolvedTitle.Length > 200))
                errors.Add($"Gallery key \"{g.Key}\": title length must be 1–200 chars");

            // Description length (0–5000)
            if (g.Description is not null && g.Description.Length > 5000)
                errors.Add($"Gallery key \"{g.Key}\": description exceeds 5000 chars");
        }

        // Validate per-photo entries
        foreach (var (name, photo) in manifest.Photos)
        {
            if (photo.Title is not null && (photo.Title.Length < 1 || photo.Title.Length > 200))
                errors.Add($"Photo \"{name}\": title length must be 1–200 chars");
            if (photo.Caption is not null && photo.Caption.Length > 2000)
                errors.Add($"Photo \"{name}\": caption exceeds 2000 chars");
        }

        return errors;
    }

    /// <summary>
    /// Returns the effective visibility string ("public" or "private"), applying the default.
    /// </summary>
    public static string EffectiveVisibility(ManifestGallery g) =>
        (g.Visibility ?? "private") == "public" ? "public" : "private";

    /// <summary>
    /// Returns the effective photo title: the manifest title, or the file name without extension.
    /// </summary>
    public static string EffectiveTitle(string fileName, ManifestPhoto? photo) =>
        photo?.Title is { Length: > 0 } t ? t : Path.GetFileNameWithoutExtension(fileName);
}
