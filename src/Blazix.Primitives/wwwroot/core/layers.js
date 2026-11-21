// @ts-check
//
// The dismissal layer stack (after UI SDK's platform/layers). Every open popup is a layer, and
// layers nest: a submenu's layer has its menu's layer as parent, and a popover opened from inside
// a dialog has the dialog's. Escape goes to the topmost layer only; an outside press closes every
// layer it's outside of, from the top down, until one contains it. So a select inside a dialog
// closes on Escape without closing the dialog, and pressing inside a child popup never dismisses
// its parent, wherever the child renders.

import { contains, isComposing, on } from './dom.js';

/**
 * @typedef {object} Layer
 * @property {(target: Node) => boolean} contains Elements that count as inside (popup, trigger).
 * @property {Layer} [parent]
 * @property {() => boolean} active False once it starts to close.
 * @property {() => boolean} [exiting] True while it animates out (inert, so presses fall through).
 * @property {(x: number, y: number) => boolean} [overBackdrop]
 * @property {boolean} modal A modal layer consumes the outside press: it only closes the layer.
 * @property {'pointerdown' | 'click'} [outsidePress] When an outside press dismisses.
 * @property {boolean} [shield] Stops an outside press here, so layers beneath stay open.
 * @property {(event: KeyboardEvent) => void} [onEscape]
 * @property {(event: Event) => void} [onOutsidePress]
 * @property {(event: FocusEvent) => void} [onBlur]
 * @property {number} [order]
 */

const realm = /** @type {{ stack: Layer[], pushed: number, persistent: Set<Element>, stop: (() => void) | null }} */ (
  /** @type {any} */ (globalThis)[Symbol.for('blazix.primitives.layers')] ??= {
    stack: [],
    pushed: 0,
    persistent: new Set(),
    stop: null,
  });

const active = () => realm.stack.filter((layer) => layer.active());

/** Whether `target` is inside `layer` or any open layer nested under it. */
function insideTree(/** @type {Layer} */ layer, /** @type {Node} */ target) {
  if (layer.contains(target)) return true;
  return active().some((other) => other.parent === layer && insideTree(other, target));
}

function inPersistentLayer(/** @type {Node} */ target) {
  for (const el of realm.persistent) if (contains(el, target)) return true;
  return false;
}

// The page's own scrollbar sits outside the root element's client area. Pressing it never dismisses.
function pressedScrollbar(/** @type {PointerEvent} */ event, /** @type {Node} */ target) {
  if (!(target instanceof Element)) return false;
  if (target === document.documentElement) {
    return event.clientX >= target.clientWidth || event.clientY >= target.clientHeight;
  }
  const rect = target.getBoundingClientRect();
  const x = event.clientX - rect.left;
  const y = event.clientY - rect.top;
  const el = /** @type {HTMLElement} */ (target);
  const scrollsX = el.scrollWidth > el.clientWidth;
  const scrollsY = el.scrollHeight > el.clientHeight;
  return (scrollsY && x >= el.clientLeft + el.clientWidth && x < rect.width)
    || (scrollsX && y >= el.clientTop + el.clientHeight && y < rect.height);
}

