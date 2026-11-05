# Decisions: 013 By Camera

| # | Kind | Decision | Rationale | Pivot cost |
|---|---|---|---|---|
| 1 | OWNER | The camera photo view steps within the camera (clarify 1A). | Owner choice. | Medium: the route, the optional crumbs and the photo part of P-CAM. |
| 2 | DEFAULT | A derived `cameras` collection, written only by `SiteProjector` (research R-01). | It reaches every owner write path without new call sites; reads match the mirror pages (1/2/2). | Medium: the store, reader and purge input would all move. |
| 3 | DEFAULT | Visible = in at least one listed gallery, not deleting, has a make or model. | II.2 and II.5. The library and uploads are never public. | High if loosened: it would need a constitution amendment. |
| 4 | DEFAULT | Group by normalised make and model; display with `ExifFormat.Camera` from the newest photo. | Matches what the photo page already shows (FR-005). | Low: one pure function. |
| 5 | DEFAULT | Readable slug address `/c/{slug}`, stable per key, with `-2` suffixes on collision. | Camera names are public facts. The 404 is the same for unknown and private-only. | Low before launch; after, links to old slugs would 404. |
| 6 | DEFAULT | No stale-slug redirect (spec edge case narrowed). | A slug changes only if the key does; case and space variants never move it. V.4. | Low: add a lookup later if needed. |
| 7 | DEFAULT | Order: count desc, then name; photos newest upload first; cover = first. | Spec FR-006 defaults; deterministic, so no-op projections write nothing. | Low. |
| 8 | DEFAULT | Spec FR-011 drops "upload confirm into a public gallery". | Confirm writes `galleryIds: []` (`ConfirmService.cs:276`), so no upload is ever in a gallery at confirm. | None. |
| 9 | DEFAULT | Camera grid tiles do not filter tombstones at render (grid projection lacks `deletingAt`). The camera photo view does filter them. | Delete re-projects before removing the document and purges the page (`RefreshAsync`). A visitor can see a deleting photo's tile only for the length of that one request. | Low: add `deletingAt` to the projection. |
| 10 | DEFAULT | Add an `InvariantChecker` rule: stored cameras == pure projection. | One function makes every `SettleAsync` test (150+) a camera-projection check at no extra test code. | Low. |
| 11 | PLAN | Four suspected public-page scan loops (test-impact B) went unread: the probe's approval timed out, which counts as a no. They stay unmodified, and their assertions are repeated for `/c` in new tests. | Recorded fallback; the tasks phase reads them first. | Low. |
| 12 | DEFAULT | The document type is `CameraGroupDoc`, because `CameraDoc` is already the EXIF sub-document. | Avoids renaming an existing type (existing tests unmodified). | None. |
