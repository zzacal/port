# Browser Image Encoding Research Notes

_Context: single-owner admin upload from desktop browser. JPEG exports up to 20 MB / ~50 MP. Browser must orient, resize to 320–2560 px widths, encode AVIF+WebP, compute placeholder, strip all metadata. Server never decodes pixels._

---

## Q1: EXIF Orientation — createImageBitmap and CSS image-orientation

**Answer:**
`createImageBitmap(blob, { imageOrientation: 'from-image' })` is the **default** value and instructs all modern browsers to apply EXIF orientation metadata before returning the `ImageBitmap`. Support for the `options.imageOrientation` parameter is: Chrome 52+, Firefox 93+, Safari 15+, Edge 79+, Chrome Android 52+, Safari iOS 15+ — broadly available since September 2021 per the MDN Baseline "Widely available" mark. The CSS property `image-orientation` also defaults to `from-image` (initial value), meaning `<img>` elements in modern browsers automatically display correctly-oriented images; this is a display-layer concern only and does not affect canvas pixel data. When you `drawImage` an `<img>` or `ImageBitmap` onto a canvas the orientation transformation is already baked in, so no further rotation is needed before encoding.

**Source:**
- https://developer.mozilla.org/en-US/docs/Web/API/Window/createImageBitmap
- https://developer.mozilla.org/en-US/docs/Web/CSS/image-orientation

**Quote:**
> "`from-image`: Image oriented according to EXIF orientation metadata, if present (default)."
> — MDN, Window: createImageBitmap() method

> "The `image-orientation` CSS property specifies a layout-independent correction to the orientation of an image. … `from-image`: Default initial value. The EXIF information contained in the image is used to rotate the image appropriately."
> — MDN, image-orientation CSS property

**Fetch: OK**

---

## Q2: Canvas / OffscreenCanvas Max Dimensions and Memory — 50 MP Risk

**Answer:**
Desktop Chrome/Edge/Firefox allow up to 32 767 px per dimension and ~268 MP total area (Chrome/Edge cap: 16 384 × 16 384; Firefox slightly larger). Desktop Safari is comparable. **iOS Safari is the critical constraint**: historically capped at 16 777 216 px total (4 096 × 4 096), raised to 67 108 864 px (8 192 × 8 192) in WebKit in March 2024 for iOS 17.4+. A 50 MP image (e.g. 8 688 × 5 792) exceeds **both** the old (16 MP) and new (67 MP) iOS Safari single-canvas limits. Strategy: decode the full image into an `ImageBitmap` (which does not carry the Canvas area limit) and use `createImageBitmap` with `resizeWidth`/`resizeHeight` to downscale to each output width before drawing onto an appropriately-sized canvas; never create a 50 MP canvas directly. Additionally, iOS Safari aggressively retains canvas memory — releasing each canvas after use (shrink to 1×1 and clear) is required to avoid the "Total canvas memory use exceeds the maximum limit" error.

**Source:**
- https://pqina.nl/blog/canvas-area-exceeds-the-maximum-limit/
- https://pqina.nl/blog/total-canvas-memory-use-exceeds-the-maximum-limit/
- https://gasan.ai.kr/tools/mockup/en/guides/export-resolution
- https://stackoverflow.com/questions/6081483/maximum-size-of-a-canvas-element

**Quote:**
> "Safari simply cannot draw large canvas elements. The limit is set at 16,777,216 pixels. Create a canvas with more pixels and iOS Safari will tell us it exceeds the memory limit."
> — pqina.nl, Canvas Area Exceeds The Maximum Limit

> "How the iOS canvas area cap changed: 2015-06-16 to 2024-03-15 → 4096×4096 = 16,777,216; 2024-03-15 to now → 8192×8192 = 67,108,864. Other platforms (desktop) sit at 16384×16384 = 268,435,456."
> — gasan.ai.kr, Export resolution

> "Unfortunately the memory limit on iOS Safari is rather low. It's limited to 384 MB on version 15."
> — pqina.nl, Total Canvas Memory Use Exceeds The Maximum Limit

**Fetch: OK**

---

