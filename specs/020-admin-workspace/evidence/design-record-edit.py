#!/usr/bin/env python3
"""T063 (research R-14, constitution VII.2): update the 001 design record for feature 020.

Edits the draft fragments under specs/001-architecture-stack-design/draft/ and the one-page
overview docs/architecture.html. Each edit names an old string and the number of times it must
occur at that point. Every count is checked, in order, on the in-memory text BEFORE anything is
written; on any mismatch the script prints the failing edit and exits 1 with no file written.
On success it writes every changed file and prints one OK line per edit.

Run from the repository root:  python3 specs/020-admin-workspace/evidence/design-record-edit.py
Then: bash specs/001-architecture-stack-design/evidence/assemble.sh
      bash specs/001-architecture-stack-design/evidence/validate.sh

Kept on purpose: the lifecycle headings "L-05: Batch confirmed" and "L-06: Batch discarded".
validate.sh C4 pins all 17 lifecycle titles, so the bodies say what happens now instead.
"""
import pathlib
import sys

ROOT = pathlib.Path(__file__).resolve().parents[3]
DRAFT = "specs/001-architecture-stack-design/draft/"

EDITS = []  # (id, path, old, new, expected_count)


def edit(eid, path, old, new, count=1):
    EDITS.append((eid, path, old, new, count))


# ---------------------------------------------------------------- 02d: D-23 superseded
F = DRAFT + "02d-decisions-delivery.md"
edit("D-23 heading", F,
     "### D-23: Admin Layout Direction: Site with a Frame\n",
     "### D-23: Admin Layout Direction: Site with a Frame (Superseded by D-27)\n")
edit("D-23 choice note", F,
     "**Choice**: Each admin page renders as the visitor sees it,",
     "**Choice**: Superseded by D-27 (feature 020, 2026-11-19): the admin is now one workspace, "
     "not a set of pages, and the pages below no longer exist. The text is kept as the record of "
     "the earlier direction. Each admin page renders as the visitor sees it,")

# ---------------------------------------------------------------- 02d: new D-27
D27 = """

### D-27: Admin Workspace
**Area**: admin-layout
**Status**: chosen
**Choice**: Added by feature 020 (specs/020-admin-workspace); replaces D-23. After sign-in the owner works on one screen, drawn by `AdminLayout` in three regions: a collections list (All photos, Not in a gallery, then every gallery in the order of the public listing, with New gallery, View site and Sign out), the photos of the open collection, and a details panel. Each collection has its own address (`/admin`, `/admin/unplaced`, `/admin/g/{gid}`); a photo viewed large adds `?photo=`. Opening another collection fetches a server-rendered fragment and swaps it in, so the document never reloads and an upload keeps running. The details panel shows the library counts or the gallery's settings when nothing is selected, the photo when one is selected, and the selection when several are. Fields save when the owner leaves them; the Public switch applies at once. Every action is a button with a visible word. The only control drawn on a photo is its select mark; Cover and New are small worded marks. Gallery membership is a checklist of galleries with a text field that narrows the list, or creates a private gallery from the typed name. Photos join a gallery by ticking it or by being dragged onto its name. Photos in a gallery and the galleries themselves are reordered by drag, or with Move buttons on touch and keyboard. Writes are JSON calls under `/admin/api/` that go through the same services, refusals, purges and logs as the old pages. Upload is a tray above the photos, not a page. It names the destination before any file is picked, and each photo joins the library as soon as it passes every check (D-11). Photos added while a gallery is open also join that gallery; when that gallery is public, the tray says in visible words that visitors will see them (Constitution II.5a). The frame has three columns at 1200 px and wider; below that the list is a strip at the top and the panel is a bottom sheet. The old Upload, Library, photo edit, Galleries and gallery builder pages are removed with their scripts and components. Their addresses answer as any unknown admin address, with no redirect. The sign-in page is unchanged.
**Rationale**: The owner asked for a new design built from the jobs the admin does, not an iteration on the current pages, and accepted a clickable preview after two review rounds (docs/ux-research/admin-preview-v2.html; 020 decisions #1). The owner also asked that upload be part of the photo view, that every action be a word, and that no two controls lead to the same place (020 decisions #2, #3). Constitution II.5 (6.3.0, rules 5a to 5d) lets an upload aimed at a gallery join it, when the destination and, for a public gallery, the visitor notice are shown before any file is picked. One layout owns the frame in every context (Constitution IX.2). The workspace calls the existing services, so every rule and refusal of the old pages still applies (020 spec FR-027).
**Alternatives**: D-23's direction, each admin page drawn as the visitor sees it with an owner bar on top and a side panel, was replaced: the owner wanted a design built from the admin's jobs rather than its pages, and found its icon rows hard to read (020 decisions #2). A first workspace preview (docs/ux-research/admin-preview.html) was rejected by the owner: "it feels like we're fixing the existing admin system". Keeping feature 008's batch confirm was rejected by the owner in clarify (020 decisions #6). Swapping regions with htmx was not chosen: the swap is one guarded `DOMParser` call and adds no package (020 decisions #10; Constitution V.2).
**Pivot cost**: high: the admin's screens and scripts would be rewritten again. Data, public pages, the addresses visitors use and the service rules are not affected (020 decisions #1)."""
edit("D-27 added after D-25", F,
     "switching to manual `az webapp deploy` needs no infrastructure change (012 decisions #7).",
     "switching to manual `az webapp deploy` needs no infrastructure change (012 decisions #7)." + D27)

