// 020 workspace page script (contracts/components.md C12). One module; registers <admin-workspace>.
//
//   Part 1 (T036): navigation by fragment swap (X-Workspace: main) with pushState/popstate, the
//   current mark in the collections list, the chrome refresh (X-Workspace: chrome) after writes
//   that change a gallery's existence, title, order, visibility or count, "Show more" and the
//   sentinel (X-Workspace: more), the write helper (X-CSRF-TOKEN from data-csrf, same-origin
//   credentials) and the Toast, which shows the server's `message` and nothing else.
//   Part 2 (T037): selection (click, Ctrl/Cmd-click, the select mark, Shift range, Ctrl/Cmd+A up
//   to data-cap, Escape, a click on empty space), roving tabindex with the arrow keys and Space,
//   the details panel cloned from the context's <template data-panel> and filled from the cell's
//   data-* with no request (R-04), and save on leave (A1, A5).
//   Part 3 (T038) adds its rows to the `clicks` / `changes` tables near the end.
//
// Markup rules (008 R-11, 020 C12): the only string-to-markup step is the function `swap`, which
// parses a fragment the server answered. Everything else is cloned from a server-rendered
// <template> or from the page itself and filled through textContent, value, checked or
// setAttribute. Elements are resolved when an event happens, never cached across a swap.

const host = () => document.querySelector('admin-workspace');
const main = () => host()?.querySelector('.ws-main');
const list = () => host()?.querySelector('.ws-list');
const panel = () => host()?.querySelector('.ws-panel');
const grid = () => main()?.querySelector('.cs');
const cells = () => [...(main()?.querySelectorAll('.ws-cell') ?? [])];
const cellById = (id) => cells().find((c) => c.dataset.id === id) ?? null;
const selected = () => cells().filter((c) => c.getAttribute('aria-selected') === 'true');
const cap = () => {
  const n = Number(host()?.dataset.cap);
  return Number.isFinite(n) && n > 0 ? n : Infinity;
};

/** The open collection's gallery id, or null for All photos and Not in a gallery. */
function galleryOf(path = location.pathname) {
  const m = /^\/admin\/g\/([^/]+)$/.exec(path);
  return m ? m[1] : null;
}

// ── swap: the one place a server fragment becomes nodes ─────────────────────────────────────

/** Parses a fragment the server answered and replaces the region's children with it. */
function swap(region, html) {
  const doc = new DOMParser().parseFromString(html, 'text/html');
  region.replaceChildren(...doc.body.children);
}

// ── toast: every result line is the server's message ───────────────────────────────────────

let toastTimer = 0;

function toast(text, failed = false) {
  const el = panel()?.querySelector('.ws-toast');
  if (!el) return;
  el.textContent = text ?? '';
  el.classList.toggle('ws-refusal', failed);
  clearTimeout(toastTimer);
  toastTimer = setTimeout(() => {
    el.textContent = '';
    el.classList.remove('ws-refusal');
  }, 6000);
}

/** No JSON message came back (network down, a fragment refused): the response's own status line. */
function statusLine(res, error) {
  if (res) return `${res.status} ${res.statusText}`.trim();
  return error instanceof Error ? error.message : String(error);
}

// ── requests ────────────────────────────────────────────────────────────────────────────────

/** GETs a fragment of `path`; resolves to its text, or null after a refusal toast. */
async function fragment(path, part) {
  let res;
  try {
    res = await fetch(path, { headers: { 'X-Workspace': part }, credentials: 'same-origin' });
  } catch (e) {
    toast(statusLine(null, e), true);
    return null;
  }
  if (!res.ok || res.redirected) {
    toast(statusLine(res), true);
    return null;
  }
  return res.text();
}

/**
 * POSTs JSON to an A-route with the CSRF header. Resolves to `{ ok, status, data, message }`;
 * `message` is the server's sentence (the status line when no JSON came back). The caller shows it.
 */
async function write(url, body) {
  let res;
  try {
    res = await fetch(url, {
      method: 'POST',
      credentials: 'same-origin',
      headers: { 'Content-Type': 'application/json', 'X-CSRF-TOKEN': host()?.dataset.csrf ?? '' },
      body: JSON.stringify(body ?? {}),
    });
  } catch (e) {
    return { ok: false, status: 0, data: null, message: statusLine(null, e) };
  }
  let data = null;
  try {
    data = await res.json();
  } catch {
    data = null;
  }
  const message = typeof data?.message === 'string' ? data.message : statusLine(res);
  return { ok: res.ok, status: res.status, data, message };
}

