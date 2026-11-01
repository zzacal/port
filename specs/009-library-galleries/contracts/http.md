# HTTP Contract: 009 Owner Routes

All routes sit in the existing `/admin` group (`RequireAuthorization("owner")` +
`AdminAntiforgeryFilter`) and are mapped by `admin.MapLibrary()` BEFORE the H6 catch-all, as
`MapUpload()` is. Without a session: GET → 302 `/admin/signin`, POST → 401; with a session but no
antiforgery token: POST → 400 (I.3, I.6; [SAFETY]). Every response carries the existing admin
`no-store` + `noindex` headers. Every POST reads its form only when `HasFormContentType`; a bare
POST is treated as "no fields" (never `ReadFormAsync` on a bodiless request).

Responses: a successful POST answers `303 See Other` to the page it came from, with a status query
(below). A refusal re-renders that page with the status code shown and the reason in a
`role="status"` element, and stores nothing. `{gid}`/`{iid}` must pass `OpaqueId.IsValid`, else 404.

| # | Method, path | Form fields | Success (303 to) | Refusals | Purge (contracts/edge.md) |
|---|---|---|---|---|---|
| L1 | GET `/admin/library?page=n&open={iid}` | | 200 page (C1) | `page` past end → last page; unknown `open` → panel closed | none |
| L2 | POST `/admin/library/images/{iid}` | `title`, `caption`, `page` | `/admin/library?page=n&open={iid}&saved=1` | 422 title empty/blank/too long, caption too long; 404 unknown image | P-TEXT |
| L3 | POST `/admin/library/add` | `ids` (repeated, ≤ PageSize), `gallery` = `{gid}` or `new`, `newTitle`, `page` | new gallery → `/admin/galleries/{gid}?added=N`; existing → `/admin/library?page=n&added=N&skipped=M&to={gid}` | 422 no ids, more than PageSize ids, `new` without a valid title; 404 unknown gallery; unknown image ids are skipped and counted | P-MEMBERS (added) |
| L4 | POST `/admin/library/delete` | `ids` (repeated, ≤ PageSize), `page` | `/admin/library?page=n&deleted=N&leftovers=K&failed=F` | 422 no ids or > PageSize | P-DELETE per image |
| L5 | POST `/admin/library/images/{iid}/delete` | `page` | `/admin/library?page=n&deleted=1&leftovers=K` | 404 unknown (an already-deleted id answers 303 with `deleted=0`) | P-DELETE |
| G1 | GET `/admin/galleries` | | 200 page (C2) | | none |
| G2 | POST `/admin/galleries` | `title` | `/admin/galleries/{newId}` | 422 title | none (L-10) |
| G3 | POST `/admin/galleries/order` | `order` (repeated gallery ids, full list) | `/admin/galleries?ordered=1` | 409 stale: "The gallery list changed in another tab. Reload to see the current order." | P-ORDER |
| G4 | POST `/admin/galleries/{gid}/visibility` | `public` = `on` or absent, `confirm` = `1` or absent, `from` = `list` or `builder` | the `from` page with `?visibility=public|private` | 409 empty: "Add a photo before making this gallery public" (checked FIRST, whatever else is sent; [SAFETY]); publish without `confirm=1` → 200 re-render with the publish warning (no write) | P-VIS |
| B1 | GET `/admin/galleries/{gid}` | | 200 page (C3) | 404 unknown | none |
| B2 | POST `/admin/galleries/{gid}` | `title`, `description` | `/admin/galleries/{gid}?saved=1` | 422 title / description; absent fields keep stored values | P-RENAME when the slug changes, else P-DESC (description changed) or none |
| B3 | POST `/admin/galleries/{gid}/order` | `order` (repeated image ids, full list) | `/admin/galleries/{gid}?ordered=1` | 409 stale: "This gallery changed in another tab. Reload to see the current order." | P-MEMBERS |
| B4 | POST `/admin/galleries/{gid}/cover` | `image` = `{iid}` to pin, absent to unpin | `/admin/galleries/{gid}` | 422 image not a member | P-MEMBERS (cover) |
| B5 | POST `/admin/galleries/{gid}/remove` | `image` | `/admin/galleries/{gid}?removed=1` | a non-member image → 303 with `removed=0` (idempotent) | P-MEMBERS |
| B6 | POST `/admin/galleries/{gid}/delete` | | `/admin/galleries?deleted=1` | 404 unknown (already deleted → 303 `deleted=0`) | P-GDELETE |

Rules every handler follows:
- Bulk handlers (L3, L4) call the single-item service method once per id, in library order, and
  apply its gate unchanged; no laxer bulk path ([SAFETY], decisions row 11). The `ids` limit is the
  same `Library:PageSize` the page uses to cap selection.
- Publish (G4) order: load gallery → if requested public and `items` is empty → 409 refusal →
  if not confirmed → render warning → conditional write (`items.0` exists) → matched 0 → 409
  refusal. Unpublish writes at once. Both recompute the site and evict + enqueue.
- Every write: services → `SiteProjector` → output-cache eviction of the purge set's page paths
  (in request) → `IEdgeCache.Enqueue` (returns immediately) → owner-action log for G4, L4, L5, B6.
- No route changes a public page's markup; public routes gain only the `page:{path}` output-cache
  tag (R-07).

## Removed

- `dotnet run -- seed` (Program.cs `seed` argument). Replaced by `remove-seeded`
  (contracts/removal-command.md).

## Changed existing test (stated requirement change)

`AdminAccessTests.Owner_GetAdminUpload_returns_200_and_library_still_404`: the library now exists,
so the `/admin/library` assertion flips to 200 and the "not built yet" 404 guard moves to
`/admin/not-built`; the test is renamed `Owner_GetAdminUpload_and_library_return_200`. No other
assertion in the file changes.
