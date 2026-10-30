# Tasks: Admin UX References

**Input**: Design documents in `specs/004-admin-ux-references/`: plan.md, spec.md, research.md,
data-model.md, contracts/board-contract.md, quickstart.md
**Tests**: None are requested. Verification is `evidence/validate.sh` (contract C1–C10), and its
output is recorded as evidence (research R-06).

## Rules for every executor (read before any task)

- Read only the files named in your task, plus `data-model.md` and `contracts/board-contract.md`.
- Never sign in anywhere, with one exception: a login the vendor prints on its own demo page, or
  a one-click "demo user" button (research R-03, decisions D-1). Never create an account.
- Every reference you report must have been loaded by you during this run. Record `Fetch: OK` only
  for pages you actually opened. If you could not confirm something, put it under `## Gaps`. Do
  not pad.
- Use only the PG/CP ids and states listed in `data-model.md`. Use only the preference ids P1–P7
  from research R-07 in `Fit:` lines.
- Do not edit `evidence/validate.sh`, `contracts/`, or anything under `src/` or `docs/design.md`.
  A failing check means your output is wrong, not the check.
- Escalate (stop and report a blocker) if a source needs any other login, or if a required state
  has no public example after you have checked every listed source.

## Format: `[ID] [P?] [Story] Description`

---

## Phase 1: Setup

- [X] T001 Create directory `docs/ux-research/shots/admin/` with an empty `.gitkeep` file at `docs/ux-research/shots/admin/.gitkeep`
- [X] T002 Write the skeleton board `docs/ux-research/admin-moodboard.html`. It contains a `<h1>Gallery: admin reference board</h1>`, the five empty `<section id>` elements in contract order (pages, components, set-aside, patterns, directions), and inline `<style>` copied from `docs/ux-research/moodboard.html`. There must be no figures yet.

---

## Phase 2: Foundational (blocks everything after)

- [X] T003 Write `specs/004-admin-ux-references/evidence/validate.sh` implementing checks C1–C10 from `contracts/board-contract.md` against `docs/ux-research/admin-moodboard.html`:
  - Print one `PASS Cn` or `FAIL Cn: <detail>` line per check, then `ALL PASS` or `FAILED <count>`, and exit non-zero on any failure.
  - Read the PG and CP ids and the states from the tables in `specs/004-admin-ux-references/data-model.md`. Do not hard-code them.
  - Use only bash, grep, sed, and awk.
  - Run every grep with `LC_ALL=C`.
- [X] T004 Run `bash specs/004-admin-ux-references/evidence/validate.sh > specs/004-admin-ux-references/evidence/validate-baseline.txt` against the T002 skeleton.
  - Acceptance: C2, C3, C4, C5, C7 and C8 report FAIL, and C1 and C9 report PASS.
  - If any of C2–C8 passes on the empty skeleton, the script is broken. Fix T003 before continuing.

**Checkpoint**: The failing baseline is recorded. Research may start.

---

## Phase 3: User Story 1 - References per admin page (Priority: P1) 🎯 MVP

**Goal**: Each of the 5 admin pages has at least 3 screenshot-backed references on the board.
**Independent test**: `validate.sh` shows C2 and C6 PASS, and the `#pages` section shows 5
groups.

- [X] T005 [P] [US1] Researcher A (low tier, notes only), output `docs/ux-research/admin-candidates-a.md`:
  - Load the photo-specific sources from research R-02 and R-03 and record at least 15 references covering PG-signin, PG-batch, PG-library, PG-galleries and PG-builder, at least 3 each. Use the exact block format in data-model.md "Reference", with `Shot:` set to the planned filename `shots/admin/<product>-<subject>-<state>.png` (no capture yet).
  - Include CP ids in `Covers:` wherever the screen also shows a component state.
  - Sources:
    - PhotoPrism demo: https://demo.photoprism.app/ (library, albums, upload dialog)
    - Lychee: https://demo.lycheeorg.dev/ (printed admin login)
    - Immich: https://demo.immich.app/ (the "Login as demo user" button only)
    - Piwigo: https://piwigo.org/demo
    - Help-centre screenshots from SmugMug, Pixieset, Format, Adobe Portfolio, Squarespace, Flickr and Google Photos
  - End the file with `## Rejected` (one line per product, with its reason) and `## Gaps`.
- [X] T006 [US1] High tier: capture every `Shot:` named in `docs/ux-research/admin-candidates-a.md` into `docs/ux-research/shots/admin/`, following research R-04:
  - Viewport 1440×900, with the force-reveal and lazy-scroll steps from the design-reference-research skill. Use element crops for component states.
  - Correct any note whose claim the screenshot contradicts, and log the correction in `specs/004-admin-ux-references/decisions.md`.
  - Drop references that can't be captured and say so under `## Gaps`.
- [X] T007 [US1] High tier: fill `<section id="pages">` in `docs/ux-research/admin-moodboard.html` with one `<h3>` group per PG id, holding the `figure.ref` elements built from the captured references.
  - Each figure needs `data-covers`, `data-product` and `data-access` attributes, the image, and a figcaption with the product, source link, `Taken:`, `Borrow:` and `Fit:`.
  - Acceptance: validate.sh C2 PASS.

**Checkpoint**: US1 can be reviewed alone. The owner can browse page references.

---

## Phase 4: User Story 2 - References per admin component (Priority: P1)

**Goal**: Each of the 11 components has at least 2 examples, and every listed state is shown or
recorded as a gap.
**Independent test**: `validate.sh` shows C3 and C4 PASS.

