# Contract: Test impact (011)

These are stated requirement changes. Existing tests pass unmodified EXCEPT the edits listed
here, each with its old and new expected value. Any other red test means the change is wrong.
It is not permission to edit the test. The tasks phase re-runs this pass by hand against the
merged tree (grep `"/"`, `HomePath`, `"Galleries"`, `HomePage`, `LibraryHost.Page("/")` across
the whole `tests/` tree, including the shared helpers `GalleryListTests.Home`/`HomeRawAsync`,
which other files call).

## Class A: the listing moved (`"/"` → `"/g"` where the test reads the gallery grid)

Requests whose assertions concern cards, II.6 listing, read counts of the grid or its cache
change the path to `/g`. They have the same expectations otherwise.

| File | Lines (at 048f32a) | Change |
|---|---|---|
| GalleryListTests.cs | 21 `Home = LibraryHost.Page("/")`, 59 `HomeRawAsync` GET `/` | `Page("/g")`, GET `/g` |
| GalleryServiceTests.cs | 27 `Home` | `Page("/g")` |
| ImageServiceTests.cs | 21 `Home` | `Page("/g")` |
| ImageDeleteTests.cs | 26 `Home` | `Page("/g")` |
| LibraryFlowTests.cs | 85 expected purge `Page("/")`, 91 GET `/` | `/g` |
| EdgeModeTests.cs | 140, 242 `Page("/")` | `Page("/g")` |
| PurgeSetTests.cs | 57–255: every `"/"` in an expected page set | `"/g"` |
| BuilderTests.cs | 169, 341 (comments and the sets they assert) | `/g` |
| PageCacheEvictorTests.cs | 47–83 GET/evict `/` with `db.site.read` counts | `/g` |
| OutputCacheEvictionTests.cs | 41–83 GET `/` with `db.site.read` counts | `/g`; test name `Home_second_GET...` → `Listing_second_GET...` |
| SiteProjectorTests.cs | 128 GET `/` reading cards | `/g` |
| InvariantTests.cs | 53 GET `/` reading cards | `/g` |
| PublishRefusalTests.cs | 58 comment plus its GET | `/g` |
| LibraryEditTests.cs | 154–169 GET `/` then card assertions | `/g` |
| NavRoutesTests.cs | 217 `DbReads("/", "/") == 1` | `DbReads("/g", "/g") == 1`; add `DbReads("/", "/") == 0` |
| NavTimingEvidenceTests.cs | 60 route list `"/"` | `"/", "/g"` (both) |
| PublicRoutesTests.cs | 208, 339, 525 GET `/` reading cards | `/g` |
| BrowserTests.cs | 38, 152 `"home" => "/"`, 191 | `/g` (the grid tests); name stays |
| NavBrowserTests.cs | 509, 537, 543, 581 `"home" => "/"` (grid walks) | `/g` |

## Class B: secret and address scans over every public page

Loops over `"/"` plus gallery and photo pages ADD `"/g"` and keep `"/"`. The `/` entry stays
because the home page is still a public page.

| File | Lines |
|---|---|
| UploadSourceGuardTests.cs | 131 |
| LibrarySourceGuardTests.cs | 81 |
| LibraryBrowserTests.cs | 691 |
| UploadResumeTests.cs | 183 |
| PublicRoutesTests.cs | 508 |
| UploadConfirmTests.cs | 519 (add `("/g", OK)`) |
| AdminAccessTests.cs | 228, 288 (public paths reachable anonymously: add `/g`) |
| NavBrowserTests.cs | 489, 607 (add `/g`) |

AdminAccessTests 325 and 415 GET `/` only for status or session checks. They are unchanged.

## Class C: breadcrumb root (`Galleries` → `Home`)

| File | Line | Old | New |
|---|---|---|---|
| NavRoutesTests.cs | 159 | `("/", "Galleries")` first crumb | `("/", "Home")` |
| NavRoutesTests.cs | 341 | `[(null,"Galleries")]` on `/` | `[(null,"Home")]`; add `/g` → `[("/","Home"),(null,"Galleries")]` |
| NavRoutesTests.cs | 343 | `[("/","Galleries"),(null,"Not found")]` | `[("/","Home"),(null,"Not found")]` |
| NavRoutesTests.cs | 346 | gallery `[("/","Galleries"),(null,title)]` | `[("/","Home"),("/g","Galleries"),(null,title)]` |
| NavRoutesTests.cs | 350–351 | photo trails | `Home`, `/g Galleries` prepended the same way |
| PageComponentTests.cs | 51 | gallery crumbs param | unchanged (it passes its own list); expected output unchanged |
| NavComponentTests.cs | 182, 200, 338, 347, 371 | component inputs | unchanged (inputs, not page trails) |
| TemplatePageTests.cs | 36 | `Contains("Galleries")` on the PublicLayout page | becomes `Contains(">Home<")` (default trail) |

## Class D: page component rename

PageComponentTests.cs 23–198 renders `HomePage` with `Cards`. These become `ListingPage`, with
the same assertions. Add new `HomePage` tests in HomePageTests.cs instead (no `Cards` parameter).

## T-IX: the chrome comparison (LayoutConsistencyBrowserTests.cs)

- The page list becomes, in this order:
  - listing `/g`, which is the new base row
  - home `/`
  - gallery
  - photo
  - 404
- `home` is compared on the first six measures only. Measure 7, "gap to content", is the
  intentional centring gap. Every other page is compared on all seven. This is the only
  narrowing, and it is written as a named `except` list in the test, not as a looser tolerance.

## Snapshots

- `specs/006-public-site-local/evidence/ui-baseline/` (strict): `tokens.html`,
  `molecules/nav.html`, `molecules/breadcrumb.html`, `organisms/site-header.html` and
  `templates/public-layout.html` are regenerated.
- `specs/005-admin-ui-components/evidence/baseline/PublicLayoutPage.html` is regenerated.
- Before any regeneration, freeze the pre-011 copies into `specs/011-home-page/evidence/pre-011-specimens/`.
  Then regenerate and run `snapshot-diff.sh`, which must report EQUAL after stripping exactly:
  - `>Galleries<` root → `>Home<` root
  - the speculation-rule string
  - the TypeDisplay token row
  - the two appended Nav specimens
- `breadcrumb.html` and `site-header.html` specimens use their own `"Galleries"` input lists. They
  are catalogue inputs, so they change only if the specimen pages are edited. Specimen pages are
  updated to the Home root for realism, and that edit is part of the strip list.
