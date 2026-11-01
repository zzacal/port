# T034 browser verification (006)

Run 2026-10-31 on the high tier's own instance (`--artifacts-path artifacts --urls
http://localhost:5090`, Development, output cache on), after the live seed (`seed-run.txt`).
Chromium driven over CDP. Pages used:

- home `/`
- gallery `/g/jkzbmoegpk3fnmzehx677pul4i/september-5`
- photo `/g/jkzbmoegpk3fnmzehx677pul4i/september-5/bh7f5rqk22lnj7anxjnyjzx3de` (first photo, with
  caption)

## SC-005: layout shift

A `PerformanceObserver({type:'layout-shift', buffered:true})` summed entries without recent input
over 2.5 s after load, then a scroll to the bottom and back.

| Page | 1440×900 | 1024×768 | 390×844 |
|---|---|---|---|
| home | 0 | 0 | 0 |
| gallery | 0 | 0 | 0 |
| photo | 0 | 0 | 0 |

No horizontal scroll at any width (`scrollWidth > innerWidth` false). Screenshots:
`shots/{home,gallery,photo}-{1440,390}.png`.

## Predictive loading (IV.11–IV.14)

- Hover (`pointerenter`) on a gallery-page tile adds exactly one `link[rel=preload][as=image]`.
  A second hover on the same tile adds none.
- The photo page adds two `link[rel=prefetch]`, the previous and next fallback WebPs on
  `img-dev.zacal.me`, after `load`.
- With `navigator.connection.saveData = true` emulated, the photo page adds 0 prefetch links.

**Defect found here and fixed:** in the first pass the photo page added no prefetch links. The
page rendered `data-prefetch` but `PhotoPage` did not set `Enhance`, so no loader script was on
the page. The loader was also 38 lines against the contract's "under 30". Both are fixed, with
tests that were seen failing on the old code (`PageComponentTests.Every_public_page_type_carries_the_enhancement_scripts`
and `Predictive_loader_is_under_30_lines`). The figures above are from after the fix.

## View transitions (IV.16)

The computed `view-transition-name` of the tile picture `#p-uqdjgyj627reoofx3smsuqyjhq` on the
gallery page and of the solo picture with the same id on its photo page are both
`img-uqdjgyj627reoofx3smsuqyjhq`.

## SC-009: no-JS walk

With `Emulation.setScriptExecutionDisabled`:

`/` → `/g/…/september-5` → `/g/…/september-5/bh7f5rqk…` → Next → `/g/…/september-5/znronaxk…` →
history back → `/g/…/september-5/bh7f5rqk…`

Every step was a plain link. On the photo page all three image layers loaded: placeholder 32 px,
thumb 640 px, and the full image at 1440 px (`shots/photo-nojs-1440.png`).

## Empty gallery card (E-16)

> **AMENDED (constitution 4.2.0, II.6):** superseded. Empty galleries are no longer shown at all; the measurement below
> records the behaviour before the amendment.


"Coming soon" renders as `a.tile.tile-empty` with title text "Coming soon". Its cell is 411×308.25
px, identical to the neighbouring image cells, so the grid row keeps its height.

## Observation, not a defect

In the home screenshot at 1440 px, the second row still shows the blurred placeholder. These
tiles are `loading="lazy"` (the first row is eager). The screenshot was taken while they were
still decoding. Re-measured 5 s after a fresh load at 1440 px: all 9 tile images (rows at y=82,
463, 844) reported `complete` with natural width 416, the eager first row and the lazy rows alike.
