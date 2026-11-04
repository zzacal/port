#!/usr/bin/env bash
# 012: read-only production settings check (contracts/runbook-check.md, FR-023).
# Usage: bash infra/check.sh <origin-default-hostname>
# Reads only: az ... show|list, curl GET/HEAD/OPTIONS. jq is the only parser.
# Secrets print as "present len=<n>" only. Optional env: R2_SERVICE_URL (NET-08 when az is not
# signed in), R2_DEV_HOST (the bucket's pub-*.r2.dev host, NET-07).
set -u

SUB=c28725a0-a61d-4a6c-999a-0a8980111fbf
RG=portfolio
APP=portfolio-web
AI=portfolio-appinsights
MI=portfolio-deploy
SITE=port.zacal.me
IMG=img.zacal.me
FED_SUBJECT=repo:zzacal/port:ref:refs/heads/main
FED_ISSUER=https://token.actions.githubusercontent.com
FED_AUDIENCE=api://AzureADTokenExchange

ORIGIN=${1:-}
if [ -z "$ORIGIN" ]; then
  echo "usage: bash infra/check.sh <origin-default-hostname>" >&2
  exit 2
fi
command -v jq >/dev/null || { echo "jq not found" >&2; exit 2; }

FAILS=0
pass()   { echo "PASS $1 $2"; }
fail()   { echo "FAIL $1 $2: expected $3, got $4"; FAILS=$((FAILS + 1)); }
skip()   { echo "SKIP $1 $2"; }
manual() { echo "MANUAL $1 $2"; }
# check <id> <what> <expected> <got>
check()  { if [ "$3" = "$4" ]; then pass "$1" "$2"; else fail "$1" "$2" "$3" "$4"; fi; }

AZ_IDS="AZ-01 AZ-02 AZ-03 AZ-04 AZ-05 AZ-06 AZ-07 AZ-08 AZ-09 AZ-10 AZ-11 AZ-12 AZ-13 AZ-14 AZ-15 AZ-16"
azq() { az "$@" --subscription "$SUB" --only-show-errors -o json 2>/dev/null; }

# Keys from data-model.md "Production configuration". SECRET_KEYS: presence + length only.
SECRET_KEYS="Hosting__OriginSecret Mongo__ConnectionString Storage__AccessKeyId Storage__SecretAccessKey Authentication__Google__ClientSecret Edge__ApiToken APPLICATIONINSIGHTS_CONNECTION_STRING"
EXACT_VALUES='{
  "ASPNETCORE_ENVIRONMENT": "Production",
  "Hosting__Target": "production",
  "Hosting__Edge": "cloudflare",
  "Hosting__DataProtectionKeysPath": "/home/data/dataprotection-keys",
  "Site__OutputCache": "true",
  "Mongo__Database": "gallery",
  "Storage__PublicBucket": "gallery-prod-public",
  "Storage__PrivateBucket": "gallery-prod-private",
  "Storage__ImageBaseUrl": "https://img.zacal.me",
  "Edge__PurgePages": "true",
  "Edge__SiteBaseUrl": "https://port.zacal.me"
}'
PATTERN_VALUES='{
  "Storage__ServiceUrl": "^https://[0-9a-f]{32}\\.r2\\.cloudflarestorage\\.com$",
  "Authentication__Google__ClientId": "\\.apps\\.googleusercontent\\.com$",
  "Edge__ZoneId": "^[0-9a-f]{32}$"
}'

SETTINGS='[]'
AZ_OK=0
if command -v az >/dev/null 2>&1 && az account show --subscription "$SUB" -o none >/dev/null 2>&1; then
  AZ_OK=1
  # account show reads the local cache; a token proves the sign-in is still valid.
  az account get-access-token --subscription "$SUB" -o none >/dev/null 2>&1 || AZ_OK=2
fi

if [ "$AZ_OK" -eq 0 ]; then
  for id in $AZ_IDS; do skip "$id" "az not signed in"; done
elif [ "$AZ_OK" -eq 2 ]; then
  for id in $AZ_IDS; do skip "$id" "az sign-in expired (run az login)"; done
