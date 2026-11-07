using Gallery.Web.Library;

namespace Gallery.Web.Components.Molecules;

public partial class GalleryActions
{
    /// <summary>The visibility control's accessible name and hover text: the data-model.md table, verbatim (016 G2a).</summary>
    public static string VisibilityLabel(bool isPublic, bool hasPhotos) => (isPublic, hasPhotos) switch
    {
        (true, true) => "Public. Make private",
        (true, false) => "Public, hidden until it has a photo. Make private",
        (false, true) => "Private. Make public",
        (false, false) => $"Private. {GalleryService.EmptyPublishReason}",
    };

    /// <summary>The visibility icon: eye when public, eye-off when private (016 G2a).</summary>
    public static string VisibilityIcon(bool isPublic) => isPublic ? "eye" : "eye-off";

    /// <summary>The visibility control is a submit button except for a private, empty gallery (016 G2a).</summary>
    public static bool CanToggle(bool isPublic, bool hasPhotos) => isPublic || hasPhotos;
}
