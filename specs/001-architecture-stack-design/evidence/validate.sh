#!/usr/bin/env bash
# Contract validation for docs/design.md (contracts/design-doc-contract.md C1-C9; Tasks T003).
# Run from repo root. Prints PASS/FAIL per check; exits non-zero on any FAIL.
set -uo pipefail
FD="specs/001-architecture-stack-design"
DOC="docs/design.md"
CON=".specify/memory/constitution.md"
bash "$FD/evidence/assemble.sh" >/dev/null || { echo "FAIL assemble"; exit 2; }

fails=0
pass() { echo "PASS $*"; }
fail() { echo "FAIL $*"; fails=$((fails+1)); }
cnt() { grep -cE "$1" "$DOC" || true; }   # count lines matching ERE

# Section extractor: text between "## N." heading and next "## " heading
section() { awk -v h="^## $1\\. " '$0 ~ h {on=1; next} on && /^## / {exit} on {print}' "$DOC"; }

# ---------- C1 headings in order ----------
expected_h=(
"# Gallery Architecture and Stack Design"
"## 1. Overview" "## 2. Decision Record" "## 3. System Diagrams" "## 4. Content Model"
"## 5. Lifecycle" "## 6. Edge Cases" "## 7. Traceability Matrix" "## 8. Cost and Free-Tier Budget"
"## 9. Accepted-Risk Register" "## 10. Deferred to Later Features" "## 11. Sources")
actual_h=$(grep -E '^#{1,2} ' "$DOC")
exp_joined=$(printf '%s\n' "${expected_h[@]}")
if [ "$actual_h" = "$exp_joined" ]; then pass C1 "12 headings in order"
else fail C1 "headings differ: $(diff <(echo "$exp_joined") <(echo "$actual_h") | grep '^[<>]' | tr '\n' ' ')"; fi

# ---------- C2 decision entries ----------
keys=(hosting-runtime edge-dns-tls origin-lockdown identity-claim datastore storage-layout
upload-intake client-derivatives server-validation upload-batch content-model visibility-addressing
public-rendering edge-cache predictive-loading observability csrf)
bad=""
for k in "${keys[@]}"; do
  n=$(grep -cxF "**Area**: $k" "$DOC" || true)
  [ "$n" = 1 ] || bad="$bad $k($n)"
done
[ -z "$bad" ] && pass C2.keys "17 area keys each exactly once" || fail C2.keys "missing/duplicate:$bad"

nD=$(cnt '^### D-[0-9][0-9]: ')
okf=1; detail="D=$nD"
for f in Area Status Choice Rationale Alternatives 'Pivot cost'; do
  n=$(cnt "^\*\*$f\*\*: ")
  detail="$detail $f=$n"
  [ "$n" = "$nD" ] || okf=0
done
{ [ "$okf" = 1 ] && [ "$nD" -ge 17 ]; } && pass C2.fields "$detail" || fail C2.fields "$detail"

badS=$(grep -E '^\*\*Status\*\*: ' "$DOC" | grep -vxE '\*\*Status\*\*: (given|chosen|requires-amendment)' || true)
{ [ "$nD" -ge 1 ] && [ -z "$badS" ]; } && pass C2.status "values valid" || fail C2.status "invalid/none: ${badS:-no entries}"
badP=$(grep -E '^\*\*Pivot cost\*\*: ' "$DOC" | grep -vE '^\*\*Pivot cost\*\*: (low|medium|high)' || true)
{ [ "$nD" -ge 1 ] && [ -z "$badP" ]; } && pass C2.pivot "values valid" || fail C2.pivot "invalid/none: ${badP:-no entries}"
# given => Rationale cites Constitution
badG=$(awk '/^### D-[0-9][0-9]: /{id=$2; st=""} /^\*\*Status\*\*: /{st=$2} /^\*\*Rationale\*\*: /{ if (st=="given" && $0 !~ /Constitution /) print id }' "$DOC")
{ [ "$nD" -ge 1 ] && [ -z "$badG" ]; } && pass C2.given "given entries cite Constitution" || fail C2.given "uncited given: ${badG:-no entries}"

