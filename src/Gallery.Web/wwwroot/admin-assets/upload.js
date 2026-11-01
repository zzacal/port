// 008 C4 page script for /admin/upload (contracts/components.md C4, contracts/http.md U2–U11).
//
// Markup comes from ONE source: the server renders <template data-tile|data-footer|data-refusals>
// with the real 005 components (research R-11). This script clones a template and fills it only
// through textContent / setAttribute, so a file name containing "<" can never become markup.
//   - Tiles and the refusal row carry sentinels (U+2063 name U+2063, …), replaced by walking text
//     nodes and attribute values.
//   - Footer counts are integers, so the footer templates are rendered with 0s and the counts are
//     replaced BY STRUCTURE, not by sentinel:
//       .bf-summary > span                      "{ready} of {total} ready" (inprogress, confirmdiscard)
//       .bf-summary > progress                  value={ready} max={total}
//       .bf-summary > span                      "All {total} photos ready" (ready)
//       .bf-summary > span > first text node    "{ready} ready · " and .bf-failed "{failed} failed" (blocked)
//       .bf-actions > button.btn-primary        "Add {total} photos to library"
//       .bf-reason                              "Restart or delete the {failed} failed photos to continue" (blocked)
//       .danger-confirm .confirm-body p         the text node holding the count gets {total}
//
// App calls send X-CSRF-TOKEN and credentials "same-origin". Storage PUTs send credentials "omit"
// and exactly one header, Content-Type (FR-044).

const MARK = '\u2063';
const SENTINEL = /\u2063(\w+)\u2063/g;
const HEARTBEAT_MS = 20_000;
const MAX_PUTS = 4;
const ACTIVE = new Set(['queued', 'processing', 'uploading']);

const root = document.querySelector('upload-batch');
if (root) start(root);

