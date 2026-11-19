#!/bin/bash
# 020: every test line that names an old admin address (contracts/test-impact.md, research R-12).
# Run from the repo root: bash specs/020-admin-workspace/evidence/test-impact-hits.sh
set -u
T=tests
g() { grep -rn --include='*.cs' --exclude-dir=bin --exclude-dir=obj -F -- "$1" "$T" | sed -E 's/^([^:]+:[0-9]+):.*/\1/'; }
section() {
  local label="$1"; shift
  local hits; hits=$(for p in "$@"; do g "$p"; done | sort -u)
  local n=0; [ -n "$hits" ] && n=$(printf '%s\n' "$hits" | wc -l | tr -d ' ')
  local f=0; [ -n "$hits" ] && f=$(printf '%s\n' "$hits" | cut -d: -f1 | sort -u | wc -l | tr -d ' ')
  echo "== $label: $n lines in $f files"
  [ -n "$hits" ] && printf '%s\n' "$hits" | awk -F: '{a[$1]=a[$1]" "$2} END {for (k in a) print k":"a[k]}' | sort
  echo
}
section "upload page (U1)"            '"/admin/upload"' '/admin/upload?'
section "confirm (U10)"               '/admin/upload/confirm'
section "discard (U11)"               '/admin/upload/discard'
section "upload item API (kept)"      '/admin/upload/items' '/admin/upload/state'
section "library page (L1)"           '"/admin/library"' '/admin/library?'
section "photo edit / save (L2, L5, L6)" '/admin/library/images/'
section "bulk add (L3)"               '/admin/library/add'
section "bulk delete (L4)"            '/admin/library/delete'
section "galleries (G1 to G4, B1 to B6)" '/admin/galleries'
section "shell (H4)"                  '"/admin"'
section "catalogue (/ui)"             '"/ui' '/ui/'
echo "== components being removed, named in tests"
for c in OwnerBar SelectionBar PhotoActions GalleryActions GalleryBuilder BatchFooter BatchItem DragHandle CoverMarker TokenCombobox VisibilityToggle IconButton 'Icon>' 'Icons.' LibraryPage PhotoEditPage GalleriesPage 'GalleryPage' UploadPage ShellPage; do
  n=$(grep -rl --include='*.cs' --exclude-dir=bin --exclude-dir=obj -F -- "$c" "$T" | wc -l | tr -d ' ')
  echo "$c: $n files"
done
