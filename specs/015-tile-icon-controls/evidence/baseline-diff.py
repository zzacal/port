#!/usr/bin/env python3
"""015 T003: verify-before-replace for the /ui byte-identity baselines (contracts/test-impact.md B).

Compares the live baselines (specs/006-public-site-local/evidence/ui-baseline/) with the frozen
pre-015 copy (specs/015-tile-icon-controls/evidence/pre-015-ui-baseline/). For each file the
feature changes, it strips EXACTLY the intended change and requires the rest to equal the frozen
copy; every other file must be byte-identical.

    python3 specs/015-tile-icon-controls/evidence/baseline-diff.py [--expect A,B,C]
    python3 specs/015-tile-icon-controls/evidence/baseline-diff.py --selftest

--expect names the batches whose baseline changes have landed (default A,B,C):
  A  B1 index.html: + Icon, IconButton atom <li> after SelectMark;
     B2 molecules/tile.html: + 3 "Title on photo, ..." specimens; B7 atoms/icon.html, atoms/icon-button.html new
  B  B1 index.html: MembershipStatus <li> -> PhotoActions <li> (same slot);
     B6 molecules/membership-status.html deleted; B7 molecules/photo-actions.html new
  C  B3 molecules/drag-handle.html: + "Icon" specimen;
     B4 molecules/cover-marker.html: + 3 "Icon, ..." specimens;
     B5 organisms/gallery-builder.html: a stated change, reviewed by eye (reported STATED, not compared)
Every stripped file must DIFFER from its frozen copy before stripping (a vacuous strip is a FAIL).
Scoped b-xxxxxxxxxx attributes are tolerated by every pattern. Exit status = number of failures.
"""
import pathlib
import re
import shutil
import sys
import tempfile

ROOT = pathlib.Path(__file__).resolve().parents[3]
LIVE = ROOT / "specs/006-public-site-local/evidence/ui-baseline"
FROZEN = ROOT / "specs/015-tile-icon-controls/evidence/pre-015-ui-baseline"

A = r'(?: b-[a-z0-9]{10})*'  # zero or more scope attributes


def li(slug: str, name: str) -> str:
    return rf'<li{A}><a href="/ui/{re.escape(slug)}"{A}>{re.escape(name)}</a></li>'


def drop_specimens(text: str, labels: list[str]) -> str:
    for label in labels:
        pat = rf'\n    <section class="specimen"{A}><h2 class="specimen-label"{A}>{re.escape(label)}</h2>.*?</section>'
        text, n = re.subn(pat, "", text, count=1, flags=re.S)
        if n != 1:
            raise ValueError(f"specimen {label!r} not found")
    return text


def strip_index(text: str, expect: set[str]) -> str:
    if "A" in expect:
        text, n = re.subn(li("atoms/icon", "Icon") + li("atoms/icon-button", "IconButton"), "", text)
        if n != 1:
            raise ValueError("Icon + IconButton <li> pair not found exactly once")
    if "B" in expect:
        m = re.search(rf'<li({A})><a href="/ui/molecules/photo-actions"({A})>PhotoActions</a></li>', text)
        if not m:
            raise ValueError("PhotoActions <li> not found")
        text = text[:m.start()] + f'<li{m.group(1)}><a href="/ui/molecules/membership-status"{m.group(2)}>MembershipStatus</a></li>' + text[m.end():]
    return text


TILE = ["Title on photo, landscape", "Title on photo, portrait", "Title on photo, long title"]
DRAG = ["Icon"]
COVER = ["Icon, unpinned", "Icon, automatic", "Icon, pinned"]


def plan(expect: set[str]):
    """(stripped files: rel -> strip fn), new files, deleted files, stated files."""
    strip, new, deleted, stated = {}, set(), set(), set()
    if expect & {"A", "B"}:
        strip["index.html"] = lambda t: strip_index(t, expect)
    if "A" in expect:
        strip["molecules/tile.html"] = lambda t: drop_specimens(t, TILE)
        new |= {"atoms/icon.html", "atoms/icon-button.html"}
    if "B" in expect:
        new.add("molecules/photo-actions.html")
        deleted.add("molecules/membership-status.html")
    if "C" in expect:
        strip["molecules/drag-handle.html"] = lambda t: drop_specimens(t, DRAG)
        strip["molecules/cover-marker.html"] = lambda t: drop_specimens(t, COVER)
        stated.add("organisms/gallery-builder.html")
    return strip, new, deleted, stated