# ---------------------------------------------------------------- 02d: D-20 admin grids
edit("D-20 admin grids", F,
     "Public grids keep the counts above.",
     "Public grids keep the counts above. Amended by feature 020 (D-27): those admin grids are "
     "gone; the workspace grid is `ContactSheet` with `Fill`, as many columns of at least 168 px "
     "as fit, and two below 600 px.")

# ---------------------------------------------------------------- 02d: D-21 component list
edit("D-21 web components", F,
     "Interactive admin organisms are web components with htmx.",
     "Interactive admin organisms are web components with htmx (amended by feature 020, D-27: "
     "the workspace is two web components, `admin-workspace` and `upload-tray`, and uses no htmx; "
     "regions are swapped from server-rendered fragments).")
edit("D-21 admin atoms", F,
     "`Icon` and `IconButton` (amended by feature 015, specs/015-tile-icon-controls/contracts/components.md "
     "K1-K2: Lucide 1.70.0 icons, ISC, vendored as path strings, inline SVG in the text colour; an icon "
     "is added only when a page uses it).",
     "`Mark` (feature 020: a small worded badge, Cover, New or Private). `Icon` and `IconButton` "
     "(added by feature 015: Lucide icons as inline SVG) were removed by feature 020, which has no "
     "icon-only control.")
edit("D-21 admin molecules and organisms", F,
     "admin molecules `BatchItem`, `CoverMarker`, `DragHandle`, `SelectionBar`, `DangerConfirm`, "
     "`VisibilityToggle`, `TokenCombobox` (amended by feature 014, specs/014-gallery-combobox/contracts/components.md "
     "K1: a generic multi-value combobox, tokens plus a filtered listbox with an optional \"+ text\" "
     "create row, data-agnostic, behaviour in the admin script); admin organisms `OwnerBar`, "
     "`SidePanel`, `UploadDropzone`, `BatchFooter`, `GalleryBuilder`, `SignInCard`, and admin grids",
     "admin molecules `DangerConfirm`, `PhotoCell` (a selectable photo in the workspace grid), "
     "`GalleryChecklist` (gallery membership as ticks, with a field that narrows the list or creates "
     "a gallery), `UploadRow` (one photo's upload state in words) and `Toast` (the one place results "
     "appear); admin organisms `SidePanel`, `UploadDropzone`, `SignInCard`, `CollectionList` (the "
     "workspace's collections list), `UploadTray` (the upload area above the photos) and "
     "`PhotoStage` (a photo viewed large) (amended by feature 020, specs/020-admin-workspace/contracts/components.md "
     "K4-K11; removed with the pages that used them: `BatchItem`, `CoverMarker`, `DragHandle`, "
     "`SelectionBar`, `VisibilityToggle`, `TokenCombobox` (feature 014), `OwnerBar`, `BatchFooter`, "
     "`GalleryBuilder`), and admin grids")
