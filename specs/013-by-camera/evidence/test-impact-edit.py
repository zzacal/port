#!/usr/bin/env python3
"""013 parent stated change, contracts/test-impact.md class (A). Applied after Batch B.

Each row asserts its old string's occurrence count before anything is written; any mismatch
aborts with no file touched. Output (tee'd to test-impact-edit.txt) is the old -> new record that
decisions.md cites. Run from the repo root:
    python3 specs/013-by-camera/evidence/test-impact-edit.py | tee specs/013-by-camera/evidence/test-impact-edit.txt
"""
import os
import pathlib
import sys

T = pathlib.Path("tests/Gallery.Web.Tests")

OLD_RULE = r'''        "{\"prerender\":[{\"where\":{\"or\":[{\"href_matches\":\"/g\"},{\"href_matches\":\"/g/*\"}]},\"eagerness\":\"moderate\"}]}";'''
NEW_RULE = r'''        "{\"prerender\":[{\"where\":{\"or\":[{\"href_matches\":\"/g\"},{\"href_matches\":\"/g/*\"},{\"href_matches\":\"/c\"},{\"href_matches\":\"/c/*\"}]},\"eagerness\":\"moderate\"}]}";'''

OLD_ANCHORS = '''        Assert.Single(AnchorRx().Matches(nav));
        Assert.Single(GalleriesLinkRx().Matches(nav));
'''
NEW_ANCHORS = '''        // 013 stated change (test-impact A): exactly two links, Galleries then By Camera.
        var anchors = AnchorRx().Matches(nav);
        Assert.Equal(2, anchors.Count);
        Assert.Single(GalleriesLinkRx().Matches(nav));
        Assert.Single(CamerasLinkRx().Matches(nav));
        Assert.True(GalleriesLinkRx().Match(nav).Index < CamerasLinkRx().Match(nav).Index, "Galleries must come before By Camera");
'''

OLD_RX = '''    [GeneratedRegex("<a href=\\"/g\\"(?: b-[a-z0-9]{10})?>Galleries</a>")]
    private static partial Regex GalleriesLinkRx();
'''
NEW_RX = OLD_RX + '''
    [GeneratedRegex("<a href=\\"/c\\"(?: b-[a-z0-9]{10})?>By Camera</a>")]
    private static partial Regex CamerasLinkRx();
'''

OLD_GEOM = '''        var count = await page.EvaluateAsync<int>("document.querySelectorAll('nav.nav--home a').length");
        if (count != 1)
        {
            failures.Add($"{at}: expected one 'nav.nav--home a', found {count}");
        }
        else
        {
            var m = await page.EvaluateAsync<double[]>("""
                (() => {
                    const a = document.querySelector('nav.nav--home a'), r = a.getBoundingClientRect();
'''
NEW_GEOM = '''        // 013 stated change (test-impact A): two links. Centring (a, b) is measured on the link
        // list as a whole; (d) and (e) on each link; (c) unchanged.
        var count = await page.EvaluateAsync<int>("document.querySelectorAll('nav.nav--home a').length");
        if (count != 2)
        {
            failures.Add($"{at}: expected two 'nav.nav--home a', found {count}");
        }
        else
        {
            foreach (var i in new[] { 0, 1 })
            {
                var l = await page.EvaluateAsync<double[]>($$"""
                    (() => {
                        const a = document.querySelectorAll('nav.nav--home a')[{{i}}], r = a.getBoundingClientRect();
                        return [r.top, r.bottom, parseFloat(getComputedStyle(a).fontSize)];
                    })()
                    """);
                if (l[2] != 48)
                    failures.Add($"{at}: (d) link {i} font-size {F(l[2])}px, expected 48px");
                if (l[1] - l[0] < 44)
                    failures.Add($"{at}: (e) link {i} height {F(l[1] - l[0])} < 44");
            }

            var m = await page.EvaluateAsync<double[]>("""
                (() => {
                    const a = document.querySelector('nav.nav--home ul'), r = a.getBoundingClientRect();
'''

OLD_TRAIL_RX = '''    private static readonly Regex Literal = new("\\"(Home|Galleries)\\"", RegexOptions.Compiled);'''
NEW_TRAIL_RX = '''    private static readonly Regex Literal = new("\\"(Home|Galleries|By Camera)\\"", RegexOptions.Compiled);'''
OLD_TRAIL_N = '''        Assert.Equal(2, inTrails);'''
NEW_TRAIL_N = '''        Assert.Equal(3, inTrails);'''

# (file, old, new, expected count, class)
EDITS = [
    ("HomePageTests.cs", OLD_RULE, NEW_RULE, 1, "A"),
    ("HomePageTests.cs", OLD_RX, NEW_RX, 1, "A"),
    ("HomePageTests.cs", OLD_ANCHORS, NEW_ANCHORS, 1, "A"),
    ("HomeBrowserTests.cs", OLD_GEOM, NEW_GEOM, 1, "A"),
    ("TrailSourceGuardTests.cs", OLD_TRAIL_RX, NEW_TRAIL_RX, 1, "A"),
    ("TrailSourceGuardTests.cs", OLD_TRAIL_N, NEW_TRAIL_N, 1, "A"),
]


def main() -> int:
    texts, failures = {}, []
    for name, old, new, want, cls in EDITS:
        text = texts.get(name) or (T / name).read_text(encoding="utf-8")
        got = text.count(old)
        if got != want:
            failures.append(f"{name}: {old.strip()[:70]!r} occurs {got}x, expected {want}")
            continue
        texts[name] = text.replace(old, new)
        print(f"OK {cls} {name} x{got}: {old.strip().splitlines()[0][:80]!r} -> {new.strip().splitlines()[0][:80]!r}")
    if failures:
        print("\n".join("FAIL " + f for f in failures))
        return 1
    if os.environ.get("DRYRUN"):
        print("dry run: nothing written")
        return 0
    for name, text in texts.items():
        (T / name).write_text(text, encoding="utf-8")
    print(f"wrote {len(texts)} files")
    return 0


if __name__ == "__main__":
    sys.exit(main())
