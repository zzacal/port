using Gallery.Web.Ui.Pages;
using Gallery.Web.Ui.Pages.Admin;

namespace Gallery.Web.Ui;

public enum UiLevel { Tokens, Atoms, Molecules, Organisms, Templates, AdminAtoms, AdminMolecules, AdminOrganisms, AdminTemplates }

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

        // Admin (feature 005, D-23). Order follows contracts/admin-components.md.
        new(UiLevel.AdminAtoms, "Button", "admin/atoms/button", typeof(ButtonPage)),
        new(UiLevel.AdminAtoms, "Input", "admin/atoms/input", typeof(InputPage)),
        new(UiLevel.AdminAtoms, "Switch", "admin/atoms/switch", typeof(SwitchPage)),
        new(UiLevel.AdminAtoms, "SelectMark", "admin/atoms/select-mark", typeof(SelectMarkPage)),
        new(UiLevel.AdminMolecules, "BatchItem", "admin/molecules/batch-item", typeof(BatchItemPage)),
        new(UiLevel.AdminMolecules, "CoverMarker", "admin/molecules/cover-marker", typeof(CoverMarkerPage)),
        new(UiLevel.AdminMolecules, "DragHandle", "admin/molecules/drag-handle", typeof(DragHandlePage)),
        new(UiLevel.AdminMolecules, "SelectionBar", "admin/molecules/selection-bar", typeof(SelectionBarPage)),
        new(UiLevel.AdminMolecules, "DangerConfirm", "admin/molecules/danger-confirm", typeof(DangerConfirmPage)),
        new(UiLevel.AdminMolecules, "VisibilityToggle", "admin/molecules/visibility-toggle", typeof(VisibilityTogglePage)),
        new(UiLevel.AdminOrganisms, "OwnerBar", "admin/organisms/owner-bar", typeof(OwnerBarPage)),
        new(UiLevel.AdminOrganisms, "SidePanel", "admin/organisms/side-panel", typeof(SidePanelPage)),
        new(UiLevel.AdminOrganisms, "UploadDropzone", "admin/organisms/upload-dropzone", typeof(UploadDropzonePage)),
        new(UiLevel.AdminOrganisms, "BatchFooter", "admin/organisms/batch-footer", typeof(BatchFooterPage)),
        new(UiLevel.AdminOrganisms, "GalleryBuilder", "admin/organisms/gallery-builder", typeof(GalleryBuilderPage)),
        new(UiLevel.AdminOrganisms, "SignInCard", "admin/organisms/sign-in-card", typeof(SignInCardPage)),
        new(UiLevel.AdminOrganisms, "ContactSheet (admin)", "admin/organisms/contact-sheet", typeof(AdminContactSheetPage)),
        new(UiLevel.AdminTemplates, "AdminLayout", "admin/templates/admin-layout", typeof(AdminLayoutPage)),
    ];

    public static string DisplayName(UiLevel level) => level switch
    {
        UiLevel.AdminAtoms => "Admin atoms",
        UiLevel.AdminMolecules => "Admin molecules",
        UiLevel.AdminOrganisms => "Admin organisms",
        UiLevel.AdminTemplates => "Admin templates",
        _ => level.ToString(),
    };
}
