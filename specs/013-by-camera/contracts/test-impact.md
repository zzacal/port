# Contract: test impact (013)

How this list was made: grep of `tests/Gallery.Web.Tests` for each literal or rule this feature
changes (speculation rule, home link count, trail literals, projector field and timer counts,
exact purge sets), plus a check of which fixtures give their images a camera.

## Fixture facts that bound the blast radius

- `SeedShapes.UploadedImage` / `SeededImage` (`Fixtures/SeedShapes.cs:55-93`) set no `Camera`. So
  every `LibraryHost` test (all `SettleAsync` / `Recorder.AllPurged()` exact-set assertions:
  BuilderTests, GalleryServiceTests, ImageServiceTests, ImageDeleteTests, LibraryFlowTests:87,
  EdgeModeTests:248, NavPurgeTests:77, RemoveSeededTests:357, PublishRefusalTests,
  GalleryListTests and the others) produces NO camera document. Their purge sets are unchanged.
  Executors must not "fix" these tests. If one goes red, the change is wrong.
- `NavBrowserFixture` (line 219) and `BrowserSiteFixture` (line 150) give every image
  `TESTCAM / T1`. NavBrowserFixture projects through the app's `SiteProjector` (line 236), so it
  gets one camera, `testcam-t1`, holding every photo of its listed galleries: the browser target
  for the new pages. BrowserSiteFixture writes `homeCards` by hand and runs no projector, so it
  gets no camera documents. That is fine; it does not visit `/c`.

## (A) Stated requirement changes (the parent edits the expected value; old → new)

| File:line | Fact | Old | New |
|---|---|---|---|
| `HomePageTests.cs:20-21` | `SpeculationRule` const | `/g`, `/g/*` | `/g`, `/g/*`, `/c`, `/c/*` (research R-07 string) |
| `HomePageTests.cs:113-114` | anchors in `nav.nav--home` | `Assert.Single(AnchorRx…)`, `Assert.Single(GalleriesLinkRx…)` | exactly 2 anchors; first is `/g` "Galleries", second `/c` "By Camera" |
| `HomeBrowserTests.cs:64-111` | home link geometry | exactly one `nav.nav--home a`; centre of that link | exactly two links; centring (a, b) measured on `nav.nav--home ul`; (d) 48 px and (e) ≥ 44 px tall checked on EACH link; (c) no scroll unchanged |
| `TrailSourceGuardTests.cs:14,40` | banned literals / count in Trails.cs | `"(Home\|Galleries)"`, count 2 | `"(Home\|Galleries\|By Camera)"`, count 3 |

## (B) Scan loops over every public page: keep, and cover the new pages in NEW tests

Read in the tasks phase (planning probe had been refused):

| File:line | Scan | Repeated for the camera pages in |
|---|---|---|
| `LibrarySourceGuardTests.cs:81` | public pages, owner session and anonymous: 200, no `/admin-assets/`, no `owner-bar`, no `lib-`/`gal-`/`b-ctl` class tokens | `CameraRoutesTests` (T018) |
| `UploadSourceGuardTests.cs:132` | public pages reference no `/admin-assets` | `CameraRoutesTests` (T018) |
| `NavRoutesTests.cs:223` | read budget (`/g` == 1, `/` == 0, gallery/photo ≤ 2) and `Server-Timing` present | `CameraRoutesTests` (T016, `==` not `≤`) |
| `LibraryBrowserTests.cs:691` | browser: public pages load no `/admin-assets/` even for the owner | `CameraBrowserTests` (T028) |
| `HomePageTests.cs:333` | exact speculation rule on every page type | `CameraRoutesTests` (T018) |
| `NavBrowserTests.cs:490,608` | no key hints in text; no duplicate header destinations | `CameraBrowserTests` (T028) |

All stay unmodified (class B).

`LibraryHost` images carry no camera. New tests give them one through
`InsertUploadedImageAsync(shape: i => i.Camera = new CameraDoc { Make = …, Model = … })`
(`Fixtures/LibraryHost.cs:202`), or through `ImageService` and the gallery services after insert.
No fixture edit is needed for `LibraryHost`.

## (C) Additive edits to shared test infrastructure

| File | Change |
|---|---|
| `LayoutConsistencyBrowserTests.cs:49-56` | Append `("cameras", "/c")`, `("camera", "/c/testcam-t1")`, `("camera-photo", "/c/testcam-t1/{first id}")` to the page list. This is the IX.4 requirement (FR-013). The existing five rows stay. |
| `Fixtures/NavBrowserFixture.cs` | Additive helpers `CameraPathAsync()` → `/c/testcam-t1` and `CameraPhotoPathAsync(int i)`, from the stored `cameras` document (its `items[i]`). |
| `Fixtures/InvariantChecker.cs` | Add one invariant: the stored `cameras` collection equals `SiteProjector.Cameras(...)` over the current data (BSON-equal, order by `rank`). It runs after every `SettleAsync`, so every owner-operation test also checks the camera projection. It holds trivially (no cameras) for camera-less fixtures and fails on a stale or wrong projection. |

## (D) Unchanged, checked

- `SiteProjectorTests.cs:67`: exactly one `db.site.project ok`. The nested timers have other
  names. Holds.
- `SiteProjectorTests.cs:83,117`: the site document's field names. The projector writes no new
  site field. Holds.
- `LibraryTimingEvidenceTests` `Ops`: unchanged. The camera timers get their own evidence test,
  because LibraryHost images carry no camera, so `db.cameras.write` would never fire there.
- `HomePageTests.cs:149-154`: home 0 reads, listing 1 read. Holds (home stays data-free).
- `PublicOutputUnchangedTests`, `UiPagesUnchangedTests`: no component default output changes
  except the speculation rule. `PublicLayoutPage` is rendered without `Enhance` (no
  `Enhance` in `Ui/Pages`), so the rule is not in those snapshots. Holds.
- `NavSourceGuardTests`: the projector is still the only nav writer. The new code adds no `Nav =`
  or `"nav"`. Holds.