// ── navigation ──────────────────────────────────────────────────────────────────────────────

let navSeq = 0;

/** The list's current mark follows the address. */
function markCurrent(path) {
  for (const a of list()?.querySelectorAll('a.ws-coll-row') ?? []) {
    if (new URL(a.href, location.href).pathname === path) a.setAttribute('aria-current', 'page');
    else a.removeAttribute('aria-current');
  }
}

/** Opens the collection at `href` by swapping in its main fragment; on failure the page stays. */
async function navigate(href, push) {
  const url = new URL(href, location.href);
  const seq = ++navSeq;
  const html = await fragment(url.pathname + url.search, 'main');
  if (html === null || seq !== navSeq) return false;
  const region = main();
  if (!region) return false;
  const tray = region.querySelector('upload-tray');
  swap(region, html);
  if (tray) region.querySelector('upload-tray')?.replaceWith(tray);
  region.scrollTop = 0;
  if (push) history.pushState({ ws: true }, '', url.pathname + url.search);
  markCurrent(url.pathname);
  const heading = region.querySelector('.ws-head h1');
  if (heading) document.title = heading.textContent.trim();
  anchorId = null;
  afterSelect();
  rove(null);
  watchSentinel();
  return true;
}

/** True for a plain primary click on a workspace link (W1 to W3) that opens in this tab. */
function isCollectionLink(a, e) {
  if (e.defaultPrevented || e.button !== 0 || e.metaKey || e.ctrlKey || e.shiftKey || e.altKey) return false;
  if (a.target && a.target !== '_self') return false;
  const url = new URL(a.href, location.href);
  return url.origin === location.origin && /^\/admin(\/unplaced|\/g\/[^/]+)?$/.test(url.pathname);
}

// ── chrome refresh ──────────────────────────────────────────────────────────────────────────

let chromeBusy = null;
let chromeAgain = false;

/**
 * Re-reads the collections list and the collection-independent templates (X-Workspace: chrome)
 * after a write that changed a gallery's existence, title, order, visibility or count. Calls made
 * while one is in flight collapse into one more read. An open photo or selection panel is then
 * re-filled from its cells.
 */
function refreshChrome() {
  if (chromeBusy) {
    chromeAgain = true;
    return chromeBusy;
  }
  chromeBusy = (async () => {
    do {
      chromeAgain = false;
      const html = await fragment(location.pathname, 'chrome');
      const region = list();
      if (html !== null && region) {
        swap(region, html);
        markCurrent(location.pathname);
        if (panelKind === 'photo' || panelKind === 'selection') showPanel();
      }
    } while (chromeAgain);
    chromeBusy = null;
  })();
  return chromeBusy;
}

// ── more: Show more and the sentinel ────────────────────────────────────────────────────────

let moreBusy = false;
let observer = null;

async function loadMore() {
  const region = main();
  const sentinel = region?.querySelector('span[data-next]');
  if (moreBusy || !sentinel) return;
  moreBusy = true;
  try {
    const cursor = sentinel.getAttribute('data-next') ?? '';
    const html = await fragment(`${location.pathname}?before=${encodeURIComponent(cursor)}`, 'more');
    if (html === null || main() !== region || !region.contains(sentinel)) return;
    const box = document.createElement('div');
    swap(box, html);
    const next = box.querySelector('span[data-next]');
    next?.remove();
    grid()?.append(...box.children);
    if (next) {
      sentinel.setAttribute('data-next', next.getAttribute('data-next') ?? '');
    } else {
      region.querySelector('.ws-more')?.remove();
      sentinel.remove();
    }
    rove(null);
  } finally {
    moreBusy = false;
  }
  watchSentinel();
}

/** Observes the sentinel; observing again after each chunk re-checks one that is still in view. */
function watchSentinel() {
  if (!('IntersectionObserver' in window)) return;
  observer ??= new IntersectionObserver((entries) => {
    if (entries.some((en) => en.isIntersecting)) loadMore();
  });
  observer.disconnect();
  const sentinel = main()?.querySelector('span[data-next]');
  if (sentinel) observer.observe(sentinel);
}

// ── selection (FR-007, FR-008) ──────────────────────────────────────────────────────────────

let anchorId = null;

function mark(cell, on) {
  cell.setAttribute('aria-selected', on ? 'true' : 'false');
  cell.classList.toggle('is-selected', on);
  const box = cell.querySelector('label.select-mark input[type="checkbox"]');
  if (box) box.checked = on;
}

