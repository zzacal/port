using Gallery.Web.Library;

namespace Gallery.Web.Components.Molecules;

/// <summary>One row of a <see cref="GalleryChecklist"/>: a gallery and whether the photos in context are in it.</summary>
public sealed record ChecklistGallery(string Id, string Title, bool Public, ChecklistTick Tick = ChecklistTick.None);

/// <summary>Whether none, all or some of the photos in context are in the gallery (Some renders partly ticked).</summary>
public enum ChecklistTick { None, All, Some }

/// <summary>
/// The result of <see cref="GalleryChecklist.Match(string, IReadOnlyList{string}, int)"/>: <see cref="Shown"/> holds the
/// indexes into the titles of the rows left visible, in display order; <see cref="Existing"/> is true when the text is
/// a gallery's title; <see cref="Offer"/> is the text a new gallery would get (null when none is offered);
/// <see cref="Message"/> is the title rule's refusal for non-blank text.
/// </summary>
public sealed record ChecklistMatch(IReadOnlyList<int> Shown, bool Existing, string? Offer, string? Message);

public partial class GalleryChecklist
{
    /// <summary>THE checklist rules (contracts K6) with the default title limit.</summary>
    public static ChecklistMatch Match(string query, IReadOnlyList<string> titles)
        => Match(query, titles, new LibraryOptions().TitleMax);

    /// <summary>
    /// THE checklist rules (contracts K6). Pure, no I/O; <c>workspace.js</c> mirrors them.
    /// Trim; blank text narrows nothing (every row, in the given order) and offers nothing; otherwise the rows whose
    /// title contains the text, ignoring case, ordered by rank (exact, then prefix, then
    /// contains; within a rank by title <c>OrdinalIgnoreCase</c>; then the given order). A title equal to the text
    /// ignoring case is "existing" and hides the offer. The offer and its refusal come from
    /// <see cref="GalleryService.TitleProblem"/>, never a second copy of the rule.
    /// </summary>
    public static ChecklistMatch Match(string query, IReadOnlyList<string> titles, int titleMax)
    {
        var q = (query ?? "").Trim();
        var problem = GalleryService.TitleProblem(q, new LibraryOptions { TitleMax = titleMax });
        if (q.Length == 0) return new ChecklistMatch(Enumerable.Range(0, titles.Count).ToList(), false, null, null);

        var shown = titles
            .Select((t, i) => (Title: t, Index: i))
            .Where(r => r.Title.Contains(q, StringComparison.OrdinalIgnoreCase))
            .OrderBy(r => Rank(r.Title, q))
            .ThenBy(r => r.Title, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.Index)
            .Select(r => r.Index)
            .ToList();

        var existing = titles.Any(t => string.Equals(t, q, StringComparison.OrdinalIgnoreCase));
        var offer = !existing && problem is null ? q : null;
        return new ChecklistMatch(shown, existing, offer, problem);
    }

    private static int Rank(string title, string q)
        => string.Equals(title, q, StringComparison.OrdinalIgnoreCase) ? 0
         : title.StartsWith(q, StringComparison.OrdinalIgnoreCase) ? 1
         : 2;
}
