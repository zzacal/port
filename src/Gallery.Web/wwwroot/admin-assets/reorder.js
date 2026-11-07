// 009 C4 reorder script (contracts/components.md C4): <reorder-grid data-form="…"> around a
// ContactSheet whose direct children are the tiles, in the same order as the `order` inputs of
// the hidden form named by data-form (G3 gal-order, B3 b-order).
//
//   - Pointer: press a tile's .handle and drag. A copy of the tile (.drag-preview) follows the
//     pointer; the tile itself is hidden (is-dragging) and a .drop-slot of its size holds its cell,
//     then moves to the landing index, so the grid keeps one cell per tile. Releasing over a new
//     place rewrites the form's `order` inputs and submits it.
//   - Keyboard: Space/Enter on a focused handle lifts the tile (aria-pressed="true", the lift help
//     text), arrows move it one place, Space/Enter drops it (rewrite + submit), Escape restores the
//     original order and submits nothing. Each move updates a polite live region "Position p of n".
//
// Everything is resolved at use time: the form (it may be declared after the grid) and the tiles.
// Nothing is cached in connectedCallback. Markup is built with createElement/textContent only.

const ARROWS = { ArrowRight: 1, ArrowDown: 1, ArrowLeft: -1, ArrowUp: -1 };

class ReorderGrid extends HTMLElement {
  connectedCallback() {
    if (this._wired) return;
    this._wired = true;
    this._drag = null; // pointer drag state
    this._lift = null; // keyboard lift state
    this.addEventListener('pointerdown', (e) => this.onPointerDown(e));
    this.addEventListener('keydown', (e) => this.onKeyDown(e));
    this.addEventListener('focusout', (e) => this.onFocusOut(e));
  }

  // ── lookups (use time) ──────────────────────────────────────────────────────

  /** The hidden order form, resolved now (it may be declared after this element). */
  orderForm() {
    const id = this.dataset.form;
    return id ? document.getElementById(id) : null;
  }

  /** The grid element: the ContactSheet inside this element. */
  grid() {
    return this.querySelector('.cs');
  }

  /** The tiles, in DOM order (every direct child of the grid except the drop slot). */
  tiles() {
    const grid = this.grid();
    return grid ? [...grid.children].filter((el) => !el.classList.contains('drop-slot')) : [];
  }

  /** The ids of the order inputs, in form order. */
  orderIds(form) {
    return [...form.querySelectorAll('input[name="order"]')].map((i) => i.value);
  }

  /** The handle under an event target, if it belongs to one of this grid's tiles. */
  handleOf(target) {
    const handle = target instanceof Element ? target.closest('.handle') : null;
    return handle && this.contains(handle) && this.tileOf(handle) ? handle : null;
  }

  tileOf(el) {
    const grid = this.grid();
    let node = el;
    while (node && node.parentElement !== grid) node = node.parentElement;
    return node && !node.classList.contains('drop-slot') ? node : null;
  }

  /** True when the form has exactly one order input per tile, so index i maps tile i to id i. */
  ready(form) {
    return form && this.orderIds(form).length === this.tiles().length && this.tiles().length > 1;
  }

  // ── submit ──────────────────────────────────────────────────────────────────

  /** Moves the id at `from` to `to`, rewrites the form's order inputs in that order, and submits. */
  submitMove(from, to) {
    const form = this.orderForm();
    if (!form || from === to) return;
    const ids = this.orderIds(form);
    const [moved] = ids.splice(from, 1);
    ids.splice(to, 0, moved);
    const inputs = [...form.querySelectorAll('input[name="order"]')];
    inputs.forEach((input, i) => { input.value = ids[i]; });
    this.dataset.submitting = 'true';
    if (typeof form.requestSubmit === 'function') form.requestSubmit();
    else form.submit();
  }

  // ── pointer drag ────────────────────────────────────────────────────────────

