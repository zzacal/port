#!/usr/bin/env bash
# scripts/prepare-photos.sh
# Prepares owner's JPEGs for seeding: decodes, scales, encodes AVIF+WebP ladder,
# computes preview+dominant colour, writes photo.json.
# Dev-machine tools only: ffmpeg, cwebp, sips, shasum, jq.
# Env overrides: PHOTOS_DIR (default: dev-pictures), PREPARED_DIR (default: .dev-prepared)
# Output: PREPARED_DIR/<sourceHash>/{photo.json, *.avif, *.webp}
# SAFETY: never reads GPS, never copies originals, never writes inside dev-pictures.
set -uo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"

PHOTOS_DIR="${PHOTOS_DIR:-$ROOT/dev-pictures}"
PREPARED_DIR="${PREPARED_DIR:-$ROOT/.dev-prepared}"

# ---------- tool check ----------
for t in ffmpeg cwebp sips shasum jq base64 od mktemp; do
  command -v "$t" >/dev/null 2>&1 || { echo "missing required tool: $t" >&2; exit 1; }
done

# ---------- collect inputs ----------
shopt -s nullglob nocaseglob
PHOTOS=("$PHOTOS_DIR"/*.jpg "$PHOTOS_DIR"/*.jpeg)
shopt -u nullglob nocaseglob

if [ "${#PHOTOS[@]}" -eq 0 ]; then
  echo "No .jpg/.jpeg files found in $PHOTOS_DIR" >&2
  exit 0
fi

mkdir -p "$PREPARED_DIR"

WIDTHS=(320 640 960 1280 1920 2560)

# Results dir: one status file per photo, keyed by index
RESULTS_DIR="$(mktemp -d)"
trap 'rm -rf "$RESULTS_DIR"' EXIT

# ---------- worker script ----------
WORKER="$RESULTS_DIR/worker.sh"
cat > "$WORKER" << 'WORKER_EOF'
#!/usr/bin/env bash
set -uo pipefail
SRC="$1"
PREPARED_DIR="$2"
ROOT="$3"
RESULT_FILE="$4"

WIDTHS=(320 640 960 1280 1920 2560)

name="$(basename "$SRC")"

# magic-byte JPEG check (FF D8 FF)
magic="$(od -An -v -tx1 -N3 "$SRC" 2>/dev/null | tr -d ' \n' | tr '[:upper:]' '[:lower:]')"
if [ "$magic" != "ffd8ff" ]; then
  echo "REJECTED $name not a JPEG" > "$RESULT_FILE"
  exit 0
fi

# size check (20 MB = 20971520 bytes)
fsize="$(stat -f%z "$SRC")"
if [ "$fsize" -gt 20971520 ]; then
  echo "REJECTED $name over 20 MB" > "$RESULT_FILE"
  exit 0
fi

# sourceHash
sourceHash="$(shasum -a 256 "$SRC" | cut -c1-16)"
outdir="$PREPARED_DIR/$sourceHash"

# skip if already prepared
if [ -f "$outdir/photo.json" ]; then
  echo "SKIPPED $name" > "$RESULT_FILE"
  exit 0
fi

mkdir -p "$outdir"

TMP="$(mktemp -d)"
cleanup() { rm -rf "$TMP"; }
trap cleanup EXIT

intermediate="$TMP/intermediate.png"

# Step 2: decode to upright, metadata-free intermediate PNG
# ffmpeg applies EXIF orientation on decode (research R-04)
# scale=min(2560,iw):-2: -2 rounds height to even for yuv420p compatibility
if ! ffmpeg -hide_banner -loglevel error -y \
    -i "$SRC" \
    -vf "scale='min(2560,iw)':-2" \
    -frames:v 1 \
    -map_metadata -1 \
    "$intermediate" 2>/dev/null; then
  echo "REJECTED $name ffmpeg-intermediate-failed" > "$RESULT_FILE"
  exit 0
fi

# read intermediate dimensions (post-orientation upright width)
read -r iw ih < <(sips -g pixelWidth -g pixelHeight "$intermediate" \
  | awk '/pixelWidth/{w=$2} /pixelHeight/{h=$2} END{print w" "h}')

# If the upright width is smaller than the smallest ladder step,
# fall back to encoding at the actual upright width (handles portrait fixtures).
eff_widths=()
for W in "${WIDTHS[@]}"; do
  [ "$W" -le "$iw" ] && eff_widths+=("$W")
done
if [ "${#eff_widths[@]}" -eq 0 ]; then
  eff_widths=("$iw")
fi

# Step 3: encode AVIF and WebP for each ladder width
variants="[]"
last_webp=""

for W in "${eff_widths[@]}"; do
  avif_tmp="$TMP/${sourceHash}-${W}.avif"
  webp_tmp="$TMP/${sourceHash}-${W}.webp"

  # AVIF via ffmpeg libsvtav1 (matches build-samples.sh encoder flags)
  if ! ffmpeg -hide_banner -loglevel error -y \
      -i "$intermediate" \
      -vf "scale=${W}:-2,format=yuv420p" \
      -map_metadata -1 \
      -frames:v 1 \
      -c:v libsvtav1 -crf 32 -preset 6 \
      "$avif_tmp" 2>/dev/null; then
    echo "REJECTED $name ffmpeg-avif-${W}-failed" > "$RESULT_FILE"
    exit 0
  fi

  # WebP via cwebp (matches build-samples.sh: -q 78 -resize W 0)
  if ! cwebp -quiet -q 78 -resize "$W" 0 "$intermediate" -o "$webp_tmp" 2>/dev/null; then
    echo "REJECTED $name cwebp-webp-${W}-failed" > "$RESULT_FILE"
    exit 0
  fi

  avif_hash="$(shasum -a 256 "$avif_tmp" | cut -c1-16)"
  webp_hash="$(shasum -a 256 "$webp_tmp" | cut -c1-16)"
  avif_name="${avif_hash}.avif"
  webp_name="${webp_hash}.webp"

  cp "$avif_tmp" "$outdir/$avif_name"
  cp "$webp_tmp" "$outdir/$webp_name"

  bytes_avif="$(stat -f%z "$outdir/$avif_name")"
  bytes_webp="$(stat -f%z "$outdir/$webp_name")"

  variants="$(jq -c \
    --argjson w "$W" \
    --arg a "$avif_name" \
    --arg p "$webp_name" \
    --argjson ba "$bytes_avif" \
    --argjson bp "$bytes_webp" \
    '. + [{w:$w,avif:$a,webp:$p,bytesAvif:$ba,bytesWebp:$bp}]' \
    <<<"$variants")"

  last_webp="$outdir/$webp_name"
done

# Step 4: preview (32 px on long edge) and dominant colour
read -r fw fh < <(sips -g pixelWidth -g pixelHeight "$last_webp" \
  | awk '/pixelWidth/{w=$2} /pixelHeight/{h=$2} END{print w" "h}')

if [ "$fw" -ge "$fh" ]; then
  pw=32
  phh=$(( (32 * fh + fw / 2) / fw ))
else
  phh=32
  pw=$(( (32 * fw + fh / 2) / fh ))
fi

preview_tmp="$TMP/preview.webp"
cwebp -quiet -q 40 -resize "$pw" "$phh" "$intermediate" -o "$preview_tmp" 2>/dev/null
preview_uri="data:image/webp;base64,$(base64 -i "$preview_tmp" | tr -d '\n')"

# dominant colour: 1x1 scale of the intermediate
rgb_raw="$(ffmpeg -hide_banner -loglevel error -i "$intermediate" \
  -vf scale=1:1 -f rawvideo -pix_fmt rgb24 - 2>/dev/null \
  | od -An -tu1 | tr -s ' \n' ' ' | awk '{print $1, $2, $3}')"
read -r r g b <<< "$rgb_raw"
color="$(printf '#%02x%02x%02x' "$r" "$g" "$b")"

# Step 5: read back width/height from largest WebP
read -r fw fh < <(sips -g pixelWidth -g pixelHeight "$last_webp" \
  | awk '/pixelWidth/{w=$2} /pixelHeight/{h=$2} END{print w" "h}')

# Step 6: run check-metadata.sh over all prepared derivatives
if ! bash "$ROOT/scripts/check-metadata.sh" "$outdir"/*.avif "$outdir"/*.webp >/dev/null 2>&1; then
  rm -rf "$outdir"
  echo "REJECTED $name metadata-check-failed" > "$RESULT_FILE"
  exit 0
fi

# Step 7: write photo.json last (marks success — interrupted runs leave no half-prepared photo)
jq -cn \
  --arg sourceHash "$sourceHash" \
  --arg sourceName "$name" \
  --argjson width "$fw" \
  --argjson height "$fh" \
  --arg color "$color" \
  --arg preview "$preview_uri" \
  --argjson derivatives "$variants" \
  '{sourceHash:$sourceHash,sourceName:$sourceName,width:$width,height:$height,
    color:$color,preview:$preview,derivatives:$derivatives}' \
  > "$outdir/photo.json"

echo "PREPARED $name" > "$RESULT_FILE"
exit 0
WORKER_EOF
chmod +x "$WORKER"

# ---------- dispatch with at-most-4 parallelism ----------
i=0
declare -a PIDS=()

for src in "${PHOTOS[@]}"; do
  result_file="$RESULTS_DIR/result_$i"
  bash "$WORKER" "$src" "$PREPARED_DIR" "$ROOT" "$result_file" &
  PIDS+=($!)
  i=$((i + 1))
  # drain to keep ≤ 4 in flight
  while [ "${#PIDS[@]}" -ge 4 ]; do
    wait "${PIDS[0]}" 2>/dev/null || true
    PIDS=("${PIDS[@]:1}")
  done
done

# wait for remaining jobs
for pid in "${PIDS[@]}"; do
  wait "$pid" 2>/dev/null || true
done

# ---------- tally and print results ----------
COUNT_PREPARED=0
COUNT_SKIPPED=0
COUNT_REJECTED=0

for result_file in "$RESULTS_DIR"/result_*; do
  [ -f "$result_file" ] || continue
  line="$(cat "$result_file")"
  echo "$line"
  case "$line" in
    PREPARED*) COUNT_PREPARED=$((COUNT_PREPARED + 1)) ;;
    SKIPPED*)  COUNT_SKIPPED=$((COUNT_SKIPPED + 1)) ;;
    REJECTED*) COUNT_REJECTED=$((COUNT_REJECTED + 1)) ;;
  esac
done

echo "prepared $COUNT_PREPARED, skipped-unchanged $COUNT_SKIPPED, rejected $COUNT_REJECTED"
