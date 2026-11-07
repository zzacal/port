#!/usr/bin/env python3
"""014 T021 (VII.2): records the TokenCombobox molecule and the panel's gallery field in the
design-record drafts (decisions 12, 16).

Each edit asserts how many times its anchor occurs, so a drifted draft fails loudly instead of
being half-edited. Run from the repo root:
    python3 specs/014-gallery-combobox/evidence/design-record-edit.py
then specs/001-architecture-stack-design/evidence/assemble.sh and validate.sh.
"""
import pathlib
import sys

D = pathlib.Path("specs/001-architecture-stack-design/draft")

# (file, old, new, expected count)
EDITS = [
    ("02d-decisions-delivery.md",
     "admin molecules `BatchItem`, `CoverMarker`, `DragHandle`, `SelectionBar`, `DangerConfirm`, `VisibilityToggle`;",
     "admin molecules `BatchItem`, `CoverMarker`, `DragHandle`, `SelectionBar`, `DangerConfirm`, `VisibilityToggle`,"
     " `TokenCombobox` (amended by feature 014, specs/014-gallery-combobox/contracts/components.md K1: a generic"
     " multi-value combobox, tokens plus a filtered listbox with an optional \"+ text\" create row, data-agnostic,"
     " behaviour in the admin script);", 1),
    ("02d-decisions-delivery.md",
     "A component is extracted only when it has a second consumer.",
     "A component is extracted only when it has a second consumer, or when the owner designates it reusable"
     " (feature 014).", 1),
    ("02d-decisions-delivery.md",
     "and title and caption editing in the side panel.",
     "and title and caption editing in the side panel, and gallery membership through a TokenCombobox"
     " (amended by feature 014: add to existing galleries, create private ones from typed text, remove; applied"
     " on Save).", 1),
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
