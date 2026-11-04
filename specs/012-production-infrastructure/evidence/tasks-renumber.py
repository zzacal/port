"""Renumber so IDs run in execution order: T012 (wiring) -> T008; T008..T011 -> T009..T012."""
import pathlib, re
p = pathlib.Path(__file__).resolve().parents[1] / "tasks.md"
m = {"T008": "T009", "T009": "T010", "T010": "T011", "T011": "T012", "T012": "T008"}
s = re.sub(r"\bT0(0[89]|1[012])\b", lambda x: m[x.group(0)], p.read_text())
p.write_text(s)
ids = re.findall(r"^- \[ \] (T\d{3})", s, re.M)
print("order ok" if ids == sorted(ids) else f"ORDER BAD {ids}", len(ids))
