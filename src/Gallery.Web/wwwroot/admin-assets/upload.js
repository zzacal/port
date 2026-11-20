// 020 C13 upload tray (contracts/components.md K7, K10, C13; contracts/http.md U2–U9, U12, U13).
// Registers <upload-tray>. Loaded ONLY by dynamic import() from workspace.js (Add photos, a file drop
// on the window, or U2 reporting a batch when the page loads); it is never a <script> of its own.
//
// Kept from 008 C4: register (U3), the original PUT, the master check (U4), the module worker's
// encode, size PUTs (at most 4 at once), heartbeats (U5), complete (U7), fail (U6), restart (U8),
// remove (U9), reconcile from U2, the at-pick refusals, and the FR-044 storage PUT rule (exactly one
// header, Content-Type, credentials "omit"). App calls send X-CSRF-TOKEN, credentials "same-origin".
//
// New (020): each photo joins on its own. After U7 succeeds, and for every succeeded/joining item a
// reconcile finds, the page calls U12; on success it tells workspace.js (CustomEvent
// "upload:joined") so the cell appears. Done / Close call U13, then the tray empties and the New
// marks go ("upload:closed"). Messages go to the workspace's Toast ("upload:said"), always the
// server's own sentence. The batch confirm, discard and footer of 008 are gone.
//
// Destination and [SAFETY II.5a]: before the first register the tray names the open collection
// (a gallery's title and data-public come from its CollectionList row); after it, the batch's stored
// destination (U3/U2 `destination`). U3 sends `gallery` = that destination and
// `visibleToVisitors: true` ONLY while the tray is showing the public notice (`.ws-notice`).
//
// Markup (008 R-11, 020 C12/C13): everything is cloned from the server-rendered
// <template class="ws-tpl-tray-*|ws-tpl-row-*|ws-tpl-refusals"> in the chrome and filled through
// textContent / setAttribute. Strings in the templates are sentinels (U+2063 key U+2063), replaced in
// text nodes and attribute values, so a file name holding "<" can never become markup. The heading's
// counts are integers, replaced by position (see `numbered`).

const MARK = '\u2063';
const SENTINEL = /\u2063(\w+)\u2063/g;
const HEARTBEAT_MS = 20_000;
const ORPHAN_RECHECK_MS = 15_000;
const MAX_PUTS = 4;
const STATE_URL = '/admin/upload/state';
const ACTIVE = new Set(['queued', 'processing', 'uploading']);
const WORKING = new Set(['queued', 'processing', 'uploading', 'succeeded', 'joining']);
const JOINABLE = new Set(['succeeded', 'joining']);
/** Item state → UploadRow state (K7); `added` is the page's own word for a joined item. */
const ROW = {
  queued: 'waiting', processing: 'preparing', uploading: 'uploading', succeeded: 'adding', joining: 'adding',
  added: 'added', failed: 'failed', interrupted: 'interrupted',
};

const host = () => document.querySelector('admin-workspace');
const tray = () => host()?.querySelector('upload-tray') ?? null;
const list = () => host()?.querySelector('.ws-list') ?? null;

// ── state (one tray per page; the element is moved across navigation, never re-created) ──────

let open = false;
let hidden = false;
let batchId = '';
/** The batch's stored destination: undefined until the server names it, null for the library. */
let destination;
/** itemId → { itemId, attemptId, fileName, fileSize, state, percent, reason, masterStored, file, run, pickAgain, born, el, elKey, joining } */
const items = new Map();
/** Ids of the photos this set has added (U2 `added` plus this page's joins). */
const addedIds = new Set();
let refusals = [];
let refusalsEl = null;
let shape = '';
const queue = [];
let busy = false;
let current = null;
let worker = null;
let pendingEncode = null;
let generation = 0; // bumped by every U2 reconcile
let orphanTimer = 0;

const emit = (name, detail = null) => document.dispatchEvent(new CustomEvent(name, { detail }));

// ── helpers ─────────────────────────────────────────────────────────────────────────────────

async function api(path, body) {
  const init = {
    method: body === undefined ? 'GET' : 'POST',
    credentials: 'same-origin',
    headers: { 'X-CSRF-TOKEN': host()?.dataset.csrf ?? '' },
  };
  if (body !== undefined) {
    init.headers['Content-Type'] = 'application/json';
    init.body = JSON.stringify(body);
  }
  try {
    const r = await fetch(path, init);
    let json = null;
    try { json = await r.json(); } catch { json = null; }
    return { status: r.status, json };
  } catch {
    return { status: 0, json: null };
  }
}

