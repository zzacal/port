#!/usr/bin/env python3
"""013 T031 (VII.2): records the camera grouping in the design-record drafts.

Each edit asserts how many times its anchor occurs, so a drifted draft fails loudly instead of
being half-edited. Run from the repo root:
    python3 specs/013-by-camera/evidence/design-record-edit.py
then specs/001-architecture-stack-design/evidence/assemble.sh and validate.sh.

Lifecycle scope: only the L-entries whose operation can change a camera's visible photos gain the
P-CAM note (L-07 add, L-08 remove, L-09 image delete, L-11 publish, L-12 unpublish, L-17 gallery
delete). L-10 (create: private and empty), L-13 (rename), L-14 (gallery order), L-15 (image order)
and L-16 (gallery cover) change no camera document (contracts/edge.md: reorder purges none).
"""
import pathlib
import sys

D = pathlib.Path("specs/001-architecture-stack-design/draft")

D26 = """
### D-26: Camera Grouping
**Area**: camera-grouping
**Status**: chosen
**Choice**: Added by feature 013 (specs/013-by-camera). Three public pages group visible photos by the camera that took them: `/c` (camera listing, `ContactSheet` 3 / 3 / 1 of camera cards), `/c/{slug}` (a camera's photos, 4 / 3 / 1) and `/c/{slug}/{imageId}` (the reused photo page, stepping within the camera). A derived collection `cameras` holds one document per camera with at least one visible photo: key (normalised make and model: trimmed, whitespace collapsed, case-folded), readable slug (from the display name, stable per key, `-2` suffixes on collision), display name (the photo page's own camera formatter over the newest photo), visible photo ids newest first, rank (count, then name) and a denormalised card. A photo is visible only when it is in at least one listed gallery (public with photos), is not being deleted, and has a make or model. The collection is written only by the site projection that already runs after every owner write (single writer, guarded by a source scan), and only for documents that changed. Each projection reports the changed cameras, and the owner operation adds their pages (P-CAM; title edits P-CAMTEXT) to the one edge purge it already sends.
**Rationale**: Visibility comes only from listed galleries, so a private-only or library-only photo never appears (Constitution II.2) and no upload is published as a side effect (Constitution II.5); a private-only camera and an unknown one answer the same not-found page. Denormalised cards keep the listing to one datastore read and the camera pages to two, matching the gallery pages (Constitution IV.9). Hooking the existing projection reaches every owner write path without new call sites. The decision is recorded here as the design-record update (Constitution VII.2).
**Alternatives**: Aggregating at request time (group images by camera, join listed galleries) was rejected: two or more reads and a scan of every image per uncached request (IV.9). Storing camera cards on the `site` document beside `homeCards` was rejected: the camera pages need each camera's full photo list, which would bloat the `/g` read. Denormalising a camera key onto each image was rejected: visibility lives on galleries, so the camera page would still need two reads, and a stale flag could show a private photo (research R-01).
**Pivot cost**: medium: the store, the reader and the purge input would move together; URLs `/c/...` would stay.
"""

CAMERAS_MODEL = """
### Camera group (collection `cameras`, derived; amended by feature 013)

Written only by the site projection (D-26). Deleting the collection and running any projection
rebuilds it. A camera with no visible photo has no document, so its address is not found.

| Field | Type | Rule |
|---|---|---|
| _id | string | Grouping key: normalised make + U+001F + normalised model (trim, collapse whitespace, lower-case invariant). Never both empty. |
| slug | string | From the display name; unique (indexed). A key keeps its stored slug while it stays unique; a new colliding key takes `-2`, `-3`... |
| name | string | The photo page's camera formatter over the newest visible photo's make and model. |
| items | id[] | Visible photo ids, `createdAt` desc then `_id` asc, each once. Visible = in a listed gallery, not deleting, has a make or model. |
| rank | int | Listing position: count desc, then name ignore-case, then key (indexed). |
| card | CameraCard | `{ slug, name, imageCount, cover }`, cover in the GalleryCard cover shape for `items[0]`, so `/c` is one read (IV.9). |
"""

PCAM = (" Amended by feature 013 (P-CAM, D-26): the camera listing `/c`, the affected camera pages"
        " `/c/{slug}` (old and new slug) and their changed camera photo pages are purged too, as"
        " computed by the site projection.")

LIFECYCLE_ANCHORS = [
    # (unique tail of the entry's Pages-and-caches line)
    "(the first photo added to an empty public gallery lists it), as computed by the site projection.",
    "(removing the last photo of a listed gallery unlists it), as computed by the site projection.",
    "(a gallery left empty is unlisted), as computed by the site projection.",
    "the published gallery and its new neighbours, as computed by the site projection.",
    "so its cached page stops showing neighbour links (II.2), as computed by the site projection.",
    "purged: its old neighbours, as computed by the site projection.",
]

# (file, old, new, expected count)
EDITS = [
    ("02c-decisions-content.md",
     "and requires an edge-purge of the full public namespace and a full data migration.\n",
     "and requires an edge-purge of the full public namespace and a full data migration.\n" + D26, 1),
    ("04-content-model.md",
     "\n### Upload Batch (collection `batches`, at most one with `state: open`)",
     CAMERAS_MODEL + "\n### Upload Batch (collection `batches`, at most one with `state: open`)", 1),
    ("02d-decisions-delivery.md",
     "`/g/{id}/{slug}/{imageId}` is `PublicLayout` + `SoloViewer`;",
     "`/g/{id}/{slug}/{imageId}` is `PublicLayout` + `SoloViewer`; amended by feature 013 (D-26): `/c` is `PublicLayout` + `ContactSheet` of camera cards (3 / 3 / 1), `/c/{slug}` is `PublicLayout` + `ContactSheet` of a camera's photos (4 / 3 / 1), and `/c/{slug}/{imageId}` is `PublicLayout` + `SoloViewer` with the camera trail;", 1),
    ("07-traceability.md",
     "source scan: only the site projection writes `nav` (feature 010) |",
     "source scan: only the site projection writes `nav` (feature 010); camera projection test: only photos of listed galleries enter `cameras`, and a private-only camera answers the same 404 as an unknown one; source scan: only the site projection writes `cameras` (feature 013, D-26) |", 1),
    ("07-traceability.md",
     "and the home page `/` none (`op.timing` count; feature 011) |",
     "and the home page `/` none (`op.timing` count; feature 011); the camera listing `/c` exactly one read of the denormalised `cameras` cards (feature 013, D-26) |", 1),
] + [("05-lifecycle.md", a, a + PCAM, 1) for a in LIFECYCLE_ANCHORS]


def main() -> int:
    texts = {}
    failures = []
    for name, old, new, want in EDITS:
        path = D / name
        text = texts.get(name) or path.read_text(encoding="utf-8")
        got = text.count(old)
        if got != want:
            failures.append(f"{name}: {old[:70]!r} occurs {got}x, expected {want}")
            continue
        texts[name] = text.replace(old, new)
        print(f"OK {name}: {got}x {old[:70]!r}")
    if failures:
        print("\n".join("FAIL " + f for f in failures))
        return 1
    for name, text in texts.items():
        (D / name).write_text(text, encoding="utf-8")
    print(f"wrote {len(texts)} drafts")
    return 0


if __name__ == "__main__":
    sys.exit(main())
