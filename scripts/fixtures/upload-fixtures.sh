#!/usr/bin/env bash
# Regenerates the generated bad-case upload fixtures (008 T003, decisions row 42) from
# tests/fixtures/gps-camera.jpg. Dev-machine tools only (cwebp from libwebp, macOS sips); they are
# not app dependencies, and the outputs are committed so the build never needs them.
#
# Outputs, in tests/fixtures/upload/:
#   placeholder-exif.webp  32 px wide WebP carrying an EXIF chunk (cwebp -metadata exif)
#   icc.webp               WebP carrying an ICC profile (cwebp -metadata icc) from a JPEG that
#                          sips --embedProfile tagged with sRGB
#   fixture-6000x4000.jpg  synthetic large JPEG (sips -z) for the 2560 ladder
#
# Structural AVIF cases (second av01 item, iref, irot, colr prof, avis brand) are built in test
# code (T018), not here. The probe-copied fixtures (gps-sips.avif etc.) are not regenerated here.
#
# Usage: bash scripts/fixtures/upload-fixtures.sh
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
src="$repo_root/tests/fixtures/gps-camera.jpg"
out="$repo_root/tests/fixtures/upload"
srgb="/System/Library/ColorSync/Profiles/sRGB Profile.icc"

for tool in cwebp sips; do
  command -v "$tool" >/dev/null 2>&1 || { echo "missing $tool (brew install webp / macOS sips)" >&2; exit 1; }
done
[[ -f "$src" ]] || { echo "missing $src" >&2; exit 1; }
[[ -f "$srgb" ]] || { echo "missing $srgb" >&2; exit 1; }

work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT
mkdir -p "$out"

# 1. placeholder-sized WebP that still carries EXIF (must be rejected as metadata-bearing).
cwebp -quiet -resize 32 0 -metadata exif "$src" -o "$out/placeholder-exif.webp"

# 2. WebP with an embedded ICC profile (must be rejected: ICC is not allowed on derivatives).
cp "$src" "$work/icc-src.jpg"
sips --embedProfile "$srgb" "$work/icc-src.jpg" --out "$work/icc-tagged.jpg" >/dev/null
# macOS ImageIO treats sRGB as the implied default and writes no APP2 ICC_PROFILE segment for it
# (Display P3 is written). When that happens, splice the same sRGB profile in as a standard APP2
# ICC_PROFILE segment (seq 1 of 1) right after SOI, so the JPEG really is sRGB-tagged.
if ! LC_ALL=C grep -a -q "ICC_PROFILE" "$work/icc-tagged.jpg"; then
  SRGB="$srgb" perl -e '
    local $/; open my $p, "<:raw", $ENV{SRGB} or die; my $icc = <$p>;
    open my $j, "<:raw", $ARGV[0] or die; my $jpg = <$j>;
    substr($jpg, 0, 2) eq "\xFF\xD8" or die "not a JPEG\n";
    my $payload = "ICC_PROFILE\0" . chr(1) . chr(1) . $icc;
    my $seg = "\xFF\xE2" . pack("n", 2 + length $payload) . $payload;
    open my $o, ">:raw", $ARGV[1] or die; print $o substr($jpg, 0, 2) . $seg . substr($jpg, 2);
  ' "$work/icc-tagged.jpg" "$work/icc-spliced.jpg"
  mv "$work/icc-spliced.jpg" "$work/icc-tagged.jpg"
fi
[[ "$(sips -g profile "$work/icc-tagged.jpg" | awk -F': ' '/profile/{print $2}')" == "sRGB IEC61966-2.1" ]] \
  || { echo "icc source JPEG is not sRGB-tagged" >&2; exit 1; }
cwebp -quiet -metadata icc "$work/icc-tagged.jpg" -o "$out/icc.webp"

# 3. Synthetic 6000x4000 JPEG for the 2560 ladder (sips -z height width).
sips -s format jpeg -s formatOptions low -z 4000 6000 "$src" --out "$out/fixture-6000x4000.jpg" >/dev/null

# Self-check: each output carries what it is meant to carry.
riff_has() { LC_ALL=C grep -c "$2" "$1" >/dev/null; }
riff_has "$out/placeholder-exif.webp" "EXIF" || { echo "placeholder-exif.webp has no EXIF chunk" >&2; exit 1; }
riff_has "$out/icc.webp" "ICCP" || { echo "icc.webp has no ICCP chunk" >&2; exit 1; }
w=$(sips -g pixelWidth "$out/placeholder-exif.webp" 2>/dev/null | awk '/pixelWidth/{print $2}')
[[ "$w" == "32" ]] || { echo "placeholder-exif.webp width is $w, expected 32" >&2; exit 1; }
dims=$(sips -g pixelWidth -g pixelHeight "$out/fixture-6000x4000.jpg" | awk '/pixel(Width|Height)/{printf "%s ", $2}')
[[ "$dims" == "6000 4000 " ]] || { echo "fixture-6000x4000.jpg is $dims, expected 6000 4000" >&2; exit 1; }

for f in placeholder-exif.webp icc.webp fixture-6000x4000.jpg; do
  echo "$f $(wc -c <"$out/$f" | tr -d ' ') bytes"
done