const itemUrl = (item, tail) => `/admin/upload/items/${encodeURIComponent(item.itemId)}/${tail}`;

// Storage PUT: exactly one header, no credentials. One retry (decisions row 48).
async function put(url, body, contentType) {
  let status = 0;
  for (let attempt = 0; attempt < 2; attempt++) {
    try {
      const r = await fetch(url, { method: 'PUT', body, credentials: 'omit', headers: { 'Content-Type': contentType } });
      if (r.ok) return { ok: true, status: r.status };
      status = r.status;
    } catch {
      status = 0;
    }
  }
  return { ok: false, status };
}

const putFailure = (status) => `upload failed (${status || 'network error'})`;

function template(cls) {
  return list()?.querySelector(`template.${cls}`) ?? null;
}

function clone(cls) {
  return template(cls)?.content.firstElementChild?.cloneNode(true) ?? null;
}

function fill(el, values) {
  const swap = (s) => s.replace(SENTINEL, (_, k) => values[k] ?? '');
  const walker = document.createTreeWalker(el, NodeFilter.SHOW_ELEMENT | NodeFilter.SHOW_TEXT);
  for (let node = walker.currentNode; node; node = walker.nextNode()) {
    if (node.nodeType === Node.TEXT_NODE) {
      if (node.nodeValue.includes(MARK)) node.nodeValue = swap(node.nodeValue);
    } else {
      for (const attr of Array.from(node.attributes)) {
        if (attr.value.includes(MARK)) node.setAttribute(attr.name, swap(attr.value));
      }
    }
  }
  return el;
}

/** The template's integers, in order, replaced by `values` (the server text keeps its words). */
function numbered(text, values) {
  let i = 0;
  return text.replace(/\d+/g, (d) => (i < values.length ? String(values[i++]) : d));
}

const maxBytes = () => Number(tray()?.dataset.maxBytes);
const maxPhotos = () => Number(tray()?.dataset.maxPhotos);

function overLimit(size) {
  const limit = Math.round((maxBytes() / 1_048_576) * 10) / 10;
  return `${(size / 1_048_576).toFixed(1)} MB, over the ${limit} MB limit`;
}

// ── destination ─────────────────────────────────────────────────────────────────────────────

/** The open gallery, named by its CollectionList row; null for All photos and Not in a gallery. */
function openGallery() {
  const m = /^\/admin\/g\/([^/]+)$/.exec(location.pathname);
  if (!m) return null;
  const row = [...(list()?.querySelectorAll('a.ws-coll-row[data-gid]') ?? [])].find((a) => a.dataset.gid === m[1]);
  if (!row) return null;
  return { id: m[1], title: row.querySelector('.ws-coll-name')?.textContent ?? '', public: row.dataset.public === 'true' };
}

/** The set holds something: items, or photos it has already added. */
const filled = () => items.size > 0 || addedIds.size > 0;

/** Where the photos go: the stored destination once the set holds something, else the open collection. */
const target = () => (filled() && destination !== undefined ? destination : openGallery());

/** [SAFETY II.5a] The public notice is on screen in the tray (plain text, not inside <details>). */
function noticeShown() {
  const notice = tray()?.querySelector(':scope > .ws-tray > .ws-notice');
  return !!notice && !tray().hidden && notice.getClientRects().length > 0 && notice.closest('details') === null;
}

// ── the tray ────────────────────────────────────────────────────────────────────────────────

function counts() {
  const all = [...items.values()];
  const failed = all.filter((i) => i.state === 'failed' || i.state === 'interrupted').length;
  const pending = all.filter((i) => i.state !== 'added').length;
  const working = all.some((i) => WORKING.has(i.state));
  const added = addedIds.size;
  return { added, total: added + pending, failed, working };
}

