namespace Gallery.Web.Components.Molecules;

/// <summary>One breadcrumb item (010 owner review 1, B1). <paramref name="Href"/> null = the current page.</summary>
public sealed record Crumb(string Label, string? Href = null);
