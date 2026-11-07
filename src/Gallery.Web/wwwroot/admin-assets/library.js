// 009 C4 library script (contracts/components.md C4). Loaded on the library page (C1), the gallery
// list (C2) and the builder (C3); every behaviour below is keyed to markup only one of them has.
//
//   Library page (C1):
//   - The selection bar (.selection-bar, data-limit) shows "{n} selected" and is `hidden` at 0.
//     Clear unticks every `ids` box; "Select all on this page" ticks this page's `ids` boxes.
//   - A tick that would exceed data-limit is refused at selection time: the box stays unticked and
//     a status line in the bar names the limit (UI-7). The server still refuses (FR-008).
//   - "Add to gallery…" opens the picker <dialog id="lib-picker">; Cancel closes it. The picker's
//     newTitle input is `required` (K2) only while "New gallery" is the picked option, so picking an
//     existing gallery can submit (decisions row 58).
//   - "Delete…" opens the bulk confirm modal (DangerConfirm #lib-bulk-confirm) titled "Delete {n}
//     photos?"; the ticked ids are copied into the hidden `lib-delete` form when it opens, on every
//     tick change while open, and again when it submits. Cancel/Esc close it; focus goes back.
//   Gallery list (C2) and builder (C3):
//   - A gallery-list visibility switch (form="gal-vis-{gid}") submits its form when it changes
//     (publish/unpublish at once, no confirm). The builder's switch belongs to its Save form and
//     submits nothing on change (owner feedback: typed edits are never lost).
//   - The page heading's "New gallery" opens <dialog id="gal-new">; Cancel closes it.
//   Combobox (TokenCombobox molecule, feature 014 K1; the photo panel's Galleries field):
//   - Typing filters the options by the rules of TokenCombobox.Match (mirrored here), with the
//     first row highlighted and a "+ text" create row; Down/Up move, Enter or a mousedown picks,
//     Esc closes an open list (and stops there), Backspace in an empty input removes the last token.
//   - A pick clones the component's <template> into a pending token with its hidden add/create
//     input; × removes a token (a saved one leaves a hidden remove input) and focus moves to the
//     next ×. cb-empty / cb-warn are kept current and .cb-status announces each change.
//   Photo panel (feature 015 J1):
//   - Below 1200 px the panel is stacked after the grid: when one is open on load (after the
//     #t-{id} fragment scroll) and its Galleries field (.combobox, else the panel) starts below
//     the viewport, that field is scrolled into view.
//
// Elements are resolved when an event happens (event delegation on the document), never cached at
// load, so markup declared after the script, or replaced later, is always found. Text is set with
// textContent and elements are built with createElement or cloned from a <template> only.

const ids = (root) => [...root.querySelectorAll('input[type="checkbox"][name="ids"]')];
const ticked = (root) => ids(root).filter((box) => box.checked);

/** The library section (C1) that owns the selection bar, or null on other pages. */
function libraryRoot(from) {
  const el = from instanceof Element ? from.closest('section.lib') : null;
  return el ?? document.querySelector('section.lib');
}

function bar(root) {
  return root.querySelector('.selection-bar');
}

function limitOf(root) {
  const value = Number(bar(root)?.dataset.limit);
  return Number.isFinite(value) && value > 0 ? value : Infinity;
}

/** The bar's status line for refused ticks, created on first use. */
function limitLine(root) {
  const b = bar(root);
  let line = b.querySelector('.lib-limit');
  if (!line) {
    line = document.createElement('span');
    line.className = 'lib-limit';
    line.setAttribute('role', 'status');
    const count = b.querySelector('.count');
    (count ?? b.firstChild)?.after(line);
  }
  return line;
}

function sayLimit(root, text) {
  const b = bar(root);
  if (!b) return;
  if (text) limitLine(root).textContent = text;
  else b.querySelector('.lib-limit')?.remove();
}

/** Re-draws the count, the bar's visibility, the tiles' selected class and the open bulk confirm. */
function refresh(root) {
  const b = bar(root);
  if (!b) return;
  const n = ticked(root).length;
  const count = b.querySelector('.count');
  if (count) count.textContent = `${n} selected`;
  b.hidden = n === 0;
  for (const box of ids(root)) box.closest('.tile')?.classList.toggle('is-selected', box.checked);
  const bulk = bulkDialog();
  if (bulk?.open) {
    if (n === 0) closeBulkDelete();
    else syncBulkDelete(root);
  }
}

