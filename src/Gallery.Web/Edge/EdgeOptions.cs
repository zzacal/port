namespace Gallery.Web.Edge;

/// <summary>
/// Edge purge and warm settings, section <c>Edge</c> (data-model "Settings", decisions rows 6 and 21).
/// <c>ZoneId</c> and <c>ApiToken</c> come from user-secrets only. This is a class, not a record, and
/// <see cref="object.ToString"/> is NOT overridden, so the token can never be printed by accident
/// (FR-043, [SAFETY]).
/// </summary>
public sealed class EdgeOptions
{
    public const string Section = "Edge";

    public string? ZoneId { get; set; }
    public string? ApiToken { get; set; }

    /// <summary>Host the public pages are addressed on (pages are purged only with <see cref="PurgePages"/>).</summary>
    public string SiteBaseUrl { get; set; } = "https://localhost:5081";

    /// <summary>False until the hosting feature puts the site hostname on the zone (decisions row 21).</summary>
    public bool PurgePages { get; set; }

    public int MaxUrlsPerCall { get; set; } = 100;
    public int MaxUrlsPerSecond { get; set; } = 800;
    public int QueueCapacity { get; set; } = 1000;

    /// <summary>True when both credentials are present: the dev-zone provider is used (FR-042).</summary>
    public bool HasCredentials =>
        !string.IsNullOrWhiteSpace(ZoneId) && !string.IsNullOrWhiteSpace(ApiToken);

    /// <summary>Binds the section and validates the limits; throws naming the key, never a value.</summary>
    public static EdgeOptions Load(IConfiguration c)
    {
        var o = c.GetSection(Section).Get<EdgeOptions>() ?? new EdgeOptions();
        if (string.IsNullOrWhiteSpace(o.SiteBaseUrl))
            throw new InvalidOperationException("Edge:SiteBaseUrl must not be empty.");
        if (o.MaxUrlsPerCall is < 1 or > 100)
            throw new InvalidOperationException("Edge:MaxUrlsPerCall must be between 1 and 100.");
        if (o.MaxUrlsPerSecond < 1)
            throw new InvalidOperationException("Edge:MaxUrlsPerSecond must be greater than 0.");
        if (o.QueueCapacity < 1)
            throw new InvalidOperationException("Edge:QueueCapacity must be greater than 0.");
        return o;
    }
}
