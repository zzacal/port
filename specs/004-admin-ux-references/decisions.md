# Decisions: 004 Admin UX References

| # | Decision | Owner or default | Rationale | Pivot cost |
|---|---|---|---|---|
| D-1 | Vendor-published demo logins count as public access under FR-006: Lychee's printed admin/admin and Immich's "Login as demo user" button | default (plan) | Without them, only PhotoPrism would show a photo-specific upload or batch screen | Low: drop 2 products' shots and re-run validate.sh |
| D-2 | TagChip/TagEditor excluded from the component list | default (spec) | The content model has no tags | Low: add one CP id plus 2 references |
| D-3 | Board at `docs/ux-research/admin-moodboard.html`, shots in `shots/admin/` | default (plan) | Sits beside the public board (FR-012) without touching its files | Trivial: move files |
| D-4 | Fixed preference ids P1–P7 for every Fit line | default (plan) | Makes FR-010 greppable | Trivial |
| D-5 | Shopify Polaris dropped as a source | default (plan) | Drop-zone page timed out on the anchor check | Trivial: re-add if it loads |
| D-6 | Failed and uploading batch states (Uppy) captured by blocking the upload endpoint in the browser (CDP `Network.setBlockedURLs`) | default (implement) | No public demo shows a failed upload, and blocking meant zero bytes were sent to any third party | None: evidence method only |
| D-7 | Upload inputs were fed generated zero-filled files (`DSC_0412.jpg` etc.), never real photos | default (implement) | FR-006, and Constitution III's leak concern applies to anything that looks like an upload | None |
| D-8 | Delete dialogs (PhotoPrism, Immich) opened for capture and then cancelled. Nothing was deleted on any demo | default (implement) | Shared demo instances; destroying data would affect other visitors | None |
| D-9 | Researcher A note corrected: Immich "Create album" is a plain text button, not a filled primary (P1 claim) | correction (T006) | Seen in `immich-albums-list-grouped.png` | None |
| D-10 | Researcher A note corrected: the Lychee sign-in is a bare form on a dark page, not a modal over the gallery | correction (T016) | Seen in `lychee-login-credentials.png` during the visual review | None |
| D-11 | Researcher B notes for Atlassian, WordPress Playground, Google Photos and Squarespace dropped and moved to "set aside" | correction (T009) | Atlassian added nothing Geist/Primer didn't show more quietly. WordPress wp-admin sits in nested cross-origin frames. The Google and Squarespace help pages have no UI screenshots, so nothing could be verified visually (FR-005) | Low: re-capture Atlassian if wanted |
| D-12 | Researcher B claim "Primer danger is outlined" refined: Primer danger is red text on the default grey button at rest, not an outline | correction (T009) | Seen in `primer-button-variants.png` | None |
| D-13 | Gaps kept honest rather than padded. 8 states have no public example: input saving/saved, batch item processing/interrupted, keyboard reorder, publish warning, cover pinned, delete-image confirm. Sign-in claim and refusal screens also have none | default (T010) | FR-005/SC-002 allow a recorded gap; inventing an example would break the evidence rule | These become our own design work in the wireframe step |
| D-14 | `anchors.sh` treats HTTP 489 as WARN, not DEAD | correction (T015) | Piwigo answers curl with a non-standard 489 bot wall. The same URLs load in a real browser (checked 2026-10-29) | None |
| D-15 | Recommended direction: A, "Site with a frame" | default (T013), owner to pick | Only direction where the managed item is its own public page (P4). Reuses ContactSheet (D-21) so it needs the fewest new components. As calm as the public site (P7) | Low now (documents only). Becomes a D-entry in docs/design.md once accepted |