function limitText(limit) {
  return `You can select up to ${limit} photos at a time.`;
}

// ── bulk delete ───────────────────────────────────────────────────────────────

/** Replaces the `ids` inputs of the hidden lib-delete form with the ticked ids. */
function copyIdsToDelete(root) {
  const form = document.getElementById('lib-delete');
  if (!form) return;
  for (const old of form.querySelectorAll('input[name="ids"]')) old.remove();
  for (const box of ticked(root)) {
    const input = document.createElement('input');
    input.type = 'hidden';
    input.name = 'ids';
    input.value = box.value;
    form.append(input);
  }
}

/** The bulk confirm: a DangerConfirm rendered without its own trigger (the bar's "Delete…" opens it). */
function bulkDialog() {
  return document.getElementById('lib-bulk-confirm');
}

/** Title and action name the count ("Delete 3 photos?" / "Delete 3 photos"); ids follow the ticks. */
function syncBulkDelete(root) {
  const dialog = bulkDialog();
  if (!dialog) return;
  const n = ticked(root).length;
  const what = `${n} ${n === 1 ? 'photo' : 'photos'}`;
  const title = dialog.querySelector('.danger-title');
  if (title) title.textContent = `Delete ${what}?`;
  const action = dialog.querySelector('.danger-actions button.btn-danger');
  if (action) action.textContent = `Delete ${what}`;
  copyIdsToDelete(root);
}

function showBulkDelete(root, opener) {
  const dialog = bulkDialog();
  if (!dialog) return;
  syncBulkDelete(root);
  openDialog(dialog, opener);
}

function closeBulkDelete() {
  const dialog = bulkDialog();
  if (dialog?.open) dialog.close();
}

// ── picker ────────────────────────────────────────────────────────────────────

/** newTitle is required only while "New gallery" is picked (or nothing is picked yet). */
function syncNewTitle(dialog) {
  const title = dialog.querySelector('input[name="newTitle"]');
  if (!title) return;
  const picked = dialog.querySelector('input[type="radio"][name="gallery"]:checked');
  title.required = !picked || picked.value === 'new';
}

function openDialog(dialog, opener) {
  if (!dialog || dialog.open) return;
  if (dialog.id === 'lib-picker') syncNewTitle(dialog);
  if (opener) openers.set(dialog, opener);
  if (typeof dialog.showModal === 'function') dialog.showModal();
  else dialog.setAttribute('open', '');
}

// ── danger confirm (DangerConfirm molecule) ───────────────────────────────────
// The "{Action}…" trigger opens its <dialog class="danger-dialog"> with invoker commands
// (command="show-modal" commandfor=…); this is the fallback where they are unsupported. Whatever
// opened a dialog gets focus back when it closes (Cancel, Esc or the close button).

const openers = new WeakMap();
const invokers = typeof HTMLButtonElement !== 'undefined' && 'commandForElement' in HTMLButtonElement.prototype;

document.addEventListener('close', (e) => {
  const dialog = e.target;
  if (!(dialog instanceof HTMLDialogElement)) return;
  const opener = openers.get(dialog);
  openers.delete(dialog);
  if (opener?.isConnected && !opener.disabled) opener.focus();
}, true);

// ── combobox (TokenCombobox molecule) ─────────────────────────────────────────
// contracts/components.md K1 "Behaviour". The filter rules MIRROR TokenCombobox.Match (C#), pinned
// by GalleryFieldBrowserTests' parity test: change one, change both.

const cbOf = (el) => (el instanceof Element ? el.closest('.combobox') : null);
const cbInput = (root) => root.querySelector('.cb-input');
const cbList = (root) => root.querySelector('.cb-list');
const cbNewRow = (root) => root.querySelector('.cb-list > .cb-new');
const cbOptionRows = (root) => [...root.querySelectorAll('.cb-list > .cb-opt:not(.cb-new)')];
const cbTokenItems = (root) => [...root.querySelectorAll('.cb-tokens > .cb-token')];
const cbVisibleRows = (root) => [...root.querySelectorAll('.cb-list > .cb-opt')].filter((r) => !r.hidden);
const cbIsOpen = (root) => cbInput(root)?.getAttribute('aria-expanded') === 'true';

