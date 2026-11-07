#!/usr/bin/env python3
"""Summarises evidence/opt/results.txt: per config and page, the median over rounds of each
variant's p50/p95/mean, the page size, and the delta against pre."""
import collections
import statistics
import sys

rows = collections.defaultdict(list)
size = {}
failed = []
for line in open(sys.argv[1], encoding="utf-8"):
    if line.startswith("#") or not line.strip():
        continue
    if " n=" not in line:  # a probe that produced no data (e.g. "PROBE FAIL flat Release 3")
        failed.append(line.strip())
        continue
    v, c, _r, page, *kv = line.split()
    d = dict(x.split("=") for x in kv)
    rows[(c, page, v)].append((float(d["p50"]), float(d["p95"]), float(d["mean"])))
    size[(page, v)] = (int(d["raw"]), int(d["gzip"]))

order = ["pre", "post", "none", "cached", "flat", "sprite", "flat-sprite"]
for c in sorted({k[0] for k in rows}):
    for page in ["library", "builder"]:
        print(f"\n{c} {page}   (median of rounds; delta vs pre)")
        print(f"  {'variant':<12} {'p50':>7} {'p95':>7} {'mean':>7}   {'d-mean':>7} {'d-p95':>7}   {'raw':>7} {'gzip':>6}  rounds")
        base = rows.get((c, page, "pre"))
        bm = statistics.median(x[2] for x in base) if base else None
        bp = statistics.median(x[1] for x in base) if base else None
        for v in order:
            r = rows.get((c, page, v))
            if not r:
                continue
            p50, p95, mean = (statistics.median(x[i] for x in r) for i in range(3))
            raw, gz = size[(page, v)]
            dm = f"{mean - bm:+7.2f}" if bm is not None else "      -"
            dp = f"{p95 - bp:+7.2f}" if bp is not None else "      -"
            print(f"  {v:<12} {p50:7.2f} {p95:7.2f} {mean:7.2f}   {dm} {dp}   {raw:7d} {gz:6d}  {len(r)}")
if failed:
    print("\nprobes with no data (not counted): " + "; ".join(failed))