/** The cap sentence, from the server-rendered template. */
function sayCap() {
  const tpl = list()?.querySelector('template.ws-tpl-cap');
  toast(tpl?.content.textContent.trim() ?? '', true);
}

/** Sets the selection to exactly `want` (cells in page order); past the cap it stops and says so. */
function select(want) {
  const limit = cap();
  const keep = new Set(want.slice(0, limit));
  for (const c of cells()) mark(c, keep.has(c));
  if (want.length > limit) sayCap();
  afterSelect();
}

function afterSelect() {
  const n = selected().length;
  const clear = main()?.querySelector('.ws-clear');
  if (clear) clear.hidden = n === 0;
  host()?.classList.toggle('ws-sheet', n > 0);
  showPanel();
}

function clearSelection() {
  anchorId = null;
  select([]);
}

/** Ctrl/Cmd-click, the select mark and Space: add or remove one cell; adding past the cap is refused. */
function toggle(cell) {
  const on = cell.getAttribute('aria-selected') !== 'true';
  if (on && selected().length >= cap()) {
    sayCap();
    return;
  }
  anchorId = cell.dataset.id;
  select(cells().filter((c) => (c === cell ? on : c.getAttribute('aria-selected') === 'true')));
}

/** Shift-click: exactly the range from the last selected cell (the anchor) to this one. */
function range(cell) {
  const all = cells();
  const from = all.findIndex((c) => c.dataset.id === anchorId);
  if (from < 0) {
    anchorId = cell.dataset.id;
    select([cell]);
    return;
  }
  const to = all.indexOf(cell);
  const step = to >= from ? 1 : -1;
  const want = [];
  for (let i = from; i !== to + step; i += step) want.push(all[i]);
  select(want);
}

function onCellClick(cell, e) {
  rove(cell);
  if (e.target.closest('label.select-mark')) {
    // The label would also toggle its checkbox; the selection state drives the box instead.
    e.preventDefault();
    toggle(cell);
  } else if (e.shiftKey) {
    range(cell);
  } else if (e.ctrlKey || e.metaKey) {
    toggle(cell);
  } else {
    anchorId = cell.dataset.id;
    select([cell]);
  }
}

// ── roving tabindex and the keys ────────────────────────────────────────────────────────────

/** Exactly one cell is in the tab order: `to`, else the current one, else the anchor, else the first. */
function rove(to) {
  const all = cells();
  const current = to ?? all.find((c) => c.tabIndex === 0) ?? cellById(anchorId) ?? all[0] ?? null;
  for (const c of all) c.tabIndex = c === current ? 0 : -1;
}

/** Cells per row, read from the laid-out grid. */
function columns(all) {
  if (all.length === 0) return 1;
  const top = all[0].offsetTop;
  const n = all.findIndex((c) => c.offsetTop !== top);
  return n < 0 ? all.length : n;
}

const typing = (el) => el instanceof Element && el.closest('input, textarea, select, [contenteditable="true"]') !== null;

function onKey(e) {
  if (document.querySelector('dialog[open]')) return;
  if ((e.ctrlKey || e.metaKey) && !e.altKey && (e.key === 'a' || e.key === 'A')) {
    if (typing(e.target)) return;
    e.preventDefault();
    const all = cells();
    anchorId = all[0]?.dataset.id ?? null;
    select(all);
    return;
  }
  if (e.key === 'Escape') {
    if (!typing(e.target) && selected().length > 0) clearSelection();
    return;
  }
  const cell = e.target instanceof Element ? e.target.closest('.ws-cell') : null;
  if (!cell || e.target !== cell) return;
  const all = cells();
  const i = all.indexOf(cell);
  const step = { ArrowLeft: -1, ArrowRight: 1, ArrowUp: -columns(all), ArrowDown: columns(all) }[e.key];
  if (step !== undefined) {
    e.preventDefault();
    const to = all[Math.min(all.length - 1, Math.max(0, i + step))];
    rove(to);
    to.focus();
  } else if (e.key === ' ') {
    e.preventDefault();
    toggle(cell);
  }
}

// ── the details panel (R-04: filled from the cell's data-*, no request) ─────────────────────

let panelKind = null;
let panelFor = null;

function template(kind) {
  if (kind === 'default') return main()?.querySelector('template[data-panel="default"]') ?? null;
  return list()?.querySelector(`template[data-panel="${kind}"]`) ?? null;
}

