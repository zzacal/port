using Gallery.Web.Ui.Pages;

namespace Gallery.Web.Ui;

public enum UiLevel { Tokens, Atoms, Molecules, Organisms, Templates }

public sealed record ComponentEntry(UiLevel Level, string Name, string Slug, Type PageType);

/// <summary>
/// The single list that drives the /ui/ index, its routes, and its tests. Components are listed
/// by atomic level only; where a component is used (public site or admin) is not a /ui category.
/// </summary>
public static class UiCatalog
{
    public static readonly IReadOnlyList<ComponentEntry> Entries =
    [
        new(UiLevel.Tokens, "Tokens", "tokens", typeof(TokensPage)),

        new(UiLevel.Atoms, "Picture", "atoms/picture", typeof(PicturePage)),
        new(UiLevel.Atoms, "Title", "atoms/title", typeof(TitlePage)),
        new(UiLevel.Atoms, "TextLink", "atoms/text-link", typeof(TextLinkPage)),
        new(UiLevel.Atoms, "ExifField", "atoms/exif-field", typeof(ExifFieldPage)),
        new(UiLevel.Atoms, "Button", "atoms/button", typeof(ButtonPage)),
        new(UiLevel.Atoms, "Input", "atoms/input", typeof(InputPage)),
        new(UiLevel.Atoms, "Switch", "atoms/switch", typeof(SwitchPage)),
        new(UiLevel.Atoms, "SelectMark", "atoms/select-mark", typeof(SelectMarkPage)),

        new(UiLevel.Molecules, "Tile", "molecules/tile", typeof(TilePage)),
        new(UiLevel.Molecules, "MetadataBar", "molecules/metadata-bar", typeof(MetadataBarPage)),
        new(UiLevel.Molecules, "Pager", "molecules/pager", typeof(PagerPage)),
        new(UiLevel.Molecules, "Brand", "molecules/brand", typeof(BrandPage)),
        new(UiLevel.Molecules, "Nav", "molecules/nav", typeof(NavPage)),
        new(UiLevel.Molecules, "BatchItem", "molecules/batch-item", typeof(BatchItemPage)),
        new(UiLevel.Molecules, "CoverMarker", "molecules/cover-marker", typeof(CoverMarkerPage)),
        new(UiLevel.Molecules, "DragHandle", "molecules/drag-handle", typeof(DragHandlePage)),
        new(UiLevel.Molecules, "SelectionBar", "molecules/selection-bar", typeof(SelectionBarPage)),
        new(UiLevel.Molecules, "DangerConfirm", "molecules/danger-confirm", typeof(DangerConfirmPage)),
        new(UiLevel.Molecules, "VisibilityToggle", "molecules/visibility-toggle", typeof(VisibilityTogglePage)),

        new(UiLevel.Organisms, "ContactSheet", "organisms/contact-sheet", typeof(ContactSheetPage)),
        new(UiLevel.Organisms, "SoloViewer", "organisms/solo-viewer", typeof(SoloViewerPage)),
        new(UiLevel.Organisms, "SiteHeader", "organisms/site-header", typeof(SiteHeaderPage)),
        new(UiLevel.Organisms, "OwnerBar", "organisms/owner-bar", typeof(OwnerBarPage)),
        new(UiLevel.Organisms, "SidePanel", "organisms/side-panel", typeof(SidePanelPage)),
        new(UiLevel.Organisms, "UploadDropzone", "organisms/upload-dropzone", typeof(UploadDropzonePage)),
        new(UiLevel.Organisms, "BatchFooter", "organisms/batch-footer", typeof(BatchFooterPage)),
        new(UiLevel.Organisms, "GalleryBuilder", "organisms/gallery-builder", typeof(GalleryBuilderPage)),
        new(UiLevel.Organisms, "SignInCard", "organisms/sign-in-card", typeof(SignInCardPage)),

        new(UiLevel.Templates, "PublicLayout", "templates/public-layout", typeof(PublicLayoutPage)),
        new(UiLevel.Templates, "AdminLayout", "templates/admin-layout", typeof(AdminLayoutPage)),
    ];
}
