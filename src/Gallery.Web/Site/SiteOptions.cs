namespace Gallery.Web.Site;

/// <summary>Site-level configuration (appsettings "Site" section).</summary>
public sealed class SiteOptions
{
    /// <summary>When true, the output cache is applied to public routes.</summary>
    public bool OutputCache { get; set; }
}
