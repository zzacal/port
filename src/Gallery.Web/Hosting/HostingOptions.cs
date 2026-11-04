namespace Gallery.Web.Hosting;

/// <summary>
/// Production hosting settings, section <c>Hosting</c> (012 contracts/hosting.md "HostingOptions").
/// This is a class, not a record, and <see cref="object.ToString"/> is NOT overridden, so the origin
/// secret can never be printed by accident (same rule as EdgeOptions).
/// </summary>
public sealed class HostingOptions
{
    public const string Section = "Hosting";

    /// <summary>"production" turns on <see cref="ProductionGuard"/>; anything else = off.</summary>
    public string? Target { get; set; }

    /// <summary>"cloudflare" turns on the origin guard and forwarded headers; anything else = off.</summary>
    public string? Edge { get; set; }

    /// <summary>Required (at least 32 characters) when <see cref="Edge"/> is cloudflare.</summary>
    public string? OriginSecret { get; set; }

    /// <summary>When set, Data Protection keys persist to this directory under application name "gallery".</summary>
    public string? DataProtectionKeysPath { get; set; }

    public bool IsProductionTarget =>
        string.Equals(Target, "production", StringComparison.OrdinalIgnoreCase);

    public bool IsCloudflareEdge =>
        string.Equals(Edge, "cloudflare", StringComparison.OrdinalIgnoreCase);
}