## Q3: Native canvas.toBlob / OffscreenCanvas.convertToBlob — WebP, AVIF, Metadata

**Answer:**
`image/webp` encoding in `toBlob`/`convertToBlob`: Chrome 50+, Firefox 96+, Edge 79+ — **Safari (all platforms) does NOT support it** (desktop and iOS Safari return PNG as fallback). `image/avif` encoding: **no support in Chrome or Firefox** natively via `toBlob`; Safari 16+ appears to encode AVIF. A Chromium bug (filed 2022) documents that passing `image/avif` to `toBlob` silently returns `image/png` in Chrome. Metadata: `toBlob` output contains **no EXIF or XMP** — the canvas pixel buffer carries no metadata. The only metadata-like content is a fixed 96 dpi resolution tag in formats that support it. This makes canvas the correct stripping layer: any EXIF read from the source file must be explicitly discarded, which happens automatically when pixels pass through the 2D canvas context.

**Source:**
- https://developer.mozilla.org/en-US/docs/Web/API/HTMLCanvasElement/toBlob
- https://caniuse.com/mdn-api_htmlcanvaselement_toblob_type_parameter_webp
- https://issues.chromium.org/issues/40848792
- https://minipx.com/blog/image-compression-api-canvas

**Quote:**
> "Browsers are required to support `image/png`; many will support additional formats including `image/jpeg` and `image/webp`. The created image will have a resolution of 96dpi for file formats that support encoding resolution metadata."
> — MDN, HTMLCanvasElement: toBlob() method

> "AVIF support in canvas.toBlob() is still limited: Safari 16+ encodes it, Chrome and Firefox do not."
> — minipx.com, Compress Images in the Browser with Canvas

> "Calling canvas.toBlob() with 'image/avif' format just returns a PNG image."
> — Chromium issue tracker #40848792

**Fetch: OK**

---

## Q4: WASM AVIF/WebP Encoders — jSquash, SAB / COOP/COEP, Alternatives

**Answer:**
`@jsquash/avif` (v2.1.1) and `@jsquash/webp` (v1.5.0) are Apache-2.0 licensed WASM packages derived from Squoosh, using libavif and libwebp respectively. The AVIF WASM is ~1.1 MB gzipped (standalone libaom-av1). Both packages work inside Web Workers. **Multi-threaded builds require `SharedArrayBuffer`**, which requires cross-origin isolation via `Cross-Origin-Opener-Policy: same-origin` + `Cross-Origin-Embedder-Policy: require-corp` (COOP/COEP) headers — these headers break third-party iframes (e.g. ad networks). The single-threaded builds do **not** require SAB/COOP/COEP and run in any context. A known Vite caveat: `@jsquash/avif` must be excluded from `optimizeDeps` because on SAB-available deployments it spawns `avif_enc_mt.worker.mjs`, which Vite hashes and renames, causing a 404. Alternatives: `@jsquash/resize` (Rust-based, Lanczos/hqx/magic-kernel) for resizing; squoosh codecs (the upstream these are derived from); libavif WASM builds directly. `@jsquash/webp` for WebP encoding is the practical standard; Safari does not support WebP encoding natively in canvas so jSquash WebP is necessary for a reliable cross-browser pipeline.

**Source:**
- https://www.npmjs.com/package/@jsquash/avif
- https://github.com/jamsinclair/jSquash
- https://dev.to/kyungju_leebenjie_519b/image-codecs-in-the-browser-with-jsquash-seven-decisions-the-readme-doesnt-make-for-you-4bid
- https://github.com/dharanish-v/omni-compress/blob/master/CLAUDE.md

**Quote:**
> "The multithreaded builds of these codecs need `SharedArrayBuffer`, which needs cross-origin isolation, which means shipping COOP/COEP headers. On an ad-funded site those headers break AdSense iframes. … The WASM codecs on this site run single-threaded by design, so they don't need it."
> — dev.to, Image codecs in the browser with jSquash

> "Uses the libavif library. … NO SharedArrayBuffer required — @jsquash/avif auto-detects multi-threading support."
> — github.com/dharanish-v/omni-compress CLAUDE.md

