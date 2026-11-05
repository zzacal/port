#!/usr/bin/env python3
"""Append rows to specs/013-by-camera/decisions.md. Usage: python3 add-decisions.py <rows.txt>
rows.txt: one row per line as 'KIND|decision|rationale|pivot cost'. Numbers continue from the table."""
import pathlib, re, sys
p = pathlib.Path("specs/013-by-camera/decisions.md")
text = p.read_text(encoding="utf-8").rstrip("\n") + "\n"
n = max(int(m) for m in re.findall(r"^\| (\d+) \|", text, re.M))
for line in pathlib.Path(sys.argv[1]).read_text(encoding="utf-8").splitlines():
    if not line.strip():
        continue
    kind, dec, why, piv = [x.strip() for x in re.split(r"(?<!\\)\|", line)]
    n += 1
    text += f"| {n} | {kind} | {dec} | {why} | {piv} |\n"
p.write_text(text, encoding="utf-8")
print(f"decisions.md now ends at row {n}")