edit("D-21 AdminLayout template", F,
     "`AdminLayout` (owner bar plus optional side panel, D-23; htmx, the antiforgery header, and admin "
     "web components are added by the first admin feature with real behaviour; feature 005 ships it "
     "with no scripts)",
     "`AdminLayout` (amended by feature 020, D-27: the workspace frame of collections list, photos "
     "and details panel, or a bare document for the sign-in page; it renders no script, and the "
     "workspace page adds one module, `workspace.js`; before 020 it was the owner bar plus an "
     "optional side panel, D-23)")
edit("D-21 admin pages", F,
     "`/admin/*` is `AdminLayout` + admin organisms.",
     "`/admin`, `/admin/unplaced` and `/admin/g/{gid}` are `AdminLayout` + `CollectionList`, a "
     "`ContactSheet` of `PhotoCell`s, `UploadTray` and `PhotoStage` (feature 020, D-27); the "
     "sign-in page is the bare `AdminLayout` + `SignInCard`.")
edit("D-21 PhotoActions and GalleryActions", F,
     "feature 016 adds `GalleryActions`, the gallery card's move/visibility/view/edit icon row)",
     "feature 016 adds `GalleryActions`, the gallery card's move/visibility/view/edit icon row; "
     "feature 020 removed both)")
edit("D-21 admin breadcrumb", F,
     "and under the owner bar on the admin builder)",
     "and under the owner bar on the admin builder, until feature 020 removed both)")
edit("D-21 owner review 3 admin pages", F,
     "every admin page with the owner bar shows a breadcrumb (a section root's is its own name)",
     "every admin page with the owner bar shows a breadcrumb (a section root's is its own name; "
     "feature 020's workspace has neither)")

# ---------------------------------------------------------------- 02b: D-10 and D-11
F = DRAFT + "02b-decisions-media.md"
edit("D-10 copy wording", F,
     "The server records each validated file's ETag, and confirm copies with "
     "`x-amz-copy-source-if-match`",
     "The server records each validated file's ETag, and the join (the confirm before feature 020) "
     "copies with `x-amz-copy-source-if-match`")
edit("D-10 pivot wording", F,
     "replacing them does not affect storage, upload, or confirm paths (decisions #11).",
     "replacing them does not affect storage, upload, or join paths (decisions #11).")
edit("D-11 choice", F,
     "An item is marked interrupted when no heartbeat has arrived in 2 minutes, evaluated when the "
     "batch is read; the interrupted state is computed, never stored. Confirm is gated client- and "
     "server-side on every item being in `succeeded`; once accepted it locks the batch against every "
     "other change until it finishes, and each confirm copy step is idempotent. Staged files are "
     "deleted explicitly on delete, restart, rejection, discard, and confirm, and a 7-day R2 "
     "lifecycle rule on `staging/` is the backstop [st Q4];",
     "An item is marked interrupted when no heartbeat has arrived in 2 minutes, evaluated when the "
     "batch is read; the interrupted state is computed, never stored, and never reported for a "
     "`joining` item. Amended by feature 020 (020 research R-06; 020 decisions #6, #12, #13): there "
     "is no confirm and no discard. Each photo joins the library on its own as soon as it reaches "
     "`succeeded`. The page calls join for it, which atomically claims the item (`joining`), copies "
     "its master and sizes out of staging on the validated ETags, upserts the Image, pulls the item "
     "from the batch and adds its id to the batch's `added` list. A failed or interrupted photo "
     "holds no other photo up. Delete and Restart refuse a `joining` item. A join is safe to "
     "repeat: a copy whose source is gone counts as done only when its destination exists, and "
     "only one join of an item completes. The batch can carry a destination gallery, set by the "
     "first registration while the batch is empty; each joined photo is also added to the end of "
     "that gallery. Registering into a public gallery is refused unless the request says the "
     "visitor notice was shown (Constitution II.5a). A join never changes visibility (II.5c), and "
     "with no destination it touches no gallery and queues no edge work (II.5b). Closing the "
     "upload is refused while any photo is still working; otherwise it drops the failed and "
     "interrupted items with their staged files and clears `added` and the destination. The batch "
     "stays `open`: `confirmed` and `discarded` are no longer written. Staged files are deleted "
     "explicitly on delete, restart, rejection, close, and join, and a 7-day R2 lifecycle rule on "
     "`staging/` is the backstop [st Q4];")
