# Decisions: 020 Admin Workspace

Kinds: OWNER (the owner chose it), DEFAULT (filled in for the owner; overturn in review), PLAN
(a planning choice), PLAN-CORRECTION (the plan corrects the spec).

| # | Kind | Decision | Rationale | Pivot cost |
|---|---|---|---|---|
| 1 | OWNER | One workspace replaces the Upload, Library, photo edit, Galleries and builder pages (preview v2, two review rounds, 2026-11-18). | "I want a fresh vision based on what problems are solved", not an iteration on the current pages. | High once built: the admin's pages and scripts are rewritten. Data, public pages and service rules are untouched. |
| 2 | OWNER | No icon-only controls; every action is a word. The in-gallery icon and any duplicate control go. | "The icons are ugly… hard to tell they are supposed to do anything"; "redundant to the pencil". | Low. |
| 3 | OWNER | Upload is a tray inside the photo view, hidden or closed by the owner; uploading inside a gallery adds to that gallery. | "The upload page can just be part of the library page"; "makes gallery-first workflow easy". | Medium: the tray and the destination field. |
| 4 | OWNER | The galleries control keeps create-by-typing for one photo or a selection. | "Like the old admin page did." | Low. |
| 5 | OWNER | Clarify 1: photos uploaded into a public gallery are visible at once. Constitution amended to 6.3.0 (II.5a to 5d). | Owner's answer "1. A". Accepted risk: a wrong file is public until removed. | Low: refuse a public destination at U3. |
| 6 | OWNER | Clarify 2: each photo joins as it finishes; no confirm, no batch discard. | Owner's answer "2. B", chosen over keeping the pipeline as built. | Medium: restore a confirm call over `JoinService`. |
| 7 | DEFAULT | Fields save when left; the Public switch applies at once. Reverses the 009 owner feedback that Public waits for Save. | No page to leave; flagged twice with the preview and in the spec report without objection. | Low: a Save button in the panel. |
| 8 | DEFAULT | Old admin addresses answer as unknown; no redirects. | Owner's standing preference for no compatibility scaffolding. | Low. |
| 9 | PLAN | Collections are switched by fetching a server-rendered fragment and swapping it in; the document never reloads (research R-01). | An upload must survive opening another collection (FR-033). | Medium. |
| 10 | PLAN | Fragments are parsed with `DOMParser` in one guarded function; the four string-to-markup sinks stay banned. Not probed in a browser at plan time. | Server markup is the only markup; no package added (V.2). | Low: vendor htmx for the swap. |
| 11 | PLAN | New JSON write routes under `/admin/api/`; the upload item routes keep their paths. | Old form routes must be unknown addresses; the 47 upload-API facts keep passing unmodified. | Low. |
| 12 | PLAN | Join is its own call (U12) made by the page after U7, not part of U7. | Keeps U3 to U9 and `UploadFlowTests` byte-for-byte (SC-012). A photo validated but not yet joined is finished on the next load. | Low. |
| 13 | PLAN | A join claims the item (`joining`); delete and restart refuse a joining item; a missing staged file is accepted when its destination exists. | Replaces the batch-wide confirm lock so one photo's join cannot race its removal (research R-06). | Medium: this is the safety core of the pipeline change. |
| 14 | PLAN | U3 refuses a public destination unless the request carries `visibleToVisitors: true`. | Makes constitution II.5a checkable on the server, not only in markup. | Low. |
| 15 | PLAN | Joins into a public gallery are not grouped: one projection and one queued purge per photo. Not probed against the edge provider's rate limit. | Grouping needs a timer and a second write path (V.4); a missed purge heals by cache lifetime (IV.18). | Low: batch the gallery add per N photos. |
| 16 | DEFAULT | Touch and keyboard reordering use worded buttons (Move earlier/later, Move up/down); dragging is mouse and pen only. The preview did not show these buttons. | The drag-handle icon is gone; whole-tile touch drag fights scrolling. | Low. |
| 17 | PLAN | `SidePanel`, `ContactSheet`, `Tile`, `Input`, `Switch`, `DangerConfirm`, `PageHeading`, `UploadDropzone` are reused; `AdminLayout` is rewritten in place. `VisibilityToggle` is removed (its text is written for a Save form). | Fewest new components; one admin layout (IX.2). | Low. |
| 18 | PLAN-CORRECTION | The spec's Assumptions listed the side panel, drop zone and page heading among components that go, and the admin layout as replaced. They are kept or rewritten in place; `VisibilityToggle` goes instead. 13 catalogue entries leave, 8 arrive: 40 → 35. The spec also said the catalogue has 43 entries; it has 40 (`IndexTests`). | Read from the components (research R-10). | None. |
| 19 | PLAN | W3 (a gallery, full document) makes 5 reads, one more than the library page it is measured against; fallback named in research R-02. Not measured at plan time. | Simplest read path; tiles are lighter than today's. | Low: fold the gallery into the list read. |
| 20 | PLAN | A reload on a deep large view loads chunks until the photo is found. | Rare; a dedicated lookup is not worth a route (V.4). | Low. |
| 21 | PLAN | The upload set remembers `added` ids so New marks survive a reload (FR-035). | The spec says the marks stay until the tray is closed. | Low. |
| 22 | PLAN | No new design token; the preview's 20 px title becomes `--type-brand`. | Tokens page and its baseline stay unchanged. | Trivial. |
