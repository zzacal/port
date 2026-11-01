namespace Gallery.Web.Admin.Auth;

/// <summary>
/// Google OIDC configuration bound from section "Authentication:Google".
/// </summary>
public sealed class GoogleOptions
{
    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";
    public string Authority { get; set; } = "https://accounts.google.com";
    public bool RequireHttpsMetadata { get; set; } = true;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecret);
}
