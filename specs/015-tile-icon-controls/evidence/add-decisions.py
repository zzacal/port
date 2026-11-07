#!/usr/bin/env python3
"""Append decision rows to specs/015-tile-icon-controls/decisions.md, numbering on from the table.

    python3 specs/015-tile-icon-controls/evidence/add-decisions.py <rows.txt>

One row per line: KIND | decision | rationale | pivot cost. Splits on UNESCAPED pipes only, so a
row may quote a regex with \\|. Blank lines and lines starting with # are skipped.
"""
import pathlib
import re
import sys

DOC = pathlib.Path("specs/015-tile-icon-controls/decisions.md")


def main() -> int:
    text = DOC.read_text(encoding="utf-8")
    nums = [int(m) for m in re.findall(r"^\| (\d+) \|", text, re.M)]
    n = max(nums) if nums else 0
    out = []
    for line in pathlib.Path(sys.argv[1]).read_text(encoding="utf-8").splitlines():
        if not line.strip() or line.startswith("#"):
            continue
        parts = [p.strip() for p in re.split(r"(?<!\\)\|", line)]
        if len(parts) != 4:
            print(f"bad row ({len(parts)} fields): {line[:80]}")
            return 1
        n += 1
        out.append(f"| {n} | " + " | ".join(parts) + " |")
    DOC.write_text(text.rstrip("\n") + "\n" + "\n".join(out) + "\n", encoding="utf-8")
    print(f"appended {len(out)} rows, last #{n}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
