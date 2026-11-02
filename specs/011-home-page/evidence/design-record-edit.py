#!/usr/bin/env python3
"""011 T023 (VII.2): rewrites the design-record drafts for the home page / listing split.

Each replacement asserts how many times its old string occurs (normally exactly once), so a
drifted draft fails loudly instead of being half-edited. Run from the repo root:
    python3 specs/011-home-page/evidence/design-record-edit.py
then assemble.sh and validate.sh (tasks.md T023).
"""
import pathlib
import sys

D = pathlib.Path("specs/001-architecture-stack-design/draft")
N11 = "specs/011-home-page"

# (file, old, new, expected count)
EDITS = [
    # D-13: the listing is the single read.
    ("02c-decisions-content.md",
     "On the home page, galleries are ordered manually by the owner;",
     "On the galleries listing (`/g`; feature 011 moved it off the home page), galleries are ordered manually by the owner;", 1),
    ("02c-decisions-content.md",
     "onto the site document's `homeCards` array so the home page is a single datastore read.",
     "onto the site document's `homeCards` array so the galleries listing is a single datastore read. Amended by feature 011: the home page `/` shows only links and makes no datastore read.", 1),
    ("02c-decisions-content.md",
     "Denormalization satisfies the single-read home-page constraint (data-model `homeCards`, IV.9).",
     "Denormalization satisfies the single-read listing constraint (data-model `homeCards`, IV.9).", 1),
    # D-15: pages list.
    ("02d-decisions-delivery.md",
     "The home page renders from the denormalized `site.homeCards` in one datastore read.",
     "The galleries listing (`/g`) renders from the denormalized `site.homeCards` in one datastore read; the home page (`/`) renders fixed links with no read (amended by feature 011).", 1),
    # D-16: purge set says /g, / never purged.
    ("02d-decisions-delivery.md",
     "A page exists for the home page and for every gallery with at least one photo, and for its photo pages.",
     "A page exists for the galleries listing and for every gallery with at least one photo, and for its photo pages. Amended by feature 011: the listing's purge address is `/g`; the home page `/` shows fixed links, so no owner change purges it.", 1),
    # D-20: the home usage becomes the listing usage.
    ("02d-decisions-delivery.md",
     "**Choice**: The home page (gallery cards) and the gallery page (image thumbnails)",
     "**Choice**: The galleries listing (gallery cards; the home page until feature 011 moved it to `/g`) and the gallery page (image thumbnails)", 1),
    ("02d-decisions-delivery.md", "home is 3 / 3 / 1 and gallery", "the listing is 3 / 3 / 1 and gallery", 1),
    ("02d-decisions-delivery.md", "the gallery title on home, the image title", "the gallery title on the listing, the image title", 1),
    ("02d-decisions-delivery.md", "(3 on home, 4 in a gallery)", "(3 on the listing, 4 in a gallery)", 1),
    # D-21: / = PublicLayout Fill + Nav Home; amended-by-011 note on Nav and the Home trail.
    ("02d-decisions-delivery.md",
     "and the breadcrumb row is always 44 px tall.",
     "and the breadcrumb row is always 44 px tall. Amended by feature 011 (specs/011-home-page/contracts/components.md): `/` is the home page, `PublicLayout` with the `Fill` option (main fills the space under the header) holding `Nav` in its `Home` mode (stacked, centred links at the new `TypeDisplay` 48 px token; `Nav`'s default output is unchanged), and `/g` is the galleries listing; every public trail is built by one `Trails` helper and starts at `Home` (home's trail is `Home`; the listing's `Home / Galleries`).", 1),
    # D-23: sign-in card and gallery list.
    ("02d-decisions-delivery.md",
     "over the home page's first cover.",
     "over the first cover in the galleries listing (the same `homeCards` read).", 1),
    ("02d-decisions-delivery.md",
     "- **Gallery list**: the home grid,",
     "- **Gallery list**: the listing grid,", 1),
    # 05 lifecycle "Pages and caches" (and the visitor lines that name the grid).
    ("05-lifecycle.md", "the home page", "the galleries listing", 15),
    ("05-lifecycle.md", "The home page", "The galleries listing", None),
    ("05-lifecycle.md", "home-card", "listing-card", None),
    ("05-lifecycle.md", "home card", "listing card", None),
    # 06 E-16 (and its siblings that name the grid's refresh).
    ("06-edge-cases.md", "the home page", "the galleries listing", None),
    ("06-edge-cases.md", "home card", "listing card", None),
    # 04 content model (found by the T023 hit-by-hit review of docs/design.md).
    ("04-content-model.md", "that hold at least one image (II.6), so the home page is one read (IV.9).", "that hold at least one image (II.6), so the galleries listing `/g` is one read (IV.9).", 1),
    # 07 traceability.
    ("07-traceability.md",
     "private galleries absent from home, listings, sitemap;",
     "private galleries absent from the galleries listing (`/g`), listings, sitemap;", 1),
    ("07-traceability.md",
     "empty public gallery absent from `/` and its page 404;",
     "empty public gallery absent from `/g` and its page 404;", 1),
    ("07-traceability.md",
     "markup/header assertion: home page issues one datastore read (`op.timing` count)",
     "markup/header assertion: the galleries listing `/g` issues exactly one datastore read and the home page `/` none (`op.timing` count; feature 011)", 1),
]


def main() -> int:
    texts = {}
    failures = []
    for name, old, new, want in EDITS:
        path = D / name
        text = texts.get(name) or path.read_text(encoding="utf-8")
        got = text.count(old)
        if (want is None and got == 0) or (want is not None and got != want):
            failures.append(f"{name}: {old!r} occurs {got}x, expected {want if want is not None else '>=1'}")
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
