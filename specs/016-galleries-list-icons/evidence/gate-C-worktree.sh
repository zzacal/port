#!/usr/bin/env bash
# Gate Batch C alone in a throwaway worktree at HEAD + C's files (Batch B is editing the main checkout).
set -u
R=/Users/zzacal/repos/gallery
W=$(cd /tmp && pwd -P)/016-gate-C
git -C $R worktree remove --force $W 2>/dev/null
git -C $R worktree add --detach $W HEAD -q
for f in src/Gallery.Web/Admin/Pages/LibraryPage.razor src/Gallery.Web/Admin/Pages/LibraryPage.razor.css \
  src/Gallery.Web/Components/Organisms/GalleryBuilder.razor src/Gallery.Web/Components/Organisms/GalleryBuilder.razor.css \
  tests/Gallery.Web.Tests/GridWidthBrowserTests.cs; do cp $R/$f $W/$f; done
cd $W
B=specs/006-public-site-local/evidence/ui-baseline
rm -f $B/organisms/gallery-builder.html
O=/tmp/016-gate-C.txt
dotnet build Gallery.slnx -warnaserror --artifacts-path artifacts -nologo -v q > $O 2>&1; echo "build exit $?" >> $O
dotnet test Gallery.slnx --no-build --artifacts-path artifacts --blame-hang-timeout 120s --filter "FullyQualifiedName~UiPagesUnchangedTests" >> /tmp/016-gate-C-regen.txt 2>&1
cp $B/organisms/gallery-builder.html $R/$B/organisms/gallery-builder.html
cp $R/specs/016-galleries-list-icons/evidence/baseline-diff.py specs/016-galleries-list-icons/evidence/baseline-diff.py
uv run --no-project python specs/016-galleries-list-icons/evidence/baseline-diff.py --expect A,C > $R/specs/016-galleries-list-icons/evidence/baseline-diff-C.txt 2>&1; echo "baseline-diff exit $?" >> $O
dotnet test Gallery.slnx --no-build --artifacts-path artifacts --blame-hang-timeout 120s >> $O 2>&1; echo "test exit $?" >> $O
grep -E '^(Passed!|Failed!)|^\s+Failed |exit' $O
cd $R && git worktree remove --force $W