function rowFor(item) {
  const kind = ROW[item.state] ?? 'waiting';
  const key = [kind, item.percent, item.reason ?? '', item.pickAgain, item.fileName].join('|');
  if (item.el && item.elKey === key) return item.el;
  const el = clone(`ws-tpl-row-${kind}`);
  if (!el) return item.el ?? null;
  fill(el, { name: item.fileName, reason: item.reason ?? '', id: item.itemId });
  if (kind === 'uploading') {
    const bar = el.querySelector('progress');
    bar?.setAttribute('value', String(item.percent));
    if (bar) bar.textContent = `${item.percent}%`;
  }
  if (item.pickAgain) addPickAgain(el, item);
  if (item.el?.isConnected) item.el.replaceWith(el);
  item.el = el;
  item.elKey = key;
  return el;
}

function addPickAgain(el, item) {
  const label = el.querySelector('.ws-up-state');
  if (label) label.textContent = `Pick ${item.fileName} again`;
  const picker = document.createElement('input');
  picker.type = 'file';
  picker.accept = 'image/jpeg';
  picker.className = 'pick-again';
  picker.setAttribute('aria-label', `Pick ${item.fileName} again`);
  picker.addEventListener('change', () => {
    const file = picker.files && picker.files[0];
    picker.value = '';
    if (file) pickedAgain(item, file);
  });
  (label ?? el).after(picker);
}

function refusalList() {
  if (refusals.length === 0) return null;
  if (refusalsEl) return refusalsEl;
  const listEl = clone('ws-tpl-refusals')?.querySelector('.dz-refusals');
  const row = listEl?.querySelector('li');
  if (!listEl || !row) return null;
  listEl.replaceChildren(...refusals.map((r) => fill(row.cloneNode(true), { name: r.name, reason: r.reason })));
  refusalsEl = listEl;
  return listEl;
}

function showRefusals(next) {
  refusals = next;
  refusalsEl?.remove();
  refusalsEl = null;
  render();
}

/** A fresh tray section for `key`, filled for `dest`. */
function build(kind, key, dest) {
  const section = clone(`ws-tpl-tray-${kind}-${key}`);
  if (!section) return null;
  fill(section, { dest: dest?.title ?? '' });
  if (!dest?.public) section.querySelector('.ws-notice')?.remove();
  section.querySelector('.ws-tray-rows')?.replaceChildren();
  // "Hide" is offered while the tray has rows (K10).
  if (items.size === 0) section.querySelector('button[name="tray"][value="hide"]')?.remove();
  // The file picker: the dropzone's own input when empty, else a hidden copy of it for Add photos while working.
  if (!section.querySelector('input.dz-input')) {
    const input = clone(`ws-tpl-tray-${kind}-empty`)?.querySelector('input.dz-input');
    if (input) section.append(input);
  }
  return section;
}

const SWAPPED_FOCUS = { hide: 'show', show: 'hide' };

/** Working into another collection than the open one: a set has one destination, so Add photos is
 *  unavailable (Button's disabled state) until it finishes; the tray's heading names where it is adding. */
const busyElsewhere = () => counts().working && (destination?.id ?? null) !== (openGallery()?.id ?? null);

function syncAdd() {
  const add = document.querySelector('.ws-main .ws-head .ws-add');
  if (add instanceof HTMLButtonElement) add.disabled = busyElsewhere();
}

