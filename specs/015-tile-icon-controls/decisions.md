# Decisions: 015 Icon Controls on Library Photos

| # | Kind | Decision | Rationale | Pivot cost |
|---|---|---|---|---|
| 1 | OWNER | The gallery icon opens the edit panel with the Galleries field in view and toggles like edit (clarify Q1 A). | Owner choice. | Low: one href. |
| 2 | OWNER | Library AND builder tiles change; the galleries list cards do not (clarify Q2 B). | Owner choice. | Low: drop K6. |
| 3 | OWNER | Gallery icon words: count only ("Not in a gallery", "In 1 gallery", "In {n} galleries"); no title, no public count. | Owner: "simplify only icon and total count". | None. |
| 4 | DEFAULT | Icon set: Lucide 1.70.0 (ISC); 8 icons copied into `Icons.cs`, no package (R-01). | Covers all 8 meanings, including three distinct folder states; same 24 px / 2 px stroke look; most active; permissive. Tabler is the runner-up. | Low: swap the path strings. |
| 5 | DEFAULT | Gemini not used. | Every meaning exists in Lucide; owner rule "if and only if needed". | None. |
| 6 | DEFAULT | "In no gallery" = `folder-plus` (not a dashed folder). | The click opens the place where a gallery is added, so the plus also tells the owner what the click does. | None: one name. |
| 7 | DEFAULT | Delivery: inline SVG per icon from one C# path table, not a sprite (R-02). | Zero requests; self-contained in every layout and catalogue page; a sprite would move all 37 `/ui` baselines for 0.25 KB gzip per 100 tiles. | Low. |
| 8 | DEFAULT | The edit toggle is a link to `?open=` / no `open`, with `#t-{id}` to land back on the tile; active = `aria-current="true"` (R-03). | Works without JS, survives a reload, one id means one active tile. | Low. |
| 9 | DEFAULT | At ≥ 1200 px the admin aside is sticky; below that a small `library.js` step scrolls the stacked panel's Galleries field into view on load (R-03, K7, J1). | Keeps "field in view" true at every width; the sticky rule is in the one layout container (IX.2). The no-JS narrow case stays as today's Edit (panel at page end). | Low. |
| 10 | DEFAULT | Title band: `rgba(0,0,0,.6)` behind `#fff` caption text, inside the photo frame (R-04). | ≥ 4.5:1 on any photo (5.7:1 over pure white); the same overlay-colour pattern as SelectMark; no new token. | None. |
| 11 | DEFAULT | Active = an inset 2 px ring, not a fill; spec FR-010 reworded (R-05). | A filled active icon would be a second filled action beside Save (009 UI-3, `LibraryBrowserTests:521`). | None. |
| 12 | DEFAULT | A photo with no public page shows the view icon unavailable (a muted, non-focusable span), not omitted (spec default). | Edit stays in the same place on every tile (IX.3). | None. |
| 13 | DEFAULT | Owner rule "↗ means a new tab" extended: the `square-arrow-out-up-right` icon counts as the ↗ cue; `NewTabLinkTests` checks both directions (R-06, test-impact A14). | The icon IS a boxed ↗; the label also says "(opens in a new tab)". | None. |
| 14 | DEFAULT | `MembershipStatus` deleted with its catalogue page and tests; `PhotoActions` takes its catalogue slot (R-08). | Its only consumer is replaced; the slot keeps `TakeLast(5)` changing by one name only. | Low: restore from git. |
| 15 | DEFAULT | Builder: `DragHandle` and `CoverMarker` gain an opt-in `Icon` mode, default output unchanged; every class, form, name and value kept (R-07). | The galleries list keeps the word handle; reorder.js and the browser tests key on the hooks. | Low. |
| 16 | DEFAULT | Pinned cover shows only the filled star (pressed), no "Cover" word. | "Icons instead of words", applied to the builder per clarify Q2. | None. |
| 17 | DEFAULT | VII.2: design record D-21 (atoms `Icon`, `IconButton`; molecule `PhotoActions` replaces `MembershipStatus`; the icon-set rule) and D-23 (library and builder tiles: title on the photo, icon row) amended in this feature. | Design is the source of truth. | None. |
