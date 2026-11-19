#!/bin/bash
# 020 plan research: read-only code reading behind research.md. Run from the repo root.
g() { grep -rn --include='*.cs' --include='*.razor' --include='*.js' --exclude-dir=bin --exclude-dir=obj --exclude-dir=vendor "$@"; }
echo "== service public signatures"
for f in GalleryService ImageService ImageDeleteService LibraryReader MembershipEdit OperationResult SiteProjector PageCacheEvictor; do
  echo "-- $f"; grep -n -E '^\s*public (static |sealed |async |record |enum |const )*[A-Za-z<>\[\]?,\(\) ]+ [A-Za-z]+\(|^\s*public (sealed |static )?(record|class|enum)|public const' src/Gallery.Web/Library/$f.cs | cut -c1-230
done
echo "== BatchStore"
grep -n -E 'public .*\(' src/Gallery.Web/Upload/BatchStore.cs | cut -c1-200
echo "== BatchDocuments"
grep -v '^\s*///' src/Gallery.Web/Data/BatchDocuments.cs | grep -v '^\s*$' | cut -c1-160
echo "== Documents (image/gallery/site)"
grep -n -E 'class |public .* \{ get' src/Gallery.Web/Data/Documents.cs | cut -c1-140
echo "== Indexes"
grep -n -E 'CreateIndex|IndexKeys|Unique|Partial|Name =' src/Gallery.Web/Data/Indexes.cs | cut -c1-200
echo "== UploadEndpoints handlers + op names"
grep -n -E 'private static (async )?Task|RequestSizeLimit|_timer.Time\("|timer.Time\("|LogInformation\("' src/Gallery.Web/Upload/UploadEndpoints.cs | cut -c1-200
echo "== timing op names in Library"
g -o -h -E 'Time\("[a-z.\-]+"' src/Gallery.Web/Library src/Gallery.Web/Data src/Gallery.Web/Site | sort | uniq -c
echo "== LibrarySetup / UploadSetup / Program"
cat src/Gallery.Web/Library/LibrarySetup.cs src/Gallery.Web/Upload/UploadSetup.cs src/Gallery.Web/Program.cs | grep -v '^\s*///' | grep -v '^\s*$'
echo "== AdminAssets"
grep -v '^\s*///' src/Gallery.Web/Upload/AdminAssets.cs | grep -v '^\s*$'
echo "== vendor"
ls src/Gallery.Web/wwwroot/admin-assets/vendor
grep -rn -i 'htmx' src/Gallery.Web --include='*.razor' --include='*.cs' --include='*.js' -l --exclude-dir=bin --exclude-dir=obj | head
echo "== UiEndpoints"
grep -v '^\s*///' src/Gallery.Web/Ui/UiEndpoints.cs | grep -v '^\s*$'
echo "== upload.js top comment + function names"
sed -n 1,40p src/Gallery.Web/wwwroot/admin-assets/upload.js | cut -c1-170
grep -n -E '^\s*(async )?function |^\s*const [a-zA-Z]+ = (async )?\(' src/Gallery.Web/wwwroot/admin-assets/upload.js | cut -c1-120
echo "== reorder.js header"
sed -n 1,22p src/Gallery.Web/wwwroot/admin-assets/reorder.js | cut -c1-170
T=tests/Gallery.Web.Tests
echo "== service methods"
for f in Library/GalleryService Library/ImageService Library/ImageDeleteService Library/LibraryReader Library/SiteProjector; do
  echo "-- $f"; grep -n -E '^\s*(public|internal) .*(Task|IReadOnly|static).*\(' src/Gallery.Web/$f.cs | cut -c1-240
