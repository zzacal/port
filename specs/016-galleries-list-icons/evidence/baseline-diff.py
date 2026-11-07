#!/usr/bin/env python3
"""016 T003: verify-before-replace for the /ui byte-identity baselines (contracts/test-impact.md B).

Compares the live baselines (specs/006-public-site-local/evidence/ui-baseline/) with the frozen
pre-016 copy (specs/016-galleries-list-icons/evidence/pre-016-ui-baseline/). For each file the
feature changes, it strips EXACTLY the intended change and requires the rest to equal the frozen
copy; every other file must be byte-identical (B5).

    uv run --no-project python specs/016-galleries-list-icons/evidence/baseline-diff.py [--expect A,C]
    uv run --no-project python specs/016-galleries-list-icons/evidence/baseline-diff.py --selftest

--expect names the batches whose baseline changes have landed (default A,C):
  A  B1 index.html: + one "Gallery actions" <li> directly after VisibilityToggle;
     B2 molecules/gallery-actions.html new (shown, not diffed);
     B3 atoms/icon.html: + eye and eye-off figures in both icon grids (2 each);
        atoms/icon-button.html: + eye and eye-off buttons in "Every icon" (1 each)
  C  B4 organisms/gallery-builder.html: cs-m3 -> cs-m2 and the sizes medium term
        calc((100vw - 128px) / 3) -> calc((100vw - 96px) / 2), on every tile
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
FROZEN = ROOT / "specs/016-galleries-list-icons/evidence/pre-016-ui-baseline"

A = r'(?: b-[a-z0-9]{10})*'  # zero or more scope attributes
NEW_ICONS = ["eye", "eye-off"]
SIZES_OLD, SIZES_NEW = "calc((100vw - 128px) / 3)", "calc((100vw - 96px) / 2)"


def li(slug: str, name: str) -> str:
    return rf'<li{A}><a href="/ui/{re.escape(slug)}"{A}>{re.escape(name)}</a></li>'


def strip_index(text: str) -> str:
    vis = re.search(li("molecules/visibility-toggle", "VisibilityToggle"), text)
    if not vis:
        raise ValueError("VisibilityToggle <li> not found")
    m = re.compile(li("molecules/gallery-actions", "Gallery actions")).match(text, vis.end())
    if not m:
        raise ValueError("Gallery actions <li> not directly after VisibilityToggle")
    text = text[:m.start()] + text[m.end():]
    if "/ui/molecules/gallery-actions" in text:
        raise ValueError("a second gallery-actions link")
    return text


def strip_icon(text: str) -> str:
    for name in NEW_ICONS:
        pat = rf'<figure{A}><svg class="icon" data-icon="{re.escape(name)}"[^>]*>.*?</svg><figcaption{A}>{re.escape(name)}</figcaption></figure>'
        text, n = re.subn(pat, "", text, flags=re.S)
        if n != 2:
            raise ValueError(f"{name} figure x{n} (expected 2)")
    return text


def strip_icon_button(text: str) -> str:
    for name in NEW_ICONS:
        pat = rf'<button class="icon-button" type="button" aria-label="{re.escape(name)}" title="{re.escape(name)}"{A}><svg class="icon" data-icon="{re.escape(name)}"[^>]*>.*?</svg></button>'
        text, n = re.subn(pat, "", text, flags=re.S)
        if n != 1:
            raise ValueError(f"{name} button x{n} (expected 1)")
    return text


def strip_builder(text: str) -> str:
    n1, n2 = text.count("cs-m2"), text.count(SIZES_NEW)
    if n1 == 0 or n2 == 0:
        raise ValueError(f"cs-m2 x{n1}, new sizes x{n2} (expected both > 0)")
    if "cs-m3" in text or SIZES_OLD in text:
        raise ValueError("old cs-m3 or old sizes term still present")
    return text.replace("cs-m2", "cs-m3").replace(SIZES_NEW, SIZES_OLD)


def plan(expect: set[str]):
    """(stripped files: rel -> strip fn), new files."""
    strip, new = {}, set()
    if "A" in expect:
        strip["index.html"] = strip_index
        strip["atoms/icon.html"] = strip_icon
        strip["atoms/icon-button.html"] = strip_icon_button
        new.add("molecules/gallery-actions.html")
    if "C" in expect:
        strip["organisms/gallery-builder.html"] = strip_builder
    return strip, new


def compare(live: pathlib.Path, frozen: pathlib.Path, expect: set[str]) -> int:
    strip, new = plan(expect)
    live_files = {p.relative_to(live).as_posix() for p in live.rglob("*.html")}
    frozen_files = {p.relative_to(frozen).as_posix() for p in frozen.rglob("*.html")}
    fails = 0
    for rel in sorted(live_files | frozen_files):
        lp, fp = live / rel, frozen / rel
        if rel in new:
            ok = rel in live_files and rel not in frozen_files
            print(f"{'NEW (review by eye)' if ok else 'FAIL expected new'} {rel}")
            fails += not ok
            continue
        if rel not in live_files or rel not in frozen_files:
            print(f"FAIL {'missing' if rel not in live_files else 'unexpected new'} {rel}")
            fails += 1
            continue
        lt, ft = lp.read_text(encoding="utf-8"), fp.read_text(encoding="utf-8")
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
    ex = {"A", "C"}
    bad = 0
    with tempfile.TemporaryDirectory() as tmp:
        live = pathlib.Path(tmp) / "live"
        shutil.copytree(FROZEN, live)
        # B1
        idx = live / "index.html"
        t = idx.read_text(encoding="utf-8")
        m = re.search(rf'<li({A})><a href="/ui/molecules/visibility-toggle"({A})>VisibilityToggle</a></li>', t)
        t = t[:m.end()] + f'<li{m.group(1)}><a href="/ui/molecules/gallery-actions"{m.group(2)}>Gallery actions</a></li>' + t[m.end():]
        idx.write_text(t, encoding="utf-8")
        # B3 icon.html: after each image-minus figure
        ip = live / "atoms/icon.html"
        t = ip.read_text(encoding="utf-8")
        def add_figs(mm):
            fig = mm.group(0)
            out = fig
            for name in NEW_ICONS:
                out += re.sub(r'data-icon="image-minus"', f'data-icon="{name}"', fig).replace(">image-minus</figcaption>", f">{name}</figcaption>").replace(
                    re.search(r'<svg[^>]*>(.*?)</svg>', fig, re.S).group(1), f'<path d="{name}" />')
            return out
        t, n = re.subn(rf'<figure{A}><svg class="icon" data-icon="image-minus"[^>]*>.*?</svg><figcaption{A}>image-minus</figcaption></figure>', add_figs, t, flags=re.S)
        assert n == 2, n
        ip.write_text(t, encoding="utf-8")
        # B3 icon-button.html: after the "Every icon" image-minus button
        bp = live / "atoms/icon-button.html"
        t = bp.read_text(encoding="utf-8")
        mm = list(re.finditer(rf'<button class="icon-button" type="button" aria-label="image-minus" title="image-minus"{A}><svg[^>]*>.*?</svg></button>', t, re.S))[-1]
        btn = mm.group(0)
        add = "".join(btn.replace('"image-minus"', f'"{name}"') for name in NEW_ICONS)
        bp.write_text(t[:mm.end()] + add + t[mm.end():], encoding="utf-8")
        # B4
        gb = live / "organisms/gallery-builder.html"
        gb.write_text(gb.read_text(encoding="utf-8").replace("cs-m3", "cs-m2").replace(SIZES_OLD, SIZES_NEW), encoding="utf-8")
        # B2
        (live / "molecules/gallery-actions.html").write_text("new", encoding="utf-8")

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

        print("\n== selftest 3: + a stray edit inside the builder page (must be RESIDUE)")
        orig = gb.read_text(encoding="utf-8")
        gb.write_text(re.sub(r'(<h2 class="specimen-label"[^>]*>)', r'\1x', orig, count=1), encoding="utf-8")
        n = compare(live, FROZEN, ex)
        if n != 1:
            print(f"SELFTEST FAIL: expected exactly 1 failure, got {n}"); bad += 1
        gb.write_text(orig, encoding="utf-8")

        print("\n== selftest 4: + a stray icon edit (folder figure) in atoms/icon.html (must be RESIDUE)")
        orig = ip.read_text(encoding="utf-8")
        ip.write_text(orig.replace(">folder</figcaption>", ">folderx</figcaption>", 1), encoding="utf-8")
        n = compare(live, FROZEN, ex)
        if n != 1:
            print(f"SELFTEST FAIL: expected exactly 1 failure, got {n}"); bad += 1
        ip.write_text(orig, encoding="utf-8")

        print("\n== selftest 5: stages A,C against the unchanged tree (must FAIL as vacuous)")
        n = compare(FROZEN, FROZEN, ex)
        if n == 0:
            print("SELFTEST FAIL: an unchanged tree passed"); bad += 1

    print(f"\n{'SELFTEST ALL OK' if bad == 0 else f'SELFTEST {bad} FAILED'}")
    return bad


if __name__ == "__main__":
    if "--selftest" in sys.argv:
        sys.exit(selftest())
    expect = {"A", "C"}
    if "--expect" in sys.argv:
        expect = set(sys.argv[sys.argv.index("--expect") + 1].split(","))
    sys.exit(compare(LIVE, FROZEN, expect))
