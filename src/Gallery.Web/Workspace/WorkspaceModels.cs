using Gallery.Core.Images;

namespace Gallery.Web.Workspace;

/// <summary>The two library counts (research R-02): every live photo, and live photos in no gallery.</summary>
public sealed record WorkspaceCounts(long Total, long Unplaced);

/// <summary>
/// One photo cell's data (data-model "Read models", "Tile data"): the image view (id and title
/// included), the caption and the ids of the galleries holding it, as stored.
/// </summary>
public sealed record WorkspaceTile(ImageView Image, string? Caption, IReadOnlyList<string> GalleryIds)
{
    public string Id => Image.Id;
    public string Title => Image.Title;
}

/// <summary>
/// One chunk of a library collection (research R-03): up to <c>Library:PageSize</c> tiles, newest
/// first, and the cursor of the last tile when more exist (null at the end).
/// </summary>
public sealed record WorkspaceChunk(IReadOnlyList<WorkspaceTile> Tiles, string? Next);

/// <summary>
/// The body a workspace response renders (contracts/http.md "Pages and fragments"): the full document
/// (no <c>X-Workspace</c> header) or one fragment (<c>main</c>, <c>chrome</c>, <c>more</c>).
/// </summary>
public enum WorkspacePart { Full, Main, Chrome, More }

/// <summary>
/// What <c>WorkspacePage</c> renders (tasks T032/T033). Each part reads only what it shows (research R-02):
/// <see cref="Counts"/> for Full, Chrome and a library Main; <see cref="Galleries"/> for Full and Chrome;
/// <see cref="Gallery"/> for a gallery's Full and Main; <see cref="Tiles"/> for Full, Main and More.
/// <see cref="Current"/> is <c>all</c>, <c>unplaced</c> or the gallery id (<c>CollectionList.Current</c>).
/// </summary>
public sealed record WorkspacePageModel(
    WorkspacePart Part,
    string Current,
    WorkspaceCounts? Counts,
    IReadOnlyList<Gallery.Web.Library.GalleryRef>? Galleries,
    Gallery.Web.Data.GalleryDoc? Gallery,
    IReadOnlyList<WorkspaceTile> Tiles,
    string? Next,
    Gallery.Web.Components.FormToken? Csrf,
    Gallery.Web.Library.LibraryOptions Options,
    string ScriptUrl,
    string UploadSrc,
    string WorkerSrc);
