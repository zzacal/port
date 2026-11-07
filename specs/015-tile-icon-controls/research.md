# Research: 015 Icon Controls on Library Photos

All code findings come from `main` at e7b73d9 (probe scripts `/tmp/probe-015*.sh`). Icon facts
come from the sets' GitHub repositories, fetched 2026-11-06 (`/tmp/icon-probe`). No NEEDS
CLARIFICATION remains.

## R-01 Icon set: Lucide (ISC), 8 icons vendored

- Decision: **Lucide**, pinned to release `1.70.0`, ISC licence. Only the 8 icons the feature
  uses are copied into the code (FR-018):

  | Meaning | Lucide name | Used on |
  |---|---|---|
  | In no gallery | `folder-plus` | library gallery icon |
  | In 1 gallery | `folder` | library gallery icon |
  | In 2+ galleries | `folders` | library gallery icon |
  | View (new tab) | `square-arrow-out-up-right` | library and builder view icon |
  | Edit | `pencil` | library edit icon |
  | Move | `grip-vertical` | builder drag handle |
  | Cover | `star` (outline; filled when pinned) | builder cover icon |
  | Remove from gallery | `image-minus` | builder remove icon |

- Rationale:
  - Every needed meaning exists in the set, including three distinct folder states (`folder-plus`
    / `folder` / `folders`). Rendered side by side (`/tmp/icon-probe/sheet.html`), the three
    are distinguishable at 24 px. **No icon has to be made, so Gemini is not needed** (FR-016).
  - One 24×24 grid, a 2 px round stroke, and `stroke="currentColor"`. That matches the site's
    thin, quiet type, and the icons take the text colour (FR-017).
  - Most active of the candidates (last push 2026-11-06, about 25.6k stars, not archived).
    ISC is permissive; the only obligation is keeping the copyright notice, which goes in the
    icon file's header and on the catalogue icon page.
- Alternatives (all fetched, all MIT, all cover the 8 meanings):
  - Tabler Icons: the same 24 px, 2 px-stroke style and very large. Close second. Its
    `folders` glyph is less distinct from `folder` at 24 px, and it was not chosen because
    Lucide's set is more consistent at small sizes.
  - Phosphor: six weights and a `folder-dashed` icon for "none". Its core repo is much less
    active (last push 2026-01), and its regular weight is lighter than the site's controls.
  - Heroicons: no distinct "many folders" icon (`rectangle-stack` is the nearest), so it fails
    the three-state requirement.
  - Icon fonts (Font Awesome and similar): rejected. They download the whole font, flash an
    invisible icon until it loads, and screen readers can announce private-use characters.
- Owner-visible note: "none" could instead be a dashed `folder` (a CSS dash on the same glyph,
  not a new icon). `folder-plus` was chosen because clicking the icon opens the panel where a
  gallery is added (FR-007), so the plus also tells the owner what the click does. Changing it
  is a one-line swap after the catalogue review.

## R-02 Delivery: inline SVG per icon, paths from one C# source

- Decision: the `Icon` atom renders `<svg class="icon" viewBox="0 0 24 24" fill="none"
  stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"
  aria-hidden="true" focusable="false">{paths}</svg>`. The paths come from
  `Components/Atoms/Icons.cs`, a `IReadOnlyDictionary<string, string>` of name → inner SVG
  markup, copied verbatim from Lucide 1.70.0, with the ISC notice in the file header.
- Rationale:
  - Zero network requests and no runtime dependency (FR-017). The icons paint with the first
    frame (no font flash), scale crisply, and `currentColor` follows the text colour and its
    states.
  - Self-contained: the icon works on any page, in any layout, and in every catalogue page, with
    nothing to place first.
  - Cost, measured on 100 library tiles × 3 icons: 117.8 KB raw / 1.1 KB gzip inline, against
    51.8 KB raw / 0.9 KB gzip as a sprite plus `<use>`. That is 0.25 KB over the wire per 100
    tiles; the extra parse is about 66 KB of simple markup on an owner-only page.
