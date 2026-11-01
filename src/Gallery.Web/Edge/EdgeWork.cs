namespace Gallery.Web.Edge;

/// <summary>
/// One operation's purge and warm work (data-model "Value objects"). Every address is absolute:
/// <see cref="Pages"/> on <c>Edge:SiteBaseUrl</c>, <see cref="Files"/> and <see cref="WarmFiles"/> on
/// <c>Storage:ImageBaseUrl</c>. <see cref="WarmFiles"/> comes from <see cref="PurgeSets.WarmFiles"/>,
/// computed in the request from the AFTER state (decisions row 45).
/// </summary>
public sealed record EdgeWork(
    string Op,
    IReadOnlyList<string> Pages,
    IReadOnlyList<string> Files,
    IReadOnlyList<string> WarmFiles,
    DateTimeOffset EnqueuedAt);
