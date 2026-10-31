#!/usr/bin/env bash
# scripts/seed-manifest.sh
# Generates seed/manifest.json from dev-pictures/ JPEG files grouped by capture date.
# Refuses to overwrite an existing manifest (exit 1).
# Env overrides: PHOTOS_DIR (default: dev-pictures), MANIFEST (default: seed/manifest.json)
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"

PHOTOS_DIR="${PHOTOS_DIR:-$ROOT/dev-pictures}"
MANIFEST="${MANIFEST:-$ROOT/seed/manifest.json}"

# ---------- tool check ----------
for t in jq mdls python3; do
  command -v "$t" >/dev/null 2>&1 || { echo "missing required tool: $t" >&2; exit 1; }
done

# ---------- refuse to overwrite ----------
if [ -f "$MANIFEST" ]; then
  echo "seed/manifest.json already exists. Delete it to regenerate." >&2
  exit 1
fi

# ---------- collect JPEGs ----------
shopt -s nullglob nocaseglob
PHOTOS=("$PHOTOS_DIR"/*.jpg "$PHOTOS_DIR"/*.jpeg)
shopt -u nullglob nocaseglob

if [ "${#PHOTOS[@]}" -eq 0 ]; then
  echo "No .jpg/.jpeg files found in $PHOTOS_DIR" >&2
  exit 1
fi

# ---------- read capture dates via mdls ----------
TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT

DATE_FILE="$TMP/dates.tsv"
: > "$DATE_FILE"

for f in "${PHOTOS[@]}"; do
  name="$(basename "$f")"
  raw_date="$(mdls -raw -name kMDItemContentCreationDate "$f" 2>/dev/null || echo "(null)")"
  if [ "$raw_date" = "(null)" ] || [ -z "$raw_date" ]; then
    date_str="unknown"
  else
    # kMDItemContentCreationDate format: "2026-09-05 12:34:56 +0000"
    date_str="$(echo "$raw_date" | awk '{print $1}')"
  fi
  printf '%s\t%s\n' "$date_str" "$name" >> "$DATE_FILE"
done

# ---------- assemble manifest with Python (stdlib, bash 3.2 compatible) ----------
PY_SCRIPT="$TMP/build_manifest.py"
cat > "$PY_SCRIPT" << 'PYEOF'
#!/usr/bin/env python3
"""Build seed/manifest.json from a date-TSV file."""
import sys, os, json, datetime
from collections import defaultdict

date_file = sys.argv[1]
manifest_out = sys.argv[2]

with open(date_file) as f:
    rows = [line.rstrip('\n').split('\t', 1) for line in f if line.strip()]

by_date = defaultdict(list)
for date_str, name in rows:
    if date_str != 'unknown':
        by_date[date_str].append(name)

if not by_date:
    print("No photos with capture dates found", file=sys.stderr)
    sys.exit(1)

sorted_dates = sorted(by_date.keys())
for d in sorted_dates:
    by_date[d] = sorted(by_date[d])

# Date with most photos (for unlisted)
max_date = max(sorted_dates, key=lambda d: len(by_date[d]))

def date_to_key(d):
    """2026-09-05 -> sep-05"""
    dt = datetime.date.fromisoformat(d)
    return dt.strftime('%b').lower() + '-' + f'{dt.day:02d}'

def date_to_title(d):
    """2026-09-05 -> September 5"""
    dt = datetime.date.fromisoformat(d)
    return dt.strftime('%B') + ' ' + str(dt.day)

galleries = []
first_photo = None
second_first_photo = None

for i, d in enumerate(sorted_dates):
    photos = list(by_date[d])
    g = {
        "key": date_to_key(d),
        "title": date_to_title(d),
        "description": None,
        "visibility": "public",
        "cover": None,
        "photos": photos,
    }
    galleries.append(g)
    if i == 0:
        first_photo = photos[0]
    elif i == 1:
        second_first_photo = photos[0]

# FR-016: append first photo of second dated gallery to first dated gallery
if second_first_photo is not None:
    galleries[0]["photos"].append(second_first_photo)

# FR-016: add "unlisted" (private, first 3 photos of day with most photos)
unlisted_photos = by_date[max_date][:3]
galleries.append({
    "key": "unlisted",
    "title": "Unlisted",
    "description": None,
    "visibility": "private",
    "cover": None,
    "photos": unlisted_photos,
})

# FR-016: add "coming-soon" (public, no photos)
galleries.append({
    "key": "coming-soon",
    "title": "Coming soon",
    "description": None,
    "visibility": "public",
    "cover": None,
    "photos": [],
})

# Top-level photos: first photo of first dated gallery gets a sample caption
photos_obj = {}
if first_photo is not None:
    photos_obj[first_photo] = {
        "title": None,
        "caption": "First light of the series."
    }

manifest = {
    "galleries": galleries,
    "photos": photos_obj,
}

os.makedirs(os.path.dirname(os.path.abspath(manifest_out)), exist_ok=True)
with open(manifest_out, 'w') as fout:
    json.dump(manifest, fout, indent=2)
    fout.write('\n')

print(f"wrote {manifest_out} ({len(galleries)} galleries)")
PYEOF

python3 "$PY_SCRIPT" "$DATE_FILE" "$MANIFEST"

# Validate with jq
jq . "$MANIFEST" > /dev/null && echo "JSON valid"
echo "$(jq '.galleries|length' "$MANIFEST") galleries, $(jq '[.galleries[].photos|length]|add' "$MANIFEST") total photo references"
