#!/usr/bin/env bash
# Builds the sample photos for the /ui/ component pages (feature 003, research R-04).
# Re-runnable. Output: src/Gallery.Web/wwwroot/samples/{hash}.avif|webp + samples.json
# Dev-machine tools only: ffmpeg, cwebp (brew install webp), sips, curl, jq, shasum.
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
OUT="$ROOT/src/Gallery.Web/wwwroot/samples"
WIDTHS=(320 640 960 1280 1920 2560)

for t in ffmpeg cwebp sips curl jq shasum base64 od; do
  command -v "$t" >/dev/null 2>&1 || { echo "missing required tool: $t" >&2; exit 1; }
done

TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT

# id role title
PHOTOS=(
  "1018|landscape|Mountain Lake"
  "1015|landscape|River Valley"
  "1016|landscape|Canyon Light"
  "1043|landscape|Harbour at Dusk"
  "1025|portrait|Pug in a Blanket"
  "1027|portrait|Woman in the Field"
  "1019|square|Still Water"
  "1039|panorama|Long Ridge"
)

full_exif() { # focal fnumber exposure iso
  jq -n --argjson f "$1" --argjson n "$2" --argjson e "$3" --argjson i "$4" \
    '{make:"FUJIFILM",model:"X-T5",lens:"XF33mmF1.4 R LM WR",focalLengthMm:$f,focalLength35Mm:(($f*1.5)|round),fNumber:$n,exposureSeconds:$e,iso:$i}'
}

exif_for() { # id
  case "$1" in
    1018) full_exif 33 2.0 0.004 400 ;;
    1015) full_exif 23 5.6 0.002 200 ;;
    1016) full_exif 56 8 0.008 160 ;;
    1043) full_exif 33 11 1.5 100 ;;
    1025) full_exif 56 1.4 0.0125 800 ;;
    1027) jq -n '{make:"Canon",model:"Canon EOS R5",iso:800}' ;;
    1019) full_exif 33 4 0.01 320 ;;
    1039) echo null ;;
  esac
}

dims_for() { # role -> "w h"
  case "$1" in
    landscape) echo "3000 2000" ;;
    portrait)  echo "2000 3000" ;;
    square)    echo "2400 2400" ;;
    panorama)  echo "3600 1200" ;;
  esac
}

hashname() { echo "$(shasum -a 256 "$1" | cut -c1-16).$2"; }

dimsof() { sips -g pixelWidth -g pixelHeight "$1" | awk '/pixelWidth/{w=$2} /pixelHeight/{h=$2} END{print w" "h}'; }

mkdir -p "$OUT"
find "$OUT" -maxdepth 1 -type f \( -name '*.avif' -o -name '*.webp' -o -name 'samples.json' \) -delete

entries="$TMP/entries.jsonl"
: > "$entries"

for row in "${PHOTOS[@]}"; do
  IFS='|' read -r id role title <<<"$row"
  read -r sw sh <<<"$(dims_for "$role")"
  src="$TMP/$id.jpg"
  echo "== $id $role \"$title\" (${sw}x${sh})"
  curl -fsSL -o "$src" "https://picsum.photos/id/$id/$sw/$sh"
  info="$(curl -fsSL "https://picsum.photos/id/$id/info")"
  author="$(jq -r .author <<<"$info")"
  srcurl="$(jq -r .url <<<"$info")"
  read -r w0 h0 <<<"$(dimsof "$src")"

  variants="[]"
  for W in "${WIDTHS[@]}"; do
    [ "$W" -le "$w0" ] || continue
    a="$TMP/$id-$W.avif"; p="$TMP/$id-$W.webp"
    ffmpeg -hide_banner -loglevel error -y -i "$src" -vf "scale=$W:-2,format=yuv420p" \
      -map_metadata -1 -frames:v 1 -c:v libsvtav1 -crf 32 -preset 6 "$a" 2>/dev/null
    cwebp -quiet -q 78 -resize "$W" 0 "$src" -o "$p"
    an="$(hashname "$a" avif)"; pn="$(hashname "$p" webp)"
    cp "$a" "$OUT/$an"; cp "$p" "$OUT/$pn"
    variants="$(jq -c --argjson w "$W" --arg a "/samples/$an" --arg p "/samples/$pn" \
      '. + [{width:$w,avif:$a,webp:$p}]' <<<"$variants")"
    last="$p"
  done

  read -r fw fh <<<"$(dimsof "$last")"

  ph="$TMP/$id-ph.webp"
  # Long side 32 px, short side rounded to the nearest pixel, so the placeholder's ratio stays
  # as close as possible to the photo's (Picture insets it 2% to hide any remaining error).
  if [ "$fw" -ge "$fh" ]; then pw=32; phh=$(( (32 * fh + fw / 2) / fw )); else phh=32; pw=$(( (32 * fw + fh / 2) / fh )); fi
  cwebp -quiet -q 40 -resize "$pw" "$phh" "$src" -o "$ph"
  placeholder="data:image/webp;base64,$(base64 -i "$ph" | tr -d '\n')"

  rgb=($(ffmpeg -hide_banner -loglevel error -i "$src" -vf scale=1:1 -f rawvideo -pix_fmt rgb24 - | od -An -tu1))
  color="$(printf '#%02x%02x%02x' "${rgb[0]}" "${rgb[1]}" "${rgb[2]}")"

  pid="$(printf '%s' "sample-$id" | shasum -a 256 | cut -c1-16)"

  jq -cn --arg id "$pid" --arg role "$role" --arg title "$title" \
    --argjson width "$fw" --argjson height "$fh" --argjson variants "$variants" \
    --arg placeholder "$placeholder" --arg color "$color" --argjson exif "$(exif_for "$id")" \
    --arg author "$author" --arg source "$srcurl" \
    '{id:$id,role:$role,title:$title,width:$width,height:$height,variants:$variants,
      placeholder:$placeholder,dominantColor:$color,exif:$exif,
      credit:{author:$author,source:$source,license:"Unsplash License"}}' >> "$entries"
done

jq -s '{photos: .}' "$entries" > "$OUT/samples.json"

bash "$ROOT/scripts/check-metadata.sh" "$OUT"/*.avif "$OUT"/*.webp || { echo "metadata check failed" >&2; exit 1; }

echo "metadata check: 0 files with EXIF/XMP structures out of $(ls "$OUT"/*.avif "$OUT"/*.webp | wc -l | tr -d ' ')"
echo "wrote $OUT/samples.json ($(jq '.photos|length' "$OUT/samples.json") photos)"