- Alternatives:
  - A same-page sprite plus `<use href="#i-x">` (web.dev's recommendation for repeats). Rejected
    for this feature (V.4): the sprite must be in the page before use, so it belongs in
    `AdminLayout` and `UiLayout`. Putting it in `UiLayout` moves EVERY `/ui` byte-identity
    baseline (37 files) for a 0.25 KB gzip gain. Revisit if a public page ever shows hundreds of
    icons.
  - An external sprite file: one request, same-origin `<use>` only. Rejected under FR-017.
  - A CSS `mask-image` data URI per icon: it lives in the inlined critical CSS, and the pinned
    (filled) star needs a second icon. Rejected.
  - Icon fonts: rejected (R-01).
- Guard: a test checks that every icon name used in `src/` exists in `Icons.Paths`, and that
  every key is used somewhere in `src/` (FR-018: nothing unused shipped).

## R-03 The edit toggle is a link to the existing `?open=` address

- Today `?open={iid}` renders the panel, so only one photo can be open; the Edit link is
  `?page={p}&open={iid}`, with no hash.
- Decision: the edit icon is an `<a>`. For a photo that is not open, its href is
  `?page={p}&open={iid}#t-{iid}`; for the open photo, it is `?page={p}#t-{iid}` (close). The
  active icon carries `aria-current="true"` and `is-active`. The gallery icon has exactly the same
  two hrefs (FR-007: "exactly as the edit icon"). The tile root gets `id="t-{iid}"` through a new
  optional `Tile.Id` parameter, so every navigation lands back on the tile.
- Rationale: plain navigation works without JS and survives a reload (FR-014), and one address
  means at most one photo is active (FR-012). The `#t-` hash keeps the owner at the tile they
  toggled, instead of at the top of a 100-tile page.
- Keeping the panel in view (FR-007 "Galleries field in view"):
  - **≥ 1200 px** (aside beside the grid): `AdminLayout` makes `.admin-aside` sticky (`position:
    sticky; top: 0; max-height: 100vh; overflow: auto`). Wherever the tile is, the panel sits
    beside it with the Galleries field visible. It is a layout rule in the one admin container
    (IX.2), so it also applies to the builder's panel.
  - **< 1200 px** (panel stacked after the grid): a 6-line `library.js` enhancement scrolls
    `#lib-edit` into view on load when a panel is open and its top is below the viewport. With
    no JS, the tile is in view and the panel is at the end of the page. That is today's Edit
    behaviour, recorded as the accepted no-JS gap.
- Why `aria-current="true"` rather than `aria-pressed`: `aria-pressed` belongs to `<button>`
  toggles. A link whose target is the current state uses `aria-current`, which screen readers
  announce as "current".
- Rejected: a `<button>` plus script toggle, because it needs JS and a second state source.
  Also rejected: a `#lib-edit` hash for open, which jumps to the top of the page at wide widths
  and loses the tile.

## R-04 Title on the photo: inside Picture's frame, in a scrim band

- The admin overlay already sits inside Picture's letterboxed `.frame` (the photo's own
  rectangle, feature 009 "contain-fit frame"). Decision: a new optional `Tile` parameter
  `TitleOnPhoto` (default false) moves the `Title` into the overlay layer as
  `<div class="on-photo">`, at the frame's bottom edge. It is a sibling of the admin
  `.overlay` inside Picture's frame, never inside it: `LibraryPageTests` pins the overlay's
  children to exactly `["overlay","pos-tl"]`, and the builder tests pin "no overlay". The default output is byte-identical,
  so public tiles and `PublicOutputUnchangedTests` are unchanged (FR-004).
- Legibility (FR-002, SC-004): a solid `rgba(0,0,0,.6)` band behind white caption text (not a
  gradient). The text sits over the band's darkest point, which is fixed, so contrast does not
  depend on the photo. White on 60 % black over a pure white photo is #666 → 5.7:1. Over a black
  photo it is ≥ 15:1. The band is 1 line of `--type-caption` with 4 px / 8 px padding.
- No new colour token: the overlay values follow `SelectMark.razor.css`, which already uses
  `rgba(0,0,0,.35)` and `#fff` over photos. They are photo-overlay colours, not theme colours.
  The 0.6 value is recorded in decisions.
- Select mark vs band (FR-003): the mark is at the top-left, 8 px in. The band is at the bottom,
  about 24 px tall. Both fit any frame taller than 24 + 32 + 8 = 64 px. The shortest frame is a
  3:1 panorama in the 390 px phone cell (frame height = cell width / 3 ≈ 117 px), so they never
  overlap. A browser test measures it.
- `title` attribute: the `Title` atom already emits `title="{Text}"` (FR-001 hover).

## R-05 One button system for icons

- Decision: a new atom `IconButton`:
  - `<a>` when Href is set, `<button>` otherwise, a `<span>` when `Unavailable`;
  - a 44×44 hit box (`var(--tap)`), with the 24 px icon centred;
  - transparent background, `--color-text`;
  - hover: `--color-surface` background. `FilledButtonsJs` reads computed styles with the
    pointer parked, so a hover background never counts as a filled button;
  - `:focus-visible`: 2 px outline;
  - Active: `aria-current="true"` (links) or `aria-pressed="true"` (buttons), plus an inset
    2 px `--color-text` ring and a filled glyph where the icon allows (the star). The background
    stays transparent, so 009 UI-3 "one filled action per state" (`LibraryBrowserTests:505-525`)
    still holds unmodified with the panel open.
  - Unavailable: a `<span aria-disabled="true">` with `--color-muted`, not focusable. Its text
    is announced through `role="img"` and `aria-label`.
  - Accessible name: `aria-label` plus a `title` with the same text (FR-009). The icon SVG is
    `aria-hidden`.
