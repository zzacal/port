# Contract: admin-moodboard.html

File: `docs/ux-research/admin-moodboard.html`. It is self-contained: no external CSS, scripts,
or fonts, and images use relative `shots/admin/...` paths. Every invariant below can be checked
with a grep in `evidence/validate.sh`.

## Structure

Sections are `<section id="...">` elements in this order:
1. `pages`
2. `components`
3. `set-aside`
4. `patterns`
5. `directions`

Each reference is one `<figure class="ref" data-covers="PG-x CP-y:state ..." data-product="<slug>"
data-access="public|vendor-demo-login|help-docs">`. It contains:
- an `<img src="shots/admin/...">`
- a `<figcaption>` with the product name, a source link, the date taken, `Borrow:`, and `Fit:`

Gaps are `<li class="gap" data-covers="CP-x:state">`. Directions are
`<article class="direction" data-name="...">`, and exactly one also has the `recommended` class.
Each direction holds one `<li data-page="PG-x">` per page.

## Invariants

- C1: The five section ids appear in the order above.
- C2: Each PG id from data-model.md appears in `data-covers` on at least 3 figures (SC-001).
- C3: Each CP id appears on at least 2 figures (FR-004).
- C4: Every `CP-id:state` from data-model.md appears in a figure's `data-covers` or in a `.gap`
  (SC-002).
- C5: There are at least 8 distinct `data-product` values (SC-003).
- C6: Every `img src` file exists, and every figure has a source link and a `Taken` date
  (SC-004).
- C7: Every figcaption contains `Fit:` (FR-010).
- C8: There are 2 or 3 `.direction` articles, each with 5 `data-page` items covering all PG ids,
  and exactly one is `.recommended` (SC-006, FR-009).
- C9: No `<script`, no `<link rel="stylesheet"`, and no `http` image src (self-contained).
- C10: `#set-aside` lists at least 1 product with a reason (FR-007).

## Not machine-checked (high-tier review)

- Each caption matches its screenshot (SC-004, second half).
- No screenshot shows another person's private data (FR-006).
- Jargon is defined in the same line (FR-011).
