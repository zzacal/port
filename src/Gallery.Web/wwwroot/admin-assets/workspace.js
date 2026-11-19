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
//   Part 3 (T038): the In galleries checklist (tick A7, untick A8, partly ticked, narrowing by the
//   mirror of GalleryChecklist.Match, Enter or the offer → A3 with the ids or A7), drag (cells onto
//   a gallery name A7, cells within a gallery A9, gallery names A4), Move earlier/later (A9) and
//   Move up/down (A4), cover (A10), the Public switch (A6), DangerConfirm then A2/A11, and naming a
//   new gallery in the list (A3, then open it). Its rows are in the `clicks` / `changes` tables.
//   Tray hooks (T046, C13): upload.js is loaded by dynamic import() only, on Add photos, a file drop
//   anywhere on the window, or when U2 reports an open batch as the page loads (`ready` waits for that
//   check). Its <upload-tray> is moved across `swap`, never re-created. upload.js tells this script
//   what happened through CustomEvents on the document: `upload:joined` (fetch the cell by W4, place
//   it, mark it New, refresh the chrome at most once a second), `upload:closed` (the New marks go),
//   `upload:said` (the server's message for the Toast) and `upload:stale` (re-read the chrome).
//   Large view (T055, R-08, FR-036): double-click, Enter on the focused cell or Open large shows the
//   photo in the page's hidden PhotoStage (a copy of the cell's Picture, `sizes` set to the shown
//   width), "{n} of {total}", Previous/Next and the arrow keys (loading the next chunk when needed),
//   ?photo={id} pushed on open and replaced on each step; Escape, the browser's back or the stage's
//   "← {collection}" button returns to the grid with that photo selected and in view; a load with
//   ?photo= loads chunks until it is found.
//   Sheet (T057, K1/K12): below 1200 px the panel is a bottom sheet shown while admin-workspace has
//   `ws-sheet`: set while a selection exists or Gallery settings is on, removed by Done.
//   Owner walk-through (T074, T075): below 1200 px the collections list is a menu under one button
//   (admin-workspace has `ws-menu-open` while it is open; a collection, Escape, the button again or a
//   tap outside closes it). A select-mark tap toggles the photo without opening the sheet; while a
//   selection exists the selection bar shows its count, and its Add to gallery opens the sheet.
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

/** The collection whose main region is on the page (popstate within it only opens or closes the large view). */
let shownPath = location.pathname;

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
  // Gallery settings stays open over a re-read of the same gallery (after the Public switch); another collection closes it.
  if (url.pathname !== shownPath) settingsOn = false;
  shownPath = url.pathname;
  largeId = null;
  largePushed = false;
  // A re-read of the shown collection (after a write) ends the large view: the address drops ?photo=.
  if (!push && url.pathname === location.pathname && new URL(location.href).searchParams.has('photo')) {
    history.replaceState({ ws: true }, '', url.pathname);
  }
  markCurrent(url.pathname);
  setMenu(false);
  const heading = region.querySelector('.ws-head h1');
  if (heading) document.title = heading.textContent.trim();
  anchorId = null;
  markNew();
  afterSelect();
  rove(null);
  watchSentinel();
  trayCollectionChanged();
  return true;
}

// ── the collections menu below 1200 px (T074) ──────────────────────────────────────────────

const menuOpen = () => !!host()?.classList.contains('ws-menu-open');

/** Opens or closes the menu; the button's aria-expanded follows. */
function setMenu(open) {
  host()?.classList.toggle('ws-menu-open', open);
  syncMenu();
}

/**
 * The menu button after a swap or a navigation: aria-expanded from the open state, and its text the
 * current row's name (the chrome fragment renders it; a main swap leaves the old one).
 */
function syncMenu() {
  const button = list()?.querySelector('.ws-menu-toggle');
  if (!button) return;
  button.setAttribute('aria-expanded', menuOpen() ? 'true' : 'false');
  const name = list()?.querySelector('a.ws-coll-row[aria-current="page"] .ws-coll-name')?.textContent;
  const label = button.querySelector('.ws-menu-name');
  if (label && name) label.textContent = name;
}

/** A tap outside the open menu (and its button) only closes it. */
function onOutsideClick(e) {
  if (!menuOpen() || !(e.target instanceof Element) || list()?.contains(e.target)) return;
  e.preventDefault();
  e.stopPropagation();
  setMenu(false);
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
        syncMenu();
        if (panelKind === 'photo' || panelKind === 'selection') showPanel();
        trayCollectionChanged();
      }
    } while (chromeAgain);
    chromeBusy = null;
  })();
  return chromeBusy;
}

// ── more: Show more and the sentinel ────────────────────────────────────────────────────────

let moreBusy = null;
let observer = null;

/** True while the open collection has more cells than the page holds (a sentinel is present). */
const hasMore = () => !!main()?.querySelector('span[data-next]');

/**
 * Fetches and appends the next chunk. Resolves true when cells were appended, false otherwise; a call
 * made while one is in flight gets that one's promise.
 */