/** `string.Format`-style `{0}` replacement. */
const cbFormat = (format, arg) => (format ?? '{0}').split('{0}').join(arg);

/** A token's label: the text of its link or span. */
const cbTokenLabel = (li) => li.querySelector(':scope > a, :scope > span')?.textContent ?? '';

/** Ordinal comparison (UTF-16 code units, as .NET's StringComparer.Ordinal). */
const cbOrdinal = (a, b) => (a < b ? -1 : a > b ? 1 : 0);

/**
 * TokenCombobox.Match, mirrored. options: [{ id, label }]; tokens: [{ id, label, kind }] where
 * kind is 'saved' | 'add' | 'create'. Case-insensitive tests compare lower-cased strings; the label
 * tie-break compares upper-cased strings with plain < (never localeCompare): .NET's
 * StringComparer.OrdinalIgnoreCase orders by the upper-cased code units.
 */
function cbMatch(query, options, tokens, maxShown, createMax, allowCreate) {
  const q = (query ?? '').trim();
  if (q.length === 0) return { shown: [], createLabel: null, message: null, status: '' };
  const ql = q.toLowerCase();
  const taken = new Set(tokens.filter((t) => t.kind !== 'create').map((t) => t.id));
  const rank = (label) => {
    const l = label.toLowerCase();
    return l === ql ? 0 : l.startsWith(ql) ? 1 : 2;
  };

  const shown = options
    .filter((o) => o.label.toLowerCase().includes(ql) && !taken.has(o.id))
    .map((o) => ({ o, r: rank(o.label), k: o.label.toUpperCase() }))
    .sort((a, b) => a.r - b.r || cbOrdinal(a.k, b.k) || cbOrdinal(a.o.id, b.o.id))
    .slice(0, Math.max(0, maxShown))
    .map((x) => x.o);

  const exact = options.some((o) => o.label.toLowerCase() === ql)
    || tokens.some((t) => t.kind === 'create' && t.label.toLowerCase() === ql);

  let createLabel = null;
  let message = null;
  if (allowCreate && createMax !== null && q.length > createMax) {
    message = `Too long: at most ${createMax} characters.`;
  } else {
    createLabel = allowCreate && !exact ? q : null;
  }

  const n = shown.length + (createLabel === null ? 0 : 1);
  const status = n === 0 && exact ? 'Already added'
    : n === 0 ? 'No matches'
    : n === 1 ? '1 option'
    : `${n} options`;
  return { shown, createLabel, message, status };
}

function cbSettings(root) {
  const max = Number(root.dataset.maxShown);
  const createMax = root.dataset.createMax === '' || root.dataset.createMax === undefined
    ? null : Number(root.dataset.createMax);
  return {
    maxShown: Number.isFinite(max) ? max : 10,
    createMax: Number.isFinite(createMax) ? createMax : null,
    allowCreate: root.dataset.allowCreate === 'true',
  };
}

function cbTokens(root) {
  return cbTokenItems(root).map((li) => ({ id: li.dataset.id ?? '', label: cbTokenLabel(li), kind: li.dataset.kind ?? 'saved' }));
}

function cbSay(root, text) {
  const status = root.querySelector('.cb-status');
  if (status) status.textContent = text;
}

/** Highlights visible row i (or none when i < 0): aria-selected plus aria-activedescendant. */
function cbHighlight(root, i) {
  const input = cbInput(root);
  const visible = cbVisibleRows(root);
  for (const row of root.querySelectorAll('.cb-list > .cb-opt')) row.setAttribute('aria-selected', 'false');
  const row = i >= 0 && i < visible.length ? visible[i] : null;
  if (row) {
    row.setAttribute('aria-selected', 'true');
    input?.setAttribute('aria-activedescendant', row.id);
    if (typeof row.scrollIntoView === 'function') row.scrollIntoView({ block: 'nearest' });
  } else {
    input?.removeAttribute('aria-activedescendant');
  }
}

function cbHighlighted(root) {
  return cbVisibleRows(root).findIndex((r) => r.getAttribute('aria-selected') === 'true');
}

function cbClose(root) {
  const list = cbList(root);
  if (list) list.hidden = true;
  cbInput(root)?.setAttribute('aria-expanded', 'false');
  cbHighlight(root, -1);
}

