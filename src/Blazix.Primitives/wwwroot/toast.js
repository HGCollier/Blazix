// @ts-check
//
// The browser side of Toast. The viewport renders in the top layer as a persistent layer (it stays
// above, and live over, modal dialogs). This measures the toasts for stacking (--toast-height,
// --toast-offset-y, --toast-frontmost-height), runs each toast's timer and pauses them while the
// region is hovered or focused or the window is in the background, handles swipe to dismiss, and
// waits for a closing toast's exit animation before Blazor removes it. F6 focuses the region.
// Anchored toasts (in a Toast.Positioner) are placed against their anchor like any popup.
// --toast-swipe-movement-x/y are 0px at rest. Blazor rewrites a toast's style attribute when it
// renders a new one (--toast-index), so the variables set here are put back right after.

import { byId, call, on } from './core/dom.js';
import { bindPersistentLayer } from './core/layers.js';
import { position } from './core/position.js';
import { animateIn, whenAnimationsFinish } from './core/presence.js';

const HOST_STYLE = 'position:fixed;inset:0 auto auto 0;width:0;height:0;margin:0;padding:0;border:0;background:transparent;overflow:visible;color:inherit;';
const SWIPE_THRESHOLD = 40;

/** @typedef {{ swipeDirections: string[] }} ToastOptions */

/**
 * @param {import('./core/dom.js').DotNetRef} dotnet
 * @param {string} hostId
 * @param {ToastOptions} initial
 */