function loadMore() {
  if (moreBusy) return moreBusy;
  const region = main();
  const sentinel = region?.querySelector('span[data-next]');
  if (!sentinel) return Promise.resolve(false);
  moreBusy = (async () => {
    let appended = false;
    try {
      const cursor = sentinel.getAttribute('data-next') ?? '';
      const html = await fragment(`${location.pathname}?before=${encodeURIComponent(cursor)}`, 'more');
      if (html === null || main() !== region || !region.contains(sentinel)) return false;
      const box = document.createElement('div');
      swap(box, html);
      const next = box.querySelector('span[data-next]');
      next?.remove();
      grid()?.append(...box.children);
      markNew();
      if (next) {
        sentinel.setAttribute('data-next', next.getAttribute('data-next') ?? '');
      } else {
        region.querySelector('.ws-more')?.remove();
        sentinel.remove();
      }
      rove(null);
      appended = true;
    } finally {
      moreBusy = null;
    }
    watchSentinel();
    if (largeId) fillStage();
    return appended;
  })();
  return moreBusy;
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

/**
 * Sets the selection to exactly `want` (cells in page order); past the cap it stops and says so.
 * `sheet` as afterSelect's.
 */
function select(want, sheet = true) {
  const limit = cap();
  const keep = new Set(want.slice(0, limit));
  for (const c of cells()) mark(c, keep.has(c));
  if (want.length > limit) sayCap();
  afterSelect(sheet);
}

/**
 * After the selection changed: Clear selection, the sheet and the panel follow it. Below 1200 px the
 * panel is a bottom sheet while `ws-sheet` is set: a selection opens it, Gallery settings opens it with
 * nothing selected. `sheet` false keeps the sheet as it is (a step in the large view, a select-mark tap).
 * The selection bar (T075) shows the count while photos are selected (CSS hides it with the sheet open).
 */
function afterSelect(sheet = true) {
  const n = selected().length;
  const clear = main()?.querySelector('.ws-clear');
  if (clear) clear.hidden = n === 0;
  const bar = main()?.querySelector('.ws-selbar');
  if (bar) {
    bar.hidden = n === 0;
    const count = bar.querySelector('.ws-selbar-n');
    if (count) count.textContent = String(n);
  }
  if (n > 0) settingsOn = false;
  const ws = host();
  if (sheet || n === 0) ws?.classList.toggle('ws-sheet', n > 0 || settingsOn);
  showPanel();
}

// ── the sheet below 1200 px (T057) ──────────────────────────────────────────────────────────

let settingsOn = false;

/** Gallery settings: the gallery's panel as the sheet (the selection, if any, is cleared first). */
function openSettings() {
  if (galleryOf() === null) return;
  if (selected().length > 0) clearSelection();
  settingsOn = true;
  host()?.classList.add('ws-sheet');
}

/** Add to gallery on the selection bar (T075): the sheet opens on the selection's panel, at its checklist. */
function openSelectionSheet() {
  if (selected().length === 0) return;
  host()?.classList.add('ws-sheet');
  panel()?.querySelector(':scope > .side-panel .ws-checks')?.scrollIntoView({ block: 'nearest' });
}

/** Done: the sheet closes; the selection stays (Clear selection clears it). */
function closeSheet() {
  settingsOn = false;
  host()?.classList.remove('ws-sheet');
}

function clearSelection() {
  anchorId = null;
  select([]);
}

/**
 * Ctrl/Cmd-click, the select mark and Space: add or remove one cell; adding past the cap is refused.
 * `sheet` false (the select mark, T075) leaves the sheet as it is.
 */
function toggle(cell, sheet = true) {
  const on = cell.getAttribute('aria-selected') !== 'true';
  if (on && selected().length >= cap()) {
    sayCap();
    return;
  }
  anchorId = cell.dataset.id;
  select(cells().filter((c) => (c === cell ? on : c.getAttribute('aria-selected') === 'true')), sheet);
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
    // The label would also toggle its checkbox; the selection state drives the box instead. Below
    // 1200 px the mark multi-selects without opening the sheet (T075); the bar opens it.
    e.preventDefault();
    toggle(cell, false);
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
  if (e.key === 'Escape' && menuOpen()) {
    e.preventDefault();
    setMenu(false);
    list()?.querySelector('.ws-menu-toggle')?.focus();
    return;
  }
  if (largeId && !typing(e.target) && !e.altKey && !e.ctrlKey && !e.metaKey) {
    if (e.key === 'Escape') {
      e.preventDefault();
      leaveLarge();
      return;
    }
    if (e.key === 'ArrowLeft' || e.key === 'ArrowRight') {
      e.preventDefault();
      stepLarge(e.key === 'ArrowLeft' ? -1 : 1);
      return;
    }
  }
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
  } else if (e.key === 'Enter') {
    e.preventDefault();
    openLarge(cell, true);
  }
}

// ── the large view (T055, K11, R-08, FR-036) ────────────────────────────────────────────────

/** The open photo's id while the large view is shown, else null. */
let largeId = null;
/** True when opening pushed the ?photo= entry (so going back pops it); false after a load with ?photo=. */
let largePushed = false;

const largeView = () => main()?.querySelector('.ws-largeview') ?? null;
const photoAddress = (id) => `${location.pathname}?photo=${encodeURIComponent(id)}`;

/** Selects `cell` alone (keeping the sheet as it is when `sheet` is false) unless it already is. */
function selectOnly(cell, sheet) {
  anchorId = cell.dataset.id;
  const sel = selected();
  if (sel.length === 1 && sel[0] === cell) return;
  for (const c of cells()) mark(c, c === cell);
  afterSelect(sheet);
}

/**
 * Shows `cell` large. `push` adds the ?photo= history entry (opening); otherwise the address is
 * replaced (a step, or a load that already carries ?photo=). The cell is selected, so the panel shows
 * its details throughout.
 */
