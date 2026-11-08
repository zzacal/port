#!/usr/bin/env python3
"""016 load-race fix: every post-navigation WaitForLoadStateAsync(LoadState.Load) in the browser
tests becomes BrowserWait.LoadedAsync (count-asserting; aborts with nothing written on a mismatch).
Run from the repo root."""
import pathlib, sys
T = pathlib.Path("tests/Gallery.Web.Tests")
W = "await page.WaitForLoadStateAsync(LoadState.Load, new() { Timeout = WaitMs });"
E = [
    ("GalleryListBrowserTests.cs", W, "await BrowserWait.LoadedAsync(page, WaitMs);", 1),
    ("GalleryFieldBrowserTests.cs", W, "await BrowserWait.LoadedAsync(page, WaitMs);", 1),
    ("BuilderIconBrowserTests.cs", W, "await BrowserWait.LoadedAsync(page, WaitMs);", 1),
    ("LibraryBrowserTests.cs", W, "await BrowserWait.LoadedAsync(page, WaitMs);", 1),
    ("TileIconBrowserTests.cs", W, "await BrowserWait.LoadedAsync(page, WaitMs);", 1),
    ("HomeBrowserTests.cs", "await page.WaitForLoadStateAsync(LoadState.Load);", "await BrowserWait.LoadedAsync(page);", 1),
    ("NavBrowserTests.cs", "await page.WaitForLoadStateAsync(LoadState.Load);", "await BrowserWait.LoadedAsync(page);", 1),
]
texts, bad = {}, 0
for f, old, new, n in E:
    p = T / f
    t = texts.get(p) or p.read_text(encoding="utf-8")
    got = t.count(old)
    if got != n:
        print(f"FAIL {f} x{got}: {old}"); bad += 1; continue
    t = t.replace(old, new)
    if "using Gallery.Web.Tests.Fixtures;" not in t:
        t = "using Gallery.Web.Tests.Fixtures;\n" + t
    texts[p] = t
    print(f"OK {f} x{got}")
if bad:
    print("aborted, nothing written"); sys.exit(1)
for p, t in texts.items():
    p.write_text(t, encoding="utf-8")
print(f"wrote {len(texts)} files")