edit("D-11 item states", F,
     "Each BatchItem has a state machine (`queued → processing → uploading → succeeded`;",
     "Each BatchItem has a state machine (`queued → processing → uploading → succeeded → joining`, "
     "added by feature 020;")
edit("D-11 rationale", F,
     "Idempotent confirm copies allow safe retry after a mid-confirm crash (data-model \"Upload "
     "batch\"; decisions #16; spec FR-028).",
     "Idempotent copies allow safe retry after a crash partway through (data-model \"Upload "
     "batch\"; decisions #16; spec FR-028; a join since feature 020, 020 spec FR-034). Per-photo "
     "joining is the owner's choice in feature 020 (020 Clarification 2): a failed photo holds "
     "nothing up, and each photo arrives where the owner aimed it.")
edit("D-11 alternatives", F,
     "Immediate staging cleanup on every failure path was rejected because the lifecycle rule "
     "backstop covers the same ground for free (decisions #16).",
     "Immediate staging cleanup on every failure path was rejected because the lifecycle rule "
     "backstop covers the same ground for free (decisions #16). Keeping feature 008's batch-wide "
     "confirm and discard was rejected by the owner in feature 020 (020 decisions #6). The "
     "batch-wide lock during confirm was replaced by the per-item `joining` claim, so one photo's "
     "join cannot race its removal (020 decisions #13).")
edit("D-11 pivot", F,
     "adding a real-time timer or changing the interrupted threshold requires only changes to that "
     "service (decisions #16, #17).",
     "adding a real-time timer or changing the interrupted threshold requires only changes to that "
     "service (decisions #16, #17). Bringing back a confirm step is a medium change: one confirm "
     "call over `ConfirmService.JoinAsync` (020 decisions #6).")

# ---------------------------------------------------------------- 05: lifecycle L-01..L-06
F = DRAFT + "05-lifecycle.md"
edit("L-01 pages", F,
     "**Pages and caches**: No public page or edge cache is affected. The library and all public "
     "pages are unchanged until L-05 (FR-017; D-11).",
     "**Pages and caches**: No public page or edge cache is affected. The library and all public "
     "pages are unchanged until this photo joins (L-05), which since feature 020 follows at once, "
     "with no confirm (FR-017; D-11).")
edit("L-02 storage", F,
     "remain in `private/staging/` until the owner chooses Restart or Delete, or until the 7-day "
     "lifecycle sweep removes them (data-model; D-11; decisions #16).",
     "remain in `private/staging/` until the owner chooses Restart or Delete, closes the upload "
     "(L-06, feature 020), or until the 7-day lifecycle sweep removes them (data-model; D-11; "
     "decisions #16).")
edit("L-02 pages", F,
     "**Pages and caches**: No public page or edge cache is affected. The library and all public "
     "pages are unchanged until L-05 (FR-017).",
     "**Pages and caches**: No public page or edge cache is affected. Since feature 020 no other "
     "photo waits for this one: each photo that succeeds joins on its own (L-05; FR-017).")
edit("L-03 records", F,
     "the owner is asked to pick the same named file again (data-model BatchItem; FR-027; D-11).",
     "the owner is asked to pick the same named file again (data-model BatchItem; FR-027; D-11). "
     "A `joining` item cannot be restarted (feature 020).")
edit("L-03 visitor", F,
     "**Visitor sees**: Nothing changes. The batch remains open and unconfirmed (FR-017; FR-011).",
     "**Visitor sees**: Nothing changes. The photo is not in the library until it succeeds and "
     "joins (L-05; FR-017; FR-011).")
edit("L-04 records", F,
     "**Records**: The BatchItem is removed from the batch's `items` array; the batch's `updatedAt` "
     "is refreshed (data-model BatchItem; FR-027).",
     "**Records**: The BatchItem is removed from the batch's `items` array; the batch's `updatedAt` "
     "is refreshed (data-model BatchItem; FR-027). A `joining` item cannot be deleted; a photo that "
     "has joined is removed only by the ordinary image delete, L-09 (feature 020).")
