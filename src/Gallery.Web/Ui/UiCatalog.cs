using Gallery.Web.Ui.Pages;

namespace Gallery.Web.Ui;

public enum UiLevel { Tokens, Atoms, Molecules, Organisms, Templates }

public sealed record ComponentEntry(UiLevel Level, string Name, string Slug, Type PageType);

/// <summary>The single list that drives the /ui/ index, its routes, and its tests.</summary>
public static class UiCatalog
{
    public static readonly IReadOnlyList<ComponentEntry> Entries =
    [
        new(UiLevel.Tokens, "Tokens", "tokens", typeof(TokensPage)),
        new(UiLevel.Atoms, "Picture", "atoms/picture", typeof(PicturePage)),
        new(UiLevel.Atoms, "Title", "atoms/title", typeof(TitlePage)),
        new(UiLevel.Atoms, "TextLink", "atoms/text-link", typeof(TextLinkPage)),
        new(UiLevel.Atoms, "ExifField", "atoms/exif-field", typeof(ExifFieldPage)),
        new(UiLevel.Molecules, "Tile", "molecules/tile", typeof(TilePage)),
        new(UiLevel.Molecules, "MetadataBar", "molecules/metadata-bar", typeof(MetadataBarPage)),
        new(UiLevel.Molecules, "Pager", "molecules/pager", typeof(PagerPage)),
        new(UiLevel.Molecules, "Brand", "molecules/brand", typeof(BrandPage)),
        new(UiLevel.Molecules, "Nav", "molecules/nav", typeof(NavPage)),
        new(UiLevel.Organisms, "ContactSheet", "organisms/contact-sheet", typeof(ContactSheetPage)),
        new(UiLevel.Organisms, "SoloViewer", "organisms/solo-viewer", typeof(SoloViewerPage)),
        new(UiLevel.Organisms, "SiteHeader", "organisms/site-header", typeof(SiteHeaderPage)),
        new(UiLevel.Templates, "PublicLayout", "templates/public-layout", typeof(PublicLayoutPage)),
    ];
}
