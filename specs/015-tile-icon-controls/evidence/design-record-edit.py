#!/usr/bin/env python3
"""015 T025 (VII.2, decisions 17): records the icon system, the PhotoActions molecule and the icon
rows on library and builder tiles in the design-record drafts.

Each edit asserts how many times its anchor occurs, so a drifted draft fails loudly instead of
being half-edited. Run from the repo root:
    python3 specs/015-tile-icon-controls/evidence/design-record-edit.py
then specs/001-architecture-stack-design/evidence/assemble.sh and validate.sh.
"""
import pathlib
import sys

D = pathlib.Path("specs/001-architecture-stack-design/draft")

# (file, old, new, expected count)
EDITS = [
    ("02d-decisions-delivery.md",
     "admin-only `Button`, `Input`, `Switch`, `SelectMark` (amended by feature 005: `TagChip` dropped, since the content model has no tags).",
     "admin-only `Button`, `Input`, `Switch`, `SelectMark` (amended by feature 005: `TagChip` dropped, since the content model has no tags),"
     " `Icon` and `IconButton` (amended by feature 015, specs/015-tile-icon-controls/contracts/components.md K1-K2: Lucide 1.70.0"
     " icons, ISC, vendored as path strings, inline SVG in the text colour; an icon is added only when a page uses it).", 1),
    ("02d-decisions-delivery.md",
     " and `MembershipStatus` (the library tile's linked first-gallery status with `+N`);",
     " and `MembershipStatus` (the library tile's linked first-gallery status with `+N`; replaced by feature 015 with"
     " `PhotoActions`, the library tile's gallery/view/edit icon row);", 1),
    ("02d-decisions-delivery.md",
     "remove; applied on Save).",
     "remove; applied on Save), photo titles on the photo and a gallery/view/edit icon row, where edit and gallery"
     " toggle the side panel (feature 015).", 1),
    ("02d-decisions-delivery.md",
     "(drag handle, the cover control, Remove) in a row under each tile's title, never as corner overlays on the cell:",
     "(drag handle, the cover control, Remove) in a row under each tile's title (per-tile controls as icons, with the"
     " title on the photo, feature 015), never as corner overlays on the cell:", 1),
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