function openLarge(cell, push) {
  const view = largeView();
  if (!view || !cell) return;
  const opening = view.hidden;
  selectOnly(cell, false);
  // Below 1200 px the photo is shown whole: opening closes the sheet; steps keep it as it is.
  if (opening) closeSheet();
  largeId = cell.dataset.id;
  rove(cell);
  if (opening) {
    view.hidden = false;
    main().scrollTop = 0;
  }
  fillStage();
  if (push && opening) {
    history.pushState({ ws: true, photo: largeId }, '', photoAddress(largeId));
    largePushed = true;
  } else if (new URL(location.href).searchParams.get('photo') !== largeId) {
    history.replaceState({ ws: true, photo: largeId }, '', photoAddress(largeId));
  }
}

/**
 * Fills the stage for the open photo: a copy of the cell's Picture (with its scope, so its contain-fit
 * rules size it to the stored ratio) without the overlay, each <source>'s `sizes` the width it is shown
 * at; "{n} of {total}"; Previous unavailable on the first, Next on the last (none left to load).
 */
function fillStage() {
  const view = largeView();
  const all = cells();
  const i = all.findIndex((c) => c.dataset.id === largeId);
  if (!view || i < 0) return;
  const frame = view.querySelector('.ws-stage-frame');
  const pic = all[i].querySelector('.pic')?.cloneNode(true);
  if (frame && pic instanceof Element && frame.dataset.id !== largeId) {
    pic.querySelector('.overlay')?.remove();
    pic.removeAttribute('id');
    const sizer = pic.querySelector('svg.sizer');
    const w = Number(sizer?.getAttribute('width')) || 1;
    const h = Number(sizer?.getAttribute('height')) || 1;
    const shown = Math.max(1, Math.ceil(Math.min(frame.clientWidth, frame.clientHeight * (w / h), w)));
    for (const source of pic.querySelectorAll('source')) source.setAttribute('sizes', `${shown}px`);
    pic.querySelector('picture img')?.setAttribute('loading', 'eager');
    frame.replaceChildren(pic);
    frame.setAttribute('data-id', largeId);
  }
  const pos = view.querySelector('.ws-stage-pos');
  if (pos) pos.textContent = pos.textContent.replace(/^\d+/, String(i + 1));
  const prev = view.querySelector('button[name="stage"][value="previous"]');
  const next = view.querySelector('button[name="stage"][value="next"]');
  if (prev) prev.disabled = i === 0;
  if (next) next.disabled = i === all.length - 1 && !hasMore();
}

/** Previous (-1) or Next (+1); past the last loaded cell the next chunk is loaded first. */
async function stepLarge(d) {
  let all = cells();
  const i = all.findIndex((c) => c.dataset.id === largeId);
  if (i < 0) return;
  if (d > 0 && i === all.length - 1 && hasMore()) {
    await loadMore();
    if (!largeId) return;
    all = cells();
  }
  const to = all[i + d];
  if (to) openLarge(to, false);
}

/** Back to the grid: pops the entry opening pushed, or (after a load with ?photo=) replaces the address. */
function leaveLarge() {
  if (!largeId) return;
  if (largePushed) {
    history.back();
    return;
  }
  history.replaceState({ ws: true }, '', location.pathname);
  closeLarge();
}

/** Hides the stage; the photo it showed stays selected, focused and scrolled into view. */
function closeLarge() {
  const view = largeView();
  const id = largeId;
  largeId = null;
  largePushed = false;
  if (!view || view.hidden) return;
  view.hidden = true;
  const frame = view.querySelector('.ws-stage-frame');
  frame?.replaceChildren();
  frame?.removeAttribute('data-id');
  const cell = id ? cellById(id) : null;
  if (!cell) return;
  selectOnly(cell, false);
  rove(cell);
  cell.scrollIntoView({ block: 'nearest' });
  cell.focus({ preventScroll: true });
}

/** Opens `id` large, loading chunks until its cell is on the page (R-08); unknown → the grid. */
async function openById(id, push) {
  let cell = cellById(id);
  while (!cell && hasMore()) {
    if (!(await loadMore())) break;
    cell = cellById(id);
  }
  if (cell) {
    openLarge(cell, push);
  } else {
    history.replaceState({ ws: true }, '', location.pathname);
    closeLarge();
  }
}

/** Back, forward: within the shown collection only the large view opens or closes. */
async function onPopState() {
  const url = new URL(location.href);
  const photo = url.searchParams.get('photo');
  if (url.pathname === shownPath) {
    if (photo) {
      if (photo !== largeId) await openById(photo, false);
    } else {
      closeLarge();
    }
    return;
  }
  const ok = await navigate(url.pathname, false);
  if (ok && photo) await openById(photo, false);
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
  if (el) { el.value = value ?? ''; recount(el); }
}

/** The Input atom's "n / max" count under a field, kept in step with its value (filled or typed). */
function recount(el) {
  const count = el.closest('.field')?.querySelector('.count');
  const max = el.getAttribute('maxlength');
  if (!count || !max) return;
  count.textContent = `${el.value.length} / ${max}`;
  count.classList.toggle('count-over', el.value.length > Number(max));
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
    const back = largeView()?.querySelector('button[name="stage"][value="back"]');
    if (back && heading) back.textContent = back.textContent.replace(heading.textContent, r.data.title ?? '');
    if (heading) heading.textContent = r.data.title ?? '';
    document.title = r.data.title ?? '';
    refreshChrome();
  }
}

// ── part 3 (T038): membership, order, cover, visibility, delete, new gallery ────────────────