def compare(live: pathlib.Path, frozen: pathlib.Path, expect: set[str]) -> int:
    strip, new, deleted, stated = plan(expect)
    live_files = {p.relative_to(live).as_posix() for p in live.rglob("*.html")}
    frozen_files = {p.relative_to(frozen).as_posix() for p in frozen.rglob("*.html")}
    fails = 0
    for rel in sorted(live_files | frozen_files):
        lp, fp = live / rel, frozen / rel
        if rel in deleted:
            ok = rel not in live_files
            print(f"{'DELETED' if ok else 'FAIL still present'} {rel}")
            fails += not ok
            continue
        if rel in new:
            ok = rel in live_files and rel not in frozen_files
            print(f"{'NEW' if ok else 'FAIL expected new'} {rel}")
            fails += not ok
            continue
        if rel not in live_files or rel not in frozen_files:
            print(f"FAIL {'missing' if rel not in live_files else 'unexpected new'} {rel}")
            fails += 1
            continue
        lt, ft = lp.read_text(encoding="utf-8"), fp.read_text(encoding="utf-8")
        if rel in stated:
            print(f"{'STATED (review by eye)' if lt != ft else 'FAIL stated change missing'} {rel}")
            fails += lt == ft
            continue
        if rel in strip:
            if lt == ft:
                print(f"FAIL vacuous: {rel} does not differ from its frozen copy")
                fails += 1
                continue
            try:
                lt = strip[rel](lt)
            except ValueError as e:
                print(f"FAIL {rel}: {e}")
                fails += 1
                continue
        if lt == ft:
            print(f"EQUAL {rel}{' (after strip)' if rel in strip else ''}")
        else:
            print(f"RESIDUE {rel}")
            fails += 1
    print(f"\n{'ALL EQUAL' if fails == 0 else f'{fails} FAILED'} (expect {','.join(sorted(expect))})")
    return fails


def selftest() -> int:
    """Synthesise the intended changes on a temp copy of the frozen baseline; require EQUAL, then RESIDUE."""
    ex = {"A", "B", "C"}

    def specimen(scope: str, label: str) -> str:
        return f'\n    <section class="specimen" {scope}><h2 class="specimen-label" {scope}>{label}</h2><div class="specimen-body " {scope}>x</div></section>'

    def append_specimens(path: pathlib.Path, labels: list[str]):
        t = path.read_text(encoding="utf-8")
        scope = re.search(r'<section class="specimen" (b-[a-z0-9]{10})', t).group(1)
        i = t.rindex("</section>") + len("</section>")
        path.write_text(t[:i] + "".join(specimen(scope, l) for l in labels) + t[i:], encoding="utf-8")

    bad = 0
    with tempfile.TemporaryDirectory() as tmp:
        live = pathlib.Path(tmp) / "live"
        shutil.copytree(FROZEN, live)
        idx = live / "index.html"
        t = idx.read_text(encoding="utf-8")
        m = re.search(rf'<li({A})><a href="/ui/atoms/select-mark"({A})>SelectMark</a></li>', t)
        t = t[:m.end()] + f'<li{m.group(1)}><a href="/ui/atoms/icon"{m.group(2)}>Icon</a></li><li{m.group(1)}><a href="/ui/atoms/icon-button"{m.group(2)}>IconButton</a></li>' + t[m.end():]
        t = t.replace('href="/ui/molecules/membership-status"', 'href="/ui/molecules/photo-actions"').replace(">MembershipStatus</a>", ">PhotoActions</a>")
        idx.write_text(t, encoding="utf-8")
        append_specimens(live / "molecules/tile.html", TILE)
        append_specimens(live / "molecules/drag-handle.html", DRAG)
        append_specimens(live / "molecules/cover-marker.html", COVER)
        gb = live / "organisms/gallery-builder.html"
        gb.write_text(gb.read_text(encoding="utf-8").replace(">Make cover<", "><svg/><"), encoding="utf-8")
        for rel in ["atoms/icon.html", "atoms/icon-button.html", "molecules/photo-actions.html"]:
            (live / rel).write_text("new", encoding="utf-8")
        (live / "molecules/membership-status.html").unlink()

        print("== selftest 1: intended edits only (must be ALL EQUAL)")
        if compare(live, FROZEN, ex) != 0:
            print("SELFTEST FAIL: intended edits were not all stripped"); bad += 1

        print("\n== selftest 2: + a stray edit to atoms/button.html (must be RESIDUE)")
        b = live / "atoms/button.html"
        orig = b.read_text(encoding="utf-8")
        b.write_text(re.sub(r'(<h2 class="specimen-label"[^>]*>)', r'\1x', orig, count=1), encoding="utf-8")
        n = compare(live, FROZEN, ex)
        if n != 1:
            print(f"SELFTEST FAIL: expected exactly 1 failure, got {n}"); bad += 1
        b.write_text(orig, encoding="utf-8")

        print("\n== selftest 3: + a stray edit inside an EARLIER tile specimen (must be RESIDUE)")
        tp = live / "molecules/tile.html"
        orig = tp.read_text(encoding="utf-8")
        tp.write_text(re.sub(r'(<h2 class="specimen-label"[^>]*>)', r'\1x', orig, count=1), encoding="utf-8")
        n = compare(live, FROZEN, ex)
        if n != 1:
            print(f"SELFTEST FAIL: expected exactly 1 failure, got {n}"); bad += 1
        tp.write_text(orig, encoding="utf-8")

        print("\n== selftest 4: stage A only against the unchanged tree (must FAIL as vacuous)")
        n = compare(FROZEN, FROZEN, {"A"})
        if n == 0:
            print("SELFTEST FAIL: an unchanged tree passed stage A"); bad += 1

    print(f"\n{'SELFTEST ALL OK' if bad == 0 else f'SELFTEST {bad} FAILED'}")
    return bad


if __name__ == "__main__":
    if "--selftest" in sys.argv:
        sys.exit(selftest())
    expect = {"A", "B", "C"}
    if "--expect" in sys.argv:
        expect = set(sys.argv[sys.argv.index("--expect") + 1].split(","))
    sys.exit(compare(LIVE, FROZEN, expect))