- [X] T008 [P] [US2] Researcher B (low tier, notes only; runs in parallel with T005), output `docs/ux-research/admin-candidates-b.md`:
  - For every CP id and state in data-model.md, find live examples, at least 2 per CP id, and record them in the data-model "Reference" block format.
  - Sources:
    - WordPress Playground: https://playground.wordpress.net/ (wp-admin media library, upload, delete confirm)
    - Ghost help: https://ghost.org/help/
    - GOV.UK Design System: https://design-system.service.gov.uk/components/ (file upload, error message, warning text, buttons, summary list)
    - GitHub Primer: https://primer.style/components/
    - Atlassian: https://atlassian.design/components/
    - Vercel Geist: https://vercel.com/geist/introduction
    - Uppy: https://uppy.io/examples/
    - FilePond: https://pqina.nl/filepond/
    - SortableJS: https://sortablejs.github.io/Sortable/
  - Name the exact demo element or doc anchor in `URL:` so the capture can find it.
  - States with no public example go under `## Gaps` in the data-model "Gap" format. End the file with `## Rejected`.
- [X] T009 [US2] High tier: capture every `Shot:` in `docs/ux-research/admin-candidates-b.md` as element-bounded crops (CDP `Page.captureScreenshot` with `clip` set from the element's bounding box) into `docs/ux-research/shots/admin/`.
  - Where a state needs interaction (drag-over, dragging, busy), trigger it in the demo before capturing.
  - If it can't be triggered, move it to `## Gaps`.
  - Log corrections in `decisions.md`.
- [X] T010 [US2] High tier: fill `<section id="components">` with one `<h3>` group per CP id, in data-model order, holding the figures plus a `<ul>` of `li.gap` items for that CP.
  - Reuse US1 page screenshots that also show a component state by adding their CP ids to the existing figure's `data-covers`. Do not duplicate the figure.
  - Acceptance: validate.sh C3, C4, C5, C6 and C7 PASS.

**Checkpoint**: US1 and US2 together satisfy SC-001 to SC-004.

---

## Phase 5: User Story 3 - Choose a direction (Priority: P2)

**Goal**: Named directions turn the references into a pick.
**Independent test**: `validate.sh` shows C8 PASS, and the owner can name a pick after one read.

- [X] T011 [US3] High tier: fill `<section id="set-aside">` with a table of product, URL and reason, merged from the `## Rejected` lists in both notes files. Acceptance: C10 PASS.
- [X] T012 [US3] High tier: fill `<section id="patterns">` with a table of pattern, seen on (at least 2 products) and fit.
  - The fit must cite the Constitution rule ids (e.g. V.3, II.5, III.1) and/or P1–P7.
  - Define every jargon term in plain words in the same row (FR-011).
- [X] T013 [US3] High tier: write 2 or 3 `article.direction` elements in `<section id="directions">`.
  - Each needs a plain-words name with its definition, the source products, one `li[data-page]` per PG id, how it relates to the public contact-sheet grid (D-20), and a line on what it costs to build with server-rendered pages plus htmx and web components.
  - Mark exactly one direction `recommended`, with the reason tied to the five pages and the stated preferences.
  - Acceptance: C8 PASS.

---

## Phase 6: Polish & verification

- [X] T014 Run `bash specs/004-admin-ux-references/evidence/validate.sh | tee specs/004-admin-ux-references/evidence/validate-output.txt`. Acceptance: `ALL PASS`.
- [X] T015 [P] Add every source URL used on the board to the heredoc in `specs/004-admin-ux-references/evidence/anchors.sh`, then run `bash specs/004-admin-ux-references/evidence/anchors.sh | tee specs/004-admin-ux-references/evidence/anchors-output.txt`.
  - Acceptance: no used source is DEAD.
  - A DEAD source is replaced with a live one, or removed and the minimums re-checked, and the swap is logged in `decisions.md`.
- [X] T016 High tier: do the visual review per `specs/004-admin-ux-references/quickstart.md` §3.
  - Render `docs/ux-research/admin-moodboard.html` in the browser at 1440 px.
  - Check every caption against its image (state named, left/right).
  - Confirm that no shot shows a real person's private data (FR-006).
  - Fix and log any problem in `decisions.md`.
- [X] T017 Run the scope guard `git diff --stat main -- src docs/design.md`. Acceptance: no output (FR-013).
- [X] T018 Delete any `docs/ux-research/shots/_sheet*.html` and `docs/ux-research/shots/admin/_sheet*.html` contact sheets. Then commit on branch `004-admin-ux-references`:
  - Include `docs/ux-research/admin-*`, `docs/ux-research/shots/admin/`, and `specs/004-admin-ux-references/` (use `git add -f` for any evidence files that are gitignored).
  - The commit body states the recommended direction and the validate result.

---

## Dependencies & Execution Order

- T001 → T002 → T003 → T004. Setup and Foundational are strictly serial.
- After T004: T005 and T008 run in parallel (two low-tier researchers, writing disjoint files).
- T005 → T006 → T007 (US1). T008 → T009 → T010 (US2).
- T010 needs T007, because it reuses the US1 figures by editing their `data-covers`.
- US3 (T011–T013) needs T007 and T010.
- Polish: T014 needs T013. T015 can run alongside T014 and T016. T017 and T018 come last.

## Parallel Example

```text
After T004, dispatch together:
  T005 Researcher A → docs/ux-research/admin-candidates-a.md
  T008 Researcher B → docs/ux-research/admin-candidates-b.md
```

Only these two tasks are delegated to the cheaper model. Every capture, synthesis and review task
stays with the high tier (plan: Execution outline). Screenshot claims and editorial judgement are
the failure classes that cheap executors get wrong.

## Implementation Strategy

1. MVP = Phases 1–3. The board shows page references and the owner can already browse them.
2. Add US2 (components), then US3 (directions and recommendation). Re-run validate.sh after each
   phase.
3. The board is only complete when the polish phase reports `ALL PASS`, the visual review is
   done, and the scope guard is empty.
