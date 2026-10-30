# Contract: Components (003)

Parameters are the component's public inputs. "States" lists what its `/ui/` page must show
(FR-007). No component emits a `style` attribute (FR-003) or a script (FR-004).

## Atoms

**Picture**
- Parameters:
  - `Image` (ImageView, required)
  - `Sizes` (string, required)
  - `Fit` (`Natural` | `Contain`, default `Natural`)
  - `Priority` (`Lazy` | `High`, default `Lazy`)
  - `ShowThumbLayer` (bool, default false)
- Output: a `.pic` stack containing:
  - the placeholder `<img src="data:…" alt="" aria-hidden="true">`
  - optionally, a thumbnail `<img>` using `Image.ThumbVariant`
  - `<picture>` with `<source type="image/avif" srcset sizes>`, `<source type="image/webp"
    srcset sizes>`, and `<img src=(largest webp ≤ 1280) width height alt=Title>`

  When `Priority=High`, the img gets `fetchpriority="high" loading="eager"`. Otherwise it gets
  `loading="lazy" decoding="async"`.
- States: landscape, portrait, square, and panorama; High and Lazy.

**Title**
- Parameters: `Text` (required), `Element` (`p` | `h1` | `h2`, default `p`).
- Output: one line, ellipsis on overflow, with the full text in `title` for hover.
- States: short, and long enough to truncate.

**TextLink**
- Parameters: `Href` (required), `ChildContent` (required), `Rel` (optional).
- States: normal.

**ExifField**
- Parameters: `Label` (screen-reader text), `Value` (string?).
- Output: nothing when `Value` is null or empty. Otherwise `<span class="exif">` with a hidden
  label.
- States: each of the six fields with a value, shutter speed ≥ 1 s, and one field with no value.

## Molecules

**Tile**
- Parameters: `Image`, `Href`, `Sizes`, `Priority`.
- Output: an `<a>` containing a fixed 4:3 cell with `Picture Fit=Contain`, and a `Title` below,
  centred.
- States: landscape, portrait, long title.

**MetadataBar**
- Parameters: `Exif` (Exif?).
- Output: a row of ExifField in a fixed order (camera, lens, focal, aperture, shutter, ISO),
  separated by a middle dot. Nothing at all is rendered when every field is empty.
- States: all fields, some fields, none.

**Pager**
- Parameters: `PrevHref?`, `NextHref?`.
- Output: TextLinks "Previous" and "Next". A missing direction renders nothing, not a disabled
  link.
- States: first, middle, last.

**Brand**
- Parameters: `Name`, `Href` (default `/`).
- States: default.

**Nav**
- Parameters: `Items` (list of `(Text, Href, Current)`).
- States: default (one item, "Galleries").

## Organisms

**ContactSheet**
- Parameters:
  - `Items` (list of `(ImageView, Href)`)
  - `Large`, `Medium`, `Small` (int 1–6). Any other value throws an `ArgumentOutOfRangeException`
    when the component renders.
  - `PriorityCount` (int, default = `Large`)
- Output: a grid with classes `cs-l{L} cs-m{M} cs-s{S}`. It contains one Tile per item, every Tile
  gets `Sizes.ForGrid(L, M, S)`, and the first `PriorityCount` Tiles get `Priority=High`.
- States: home 3/3/1, gallery 4/3/1, fewer than one row.

**SoloViewer**
- Parameters: `Image`, `PrevHref?`, `NextHref?`.
- Output: a viewport-bounded box containing `Picture Fit=Contain Priority=High
  ShowThumbLayer=true Sizes="100vw"`, then the `Title`, the `MetadataBar`, and the `Pager`.
- States: landscape with full EXIF, portrait with partial EXIF.

**SiteHeader**
- Parameters: `SiteName`, `NavItems`.
- Output: Brand on the left, Nav on the right.
- States: default.

## Template

**PublicLayout**
- Parameters: `Title`, `ChildContent`, `NoIndex` (bool).
- Output: doctype and `<html lang="en">`. The head holds charset, viewport, the title, the
  robots meta when `NoIndex` is set, and one `<style>` with tokens plus the bundle. The body holds
  SiteHeader, then `<main>`. Page-level behaviours (Speculation Rules, view transitions) are
  deferred to the public-pages feature (spec Assumptions).
- States: an empty page body.

**UiLayout** (for /ui only, not a public component)
- Same head as PublicLayout, with `NoIndex` always on.
- Body: a small `/ui/` back link, the page heading, then labelled specimen blocks
  (`<section><h2>state</h2>…</section>`). It has no SiteHeader.
