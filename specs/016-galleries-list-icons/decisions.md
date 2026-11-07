# Decisions: 016 Icon Controls on the Galleries List

| # | Kind | Decision | Rationale | Pivot cost |
|---|---|---|---|---|
| 1 | OWNER | The list's Public switch becomes an eye icon when public and a crossed-out eye when private; clicking it publishes or unpublishes at once (clarify Q1 A). | Owner choice. | Low: one IconButton. |
| 2 | OWNER | A photo is never narrower than its icon row: the library, builder and list grids use 2 columns at medium widths, and 2 columns at large widths while a side panel leaves the grid too narrow for 4 (plan Q2, R-07). | Owner rule: a row narrower than its photo is a design error. | Low: CSS counts and one container query per grid. |
| 3 | PARENT-FIX | A2: `VisibilityOf` is ADDED beside `SwitchOf` rather than replacing it. | BuilderTests and the builder half of PublishRefusalTests still read the builder's switch through `SwitchOf`. | None. |
| 4 | STATED-CHANGE | A8 IconTests: Keys + eye, eye-off; two SHA-256 pins (eye cb512bc1…, eye-off 1bc7a8e7…, recomputed by the parent from the fetched Lucide 1.70.0 files and equal to the executor's); IconPage sizes 8+8+1 -> 10+10+1; IconButtonPage follows Keys.Length | Two new icons (G1, R-02) | low |
| 5 | STATED-CHANGE | A9 IndexTests: catalogue count 39 -> 40 (Gallery actions) | G6 | low |
| 6 | STATED-CHANGE | B1 index.html + one li "Gallery actions" after VisibilityToggle; B3 atoms/icon.html + eye/eye-off figures (2 each), atoms/icon-button.html + eye/eye-off buttons; baseline-diff --expect A: ALL EQUAL | Verify-before-replace (evidence/baseline-diff-A.txt) | low |
| 7 | STATED-CHANGE | B2 molecules/gallery-actions.html new; reviewed in a browser at 1440 and 390 (5 specimens, one row each, edit last, unavailable icons greyed) | FR-014 | low |
| 8 | EXECUTOR | Batch A: GalleryActions writes the eye/eye-off icon names as literals in the razor (chosen by IsPublic, matching VisibilityIcon) so IconRegistryGuardTests sees them; VisibilityIcon stays the tested pure rule | The registry guard scans source for literal icon names; a computed name would read as unused | low: one ternary |
| 9 | EXECUTOR | Batch A: the catalogue's two "no photos" specimens are an empty Tile (blank cell, title under it) followed by the row, not the list's gal-band card | Tile's empty branch has no caption slot and is pinned byte-identical (R-05); the contract allows a blank cell here (T010) | low |
| 10 | EXECUTOR | Batch A: UiCatalog entry named "Gallery actions" (contract G6 verbatim), though sibling entries use the PascalCase type name ("PhotoActions") | Contract wording; no test pins the naming pattern | trivial: one string plus the B1 baseline |
