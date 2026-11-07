#!/usr/bin/env python3
"""016 PARENT-FIX: the builder grid wrapper is `b-sheet`, not `b-grid` (the section already carries
b-grid). Count-asserting edits across source, the new test and the 016 docs. Run from repo root."""
import pathlib, sys
E = [
    ("src/Gallery.Web/Components/Organisms/GalleryBuilder.razor", '<div class="b-grid">', '<div class="b-sheet">', 1),
    ("src/Gallery.Web/Components/Organisms/GalleryBuilder.razor.css",
     "narrower than that up to about 1440 px, so it shows 2 columns. div.b-grid is the wrapper round\n   the ContactSheet (inside reorder-grid); the section keeps its own .b-grid rule above. */\ndiv.b-grid { container-type: inline-size; }",
     "narrower than that up to about 1440 px, so it shows 2 columns. .b-sheet is the wrapper round the\n   ContactSheet (inside reorder-grid), because a container query reads an ancestor's width. */\n.b-sheet { container-type: inline-size; }", 1),
    ("src/Gallery.Web/Components/Organisms/GalleryBuilder.razor.css", "        div.b-grid ::deep .cs {", "        .b-sheet ::deep .cs {", 1),
    ("src/Gallery.Web/Admin/Pages/LibraryPage.razor.css",
     "with the side panel open the grid\n   is narrower than that up to about 1440 px, so it shows 2 columns.",
     "with the side panel open the grid\n   is narrower than that from 1200 to about 1250 px, so it shows 2 columns there.", 1),
    ("tests/Gallery.Web.Tests/GridWidthBrowserTests.cs", '".builder div.b-grid"', '".builder .b-sheet"', 1),
    ("tests/Gallery.Web.Tests/GridWidthBrowserTests.cs", "cs.parentElement.matches('div.b-grid')", "cs.parentElement.matches('div.b-sheet')", 1),
    ("tests/Gallery.Web.Tests/GridWidthBrowserTests.cs", "does not hold div.b-grid > .cs", "does not hold div.b-sheet > .cs", 1),
    ("specs/016-galleries-list-icons/tasks.md", "`gal-band`, `lib-grid`, `b-grid`;", "`gal-band`, `lib-grid`, `b-sheet` (PARENT-FIX: was `b-grid`, which the builder's section already carries);", 1),
    ("specs/016-galleries-list-icons/tasks.md", "with `b-grid` and `991.98px`.", "with `b-sheet` (PARENT-FIX, was `b-grid`) and `991.98px`.", 1),
    ("specs/016-galleries-list-icons/contracts/components.md", "(class `lib-grid` and `b-grid`).", "(class `lib-grid` and `b-sheet`; AMENDED at implement: `b-grid` is already the builder section's class).", 1),
    ("specs/016-galleries-list-icons/contracts/test-impact.md", "the `b-grid` wrapper `div`.", "the `b-sheet` wrapper `div` (AMENDED at implement, was `b-grid`).", 1),
]
texts, bad = {}, 0
for f, old, new, n in E:
    t = texts.get(f) or pathlib.Path(f).read_text(encoding="utf-8")
    got = t.count(old)
    if got != n:
        print(f"FAIL {f} x{got}: {old[:60]!r}"); bad += 1; continue
    texts[f] = t.replace(old, new); print(f"OK {f}: {old[:50]!r}")
if bad: print("aborted"); sys.exit(1)
for f, t in texts.items(): pathlib.Path(f).write_text(t, encoding="utf-8")
print(f"wrote {len(texts)} files")