function render() {
  syncAdd();
  const el = tray();
  if (!el) return;
  if (!open) {
    if (el.firstChild) el.replaceChildren();
    el.hidden = true;
    shape = '';
    return;
  }
  const dest = target();
  const kind = dest ? 'gal' : 'lib';
  const c = counts();
  const state = !filled() ? 'empty' : c.working ? 'working' : c.failed > 0 ? 'failed' : 'finished';
  const shown = state === 'finished' || state === 'failed' ? `${state}-${c.added === 1 ? '1' : 'n'}` : state;
  const key = hidden && state !== 'empty' ? `hidden-${c.working ? 'busy' : 'idle'}` : shown;
  const next = [kind, key, dest?.public ? 'public' : '', dest?.title ?? '', items.size > 0].join('|');

  let section = el.querySelector(':scope > .ws-tray');
  if (!section || next !== shape) {
    const active = document.activeElement;
    const focused = active instanceof HTMLButtonElement && el.contains(active) && active.name === 'tray' ? active.value : null;
    const fresh = build(kind, key, dest);
    if (!fresh) return;
    el.replaceChildren(fresh);
    section = fresh;
    shape = next;
    if (focused) {
      const want = section.querySelector(`button[name="tray"][value="${SWAPPED_FOCUS[focused] ?? focused}"]`)
        ?? section.querySelector('button[name="tray"]');
      want?.focus();
    }
  }
  el.hidden = false;

  // The heading line: the server's words for the shown state, with this tray's counts.
  const values = shown === 'working' ? [c.added, c.total]
    : shown.startsWith('finished') ? [c.added]
      : shown.startsWith('failed') ? [c.added, c.failed]
        : [];
  const words = template(`ws-tpl-tray-${kind}-${shown}`)?.content.querySelector('.ws-tray-head')?.textContent ?? '';
  const head = section.querySelector('.ws-tray-head');
  const text = numbered(words, values).replace(SENTINEL, (_, k) => (k === 'dest' ? dest?.title ?? '' : ''));
  if (head && head.textContent !== text) head.textContent = text;

  const bar = section.querySelector('.ws-tray-bar');
  if (bar) {
    bar.setAttribute('value', String(c.added));
    bar.setAttribute('max', String(c.total));
    bar.textContent = numbered(bar.textContent, [c.added, c.total]);
  }

  // Body: the refusals, then one row per item, in the set's order.
  const zone = section.querySelector('.dropzone');
  const rows = section.querySelector('.ws-tray-rows');
  const refused = refusalList();
  if (zone) {
    for (const old of zone.querySelectorAll('.dz-refusals')) if (old !== refused) old.remove();
    if (refused && refused.parentElement !== zone) zone.append(refused);
    for (const cls of [...zone.classList]) if (cls.startsWith('state-')) zone.classList.remove(cls);
    zone.classList.add(refused ? 'state-refused' : 'state-idle');
  }
  if (rows) {
    const want = [];
    if (refused) want.push(refused);
    for (const item of items.values()) {
      const row = rowFor(item);
      if (row) want.push(row);
    }
    const have = [...rows.children];
    if (have.length !== want.length || have.some((n, i) => n !== want[i])) rows.replaceChildren(...want);
  }
}

function setState(item, state, extra = {}) {
  Object.assign(item, { state }, extra);
  if (items.has(item.itemId)) render();
}

// ── pick time (FR-004) and register (U3) ────────────────────────────────────────────────────

async function isJpeg(file) {
  const head = new Uint8Array(await file.slice(0, 3).arrayBuffer());
  return head.length === 3 && head[0] === 0xff && head[1] === 0xd8 && head[2] === 0xff;
}

async function refusalFor(file) {
  if (!(await isJpeg(file))) return 'not a JPEG';
  if (file.size > maxBytes()) return overLimit(file.size);
  return null;
}

async function addFiles(files) {
  const found = [];
  const accepted = [];
  let remaining = maxPhotos() - [...items.values()].filter((i) => i.state !== 'added').length - addedIds.size;
  for (const file of files) {
    const reason = await refusalFor(file);
    if (reason) { found.push({ name: file.name, reason }); continue; }
    if (remaining <= 0) { found.push({ name: file.name, reason: `batch is full (${maxPhotos()} photos)` }); continue; }
    remaining--;
    accepted.push(file);
  }

  if (accepted.length > 0) {
    const dest = target();
    const body = { files: accepted.map((f) => ({ name: f.name, size: f.size, type: 'image/jpeg' })) };
    if (dest) body.gallery = dest.id;
    // [SAFETY II.5a] Only while the owner can see the sentence that visitors will see these photos.
    if (noticeShown()) body.visibleToVisitors = true;
    const r = await api('/admin/upload/items', body);
    if (r.status === 200 && r.json) {
      batchId = r.json.batchId;
      destination = r.json.destination ?? null;
      for (const a of r.json.accepted) {
        const file = accepted[a.index];
        const item = {
          itemId: a.itemId, attemptId: a.attemptId, fileName: file.name, fileSize: file.size,
          state: 'queued', percent: 0, reason: null, masterStored: false, file, run: 0, pickAgain: false, born: generation,
          el: null, elKey: '', joining: false,
        };
        items.set(item.itemId, item);
        enqueue(item, { masterUrl: a.master.url });
      }
      for (const x of r.json.refused) found.push({ name: x.name || accepted[x.index]?.name || '', reason: x.reason });
    } else {
      const reason = r.json?.message ?? `could not add (${r.status || 'network error'})`;
      for (const f of accepted) found.push({ name: f.name, reason });
      // The destination changed under the page (now public, or gone): re-read the list so the tray
      // names it as it is, notice included, before the owner picks again.
      if (r.json?.error === 'destination-public' || r.json?.error === 'gallery-gone') emit('upload:stale');
    }
  }
  showRefusals(found);
}