const galleryRows = () => [...(list()?.querySelectorAll('a.ws-coll-row[data-gid]') ?? [])];
const galleryOrder = () => galleryRows().map((a) => a.dataset.gid);
const selectedIds = () => selected().map((c) => c.dataset.id);
const api = (path) => `/admin/api/${path}`;
const gApi = (gid, tail = '') => api(`galleries/${encodeURIComponent(gid)}${tail}`);

/** Shows the server's message; a refusal also when `ok` is false. */
const say = (r) => toast(r.message, !r.ok);

/**
 * Re-reads one live cell (W4, no X-Workspace header) for each id and copies the attributes that
 * depend on its galleries (data-galleries, data-consequence) onto the cell on the page, so the
 * delete confirm keeps the server's sentence. The cell node itself stays (focus, selection).
 */
async function refreshTiles(ids) {
  await Promise.all(ids.filter((id) => cellById(id)).map(async (id) => {
    let res;
    try {
      res = await fetch(`/admin/tiles/${encodeURIComponent(id)}`, { credentials: 'same-origin' });
    } catch {
      return;
    }
    if (!res.ok || res.redirected) return;
    const box = document.createElement('div');
    swap(box, await res.text());
    const fresh = box.querySelector('.ws-cell');
    const cell = cellById(id);
    if (!fresh || !cell) return;
    for (const name of ['data-galleries', 'data-consequence']) cell.setAttribute(name, fresh.getAttribute(name) ?? '');
    if (panelKind === 'photo' && panelFor === id) {
      const consequence = panel()?.querySelector(':scope > .side-panel .danger-consequence');
      if (consequence) consequence.textContent = cell.dataset.consequence ?? '';
    }
  }));
}

/** The page's cells now hold (`on`) or no longer hold gallery `gid`; the server's tiles follow. */
function setMembership(ids, gid, on) {
  for (const id of ids) {
    const cell = cellById(id);
    if (!cell) continue;
    const now = galleriesOf(cell).filter((g) => g !== gid);
    if (on) now.push(gid);
    cell.setAttribute('data-galleries', now.join(' '));
  }
  refreshTiles(ids);
}

/** Re-reads the open collection's main region (counts, switch, marks) without a history entry. */
const reloadMain = () => navigate(location.pathname, false);

// ── the checklist (FR-014, FR-015; K6) ──────────────────────────────────────────────────────

/**
 * THE JS mirror of GalleryChecklist.Match (contracts K6; the parity fact pins the two): trim; blank
 * narrows nothing and offers nothing; else the titles containing the text ignoring case, ordered
 * exact, prefix, contains, then by title ignoring case (ordinal), then the given order. A title equal
 * to the text ignoring case is "existing" and hides the offer; text over `max` offers nothing (the
 * server's refusal is shown when it is sent).
 */
function matchGalleries(query, titles, max) {
  const q = (query ?? '').trim();
  if (q.length === 0) return { shown: titles.map((_, i) => i), existing: false, offer: null };
  const up = (s) => s.toUpperCase();
  const Q = up(q);
  const rank = (t) => (up(t) === Q ? 0 : up(t).startsWith(Q) ? 1 : 2);
  const ordinal = (a, b) => (a < b ? -1 : a > b ? 1 : 0);
  const shown = titles
    .map((t, i) => ({ t, i }))
    .filter((r) => up(r.t).includes(Q))
    .sort((a, b) => rank(a.t) - rank(b.t) || ordinal(up(a.t), up(b.t)) || a.i - b.i)
    .map((r) => r.i);
  const existing = titles.some((t) => up(t) === Q);
  const offer = !existing && q.length <= max ? q : null;
  return { shown, existing, offer };
}

/** The checklist's rows in the given (home) order, numbered once with data-i before any narrowing. */
function checkRows(checks) {
  const rows = [...checks.querySelectorAll('label.ws-checks-row')];
  if (rows.some((r) => !r.hasAttribute('data-i'))) rows.forEach((r, i) => r.setAttribute('data-i', String(i)));
  return rows.sort((a, b) => Number(a.dataset.i) - Number(b.dataset.i));
}

const rowTitle = (row) => row.querySelector('.ws-checks-name')?.textContent ?? '';

/** Narrows and orders the rows as the mirror says; shows the offer when it has one. */
function narrow(field) {
  const checks = field.closest('.ws-checks');
  if (!checks) return;
  const rows = checkRows(checks);
  const m = matchGalleries(field.value, rows.map(rowTitle), Number(checks.dataset.max) || Infinity);
  const box = checks.querySelector('.ws-checks-box');
  const offer = checks.querySelector('[data-mk]');
  const shown = new Set(m.shown);
  // Shown rows first in Match order, the hidden rest after them in the given order (as the server renders).
  for (const i of m.shown) box?.insertBefore(rows[i], offer);
  rows.forEach((r, i) => {
    r.hidden = !shown.has(i);
    if (r.hidden) box?.insertBefore(r, offer);
  });
  if (offer) {
    offer.hidden = m.offer === null;
    offer.textContent = m.offer === null ? '' : `New gallery \u201C${m.offer}\u201D`;
  }
  const msg = checks.querySelector('.ws-checks-msg');
  if (msg) msg.textContent = '';
}

