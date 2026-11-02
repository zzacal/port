#!/usr/bin/env bash
# 010 T043: contract-invariant greps over the merged tree, both directions (things that must
# exist and things that must be gone). Run from the repo root:
#   bash specs/010-navigation-improvements/evidence/verify.sh | tee specs/010-navigation-improvements/evidence/verify-output.txt
set -uo pipefail
S=src/Gallery.Web
fails=0
ok()  { echo "PASS $*"; }
bad() { echo "FAIL $*"; fails=$((fails+1)); }
chk() { if eval "$2"; then ok "$1"; else bad "$1"; fi; }
cssfiles() { find "$S" -name '*.razor.css' -not -path '*/bin/*' -not -path '*/obj/*'; }
srcfiles() { find "$S" \( -name '*.razor' -o -name '*.cs' -o -name '*.razor.css' \) -not -path '*/bin/*' -not -path '*/obj/*'; }

# ── K1 one tap rule (UI-2) ────────────────────────────────────────────────────
n=$(cssfiles | xargs grep -l '\.text-link\.tap' | wc -l | tr -d ' ')
chk "K1 the .text-link.tap rule exists in exactly one stylesheet ($n)" '[ "$n" = 1 ] && grep -q "\.text-link\.tap" $S/Components/Atoms/TextLink.razor.css'
others=$(cssfiles | xargs grep -nE '44px' || true)
chk "UI-2 the tap size is the one Tokens.Tap value: no literal 44px in any stylesheet" '[ -z "$others" ] && grep -q "public const int Tap = 44;" src/Gallery.Core/Design/Tokens.cs'
[ -n "$others" ] && echo "$others"
chk "K1 TextLink renders text-link tap only when Tap" 'grep -q "\"text-link tap\" : \"text-link\"" $S/Components/Atoms/TextLink.razor'

# ── K2 Pager ──────────────────────────────────────────────────────────────────
chk "K2 Pager links pass Tap=\"true\" (2)" '[ "$(grep -c "Tap=\"true\"" $S/Components/Molecules/Pager.razor)" = 2 ]'
chk "B4 Pager side tracks are at least max-content (whole words)" 'grep -q "minmax(max-content, 1fr) auto minmax(max-content, 1fr)" $S/Components/Molecules/Pager.razor.css'
chk "B4 no Pager stacking and no PagerStack type left" '! grep -q "pager-stack" $S/Components/Molecules/Pager.razor.css && [ ! -f $S/Components/Molecules/PagerStack.cs ]'
chk "B3 GalleryLink is gone (review 1)" '[ ! -f $S/Components/Molecules/GalleryLink.razor ] && ! srcfiles | xargs grep -l "gallery-link" >/dev/null'

# ── K3/K5/K8 molecules call TextLink, never a raw anchor ──────────────────────
for c in GalleryNeighbours MembershipStatus Breadcrumb; do
  f=$S/Components/Molecules/$c.razor
  chk "K $c exists and uses the TextLink atom" '[ -f "$f" ] && grep -q "<TextLink" "$f"'
  chk "K $c has no raw <a element" '[ -f "$f" ] && ! grep -q "<a " "$f"'
done
chk "B1 Breadcrumb marks the current page" 'grep -q "aria-current=\"page\"" $S/Components/Molecules/Breadcrumb.razor'
chk "B6 OwnerBar has no PrimaryAction and a fixed View site link" '! grep -q "PrimaryAction" $S/Components/Organisms/OwnerBar.razor $S/Components/Templates/AdminLayout.razor && grep -q "View site ↗" $S/Components/Organisms/OwnerBar.razor'
chk "B6 no admin page passes ViewHref to AdminLayout" '! grep -h "<AdminLayout" $S/Admin/Pages/*.razor | grep -q "ViewHref"'
chk "B6 New gallery sits in a PageHeading" 'grep -q "<PageHeading Text=\"Galleries\" Actions=\"@NewGallery\"" $S/Admin/Pages/GalleriesPage.razor'
chk "IX constitution 4.4.0 has principle IX" 'grep -q "^### IX. Visual Consistency Through Shared Containers" .specify/memory/constitution.md && grep -q "Version\*\*: 4.4.0" .specify/memory/constitution.md'
chk "IX.3 breadcrumb row is a fixed tap height" 'grep -q "min-height: var(--tap)" $S/Components/Molecules/Breadcrumb.razor.css'
chk "IX.2 both layouts zero the first content margin" 'grep -q ".public-main > ::deep :first-child" $S/Components/Templates/PublicLayout.razor.css && grep -q ".admin-main > ::deep :first-child" $S/Components/Templates/AdminLayout.razor.css'
chk "IX.2 no public page sets a top margin on its head" '! grep -qE "margin: var\(--space-[sml]\) auto" $S/Site/Pages/GalleryPage.razor.css'
chk "B6 no Back to galleries in admin" '! grep -rq "Back to galleries" $S/Admin $S/Components/Organisms'