edit("L-05 records", F,
     "**Records**: Each BatchItem's `itemId` becomes the `_id` of a new Image document; the Image "
     "documents are written to the `images` collection with all validated fields; the batch `state` "
     "changes to `confirmed`; every Gallery whose `items` array contains an id from this batch has "
     "its `card` recomputed; `site.homeCards` is refreshed for any affected gallery in `homeOrder` "
     "(data-model; D-11; D-13; FR-017).",
     "**Records**: Feature 020 replaced the batch confirm with a join per photo; the heading is kept "
     "for the section's numbering. When an item reaches `succeeded` the page calls join. The item "
     "is claimed (`joining`); its `itemId` becomes the `_id` of a new Image document written with "
     "all validated fields (an upsert, so a repeated join writes nothing new); the item is pulled "
     "from the batch and its id added to `added`. When the batch has a destination gallery, the "
     "image is appended to that gallery's `items`, its `card` is recomputed, and `site.homeCards` "
     "is refreshed when the gallery is listed. The gallery's visibility is never changed "
     "(Constitution II.5c). With no destination no gallery is touched (II.5b). The batch stays "
     "`open` (data-model; D-11; D-13; 020 research R-06).")
edit("L-05 storage", F,
     "**Storage**: For each item: (1) staged derivatives are copied from `private/staging/` to "
     "`public/` with `Cache-Control: public, max-age=31536000, immutable`; (2) the staged master is "
     "copied to `private/masters/{imageId}.jpg`; (3) all staged objects are deleted. These are "
     "server-side copies inside R2 and the confirm step is idempotent per item (data-model Storage "
     "layout; D-07; decisions #1, #2, #8, #9).",
     "**Storage**: For the one photo: (1) staged derivatives are copied from `private/staging/` to "
     "`public/` with `Cache-Control: public, max-age=31536000, immutable`; (2) the staged master is "
     "copied to `private/masters/{imageId}.jpg`; (3) its staged objects are deleted. These are "
     "server-side copies inside R2, made only if the files still match the validated ETags. The "
     "join is idempotent: a missing source counts as copied only when its destination exists "
     "(data-model Storage layout; D-07; D-11; decisions #1, #2, #8, #9).")
edit("L-05 pages", F,
     "**Pages and caches**: If any affected gallery is public, the galleries listing and that "
     "gallery's page are purged by URL at the edge (up to 100 URLs per call, 800/s) and then "
     "warmed. The purge is best-effort; a failed purge self-heals within the 1-day HTML edge TTL "
     "(D-16; decisions #15; R-01).",
     "**Pages and caches**: Only when the destination gallery is public: the galleries listing and "
     "that gallery's page are purged by URL at the edge (up to 100 URLs per call, 800/s) and then "
     "warmed, one queued purge per photo (020 decisions #15). The purge is best-effort; a failed "
     "purge self-heals within the 1-day HTML edge TTL (D-16; decisions #15; R-01). A photo with no "
     "destination or a private one changes no public page.")
edit("L-05 visitor", F,
     "**Visitor sees**: Images from the confirmed batch appear in any public gallery they belong to "
     "immediately after purge and warm; visitors hitting a cached page see the new images within "
     "at most 1 day if purge fails (D-16; FR-017).",
     "**Visitor sees**: A photo aimed at a public gallery appears there as soon as it joins, after "
     "purge and warm; the upload area said so before any file was picked (Constitution II.5a). "
     "Visitors hitting a cached page see it within at most 1 day if the purge fails. A photo with "
     "no destination or a private one is seen by no visitor (D-16; II.5b).")
edit("L-06 records", F,
     "**Records**: The batch `state` changes to `discarded` (data-model; FR-029).",
     "**Records**: Feature 020 removed the batch discard; the heading is kept for the section's "
     "numbering. Closing the upload is refused while any photo is still working. Otherwise the "
     "failed and interrupted items are removed and the batch's `added` list and destination are "
     "cleared; the batch stays `open`. Photos that already joined stay in the library; only the "
     "ordinary delete (L-09) removes them (data-model; 020 spec FR-033).")
edit("L-06 storage", F,
     "**Storage**: All staged objects for every item in the batch — masters and derivatives — are "
     "deleted from `private/staging/` immediately; the 7-day lifecycle policy is the backstop for "
     "any that were not cleaned up (data-model; FR-029; FR-030; D-11; decisions #16).",
     "**Storage**: All staged objects of every dropped item — masters and derivatives — are deleted "
     "from `private/staging/` immediately; the 7-day lifecycle policy is the backstop for any that "
     "were not cleaned up (data-model; FR-030; D-11; decisions #16).")