/** Tick → A7, untick → A8, for the photos in the panel's context. */
async function tick(box) {
  const gid = box.getAttribute('data-member');
  const ids = selectedIds();
  if (!gid || ids.length === 0) return;
  const on = box.checked;
  box.removeAttribute('aria-checked');
  box.disabled = true;
  const r = await write(gApi(gid, on ? '/photos' : '/photos/remove'), { ids });
  box.disabled = false;
  say(r);
  if (!r.ok) {
    box.checked = !on;
    return;
  }
  if (!on && galleryOf() === gid) {
    // US3-9: the photos leave the open gallery, so their cells leave the grid.
    await reloadMain();
  } else {
    setMembership(ids, gid, on);
  }
  refreshChrome();
}

/** Enter in the find field or the offer: A7 when the text is a gallery's title, else A3 with the ids. */
async function addByName(checks) {
  const field = checks.querySelector('[data-gq]');
  const ids = selectedIds();
  const text = (field?.value ?? '').trim();
  if (!field || text.length === 0 || ids.length === 0) return;
  const kind = panelKind;
  const rows = checkRows(checks);
  const hit = rows.find((r) => rowTitle(r).toUpperCase() === text.toUpperCase());
  const gid = hit?.querySelector('input[data-member]')?.getAttribute('data-member') ?? null;
  const r = gid
    ? await write(gApi(gid, '/photos'), { ids })
    : await write(api('galleries'), { title: text, ids });
  say(r);
  if (!r.ok) {
    const msg = checks.querySelector('.ws-checks-msg');
    if (msg) msg.textContent = r.message;
    field.focus();
    return;
  }
  setMembership(ids, gid ?? r.data.id, true);
  await refreshChrome();
  // The chrome refresh re-filled the panel from the cells: focus goes back to its (empty) field.
  if (panelKind === kind) panel()?.querySelector(':scope > .side-panel [data-gq]')?.focus();
}

// ── order: drag, Move earlier/later (A9), Move up/down (A4) (R-07) ─────────────────────────

/** A9 with the full order of the open gallery; on success the cells move into that order. */
async function saveCellOrder(order) {
  const gid = galleryOf();
  const now = cells().map((c) => c.dataset.id);
  if (!gid || order.join(' ') === now.join(' ')) return;
  const r = await write(gApi(gid, '/order'), { order });
  say(r);
  if (!r.ok || galleryOf() !== gid) return;
  const sheet = grid();
  for (const id of order) {
    const cell = cellById(id);
    if (cell) sheet?.append(cell);
  }
}

/** A4 with the full home order; on success the list is re-read. */
async function saveGalleryOrder(order) {
  if (order.join(' ') === galleryOrder().join(' ')) return;
  const r = await write(api('galleries/order'), { order });
  say(r);
  if (r.ok) refreshChrome();
}

/** `ids` moved (in page order) to just before `before` (null: the end) within `all`. */
function moved(all, ids, before) {
  const moving = all.filter((id) => ids.includes(id));
  const rest = all.filter((id) => !ids.includes(id));
  let at = rest.length;
  if (before !== null) {
    const from = all.indexOf(before);
    const next = all.slice(from).find((id) => !ids.includes(id));
    if (next !== undefined) at = rest.indexOf(next);
  }
  return [...rest.slice(0, at), ...moving, ...rest.slice(at)];
}

function swapWith(all, id, step) {
  const i = all.indexOf(id);
  const j = i + step;
  if (i < 0 || j < 0 || j >= all.length) return all;
  const out = [...all];
  [out[i], out[j]] = [out[j], out[i]];
  return out;
}

let drag = null;

function clearDragMarks() {
  for (const el of host()?.querySelectorAll('.ws-over, .ws-before, .ws-drop') ?? []) {
    el.classList.remove('ws-over', 'ws-before', 'ws-drop');
  }
}

function onDragStart(e) {
  const t = e.target instanceof Element ? e.target : null;
  const row = t?.closest('.ws-list a.ws-coll-row[data-gid]');
  const cell = t?.closest('.ws-main .ws-cell');
  if (row) {
    drag = { kind: 'gallery', ids: [row.dataset.gid], target: null };
  } else if (cell) {
    const sel = selected();
    drag = { kind: 'cells', ids: sel.includes(cell) ? sel.map((c) => c.dataset.id) : [cell.dataset.id], target: null };
  } else {
    return;
  }
  e.dataTransfer.effectAllowed = drag.kind === 'cells' ? 'copyMove' : 'move';
  e.dataTransfer.setData('text/plain', drag.ids.join(' '));
}

/** Where a drop at this event would go: { kind: 'add', gid } | { kind: 'cells'|'gallery', before }. */
function dropAt(e) {
  const t = e.target instanceof Element ? e.target : null;
  if (!drag || !t) return null;
  const row = t.closest('.ws-list a.ws-coll-row[data-gid]');
  if (drag.kind === 'gallery') {
    if (!row) return null;
    const box = row.getBoundingClientRect();
    const rows = galleryRows();
    const at = e.clientY > box.top + box.height / 2 ? rows[rows.indexOf(row) + 1] ?? null : row;
    return { kind: 'gallery', before: at, mark: at, cls: 'ws-before' };
  }
  if (row) return { kind: 'add', gid: row.dataset.gid, mark: row, cls: 'ws-over' };
  if (galleryOf() === null || !t.closest('.ws-main .cs')) return null;
  const cell = t.closest('.ws-cell');
  if (!cell) return drag.target?.kind === 'cells' ? drag.target : null;
  const box = cell.getBoundingClientRect();
  const all = cells();
  const at = e.clientX > box.left + box.width / 2 ? all[all.indexOf(cell) + 1] ?? null : cell;
  return { kind: 'cells', before: at, mark: at, cls: 'ws-drop' };
}

