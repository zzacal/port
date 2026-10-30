# Research: Admin UX References

Phase 0 for feature 004. Evidence for the source checks is in `evidence/anchors-output.txt`
(produced by `evidence/anchors.sh`). The live demo probe was run in a real browser on 2026-10-29.

## R-01: Method — reuse the public-board process

**Decision**: Use the same process as the public-site board in `docs/ux-research/` (commit
63fe762): cheap researchers fan out by source type and write notes files. The high tier takes
1440×900 screenshots in a real browser, reviews them on a contact sheet, and writes one
self-contained HTML board with directions.
**Rationale**: Spec FR-012 puts both boards side by side. The owner already knows how to read
that format, and it meets FR-005 (every claim checked against a screenshot).
**Alternatives**: A link list or a markdown table was rejected because it leaves claims
unchecked (FR-005) and the owner can't judge without opening the sites (SC-005). Figma-community
admin UI kits were rejected because they are mock-ups, not real products, so they fail "real
products" (US1).

## R-02: Source types and the researcher split

**Decision**: Two researchers, split by source type:
- **A, photo-specific**: self-hosted photo apps with public demos (PhotoPrism, Lychee, Piwigo,
  Immich), plus help-centre screenshots from hosted photo and portfolio services (SmugMug,
  Pixieset, Format, Adobe Portfolio, Squarespace, Flickr, Google Photos).
- **B, general admin**: a CMS admin running in the browser (WordPress Playground, which needs no
  account), Ghost's help docs, and documented component systems that publish live state examples
  for the FR-002 controls: GOV.UK Design System (file upload, error states, warning text),
  GitHub Primer, Atlassian Design System, Vercel Geist, the Uppy and FilePond upload libraries,
  and SortableJS (reorder).

**Rationale**: FR-003 requires both photo-specific and generic sources. Design systems are the
only public places that show each control in each state side by side (FR-002, SC-002), so
researcher B covers components and researcher A covers pages.
**Alternatives**: Splitting by admin page was rejected because both researchers would load the
same products. Shopify Polaris was dropped because its drop-zone page timed out on the anchor
check (DEAD 000).

## R-03: Access to admin screens without the owner's credentials (FR-006)

**Observed on 2026-10-29**:

| Source | Access |
|---|---|
| PhotoPrism demo | Opens straight into `/library/browse` with no sign-in. Library, albums, and upload dialog all reachable. |
| WordPress Playground | Spins up a throwaway WordPress in the browser with no account. wp-admin media library and upload are reachable. |
| Lychee demo | The landing page prints the demo admin login ("ADMIN / ADMIN"). |
| Immich demo | Login page with a vendor-provided "Login as demo user" button. Uploads are disabled. |
| Piwigo demo | Public demo page (200). |
| SmugMug, Pixieset, Format, Adobe Portfolio, Flickr help | Return 403 to curl (bot wall); a real browser is needed. Use only their published help screenshots. |

**Decision**: Treat a demo login the vendor publishes on the demo page itself (Lychee's printed
credentials, Immich's one-click demo button) as public access, not as "credentials the owner did
not provide". Never use any other account, and never create one. A paid product with no public
demo is referenced only through its own published help or marketing screenshots, captioned that
way.
**Rationale**: The aim of FR-006 is to avoid unauthorised access and other people's private
data. Vendor demos exist to be signed into, and they show seeded sample content. Without them,
the upload and batch screens (the hardest pages) would have no photo-specific source except
PhotoPrism.
**Alternatives**: The strict reading (no demo logins at all) was rejected because it loses
Immich and Lychee. Pivot cost is low: delete about two sources' shots and re-check the
three-per-page minimum. This decision is logged as D-1 in `decisions.md`.

## R-04: Capture and review

**Decision**: Capture in `browser_exec` at 1440×900, using the force-reveal and lazy-load steps
from the design-reference-research skill. Component close-ups are cropped by element bounding
box with CDP `Page.captureScreenshot` and a `clip`, so a state example isn't lost in a full-page
shot. File names follow `shots/admin/<product>-<subject>-<state>.png`. Review everything on a
throwaway contact sheet, which is deleted before commit.
**Rationale**: The skill's recipes already solved fade-in and lazy-load blanks on the public
board. Crops make SC-002's state check visual instead of descriptive.
**Alternatives**: Full-page shots only were rejected because states become unreadable at board
size.

## R-05: Where the deliverable lives

**Decision**: Put the board at `docs/ux-research/admin-moodboard.html`, the notes at
`docs/ux-research/admin-candidates-{a,b}.md`, and the shots in `docs/ux-research/shots/admin/`.
Verification scripts and their output go in `specs/004-admin-ux-references/evidence/`.
**Rationale**: FR-012. The `admin/` subfolder keeps the public shots untouched.

## R-06: Verification without timing logs

**Decision**: Constitution IV (timing events) has nothing to instrument here. In its place,
`evidence/validate.sh` greps the board's machine-checkable structure (contract C1–C10) and
checks that every referenced image file exists. Its output is saved to
`evidence/validate-output.txt`. A failing baseline is recorded against an empty skeleton before
any research is dispatched.
**Rationale**: This is the substitute used for document-only features (feature 001 precedent),
and it makes SC-001 to SC-004 and SC-006 countable instead of a matter of model judgement.

## R-07: Judging against stated preferences (FR-010)

**Decision**: Use a fixed list of preference ids that every reference and pattern cites:
- P1: one filled primary action per toolbar.
- P2: danger actions outlined, not solid.
- P3: per-item controls inside the item.
- P4: every managed item links to its public page.
- P5: bulk screens don't greet with validation errors.
- P6: limits enforced when files are picked.
- P7: calm, minimal chrome matching the public contact sheet.

Each reference gets a verdict line: `Fit: P3 agrees, P2 conflicts (solid red Delete)`.
**Rationale**: Fixed ids make the fit judgements greppable and comparable across references.

## R-08: Dependencies

None added. The work uses the browser tool, curl, and bash, which are already used on this
machine. Nothing touches `src/`.
