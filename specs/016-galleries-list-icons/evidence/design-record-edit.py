#!/usr/bin/env python3
"""016 T022 (VII.2): records the admin grid counts (medium 2 columns, the panel rule), the
GalleryActions molecule and the gallery list's icon row in the design-record drafts.

Each edit asserts how many times its anchor occurs, so a drifted draft fails loudly instead of
being half-edited. Run from the repo root:
    uv run --no-project python specs/016-galleries-list-icons/evidence/design-record-edit.py
then specs/001-architecture-stack-design/evidence/assemble.sh and validate.sh.
"""
import pathlib
import sys

D = pathlib.Path("specs/001-architecture-stack-design/draft")

# (file, old, new, expected count)
EDITS = [
    # D-20: admin grid counts and the panel rule.
    ("02d-decisions-delivery.md",
     "Each usage sets its own counts: the listing is 3 / 3 / 1 and gallery is 4 / 3 / 1.",
     "Each usage sets its own counts: the listing is 3 / 3 / 1 and gallery is 4 / 3 / 1. Amended by feature 016"
     " (specs/016-galleries-list-icons/research.md R-07): the admin grids that carry an icon row under each photo"
     " use 2 columns at medium widths, so a photo is never narrower than its row (library and builder 4 / 2 / 1,"
     " gallery list 3 / 2 / 1); at large widths the library and builder grids also drop to 2 columns while a side"
     " panel leaves the grid too narrow for 4 rows of icons (a container query on a wrapper around the grid:"
     " library below 800 px, builder below 992 px). Public grids keep the counts above.", 1),
    # D-21: the GalleryActions molecule.
    ("02d-decisions-delivery.md",
     "replaced by feature 015 with `PhotoActions`, the library tile's gallery/view/edit icon row);",
     "replaced by feature 015 with `PhotoActions`, the library tile's gallery/view/edit icon row; feature 016 adds"
     " `GalleryActions`, the gallery card's move/visibility/view/edit icon row);", 1),
    # D-23: the gallery list line.
    ("02d-decisions-delivery.md",
     "- **Gallery list**: the listing grid, with private galleries dimmed. Each card has a drag handle, a Public switch, and a View link.",
     "- **Gallery list**: the listing grid, with private galleries dimmed. Each card has its title on the cover and"
     " one icon row: move, visibility (eye / crossed-out eye), view, edit (feature 016).", 1),
]


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
