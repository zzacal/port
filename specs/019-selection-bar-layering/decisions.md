# Decisions: 019 Selection bar stays on top of the photos

| # | Kind | Decision | Rationale | Pivot cost |
|---|---|---|---|---|
| 1 | OWNER | Fix only the layering; the bar's look, offset and width stay. | Clarify: "for now I just don't want it to be behind the images". | Low: a later restyle touches SelectionBar.razor.css only. |
| 2 | PLAN | Contain the layers where they are created: `isolation: isolate` on Picture `.pic` and Tile `.cell`, rather than a z-index on the bars. | Root cause (R-01/R-02); a bar-side number must track Tile's internals, and `z-index: 1` alone still lost 3–4/120 points. Also fixes the upload footer and any future pinned element. | Low: two lines. |
| 3 | DEFAULT | No z-index on the bars as a second guard. | Only needed if a positioned element is placed after a bar; nothing is today (V.4). | Low: one line per bar. |
| 4 | DEFAULT | (Superseded by #6.) Upload footer gets its own regression fact (SC-002), in a new file. | Fixed at no cost by decision 2; the fact keeps it from returning unseen. | Low: delete the file. |
| 5 | PLAN | Implemented by the parent, no executor dispatch. | 2 CSS lines + 2 facts; a dispatch round-trip costs more than the work. | None. |
| 6 | PLAN-CORRECTION | Upload footer fact dropped; spec FR-003 corrected, SC-002 withdrawn. | T004: the fact PASSED on the unfixed code. The live upload page renders no Picture in batch items (upload.js builds filename placeholders; `Preview=` is passed only on `/ui` catalogue pages), so the footer has nothing to lose to. The spec's "same fault" was inferred from the CSS, not observed. | None. |
| 7 | IMPLEMENT | The sampling probe reports a geometry line (bar box, viewport, max scroll, cell count) in its vacuity message. | Found the upload case in one run. | None. |
