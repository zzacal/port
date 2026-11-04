#!/usr/bin/env bash
# 012: read-only probe of the owner's Cloudflare/Azure steps A-G. No secrets read or printed.
set -u
H=port.zacal.me
echo "== DNS"
echo "port A (via CF?): $(dig +short $H A | tr '\n' ' ')"
echo "asuid.port TXT present: $( [ -n "$(dig +short TXT asuid.$H)" ] && echo yes || echo no )"
echo "img A: $(dig +short img.zacal.me A | tr '\n' ' ')"
echo "== HTTPS port"
curl -sS -o /dev/null -D /tmp/012-h.txt -w 'status=%{http_code} ssl_verify=%{ssl_verify_result}\n' https://$H/ 2>&1
grep -i -E '^(server|cf-ray|cf-cache-status|location):' /tmp/012-h.txt | sed 's/cf-ray: .*/cf-ray: <present>/I'
echo "== cert seen by visitors"
echo | openssl s_client -connect $H:443 -servername $H 2>/dev/null | openssl x509 -noout -subject -issuer -enddate 2>/dev/null
echo "== HTTP -> HTTPS"
curl -sS -o /dev/null -w 'status=%{http_code} location=%{redirect_url}\n' "http://$H/g?x=1"
echo "== cache bypass on /admin"
curl -sS -o /dev/null -D - https://$H/admin 2>/dev/null | grep -i -E '^(cf-cache-status|HTTP/)' 
echo "== direct origin (bypassing Cloudflare)"
ORIGIN=$(dig +short CNAME $H)
echo "port CNAME visible publicly: ${ORIGIN:-<hidden by proxy>}"
echo "== img"
curl -sS -o /dev/null -D - https://img.zacal.me/ 2>/dev/null | grep -i -E '^(HTTP/|server|cf-cache-status)'