# ---------- C3 diagrams ----------
bad=""
for h in "Visitor public page" "Cache miss to origin" "Owner upload to library" "Owner sign-in and claim"; do
  ok=$(awk -v h="### Diagram: $h" '$0==h{f=NR} f && NR>f && NR<=f+3 && $0=="```mermaid"{print "y"; exit}' "$DOC")
  [ "$ok" = y ] || bad="$bad [$h]"
done
[ -z "$bad" ] && pass C3 "4 diagrams with mermaid" || fail C3 "missing:$bad"

# ---------- C4 lifecycle ----------
exp_l="### L-01: Image succeeded in batch
### L-02: Image failed or interrupted in batch
### L-03: Image restarted
### L-04: Image deleted from batch
### L-05: Batch confirmed
### L-06: Batch discarded
### L-07: Image added to gallery
### L-08: Image removed from gallery
### L-09: Image deleted
### L-10: Gallery created
### L-11: Gallery made public
### L-12: Gallery made private
### L-13: Gallery renamed
### L-14: Gallery reordered
### L-15: Images reordered
### L-16: Gallery cover changed
### L-17: Gallery deleted"
act_l=$(grep -E '^### L-[0-9][0-9]: ' "$DOC" || true)
[ "$act_l" = "$exp_l" ] && pass C4.titles "17 lifecycle entries in order" || fail C4.titles "lifecycle headings differ ($(echo -n "$act_l" | grep -c . ) found)"
detail=""; okf=1
for f in 'Records' 'Storage' 'Pages and caches' 'Visitor sees'; do
  n=$(cnt "^\*\*$f\*\*: "); detail="$detail $f=$n"; [ "$n" = 17 ] || okf=0
done
[ "$okf" = 1 ] && pass C4.fields "$detail" || fail C4.fields "$detail (want 17 each)"

# ---------- C5 edge cases ----------
nE=$(cnt '^### E-(0[1-9]|1[0-9]): ')
nO=$(cnt '^\*\*Outcome\*\*: '); nM=$(cnt '^\*\*Mechanism\*\*: ')
{ [ "$nE" = 19 ] && [ "$nO" = 19 ] && [ "$nM" = 19 ]; } && pass C5 "19 edge cases x2 fields" || fail C5 "E=$nE Outcome=$nO Mechanism=$nM (want 19)"