function start(root) {
  const csrf = root.dataset.csrf ?? '';
  const maxBytes = Number(root.dataset.maxBytes);
  const maxPhotos = Number(root.dataset.maxPhotos);
  const stateUrl = root.dataset.stateUrl || '/admin/upload/state';
  const workerUrl = root.dataset.worker;
  const templates = root.querySelector('.upload-templates');
  const sheet = root.querySelector('.cs');
  const dropzone = root.querySelector('.dropzone');
  const input = dropzone.querySelector('.dz-input');
  const idleTitle = dropzone.querySelector('.dz-title');

  let batchId = root.dataset.batch ?? '';
  let discardOpen = false;
  /** itemId → { itemId, attemptId, fileName, fileSize, state, percent, reason, masterStored, file, run, pickAgain } */
  const items = new Map();
  const queue = [];
  let busy = false;
  let current = null;
  let worker = null;
  let pendingEncode = null;
  let generation = 0; // bumped by every U2 reconcile

  // ── helpers ────────────────────────────────────────────────────────────────

  async function api(path, body) {
    const init = { method: body === undefined ? 'GET' : 'POST', credentials: 'same-origin', headers: { 'X-CSRF-TOKEN': csrf } };
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

  function clone(selector) {
    const tpl = templates.querySelector(selector);
    return tpl.content.firstElementChild.cloneNode(true);
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

  function formatSize(bytes) {
    return bytes >= 1_048_576 ? `${(bytes / 1_048_576).toFixed(1)} MB` : `${Math.round(bytes / 1024)} KB`;
  }

  function overLimit(size) {
    const limit = Math.round((maxBytes / 1_048_576) * 10) / 10;
    return `${(size / 1_048_576).toFixed(1)} MB, over the ${limit} MB limit`;
  }

  // ── tiles and footer ───────────────────────────────────────────────────────

  function tileFor(item) {
    const el = fill(clone(`template[data-tile="${item.state}"]`), {
      name: item.fileName, id: item.itemId, reason: item.reason ?? '',
    });
    el.querySelector('.meta .size').textContent = formatSize(item.fileSize);
    if (item.state === 'uploading') {
      el.querySelector('.status > span').textContent = `Uploading ${item.percent}%`;
      el.querySelector('.status > progress').setAttribute('value', String(item.percent));
    }
    if (item.pickAgain) addPickAgain(el, item);
    return el;
  }

  function addPickAgain(el, item) {
    const status = el.querySelector('.status');
    const label = status.querySelector('span');
    label.textContent = `Pick ${item.fileName} again`;
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
    status.append(picker);
  }

  function tileOf(itemId) {
    for (const el of sheet.children) if (el.dataset.item === itemId) return el;
    return null;
  }

  function renderTile(item) {
    const fresh = tileFor(item);
    const old = tileOf(item.itemId);
    if (old) old.replaceWith(fresh); else sheet.append(fresh);
  }

  function renderAll() {
    const tiles = Array.from(items.values()).map(tileFor);
    sheet.replaceChildren(...tiles);
    renderFooter();
  }

  function renderFooter() {
    const old = root.querySelector(':scope > .batch-footer');
    const list = Array.from(items.values());
    const total = list.length;
    if (total === 0) {
      if (old) old.remove();
      discardOpen = false;
      return;
    }
    const ready = list.filter((i) => i.state === 'succeeded').length;
    const failed = list.filter((i) => i.state === 'failed' || i.state === 'interrupted').length;
    const kind = discardOpen ? 'confirmdiscard'
      : ready === total ? 'ready'
        : failed > 0 ? 'blocked'
          : 'inprogress';

    const el = clone(`template[data-footer="${kind}"]`);
    const summary = el.querySelector('.bf-summary');
    const span = summary.querySelector(':scope > span');
    if (kind === 'inprogress' || kind === 'confirmdiscard') {
      span.textContent = `${ready} of ${total} ready`;
      const bar = summary.querySelector(':scope > progress');
      bar.setAttribute('value', String(ready));
      bar.setAttribute('max', String(total));
    } else if (kind === 'ready') {
      span.textContent = `All ${total} photos ready`;
    } else {
      span.firstChild.nodeValue = `${ready} ready · `;
      span.querySelector('.bf-failed').textContent = `${failed} failed`;
    }
    el.querySelector('.bf-actions > button.btn-primary').textContent = `Add ${total} photos to library`;
    const reason = el.querySelector('.bf-reason');
    if (reason && kind === 'blocked') reason.textContent = `Restart or delete the ${failed} failed photos to continue`;
    const consequence = el.querySelector('.danger-confirm .confirm-body p');
    if (consequence) {
      for (const node of consequence.childNodes) {
        if (node.nodeType === Node.TEXT_NODE && /\d+/.test(node.nodeValue)) node.nodeValue = node.nodeValue.replace(/\d+/, String(total));
      }
    }

    if (old) old.replaceWith(el); else templates.before(el);
  }

  function setState(item, state, extra = {}) {
    Object.assign(item, { state }, extra);
    if (items.has(item.itemId)) {
      renderTile(item);
      renderFooter();
    }
  }

  // ── refusals and drag-over (dropzone states) ───────────────────────────────

  function dropzoneState(name) {
    for (const c of Array.from(dropzone.classList)) if (c.startsWith('state-')) dropzone.classList.remove(c);
    dropzone.classList.add(`state-${name}`);
  }

  function showRefusals(refusals) {
    const old = dropzone.querySelector('.dz-refusals');
    if (old) old.remove();
    if (refusals.length === 0) {
      dropzoneState('idle');
      return;
    }
    const list = clone('template[data-refusals]').querySelector('.dz-refusals');
    const row = list.querySelector('li');
    list.replaceChildren(...refusals.map((r) => fill(row.cloneNode(true), { name: r.name, reason: r.reason })));
    dropzone.append(list);
    dropzoneState('refused');
  }

  let dragTitle = null;
  function dragOver(on) {
    const shown = dropzone.querySelector('.dz-title');
    if (on) {
      if (!dragTitle) {
        dragTitle = idleTitle.cloneNode(false);
        dragTitle.textContent = 'Drop to add these photos to the batch';
      }
      if (shown !== dragTitle) shown.replaceWith(dragTitle);
      dropzoneState('dragover');
    } else {
      if (shown !== idleTitle) shown.replaceWith(idleTitle);
      dropzoneState(dropzone.querySelector('.dz-refusals') ? 'refused' : 'idle');
    }
  }

  // ── pick time (FR-004) and register (U3) ───────────────────────────────────

  async function isJpeg(file) {
    const head = new Uint8Array(await file.slice(0, 3).arrayBuffer());
    return head.length === 3 && head[0] === 0xff && head[1] === 0xd8 && head[2] === 0xff;
  }

  async function refusalFor(file) {
    if (!(await isJpeg(file))) return 'not a JPEG';
    if (file.size > maxBytes) return overLimit(file.size);
    return null;
  }

  async function addFiles(files) {
    const refusals = [];
    const accepted = [];
    let remaining = maxPhotos - items.size;
    for (const file of files) {
      const reason = await refusalFor(file);
      if (reason) { refusals.push({ name: file.name, reason }); continue; }
      if (remaining <= 0) { refusals.push({ name: file.name, reason: `batch is full (${maxPhotos} photos)` }); continue; }
      remaining--;
      accepted.push(file);
    }

    if (accepted.length > 0) {
      const r = await api('/admin/upload/items', {
        files: accepted.map((f) => ({ name: f.name, size: f.size, type: 'image/jpeg' })),
      });
      if (r.status === 200 && r.json) {
        batchId = r.json.batchId;
        for (const a of r.json.accepted) {
          const file = accepted[a.index];
          const item = {
            itemId: a.itemId, attemptId: a.attemptId, fileName: file.name, fileSize: file.size,
            state: 'queued', percent: 0, reason: null, masterStored: false, file, run: 0, pickAgain: false, born: generation,
          };
          items.set(item.itemId, item);
          renderTile(item);
          enqueue(item, { masterUrl: a.master.url });
        }
        for (const x of r.json.refused) refusals.push({ name: x.name || accepted[x.index]?.name || '', reason: x.reason });
        renderFooter();
      } else {
        const reason = r.json?.message ?? `could not add (${r.status || 'network error'})`;
        for (const f of accepted) refusals.push({ name: f.name, reason });
      }
    }
    showRefusals(refusals);
  }

  // ── the worker (one, sequential) ───────────────────────────────────────────

  function encode(file, widths) {
    if (!worker) {
      worker = new Worker(workerUrl, { type: 'module' });
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

  // ── per item (C4 step 4) ───────────────────────────────────────────────────

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

  const progress = (item) => api(`/admin/upload/items/${item.itemId}/progress`,
    { attemptId: item.attemptId, state: item.state, percent: item.percent });

  async function fail(item, reason) {
    const text = String(reason).slice(0, 200);
    await api(`/admin/upload/items/${item.itemId}/fail`, { attemptId: item.attemptId, reason: text });
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
        const r = await api(`/admin/upload/items/${item.itemId}/master`, { attemptId: item.attemptId });
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

      const c = await api(`/admin/upload/items/${item.itemId}/complete`,
        { attemptId: item.attemptId, placeholder: result.placeholder, timings: result.timings });
      if (!live()) return;
      if (c.status === 200) setState(item, 'succeeded', { percent: 100, reason: null });
      else await refused(item, c);
    } finally {
      clearInterval(beat);
      if (current === item) current = null;
    }
  }

  // ── restart / delete / confirm / discard (C4 steps 5–7) ────────────────────

  async function restart(item) {
    const r = await api(`/admin/upload/items/${item.itemId}/restart`, { attemptId: item.attemptId });
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
    const r = await api(`/admin/upload/items/${item.itemId}/delete`, { attemptId: item.attemptId });
    if (r.status !== 204 && r.status !== 404) return reconcile();
    item.run += 1;
    items.delete(item.itemId);
    tileOf(item.itemId)?.remove();
    renderFooter();
  }

  function showFooterReason(message) {
    const footer = root.querySelector(':scope > .batch-footer');
    if (!footer) return;
    let reason = footer.querySelector('.bf-reason');
    if (!reason) {
      reason = document.createElement('span');
      reason.className = 'bf-reason';
      footer.querySelector('.bf-actions > button.btn-primary').after(reason);
    }
    reason.textContent = message;
  }

  async function confirm() {
    const r = await api('/admin/upload/confirm', { batchId });
    if (r.status === 200 && r.json?.redirect) {
      location.assign(r.json.redirect);
      return;
    }
    await reconcile();
    if (r.status === 409 && r.json?.message) showFooterReason(r.json.message);
  }

  async function discard() {
    const r = await api('/admin/upload/discard', { batchId });
    if (r.status === 200 && r.json?.redirect) location.assign(r.json.redirect);
    else location.reload();
  }

  // ── U2 reconcile (step 1; also after bfcache and any 404/409) ──────────────

  async function reconcile() {
    const asOf = ++generation;
    const r = await api(stateUrl);
    if (r.status !== 200 || !r.json) return;
    const batch = r.json.batch;
    const serverItems = batch?.items ?? [];
    if (batch?.id) batchId = batch.id;
    const ordered = [];
    for (const s of serverItems) {
      const local = items.get(s.itemId);
      // The item this page is working on keeps its live view; the server lags it by one beat.
      if (local && local === current && local.attemptId === s.attemptId && ACTIVE.has(s.state)) {
        ordered.push(local);
        continue;
      }
      const merged = Object.assign(local ?? { file: null, run: 0, pickAgain: false, born: asOf }, {
        itemId: s.itemId, attemptId: s.attemptId, fileName: s.fileName, fileSize: s.fileSize,
        state: s.state, percent: s.percent, reason: s.reason, masterStored: s.masterStored,
      });
      if (local && local.attemptId !== s.attemptId) merged.run += 1;
      if (!ACTIVE.has(merged.state)) merged.pickAgain = false;
      ordered.push(merged);
    }
    // Keep the server's order. An item registered after this U2 was sent is kept; any other item
    // the server no longer lists is gone (deleted, discarded or confirmed elsewhere).
    const listed = new Set(serverItems.map((s) => s.itemId));
    for (const item of items.values()) {
      if (listed.has(item.itemId)) continue;
      if (item.born >= asOf) ordered.push(item); else item.run += 1;
    }
    items.clear();
    for (const item of ordered) items.set(item.itemId, item);
    renderAll();
  }

  // ── events ─────────────────────────────────────────────────────────────────

  input.addEventListener('change', () => {
    const files = Array.from(input.files ?? []);
    input.value = '';
    if (files.length) addFiles(files);
  });

  const hasFiles = (e) => Array.from(e.dataTransfer?.types ?? []).includes('Files');
  document.addEventListener('dragover', (e) => {
    if (!hasFiles(e)) return;
    e.preventDefault();
    e.dataTransfer.dropEffect = 'copy';
    dragOver(true);
  });
  document.addEventListener('dragleave', (e) => {
    if (e.relatedTarget === null) dragOver(false);
  });
  document.addEventListener('drop', (e) => {
    if (!hasFiles(e)) return;
    e.preventDefault();
    dragOver(false);
    const files = Array.from(e.dataTransfer.files ?? []);
    if (files.length) addFiles(files);
  });

  root.addEventListener('click', (e) => {
    const button = e.target instanceof Element ? e.target.closest('button') : null;
    if (!button || !root.contains(button)) return;
    const tile = button.closest('[data-item]');
    if (tile) {
      e.preventDefault();
      const item = items.get(tile.dataset.item);
      if (!item) return;
      if (button.value === 'restart') restart(item);
      else if (button.value === 'delete') remove(item);
      return;
    }
    if (button.closest('.danger-confirm .confirm-body')) {
      e.preventDefault();
      discard();
      return;
    }
    if (button.matches('.bf-actions > button.btn-primary')) {
      e.preventDefault();
      if (!button.disabled) confirm();
    }
  });

  // The footer's DangerConfirm is a <details>; 'toggle' does not bubble, so listen in capture.
  root.addEventListener('toggle', (e) => {
    const details = e.target;
    if (!(details instanceof HTMLDetailsElement) || !details.matches('.batch-footer .danger-confirm')) return;
    if (details.open === discardOpen) return;
    discardOpen = details.open;
    renderFooter();
  }, true);

  window.addEventListener('pageshow', (e) => { if (e.persisted) reconcile(); });

  root.hidden = false;
  reconcile();
}