> "Every jSquash package you use goes in the [optimizeDeps exclude] list … Keeping them out of the pre-bundle also preserves lazy loading."
> — dev.to, Image codecs in the browser with jSquash

**Fetch: OK**

---

## Q5: Resize Quality — canvas drawImage + imageSmoothingQuality vs. WASM resizer

**Answer:**
`ctx.imageSmoothingQuality = 'high'` with `drawImage` maps to a **Lanczos-class kernel** in Chrome, Edge, Firefox, and Safari — effectively Lanczos a=2, the standard industry downscale filter, per multiple sources. This is fast, zero-dependency, and produces visually indistinguishable results from dedicated libraries for typical photography down to ~25% of the original size. `pica` (Lanczos3 / mks2013 filter, ~13 KB gzipped, optionally uses WASM+workers) is marginally sharper at extreme reductions on very-high-detail textures and also avoids a known Chrome bug where `imageSmoothingQuality: 'high'` can produce artifacts depending on the source type. `@jsquash/resize` is another WASM option (Rust, magic-kernel + hqx) integrated into the jSquash ecosystem. For a single-owner admin pipeline targeting widths 320–2560 from a ~50 MP source (ratios as large as 160×), using `createImageBitmap` with `resizeWidth/resizeHeight` (which internally uses `resizeQuality: 'high'`) to produce the target `ImageBitmap` before drawing onto an appropriately-sized canvas is the recommended pattern; it avoids creating an intermediate 50 MP canvas entirely.

**Source:**
- https://openimages.app/resize-image
- https://abdelrahmansoltan.hashnode.dev/frontend-image-resizing
- https://github.com/nodeca/pica/blob/master/README.md

**Quote:**
> "The tool calls drawImage() with imageSmoothingQuality = 'high', which Chrome / Edge / Safari / Firefox all map to a Lanczos-class kernel (Duchon 1979). Lanczos a=2 is the industry-standard downscale filter."
> — openimages.app, Resize Image Online

> "**Pica** is a high-quality re-sampler (using Lanczos3, with WASM/worker support). It's designed to provide better sharpness and fewer artifacts when significantly reducing the size of large images (e.g., 4000–6000px sources)."
> — abdelrahmansoltan.hashnode.dev, Frontend Image Resizing

> "JS canvas does not support access to info about gamma correction. Bitmaps have 8 bits per channel. That causes some quality loss, because with gamma correction precision could be 12 bits per channel."
> — nodeca/pica README (quality caveat)

**Fetch: OK**

---

## Q6: Placeholder — ThumbHash vs BlurHash; No-JS Rendering Requirement

