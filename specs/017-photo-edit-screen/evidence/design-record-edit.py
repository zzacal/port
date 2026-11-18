#!/usr/bin/env python3
"""017 T018 (VII.2, research R-10): records the photo edit screen in the design-record drafts.

D-20 loses the library's panel container-query clause (the panel no longer sits beside the
library grid; the builder clause stays). D-23's Library line: edit and gallery open the photo's
own edit screen instead of toggling the side panel.

Each edit asserts how many times its anchor occurs, so a drifted draft fails loudly instead of
being half-edited. Run from the repo root:
    uv run --no-project python specs/017-photo-edit-screen/evidence/design-record-edit.py
then specs/001-architecture-stack-design/evidence/assemble.sh and validate.sh.
"""
import pathlib
import sys

D = pathlib.Path("specs/001-architecture-stack-design/draft")

# (file, old, new, expected count)
EDITS = [
    # D-20: the library's panel rule goes; the builder's stays.
    ("02d-decisions-delivery.md",
     "at large widths the library and builder grids also drop to 2 columns while a side panel leaves the grid"
     " too narrow for 4 rows of icons (a container query on a wrapper around the grid: library below 800 px,"
     " builder below 992 px).",
     "at large widths the builder grid also drops to 2 columns while its side panel leaves the grid too narrow"
     " for 4 rows of icons (a container query on a wrapper around the grid, below 992 px; the library's matching"
     " rule was removed by feature 017, which moved the photo panel to its own screen).", 1),
    # D-23 Library: the icons open the photo edit screen.
    ("02d-decisions-delivery.md",
     ", where edit and gallery toggle the side panel (feature 015).",
     " (feature 015); edit and gallery open the photo's own edit screen (`/admin/library/images/{id}`): the whole"
     " photo beside the panel (title, caption, galleries, View, delete), breadcrumb Library › title"
     " (feature 017).", 1),
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