else
  WEBAPP=$(azq webapp show -g "$RG" -n "$APP")
  CONFIG=$(azq webapp config show -g "$RG" -n "$APP")
  APP_ID=$(printf '%s' "${WEBAPP:-null}" | jq -r '.id // empty')

  # AZ-01 plan sku
  PLAN_ID=$(printf '%s' "${WEBAPP:-null}" | jq -r '.appServicePlanId // .serverFarmId // empty')
  if [ -n "$PLAN_ID" ] && PLAN=$(azq appservice plan show --ids "$PLAN_ID") && [ -n "$PLAN" ]; then
    got=$(printf '%s' "$PLAN" | jq -r '"\(.sku.name), \(if .reserved then "Linux" else "Windows" end)"')
    check AZ-01 "plan sku" "B2, Linux" "$got"
  else
    skip AZ-01 "plan not readable"
  fi

  if [ -n "$CONFIG" ]; then
    check AZ-02 "app runtime" "DOTNETCORE|10.0" "$(printf '%s' "$CONFIG" | jq -r '.linuxFxVersion')"
    check AZ-03 "alwaysOn" "true" "$(printf '%s' "$CONFIG" | jq -r '.alwaysOn')"
  else
    skip AZ-02 "site config not readable"; skip AZ-03 "site config not readable"
  fi
  if [ -n "$WEBAPP" ]; then
    check AZ-04 "httpsOnly" "true" "$(printf '%s' "$WEBAPP" | jq -r '.httpsOnly')"
  else
    skip AZ-04 "web app not readable"
  fi
  if [ -n "$CONFIG" ]; then
    check AZ-05 "ftpsState" "Disabled" "$(printf '%s' "$CONFIG" | jq -r '.ftpsState')"
    check AZ-06 "minTlsVersion" "1.2" "$(printf '%s' "$CONFIG" | jq -r '.minTlsVersion')"
  else
    skip AZ-05 "site config not readable"; skip AZ-06 "site config not readable"
  fi

  # AZ-07 basic auth (scm + ftp)
  if [ -n "$APP_ID" ] \
    && SCM=$(azq resource show --ids "$APP_ID/basicPublishingCredentialsPolicies/scm") \
    && FTP=$(azq resource show --ids "$APP_ID/basicPublishingCredentialsPolicies/ftp"); then
    got="scm=$(printf '%s' "$SCM" | jq -r '.properties.allow') ftp=$(printf '%s' "$FTP" | jq -r '.properties.allow')"
    check AZ-07 "basic auth (scm + ftp)" "scm=false ftp=false" "$got"
  else
    skip AZ-07 "basic publishing policies not readable"
  fi

  # AZ-08 access restrictions main site
  CF_IPS=$(curl -sS --max-time 20 https://api.cloudflare.com/client/v4/ips 2>/dev/null \
    | jq -c 'select(.success == true) | [.result.ipv4_cidrs[], .result.ipv6_cidrs[]] | map(ascii_downcase) | sort' 2>/dev/null)
  RESTR=$(azq webapp config access-restriction show -g "$RG" -n "$APP")
  if [ -z "$CF_IPS" ]; then
    skip AZ-08 "cloudflare ip list unreachable"
  elif [ -z "$RESTR" ] || [ -z "$CONFIG" ]; then
    skip AZ-08 "access restrictions not readable"
  else
    ALLOW=$(printf '%s' "$RESTR" | jq -c '[(.ipSecurityRestrictions // [])[] | select(.action == "Allow") | (.ipAddress // .tag // .name) | ascii_downcase | split(",")[] | gsub(" "; "")] | sort')
    DEFAULT=$(jq -n -r --argjson c "$CONFIG" --argjson r "$RESTR" '
      ($c.ipSecurityRestrictionsDefaultAction // $r.ipSecurityRestrictionsDefaultAction //
       ([($r.ipSecurityRestrictions // [])[] | select((.ipAddress // "") | ascii_downcase == "any") | .action][0]) // "Allow")')
    missing=$(jq -n -r --argjson want "$CF_IPS" --argjson got "$ALLOW" '($want - $got) | length')
    extra=$(jq -n -r --argjson want "$CF_IPS" --argjson got "$ALLOW" '($got - $want) | join(",")')
    want_n=$(printf '%s' "$CF_IPS" | jq 'length')
    if [ "$missing" = "0" ] && [ -z "$extra" ] && [ "$DEFAULT" = "Deny" ]; then
      pass AZ-08 "access restrictions main site (${want_n} cloudflare ranges, default Deny)"
    else
      fail AZ-08 "access restrictions main site" "allow = ${want_n} cloudflare ranges, default Deny" \
        "missing ${missing}, extra [${extra}], default ${DEFAULT}"
    fi
  fi

  # AZ-09 scm site rules
  if [ -n "$CONFIG" ]; then
    check AZ-09 "access restrictions scm site: scmIpSecurityRestrictionsUseMain" "false" \
      "$(printf '%s' "$CONFIG" | jq -r '.scmIpSecurityRestrictionsUseMain')"
  else
    skip AZ-09 "site config not readable"
  fi

  # AZ-10 custom domain
  if HOSTS=$(azq webapp config hostname list -g "$RG" --webapp-name "$APP") && [ -n "$HOSTS" ]; then
    got=$(printf '%s' "$HOSTS" | jq -r --arg h "$SITE" '[.[] | select(.name == $h) | .sslState][0] // "not bound"')
    check AZ-10 "custom domain ${SITE} sslState" "SniEnabled" "$got"
  else
    skip AZ-10 "hostnames not readable"
  fi

  # AZ-11 / AZ-12 app settings
  if SETTINGS=$(azq webapp config appsettings list -g "$RG" -n "$APP") && [ -n "$SETTINGS" ]; then
    ALL_KEYS=$(jq -n -r --argjson e "$EXACT_VALUES" --argjson p "$PATTERN_VALUES" --arg s "$SECRET_KEYS" \
      '(($e | keys) + ($p | keys) + ($s | split(" "))) | unique | .[]')
    missing=""
    for k in $ALL_KEYS; do
      len=$(printf '%s' "$SETTINGS" | jq -r --arg k "$k" '[.[] | select(.name == $k) | (.value // "") | length][0] // 0')
      [ "$len" -gt 0 ] || missing="${missing}${missing:+,}${k}"
    done
    lens=""
    for k in $SECRET_KEYS; do
      len=$(printf '%s' "$SETTINGS" | jq -r --arg k "$k" '[.[] | select(.name == $k) | (.value // "") | length][0] // 0')
      if [ "$len" -gt 0 ]; then lens="${lens}${lens:+; }${k} present len=${len}"; fi
    done
    osl=$(printf '%s' "$SETTINGS" | jq -r '[.[] | select(.name == "Hosting__OriginSecret") | (.value // "") | length][0] // 0')
    if [ -n "$missing" ]; then
      fail AZ-11 "app settings present" "every data-model key" "missing [${missing}] (${lens})"
    elif [ "$osl" -lt 32 ]; then
      fail AZ-11 "app settings present" "Hosting__OriginSecret len>=32" "len=${osl}"
    else
      pass AZ-11 "app settings present (${lens})"
    fi

    diffs=$(jq -n -r --argjson s "$SETTINGS" --argjson e "$EXACT_VALUES" --argjson p "$PATTERN_VALUES" '
      ($s | map({key: .name, value: (.value // "")}) | from_entries) as $have
      | ([$e | to_entries[] | select(($have[.key] // "<unset>") != .value)
          | "\(.key)=\($have[.key] // "<unset>") (want \(.value))"]
       + [$p | to_entries[] | select((($have[.key] // "") | test(.value)) | not)
          | "\(.key)=\($have[.key] // "<unset>") (want /\(.value)/)"])
      | join("; ")')
    if [ -z "$diffs" ]; then
      pass AZ-12 "app settings values (non-secret keys equal data-model)"
    else
      fail AZ-12 "app settings values" "data-model values" "$diffs"
    fi
  else
    SETTINGS='[]'
    skip AZ-11 "app settings not readable"; skip AZ-12 "app settings not readable"
  fi

  # AZ-13 deploy identity role
  PRINCIPAL=$(azq identity show -g "$RG" -n "$MI" | jq -r '.principalId // empty' 2>/dev/null)
  if [ -z "$PRINCIPAL" ]; then
    skip AZ-13 "identity ${MI} not readable"
  elif ROLES=$(azq role assignment list --all --assignee-object-id "$PRINCIPAL") && [ -n "$ROLES" ]; then
    got=$(printf '%s' "$ROLES" | jq -r --arg id "$APP_ID" '
      if length == 1 and .[0].roleDefinitionName == "Website Contributor"
         and ((.[0].scope | ascii_downcase) == ($id | ascii_downcase))
      then "ok" else (map("\(.roleDefinitionName)@\(.scope | split("/") | last)") | join(",") | if . == "" then "none" else . end) end')
    if [ "$got" = "ok" ]; then
      pass AZ-13 "deploy identity role (${MI}: Website Contributor on ${APP} only)"
    else
      fail AZ-13 "deploy identity role" "exactly Website Contributor@${APP}" "$got"
    fi
  else
    skip AZ-13 "role assignments not readable with these rights"
  fi

  # AZ-14 federated credential
  if FED=$(azq identity federated-credential list -g "$RG" --identity-name "$MI") && [ -n "$FED" ]; then
    got=$(printf '%s' "$FED" | jq -r --arg s "$FED_SUBJECT" --arg i "$FED_ISSUER" --arg a "$FED_AUDIENCE" '
      [.[] | select(.subject == $s and .issuer == $i and ((.audiences // []) | index($a)))] | length')
    if [ "$got" = "1" ]; then
      pass AZ-14 "federated credential (${FED_SUBJECT})"
    else
      fail AZ-14 "federated credential" "1 credential subject ${FED_SUBJECT}, issuer GitHub, audience ${FED_AUDIENCE}" \
        "$(printf '%s' "$FED" | jq -r 'map(.subject) | join(",") | if . == "" then "none" else . end')"
    fi
  else
    skip AZ-14 "federated credentials not readable"
  fi

  # AZ-15 / AZ-16 app insights
  if BILL=$(azq monitor app-insights component billing show -g "$RG" --app "$AI") && [ -n "$BILL" ]; then
    check AZ-15 "app insights cap dailyQuotaGb" "0.15" "$(printf '%s' "$BILL" | jq -r '.dataVolumeCap.cap')"
  else
    skip AZ-15 "app insights billing not readable (az extension application-insights)"
  fi
  if COMP=$(azq monitor app-insights component show -g "$RG" --app "$AI") && [ -n "$COMP" ]; then
    check AZ-16 "app insights retention days" "30" "$(printf '%s' "$COMP" | jq -r '.retentionInDays')"
  else
    skip AZ-16 "app insights component not readable (az extension application-insights)"
  fi
fi

# ---- NET rows: observable outcomes, no token ----
C="curl -sS --max-time 20"
hdr() { tr -d '\r' | grep -i "^$1:" | head -1 | cut -d: -f2- | sed 's/^ *//'; }

# NET-01 origin direct
got=$($C -o /dev/null -w '%{http_code}' "https://${ORIGIN}/" 2>&1 | tr '\n' ' ' | sed 's/ *$//')
check NET-01 "origin direct https://${ORIGIN}/" "403" "${got:-000}"

# NET-02 site through edge
H=$($C -o /dev/null -D - -w 'status=%{http_code} verify=%{ssl_verify_result}\n' "https://${SITE}/" 2>/dev/null)
status=$(printf '%s' "$H" | grep -o 'status=[0-9]*' | cut -d= -f2)
verify=$(printf '%s' "$H" | grep -o 'verify=[0-9]*' | cut -d= -f2)
ray=$(printf '%s' "$H" | hdr cf-ray)
got="status ${status:-000}, cf-ray $([ -n "$ray" ] && echo present || echo absent), cert verify ${verify:-none}"
check NET-02 "site through edge https://${SITE}/" "status 200, cf-ray present, cert verify 0" "$got"

# NET-03 https redirect
got=$($C -o /dev/null -w '%{http_code} %{redirect_url}' "http://${SITE}/g" 2>/dev/null)
check NET-03 "https redirect http://${SITE}/g" "301 https://${SITE}/g" "${got:-000}"

# NET-04 owner bypass
c1=$($C -o /dev/null -D - -H 'Cookie: __Host-gallery=x' "https://${SITE}/" 2>/dev/null | hdr cf-cache-status)
c2=$($C -o /dev/null -D - "https://${SITE}/admin" 2>/dev/null | hdr cf-cache-status)
c1=${c1:-none}; c2=${c2:-none}
if { [ "$c1" = "BYPASS" ] || [ "$c1" = "DYNAMIC" ]; } && [ "$c2" != "HIT" ]; then
  pass NET-04 "owner bypass (cookie /: ${c1}; /admin: ${c2})"
else
  fail NET-04 "owner bypass" "cookie / BYPASS|DYNAMIC, /admin not HIT" "cookie / ${c1}, /admin ${c2}"
fi

# NET-05 page cache
$C -o /dev/null "https://${SITE}/g" 2>/dev/null
got=$($C -o /dev/null -D - "https://${SITE}/g" 2>/dev/null | hdr cf-cache-status)
check NET-05 "page cache second GET /g cf-cache-status" "HIT" "${got:-none}"

# NET-06 img through edge
H=$($C -o /dev/null -D - -w 'status=%{http_code}\n' "https://${IMG}/" 2>/dev/null)
ray=$(printf '%s' "$H" | hdr cf-ray)
status=$(printf '%s' "$H" | grep -o 'status=[0-9]*' | cut -d= -f2)
if [ -n "$ray" ]; then
  pass NET-06 "img through edge https://${IMG}/ (status ${status}, cf-ray present)"
else
  fail NET-06 "img through edge https://${IMG}/" "cf-ray present" "status ${status:-000}, cf-ray absent"
fi

# NET-07 r2.dev off
pages="/ /g"
for p in $($C "https://${SITE}/g" 2>/dev/null | grep -oE 'href="/g/[^"#?]+' | cut -d'"' -f2 | sort -u | head -5); do
  pages="$pages $p"
done
hits=""
for p in $pages; do
  n=$($C "https://${SITE}${p}" 2>/dev/null | grep -oE 'pub-[a-z0-9]+\.r2\.dev' | wc -l | tr -d ' ')
  [ "$n" = "0" ] || hits="${hits}${hits:+,}${p}(${n})"
done
devhost=""
if [ -n "${R2_DEV_HOST:-}" ]; then
  code=$($C -o /dev/null -w '%{http_code}' "https://${R2_DEV_HOST}/" 2>/dev/null)
  [ "$code" != "200" ] || devhost="${R2_DEV_HOST} answers 200"
fi
npages=$(echo $pages | wc -w | tr -d ' ')
if [ -z "$hits" ] && [ -z "$devhost" ]; then
  pass NET-07 "r2.dev off (no pub-*.r2.dev in ${npages} public pages$([ -n "${R2_DEV_HOST:-}" ] && echo ", ${R2_DEV_HOST} not 200"))"
else
  fail NET-07 "r2.dev off" "no pub-*.r2.dev URL, r2.dev host not 200" "pages [${hits}] ${devhost}"
fi

# NET-08 r2 cors
SVC=$(printf '%s' "$SETTINGS" | jq -r '[.[] | select(.name == "Storage__ServiceUrl") | .value][0] // empty')
SVC=${SVC:-${R2_SERVICE_URL:-}}
if [ -z "$SVC" ]; then
  skip NET-08 "r2 service url unknown (az not signed in; set R2_SERVICE_URL)"
else
  pre() {
    $C -o /dev/null -D - -w 'status=%{http_code}\n' -X OPTIONS "${SVC%/}/gallery-prod-private/staging/check" \
      -H "Origin: $1" -H 'Access-Control-Request-Method: PUT' -H 'Access-Control-Request-Headers: content-type' 2>/dev/null
  }
  A=$(pre "https://${SITE}"); B=$(pre "https://example.com")
  a_st=$(printf '%s' "$A" | grep -o 'status=[0-9]*' | cut -d= -f2); a_o=$(printf '%s' "$A" | hdr access-control-allow-origin)
  b_st=$(printf '%s' "$B" | grep -o 'status=[0-9]*' | cut -d= -f2); b_o=$(printf '%s' "$B" | hdr access-control-allow-origin)
  if [ "$a_st" = "200" ] && [ "$a_o" = "https://${SITE}" ] && [ "$b_o" != "https://example.com" ] && [ "$b_o" != "*" ]; then
    pass NET-08 "r2 cors (port.zacal.me allowed, example.com refused ${b_st})"
  else
    fail NET-08 "r2 cors" "port.zacal.me 200 + allow-origin, example.com no allow-origin" \
      "port.zacal.me ${a_st:-000} allow-origin '${a_o}', example.com ${b_st:-000} allow-origin '${b_o}'"
  fi
fi

# NET-09
manual NET-09 "tiered cache / early hints: not observable from outside (runbook 5, toggles G)"

echo "RESULT: ${FAILS} FAIL"
[ "$FAILS" -eq 0 ] && exit 0 || exit 1
