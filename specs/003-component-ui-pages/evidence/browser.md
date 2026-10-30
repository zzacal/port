# Browser evidence: 003-component-ui-pages (T042)

Measured 2026-10-27 in Chromium via CDP against `dotnet run` (Development, http://localhost:5080). Every probe is scoped to its own `section.specimen` or `.pic`, never the whole document. Screenshots at 1440 px are in `shots/`.

## SC-003 Column counts

Method: count distinct tile `left` offsets in the first row of each ContactSheet specimen.

| Viewport | Home (3/3/1) | Gallery (4/3/1) | Fewer than one row (4/3/1, 2 items) |
|---|---|---|---|
| 1440 px | 3 cols, cell 416 px | 4 cols, cell 300 px | 2 shown, cell 300 px |
| 900 px | 3 cols, cell 257 px | 3 cols, cell 257 px | 2 shown, cell 257 px |
| 390 px | 1 cols, cell 358 px | 1 cols, cell 358 px | 1 shown, cell 358 px |

Result: PASS. The columns match the configuration at every size. The 2-item row keeps the full-row cell width (300, 257 and 358 px) instead of stretching.

## SC-004 No cropping

Method: for every main `<img>`, compare its natural aspect ratio with its `width/height` attributes, and check that its rendered content box (accounting for `object-fit: contain`) fits inside its cell, stage, or specimen. 1440 px, after scrolling so lazy images load.

| Page | Images | All loaded | Max ratio difference | All fit |
|---|---|---|---|---|
| atoms/picture | 5 | True | 0.33% | True |
| molecules/tile | 3 | True | 0.18% | True |
| organisms/contact-sheet | 18 | True | 0.5% | True |
| organisms/solo-viewer | 2 | True | 0.02% | True |

Result: PASS. 28 images, every one within 0.5% of its stored ratio and fully inside its box.

## SC-005 No layout shift

Method: a `PerformanceObserver` for `layout-shift` is injected before page scripts run. Cache disabled, network throttled to 150 ms latency and 200 KB/s, CPU throttled 4×. The page is scrolled through and measured once every image has loaded.

| Page | CLS at 1440 | CLS at 390 |
|---|---|---|
| atoms/picture | 0 | 0 |
| molecules/tile | 0 | 0 |
| organisms/contact-sheet | 0 | 0 |
| organisms/solo-viewer | 0 | 0 |

**Observer vacuity check**: the same observer, after a deliberately inserted 12-line block, measured CLS 0.139, so it does register shifts. Result: PASS.

## Defect found and fixed during measurement (decision #28)

The first screenshot pass showed a dark band under the landscape photo on the Picture page. Measured: the `.pic` box was 330 px tall while the photo was 320 px. The cause was that all layers were stacked in one CSS grid cell, so the decoded 32×21 placeholder's own aspect ratio (1.52, versus the photo's 1.50) overrode its `width`/`height` attributes and stretched the shared row. The HTML was correct throughout, so no markup test could have caught it.

Fix:
- `.pic` is now `position: relative`. Only the main `<picture>` is in normal flow. The placeholder and thumbnail are `position: absolute` behind it, so they can't size the box.
- In contain mode the placeholder is inset 2%, so its letterboxed content always sits inside the photo's.
- `build-samples.sh` now rounds the placeholder's short side to the nearest pixel.
- Guard: `PictureLayoutCssTests`, which asserts the CSS rule itself (the layer where the defect lives).

Re-measured after the fix (every `.pic` on 4 pages at 1440 and 390 px):

| Page | Pictures | Box height = photo height (natural fit) | Placeholder inside photo |
|---|---|---|---|
| 1440 atoms/picture | 5 | True | True |
| 1440 molecules/tile | 3 | True | True |
| 1440 organisms/contact-sheet | 18 | True | True |
| 1440 organisms/solo-viewer | 2 | True | True |
| 390 atoms/picture | 5 | True | True |
| 390 molecules/tile | 3 | True | True |
| 390 organisms/contact-sheet | 18 | True | True |
| 390 organisms/solo-viewer | 2 | True | True |

Result: PASS on all 56 pictures. SC-003, SC-004 and SC-005 above were re-run after the fix.

## Visual review

All 15 screenshots were reviewed. The index groups and links are correct. Token swatches and rulers render. The Picture page shows no band. In the ContactSheet, landscapes fill the cell width, portraits are centred with white space on either side, and titles are centred and aligned across each row. PublicLayout's body text is aligned with the brand.
