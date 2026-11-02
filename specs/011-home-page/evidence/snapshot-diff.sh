#!/usr/bin/env bash
# 011 T021: compare the regenerated snapshots with the frozen pre-011 copies after stripping
# EXACTLY the intended changes (contracts/test-impact.md "Snapshots"). Every page must be SAME.
# Normalisation (as 010's snapshot-diff-review2.sh): scoped ids stripped, <style> blanked,
# whitespace collapsed. Intended strips, applied to the NEW file only, each scoped to its pages:
#   1 tokens.html:            the TypeDisplay token row
#   2 molecules/nav.html:     the two appended Home specimens
#   3 breadcrumb / site-header specimens, public-layout pages: the Home root crumb
#       (a new <li> Home crumb before Galleries; site-header's Home specimen 'Galleries' -> 'Home';
#        the public layout's default crumb label 'Home' -> the old 'Galleries')
#   4 the speculation-rule string (pages that carry the enhancement scripts; none of these six do)
# Usage (repo root): bash specs/011-home-page/evidence/snapshot-diff.sh <old-dir> [<new-root>]
#   <old-dir>: specs/011-home-page/evidence/pre-011-specimens (006/..., 005/...)
#   <new-root>: holds 006/... and 005/... laid out the same way; defaults to the live baselines.
# Exit status = number of pages with residue.
set -uo pipefail
OLD="$1"
NEW="${2:-}"
newpath() {
  if [ -n "$NEW" ]; then echo "$NEW/$1"; return; fi
  case "$1" in
    006/*) echo "specs/006-public-site-local/evidence/ui-baseline/${1#006/}" ;;
    005/*) echo "specs/005-admin-ui-components/evidence/baseline/${1#005/}" ;;
  esac
}
norm() { perl -0777 -pe 's/ b-[a-z0-9]{10}//g; s/<style>.*?<\/style>/<style><\/style>/gs; s/\s+/ /g; s/>\s*</>\n</g' "$1"; }
RULE_NEW='{"prerender":[{"where":{"or":[{"href_matches":"/g"},{"href_matches":"/g/*"}]},"eagerness":"moderate"}]}'
RULE_OLD='{"prerender":[{"where":{"href_matches":"/g/*"},"eagerness":"moderate"}]}'
# strip <page> <normalised new text>: removes the intended changes for that page.
strip() {
  local page="$1"
  perl -0777 -e '
    my ($page, $rn, $ro) = @ARGV[0..2]; local $/; my $s = <STDIN>;
    $s =~ s/\Q$rn\E/$ro/g;                                                   # 4
    if ($page eq "006/tokens.html") {                                        # 1
      $s =~ s{<div class="token">\n?<dt>TypeDisplay</dt>\s*\n?<dd>\n?<code>48px</code>\n?</dd>\n?</div>\n?}{}g;
    }
    if ($page eq "006/molecules/nav.html") {                                 # 2
      $s =~ s{<section class="specimen">\n?<h2 class="specimen-label">Home, one link</h2>.*?</section>\n?}{}s;
      $s =~ s{<section class="specimen">\n?<h2 class="specimen-label">Home, three links \(sample data\)</h2>.*?</section>\n?}{}s;
    }
    if ($page =~ m{^006/molecules/breadcrumb\.html$|^006/organisms/site-header\.html$}) {   # 3
      $s =~ s{<li>\n?<a class="text-link tap" href="\#">Home</a>\n?</li>\n?(<li>\n?<a class="text-link tap" href="\#">Galleries</a>)}{$1}g;
      $s =~ s{(<li aria-current="page">\n?<span>)Home(</span>)}{$1Galleries$2}g;
    }
    if ($page =~ m{^006/templates/public-layout\.html$|^005/PublicLayoutPage\.html$}) {      # 3
      $s =~ s{(<a class="text-link tap" href="/">)Home(</a>)}{$1Galleries$2}g;
    }
    print $s;' "$page" "$RULE_NEW" "$RULE_OLD"
}
PAGES="006/tokens.html 006/molecules/nav.html 006/molecules/breadcrumb.html 006/organisms/site-header.html 006/templates/public-layout.html 005/PublicLayoutPage.html"
res=0
for p in $PAGES; do
  n=$(newpath "$p")
  if [ ! -f "$OLD/$p" ] || [ ! -f "$n" ]; then echo "RESIDUE $p (missing file)"; res=$((res+1)); continue; fi
  d=$(diff <(norm "$OLD/$p") <(norm "$n" | strip "$p"))
  if [ -z "$d" ]; then echo "SAME $p"; else echo "RESIDUE $p"; printf '%s\n' "$d" | grep '^[<>]' | cut -c1-240; res=$((res+1)); fi
done
echo
[ "$res" -eq 0 ] && echo "ALL EQUAL" || echo "$res RESIDUE"
exit "$res"
