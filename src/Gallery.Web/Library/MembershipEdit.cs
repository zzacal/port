namespace Gallery.Web.Library;

/// <summary>
/// One L2 request's gallery-membership change (014 data-model "MembershipEdit"): galleries to add
/// the photo to (<c>add</c>), titles of private galleries to create holding it (<c>create</c>) and
/// galleries to take it out of (<c>leave</c>). Transient; built by <see cref="Normalise"/>.
/// <see cref="IsEmpty"/> means no membership work: the request behaves exactly like today's L2.
/// </summary>
public sealed record MembershipEdit(IReadOnlyList<string> Add, IReadOnlyList<string> Create, IReadOnlyList<string> Leave)
{
    public bool IsEmpty => Add.Count == 0 && Create.Count == 0 && Leave.Count == 0;

    /// <summary>
    /// Research R-04: ids deduped (ordinal, first kept); create titles trimmed, blank ones dropped,
    /// deduped case-insensitively (first kept); an id in both Add and Leave is dropped from both (no
    /// change). Ids are NOT validated here: an invalid one stays, so the save answers 404.
    /// </summary>
    public static MembershipEdit Normalise(IEnumerable<string>? add, IEnumerable<string>? create, IEnumerable<string>? leave)
    {
        var a = (add ?? []).Distinct(StringComparer.Ordinal).ToList();
        var l = (leave ?? []).Distinct(StringComparer.Ordinal).ToList();
        var both = a.Intersect(l, StringComparer.Ordinal).ToHashSet(StringComparer.Ordinal);
        a.RemoveAll(both.Contains);
        l.RemoveAll(both.Contains);

        var c = (create ?? [])
            .Select(t => t?.Trim() ?? string.Empty)
            .Where(t => t.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new MembershipEdit(a, c, l);
    }
}
