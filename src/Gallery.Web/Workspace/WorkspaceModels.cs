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
