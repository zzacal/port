# Data Model: 016 Icon Controls on the Galleries List

No stored data changes. The galleries table, the publish rule (G4) and the order data are unchanged.
The entities below are view models.

## GalleryRow (existing view model, `Library/LibraryEndpoints.cs`)

- Kept: `Id`, `Title`, `IsPublic`, `ImageCount`, `Cover`, `ViewHref`.
- Removed: `Status`. Its only consumer was the `.gal-status` span; its text is replaced by the
  visibility label rule below.

## GalleryActions (new molecule; pure rules in `GalleryActions.cs`)

Inputs (all supplied by the page; the molecule builds no URL):

| Parameter | Type | Meaning |
|---|---|---|
| `Title` | string | gallery title, used in the labels ("Move Coast", "Edit Coast") |
| `IsPublic` | bool | stored state |
| `HasPhotos` | bool | `ImageCount > 0` |
| `VisibilityForm` | string | form id `gal-vis-{id}` |
| `ViewHref` | string? | public page URL, or null when none |
| `EditHref` | string | `/admin/galleries/{id}` |
| `DragLabel` | string | move control name (as DragHandle today) |

The visibility state determines the icon, the availability and the name:

| IsPublic | HasPhotos | icon | control | name (hover text and accessible name) | posts |
|---|---|---|---|---|---|
| true | true | `eye` | submit button | "Public. Make private" | no `public` field, so G4 unpublishes |
| true | false | `eye` | submit button | "Public, hidden until it has a photo. Make private" | no `public` field, so G4 unpublishes |
| false | true | `eye-off` | submit button | "Private. Make public" | `public=on`, so G4 publishes |
| false | false | `eye-off` | unavailable span | "Private. Add a photo before making this gallery public" | nothing |

View: a link to `ViewHref`, with a new-tab marker, named "View {Title} (opens in a new tab)".
When `ViewHref` is null it is an unavailable span named "No public page yet".

Edit: a link to `EditHref`, named "Edit {Title}". It is always present and always last.

## Grid columns (CSS, R-07)

| Grid | phone (<600) | medium (600–1199) | large (≥1200) | large, grid narrower than a 4-column row needs |
|---|---|---|---|---|
| library | 1 | 2 (was 3) | 4 | 2 (grid < 800 px) |
| builder | 1 | 2 (was 3) | 4 | 2 (grid < 992 px) |
| galleries list | 1 | 2 (was 3) | 3 | (never: narrowest tile is 304 px) |

The "grid narrower" thresholds follow from the row's minimum width: 4 tiles of one row each, plus
3 gaps and 2 paddings of 48 px.

- Library row: 3 × 44 + 2 × 4 = 140 px.
- Builder row: 4 × 44 + 3 × 4 = 188 px.
