// Probe worker: decode with orientation, resize via OffscreenCanvas, encode AVIF+WebP with vendored
// jSquash codec builds (single-threaded), ThumbHash placeholder encoded as WebP by jSquash.
import avifFactory from './vendor/avif_enc.js';
import webpFactory from './vendor/webp_enc_simd.js';
import { rgbaToThumbHash, thumbHashToRGBA, thumbHashToAverageRGBA } from './vendor/thumbhash.js';

const AVIF_OPTS = { quality: 50, qualityAlpha: -1, denoiseLevel: 0, tileColsLog2: 0, tileRowsLog2: 0, speed: 6,
  subsample: 1, chromaDeltaQ: false, sharpness: 0, tune: 0, enableSharpYUV: false, bitDepth: 8, lossless: false };
const WEBP_OPTS = { quality: 75, target_size: 0, target_PSNR: 0, method: 4, sns_strength: 50, filter_strength: 60,
  filter_sharpness: 0, filter_type: 1, partitions: 0, segments: 4, pass: 1, show_compressed: 0, preprocessing: 0,
  autofilter: 0, partition_limit: 0, alpha_compression: 1, alpha_filtering: 1, alpha_quality: 100, lossless: 0,
  exact: 0, image_hint: 0, emulate_jpeg_size: 0, thread_level: 0, low_memory: 0, near_lossless: 100,
  use_delta_palette: 0, use_sharp_yuv: 0 };
const LADDER = [320, 640, 960, 1280, 1920, 2560];

let avif, webp;
const t = () => performance.now();

function scaled(bitmap, w) {
  const h = Math.round(bitmap.height * w / bitmap.width);
  const c = new OffscreenCanvas(w, h);
  const g = c.getContext('2d', { colorSpace: 'srgb' });
  g.imageSmoothingQuality = 'high';
  g.drawImage(bitmap, 0, 0, w, h);
  return g.getImageData(0, 0, w, h, { colorSpace: 'srgb' });
}

self.onmessage = async (e) => {
  const { url, name, ladder } = e.data;
  const timings = {}; const out = [];
  try {
    let s = t();
    avif ??= await avifFactory({ noInitialRun: true });
    webp ??= await webpFactory({ noInitialRun: true });
    timings.codecInit = t() - s;
    s = t();
    const blob = await (await fetch(url)).blob();
    const bitmap = await createImageBitmap(blob, { imageOrientation: 'from-image' });
    timings.decode = t() - s;
    const widths = LADDER.filter(w => w <= bitmap.width);
    const use = (ladder ?? (widths.length ? widths : [bitmap.width]));
    for (const w of use) {
      s = t(); const img = scaled(bitmap, w); timings[`resize.${w}`] = t() - s;
      s = t(); const a = avif.encode(new Uint8Array(img.data.buffer), img.width, img.height, AVIF_OPTS); timings[`encode.avif.${w}`] = t() - s;
      s = t(); const b = webp.encode(img.data, img.width, img.height, WEBP_OPTS); timings[`encode.webp.${w}`] = t() - s;
      out.push({ name: `${name}-${w}.avif`, bytes: a.buffer.slice(0), w, h: img.height });
      out.push({ name: `${name}-${w}.webp`, bytes: b.buffer.slice(0), w, h: img.height });
    }
    s = t();
    const tw = Math.max(1, Math.round(Math.min(100, bitmap.width * 100 / Math.max(bitmap.width, bitmap.height))));
    const small = scaled(bitmap, tw);
    const hash = rgbaToThumbHash(small.width, small.height, small.data);
    const avg = thumbHashToAverageRGBA(hash);
    const ph = thumbHashToRGBA(hash);
    const pb = webp.encode(new Uint8ClampedArray(ph.rgba.buffer), ph.w, ph.h, { ...WEBP_OPTS, quality: 40 });
    timings.placeholder = t() - s;
    const hex = '#' + [avg.r, avg.g, avg.b].map(v => Math.round(v * 255).toString(16).padStart(2, '0')).join('');
    out.push({ name: `${name}-placeholder.webp`, bytes: pb.buffer.slice(0), w: ph.w, h: ph.h });
    self.postMessage({ ok: true, name, src: [bitmap.width, bitmap.height], color: hex, timings, out });
  } catch (err) {
    self.postMessage({ ok: false, name, error: String(err), timings });
  }
};