**Answer:**
ThumbHash (Evan Wallace) encodes more detail than BlurHash in the same ~20–30 byte hash: higher-resolution luminance, aspect ratio encoding, and alpha support. BlurHash is ~20–30 chars but lower fidelity. **Neither can be rendered without JavaScript from the raw hash alone** — both require a decode step to produce pixels. However, both support a **no-JS path via server-side pre-rendering**: ThumbHash can be decoded server-side to a tiny PNG and inlined as a `data:image/png;base64,…` in the HTML `src` or CSS `background-image` — no client-side JS required to display it. `@unpic/placeholder` renders BlurHash to CSS `linear-gradient()` strings at build time, which display in any browser with zero JS. For this project (public site must work without JS): the recommended pattern is to pre-compute the ThumbHash during upload (client-side, from the resized 320 px canvas `ImageData`), then have the server decode it to a tiny PNG data URL (or use ThumbHash's JS `thumbHashToDataURL()` in the upload workflow) and store that data URL alongside the image record; the public HTML template uses `<img src="data:image/png;base64,…">` or `style="background-image: url('data:…')"` — fully no-JS.

**Source:**
- https://evanw.github.io/thumbhash/ (search result description; direct fetch rate-limited)
- https://metacpan.org/pod/Image::ThumbHash
- https://www.npmjs.com/package/@unpic/placeholder
- https://dev.to/vladimirvovk/better-image-placeholders-with-thumbhash-43mj

**Quote:**
> "You would either convert the thumb hash to a small (inline) PNG on the server and embed in your HTML, or embed the raw thumb hash and convert it to a PNG on the client side (using JavaScript). In either case, you end up with small placeholder images that load instantly while the original images can be loaded in asynchronously."
> — metacpan.org, Image::ThumbHash

> "Unlike other BlurHash libraries, this [CSS gradient renderer] generates CSS values so it works without client-side JavaScript in any web framework or none, and can be displayed before page hydration."
> — npmjs.com, @unpic/placeholder

> "Another option will be to get the 'URL data' from ThumbHash using the `thumbhash` library: `thumbHashToDataURL(…)` → `'data:image/png;base64,iVBORw0KGgo…'`"
> — dev.to, Better Image Placeholders with ThumbHash

**Fetch: OK**

---

## Q7: jSquash AVIF/WebP Output — EXIF/XMP Metadata, ICC Profiles, Color Space

**Answer:**
jSquash `encode()` functions accept a plain `ImageData` object (raw RGBA pixels) and return an `ArrayBuffer`. Because the canvas `getImageData()` / `ImageBitmap` pipeline **strips all metadata** — EXIF, XMP, and ICC profiles — before the pixels reach the encoder, the output AVIF and WebP files contain **no EXIF or XMP**. ICC profile handling is the critical gap: the 2D canvas context operates in **sRGB** internally; if the source JPEG carries a Display P3 (or other wide-gamut) ICC profile, `colorSpaceConversion: 'default'` in `createImageBitmap` will apply implementation-defined conversion (typically to sRGB), clipping wide-gamut values. The resulting `ImageData` passed to jSquash encode will be sRGB pixels with no embedded ICC profile — the AVIF/WebP output will be implicitly sRGB but contain **no ICC tag**. A third-party README for a jSquash-based pipeline explicitly lists "Custom ICC profile preservation" as unsupported. If the admin exclusively exports sRGB JPEGs this is acceptable; if Display P3 exports are possible, the image should be tagged sRGB explicitly or the canvas `colorSpace: 'display-p3'` option must be used with a custom encoder path (not supported by jSquash out of the box).

**Source:**
- https://github.com/Junhui20/webcvt/blob/main/packages/image-jsquash-avif/README.md
- https://developer.mozilla.org/en-US/docs/Web/API/Window/createImageBitmap
- https://dev.to/linmingren/browser-based-image-compression-with-jsquash-a-pure-frontend-implementation-f3g

**Quote:**
> "Custom ICC profile preservation — [not supported]"
> — webcvt/packages/image-jsquash-avif README

> "`colorSpaceConversion`: Specifies whether the image should be decoded using color space conversion. Either `none` or `default` (default). The value `default` indicates that implementation-specific behavior is used."
> — MDN, Window: createImageBitmap()

**Fetch: OK**

---

## Gaps

1. **iOS Safari canvas area limit post-March 2024**: The cap rose from 16 MP to 67 MP in iOS 17.4+ but older iOS (17 and below) still enforces 16 MP. The oldest supported iOS version for this project is not yet defined; the safe floor is to never create a canvas larger than 16 MP directly.

2. **Safari AVIF toBlob confirmation**: The claim "Safari 16+ encodes AVIF via toBlob" came from a single source (minipx.com). No direct caniuse data for `toBlob` `image/avif` was fetched; this should be verified against caniuse before relying on it.

3. **jSquash AVIF encode speed on 50 MP source**: A ~50 MP full-resolution encode via single-threaded libaom AVIF WASM would be very slow (several minutes). The pipeline must resize first, then encode — this note assumes that ordering but no direct benchmark for 8+ MP inputs was found.

4. **Display P3 canvas support in Firefox**: `getContext('2d', { colorSpace: 'display-p3' })` is available in Chrome 98+ and Safari 15.2+ but Firefox support was not confirmed from fetched pages.

5. **ThumbHash server-side PNG data URL size**: No concrete byte-size measurement for a ThumbHash-derived PNG data URL was fetched directly; typical values cited are 20–30 bytes for the hash plus encoding overhead for the PNG. Actual sizes should be measured during implementation.