function listen() {
  let swallowClick = false;
  /** @type {{ x: number, y: number, target: Node } | undefined} */
  let touch;
  /** @type {{ layer: Layer, target: Node, point: { x: number, y: number } } | undefined} */
  let pending;

  /** @param {Node} target @param {{ x: number, y: number }} point */
  function plan(target, point) {
    /** @type {Layer[]} */
    const dismissed = [];
    let consume = false;
    if (inPersistentLayer(target)) return { dismissed, consume };
    for (const layer of [...realm.stack].reverse()) {
      if (layer.active()) {
        if (insideTree(layer, target)) break;
        if (layer.modal) consume = true;
        dismissed.push(layer);
        if (layer.shield) break;
      } else if (layer.exiting?.()) {
        if (insideTree(layer, target)) break;
        if (!layer.modal) continue;
        if (layer.overBackdrop?.(point.x, point.y)) consume = true;
        break;
      }
    }
    return { dismissed, consume };
  }

  /** @param {Event} event @param {Node} target @param {{ x: number, y: number }} point */
  function dismiss(event, target, point) {
    const { dismissed, consume } = plan(target, point);
    for (const layer of dismissed) layer.onOutsidePress?.(event);
    return consume;
  }

  const offPointerDown = on(window, 'pointerdown', (/** @type {PointerEvent} */ event) => {
    swallowClick = false;
    const target = /** @type {Node} */ (event.composedPath()[0]);
    if (event.pointerType === 'touch') {
      // Touch waits for the click, so a scroll gesture that starts outside never dismisses.
      touch = { x: event.clientX, y: event.clientY, target };
      return;
    }
    touch = undefined;
    pending = undefined;
    if (pressedScrollbar(event, target)) return;
    const point = { x: event.clientX, y: event.clientY };
    const { dismissed, consume } = plan(target, point);
    if (dismissed[0]?.outsidePress === 'click') {
      // Like native light dismiss: only once the press is also released outside, so a text
      // selection dragged out of a field doesn't close a dialog.
      if (event.button !== 0) return;
      pending = { layer: dismissed[0], target, point };
      if (consume) {
        event.preventDefault();
        swallowClick = true;
      }
      return;
    }
    if (consume) {
      // Prevents the mousedown that would move focus, and the click.
      event.preventDefault();
      swallowClick = true;
    }
    for (const layer of dismissed) layer.onOutsidePress?.(event);
  }, { capture: true });

  const offPointerUp = on(window, 'pointerup', (/** @type {PointerEvent} */ event) => {
    const press = pending;
    pending = undefined;
    if (!press || !press.layer.active()) return;
    if (insideTree(press.layer, /** @type {Node} */ (event.composedPath()[0]))) return;
    dismiss(event, press.target, press.point);
  }, { capture: true });

  const offClick = on(window, 'click', (/** @type {MouseEvent} */ event) => {
    if (swallowClick) {
      swallowClick = false;
      event.preventDefault();
      event.stopPropagation();
      return;
    }
    if (!touch) return;
    const press = touch;
    touch = undefined;
    if (Math.hypot(event.clientX - press.x, event.clientY - press.y) > 10) return;
    if (dismiss(event, press.target, { x: press.x, y: press.y })) {
      event.preventDefault();
      event.stopPropagation();
    }
  }, { capture: true });

  const offKey = on(document, 'keydown', (/** @type {KeyboardEvent} */ event) => {
    if (event.key !== 'Escape' || event.defaultPrevented || isComposing(event)) return;
    const top = active().at(-1);
    if (top?.onEscape) {
      // Don't also close a native <dialog> or anything else listening further up.
      event.preventDefault();
      top.onEscape(event);
    }
  });

  // A click into an iframe or another window never reaches the page. Each layer decides.
  const offBlur = on(window, 'blur', (/** @type {FocusEvent} */ event) => {
    for (const layer of active().reverse()) layer.onBlur?.(event);
  });

  return () => {
    offPointerDown();
    offPointerUp();
    offKey();
    offBlur();
    if (!swallowClick) {
      offClick();
      return;
    }
    // The press that closed the last layer still owes its click; swallow it when it comes.
    const offNext = on(window, 'pointerdown', () => off(), { capture: true });
    const offSwallowed = on(window, 'click', () => off(), { capture: true });
    function off() {
      offClick();
      offNext();
      offSwallowed();
    }
  };
}

/**
 * Adds a layer and returns the function that removes it.
 * @param {Layer} layer
 */
export function pushLayer(layer) {
  realm.pushed += 1;
  layer.order = realm.pushed;
  realm.stack.push(layer);
  realm.stop ??= listen();
  let popped = false;
  return () => {
    if (popped) return;
    popped = true;
    const index = realm.stack.indexOf(layer);
    if (index !== -1) realm.stack.splice(index, 1);
    if (realm.stack.length === 0) {
      realm.stop?.();
      realm.stop = null;
    }
  };
}

/** The topmost open layer that contains a node: a popup opened from inside it nests under it. */
export function findLayer(/** @type {Node | null | undefined} */ target) {
  if (!target) return undefined;
  return active().reverse().find((layer) => layer.contains(target));
}

/** Whether `layer` or one nested under it contains `target`. */
export function layerContains(/** @type {Layer} */ layer, /** @type {Node} */ target) {
  return insideTree(layer, target);
}

/** Whether a layer opened after `order` is still open (e.g. a dialog opened from a menu item). */
export function openedSince(/** @type {number} */ order) {
  return active().some((layer) => (layer.order ?? 0) > order);
}

/** The open layers, oldest first. */
export function activeLayers() {
  return active();
}

/**
 * Keeps an element live over every layer, like a toast region over a modal dialog: modal layers
 * don't make it inert, presses in it never dismiss a layer, and it's shown again whenever another
 * popover opens so it stays on top. Returns the function that undoes it.
 * @param {HTMLElement} el
 */
export function bindPersistentLayer(el) {
  realm.persistent.add(el);
  /** @param {Event} event */
  const onToggle = (event) => {
    const opened = event.target;
    if (/** @type {ToggleEvent} */ (event).newState !== 'open' || !(opened instanceof Element)) return;
    if (inPersistentLayer(opened) || !el.isConnected || !el.matches(':popover-open')) return;
    const focused = /** @type {HTMLElement | null} */ (document.activeElement);
    try {
      el.hidePopover();
      el.showPopover();
    } catch { /* not a popover */ }
    if (focused && focused !== document.activeElement) focused.focus({ preventScroll: true });
  };
  // Toggle events don't bubble, so the document hears them while capturing.
  document.addEventListener('toggle', onToggle, true);
  return () => {
    realm.persistent.delete(el);
    document.removeEventListener('toggle', onToggle, true);
  };
}

/** The persistent layers (toast regions), which modal layers keep live. */
export function persistentLayers() {
  return [...realm.persistent].filter((el) => el.isConnected);
}