edit("L-06 pages", F,
     "**Pages and caches**: No public page or edge cache is affected. No Image documents were ever "
     "created for this batch (FR-017).",
     "**Pages and caches**: No public page or edge cache is affected. No Image document is created "
     "for a dropped item (FR-017).")
edit("L-06 visitor", F,
     "**Visitor sees**: Nothing changes. The library is unaffected (FR-017; FR-011).",
     "**Visitor sees**: Nothing changes. Photos that already joined stay where they joined "
     "(FR-017; FR-011).")

# ---------------------------------------------------------------- 06: E-03, E-07, E-08
F = DRAFT + "06-edge-cases.md"
edit("E-03 outcome", F,
     "**Outcome**: When the owner returns, the open batch is still there. Images that had succeeded "
     "remain succeeded; images that were waiting, processing, or uploading with no progress for more "
     "than two minutes are shown as interrupted with Restart and Delete. Nothing has entered the "
     "library.",
     "**Outcome**: When the owner returns, the open batch is still there. Photos that had joined are "
     "in the library; a photo that passed its checks but had not joined is joined when the "
     "workspace loads (feature 020). Photos that were waiting, processing, or uploading with no "
     "progress for more than two minutes are shown as interrupted with Restart and Delete; they "
     "are not in the library.")
edit("E-03 mechanism", F,
     "L-02 captures the failed-or-interrupted state; L-05 has not fired, so no image is in the "
     "library.",
     "L-02 captures the failed-or-interrupted state; an interrupted photo has not joined (L-05), so "
     "it is not in the library. Every workspace load reads the open batch and joins its "
     "`succeeded` and `joining` items (feature 020).")
edit("E-07", F,
     "### E-07: Owner tries to confirm a batch with failed or interrupted images\n"
     "**Outcome**: Confirmation is refused — by the page and by the server — and the page states how "
     "many images still need Restart or Delete.\n"
     "**Mechanism**: D-11 enforces FR-028: the Confirm action is disabled client-side until every "
     "remaining item is succeeded, and the server independently rejects a confirm request if any "
     "item is not in the succeeded state.",
     "### E-07: Owner closes the upload with failed or interrupted photos\n"
     "**Outcome**: The photos that passed have already joined. Closing drops the failed and "
     "interrupted photos with their uploaded files and says how many were not added. Closing is "
     "refused while any photo is still working. (Before feature 020 this case was a confirm, "
     "refused until every photo had succeeded.)\n"
     "**Mechanism**: D-11 (feature 020): the close request is refused with `still-working` while any "
     "item is queued, processing, uploading, succeeded or joining; otherwise the server deletes the "
     "staged files of the remaining items and removes them (L-06). Done is unavailable on the page "
     "while a photo is working.")
edit("E-08", F,
     "### E-08: Owner tries to start a new upload while a batch is unconfirmed\n"
     "**Outcome**: The upload page shows the existing open batch, and newly picked files join it (up "
     "to the 100-photo limit); no second batch is started.\n"
     "**Mechanism**: D-11 enforces FR-029 via the unique partial index on `{state:\"open\"}` in the "
     "batches collection (data-model Upload batch). The server returns the open batch's URL; no "
     "second batch document can be inserted while one is open.",
     "### E-08: Owner adds photos while an upload is still open\n"
     "**Outcome**: The upload area shows the open upload, and newly picked files join it (up to the "
     "100-photo limit) with its destination, even when the owner has since opened another "
     "collection; no second batch is started. After a reload or a closed tab the workspace reopens "
     "the open upload (feature 020).\n"
     "**Mechanism**: D-11 enforces FR-029 via the unique partial index on `{state:\"open\"}` in the "
     "batches collection (data-model Upload batch); no second batch document can be inserted while "
     "one is open. The destination is set only while the batch has no items and no `added` ids, and "
     "the stored one wins after that (020 contracts/http.md U3). Every workspace load reads the "
     "open batch (U2).")

# ---------------------------------------------------------------- 03: upload diagram
F = DRAFT + "03-diagrams.md"
edit("diagram admin node", F,
     "  Admin[\"Admin page\"]\n",
     "  Admin[\"Workspace upload tray: destination named first\"]\n")
