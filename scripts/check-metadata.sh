#!/usr/bin/env bash
# Structural metadata check for AVIF/WebP (feature 003, Constitution III.3).
# A raw byte grep is wrong for compressed files: "GPS" occurs by chance inside
# entropy-coded pixel data. This parses the container instead (as D-10 does):
#   WebP: walk the RIFF chunks; fail on EXIF or XMP chunks, or VP8X EXIF/XMP flags.
#   AVIF: walk the top-level boxes, then the meta/iinf item infos; fail on an
#         'Exif' item or a 'mime' item (XMP).
# Usage: check-metadata.sh FILE...   Exit status = number of files that failed.
set -uo pipefail

u32be() { od -An -v -tu1 -j "$2" -N4 "$1" | tr -s ' \n' ' ' | awk '{print ($1*16777216)+($2*65536)+($3*256)+$4}'; }
u32le() { od -An -v -tu1 -j "$2" -N4 "$1" | tr -s ' \n' ' ' | awk '{print ($4*16777216)+($3*65536)+($2*256)+$1}'; }
fourcc() { dd if="$1" bs=1 skip="$2" count=4 2>/dev/null; }
byte()  { od -An -v -tu1 -j "$2" -N1 "$1" | tr -d ' \n'; }

check_webp() {
  local f="$1" size off id len
  [ "$(fourcc "$f" 0)" = RIFF ] && [ "$(fourcc "$f" 8)" = WEBP ] || { echo "not webp"; return 1; }
  size=$(stat -f%z "$f"); off=12
  while [ $((off + 8)) -le "$size" ]; do
    id="$(fourcc "$f" "$off")"; len=$(u32le "$f" $((off + 4)))
    case "$id" in
      EXIF|"XMP ") echo "chunk $id"; return 1 ;;
      VP8X) local flags; flags=$(byte "$f" $((off + 8)))
            if [ $((flags & 8)) -ne 0 ] || [ $((flags & 4)) -ne 0 ]; then echo "VP8X EXIF/XMP flag"; return 1; fi ;;
    esac
    off=$((off + 8 + len + (len % 2)))
  done
  return 0
}

check_avif() {
  local f="$1" size off len typ
  size=$(stat -f%z "$f"); off=0
  [ "$(fourcc "$f" 4)" = ftyp ] || { echo "not avif"; return 1; }
  while [ $((off + 8)) -le "$size" ]; do
    len=$(u32be "$f" "$off"); typ="$(fourcc "$f" $((off + 4)))"
    [ "$len" -ge 8 ] || { echo "bad box"; return 1; }
    if [ "$typ" = meta ]; then
      # meta is a FullBox: children start at off+12
      local c=$((off + 12)) end=$((off + len))
      while [ $((c + 8)) -le "$end" ]; do
        local cl ct; cl=$(u32be "$f" "$c"); ct="$(fourcc "$f" $((c + 4)))"
        [ "$cl" -ge 8 ] || break
        if [ "$ct" = iinf ]; then
          # iinf FullBox; entry count is u16 (v0) or u32 (v1); scan infe children for item_type
          local v; v=$(byte "$f" $((c + 8)))
          local e=$((c + 12 + (v == 0 ? 2 : 4))) iend=$((c + cl))
          while [ $((e + 8)) -le "$iend" ]; do
            local el ev; el=$(u32be "$f" "$e"); ev=$(byte "$f" $((e + 8)))
            [ "$el" -ge 8 ] || break
            # infe v2: item_ID u16, protection u16, item_type 4cc ; v3: item_ID u32
            local tpos=$((e + 12 + (ev >= 3 ? 4 : 2) + 2))
            local itype; itype="$(fourcc "$f" "$tpos")"
            case "$itype" in Exif|mime) echo "item $itype"; return 1 ;; esac
            e=$((e + el))
          done
        fi
        c=$((c + cl))
      done
    fi
    off=$((off + len))
  done
  return 0
}

fails=0
for f in "$@"; do
  case "$f" in
    *.webp) msg="$(check_webp "$f")" ;;
    *.avif) msg="$(check_avif "$f")" ;;
    *) continue ;;
  esac
  if [ $? -ne 0 ] || [ -n "${msg:-}" ]; then echo "METADATA: $f ($msg)"; fails=$((fails + 1)); fi
done
exit "$fails"