# ---------- C6 traceability ----------
expected_ids=$(awk '
  function flush() { if (p!="") { if (n==0) n=1; for(i=1;i<=n;i++) print p "." i } p=""; n=0 }
  /^### [IVX]+\. / { flush(); p=$2; sub(/\.$/,"",p); inpc=0; next }
  /^## Platform Constraints/ { flush(); inpc=1; next }
  /^## / { flush(); inpc=0; next }
  p!="" && /^[0-9]+\. / { n++ }
  inpc && /^- / { pc++; print "PC." pc }
  END { flush() }' "$CON" | LC_ALL=C sort)
rows=$(section 7 | grep -E '^\| ' | grep -vE '^\| *Rule *\|' | grep -vE '^\| *-+' )
found_ids=$(echo "$rows" | awk -F'|' 'NF>2{gsub(/ /,"",$2); if($2!="") print $2}' | LC_ALL=C sort)
dups=$(echo "$found_ids" | uniq -d)
missing=$(comm -23 <(echo "$expected_ids") <(echo "$found_ids" | uniq) | tr '\n' ' ')
extra=$(comm -13 <(echo "$expected_ids") <(echo "$found_ids" | uniq) | tr '\n' ' ')
nexp=$(echo "$expected_ids" | grep -c .)
if [ -n "$found_ids" ] && [ -z "$dups" ] && [ -z "${missing// /}" ] && [ -z "${extra// /}" ]; then
  pass C6.ids "$nexp rule ids exactly once"
else fail C6.ids "expected=$nexp missing=[${missing}] extra=[${extra}] dups=[$(echo $dups)]"; fi
badst=$(echo "$rows" | awk -F'|' 'NF>2{s=$5; gsub(/^ +| +$/,"",s); if (s !~ /^(designed|deferred:.+|n\/a:.+)$/) {gsub(/ /,"",$2); print $2}}' | tr '\n' ' ')
{ [ -n "$rows" ] && [ -z "${badst// /}" ]; } && pass C6.status "all statuses valid" || fail C6.status "bad status: ${badst:-no rows}"
badv=""
for id in IV.5 IV.11 IV.12 IV.13 IV.14 IV.15 IV.16 IV.17 IV.18 IV.19 IV.20 IV.21 IV.22; do
  v=$(echo "$rows" | awk -F'|' -v id="$id" '{r=$2; gsub(/ /,"",r); if (r==id) print $4}')
  echo "$v" | grep -qE 'markup/header assertion|browser measurement' || badv="$badv $id"
done
[ -z "$badv" ] && pass C6.verif "IV.5, IV.11-22 use markup/header or browser verification" || fail C6.verif "wrong verification:$badv"

# ---------- C7 cost ----------
sec8=$(section 8)
okh=$(echo "$sec8" | grep -cE '^\| *Service *\| *Dimension *\| *Free limit *\| *Projected *\| *Overage behaviour *\|' || true)
fixed=$(echo "$sec8" | grep -cE '^\| *TOTAL fixed *\|' || true)
metered_row=$(echo "$sec8" | grep -E '^\| *TOTAL metered *\|' || true)
mval=$(echo "$metered_row" | awk -F'|' '{print $5}' | grep -oE '\$[0-9]+(\.[0-9]+)?' | head -1 | tr -d '$')
mok=$(awk -v v="${mval:-999}" 'BEGIN{print (v<7)?"y":"n"}')
svc_bad=""
for s in "Azure App Service" "Azure Blob Storage" "Application Insights" "MongoDB Atlas" "Cloudflare"; do
  if grep -E '^\*\*Choice\*\*: ' "$DOC" | grep -qF "$s"; then
    echo "$sec8" | grep -E '^\| ' | grep -qF "$s" || svc_bad="$svc_bad [$s]"
  fi
done
if [ "$okh" = 1 ] && [ "$fixed" -ge 1 ] && [ -n "$metered_row" ] && [ "$mok" = y ] && [ -z "$svc_bad" ]; then
  pass C7 "cost table ok; metered=\$$mval < \$7"
else fail C7 "header=$okh fixed=$fixed metered=${mval:-none} services_missing=${svc_bad:-none}"; fi

# ---------- C8 risks ----------
nR=$(cnt '^### R-[0-9][0-9]: ')
bad=""
for kw in "claim token" "backups" "log retention" "unlisted" "browser cache" "client-generated" "credential"; do
  grep -E '^### R-[0-9][0-9]: ' "$DOC" | grep -qiF "$kw" || bad="$bad [$kw]"
done
detail="R=$nR"; okf=1
for f in Failure Detection Recovery 'Reversal cost'; do
  n=$(cnt "^\*\*$f\*\*: "); detail="$detail $f=$n"; [ "$n" = "$nR" ] || okf=0
done
{ [ -z "$bad" ] && [ "$okf" = 1 ] && [ "$nR" -ge 7 ]; } && pass C8 "$detail" || fail C8 "$detail missing:${bad:-none}"

# ---------- C9 hygiene ----------
h=$(grep -nE 'TBD|TODO|NEEDS CLARIFICATION|\?\?' "$DOC" || true)
[ -z "$h" ] && pass C9 "no banned strings" || fail C9 "banned: $(echo "$h" | head -5 | tr '\n' ' ')"

echo "---"
echo "failures: $fails"
[ "$fails" = 0 ]