// ── the worker (one, sequential) ────────────────────────────────────────────────────────────

function encode(file, widths) {
  if (!worker) {
    worker = new Worker(host()?.dataset.workerSrc ?? '', { type: 'module' });
    worker.onmessage = (e) => { const p = pendingEncode; pendingEncode = null; p?.(e.data); };
    worker.onerror = (e) => {
      e.preventDefault?.();
      const p = pendingEncode;
      pendingEncode = null;
      worker.terminate();
      worker = null;
      p?.({ ok: false, reason: 'Could not prepare sizes: the worker stopped' });
    };
  }
  return new Promise((resolve) => {
    pendingEncode = resolve;
    worker.postMessage({ file, widths });
  });
}

// ── per item ────────────────────────────────────────────────────────────────────────────────

function enqueue(item, plan) {
  item.run += 1;
  queue.push({ item, run: item.run, plan });
  pump();
}

async function pump() {
  if (busy) return;
  busy = true;
  try {
    while (queue.length > 0) {
      const job = queue.shift();
      if (job.item.run !== job.run || !items.has(job.item.itemId)) continue;
      await process(job);
    }
  } finally {
    busy = false;
  }
}

const progress = (item) => api(itemUrl(item, 'progress'), { attemptId: item.attemptId, state: item.state, percent: item.percent });

async function fail(item, reason) {
  const text = String(reason).slice(0, 200);
  await api(itemUrl(item, 'fail'), { attemptId: item.attemptId, reason: text });
  setState(item, 'failed', { reason: text });
}

// A refused server step. 422 carries the reason: the server has already failed the item, except
// "original not found" (U4 leaves the item in its state), which the page reports through U6.
// 404/409 mean the page's view is out of date, so reconcile from U2.
async function refused(item, r) {
  if (r.status === 422) {
    const reason = r.json?.message ?? 'rejected';
    if (reason === 'original not found') return fail(item, reason);
    return setState(item, 'failed', { reason });
  }
  if (r.status === 404 || r.status === 409) return reconcile();
  return fail(item, r.json?.message ?? `request failed (${r.status || 'network error'})`);
}

async function process({ item, run, plan }) {
  const live = () => item.run === run && items.has(item.itemId);
  current = item;
  const beat = setInterval(() => { if (live() && item.state !== 'queued') progress(item); }, HEARTBEAT_MS);
  try {
    setState(item, 'processing', { percent: 0, reason: null, pickAgain: false });
    await progress(item);
    if (!live()) return;

    let sizes = plan.sizes;
    if (!sizes) {
      const m = await put(plan.masterUrl, item.file, 'image/jpeg');
      if (!live()) return;
      if (!m.ok) return fail(item, putFailure(m.status));
      const r = await api(itemUrl(item, 'master'), { attemptId: item.attemptId });
      if (!live()) return;
      if (r.status !== 200) return refused(item, r);
      sizes = r.json.sizes;
      item.masterStored = true;
    }

    const result = await encode(item.file, sizes.map((s) => s.w));
    if (!live()) return;
    if (!result.ok) return fail(item, result.reason);

    setState(item, 'uploading', { percent: 0 });
    await progress(item);
    if (!live()) return;

    const puts = [];
    for (const s of sizes) {
      const enc = result.sizes.find((x) => x.w === s.w);
      puts.push(() => put(s.avif.url, enc.avif, s.avif.contentType));
      puts.push(() => put(s.webp.url, enc.webp, s.webp.contentType));
    }
    let done = 0;
    let failure = null;
    let next = 0;
    const lane = async () => {
      while (next < puts.length && !failure && live()) {
        const r = await puts[next++]();
        if (!r.ok) { failure ??= r; return; }
        done += 1;
        if (live()) setState(item, 'uploading', { percent: Math.round((done / puts.length) * 100) });
      }
    };
    await Promise.all(Array.from({ length: Math.min(MAX_PUTS, puts.length) }, lane));
    if (!live()) return;
    if (failure) return fail(item, putFailure(failure.status));
    await progress(item);
    if (!live()) return;

    const c = await api(itemUrl(item, 'complete'),
      { attemptId: item.attemptId, placeholder: result.placeholder, timings: result.timings });
    if (!live()) return;
    if (c.status === 200) {
      setState(item, 'succeeded', { percent: 100, reason: null });
      join(item); // not awaited: the next photo's work starts while this one joins
    } else {
      await refused(item, c);
    }
  } finally {
    clearInterval(beat);
    if (current === item) current = null;
  }
}

