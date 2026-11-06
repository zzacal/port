# Contract: public routes (013)

All routes accept only GET and HEAD and are mapped in `Site/PublicEndpoints.cs` beside the `/g`
routes. Each new route uses the same pieces as the page it mirrors:
- `StatusAndHeadersResult` (Cache-Control `PublicCache.HtmlPolicy`, `page:{path}` tag, output cache
  policy when `Site:OutputCache`);
- `NotFoundResult()` for every refusal (404, `ShortPolicy`, `X-Robots-Tag: noindex`, the
  `NotFoundPage`), so an unknown address and a private-only camera are byte-identical
  ([SAFETY] FR-009);
- `IsDataException` → 500 "Something went wrong";
- `.CacheOutput(PublicCache.PolicyName)` when `siteOptions.OutputCache` is on.

| Id | Route | Mirrors | Reads (exact) | 200 body | Link preload |
|---|---|---|---|---|---|
| C1 | `/c` | `/g` | 1 `db.cameras.list` | `CameraListingPage` with `ContactSheet` 3/3/1 of camera cards | first card, `Sizes.ForGrid(3,3,1)` |
| C2 | `/c/{slug}` | `/g/{id}/{slug}` | 1 `db.camera.read` + 1 `db.images.read` (grid projection) | `CameraPage` with `ContactSheet` 4/3/1, tiles titled with photo titles, href `/c/{slug}/{iid}`, `TransitionId = iid`, `PreloadSrcSet` AVIF | first tile, `Sizes.ForGrid(4,3,1)` |
| C3 | `/c/{slug}/{iid}` | photo page | 1 `db.camera.read` + 1 `db.images.read` (full, ≤ 3 ids) | `PhotoPage` (reused) with camera crumbs, `PhotoCount` N/total over the camera, prev/next within the camera, `PrefetchUrls` for the neighbours | the photo, `100vw` |

Routing order: `/c/` (with a trailing slash) is handled inside the C1 handler by
`Request.Path == "/c/"` → 301 `/c` with `ShortPolicy`. This is the 011 R-03 pattern; a separate
`/c/` route makes routing ambiguous.

Refusals (404 via `NotFoundResult`):
- C2/C3: the slug fails `^[a-z0-9-]{1,64}$`, which stops a lookup for junk. The slug is also
  bounded by `Slug.From`'s 60 characters plus a suffix.
- C2/C3: no `cameras` document has that slug.
- C3: `!OpaqueId.IsValid(iid)`, checked before the read, or `iid` not in `camera.items`, or the
  image read misses it (deleted between projection and request).

There is no redirect for a stale slug (research R-10).

Headers:
- None of the three ever sends `X-Robots-Tag` or `<meta robots noindex>`. Everything they show is
  visible by construction, so they are indexable like `/g`.
- `Server-Timing` comes through the existing request-timing middleware (IV.3).

## Reader additions (`Data/ContentReader.cs`)

| Method | Timer (`isDbRead: true`) | Query |
|---|---|---|
| `CamerasAsync()` | `db.cameras.list` | `cameras.find({})`, projection `card`, sort `rank` asc |
| `CameraAsync(slug)` | `db.camera.read` | `cameras.find({slug})`, projection `slug`, `name`, `items` |

`ImagesAsync` is reused unchanged. On the camera routes the handler drops any returned image with
`DeletingAt` set before rendering (render-time guard, research R-09). The grid projection omits
`deletingAt`, so for C2 the handler cannot see tombstones. This is accepted: the projection runs
inside the delete before the document is removed (`RefreshAsync`), and that purges the page.
Decisions row.

## Log events

No new event names on the request path except the two read timers above. `http.request` carries
route, status, duration and `db_reads` as today.

## Breadcrumbs (`Site/Trails.cs`)

- `Trails.CamerasLabel = "By Camera"`, `Trails.CamerasHref = "/c"`.
- `Trails.Cameras = [Home → "/", By Camera (current)]`.
- `Trails.UnderCameras(params Crumb[] tail) = [Home → "/", By Camera → "/c", ..tail]`.

The camera page trail is `UnderCameras(new Crumb(name))`. The camera photo page trail is
`UnderCameras(new Crumb(name, $"/c/{slug}#p-{iid}"), new Crumb(title))`.

`TrailSourceGuardTests` bans the literals `"Home"` and `"Galleries"` outside `Trails.cs`, and
counts exactly 2 inside it. Its regex is `"(Home|Galleries)"`, so `"By Camera"` is not in its ban
list, and the inTrails count stays 2. It is extended (stated change, test-impact) to ban
`"By Camera"` too and count 3. Dry-run against these snippets: every `"By Camera"` literal is in
`Trails.cs` (`CamerasLabel`), and `HomePage.razor` uses `Trails.CamerasLabel`.