/** Clones the context's template into the panel region (before the Toast) and fills it. */
function showPanel() {
  const region = panel();
  if (!region) return;
  const sel = selected();
  const kind = sel.length === 0 ? 'default' : sel.length === 1 ? 'photo' : 'selection';
  region.querySelector(':scope > .side-panel')?.remove();
  panelKind = kind;
  panelFor = kind === 'photo' ? sel[0].dataset.id : null;
  const tpl = template(kind);
  if (!tpl) return;
  const frag = tpl.content.cloneNode(true);
  const body = frag.querySelector('.side-panel');
  if (!body) return;
  if (kind === 'photo') fillPhoto(body, sel[0]);
  else if (kind === 'selection') fillSelection(body, sel);
  region.prepend(body);
}

function setField(root, name, value) {
  const el = root.querySelector(`[name="${name}"]`);
  if (el) el.value = value ?? '';
}

/** A copy of a cell's <picture> for the panel's preview frame. */
const previewOf = (cell) => cell.querySelector('picture')?.cloneNode(true) ?? null;

const galleriesOf = (cell) => (cell.dataset.galleries ?? '').split(' ').filter(Boolean);

/** Ticks a gallery row when every member list holds it; partly ticked when some do. */
function fillChecklist(root, members) {
  for (const box of root.querySelectorAll('input[data-member]')) {
    const gid = box.getAttribute('data-member');
    const n = members.filter((m) => m.includes(gid)).length;
    box.checked = n > 0 && n === members.length;
    box.indeterminate = n > 0 && n < members.length;
    if (box.indeterminate) box.setAttribute('aria-checked', 'mixed');
    else box.removeAttribute('aria-checked');
  }
}

function fillPhoto(root, cell) {
  const preview = root.querySelector('.ws-preview');
  const pic = previewOf(cell);
  if (preview && pic) preview.append(pic);
  setField(root, 'title', cell.dataset.title);
  setField(root, 'caption', cell.dataset.caption);
  fillChecklist(root, [galleriesOf(cell)]);
  const inGallery = root.querySelector('.ws-in-gallery');
  if (inGallery) inGallery.hidden = galleryOf() === null;

  // "View on site": the first public gallery in the list holding the photo (LibraryReader.Tile's rule).
  const mine = new Set(galleriesOf(cell));
  const row = [...(list()?.querySelectorAll('a[data-gid]') ?? [])]
    .find((a) => a.dataset.public === 'true' && mine.has(a.dataset.gid));
  const link = root.querySelector('.ws-view a');
  const unseen = root.querySelector('.ws-view .ws-unseen');
  if (link) {
    link.hidden = !row;
    if (row) link.setAttribute('href', `/g/${row.dataset.gid}/${row.dataset.slug}/${cell.dataset.id}`);
  }
  if (unseen) unseen.hidden = !!row;

  const consequence = root.querySelector('.danger-consequence');
  if (consequence) consequence.textContent = cell.dataset.consequence ?? '';
}

/** Replaces the count in a server sentence ("0 photos selected") with n. */
function count(el, n) {
  if (el) el.textContent = el.textContent.replace(/\d+/, String(n));
}

function fillSelection(root, sel) {
  const heading = root.querySelector(':scope > h2');
  count(heading, sel.length);
  if (heading) root.setAttribute('aria-label', heading.textContent);
  const previews = root.querySelector('.ws-previews');
  for (const cell of sel.slice(0, 4)) {
    const pic = previewOf(cell);
    if (previews && pic) previews.append(pic);
  }
  fillChecklist(root, sel.map(galleriesOf));
  count(root.querySelector('.danger-trigger'), sel.length);
  count(root.querySelector('.danger-title'), sel.length);
}

// ── save on leave (FR-013): A1 for a photo, A5 for the open gallery ─────────────────────────

/** The field's foot line: saving, the server's word, or the refusal. */
function fieldState(field, kind, text) {
  const msg = field.closest('.field')?.querySelector('.field-foot .msg');
  if (msg) {
    msg.textContent = text;
    msg.className = `msg msg-${kind}`;
  }
  if (kind === 'invalid') field.setAttribute('aria-invalid', 'true');
  else field.removeAttribute('aria-invalid');
}

