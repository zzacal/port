namespace Gallery.Web.Workspace;

/// <summary>
/// 020 (contracts/http.md "Messages", contracts/components.md K12, tasks.md T012): every
/// owner-facing sentence the workspace shows, written by the server only. The page script never
/// composes one; it shows the <c>message</c> of a JSON answer or a sentence rendered from here.
/// <c>{n}</c> counts are pluralised photo/photos; <c>{g}</c> is the gallery title.
/// </summary>
public static class Messages
{
    // ── A-route results ──────────────────────────────────────────────────────

    /// <summary>A1, A5.</summary>
    public const string Saved = "Saved";

    /// <summary>A4.</summary>
    public const string GalleryOrderSaved = "Gallery order saved.";

    /// <summary>A9.</summary>
    public const string OrderSaved = "Order saved.";

    /// <summary>A11 on a gallery that is already gone (200).</summary>
    public const string GalleryAlreadyDeleted = "That gallery was already deleted.";

    /// <summary>A1 on an unknown or tombstoned photo (404).</summary>
    public const string PhotoGone = "That photo no longer exists.";

    /// <summary>"1 photo", "{n} photos".</summary>
    public static string Photos(int n) => n == 1 ? "1 photo" : $"{n} photos";

    /// <summary>
    /// A7: <c>{n} added to {g}.</c> (+ <c> {k} were already in it.</c> when skipped &gt; 0), then for
    /// a public gallery <c> Visitors can see them now.</c> (<c>it</c> for one); nothing new →
    /// <c>Already in {g}.</c>
    /// </summary>
    public static string Added(int added, int skipped, string gallery, bool isPublic)
    {
        if (added == 0) return $"Already in {gallery}.";
        var text = $"{Photos(added)} added to {gallery}.";
        if (skipped > 0) text += $" {skipped} were already in it.";
        if (isPublic) text += added == 1 ? " Visitors can see it now." : " Visitors can see them now.";
        return text;
    }

    /// <summary>A3: <c>{g} created. It is private.</c> / <c>{g} created with {n}. It is private.</c></summary>
    public static string Created(string gallery, int? added) =>
        added is { } n ? $"{gallery} created with {Photos(n)}. It is private." : $"{gallery} created. It is private.";

    /// <summary>
    /// A8: <c>{n} removed from {g}.</c> then, when a public gallery became empty,
    /// <c> Visitors can no longer see {g} until it has a photo.</c>
    /// </summary>
    public static string Removed(int removed, string gallery, bool hidden) =>
        $"{Photos(removed)} removed from {gallery}." +
        (hidden ? $" Visitors can no longer see {gallery} until it has a photo." : "");

    /// <summary>A6 on.</summary>
    public static string NowPublic(string gallery) => $"{gallery} is now public. Visitors can see it.";

    /// <summary>A6 off.</summary>
    public static string NowPrivate(string gallery) => $"{gallery} is now private.";

    /// <summary>A10 with a photo.</summary>
    public static string Cover(string photoTitle, string gallery) => $"{photoTitle} is now the cover of {gallery}.";

    /// <summary>A10 with <c>null</c>.</summary>
    public static string FirstPhotoCover(string gallery) => $"{gallery} uses its first photo as the cover.";

    /// <summary>
    /// A2 (tasks.md C-07): <c>{n} deleted.</c>, then when leftovers &gt; 0
    /// <c> {k} files could not be removed and were logged.</c>, then when failed &gt; 0
    /// <c> {f} could not be deleted. Try again.</c>
    /// </summary>
    public static string Deleted(int deleted, int leftovers, int failed)
    {
        var text = $"{Photos(deleted)} deleted.";
        if (leftovers > 0) text += $" {leftovers} files could not be removed and were logged.";
        if (failed > 0) text += $" {failed} could not be deleted. Try again.";
        return text;
    }

    /// <summary>A11.</summary>
    public static string GalleryDeleted(string gallery) => $"{gallery} deleted. Its photos are still in All photos.";

    // ── upload (U3, U13) ─────────────────────────────────────────────────────

    /// <summary>U3 409 <c>destination-public</c> ([SAFETY] II.5a).</summary>
    public static string DestinationPublic(string gallery) => $"{gallery} is public. Visitors will see these photos.";

    /// <summary>U13 409 <c>still-working</c>.</summary>
    public const string StillWorking = "Wait for the photos that are still working, or remove them.";

    /// <summary>U13 200 when <paramref name="dropped"/> &gt; 0 (omitted when 0).</summary>
    public static string NotAdded(int dropped) => $"{dropped} were not added.";

    // ── selection and empty collections (K12) ────────────────────────────────

    /// <summary>The selection cap (FR-008).</summary>
    public static string SelectionCap(int cap) => $"You can select up to {cap} photos at a time.";

    /// <summary>K12: All photos with no photos.</summary>
    public const string LibraryEmpty = "No photos yet. Use Add photos, or drop files anywhere on this page.";

    /// <summary>K12: Not in a gallery with no photos.</summary>
    public const string UnplacedEmpty = "Every photo is in a gallery.";

    /// <summary>K12: a gallery with no photos.</summary>
    public const string GalleryEmpty =
        "This gallery has no photos yet. Use Add photos to upload straight into it, or open All photos and drag photos onto its name.";

    // ── delete confirms ──────────────────────────────────────────────────────

    /// <summary>The selection confirm's consequence (contracts K12 "Confirm wording": the pre-020 bulk text).</summary>
    public const string BulkDeleteConsequence = "This removes them from every gallery and deletes the original files. It can't be undone.";

    /// <summary>contracts/components.md "delete confirm (one)" (the pre-020 photo edit page's text).</summary>
    public static string DeleteConsequence(int galleries) =>
        (galleries switch
        {
            0 => "It is in no gallery.",
            1 => "It is in 1 gallery.",
            _ => $"It is in {galleries} galleries.",
        }) + " This removes it from every gallery and deletes the original file. It can't be undone.";
}
