# Contract: components and pages (013)

No new atom, molecule or organism. Two new pages plus three optional, default-unchanged
parameters. IX.2: every page renders inside `PublicLayout`, and no page adds margins to the chrome
or the frame.

| Id | File | Change | Default output |
|---|---|---|---|
| K1 | `Site/Pages/CameraListingPage.razor` (new) | Copy of `ListingPage.razor` with `Title="@Trails.CamerasLabel"`, `Crumbs="Trails.Cameras"`, and `ContactSheet Items="Cards" Large="3" Medium="3" Small="1"` when Cards is non-empty. | n/a |
| K2 | `Site/Pages/CameraPage.razor` (new) | Copy of `GalleryPage.razor` without the description, without `GalleryNeighbours` and without `NoIndex`. Parameters `Title`, `Items`, `Crumbs`. `ContactSheet` 4/3/1 and the same `_transitions` style block. | n/a |
| K3 | `Site/Pages/PhotoPage.razor` | Adds `[Parameter] public IReadOnlyList<Crumb>? Crumbs`. When it is set, it is used as the trail; otherwise the current `GalleryHref` rule applies. | Unchanged when Crumbs is null (every existing caller). |
| K4 | `Site/Pages/HomePage.razor` | `Links` becomes `[new(Trails.ListingLabel, Trails.ListingHref), new(Trails.CamerasLabel, Trails.CamerasHref)]`. | Changes (stated, test-impact). |
| K5 | `Components/Templates/PublicLayout.razor` | The speculation rule string becomes the research R-07 string. | Changes on every `Enhance` page (stated). |

Camera listing tiles:
- `ViewMapper.ToCameraCard(CameraCardDoc, url)` follows the `ToCard` shape: href `/c/{slug}`,
  title = name, image from `cover` with `Title = name`.
- A camera document always has a cover, so there is no empty tile.

Camera page tiles: built exactly like the gallery page's `ContactSheetItem`
(`PublicEndpoints.cs:103-114`), with href `/c/{slug}/{iid}`.

The `/ui` catalogue gets no new entries: no new component. K1 and K2 are pages, and pages are not
catalogued (011 precedent).