/** Re-filters the rows for the input's text (K1: show/hide, re-order, the + row, message, status). */
function cbFilter(root) {
  const input = cbInput(root);
  const list = cbList(root);
  const newRow = cbNewRow(root);
  if (!input || !list || !newRow) return;
  const { maxShown, createMax, allowCreate } = cbSettings(root);
  const rows = cbOptionRows(root);
  const options = rows.map((row) => ({ id: row.dataset.id ?? '', label: row.dataset.label ?? '', row }));
  const m = cbMatch(input.value, options, cbTokens(root), maxShown, createMax, allowCreate);

  const shown = new Set(m.shown.map((o) => o.row));
  for (const row of rows) row.hidden = !shown.has(row);
  for (const o of m.shown) list.insertBefore(o.row, newRow);
  newRow.hidden = m.createLabel === null;
  newRow.textContent = m.createLabel === null ? '+ ' : `+ ${m.createLabel}`;

  const msg = root.querySelector('.cb-msg');
  if (msg) msg.textContent = m.message ?? '';

  const open = m.shown.length > 0 || m.createLabel !== null;
  list.hidden = !open;
  input.setAttribute('aria-expanded', open ? 'true' : 'false');
  cbHighlight(root, open ? 0 : -1);
  cbSay(root, m.status);
}

/** cb-empty shows only with no tokens; cb-warn lists each pending add's formatted warning. */
function cbRefresh(root) {
  const items = cbTokenItems(root);
  const empty = root.querySelector('.cb-empty');
  if (empty) empty.hidden = items.length > 0;
  const warn = root.querySelector('.cb-warn');
  if (!warn) return;
  const rows = cbOptionRows(root);
  const text = items
    .filter((li) => li.dataset.kind === 'add')
    .map((li) => rows.find((r) => r.dataset.id === li.dataset.id)?.dataset.warning)
    .filter((w) => w !== undefined && w !== null)
    .map((w) => cbFormat(root.dataset.warningFormat, w))
    .join(' ');
  warn.textContent = text;
  warn.hidden = text.length === 0;
}

/** A fresh token from the add or create template: span text and title, data-id, × name, input value. */
function cbFromTemplate(root, cls, id, label) {
  const tpl = root.querySelector(`template.${cls}`);
  const li = tpl?.content.firstElementChild?.cloneNode(true);
  if (!(li instanceof Element)) return null;
  li.setAttribute('data-id', id);
  const span = li.querySelector('span');
  if (span) {
    span.textContent = label;
    span.setAttribute('title', label);
  }
  const x = li.querySelector('.cb-x');
  if (x) {
    x.textContent = '×';
    x.setAttribute('aria-label', cbFormat(root.dataset.removeLabel, label));
  }
  const hidden = li.querySelector('input[type="hidden"]');
  if (hidden) hidden.value = id;
  return li;
}

/** The hidden RemoveName input holding <paramref name="id"/>, if the token was removed this visit. */
function cbRemoveInput(root, id) {
  const name = root.dataset.removeName ?? 'remove';
  return [...root.querySelectorAll(':scope > input[type="hidden"]')].find((i) => i.name === name && i.value === id) ?? null;
}

function cbPick(root, row) {
  const input = cbInput(root);
  const ul = root.querySelector('.cb-tokens');
  if (!input || !ul) return;
  let li;
  let said;
  if (row.classList.contains('cb-new')) {
    const q = input.value.trim();
    if (q.length === 0) return;
    li = cbFromTemplate(root, 'cb-tpl-create', q, q);
    said = `Added new ${q}`;
  } else {
    const id = row.dataset.id ?? '';
    const label = row.dataset.label ?? '';
    li = cbFromTemplate(root, 'cb-tpl-add', id, label);
    const removed = cbRemoveInput(root, id);
    if (li && removed) {
      // Re-picking a saved token removed this visit restores it: no leave, no add.
      removed.remove();
      li.querySelector('input[type="hidden"]')?.remove();
      li.setAttribute('data-kind', 'saved');
      li.classList.remove('is-pending');
    }
    said = `Added ${label}`;
  }
  if (!li) return;
  ul.append(li);
  input.value = '';
  cbClose(root);
  input.focus();
  cbRefresh(root);
  cbSay(root, said);
}