function onDragOver(e) {
  const at = dropAt(e);
  if (!at) return;
  e.preventDefault();
  e.dataTransfer.dropEffect = at.kind === 'add' ? 'copy' : 'move';
  drag.target = at;
  if (at.mark?.classList.contains(at.cls)) return;
  clearDragMarks();
  at.mark?.classList.add(at.cls);
}

function onDrop(e) {
  const at = dropAt(e) ?? null;
  const d = drag;
  clearDragMarks();
  drag = null;
  if (!at || !d) return;
  e.preventDefault();
  if (at.kind === 'add') {
    addTo(at.gid, d.ids);
  } else if (at.kind === 'cells') {
    saveCellOrder(moved(cells().map((c) => c.dataset.id), d.ids, at.before?.dataset.id ?? null));
  } else {
    saveGalleryOrder(moved(galleryOrder(), d.ids, at.before?.dataset.gid ?? null));
  }
}

function onDragEnd() {
  clearDragMarks();
  drag = null;
}

/** Cells dropped on a gallery's name: A7. */
async function addTo(gid, ids) {
  const r = await write(gApi(gid, '/photos'), { ids });
  say(r);
  if (!r.ok) return;
  setMembership(ids, gid, true);
  if (panelKind === 'photo' || panelKind === 'selection') showPanel();
  refreshChrome();
}

// ── cover (A10), Public (A6) ────────────────────────────────────────────────────────────────

/** The Cover mark moves to the photo the server now names as the cover. */
function moveCoverMark(id) {
  const tpl = list()?.querySelector('template.ws-tpl-marks')?.content.querySelector('.ws-mark');
  if (!tpl) return;
  const word = tpl.textContent;
  for (const m of main()?.querySelectorAll('.ws-cell .ws-cell-marks .ws-mark') ?? []) {
    if (m.textContent !== word) continue;
    const holder = m.parentElement;
    m.remove();
    if (holder && holder.children.length === 0) holder.remove();
  }
  marksOf(cellById(id))?.prepend(tpl.cloneNode(true));
}

/** The cell's marks holder (Cover, New), made when missing; null when the cell is not a PhotoCell. */
function marksOf(cell) {
  const sel = cell?.querySelector('.ws-cell-sel');
  if (!sel) return null;
  let holder = cell.querySelector('.ws-cell-marks');
  if (!holder) {
    holder = document.createElement('span');
    holder.setAttribute('class', 'ws-cell-marks');
    // The PhotoCell scope (b-*) so the component's own rule places it.
    for (const a of sel.attributes) if (a.name.startsWith('b-')) holder.setAttribute(a.name, a.value);
    sel.after(holder);
  }
  return holder;
}

async function setCover() {
  const gid = galleryOf();
  const id = panelFor;
  if (!gid || !id) return;
  const r = await write(gApi(gid, '/cover'), { image: id });
  say(r);
  if (r.ok && galleryOf() === gid) moveCoverMark(r.data.cover);
}

/** A6: the switch applies at once; a refusal puts it back and shows the reason beside it. */
async function setPublic(sw) {
  const gid = galleryOf();
  if (!gid) return;
  const want = sw.checked;
  sw.disabled = true;
  const r = await write(gApi(gid, '/visibility'), { public: want });
  sw.disabled = false;
  say(r);
  if (!r.ok) {
    sw.checked = !want;
    const hint = sw.closest('label.switch')?.nextElementSibling;
    if (hint?.matches('p.hint')) hint.textContent = r.message;
    return;
  }
  // The heading's Public/Private, View on site and the switch's hint are the server's: re-read them.
  await reloadMain();
  refreshChrome();
}

// ── delete (DangerConfirm, then A2 / A11) ───────────────────────────────────────────────────

const invokers = typeof HTMLButtonElement !== 'undefined' && 'command' in HTMLButtonElement.prototype;

async function deletePhotos() {
  const ids = selectedIds();
  if (ids.length === 0) return;
  const r = await write(api('photos/delete'), { ids });
  say(r);
  if (!r.ok) return;
  await reloadMain();
  refreshChrome();
}

async function deleteGallery() {
  const gid = galleryOf();
  if (!gid) return;
  const r = await write(gApi(gid, '/delete'), {});
  say(r);
  if (!r.ok) return;
  await navigate('/admin', true);
  refreshChrome();
}

function onConfirm(btn) {
  const dialog = btn.closest('dialog');
  if (!dialog) return false;
  dialog.close();
  if (dialog.id === 'ws-del-gallery') deleteGallery();
  else deletePhotos();
  return true;
}

// ── New gallery: naming in the list (A3 with no ids, then open it) ─────────────────────────

let naming = null;

function startNaming(button) {
  const tpl = list()?.querySelector('template.ws-tpl-naming');
  const label = tpl?.content.querySelector('.ws-naming')?.cloneNode(true);
  if (!label) return;
  naming = { button, label, busy: false };
  button.replaceWith(label);
  label.querySelector('input')?.focus();
}

function stopNaming() {
  const n = naming;
  naming = null;
  if (n?.label.isConnected) n.label.replaceWith(n.button);
}

async function createNamed(input) {
  const n = naming;
  if (!n || n.busy) return;
  n.busy = true;
  const r = await write(api('galleries'), { title: input.value });
  say(r);
  if (!r.ok) {
    n.busy = false;
    input.focus();
    return;
  }
  naming = null;
  await navigate(`/admin/g/${encodeURIComponent(r.data.id)}`, true);
  await refreshChrome();
}