  onPointerDown(e) {
    if (e.button !== 0 || this._drag || this._lift || this.dataset.submitting) return;
    const handle = this.handleOf(e.target);
    const form = this.orderForm();
    if (!handle || !this.ready(form)) return;
    const tiles = this.tiles();
    const tile = this.tileOf(handle);
    const from = tiles.indexOf(tile);
    e.preventDefault();

    // Hit-test against the cell positions as they were when the drag started: moving the slot
    // reflows the grid, and live positions would make the landing index oscillate.
    const rects = tiles.map((t) => t.getBoundingClientRect());
    const r = rects[from];
    // The slot takes the dragged tile's place in the grid (same size), and the tile itself is hidden,
    // so the grid keeps exactly one cell per tile: nothing shifts until the landing place changes.
    const slot = document.createElement('div');
    slot.className = 'drop-slot';
    slot.setAttribute('aria-hidden', 'true');
    // The .drop-slot rule is scoped to the component that renders the grid's wrapper (the
    // GalleryBuilder's section, or the page that declares this element); AdminLayout styles both.
    copyScope(this, slot);
    copyScope(this.parentElement, slot);
    slot.style.height = `${r.height}px`;
    // A copy of the tile follows the pointer (fixed, moved by transform; inert and hidden from AT).
    const preview = tile.cloneNode(true);
    preview.classList.add('drag-preview');
    preview.removeAttribute('id');
    for (const n of preview.querySelectorAll('[id]')) n.removeAttribute('id');
    preview.setAttribute('aria-hidden', 'true');
    preview.inert = true;
    preview.style.left = `${r.left}px`;
    preview.style.top = `${r.top}px`;
    preview.style.width = `${r.width}px`;
    tile.before(slot);
    tile.classList.add('is-dragging');
    this.append(preview);

    const x0 = e.clientX;
    const y0 = e.clientY;
    const drag = { handle, tile, from, to: from, rects, slot, preview, pointerId: e.pointerId };
    this._drag = drag;
    try { handle.setPointerCapture(e.pointerId); } catch { /* capture is best effort */ }

    const move = (ev) => {
      if (ev.pointerId !== drag.pointerId) return;
      preview.style.transform = `translate(${ev.clientX - x0}px, ${ev.clientY - y0}px)`;
      const to = landingIndex(rects, ev.clientX, ev.clientY);
      if (to !== null && to !== drag.to) {
        drag.to = to;
        this.placeSlot(drag);
      }
    };
    const end = (ev) => {
      if (ev.pointerId !== drag.pointerId) return;
      handle.removeEventListener('pointermove', move);
      handle.removeEventListener('pointerup', end);
      handle.removeEventListener('pointercancel', end);
      try { handle.releasePointerCapture(drag.pointerId); } catch { /* already released */ }
      preview.remove();
      const drop = ev.type === 'pointerup' && drag.to !== drag.from;
      // On a drop the tile shows where it landed while the form submits; otherwise where it was.
      if (drop) slot.replaceWith(tile); else slot.remove();
      tile.classList.remove('is-dragging');
      this._drag = null;
      if (drop) this.submitMove(drag.from, drag.to);
    };
    handle.addEventListener('pointermove', move);
    handle.addEventListener('pointerup', end);
    handle.addEventListener('pointercancel', end);
  }

  /** Puts the slot where the tile would land: before the target cell when moving back, after it when moving forward, in the tile's own place otherwise. */
  placeSlot(drag) {
    const tiles = this.tiles();
    if (drag.to === drag.from) { drag.tile.before(drag.slot); return; }
    const target = tiles[drag.to];
    if (drag.to < drag.from) target.before(drag.slot);
    else target.after(drag.slot);
  }

  // ── keyboard ────────────────────────────────────────────────────────────────