/** Removes a token: a saved one leaves a RemoveName input; focus to the next ×, else the input. */
function cbRemove(root, li, moveFocus) {
  const label = cbTokenLabel(li);
  if (li.dataset.kind === 'saved') {
    const tpl = root.querySelector('template.cb-tpl-remove');
    const hidden = tpl?.content.firstElementChild?.cloneNode(true);
    if (hidden instanceof HTMLInputElement) {
      hidden.value = li.dataset.id ?? '';
      root.append(hidden);
    }
  }
  const next = li.nextElementSibling;
  li.remove();
  cbRefresh(root);
  cbSay(root, `Removed ${label}`);
  if (moveFocus) {
    const x = next?.querySelector('.cb-x');
    if (x instanceof HTMLElement) x.focus();
    else cbInput(root)?.focus();
  }
}

document.addEventListener('input', (e) => {
  const el = e.target;
  if (!(el instanceof HTMLInputElement) || !el.classList.contains('cb-input')) return;
  const root = cbOf(el);
  if (root) cbFilter(root);
});

// Capture phase, so Esc's stopPropagation keeps a list-closing Esc from reaching any outer handler.
document.addEventListener('keydown', (e) => {
  const el = e.target;
  if (!(el instanceof HTMLInputElement) || !el.classList.contains('cb-input')) return;
  const root = cbOf(el);
  if (!root) return;
  const open = cbIsOpen(root);

  if (e.key === 'ArrowDown' || e.key === 'ArrowUp') {
    if (!open) {
      if (el.value.trim().length === 0) return;
      cbFilter(root);
      e.preventDefault();
      return;
    }
    e.preventDefault();
    const n = cbVisibleRows(root).length;
    if (n === 0) return;
    const at = cbHighlighted(root);
    const step = e.key === 'ArrowDown' ? 1 : -1;
    cbHighlight(root, at < 0 ? (step > 0 ? 0 : n - 1) : (at + step + n) % n);
    return;
  }
  if (e.key === 'Enter') {
    if (!open) return;
    e.preventDefault();
    const row = cbVisibleRows(root)[cbHighlighted(root)];
    if (row) cbPick(root, row);
    return;
  }
  if (e.key === 'Escape') {
    if (!open) return;
    e.preventDefault();
    e.stopPropagation();
    cbClose(root);
    return;
  }
  if (e.key === 'Backspace' && el.value.length === 0) {
    const items = cbTokenItems(root);
    const last = items[items.length - 1];
    if (last) {
      e.preventDefault();
      cbRemove(root, last, false);
    }
  }
}, true);

// A row is picked on mousedown, with the default prevented, so focus stays in the input (P5).
document.addEventListener('mousedown', (e) => {
  const row = e.target instanceof Element ? e.target.closest('.combobox .cb-list > .cb-opt') : null;
  if (!row || row.hidden) return;
  const root = cbOf(row);
  if (!root) return;
  e.preventDefault();
  cbPick(root, row);
});

document.addEventListener('click', (e) => {
  const x = e.target instanceof Element ? e.target.closest('.combobox .cb-tokens .cb-x') : null;
  if (!x) return;
  const root = cbOf(x);
  const li = x.closest('.cb-token');
  if (!root || !li) return;
  e.preventDefault();
  cbRemove(root, li, true);
});

// Leaving the field with text that was never picked: say so and keep the text (never saved).
document.addEventListener('focusout', (e) => {
  const el = e.target;
  if (!(el instanceof HTMLInputElement) || !el.classList.contains('cb-input')) return;
  const root = cbOf(el);
  if (!root) return;
  const to = e.relatedTarget;
  if (to instanceof Node && cbList(root)?.contains(to)) return;
  cbClose(root);
  if (el.value.trim().length > 0) cbSay(root, 'Not added');
});

// ── events ────────────────────────────────────────────────────────────────────

