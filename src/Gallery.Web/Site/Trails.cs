using Gallery.Web.Components.Molecules;

namespace Gallery.Web.Site;

/// <summary>
/// 011 (contracts/components.md C3, FR-011 to FR-015): every public breadcrumb trail, rooted at Home.
/// Pages call these; none builds its own trail, so a root rename is one line here
/// (<c>TrailSourceGuardTests</c>). <see cref="Under"/> only puts "/" and "/g" ahead of the caller's
/// own crumbs, so a private gallery's trail links to no other gallery (II.2).
/// </summary>
public static class Trails
{
    public const string HomeLabel = "Home", ListingLabel = "Galleries";
    public const string HomeHref = "/", ListingHref = "/g";
    public static readonly IReadOnlyList<Crumb> Home     = [new(HomeLabel)];
    public static readonly IReadOnlyList<Crumb> Listing  = [new(HomeLabel, HomeHref), new(ListingLabel)];
    public static readonly IReadOnlyList<Crumb> NotFound = [new(HomeLabel, HomeHref), new("Not found")];
    public static readonly IReadOnlyList<Crumb> Default  = [new(HomeLabel, HomeHref)];
    public static IReadOnlyList<Crumb> Under(params Crumb[] tail) =>
        [new(HomeLabel, HomeHref), new(ListingLabel, ListingHref), .. tail];
}
