#!/usr/bin/env bash
# Vendors the browser encoders used by the 008 upload pipeline (research R-01).
# Downloads pinned npm tarballs with curl, verifies each against its registry
# dist.integrity (sha512, base64), and extracts only the named members into
# src/Gallery.Web/wwwroot/admin-assets/vendor/. No npm or node is needed or run.
# Exits non-zero on any download failure or integrity mismatch.
#
# Usage: bash scripts/vendor-encoders.sh
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
vendor="$repo_root/src/Gallery.Web/wwwroot/admin-assets/vendor"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

# name|version|tarball|integrity|dest|members (member=flattened-name, space separated)
packages=(
  "@jsquash/avif|2.1.1|https://registry.npmjs.org/@jsquash/avif/-/avif-2.1.1.tgz|sha512-LMRxd0fMgfCLtobDh0/sFYJMMiRJTNYSEEWvRDKXlAeZ08t3gI5V+1thIT0XjXJ+SVG7Zug9B0XPyx0Ti5VRNA==|jsquash-avif-2.1.1|package/codec/enc/avif_enc.js=avif_enc.js package/codec/enc/avif_enc.wasm=avif_enc.wasm package/LICENSE=LICENSE"
  "@jsquash/webp|1.5.0|https://registry.npmjs.org/@jsquash/webp/-/webp-1.5.0.tgz|sha512-KggLoj2MnRSfIqTeKe1EmbljTX2vuV7mh79k89PCL1pyqiDULcPM1L47twxXt0hkb68F70bXiL31MxsuoZtKFw==|jsquash-webp-1.5.0|package/codec/enc/webp_enc_simd.js=webp_enc_simd.js package/codec/enc/webp_enc_simd.wasm=webp_enc_simd.wasm package/LICENSE=LICENSE package/codec/LICENSE.codec.md=LICENSE.codec.md"
  "thumbhash|0.1.1|https://registry.npmjs.org/thumbhash/-/thumbhash-0.1.1.tgz|sha512-kH5pKeIIBPQXAOni2AiY/Cu/NKdkFREdpH+TLdM0g6WA7RriCv0kPLgP731ady67MhTAqrVG/4mnEeibVuCJcg==|thumbhash-0.1.1|package/thumbhash.js=thumbhash.js"
)

mkdir -p "$vendor"

for entry in "${packages[@]}"; do
  IFS='|' read -r name version url integrity dest members <<<"$entry"
  tgz="$work/$dest.tgz"
  echo "== $name@$version"
  curl -sSfL -o "$tgz" "$url"

  actual="sha512-$(shasum -a 512 "$tgz" | cut -d' ' -f1 | xxd -r -p | base64)"
  if [[ "$actual" != "$integrity" ]]; then
    echo "INTEGRITY MISMATCH for $name@$version" >&2
    echo "  expected $integrity" >&2
    echo "  actual   $actual" >&2
    exit 1
  fi
  echo "   integrity OK"

  extract="$work/$dest"
  mkdir -p "$extract"
  member_paths=()
  for m in $members; do member_paths+=("${m%%=*}"); done
  tar -xzf "$tgz" -C "$extract" "${member_paths[@]}"

  rm -rf "${vendor:?}/$dest"
  mkdir -p "$vendor/$dest"
  for m in $members; do
    src="${m%%=*}"; out="${m#*=}"
    cp "$extract/$src" "$vendor/$dest/$out"
    echo "   $dest/$out ($(wc -c <"$vendor/$dest/$out" | tr -d ' ') bytes)"
  done
done

cat >"$vendor/VENDORED.md" <<'EOF'
# Vendored browser encoders

Static files only, served under `/admin-assets/vendor/` (008 research R-01, R-12). The app build
never runs npm or node. Do not edit these files by hand.

Regenerate (verifies sha512 integrity, exits non-zero on mismatch):

```bash
bash scripts/vendor-encoders.sh
```

Integrity strings are the registry `dist.integrity` values, read from
`https://registry.npmjs.org/<package>/<version>` metadata on 2026-11-01 (research R-01 records the
same values, abbreviated).

| Package | Version | Source | Integrity | License | Files |
|---|---|---|---|---|---|
| `@jsquash/avif` | 2.1.1 | https://registry.npmjs.org/@jsquash/avif/-/avif-2.1.1.tgz | `sha512-LMRxd0fMgfCLtobDh0/sFYJMMiRJTNYSEEWvRDKXlAeZ08t3gI5V+1thIT0XjXJ+SVG7Zug9B0XPyx0Ti5VRNA==` | Apache-2.0 (`jsquash-avif-2.1.1/LICENSE`) | `avif_enc.js`, `avif_enc.wasm`, `LICENSE` (from `codec/enc/` and the package root) |
| `@jsquash/webp` | 1.5.0 | https://registry.npmjs.org/@jsquash/webp/-/webp-1.5.0.tgz | `sha512-KggLoj2MnRSfIqTeKe1EmbljTX2vuV7mh79k89PCL1pyqiDULcPM1L47twxXt0hkb68F70bXiL31MxsuoZtKFw==` | Apache-2.0 (`LICENSE`); codec BSD-3-Clause (`LICENSE.codec.md`) | `webp_enc_simd.js`, `webp_enc_simd.wasm`, `LICENSE`, `LICENSE.codec.md` |
| `thumbhash` | 0.1.1 | https://registry.npmjs.org/thumbhash/-/thumbhash-0.1.1.tgz | `sha512-kH5pKeIIBPQXAOni2AiY/Cu/NKdkFREdpH+TLdM0g6WA7RriCv0kPLgP731ady67MhTAqrVG/4mnEeibVuCJcg==` | MIT (notice below; the tarball has no LICENSE file) | `thumbhash.js` |

## thumbhash MIT notice

Source: https://github.com/evanw/thumbhash (LICENSE).

```
Copyright (c) 2023 Evan Wallace

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```
EOF
echo "== wrote $vendor/VENDORED.md"