async function savePhotoField(field) {
  const id = panelFor;
  if (!id) return;
  const name = field.name;
  fieldState(field, 'saving', 'Saving\u2026');
  const r = await write(`/admin/api/photos/${encodeURIComponent(id)}`, { [name]: field.value });
  if (!r.ok) {
    // Refused: the typed text stays in the field and the cell keeps the stored value.
    fieldState(field, 'invalid', r.message);
    toast(r.message, true);
    return;
  }
  fieldState(field, 'saved', r.message);
  toast(r.message);
  const cell = cellById(id);
  if (!cell) return;
  if (name === 'title') {
    const old = cell.dataset.title ?? '';
    const now = r.data.title ?? '';
    cell.setAttribute('data-title', now);
    const shown = cell.querySelector('.title');
    if (shown) {
      shown.textContent = now;
      shown.setAttribute('title', now);
    }
    const label = cell.querySelector('label.select-mark .vh');
    if (label && old) label.textContent = label.textContent.replace(old, now);
  } else {
    cell.setAttribute('data-caption', r.data.caption ?? '');
  }
}

async function saveGalleryField(field) {
  const gid = galleryOf();
  if (!gid) return;
  const name = field.name;
  fieldState(field, 'saving', 'Saving\u2026');
  const r = await write(`/admin/api/galleries/${encodeURIComponent(gid)}`, { [name]: field.value });
  if (!r.ok) {
    fieldState(field, 'invalid', r.message);
    toast(r.message, true);
    return;
  }
  fieldState(field, 'saved', r.message);
  toast(r.message);
  if (galleryOf() !== gid) return;
  // The default template keeps the stored values, so the panel opens with them next time.
  const stored = template('default')?.content.querySelector(`[name="${name}"]`);
  if (stored instanceof HTMLTextAreaElement) stored.textContent = r.data[name] ?? '';
  else stored?.setAttribute('value', r.data[name] ?? '');
  if (name === 'title') {
    const heading = main()?.querySelector('.ws-head h1');
    if (heading) heading.textContent = r.data.title ?? '';
    document.title = r.data.title ?? '';
    refreshChrome();
  }
}

// ── event tables (part 3, T038, appends its own rows) ───────────────────────────────────────

/** [selector, handler(el, event) → handled]: the first row whose selector matches and handles wins. */
const clicks = [
  ['.ws-list a', (a, e) => {
    if (!isCollectionLink(a, e)) return false;
    e.preventDefault();
    navigate(a.href, true);
    return true;
  }],
  ['.ws-main .ws-more', () => {
    loadMore();
    return true;
  }],
  ['.ws-main .ws-clear', () => {
    clearSelection();
    return true;
  }],
  ['.ws-main .ws-cell', (cell, e) => {
    onCellClick(cell, e);
    return true;
  }],
];

/** [selector, handler(el, event) → handled] for `change` (fired when the owner leaves a field). */
const changes = [
  ['.ws-panel .side-panel input[name="title"], .ws-panel .side-panel textarea[name="caption"]', (field) => {
    if (panelKind !== 'photo') return false;
    savePhotoField(field);
    return true;
  }],
  ['.ws-panel .side-panel input[name="title"], .ws-panel .side-panel textarea[name="description"]', (field) => {
    if (panelKind !== 'default' || galleryOf() === null) return false;
    saveGalleryField(field);
    return true;
  }],
];

function dispatch(table, e) {
  if (!(e.target instanceof Element)) return false;
  for (const [sel, fn] of table) {
    const el = e.target.closest(sel);
    if (el && fn(el, e)) return true;
  }
  return false;
}

/** A click in the main region that hits no cell or control clears the selection. */
function onEmptyClick(e) {
  if (!(e.target instanceof Element) || !main()?.contains(e.target)) return;
  if (e.target.closest('.ws-cell, a, button, input, textarea, select, label, summary, dialog')) return;
  if (selected().length > 0) clearSelection();
}

// ── <admin-workspace> ───────────────────────────────────────────────────────────────────────

class AdminWorkspace extends HTMLElement {
  connectedCallback() {
    if (this.dataset.script === 'ready') return;
    this.addEventListener('click', (e) => {
      if (!dispatch(clicks, e)) onEmptyClick(e);
    });
    this.addEventListener('change', (e) => dispatch(changes, e));
    document.addEventListener('keydown', onKey);
    window.addEventListener('popstate', () => navigate(location.href, false));
    afterSelect();
    rove(null);
    watchSentinel();
    this.dataset.script = 'ready';
  }
}

customElements.define('admin-workspace', AdminWorkspace);