edit("diagram join node", F,
     "  AppConfirm[\"App Service confirm batch\"]\n",
     "  AppJoin[\"App Service join, one photo\"]\n")
edit("diagram join edges", F,
     "  ItemOK -->|\"owner presses Confirm\"| AppConfirm\n"
     "  AppConfirm -->|\"copy\"| R2Final\n"
     "  AppConfirm -->|\"Image documents created\"| Atlas\n"
     "  AppConfirm -->|\"purge and warm\"| CFEdge\n",
     "  ItemOK -->|\"page calls join at once, no confirm\"| AppJoin\n"
     "  AppJoin -->|\"copy\"| R2Final\n"
     "  AppJoin -->|\"Image document created, added to the destination gallery if any\"| Atlas\n"
     "  AppJoin -->|\"purge and warm, public destination only\"| CFEdge\n")

# ---------------------------------------------------------------- 04: content model
F = DRAFT + "04-content-model.md"
edit("Image exists after join", F,
     "An Image exists only after its batch is confirmed. It has no state field (FR-017).",
     "An Image exists only after its upload item has passed every check and joined (feature 020; "
     "before it, after its batch was confirmed). It has no state field (FR-017).")
edit("batch state field", F,
     r"| state | `open` \| `confirmed` \| `discarded` | A unique partial index on `{state:\"open\"}` "
     r"enforces a single open batch (FR-029). |".replace('\\"', '"'),
     r"| state | `open` | A unique partial index on `{state:\"open\"}` enforces a single open batch "
     r"(FR-029). Feature 020 writes only `open`; `confirmed` and `discarded` documents from before "
     r"stay, inert. |".replace('\\"', '"'))
edit("batch destination and added", F,
     "| items | BatchItem[] | |\n",
     "| items | BatchItem[] | |\n"
     "| destination | id? | Feature 020. The gallery this upload's photos also join. Set by the first "
     "registration while the batch has no items and no `added` ids; unset when that gallery is gone "
     "at a join; cleared on close. |\n"
     "| added | id[] | Feature 020. Images joined in this upload, oldest first; cleared on close. |\n")
edit("BatchItem itemId", F,
     "| itemId | id | Becomes the Image `_id` when the batch is confirmed. |",
     "| itemId | id | Becomes the Image `_id` when the item joins (feature 020; before it, when the "
     "batch was confirmed). |")
edit("BatchItem state", F,
     r"| state | `queued` \| `processing` \| `uploading` \| `succeeded` \| `failed` \| `interrupted` "
     r"| `interrupted` is computed on read from `heartbeatAt`, never stored (D-11). |",
     r"| state | `queued` \| `processing` \| `uploading` \| `succeeded` \| `joining` \| `failed` \| "
     r"`interrupted` | `interrupted` is computed on read from `heartbeatAt`, never stored, and never "
     r"reported for a `joining` item (D-11). `joining` was added by feature 020. |")
edit("item state machine", F,
     "    uploading --> succeeded\n",
     "    uploading --> succeeded\n"
     "    succeeded --> joining : join claims the item (feature 020)\n"
     "    joining --> [*] : joined, the item leaves the batch and the Image exists\n"
     "    joining --> failed : a staged file changed after validation\n")
edit("batch state machine", F,
     "    open --> confirmed : all items succeeded and at least one item present (FR-028)\n"
     "    open --> discarded : always allowed — all staged keys are deleted (FR-029)\n",
     "    open --> open : close (feature 020) — refused while a photo is working; drops failed and interrupted items and their staged keys\n")
edit("batch confirm paragraph", F,
     "On confirm, each item's derivatives and master are copied out of staging (see the Storage Layout\n"
     "section below), its Image document is created, and the batch is marked confirmed. A confirm that\n"
     "crashes partway through can be re-run (D-11).",
     "Amended by feature 020: there is no confirm and no discard, and the batch stays `open`. Each\n"
     "item joins on its own: its derivatives and master are copied out of staging (see the Storage\n"
     "Layout section below) and its Image document is created. A join that crashes partway through\n"
     "can be re-run (D-11). Before feature 020 the batch moved to `confirmed` or `discarded`.")