function onKeyIn(e) {
  const t = e.target instanceof Element ? e.target : null;
  if (!t) return;
  if (t.matches('.ws-list .ws-naming-input')) {
    if (e.key === 'Enter') {
      e.preventDefault();
      createNamed(t);
    } else if (e.key === 'Escape') {
      e.preventDefault();
      stopNaming();
    }
  } else if (t.matches('.ws-panel [data-gq]') && e.key === 'Enter') {
    e.preventDefault();
    const checks = t.closest('.ws-checks');
    if (checks) addByName(checks);
  }
}

function onFocusOut(e) {
  if (naming && !naming.busy && e.target instanceof Element && e.target.matches('.ws-list .ws-naming-input')) stopNaming();
}


// ── the upload tray's hooks (T046, C13) ─────────────────────────────────────────────────────

const STATE_URL = '/admin/upload/state';
let uploadModule = null;

/** upload.js, imported once (dynamic import only: the page has exactly one <script>). */
function upload() {
  uploadModule ??= import(host()?.dataset.uploadSrc ?? '').catch((e) => {
    uploadModule = null;
    toast(statusLine(null, e), true);
    return null;
  });
  return uploadModule;
}

function trayCollectionChanged() {
  uploadModule?.then((m) => m?.collectionChanged());
}

/** U2 as the page loads: an open batch reopens the tray (and its New marks). */
async function resumeUpload() {
  let state = null;
  try {
    const res = await fetch(STATE_URL, { credentials: 'same-origin' });
    if (res.ok && !res.redirected) state = await res.json();
  } catch {
    state = null;
  }
  if (!state?.batch) return;
  const m = await upload();
  for (const id of m?.resume(state) ?? []) newIds.add(id);
  markNew();
}

const carriesFiles = (e) => Array.from(e.dataTransfer?.types ?? []).includes('Files');

/** Files dragged anywhere over the window may be dropped (US2-2). */
function onFileDragOver(e) {
  if (!carriesFiles(e)) return;
  e.preventDefault();
  e.dataTransfer.dropEffect = 'copy';
}

/** Files dropped anywhere on the window open the tray, which takes them (US2-2). */
function onFileDrop(e) {
  if (!carriesFiles(e)) return;
  e.preventDefault();
  const files = Array.from(e.dataTransfer.files ?? []);
  if (files.length > 0) upload().then((m) => m?.show(files));
}

/** Photos joined while this page is open; they carry the New mark until Done or Close. */
const newIds = new Set();

function newMark() {
  return list()?.querySelector('template.ws-tpl-marks')?.content.querySelectorAll('.ws-mark')[1] ?? null;
}

/** Every cell of a joined photo on the page carries the New mark once. */
function markNew() {
  const tpl = newMark();
  if (!tpl || newIds.size === 0) return;
  for (const cell of cells()) {
    if (!newIds.has(cell.dataset.id)) continue;
    const marks = [...cell.querySelectorAll('.ws-cell-marks .ws-mark')];
    if (marks.some((m) => m.textContent === tpl.textContent)) continue;
    marksOf(cell)?.append(tpl.cloneNode(true));
  }
}

/** Done / Close: the New marks go. */
function clearNew() {
  newIds.clear();
  const word = newMark()?.textContent;
  for (const m of main()?.querySelectorAll('.ws-cell .ws-cell-marks .ws-mark') ?? []) {
    if (m.textContent !== word) continue;
    const holder = m.parentElement;
    m.remove();
    if (holder && holder.children.length === 0) holder.remove();
  }
}

/**
 * Where a joined photo goes on this page (C13): first in All photos; first in Not in a gallery when
 * it went to no gallery; last in its destination gallery when that is open; elsewhere nowhere.
 */
function placeFor(gallery) {
  const path = location.pathname;
  if (path === '/admin') return 'first';
  if (path === '/admin/unplaced') return gallery ? null : 'first';
  const open = galleryOf(path);
  return open && gallery?.id === open ? 'last' : null;
}

/** W4: the photo's cell, rendered by the server; null when it cannot be read. */
async function tileCell(id) {
  let res;
  try {
    res = await fetch(`/admin/tiles/${encodeURIComponent(id)}`, { credentials: 'same-origin' });
  } catch {
    return null;
  }
  if (!res.ok || res.redirected) return null;
  const box = document.createElement('div');
  swap(box, await res.text());
  return box.querySelector('.ws-cell');
}

let chromeTimer = 0;
let chromeAt = 0;

/** A chrome refresh (counts) at most once a second while photos keep joining. */
function refreshChromeSoon() {
  if (chromeTimer) return;
  chromeTimer = setTimeout(() => {
    chromeTimer = 0;
    chromeAt = Date.now();
    refreshChrome();
  }, Math.max(0, chromeAt + 1000 - Date.now()));
}

async function onJoined({ imageId, gallery }) {
  newIds.add(imageId);
  const where = placeFor(gallery);
  const path = location.pathname;
  if (where && !cellById(imageId)) {
    const cell = await tileCell(imageId);
    if (cell && location.pathname === path && !cellById(imageId)) {
      const sheet = grid();
      if (!sheet) {
        // The collection was empty (the sentence, no grid): the server renders it with the photo.
        await reloadMain();
      } else {
        if (where === 'first') sheet.prepend(cell);
        else sheet.append(cell);
        rove(null);
      }
    }
  }
  markNew();
  refreshChromeSoon();
}

// ── event tables (part 3, T038, appends its own rows) ───────────────────────────────────────

