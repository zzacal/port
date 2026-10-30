# Browser review: 005 admin UI components

Recorded 2026-10-30, on a review instance at :5082 (`--artifacts-path artifacts`). The owner's
`dotnet watch` on :5080 was left untouched throughout.

## Measurements (all 18 admin pages)

| Check | Method | Result |
|---|---|---|
| SC-007: no sideways scroll | `scrollWidth - innerWidth` at 1440, 1024 and 390 | 0 on every page at every width |
| SC-007: public column counts | `gridTemplateColumns` track count of every `.cs` | contact-sheet (admin) 4/3/4 at 1440, 3/3/3 at 1024, 1/1/1 at 390; builder 4 → 3 → 1. Matches 4/3/1 and 3/3/1 |
| SC-005: one filled action per bar or panel | Count of filled, non-danger `.btn` per `.side-panel/.owner-bar/.batch-footer/.selection-bar/.publish-warning/.si-card` | 0 violations (after fix 3 below) |
| SC-005: danger never filled | Computed background of every `.btn-danger` | 0 filled |
| Button row parity | Top and height of the button / link / button row | 225/32, 225/32, 225/32 (identical) |
| SC-006: focus ring | 40 Tab presses through the gallery builder, computed outline on each focused control (the track for switches) | 40 of 40 have an outline |
| Font | Computed `font-family` of body, controls and text on all 33 pages | system-ui everywhere visible. Arial only on 1×1 visually hidden checkboxes (never painted) |

## Defects found and fixed in this review

1. **Overlays on letterboxed photos** (builder, admin contact sheet, CoverMarker page). Photos are
   centred inside a 4:3 cell, so corner controls floated over empty cell space and covered small
   portraits. Fixed by moving the drag handle and cover marker into a row under the title (Tile
   `Caption`). Only the select mark stays as an overlay, because it marks the photo itself.
   Logged as decisions #4.
2. **BatchItem status covered the photo.** A long failure reason plus Restart/Delete hid most of a
   small preview, and grid rows went uneven. The status now sits below the cell. Logged as #5.
3. **Two filled buttons during the publish warning** (side panel Save plus "Make public"). This
   breaks P1. SidePanel gained `SaveVariant`, and the builder demotes Save to secondary while the
   warning is up. Logged as #6.
4. **Admin pages rendered in the default serif font.** `body.admin { font: inherit }` has no parent
   to inherit from. Fixed to `font-family: var(--font)`. It is guarded by `LayoutFontCssTests`,
   which was shown to fail with the bug restored and pass after the fix (non-vacuous). Logged as #7.
5. Minor: input text size made explicit, the empty-gallery link coloured, the batch footer type
   size set, the publish-warning frame changed from a danger border to a neutral left rule (a
   warning, not an error), and the drop slot ratio taken from the `--cell-ratio` token. Logged as
   #8.

## Screenshots

`shots/`: gallery-builder-1440.png (all 5 states), batch-1440.png, signin-1440.png (all 4 states),
admin-layout-390.png.