// ── join (U12) ──────────────────────────────────────────────────────────────────────────────

async function join(item) {
  if (item.joining) return;
  item.joining = true;
  const attempt = item.attemptId;
  const r = await api(itemUrl(item, 'join'), { attemptId: attempt });
  item.joining = false;
  if (r.status === 200 && r.json?.imageId) {
    addedIds.add(r.json.imageId);
    if (r.json.galleryGone) destination = null;
    setState(item, 'added', { percent: 100, reason: null });
    emit('upload:joined', { imageId: r.json.imageId, gallery: r.json.gallery ?? null });
    return;
  }
  // 404 (already joined: its id is in U2 `added`), 409 (refused copy, stale attempt, not active):
  // the server's view wins.
  await reconcile();
}

// ── restart (U8) / remove (U9) / Done and Close (U13) ───────────────────────────────────────

async function restart(item) {
  const r = await api(itemUrl(item, 'restart'), { attemptId: item.attemptId });
  if (r.status !== 200) return reconcile();
  item.attemptId = r.json.attemptId;
  item.run += 1;
  item.masterStored = !r.json.needsFile;
  item.plan = r.json.needsFile ? { masterUrl: r.json.master.url } : { sizes: r.json.sizes };
  if (item.file) {
    setState(item, 'queued', { percent: 0, reason: null, pickAgain: false });
    enqueue(item, item.plan);
  } else {
    setState(item, 'queued', { percent: 0, reason: null, pickAgain: true });
  }
}

async function pickedAgain(item, file) {
  if (file.name !== item.fileName) return addFiles([file]);
  const reason = await refusalFor(file);
  if (reason) return showRefusals([{ name: file.name, reason }]);
  item.file = file;
  item.fileSize = file.size;
  item.pickAgain = false;
  enqueue(item, item.plan);
}

async function remove(item) {
  const r = await api(itemUrl(item, 'delete'), { attemptId: item.attemptId });
  if (r.status !== 204 && r.status !== 404) return reconcile();
  item.run += 1;
  items.delete(item.itemId);
  item.el?.remove();
  render();
}

/** Done / Close: U13 drops what failed or was interrupted, then the tray and the New marks go. */
async function finish() {
  if (counts().working) return;
  if (batchId) {
    const r = await api('/admin/upload/clear', { batchId });
    if (r.status !== 200 && r.status !== 404) {
      if (r.json?.message) emit('upload:said', { text: r.json.message, failed: true });
      await reconcile();
      return;
    }
    if (r.status === 200 && r.json?.message) emit('upload:said', { text: r.json.message, failed: false });
  }
  for (const item of items.values()) item.run += 1;
  items.clear();
  addedIds.clear();
  queue.length = 0;
  batchId = '';
  destination = undefined;
  refusals = [];
  refusalsEl = null;
  hidden = false;
  open = false;
  render();
  emit('upload:closed');
}

// ── U2 reconcile (on load, after bfcache and after any 404/409) ─────────────────────────────

async function reconcile() {
  const asOf = ++generation;
  const r = await api(STATE_URL);
  if (r.status !== 200 || !r.json) return;
  apply(r.json, asOf);
}