document.addEventListener('change', (e) => {
  const el = e.target;
  if (!(el instanceof HTMLInputElement)) return;

  // A gallery-list switch posts its own form at once (K1/K3: form="gal-vis-{gid}"; no confirm step).
  // The builder's switch is a field of its Save form (b-panel): changing it submits nothing, so typed
  // edits are kept; CSS shows the publish warning while it is on.
  if (el.type === 'checkbox' && el.getAttribute('role') === 'switch' && el.form && !el.disabled) {
    if (!el.form.id.startsWith('gal-vis-')) return;
    if (typeof el.form.requestSubmit === 'function') el.form.requestSubmit();
    else el.form.submit();
    return;
  }

  // Picker radio: keep newTitle's `required` in step with the picked option.
  if (el.type === 'radio' && el.name === 'gallery') {
    const dialog = el.closest('dialog');
    if (dialog) syncNewTitle(dialog);
    return;
  }

  // A library tick: refuse it when it would pass the limit (UI-7).
  if (el.type === 'checkbox' && el.name === 'ids') {
    const root = libraryRoot(el);
    if (!root) return;
    const limit = limitOf(root);
    if (el.checked && ticked(root).length > limit) {
      el.checked = false;
      sayLimit(root, limitText(limit));
    } else {
      sayLimit(root, null);
    }
    refresh(root);
  }
});

document.addEventListener('click', (e) => {
  const button = e.target instanceof Element ? e.target.closest('button') : null;
  if (!button) return;

  // Cancel in any dialog closes it.
  const dialog = button.closest('dialog');
  if (dialog && button.name === 'action' && button.value === 'cancel') {
    e.preventDefault();
    dialog.close();
    return;
  }

  // A DangerConfirm trigger: open its modal (invoker commands do this natively; remember the opener).
  if (button.matches('.danger-confirm > .danger-trigger')) {
    const target = document.getElementById(button.getAttribute('commandfor') ?? '');
    if (target instanceof HTMLDialogElement) {
      if (invokers) openers.set(target, button);
      else { e.preventDefault(); openDialog(target, button); }
    }
    return;
  }

  // "New gallery" in the page heading row (C2; 010 owner review 1).
  if (button.name === 'action' && button.value === 'new-gallery' && button.closest('.page-heading')) {
    e.preventDefault();
    openDialog(document.getElementById('gal-new'));
    return;
  }

  const b = button.closest('.selection-bar');
  const root = b ? libraryRoot(b) : null;
  if (!root) return;

  if (button.name === 'action' && button.value === 'add') {
    e.preventDefault();
    closeBulkDelete();
    openDialog(document.getElementById('lib-picker'));
    return;
  }
  if (button.name === 'action' && button.value === 'delete') {
    e.preventDefault();
    if (ticked(root).length > 0) showBulkDelete(root, button);
    return;
  }

  const label = button.textContent.trim();
  if (label === 'Clear') {
    e.preventDefault();
    for (const box of ids(root)) box.checked = false;
    sayLimit(root, null);
    closeBulkDelete();
    refresh(root);
    return;
  }
  if (label === 'Select all on this page') {
    e.preventDefault();
    const limit = limitOf(root);
    let n = ticked(root).length;
    let refused = false;
    for (const box of ids(root)) {
      if (box.checked) continue;
      if (n >= limit) { refused = true; break; }
      box.checked = true;
      n++;
    }
    sayLimit(root, refused ? limitText(limit) : null);
    refresh(root);
  }
});

// The bulk delete submits lib-delete through a form="lib-delete" button: copy the ids once more.
document.addEventListener('submit', (e) => {
  const form = e.target;
  if (form instanceof HTMLFormElement && form.id === 'lib-delete') {
    const root = libraryRoot(null);
    if (root) copyIdsToDelete(root);
  }
}, true);

// Boxes ticked before the script ran (back/forward cache, form restore) are counted on load, and
// the bar is marked as driven by this script (data-script="ready").
for (const root of document.querySelectorAll('section.lib')) {
  const b = bar(root);
  if (b) b.dataset.script = 'ready';
  refresh(root);
}

// ── panel into view (015 R-03) ────────────────────────────────────────────────

// On `load`, not DOMContentLoaded: the #t-{id} fragment scroll lands until load and would undo it.
addEventListener('load', () => {
  const panel = document.getElementById('lib-edit');
  if (!panel || !matchMedia('(max-width: 1199.98px)').matches) return;
  const field = panel.querySelector('.combobox') ?? panel;
  if (field.getBoundingClientRect().top < innerHeight) return;
  field.scrollIntoView({ block: 'start' });
});
