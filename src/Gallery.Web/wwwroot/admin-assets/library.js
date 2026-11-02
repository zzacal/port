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
//
// Elements are resolved when an event happens (event delegation on the document), never cached at
// load, so markup declared after the script, or replaced later, is always found. Text is set with
// textContent and elements are built with createElement only.

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