/** [selector, handler(el, event) → handled]: the first row whose selector matches and handles wins. */
const clicks = [
  // The collections menu below 1200 px (T074).
  ['.ws-list .ws-menu-toggle', () => {
    setMenu(!menuOpen());
    return true;
  }],
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
  // The large view (T055): its bar, and nothing else in it clears the selection.
  ['.ws-main .ws-largeview button[name="stage"]', (button) => {
    if (button.value === 'back') leaveLarge();
    else stepLarge(button.value === 'previous' ? -1 : 1);
    return true;
  }],
  ['.ws-main .ws-largeview', () => true],
  ['.ws-panel .side-panel .ws-large', () => {
    openLarge(cellById(panelFor ?? ''), true);
    return true;
  }],
  // The sheet below 1200 px (T057).
  ['.ws-main .ws-settings', () => {
    openSettings();
    return true;
  }],
  ['.ws-panel .side-panel .ws-done', () => {
    closeSheet();
    return true;
  }],
  ['.ws-main .ws-selbar .ws-selbar-add', () => {
    openSelectionSheet();
    return true;
  }],
  ['.ws-main .ws-selbar', () => true],
  ['.ws-main .ws-cell', (cell, e) => {
    onCellClick(cell, e);
    return true;
  }],
  // The tray (T046): Add photos opens it; its own clicks are upload.js's.
  ['.ws-main .ws-add', () => {
    upload().then((m) => m?.show());
    return true;
  }],
  ['.ws-main upload-tray', () => true],
  // Part 3 (T038).
  ['.ws-list button[name="collection"][value="new"]', (button) => {
    startNaming(button);
    return true;
  }],
  ['.ws-panel .side-panel [data-mk]', (button) => {
    const checks = button.closest('.ws-checks');
    if (checks) addByName(checks);
    return true;
  }],
  ['.ws-panel .side-panel .ws-cover', () => {
    setCover();
    return true;
  }],
  ['.ws-panel .side-panel .ws-earlier, .ws-panel .side-panel .ws-later', (button) => {
    if (!panelFor) return false;
    saveCellOrder(swapWith(cells().map((c) => c.dataset.id), panelFor, button.classList.contains('ws-earlier') ? -1 : 1));
    return true;
  }],
  ['.ws-panel .side-panel .ws-move-up, .ws-panel .side-panel .ws-move-down', (button) => {
    const gid = galleryOf();
    if (!gid) return false;
    saveGalleryOrder(swapWith(galleryOrder(), gid, button.classList.contains('ws-move-up') ? -1 : 1));
    return true;
  }],
  // DangerConfirm opens and closes by invoker commands; this is the fallback where they are missing.
  ['.ws-panel .danger-trigger', (button) => {
    if (invokers) return false;
    const dialog = document.getElementById(button.getAttribute('commandfor') ?? '');
    if (dialog instanceof HTMLDialogElement && !dialog.open) dialog.showModal();
    return true;
  }],
  ['.ws-panel .danger-dialog .danger-cancel', (button) => {
    if (invokers) return false;
    button.closest('dialog')?.close();
    return true;
  }],
  ['.ws-panel .danger-dialog .danger-actions button:not(.danger-cancel)', (button) => onConfirm(button)],
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
  // Part 3 (T038).
  ['.ws-panel .side-panel input[data-member]', (box) => {
    tick(box);
    return true;
  }],
  ['.ws-panel .side-panel input[role="switch"][name="public"]', (sw) => {
    if (panelKind !== 'default' || galleryOf() === null) return false;
    setPublic(sw);
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
    document.addEventListener('click', onOutsideClick, true);
    this.addEventListener('click', (e) => {
      if (!dispatch(clicks, e)) onEmptyClick(e);
    });
    this.addEventListener('change', (e) => dispatch(changes, e));
    this.addEventListener('input', (e) => {
      if (e.target instanceof Element && e.target.matches('.ws-panel [data-gq]')) narrow(e.target);
      else if (e.target instanceof Element && e.target.matches('.ws-panel .field input, .ws-panel .field textarea')) recount(e.target);
    });
    this.addEventListener('keydown', onKeyIn);
    this.addEventListener('focusout', onFocusOut);
    this.addEventListener('dragstart', onDragStart);
    this.addEventListener('dragover', onDragOver);
    this.addEventListener('drop', onDrop);
    this.addEventListener('dragend', onDragEnd);
    document.addEventListener('keydown', onKey);
    window.addEventListener('popstate', onPopState);
    this.addEventListener('dblclick', (e) => {
      const cell = e.target instanceof Element ? e.target.closest('.ws-main .ws-cell') : null;
      if (cell && !e.target.closest('label.select-mark')) openLarge(cell, true);
    });
    document.addEventListener('dragover', onFileDragOver);
    document.addEventListener('drop', onFileDrop);
    document.addEventListener('upload:joined', (e) => onJoined(e.detail ?? {}));
    document.addEventListener('upload:closed', clearNew);
    document.addEventListener('upload:said', (e) => toast(e.detail?.text ?? '', !!e.detail?.failed));
    document.addEventListener('upload:stale', () => refreshChrome());
    afterSelect();
    rove(null);
    watchSentinel();
    // `ready` after the U2 check, so an open batch's tray is back before anyone looks, and after a
    // ?photo= in the address has been opened large (R-08).
    const photo = new URL(location.href).searchParams.get('photo');
    Promise.allSettled([resumeUpload(), photo ? openById(photo, false) : null]).finally(() => {
      this.dataset.script = 'ready';
    });
  }
}

customElements.define('admin-workspace', AdminWorkspace);
