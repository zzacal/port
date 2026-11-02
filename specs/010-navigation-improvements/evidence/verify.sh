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
others=$(cssfiles | grep -v 'Atoms/TextLink.razor.css' | xargs grep -nE 'min-height: *44px|line-height: *44px' || true)
chk "UI-2 no min-height/line-height 44px outside TextLink.razor.css" '[ -z "$others" ]'
[ -n "$others" ] && echo "$others"
chk "K1 TextLink renders text-link tap only when Tap" 'grep -q "\"text-link tap\" : \"text-link\"" $S/Components/Atoms/TextLink.razor'

# ── K2 Pager ──────────────────────────────────────────────────────────────────
chk "K2 Pager links pass Tap=\"true\" (2)" '[ "$(grep -c "Tap=\"true\"" $S/Components/Molecules/Pager.razor)" = 2 ]'
chk "K2 PagerStack enum has None, Narrow, Always" 'grep -qE "None,? *Narrow,? *Always|None.*Narrow.*Always" <(tr -d "\n" < $S/Components/Molecules/PagerStack.cs)'
chk "K2 stack breakpoint 599.98px" 'grep -q "max-width: 599.98px" $S/Components/Molecules/Pager.razor.css'

# ── K3/K5/K8 molecules call TextLink, never a raw anchor ──────────────────────
for c in GalleryLink GalleryNeighbours MembershipStatus; do
  f=$S/Components/Molecules/$c.razor
  chk "K $c exists and uses the TextLink atom" '[ -f "$f" ] && grep -q "<TextLink" "$f"'
  chk "K $c has no raw <a element" '[ -f "$f" ] && ! grep -q "<a " "$f"'
done
chk "K3 GalleryLink budgets 26/60" 'grep -q "NarrowChars = 26" $S/Components/Molecules/GalleryLink.razor && grep -q "WideChars = 60" $S/Components/Molecules/GalleryLink.razor'

# ── K6/K7 header and key handler ──────────────────────────────────────────────
L=$S/Components/Templates/PublicLayout.razor
chk "K7 PublicLayout passes no nav items (no NavItem)" '! grep -q "NavItem" $L'
chk "K7 PublicLayout renders <SiteHeader SiteName=\"Gallery\" />" 'grep -q "<SiteHeader SiteName=\"Gallery\" />" $L'
kl=$(grep -n "addEventListener('keydown'" $L | head -1 | cut -d: -f1); sl=$(grep -n "navigator.connection?.saveData" $L | head -1 | cut -d: -f1)
chk "K7 key handler (line ${kl:-none}) precedes the Save-Data return (line ${sl:-none})" '[ -n "$kl" ] && [ -n "$sl" ] && [ "$kl" -lt "$sl" ]'
chk "K7 exactly one keydown listener in src" '[ "$(srcfiles | xargs grep -l "keydown" | wc -l | tr -d " ")" = 1 ]'
chk "K6 SiteHeader still renders <Nav for non-empty items" 'grep -q "<Nav Items" $S/Components/Organisms/SiteHeader.razor'
chk "K6 SiteHeader NavItems not EditorRequired" '! grep -B2 "NavItems" $S/Components/Organisms/SiteHeader.razor | grep -q EditorRequired'
chk "UI-4 no aria-keyshortcuts anywhere in src" '! srcfiles | xargs grep -l "aria-keyshortcuts" >/dev/null'

# ── catalogue ─────────────────────────────────────────────────────────────────
C=$S/Ui/UiCatalog.cs
chk "Catalogue has 34 entries" '[ "$(grep -cE "^\s+new\(UiLevel\." $C)" = 34 ]'
for slug in molecules/gallery-link molecules/gallery-neighbours molecules/membership-status; do
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
new="$S/Components/Molecules/GalleryLink.razor $S/Components/Molecules/GalleryNeighbours.razor $S/Components/Molecules/MembershipStatus.razor $S/Components/Molecules/Pager.razor $S/Components/Atoms/TextLink.razor"
chk "no style= attribute in 010 components" '! grep -l "style=" $new >/dev/null 2>&1'
chk "no <script in 010 components" '! grep -l "<script" $new >/dev/null 2>&1'
chk "public pages keep one inline module (one <script type=\"module\"> in PublicLayout)" '[ "$(grep -c "<script type=\"module\">" $L)" = 1 ]'

echo
[ "$fails" -eq 0 ] && echo "ALL PASS" || echo "$fails FAILED"
exit "$fails"
