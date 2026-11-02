# Contract: Routes (011)

All of these routes answer GET and HEAD only, as before.

| Route | Template logged | Page | Reads | Cache-Control | Output cache | Link header |
|---|---|---|---|---|---|---|
| `/` | `/` | `HomePage` | 0 (no `db.*` op.timing) | `HtmlPolicy` | `public-page`, tag `page:/` | none |
| `/g` | `/g` | `ListingPage` | exactly 1 (`db.site.read`) | `HtmlPolicy` | `public-page`, tag `page:/g` | first cover preload, as before |
| `/g/` | n/a | 301 → `/g` | 0 | `ShortPolicy` | not cached | none |
| `/g/{id}` | unchanged | 301 → `/g/{id}/{slug}` | unchanged | unchanged | unchanged | unchanged |
| `/g/{id}/{slug}` | unchanged | `GalleryPage` | ≤ 2 | unchanged | unchanged | unchanged |
| `/g/{id}/{slug}/{imageId}` | unchanged | `PhotoPage` | ≤ 2 | unchanged | unchanged | unchanged |

## `/` (new handler)

- It takes no `ContentReader` and no `StorageOptions`. It injects nothing that touches the
  datastore. A source guard asserts that the lambda's parameter list holds no `ContentReader`.
- It renders `RazorComponentResult<HomePage>` with no parameters, then sets `Cache-Control` to
  `HtmlPolicy` and tags it for the output cache through the same `StatusAndHeadersResult` the
  other pages use.
- It never answers 500 for a data error, because it has no data to fail on.

## `/g` (moved handler)

- The body of today's `/` handler is moved verbatim, with two changes: the component type is
  `ListingPage` instead of `HomePage`, and the local is renamed `home` → `listing`.
- `ApplyHomeHeaders` is renamed `ApplyListingHeaders`; its body is unchanged.
- `.CacheOutput(PublicCache.PolicyName)` attaches to `/g` and `/`.

## `/g/`

- 301 to `/g` with `ShortPolicy`, using the existing `ShortPolicyRedirectResult`. If the router
  doesn't deliver `/g/` to this endpoint, the executor escalates (research R-03).

## Not added

- No redirect from `/` (FR-010) and no alias for the old listing.
- No new admin route.