function apply(state, asOf) {
  const batch = state?.batch ?? null;
  const serverItems = batch?.items ?? [];
  if (batch) {
    batchId = batch.id;
    destination = batch.destination ?? null;
    for (const id of batch.added ?? []) addedIds.add(id);
  } else {
    // No open set on the server (cleared elsewhere): nothing has been added to it.
    addedIds.clear();
    destination = undefined;
  }
  const ordered = [];
  const joinable = [];
  for (const s of serverItems) {
    const local = items.get(s.itemId);
    // The item this page is working on keeps its live view; the server lags it by one beat.
    if (local && local === current && local.attemptId === s.attemptId && ACTIVE.has(s.state)) {
      ordered.push(local);
      continue;
    }
    const merged = Object.assign(local ?? { file: null, run: 0, pickAgain: false, born: asOf, el: null, elKey: '', joining: false }, {
      itemId: s.itemId, attemptId: s.attemptId, fileName: s.fileName, fileSize: s.fileSize,
      state: s.state, percent: s.percent, reason: s.reason, masterStored: s.masterStored,
    });
    if (local && local.attemptId !== s.attemptId) merged.run += 1;
    if (!ACTIVE.has(merged.state)) merged.pickAgain = false;
    if (JOINABLE.has(merged.state)) joinable.push(merged);
    ordered.push(merged);
  }
  // Keep the server's order. A joined item stays as Added; an item registered after this U2 was
  // sent is kept; any other item the server no longer lists is gone (removed or cleared elsewhere).
  const listed = new Set(serverItems.map((s) => s.itemId));
  for (const item of items.values()) {
    if (listed.has(item.itemId)) continue;
    if (addedIds.has(item.itemId)) {
      item.state = 'added';
      ordered.push(item);
    } else if (item.born >= asOf) {
      ordered.push(item);
    } else {
      item.run += 1;
    }
  }
  items.clear();
  for (const item of ordered) items.set(item.itemId, item);
  if (filled()) open = true;
  render();
  for (const item of joinable) join(item);

  // An item the server still calls active that no page is working on (its file is gone with a
  // reload) turns interrupted when its heartbeat ages out; look again until it has.
  clearTimeout(orphanTimer);
  if ([...items.values()].some((i) => ACTIVE.has(i.state) && i !== current && !queue.some((j) => j.item === i))) {
    orphanTimer = setTimeout(reconcile, ORPHAN_RECHECK_MS);
  }
}

// ── <upload-tray> ───────────────────────────────────────────────────────────────────────────

const wired = new WeakSet();

function onClick(e) {
  const button = e.target instanceof Element ? e.target.closest('button') : null;
  if (!button || button.disabled) return;
  const row = button.closest('.ws-up[data-item]');
  if (row && button.name === 'action') {
    e.preventDefault();
    const item = items.get(row.dataset.item);
    if (!item) return;
    if (button.value === 'retry') restart(item);
    else if (button.value === 'remove') remove(item);
    return;
  }
  if (button.name !== 'tray') return;
  e.preventDefault();
  if (button.value === 'hide') { hidden = true; render(); }
  else if (button.value === 'show') { hidden = false; render(); }
  else if (button.value === 'close' || button.value === 'done') finish();
}

function onChange(e) {
  const input = e.target;
  if (!(input instanceof HTMLInputElement) || !input.matches('input.dz-input')) return;
  const files = Array.from(input.files ?? []);
  input.value = '';
  if (files.length) addFiles(files);
}

class UploadTrayElement extends HTMLElement {
  connectedCallback() {
    if (wired.has(this)) return;
    wired.add(this);
    this.addEventListener('click', onClick);
    this.addEventListener('change', onChange);
  }
}

if (!customElements.get('upload-tray')) customElements.define('upload-tray', UploadTrayElement);

window.addEventListener('pageshow', (e) => { if (e.persisted && open) reconcile(); });

// ── the hooks workspace.js calls ────────────────────────────────────────────────────────────

/** Add photos, or files dropped on the window: open (or show) the tray, then take the files. */
export async function show(files = []) {
  if (busyElsewhere()) { open = true; hidden = false; render(); return; } // a drop here: show where it is adding
  // Owner walk-through: Add photos (or a drop) after a set has finished starts a new set for the open
  // collection; the finished one closes first, exactly as Done closes it. There is no second control for this.
  if (filled() && !counts().working) {
    await finish();
    if (filled()) return; // U13 refused; its message is in the Toast and the set stays.
  }
  open = true;
  hidden = false;
  render();
  if (files.length > 0) {
    addFiles(files);
  } else if (!filled()) {
    tray()?.querySelector('input.dz-input')?.focus();
  } else if ((destination?.id ?? null) === (openGallery()?.id ?? null)) {
    // Still working, into the open collection: Add photos picks more files for the same set.
    tray()?.querySelector('input.dz-input')?.click();
  }
}

/** The page loaded with an open batch (U2 answered by workspace.js): reopen the tray with it. */
export function resume(state) {
  apply(state, ++generation);
  return [...addedIds];
}

/** The open collection or the list changed: the tray may name a different destination. */
export function collectionChanged() {
  render();
}
