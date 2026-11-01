namespace Gallery.Web.Upload;

/// <summary>
/// 008 upload settings, config section <c>Upload</c> (data-model "Settings"). The page reads the
/// same instance the server enforces with (FR-002). The ladder is NOT here: it lives in
/// <see cref="Gallery.Core.Images.Ladder"/> so it cannot drift from IV.6.
/// </summary>
public sealed class UploadOptions
{
    public const string Section = "Upload";

    public long MaxFileBytes { get; set; } = 20_971_520;
    public int MaxPhotosPerBatch { get; set; } = 100;
    public int PresignMinutes { get; set; } = 15;
    public int HeartbeatTimeoutSeconds { get; set; } = 120;
    public int MasterHeaderBytes { get; set; } = 262_144;
    public int PlaceholderMaxBytes { get; set; } = 1536;
    public int PlaceholderMaxEdge { get; set; } = 32;

    /// <summary>Shown on the page: <c>MaxFileBytes / 1048576</c>.</summary>
    public long MaxMb => MaxFileBytes / 1_048_576;

    public TimeSpan PresignExpiry => TimeSpan.FromMinutes(PresignMinutes);
    public TimeSpan HeartbeatTimeout => TimeSpan.FromSeconds(HeartbeatTimeoutSeconds);

    /// <summary>
    /// Binds the <c>Upload</c> section over the defaults and validates it. Throws
    /// <see cref="InvalidOperationException"/> naming the offending key (startup fails).
    /// </summary>
    public static UploadOptions Load(IConfiguration c)
    {
        var o = c.GetSection(Section).Get<UploadOptions>() ?? new UploadOptions();
        o.Validate();
        return o;
    }

    public void Validate()
    {
        if (PresignMinutes is < 1 or > 15)
            throw Bad(nameof(PresignMinutes), "must be between 1 and 15");
        if (MaxFileBytes <= 0) throw Bad(nameof(MaxFileBytes), "must be greater than 0");
        if (MaxPhotosPerBatch <= 0) throw Bad(nameof(MaxPhotosPerBatch), "must be greater than 0");
        if (HeartbeatTimeoutSeconds <= 0) throw Bad(nameof(HeartbeatTimeoutSeconds), "must be greater than 0");
        if (MasterHeaderBytes <= 0) throw Bad(nameof(MasterHeaderBytes), "must be greater than 0");
        if (PlaceholderMaxBytes <= 0) throw Bad(nameof(PlaceholderMaxBytes), "must be greater than 0");
        if (PlaceholderMaxEdge <= 0) throw Bad(nameof(PlaceholderMaxEdge), "must be greater than 0");
    }

    private static InvalidOperationException Bad(string key, string rule) =>
        new($"Configuration {Section}:{key} {rule}.");
}