  onKeyDown(e) {
    const handle = this.handleOf(e.target);
    if (!handle || this.dataset.submitting) return;
    const key = e.key === ' ' ? 'Space' : e.key;

    if (!this._lift) {
      if (key !== 'Space' && key !== 'Enter') return;
      const form = this.orderForm();
      if (!this.ready(form)) return;
      e.preventDefault();
      this.lift(handle);
      return;
    }

    if (this._lift.handle !== handle) return;
    if (key === 'Space' || key === 'Enter') {
      e.preventDefault();
      const { from, pos } = this._lift;
      this.unlift();
      if (pos !== from) this.submitMove(from, pos);
      return;
    }
    if (key === 'Escape') {
      e.preventDefault();
      this.cancelLift();
      return;
    }
    if (key in ARROWS) {
      e.preventDefault();
      this.moveLifted(ARROWS[key]);
    }
  }

  lift(handle) {
    const tiles = this.tiles();
    const tile = this.tileOf(handle);
    const from = tiles.indexOf(tile);
    const live = document.createElement('p');
    live.className = 'lift-help';
    live.setAttribute('role', 'status');
    live.setAttribute('aria-live', 'polite');
    live.id = `lift-help-${Math.random().toString(36).slice(2, 10)}`;
    copyScope(handle, live);
    handle.after(live);
    this._lift = { handle, tile, from, pos: from, live, original: tiles.slice() };
    handle.setAttribute('aria-pressed', 'true');
    handle.setAttribute('aria-describedby', live.id);
    handle.classList.add('is-lifted');
    tile.classList.add('is-lifted');
    live.textContent = `Lifted. Position ${from + 1} of ${tiles.length}. Arrow keys move it, Space drops it, Escape cancels.`;
  }

  moveLifted(step) {
    const lift = this._lift;
    const tiles = this.tiles();
    const to = lift.pos + step;
    if (to < 0 || to >= tiles.length) return;
    const neighbour = tiles[to];
    this.withFocusKept(lift.handle, () => {
      if (step > 0) neighbour.after(lift.tile);
      else neighbour.before(lift.tile);
    });
    lift.pos = to;
    lift.live.textContent = `Position ${to + 1} of ${tiles.length}`;
  }

  /** Escape: the tiles go back where they were and nothing is submitted. */
  cancelLift() {
    const lift = this._lift;
    const grid = this.grid();
    this.withFocusKept(lift.handle, () => {
      for (const tile of lift.original) grid.append(tile);
    });
    this.unlift();
  }

  unlift() {
    const lift = this._lift;
    if (!lift) return;
    this._lift = null;
    lift.handle.setAttribute('aria-pressed', 'false');
    lift.handle.removeAttribute('aria-describedby');
    lift.handle.classList.remove('is-lifted');
    lift.tile.classList.remove('is-lifted');
    lift.live.remove();
  }

  /** Moving a node that holds focus blurs it; keep focus on the handle and ignore that blur. */
  withFocusKept(handle, fn) {
    this._moving = true;
    try { fn(); } finally { this._moving = false; }
    handle.focus();
  }

  onFocusOut(e) {
    if (this._moving || !this._lift || e.target !== this._lift.handle) return;
    // Focus left the lifted handle (Tab, a click elsewhere): treat it as Escape.
    if (e.relatedTarget && e.relatedTarget === this._lift.handle) return;
    this.cancelLift();
  }
}

/** The index of the start-of-drag cell nearest the point (null when there are no cells). */
function landingIndex(rects, x, y) {
  let best = null;
  let bestDist = Infinity;
  rects.forEach((r, i) => {
    if (x >= r.left && x <= r.right && y >= r.top && y <= r.bottom) {
      best = i;
      bestDist = -1;
      return;
    }
    if (bestDist < 0) return;
    const cx = r.left + r.width / 2;
    const cy = r.top + r.height / 2;
    const d = (cx - x) ** 2 + (cy - y) ** 2;
    if (d < bestDist) { bestDist = d; best = i; }
  });
  return best;
}

/** Copies the Razor CSS-isolation attributes (b-xxxx) so a created element gets its component's scoped rules. */
function copyScope(from, to) {
  if (!from) return;
  for (const attr of from.attributes) if (/^b-[a-z0-9]+$/i.test(attr.name)) to.setAttribute(attr.name, '');
}

if (!customElements.get('reorder-grid')) customElements.define('reorder-grid', ReorderGrid);
