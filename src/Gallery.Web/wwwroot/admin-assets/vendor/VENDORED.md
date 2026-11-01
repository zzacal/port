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
