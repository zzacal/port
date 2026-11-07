# HTTP contract: L2 extended

`POST /admin/library/images/{iid}`. The route, owner policy and antiforgery filter are unchanged
(FR-016).

## Request (form, all optional)

| Field | Meaning |
|---|---|
| `page` | unchanged |
| `title`, `caption` | unchanged (null keeps the stored value) |
| `add` ×n | add the photo to this existing gallery |
| `create` ×n | create a private gallery with this title, then add the photo |
| `leave` ×n | take the photo out of this gallery |

## Processing order (ImageService.SavePanelAsync)

1. Text problem (`GalleryService.TitleProblem`, caption > `CaptionMax`) → delegate to
   `UpdateTextAsync`, which refuses with 422 and the reason. Nothing is written and no gallery
   change is made.
2. Normalise the MembershipEdit (data-model). If it is empty, return `UpdateTextAsync(...)`,
   which is exactly today's behaviour.
3. Image unknown or tombstoned → NotFound (as today).
4. Any create title with a TitleProblem → 422 with that reason.
5. Any add/leave id invalid, or missing from one `db.gallery.load` find
   (`Filter.In(ids)`) → `OperationResult.Refused(404, "That gallery no longer exists.")`.
   It is Refused, not NotFound, so the L2 handler's existing `_ =>` branch re-renders with the
   panel open, and `NotFound` keeps meaning "photo missing". Nothing is written. The
   `library.image.memberships` outcome is `notfound` (a bad title in step 4 logs `refused`).
6. `UpdateTextAsync(iid, title, caption)` (own scope, P-TEXT when text changed).
7. `library.image.memberships` timer around: `scope = galleries.Begin("image.memberships")`;
   for each leave → `RemoveOneAsync(gid, iid, scope)`; for each add → `AddOneAsync(gid, iid,
   scope)`; for each create → `CreateAsync(title)`, then `AddOneAsync(newId, iid, scope)`;
   then `scope.CompleteAsync()`. The order is leave, add, create, so the panel shows the result
   in title order, whatever the order of the writes.

## Responses

| Case | Response |
|---|---|
| Ok | 303 `/admin/library?page={page}&open={iid}&saved=1` (unchanged) |
| Text refused / create title refused | 422, library re-rendered, panel open, reason in `role="status"`, typed title/caption AND pending tokens kept (`EditAdd/EditCreate/EditLeave`) |
| Gallery missing | 404, library re-rendered with the panel open on current memberships, status "That gallery no longer exists." |
| Image missing | 404 "That photo no longer exists." (unchanged) |

## Log events

- `http.request POST /admin/library/images/{iid} {status} {ms}` (existing)
- `op.timing library.image.memberships {ms} ok|refused|notfound` (new; only when the edit is
  non-empty)
- `op.timing db.gallery.create|db.gallery.update|db.image.update|db.gallery.load …` (existing,
  per write)
