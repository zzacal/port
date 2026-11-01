// 008 C5 module worker (contracts/components.md C5, research R-02). One photo per message:
//   in:  { file: File, widths: number[] }       widths come from the server (U4/U8), never computed here
//   out: { ok: true, sizes: [{ w, h, avif: ArrayBuffer, webp: ArrayBuffer }],
//          placeholder: { color, preview }, upright: { w, h }, timings: [{ op, ms }] }   (buffers transferred)
//     or { ok: false, reason }
// The codec factories are the vendored single-threaded emscripten builds, called directly with the
// full option objects copied from each package's meta.js defaults (research R-01): the packages'
// encode.js wrappers import a bare specifier and would pick the multithreaded AVIF build.
import avifFactory from './vendor/jsquash-avif-2.1.1/avif_enc.js';
import webpFactory from './vendor/jsquash-webp-1.5.0/webp_enc_simd.js';
import { rgbaToThumbHash, thumbHashToRGBA, thumbHashToAverageRGBA } from './vendor/thumbhash-0.1.1/thumbhash.js';

// @jsquash/avif 2.1.1 meta.js defaults (quality 50, speed 6).
const AVIF_OPTIONS = {
  quality: 50, qualityAlpha: -1, denoiseLevel: 0, tileColsLog2: 0, tileRowsLog2: 0, speed: 6,
  subsample: 1, chromaDeltaQ: false, sharpness: 0, tune: 0, enableSharpYUV: false, bitDepth: 8,
  lossless: false,
};

// @jsquash/webp 1.5.0 meta.js defaults (quality 75, method 4).
const WEBP_OPTIONS = {
  quality: 75, target_size: 0, target_PSNR: 0, method: 4, sns_strength: 50, filter_strength: 60,
  filter_sharpness: 0, filter_type: 1, partitions: 0, segments: 4, pass: 1, show_compressed: 0,
  preprocessing: 0, autofilter: 0, partition_limit: 0, alpha_compression: 1, alpha_filtering: 1,
  alpha_quality: 100, lossless: 0, exact: 0, image_hint: 0, emulate_jpeg_size: 0, thread_level: 0,
  low_memory: 0, near_lossless: 100, use_delta_palette: 0, use_sharp_yuv: 0,
};

// The placeholder preview is WebP at quality 40 (research R-02).
const PREVIEW_OPTIONS = { ...WEBP_OPTIONS, quality: 40 };

// ThumbHash's encoder accepts at most 100 px on each edge.
const THUMBHASH_MAX = 100;

let avif = null;
let webp = null;

const now = () => performance.now();
const round = (ms) => Math.round(ms * 10) / 10;

/** Draws the bitmap at w×h on an sRGB canvas and reads the pixels back as sRGB (P3 → sRGB). */
function pixels(bitmap, w, h) {
  const canvas = new OffscreenCanvas(w, h);
  const g = canvas.getContext('2d', { colorSpace: 'srgb' });
  g.imageSmoothingQuality = 'high';
  g.drawImage(bitmap, 0, 0, w, h);
  return g.getImageData(0, 0, w, h, { colorSpace: 'srgb' });
}

/** An exactly-sized, transferable copy of an encoder result. */
function toBuffer(result, what) {
  if (!result) throw new Error(`${what} encoder returned nothing`);
  if (result instanceof ArrayBuffer) return result;
  return new Uint8Array(result.buffer, result.byteOffset, result.byteLength).slice().buffer;
}

function base64(buffer) {
  const bytes = new Uint8Array(buffer);
  let s = '';
  for (let i = 0; i < bytes.length; i++) s += String.fromCharCode(bytes[i]);
  return btoa(s);
}

const hex = (v) => Math.max(0, Math.min(255, Math.round(v * 255))).toString(16).padStart(2, '0');

async function encodePhoto(file, widths) {
  const timings = [];
  const time = (op, start) => timings.push({ op, ms: round(now() - start) });
  const total = now();

  let s = now();
  avif ??= await avifFactory({ noInitialRun: true });
  webp ??= await webpFactory({ noInitialRun: true });
  time('browser.codec-init', s);

  s = now();
  const bitmap = await createImageBitmap(file, { imageOrientation: 'from-image' });
  time('browser.decode-orient', s);

  try {
    const W = bitmap.width;
    const H = bitmap.height;
    const sizes = [];
    const transfer = [];

    for (const w of widths) {
      const h = Math.max(1, Math.round(H * w / W));
      s = now();
      const img = pixels(bitmap, w, h);
      time(`browser.resize.${w}`, s);

      s = now();
      const a = toBuffer(avif.encode(new Uint8Array(img.data.buffer), w, h, AVIF_OPTIONS), 'AVIF');
      time(`browser.encode.avif.${w}`, s);

      s = now();
      const b = toBuffer(webp.encode(img.data, w, h, WEBP_OPTIONS), 'WebP');
      time(`browser.encode.webp.${w}`, s);

      sizes.push({ w, h, avif: a, webp: b });
      transfer.push(a, b);
    }

    s = now();
    const scale = Math.min(1, THUMBHASH_MAX / Math.max(W, H));
    const tw = Math.min(THUMBHASH_MAX, Math.max(1, Math.round(W * scale)));
    const th = Math.min(THUMBHASH_MAX, Math.max(1, Math.round(H * scale)));
    const small = pixels(bitmap, tw, th);
    const hash = rgbaToThumbHash(tw, th, small.data);
    const avg = thumbHashToAverageRGBA(hash);
    const ph = thumbHashToRGBA(hash);
    const preview = toBuffer(
      webp.encode(new Uint8ClampedArray(ph.rgba.buffer, ph.rgba.byteOffset, ph.rgba.byteLength), ph.w, ph.h, PREVIEW_OPTIONS),
      'preview WebP');
    const placeholder = {
      color: `#${hex(avg.r)}${hex(avg.g)}${hex(avg.b)}`,
      preview: `data:image/webp;base64,${base64(preview)}`,
    };
    time('browser.placeholder', s);
    time('browser.total', total);

    return { message: { ok: true, sizes, placeholder, upright: { w: W, h: H }, timings }, transfer };
  } finally {
    bitmap.close();
  }
}

self.onmessage = async (event) => {
  const { file, widths } = event.data ?? {};
  try {
    if (!(file instanceof Blob)) throw new Error('no file to prepare');
    if (!Array.isArray(widths) || widths.length === 0 || !widths.every((w) => Number.isInteger(w) && w > 0))
      throw new Error('no sizes to prepare');
    const { message, transfer } = await encodePhoto(file, widths);
    self.postMessage(message, transfer);
  } catch (err) {
    // Any failure, including RangeError / out of memory, fails only this photo (FR-017).
    const reason = err instanceof RangeError
      ? 'Out of memory while preparing sizes'
      : `Could not prepare sizes: ${(err && err.message) || String(err)}`;
    self.postMessage({ ok: false, reason: reason.slice(0, 200) });
  }
};