- FR-010 resolved: the spec's "only the active edit icon may be filled" becomes "no icon is
  filled". Active is a ring, not a fill, because a filled active edit icon would be a second
  filled action beside the panel's Save, and `LibraryBrowserTests:521` pins `["Save"]`.
  Spec FR-010 is reworded to match.

## R-06 The ↗ new-tab rule meets icon links

- `NewTabLinkTests` (owner rule 2026-11-01): a link opens a new tab if and only if its text ends
  in ↗. The view icon has no text, so it would fail "opens a new tab but has no ↗ arrow".
- Decision: amend the rule's mechanism, not its intent. The cue for a new tab is either a
  trailing ↗ in the text, or the `square-arrow-out-up-right` icon (the ↗ drawn in a box). The
  test treats a link holding `<svg … data-icon="square-arrow-out-up-right">` like an arrow link,
  in both directions. The icon's accessible name says "(opens in a new tab)": "View photo
  (opens in a new tab)". This is a stated test change (test-impact A), and the test still
  checks both directions.

## R-07 Builder: restyle in place, keep every hook

- `reorder.js` finds the handle by `.handle` and toggles `aria-pressed`/`is-lifted`.
  `LibraryBrowserTests` finds `.b-ctl .handle`, `.b-ctl [role=status]`, and
  `.b-ctl button[name='image'][value]`.
- Decision: keep the `b-ctl` row and every class, attribute, name and value. Changes:
  - `DragHandle` gains an optional `Icon` mode, default false. In it the `⠇` glyph becomes the
    `grip-vertical` icon in a 44×44 box. The default output is unchanged, because the galleries
    list (`GalleriesPage`) still uses it unchanged (FR-021).
  - `CoverMarker` gains an optional `Icon` mode, default false:
    - Unpinned: a `star` icon button named "Make {title} the cover";
    - Automatic: a static star span named "Cover: first photo";
    - Pinned: a filled star with `aria-pressed="true"`, named "Unpin cover". The visible "Cover"
      word goes too; the filled star is the pinned cue, as requested (icons, not words).
    The form, name and value attributes are the same as today, and the default output is
    unchanged.
  - Remove becomes an `IconButton` (`image-minus`) with the same `form`, `name` and `value`, and
    the same aria-label "Remove {title} from this gallery".
  - View becomes an `IconButton` link (`square-arrow-out-up-right`, new tab).
  - Order: move, cover, remove, view (FR-022).
- Tests pinning the visible words "Remove", "Make cover", "Unpin" and `cover-tag` text in
  builder pages are stated changes (test-impact A). Each keeps its assertion and moves from the
  text to the aria-label.

## R-08 Components and the catalogue

- New atoms: `Icon` (one inline `<svg data-icon="{name}">` with Name and Size) and `IconButton` (R-05). New molecule:
  `PhotoActions` (the library tile's gallery/view/edit row). It is data-agnostic: it takes a
  count, a view href and an edit state, and builds no admin URLs itself; the page passes the
  hrefs (FR-020). It replaces `MembershipStatus` in the catalogue, in the same list position, so
  the `NavCataloguePageTests` `TakeLast(5)` list changes by one name only.
- `MembershipStatus` is removed (spec default). It has no other consumer: grep finds only
  `LibraryPage`, its catalogue page and its tests.
- Catalogue pages (FR-019):
  - `atoms/icon`: every icon with its name, at 16/24 px, plus the Lucide 1.70.0 ISC notice;
  - `atoms/icon-button`: normal, active (link and button), unavailable, and every icon;
  - `molecules/photo-actions`: none/one/many; no public page; edit active; gallery-icon open;
  - `molecules/tile`: a "Title on photo" specimen (landscape, portrait, long title) appended;
  - `molecules/drag-handle` and `molecules/cover-marker`: icon-mode specimens appended;
  - `organisms/gallery-builder` updates by itself.
- Catalogue count: 37 − 1 (MembershipStatus) + 3 (Icon, IconButton, PhotoActions) = 39.

## R-09 Timing and reads (IV.1, FR-025, SC-005)

- No new route, read or op. The library's existing `http.request GET /admin/library` and its
  four `db.library.*` timers cover the page; the builder's B1 timers cover the builder.
  Membership counts come from `LibraryTile.GalleryCount`, which is already read.
- Evidence: a Setup task captures 100 GETs of `/admin/library` and of a builder before the
  change (`evidence/pre-timings.log`). An Evidence test repeats them after, and `p95.sh` checks
  that p95 ≤ pre × 1.10 + 1 ms and that each line's `db_reads` equals the pre value.
- Page weight: inline icons add about 118 KB raw (about 1 KB gzip) to a 100-tile library page,
  while removing the words. The net transfer size is measured and recorded, not gated.

## R-10 Gemini

- Not invoked. Every meaning has an unmodified Lucide icon (R-01). Per the owner's rule, Gemini
  is used "if and only if" a needed icon does not exist.