edit("storage layout master", F,
     "master.jpg         replaced on confirm by:",
     "master.jpg         replaced on join by:")
edit("storage layout derivatives", F,
     "{w}.{avif|webp}    deleted after confirm",
     "{w}.{avif|webp}    deleted after join")
edit("storage on join", F,
     "On confirm, for each item:",
     "On join, for each item (on confirm before feature 020):")
edit("storage idempotent", F,
     "These are server-side copies inside R2. The confirm step is idempotent per item.",
     "These are server-side copies inside R2. The join is idempotent per item.")

# ---------------------------------------------------------------- 07: traceability
F = DRAFT + "07-traceability.md"
edit("matrix constitution version", F,
     "One row per numbered rule in constitution v6.3.0,",
     "One row per numbered rule in constitution v6.3.1,")
edit("II.5 row", F,
     "| II.5 | D-14, D-13: galleries default private; upload and edit never change visibility | "
     "integration test: new gallery and confirmed batch leave visibility private | designed |",
     "| II.5 | D-14, D-13, D-11, D-27: galleries default private; upload and edit never change "
     "visibility. An upload joins a gallery only when the owner made it the destination, named in "
     "the upload area before any file is picked, with a visible notice when that gallery is public; "
     "the server refuses a public destination without the notice flag; a join with no destination "
     "touches no gallery; a join never writes visibility; a photo joins only after every check "
     "(II.5a to 5d, feature 020) | integration test: UploadJoinTests (public destination without "
     "the flag is 409 `destination-public`; a join with no destination calls no gallery method and "
     "queues no edge work; a join never changes visibility; public pages byte-identical around a "
     "join with no destination); browser test: WorkspaceUploadBrowserTests (the tray names the "
     "destination and shows the public notice before any file is picked) | designed |")
edit("III.2 row", F,
     "integration test: only validated derivatives are copied to `public` on confirm |",
     "integration test: only validated derivatives are copied to `public` on join (on confirm "
     "before feature 020) |")
edit("IX.1 row", F,
     "| IX.1 | D-21, D-23 (amended by 010 owner review 3):",
     "| IX.1 | D-21, D-27 (D-23 before feature 020; amended by 010 owner review 3):")
edit("IX.2 row", F,
     "| IX.2 | D-21, D-23: PublicLayout and AdminLayout",
     "| IX.2 | D-21, D-27 (D-23 before feature 020): PublicLayout and AdminLayout")

# ---------------------------------------------------------------- 08: cost wording
F = DRAFT + "08-cost.md"
edit("cost class A wording", F,
     "staging PUTs and confirm copies",
     "staging PUTs and join copies")

# ---------------------------------------------------------------- docs/architecture.html
F = "docs/architecture.html"
edit("architecture upload label", F,
     ">upload batch</text>",
     ">upload tray</text>")
edit("architecture upload actions", F,
     ">Restart · Delete · Confirm</text>",
     ">Retry · Remove · join</text>")
edit("architecture staging", F,
     ">staging/ — unconfirmed batch</text>",
     ">staging/ — photos not yet joined</text>")
edit("architecture card title", F,
     "<h3>Upload and confirm</h3>",
     "<h3>Upload and join</h3>")
edit("architecture card line", F,
     "<li>• Confirm copies to public + masters, then purge + warm (D-11)</li>",
     "<li>• Each photo joins once checked: copy to public + masters; purge + warm for a public "
     "gallery (D-11)</li>")


def main():
    texts = {}
    for _, path, _, _, _ in EDITS:
        if path not in texts:
            texts[path] = (ROOT / path).read_text(encoding="utf-8")
    original = dict(texts)
    for eid, path, old, new, count in EDITS:
        n = texts[path].count(old)
        if n != count:
            print(f"ABORT {eid}: {path}: old string found {n} times, expected {count}. Nothing written.")
            sys.exit(1)
        texts[path] = texts[path].replace(old, new)
    for path, text in texts.items():
        if text != original[path]:
            (ROOT / path).write_text(text, encoding="utf-8")
    for eid, path, _, _, count in EDITS:
        print(f"OK {eid} ({path}, {count}x)")
    print(f"{len(EDITS)} edits applied to {sum(texts[p] != original[p] for p in texts)} files")


if __name__ == "__main__":
    main()