done
echo "== OperationScope"
sed -n 55,130p src/Gallery.Web/Library/OperationResult.cs | grep -v '^\s*$' | cut -c1-200
grep -n -E 'public .*\(' src/Gallery.Web/Library/OperationResult.cs | sed -n 1,60p | cut -c1-220
echo "== LibraryReader.PageAsync"
sed -n 37,100p src/Gallery.Web/Library/LibraryReader.cs | cut -c1-200
echo "== test files: facts per file, and which admin surface they hit"
for f in $T/*.cs; do
  n=$(grep -c -E '\[(Fact|Theory)' "$f")
  tags=""
  grep -q -E '/admin/upload' "$f" && tags="$tags upload"
  grep -q -E '/admin/library' "$f" && tags="$tags library"
  grep -q -E '/admin/galleries' "$f" && tags="$tags galleries"
  grep -q -E '"/ui|/ui/' "$f" && tags="$tags ui"
  grep -q -E 'Render\.Html<|HtmlRenderer' "$f" && tags="$tags render"
  grep -q -E 'IPage|Playwright' "$f" && tags="$tags browser"
  grep -q -E 'File\.ReadAllText|Directory\.(Get|Enumerate)Files' "$f" && tags="$tags scan"
  [ -n "$tags" ] && echo "$(basename $f) [$n]$tags"
done
echo "== fixtures"
ls $T/Fixtures
echo "== total facts"
grep -r -c -E '\[(Fact|Theory)' $T/*.cs | awk -F: '{s+=$2} END {print s}'
echo "== UI snapshot tests"
grep -n -E 'baseline|Baseline|Count|Entries' $T/UiPagesUnchangedTests.cs | head -30 | cut -c1-200
grep -rn -l -E 'UiCatalog\.Entries|ui-baseline' $T/*.cs
echo "== public output unchanged test"
sed -n 1,40p $T/PublicOutputUnchangedTests.cs | cut -c1-200
echo "== layout consistency tests (admin)"
ls $T | grep -i -E 'consisten|chrome|nav'
grep -n -E 'admin/|Fact|InlineData' $T/AdminNavBrowserTests.cs | head -30 | cut -c1-200
echo "== admin css guard / script count guards"
grep -rn -E 'script.*Count|<script' $T/*.cs | grep -i -E 'Assert|Count' | head -12 | cut -c1-200
T=tests/Gallery.Web.Tests
echo "== script source guards"
grep -n -E 'innerHTML|outerHTML|insertAdjacentHTML|DOMParser|document\.write|eval|Banned|Forbidden|new\[\]|\"[a-zA-Z.]+\(?\"' $T/UploadSourceGuardTests.cs $T/LibrarySourceGuardTests.cs | cut -c1-220 | head -50
echo "== antiforgery filter"
grep -v '^\s*///' src/Gallery.Web/Admin/AdminAntiforgeryFilter.cs | grep -v '^\s*$' | head -40
echo "== GalleriesAsync"
sed -n 139,173p src/Gallery.Web/Library/LibraryReader.cs
echo "== LibraryTile / tile builder"
sed -n 12,36p src/Gallery.Web/Library/LibraryReader.cs; sed -n 100,138p src/Gallery.Web/Library/LibraryReader.cs
echo "== IEdgeCache"
grep -rn --include='*.cs' --exclude-dir=bin --exclude-dir=obj -E 'interface IEdgeCache|void Enqueue|class .*: IEdgeCache|Channel|record EdgeWork' src/Gallery.Web | cut -c1-220
echo "== LibraryHost helpers"
grep -n -E 'public .*\(' $T/Fixtures/LibraryHost.cs | cut -c1-200
grep -n -E 'public .*\(' $T/Fixtures/UploadHost.cs | cut -c1-200 | head -30
grep -n -E 'public .*\(' $T/Fixtures/LibraryBrowserFixture.cs $T/Fixtures/UploadBrowserFixture.cs | cut -c1-200 | head -30
echo "== how tests post (literal route usage counts)"
for r in '/admin/library/add' '/admin/library/delete' '/admin/library/images/' '/admin/galleries/order' '/visibility' '/cover' '/remove' '/order' '/admin/upload/confirm' '/admin/upload/discard' '/admin/upload/items' '/admin/upload/state' '"/admin/upload' '"/admin/library' '"/admin/galleries' '"/admin"'; do
  echo "$r: $(grep -r --include='*.cs' --exclude-dir=bin --exclude-dir=obj -F -- "$r" tests | wc -l | tr -d ' ') hits in $(grep -rl --include='*.cs' --exclude-dir=bin --exclude-dir=obj -F -- "$r" tests | wc -l | tr -d ' ') files"
done
echo "== component params"
for c in Molecules/VisibilityToggle Molecules/PageHeading Atoms/Input Atoms/SelectMark Organisms/UploadDropzone Molecules/DangerConfirm Molecules/Tile Organisms/ContactSheet Atoms/Picture; do
  echo "-- $c"; grep -n -E '\[Parameter' src/Gallery.Web/Components/$c.razor | sed -E 's/^\s+//' | cut -c1-170
done
echo "== Picture srcset shape"
grep -n -E 'srcset|sizes' src/Gallery.Web/Components/Atoms/Picture.razor | head -8 | cut -c1-200
echo "== upload.js reconcile"
sed -n 522,565p src/Gallery.Web/wwwroot/admin-assets/upload.js
echo "== ComponentTypes enums"
grep -n -E 'enum ' src/Gallery.Web/Components/ComponentTypes.cs
echo "== p95 scripts / evidence helpers"
ls specs/017-photo-edit-screen/evidence specs/019-selection-bar-layering/evidence | head -40
echo "== source guard for removed? owner write guard"
sed -n 1,30p $T/OwnerWriteGuardTests.cs | cut -c1-180
