# Contract: Edge purge and warm (011)

## E1 `ListingPath` replaces `HomePath`

- In `src/Gallery.Web/Edge/PurgeSets.cs`, `public const string HomePath = "/";` becomes
  `public const string ListingPath = "/g";`.
- Every use is renamed, with no change to its condition:
  - `Home(before, after)` → `Listing(before, after)`
  - `Visibility`: `.Append(ListingPath)`
  - `Rename`
  - `Order`
  - `GalleryDelete`
  - `Text` (cover of a listed gallery)
  - `Delete` (through `MembersPaths`)
- `WarmFiles`: `if (path == ListingPath)` warms the first 3 `homeCards` covers, as before.
- The doc comments that say `/` for the listing are updated to say `/g`.

## E2 The home page is never purged

- No purge set contains `/`.
- Guard: `PurgeSetTests` gains one theory that runs every `PurgeSets` method over the existing
  fixtures and asserts that `"/"` is absent from `PagePaths`.

## E3 Rule table (unchanged except the path)

| Set | Listing page included when |
|---|---|
| P-MEMBERS | the gallery is listed before or after |
| P-VIS | always |
| P-RENAME | listed before or after |
| P-ORDER | the relative order of listed galleries changed |
| P-GDELETE | listed before |
| P-TEXT | the image is the cover of a listed gallery |
| P-DELETE | any affected gallery listed before or after |
| P-NAV, P-DESC | never (unchanged) |

## E4 Log events

Unchanged. `edge.purge` and `edge.warm` carry counts, not paths, and the `cache.evict` timer
is unchanged.
