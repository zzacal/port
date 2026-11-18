# Data Model: Focused Photo Edit Screen

No stored data changes. No new collection, field, index or projection; purge sets are unchanged
because the write services are unchanged (research R-05).

## View models (render-only)

### PhotoEditPageModel (new, `src/Gallery.Web/Library/LibraryEndpoints.cs` beside the other page models)

| Field | Type | Meaning |
|---|---|---|
| Photo | LibraryTile | The photo being edited: view (derivatives, size, placeholder), caption, holders, ViewHref, GalleryCount. Built by the same tile rule as the library grid. |
| Galleries | IReadOnlyList<GalleryRef> | Every gallery in home order: the combobox options and the pending-add lookup. |
| Options | LibraryOptions | TitleMax, CaptionMax (the panel's limits). |
| Csrf | FormToken | The form token. |
| LibraryScriptUrl | string | `library.js` with its version query. |
| ReturnPage | int | The library page to go back to (≥ 1). |
| Status | string? | The `role="status"` line: "Saved" or a refusal reason. |
| Saved | bool | The panel's Saved state. |
| EditTitle, EditCaption | string? | Submitted values after a refusal, else stored. |
| EditAdd, EditCreate, EditLeave | IReadOnlyList<string>? | Pending combobox entries after a 422 (null otherwise), as `LibraryPageModel` carries today. |

### LibraryPageModel (changed)

Removes `Saved`, `EditTitle`, `EditCaption`, `EditAdd`, `EditCreate`, `EditLeave`. Keeps `Data`,
`Options`, `Csrf`, `ScriptUrl`, `Status`.

### LibraryPageData (changed)

Removes `Open`. `LibraryReader.PageAsync(int page)` loses the `open` parameter.

### LibraryReader.PhotoAsync (new)

`Task<(LibraryTile? Photo, IReadOnlyList<GalleryRef> Galleries)> PhotoAsync(string iid)`.
Reads: one image find (live only: no `deletingAt`), op `db.library.page`; then `GalleriesAsync()`
(`db.galleries.list`, `db.site.order`). When the image is unknown or tombstoned, `Photo` is null
and `GalleriesAsync` is not called (two fewer reads on a 404).

## State on the edit screen

```
GET L6 ──► Idle (stored values)
POST L2 ok ──► 303 ──► GET L6 ?saved=1 ──► Saved
POST L2 refused ──► re-render L6 with status code ──► Refused (reason, entries kept)
POST L5 ──► 303 ──► library page ReturnPage (deleted status)
```