export function createToasts(dotnet, hostId, initial) {
  let options = initial;
  const host = byId(hostId);
  if (!host) return { dispose() {} };
  host.style.cssText = HOST_STYLE;
  try { host.showPopover(); } catch { /* not supported */ }
  const releaseLayer = bindPersistentLayer(host);

  const viewport = () => /** @type {HTMLElement | null} */ (host.querySelector('[data-blazix-toast-viewport]'));
  const toasts = () => /** @type {HTMLElement[]} */ ([...host.querySelectorAll('[data-blazix-toast]')]);

  /** @type {Map<string, { remaining: number, started: number, timer: ReturnType<typeof setTimeout> | undefined }>} */
  const timers = new Map();
  /** @type {Set<string>} */
  const known = new Set();
  /** @type {Map<string, number>} */
  const timeoutOf = new Map();
  /** @type {Map<HTMLElement, ReturnType<typeof position>>} */
  const positioned = new Map();
  /** @type {WeakMap<HTMLElement, Map<string, string>>} */
  const varsOf = new WeakMap();
  const setVar = (/** @type {HTMLElement} */ el, /** @type {string} */ name, /** @type {string} */ value) => {
    let vars = varsOf.get(el);
    if (!vars) varsOf.set(el, (vars = new Map()));
    vars.set(name, value);
    el.style.setProperty(name, value);
  };
  const restoreVars = (/** @type {HTMLElement} */ el) => {
    for (const [name, value] of varsOf.get(el) ?? []) {
      if (el.style.getPropertyValue(name) !== value) el.style.setProperty(name, value);
    }
  };
  let expanded = false;
  let windowBlurred = false;
  const paused = () => expanded || windowBlurred;

  function startTimer(/** @type {string} */ id) {
    const entry = timers.get(id);
    if (!entry || entry.remaining <= 0 || entry.timer) return;
    entry.started = Date.now();
    entry.timer = setTimeout(() => {
      timers.delete(id);
      call(dotnet, 'Close', id);
    }, entry.remaining);
  }

  function pauseTimers() {
    for (const entry of timers.values()) {
      if (!entry.timer) continue;
      clearTimeout(entry.timer);
      entry.timer = undefined;
      entry.remaining = Math.max(0, entry.remaining - (Date.now() - entry.started));
    }
  }

  function resumeTimers() {
    if (paused()) return;
    for (const id of timers.keys()) startTimer(id);
  }

  function setExpanded(/** @type {boolean} */ value) {
    if (expanded === value) return;
    expanded = value;
    viewport()?.toggleAttribute('data-expanded', value);
    for (const toast of toasts()) {
      toast.toggleAttribute('data-expanded', value);
      for (const content of toast.querySelectorAll('[data-blazix-toast-content]')) content.toggleAttribute('data-expanded', value);
    }
    if (value) pauseTimers();
    else resumeTimers();
  }

  /** Starts placing new positioners against their anchors; stops for the ones that left. */
  function positionAnchored() {
    const current = /** @type {HTMLElement[]} */ ([...host.querySelectorAll('[data-blazix-toast-positioner]')]);
    for (const el of current) {
      if (positioned.has(el)) {
        positioned.get(el)?.update();
        continue;
      }
      el.style.position = 'fixed';
      const placement = () => {
        try {
          return JSON.parse(el.dataset.blazixPlacement ?? '{}');
        } catch {
          return {};
        }
      };
      positioned.set(el, position(() => byId(placement().anchor), el, placement, {
        popup: () => /** @type {HTMLElement | null} */ (el.querySelector('[data-blazix-toast]')),
        arrow: () => /** @type {HTMLElement | null} */ (el.querySelector('[data-blazix-toast-arrow]')),
      }));
    }
    for (const [el, stop] of positioned) {
      if (!el.isConnected) {
        stop();
        positioned.delete(el);
      }
    }
  }

  /**
   * A toast's own height. CSS usually sizes a toast from the variables measured here (all take the
   * frontmost one's height while stacked), so its rendered height would only read those back: measure
   * it at height auto.
   * @param {HTMLElement} toast
   */
  function naturalHeight(toast) {
    const previous = toast.style.height;
    toast.style.height = 'auto';
    const height = toast.offsetHeight;
    toast.style.height = previous;
    return height;
  }

  // Newest first: each toast's offset is the height of the toasts in front of it.
  function layout() {
    // New toasts enter from data-starting-style. Mark them before anything below reads layout: that
    // computes their style, and a starting style set afterwards would be transitioned to, not from.
    /** @type {Set<string>} */
    const fresh = new Set();
    for (const toast of toasts()) {
      const id = toast.dataset.blazixToast ?? '';
      if (known.has(id)) continue;
      known.add(id);
      fresh.add(id);
      setVar(toast, '--toast-swipe-movement-x', '0px');
      setVar(toast, '--toast-swipe-movement-y', '0px');
      toast.toggleAttribute('data-expanded', expanded);
      animateIn([toast]);
    }
    positionAnchored();
    const list = toasts().filter((toast) => !toast.hasAttribute('data-ending-style'));
    const heights = list.map(naturalHeight);
    let offset = 0;
    list.forEach((toast, index) => {
      setVar(toast, '--toast-height', `${heights[index]}px`);
      setVar(toast, '--toast-offset-y', `${offset}px`);
      offset += heights[index];
    });
    viewport()?.style.setProperty('--toast-frontmost-height', `${heights[0] ?? 0}px`);
    for (const toast of toasts()) {
      const id = toast.dataset.blazixToast ?? '';
      const timeout = Number(toast.dataset.timeout ?? 0);
      if (!fresh.has(id) && (timeoutOf.get(id) === timeout || toast.hasAttribute('data-ending-style'))) {
        continue;
      }
      // New, or its timeout changed (a loading toast that finished): (re)start its timer.
      timeoutOf.set(id, timeout);
      const entry = timers.get(id);
      if (entry?.timer) clearTimeout(entry.timer);
      timers.delete(id);
      if (timeout > 0) {
        timers.set(id, { remaining: timeout, started: 0, timer: undefined });
        if (!paused()) startTimer(id);
      }
    }
    for (const id of [...known]) {
      if (!toasts().some((toast) => toast.dataset.blazixToast === id)) {
        known.delete(id);
        timeoutOf.delete(id);
        const entry = timers.get(id);
        if (entry?.timer) clearTimeout(entry.timer);
        timers.delete(id);
      }
    }
  }

  const changes = new MutationObserver((records) => {
    // Before the next paint: put back what Blazor's new style attribute dropped.
    for (const record of records) {
      if (record.attributeName === 'style') restoreVars(/** @type {HTMLElement} */ (record.target));
    }
    requestAnimationFrame(layout);
  });
  changes.observe(host, { childList: true, subtree: true, attributes: true, attributeFilter: ['data-blazix-placement', 'style'] });
  const sizes = new ResizeObserver(() => requestAnimationFrame(layout));
  sizes.observe(host);
  layout();

  /** @type {{ toast: HTMLElement, id: string, pointerId: number, x: number, y: number, dx: number, dy: number } | null} */
  let swipe = null;

  const offs = [
    on(host, 'pointerenter', () => setExpanded(true)),
    on(host, 'pointerleave', () => {
      if (!host.contains(document.activeElement)) setExpanded(false);
    }),
    on(host, 'focusin', () => setExpanded(true)),
    on(host, 'focusout', (/** @type {FocusEvent} */ event) => {
      if (!host.contains(/** @type {Node | null} */ (event.relatedTarget)) && !host.matches(':hover')) setExpanded(false);
    }),
    on(window, 'blur', () => {
      windowBlurred = true;
      pauseTimers();
    }),
    on(window, 'focus', () => {
      windowBlurred = false;
      resumeTimers();
    }),
    on(document, 'keydown', (/** @type {KeyboardEvent} */ event) => {
      if (event.key !== 'F6' || !toasts().length) return;
      event.preventDefault();
      viewport()?.focus();
    }),
    on(host, 'pointerdown', (/** @type {PointerEvent} */ event) => {
      const target = /** @type {Element} */ (event.target);
      const toast = /** @type {HTMLElement | null} */ (target.closest('[data-blazix-toast]'));
      if (!toast || event.button !== 0 || target.closest('button,a,input,textarea,select,[data-swipe-ignore]')) return;
      swipe = { toast, id: toast.dataset.blazixToast ?? '', pointerId: event.pointerId, x: event.clientX, y: event.clientY, dx: 0, dy: 0 };
      toast.setPointerCapture?.(event.pointerId);
    }),
    on(document, 'pointermove', (/** @type {PointerEvent} */ event) => {
      if (!swipe || event.pointerId !== swipe.pointerId) return;
      const dirs = options.swipeDirections;
      let dx = event.clientX - swipe.x;
      let dy = event.clientY - swipe.y;
      // Movement the toast can't be dismissed in is damped, so it resists.
      if (!(dirs.includes('right') && dx > 0) && !(dirs.includes('left') && dx < 0)) dx = Math.sign(dx) * Math.sqrt(Math.abs(dx));
      if (!(dirs.includes('down') && dy > 0) && !(dirs.includes('up') && dy < 0)) dy = Math.sign(dy) * Math.sqrt(Math.abs(dy));
      swipe.dx = dx;
      swipe.dy = dy;
      swipe.toast.setAttribute('data-swiping', '');
      setVar(swipe.toast, '--toast-swipe-movement-x', `${dx}px`);
      setVar(swipe.toast, '--toast-swipe-movement-y', `${dy}px`);
    }),
    ...['pointerup', 'pointercancel'].map((type) => on(document, type, (/** @type {PointerEvent} */ event) => {
      if (!swipe || event.pointerId !== swipe.pointerId) return;
      const { toast, id, dx, dy } = swipe;
      swipe = null;
      const dirs = options.swipeDirections;
      /** @type {string | null} */
      let direction = null;
      if (dirs.includes('right') && dx > SWIPE_THRESHOLD) direction = 'right';
      else if (dirs.includes('left') && dx < -SWIPE_THRESHOLD) direction = 'left';
      else if (dirs.includes('down') && dy > SWIPE_THRESHOLD) direction = 'down';
      else if (dirs.includes('up') && dy < -SWIPE_THRESHOLD) direction = 'up';
      toast.removeAttribute('data-swiping');
      if (direction) {
        toast.setAttribute('data-swipe-direction', direction);
        call(dotnet, 'Close', id);
      } else {
        setVar(toast, '--toast-swipe-movement-x', '0px');
        setVar(toast, '--toast-swipe-movement-y', '0px');
      }
    })),
  ];

  return {
    /** Blazor rendered a toast as closing: wait for its exit animation, then let Blazor remove it. @param {string} id */
    async closing(id) {
      const toast = toasts().find((el) => el.dataset.blazixToast === id);
      const entry = timers.get(id);
      if (entry?.timer) clearTimeout(entry.timer);
      timers.delete(id);
      if (toast) await whenAnimationsFinish([toast]).done;
      call(dotnet, 'Removed', id);
      requestAnimationFrame(layout);
    },
    /** @param {ToastOptions} next */
    update(next) {
      options = next;
    },
    dispose() {
      for (const off of offs) off();
      changes.disconnect();
      sizes.disconnect();
      for (const stop of positioned.values()) stop();
      positioned.clear();
      pauseTimers();
      releaseLayer();
    },
  };
}
