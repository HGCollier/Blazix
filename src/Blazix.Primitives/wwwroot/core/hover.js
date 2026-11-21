// @ts-check
//
// Opening a popup by resting the pointer on its trigger (after UI SDK's popups/hover and
// popups/safe-area): a delay before it opens, a delay before it closes, and a safe area spanning
// the trigger and the popup, so the pointer can cross from one to the other without closing it.

import { on } from './dom.js';

/** @typedef {{ x: number, y: number }} Point */

/** @param {Point} p @param {Point} a @param {Point} b @param {Point} c */
function inTriangle(p, a, b, c) {
  const sign = (/** @type {Point} */ p1, /** @type {Point} */ p2, /** @type {Point} */ p3) => (p1.x - p3.x) * (p2.y - p3.y) - (p2.x - p3.x) * (p1.y - p3.y);
  const d1 = sign(p, a, b);
  const d2 = sign(p, b, c);
  const d3 = sign(p, c, a);
  const negative = d1 < 0 || d2 < 0 || d3 < 0;
  const positive = d1 > 0 || d2 > 0 || d3 > 0;
  return !(negative && positive);
}

/** @param {DOMRect} r @param {Point} p @param {number} [pad] */
const inRect = (r, p, pad = 0) => p.x >= r.left - pad && p.x <= r.right + pad && p.y >= r.top - pad && p.y <= r.bottom + pad;

/**
 * The area the pointer may cross between trigger and popup: the gap between them, plus the
 * triangle from where the pointer left the trigger to the popup's near edge.
 * @param {Point} exit where the pointer left the trigger
 * @param {DOMRect} trigger
 * @param {DOMRect} popup
 */
export function bridgeBetween(exit, trigger, popup) {
  const beside = popup.left >= trigger.right - 1 || popup.right <= trigger.left + 1;
  return (/** @type {Point} */ p) => {
    if (inRect(popup, p) || inRect(trigger, p)) return true;
    if (beside) {
      const near = popup.left >= trigger.right - 1 ? popup.left : popup.right;
      return inTriangle(p, exit, { x: near, y: popup.top - 8 }, { x: near, y: popup.bottom + 8 })
        || (p.y >= Math.max(trigger.top, popup.top) && p.y <= Math.min(trigger.bottom, popup.bottom)
          && p.x >= Math.min(trigger.right, popup.right) && p.x <= Math.max(trigger.left, popup.left));
    }
    const near = popup.top >= trigger.bottom - 1 ? popup.top : popup.bottom;
    return inTriangle(p, exit, { x: popup.left - 8, y: near }, { x: popup.right + 8, y: near })
      || (p.x >= Math.max(trigger.left, popup.left) && p.x <= Math.min(trigger.right, popup.right)
        && p.y >= Math.min(trigger.bottom, popup.bottom) && p.y <= Math.max(trigger.top, popup.top));
  };
}

/**
 * @param {{
 *   trigger: () => HTMLElement | null,
 *   popup: () => HTMLElement | null,
 *   isOpen: () => boolean,
 *   openDelay: () => number,
 *   closeDelay: () => number,
 *   open: (event: PointerEvent) => void,
 *   close: (event: PointerEvent) => void,
 * }} options
 */
export function createHoverIntent(options) {
  /** @type {ReturnType<typeof setTimeout> | undefined} */
  let openTimer;
  /** @type {ReturnType<typeof setTimeout> | undefined} */
  let closeTimer;
  /** @type {(() => void) | undefined} */
  let stopBridge;

  const stopWatching = () => {
    stopBridge?.();
    stopBridge = undefined;
  };

  function clear() {
    clearTimeout(openTimer);
    clearTimeout(closeTimer);
    stopWatching();
  }

  /** @param {PointerEvent} event */
  function scheduleClose(event) {
    stopWatching();
    clearTimeout(closeTimer);
    const delay = options.closeDelay();
    if (delay <= 0) options.close(event);
    else closeTimer = setTimeout(() => options.close(event), delay);
  }

  return {
    clear,
    /** The pointer entered the trigger: open after the delay. Touch has no hover. */
    enterTrigger(/** @type {PointerEvent} */ event) {
      if (event.pointerType === 'touch') return;
      clearTimeout(closeTimer);
      stopWatching();
      if (options.isOpen()) return;
      clearTimeout(openTimer);
      const delay = options.openDelay();
      if (delay <= 0) options.open(event);
      else openTimer = setTimeout(() => options.open(event), delay);
    },
    /** The pointer entered the popup: keep it open. */
    enterPopup(/** @type {PointerEvent} */ event) {
      if (event.pointerType === 'touch') return;
      clearTimeout(closeTimer);
      stopWatching();
    },
    /** The pointer left the trigger or the popup. On its way between the two, the popup stays open. */
    leave(/** @type {PointerEvent} */ event) {
      if (event.pointerType === 'touch') return;
      clearTimeout(openTimer);
      if (!options.isOpen()) return;
      const trigger = options.trigger();
      const popup = options.popup();
      if (!trigger || !popup) {
        scheduleClose(event);
        return;
      }
      // The apex is where the pointer crossed the trigger's edge. Pointer events report where the
      // pointer is now, which after a fast move (or a jump) can be far away, so clamp it to the trigger.
      const rect = trigger.getBoundingClientRect();
      const exit = {
        x: Math.min(Math.max(event.clientX, rect.left), rect.right),
        y: Math.min(Math.max(event.clientY, rect.top), rect.bottom),
      };
      const inside = bridgeBetween(exit, rect, popup.getBoundingClientRect());
      const related = /** @type {Node | null} */ (event.relatedTarget);
      if (related && (trigger.contains(related) || popup.contains(related))) return;
      if (!inside({ x: event.clientX, y: event.clientY })) {
        scheduleClose(event);
        return;
      }
      stopWatching();
      stopBridge = on(document, 'pointermove', (/** @type {PointerEvent} */ move) => {
        if (!inside({ x: move.clientX, y: move.clientY })) scheduleClose(move);
      });
    },
  };
}

/**
 * Lets the pointer cross other items on its way to an open submenu without switching to them:
 * moves inside the triangle from where it left the trigger to the popup's near edge are ignored
 * until the pointer rests.
 */
export function createSafeArea(restTimeout = 300) {
  /** @type {Point | undefined} */
  let apex;
  /** @type {HTMLElement | undefined} */
  let popup;
  /** @type {Point | undefined} */
  let last;
  /** @type {ReturnType<typeof setTimeout> | undefined} */
  let restTimer;
  let expired = false;
  return {
    /** @param {Point} point @param {HTMLElement} target */
    start(point, target) {
      apex = point;
      last = point;
      popup = target;
      expired = false;
    },
    stop() {
      apex = popup = last = undefined;
      clearTimeout(restTimer);
    },
    /** True when the move should be ignored. `onRest` runs if the pointer stops inside the area. */
    contains(/** @type {Point} */ point, /** @type {() => void} */ onRest) {
      if (!apex || !popup || expired || !popup.isConnected) return false;
      const rect = popup.getBoundingClientRect();
      const near = rect.left >= apex.x ? rect.left : rect.right;
      const inside = inTriangle(point, apex, { x: near, y: rect.top - 8 }, { x: near, y: rect.bottom + 8 });
      const progressing = !last || Math.abs(near - point.x) <= Math.abs(near - last.x) + 1;
      last = point;
      clearTimeout(restTimer);
      if (!inside || !progressing) return false;
      restTimer = setTimeout(() => {
        expired = true;
        onRest();
      }, restTimeout);
      return true;
    },
  };
}
