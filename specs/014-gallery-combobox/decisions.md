# Decisions: 014 Galleries Combobox

| # | Kind | Decision | Rationale | Pivot cost |
|---|---|---|---|---|
| 1 | OWNER | Membership changes apply on the panel's Save, not per pick. | Owner accepted the spec defaults ("I like your choices"). | Medium: per-pick would need its own route and client posts. |
| 2 | OWNER | Validate every named gallery and title before any write; one bad entry means no gallery change. | Same. | Low: switch to apply-what-works in `SavePanelAsync`. |
| 3 | OWNER | Keep the extras: token removal, public note on options, token links. | Same. | Low each. |
| 4 | DEFAULT | Search data = every gallery rendered as hidden `role=option` items from `Data.Galleries`; no JSON, no endpoint (R-01). | Zero reads and zero requests per keystroke; no injection sink. | Low. |
| 5 | DEFAULT | Extend L2 with `add`/`create`/`leave`; absent fields keep today's behaviour exactly (R-02). | One commit point, no new route, existing tests keep their meaning. | Low. |
| 6 | DEFAULT | Add `GalleryService.RemoveOneAsync(gid, iid, scope)`; `RemoveAsync` keeps its signature and delegates (R-03). | One scope means one purge; B5 unchanged. | None. |
| 7 | DEFAULT | Missing gallery = `Refused(404, "That gallery no longer exists.")`, not `NotFound` (http.md step 5). | Re-renders with the panel open; NotFound stays "photo missing". | None. |
| 8 | DEFAULT | Accepted risk: validation and writes are not one transaction (R-02). | Single owner; a millisecond window; the result equals today's gate skip. | Low. |
| 9 | DEFAULT (superseded by 13) | ~~Page-local markup, not a catalogue molecule.~~ | | |
| 10 | DEFAULT | Dropped: count cap, duplicate-title refusal (V.4). | Marginal; G2/L3 behave the same. | Low. |
| 11 | DEFAULT (superseded by 17) | ~~Placeholder "Not in a gallery" / "Add to a gallery".~~ | Keeps `LibraryEditTests:194` passing unmodified and tells the owner the state. | None. |
| 12 | DEFAULT | VII.2: design record line "title and caption editing in the side panel" gains "and gallery membership". | Design is the source of truth. | None. |
| 13 | OWNER | The field is a reusable `TokenCombobox` molecule with a `/ui` catalogue page (K1, K2); the photo panel is its first consumer (K3). | Owner direction, 2026-11-06. | Low: one molecule, one catalogue page, three stated test changes (test-impact A2, A3, B1). |
| 14 | DEFAULT | Filtering rules are one pure `TokenCombobox.Match`; `library.js` mirrors it and a browser parity test pins the two together. | The catalogue renders states from it; unit-testable. | Low. |
| 15 | DEFAULT | Catalogue entry inserted after `VisibilityToggle`, not at the end of the molecules. | `NavCataloguePageTests` pins the last five molecules. | None. |
| 16 | DEFAULT | VII.2: the design record's D-21 molecule list gains `TokenCombobox`; its rule "A component is extracted only when it has a second consumer" gains "or when the owner designates it reusable". | Owner decision overrides the rule; recorded so the next plan doesn't flag it. | None. |
| 17 | OWNER | Follow the tag-input principles researched in `research-notes/tag-input-patterns.md` (P1–P12). Changes: first match highlighted (P3); no placeholder, a visible empty line instead (P7); polite announcements of counts, picks and removals (P8); one-line options, with the public warning moved to the moment of picking (P9); 20ch truncation with the full title kept (P10); 44×44 remove targets (P12); Backspace described (P6). | Owner direction, 2026-11-06. | Low: contract and markup only. |
| 18 | DEFAULT | Not adopted: Tab/blur creates a value (Carbon combo box) and inline completion (APG). | Creating a gallery is deliberate, so it takes an explicit "+" pick; Fluent and Polaris also skip inline completion. | Low. |
| 19 | DEFAULT | Tokens added by the script are cloned from server-rendered `<template>`s (analyze H1). | Keeps the CSS scope and one markup source; the 008 upload.js precedent. | None. |
| 20 | DEFAULT | Match ranks exact, then prefix, then contains, before title order (analyze M5). | The first row is highlighted and Enter takes it, so it must be the closest match (Fluent). | Low. |
| 21 | DEFAULT | Log outcome: a missing gallery or photo → `notfound`, a bad title or text → `refused` (analyze M2). | Separates stale-tab cases from input errors in the logs. | None. |
| 22 | DEFAULT | Field wording "Too long: at most {m} characters." differs from the server's "The title can be at most {m} characters." (analyze L7, accepted). | They appear in different places: the field is generic and has no "title" noun. | None. |
