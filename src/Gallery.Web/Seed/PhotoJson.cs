using System.Text.Json;
using System.Text.Json.Serialization;

namespace Gallery.Web.Seed;

/// <summary>
/// Represents a derivative entry in photo.json.
/// </summary>
public sealed class PhotoJsonDerivative
{
    [JsonPropertyName("w")]          public int W          { get; set; }
    [JsonPropertyName("avif")]       public string Avif    { get; set; } = string.Empty;
    [JsonPropertyName("webp")]       public string Webp    { get; set; } = string.Empty;
    [JsonPropertyName("bytesAvif")]  public long BytesAvif { get; set; }
    [JsonPropertyName("bytesWebp")]  public long BytesWebp { get; set; }
}

/// <summary>
/// Represents the photo.json file written by prepare-photos.sh in .dev-prepared/{sourceHash}/.
/// </summary>
public sealed class PhotoJson
{
    [JsonPropertyName("sourceHash")]   public string SourceHash  { get; set; } = string.Empty;
    [JsonPropertyName("sourceName")]   public string SourceName  { get; set; } = string.Empty;
    [JsonPropertyName("width")]        public int Width          { get; set; }
    [JsonPropertyName("height")]       public int Height         { get; set; }
    [JsonPropertyName("color")]        public string Color       { get; set; } = string.Empty;
    [JsonPropertyName("preview")]      public string Preview     { get; set; } = string.Empty;
    [JsonPropertyName("derivatives")]  public List<PhotoJsonDerivative> Derivatives { get; set; } = [];
}

public static class PhotoJsonLoader
{
    private static readonly JsonSerializerOptions _opts = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public static PhotoJson Load(string path)
    {
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<PhotoJson>(json, _opts)
               ?? throw new InvalidOperationException($"Failed to deserialize {path}");
    }
}