# ── K6/K7 header and key handler ──────────────────────────────────────────────
L=$S/Components/Templates/PublicLayout.razor
chk "R1 the public header is the breadcrumb (no brand, no nav)" 'grep -q "<SiteHeader Crumbs=" $L && grep -q "<header class=\"site-header\"><Breadcrumb Items=\"Crumbs\" /></header>" $S/Components/Organisms/SiteHeader.razor && ! grep -qE "<Brand|<Nav " $S/Components/Organisms/SiteHeader.razor'
chk "B2 admin renders the breadcrumb right after the bar" 'grep -q "Bar : null)@CrumbsContent" $S/Components/Templates/AdminLayout.razor'
chk "R3 breadcrumb separator is / and the trail aligns left" 'grep -q "content: \"/\";" $S/Components/Molecules/Breadcrumb.razor.css && grep -q "justify-content: flex-start;" $S/Components/Molecules/Breadcrumb.razor.css'
chk "R2 home and 404 have trails; no Back to galleries on 404" 'grep -q "Crumbs=\"Crumbs\"" $S/Site/Pages/HomePage.razor && grep -qF "new(\"Not found\")" $S/Site/Pages/NotFoundPage.razor && ! grep -q "Back to galleries" $S/Site/Pages/NotFoundPage.razor'
chk "K7 PublicLayout passes no nav items (no NavItem)" '! grep -q "NavItem" $L'
kl=$(grep -n "addEventListener('keydown'" $L | head -1 | cut -d: -f1); sl=$(grep -n "navigator.connection?.saveData" $L | head -1 | cut -d: -f1)
chk "K7 key handler (line ${kl:-none}) precedes the Save-Data return (line ${sl:-none})" '[ -n "$kl" ] && [ -n "$sl" ] && [ "$kl" -lt "$sl" ]'
chk "K7 exactly one keydown listener in src" '[ "$(srcfiles | xargs grep -l "keydown" | wc -l | tr -d " ")" = 1 ]'
chk "K6 SiteHeader NavItems not EditorRequired" '! grep -B2 "NavItems" $S/Components/Organisms/SiteHeader.razor | grep -q EditorRequired'
chk "UI-4 no aria-keyshortcuts anywhere in src" '! srcfiles | xargs grep -l "aria-keyshortcuts" >/dev/null'

# ── catalogue ─────────────────────────────────────────────────────────────────
C=$S/Ui/UiCatalog.cs
chk "Catalogue has 36 entries" '[ "$(grep -cE "^\s+new\(UiLevel\." $C)" = 36 ]'
for slug in molecules/photo-count molecules/gallery-neighbours molecules/membership-status molecules/breadcrumb molecules/page-heading; do
  chk "Catalogue has $slug" 'grep -q "\"$slug\"" $C'
done

# ── projector, purge, [SAFETY] mechanisms ─────────────────────────────────────
P=$S/Library/SiteProjector.cs
chk "ProjectAsync still exists (signature kept)" 'grep -qE "public (async )?Task<IReadOnlyList<GalleryCardDoc>> ProjectAsync\(\)" $P'
chk "ProjectWithNavAsync exists" 'grep -q "ProjectWithNavAsync" $P'
chk "[SAFETY] SiteProjector.Nav calls Cards(" 'awk "/static Dictionary<string, GalleryNavDoc\\?> Nav\\(/,/^    }/" $P | grep -q "Cards("'
chk "SiteProjector.Nav calls GalleryNav.Neighbours" 'grep -q "GalleryNav.Neighbours" $P'
chk "PurgeSets.Nav is called from OperationResult.cs" 'grep -q "PurgeSets.Nav(" $S/Library/OperationResult.cs'
chk "PurgeSets.Nav defined" 'grep -q "public static PurgeSet Nav(" $S/Edge/PurgeSets.cs'
E=$S/Site/PublicEndpoints.cs
chk "[SAFETY] gallery route checks Visibility == \"public\" before using Nav" 'grep -qE "Visibility == \"public\".*Nav|Visibility == \"public\" && [a-z]+\.Nav" $E'
chk "[SAFETY] NavSourceGuardTests present" '[ -f tests/Gallery.Web.Tests/NavSourceGuardTests.cs ]'
nw=$(find "$S" -name '*.cs' -not -path '*/bin/*' -not -path '*/obj/*' | grep -vE 'Library/SiteProjector.cs|Data/Documents.cs' | xargs grep -nE '"nav"|Update\.(Set|Unset)\([^)]*\.Nav\b|\bNav = ' || true)
chk "[SAFETY] no nav write outside SiteProjector/Documents" '[ -z "$nw" ]'
[ -n "$nw" ] && echo "$nw"

# ── hygiene over the 010 components ───────────────────────────────────────────
new="$S/Components/Molecules/Breadcrumb.razor $S/Components/Molecules/PhotoCount.razor $S/Components/Molecules/PageHeading.razor $S/Components/Molecules/GalleryNeighbours.razor $S/Components/Molecules/MembershipStatus.razor $S/Components/Molecules/Pager.razor $S/Components/Atoms/TextLink.razor"
chk "no style= attribute in 010 components" '! grep -l "style=" $new >/dev/null 2>&1'
chk "no <script in 010 components" '! grep -l "<script" $new >/dev/null 2>&1'
chk "public pages keep one inline module (one <script type=\"module\"> in PublicLayout)" '[ "$(grep -c "<script type=\"module\">" $L)" = 1 ]'

echo
[ "$fails" -eq 0 ] && echo "ALL PASS" || echo "$fails FAILED"
exit "$fails"
