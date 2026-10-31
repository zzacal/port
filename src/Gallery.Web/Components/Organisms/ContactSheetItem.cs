namespace Gallery.Web.Components.Organisms;

public sealed record ContactSheetItem(
    Gallery.Core.Images.ImageView? Image,
    string Href,
    string? Title = null,
    string? TransitionId = null,
    string? PreloadSrcSet = null,
    string? PreloadSizes = null);
