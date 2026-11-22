// @ts-check
//
// The browser side of Navigation Menu, on the shared popup layer. One popup (with its viewport)
// shows the active item's content, anchored to that item's trigger. Resting the pointer on a
// trigger opens its item after a delay, and moves straight to another item once one is open;
// leaving the menu and the popup closes it after a delay. Tab moves from an open trigger into its
// content, and on past it to the next trigger. Blazor owns which item is open.

import { byId, call, isRTL, on } from './core/dom.js';
import { tabbables } from './core/focus.js';
import { createPopup } from './core/popup.js';
import { whenAnimationsFinish } from './core/presence.js';

/** @typedef {{ rootId: string, delay: number, closeDelay: number, orientation: 'horizontal' | 'vertical', value: string | null }} NavigationOptions */

/**
 * @param {import('./core/dom.js').DotNetRef} dotnet
 * @param {string} triggerId the active item's trigger, which the popup is anchored to
 * @param {NavigationOptions} initial
 */
export function createNavigationMenu(dotnet, triggerId, initial) {
  let id = triggerId;
  let options = initial;
  let open = false;
  /** @type {ReturnType<typeof setTimeout> | undefined} */ let openTimer;
  /** @type {ReturnType<typeof setTimeout> | undefined} */ let closeTimer;

  const root = () => byId(options.rootId);
  const trigger = () => byId(id);
  const popup = createPopup({ trigger, requestClose: (reason) => call(dotnet, 'RequestClose', reason) });
  const host = () => popup.popup?.closest('[data-blazix-popup-host]') ?? null;
  const inMenu = (/** @type {Node | null} */ node) => !!node && (!!root()?.contains(node) || !!host()?.contains(node));
  const triggerOf = (/** @type {EventTarget | null} */ target) =>
    /** @type {HTMLElement | null} */ ((/** @type {Element} */ (target))?.closest?.('[data-blazix-nav-trigger]') ?? null);

  /** The size the popup is moving to, or null before it has one (so the first size doesn't animate). */
  /** @type {{ width: number, height: number } | null} */ let target = null;
  let sizeFrame = 0;
  let measureFrame = 0;

  /** @param {HTMLElement} el @param {string} width @param {string} height */
  function setSize(el, width, height) {
    for (const part of [el, el.querySelector('[data-blazix-nav-viewport]')]) {
      if (!(part instanceof HTMLElement)) continue;
      part.style.setProperty('--popup-width', width);
      part.style.setProperty('--popup-height', height);
    }
  }

  // The popup takes the active content's size, for CSS to animate between items.
  // The content fills the popup (w-auto, h-full), so measuring it would only give back the popup's
  // current size: the popup is measured instead, at its natural size, with the size variables set
  // to auto (an exiting content is absolutely positioned, so only the new one counts). Then it goes
  // back to the size it has now, and moves to the new one in the next frame.
  /** @param {boolean} animate */
  function measure(animate) {
    const el = popup.popup;
    if (!el) return;
    cancelAnimationFrame(sizeFrame);
    const from = { width: el.offsetWidth, height: el.offsetHeight };
    setSize(el, 'auto', 'auto');
    const to = { width: el.offsetWidth, height: el.offsetHeight };
    const first = !target;
    target = to;
    if (!animate || first || from.width === 0 || from.height === 0 || (from.width === to.width && from.height === to.height)) {
      setSize(el, `${to.width}px`, `${to.height}px`);
      popup.reposition();
      return;
    }
    setSize(el, `${from.width}px`, `${from.height}px`);
    void el.offsetWidth; // commit the size it starts from
    sizeFrame = requestAnimationFrame(() => {
      setSize(el, `${to.width}px`, `${to.height}px`);
      popup.reposition();
    });
  }

  /** Measures once in the next frame, however many changes asked for it. */
  function scheduleMeasure() {
    if (measureFrame) return;
    measureFrame = requestAnimationFrame(() => {
      measureFrame = 0;
      if (open) measure(true);
    });
  }

  // Content that changes while open (loaded later, expanded) resizes the popup too.
  const contentChanges = new MutationObserver(() => scheduleMeasure());

  const clearTimers = () => {
    clearTimeout(openTimer);
    clearTimeout(closeTimer);
  };

  const offs = [
    on(document, 'pointerover', (/** @type {PointerEvent} */ event) => {
      if (event.pointerType === 'touch') return;
      if (inMenu(/** @type {Node} */ (event.target))) clearTimeout(closeTimer);
      const t = triggerOf(event.target);
      if (!t || !root()?.contains(t) || t.matches(':disabled')) return;
      const value = t.dataset.value ?? '';
      if (open && value === options.value) return;
      clearTimeout(openTimer);
      // Once the menu is open, moving to another trigger switches at once.
      if (open) call(dotnet, 'RequestOpen', 'trigger-hover', value);
      else openTimer = setTimeout(() => call(dotnet, 'RequestOpen', 'trigger-hover', value), options.delay);
    }),
    on(document, 'pointerout', (/** @type {PointerEvent} */ event) => {
      if (event.pointerType === 'touch') return;
      const from = /** @type {Node} */ (event.target);
      const to = /** @type {Node | null} */ (event.relatedTarget);
      if (!inMenu(from) || inMenu(to)) return;
      clearTimeout(openTimer);
      if (!open) return;
      clearTimeout(closeTimer);
      closeTimer = setTimeout(() => call(dotnet, 'RequestClose', 'trigger-hover'), options.closeDelay);
    }),
    on(document, 'keydown', (/** @type {KeyboardEvent} */ event) => {
      const t = triggerOf(event.target);
      if (!t || event.target !== t || !root()?.contains(t) || event.defaultPrevented) return;
      // A vertical menu opens toward the inline end, where its content sits.
      const opener = options.orientation === 'vertical' ? (isRTL(t) ? 'ArrowLeft' : 'ArrowRight') : 'ArrowDown';
      if (event.key === opener) {
        event.preventDefault();
        call(dotnet, 'RequestOpen', 'list-navigation', t.dataset.value ?? '');
        return;
      }
      // Tab from the open item's trigger goes into its content, as if it followed the trigger.
      if (event.key === 'Tab' && !event.shiftKey && open && t.id === id && popup.popup) {
        const first = tabbables(popup.popup)[0];
        if (first) {
          event.preventDefault();
          first.focus();
        }
      }
    }),
  ];

  return {
    /** @param {import('./core/popup.js').ShowOptions} next */
    show(next) {
      const wasOpen = open;
      open = true;
      clearTimers();
      popup.show({ ...next, initialFocus: 'none', insideIds: [options.rootId] });
      const viewport = popup.popup?.querySelector('[data-blazix-nav-viewport]');
      contentChanges.disconnect();
      if (viewport) contentChanges.observe(viewport, { childList: true, subtree: true, characterData: true });
      // Opening takes the content's size at once; an update while open animates to it.
      if (wasOpen) scheduleMeasure();
      else measure(false);
      if (!wasOpen && next.initialFocus === 'first' && popup.popup) tabbables(popup.popup)[0]?.focus();
    },
    /** @param {boolean} restore */
    hide(restore) {
      open = false;
      target = null;
      clearTimers();
      contentChanges.disconnect();
      cancelAnimationFrame(measureFrame);
      measureFrame = 0;
      return popup.hide(restore, null);
    },
    restoreFocus() {
      popup.restoreFocus();
    },
    /** The active item changed: anchor to its trigger, and measure its content. */
    /** @param {string} triggerIdNext @param {NavigationOptions} next */
    setTrigger(triggerIdNext, next) {
      id = triggerIdNext;
      options = next;
      if (open) scheduleMeasure();
    },
    /**
     * Blazor rendered a switch between items: the new content with data-starting-style, the old one
     * with data-ending-style. Starts the new one's transition; resolves once the old one's finished.
     */
    async settleContent() {
      const viewport = popup.popup?.querySelector('[data-blazix-nav-viewport]');
      if (!viewport) return true;
      const entering = /** @type {HTMLElement[]} */ ([...viewport.querySelectorAll(':scope > [data-starting-style]')]);
      await new Promise((resolve) => requestAnimationFrame(() => requestAnimationFrame(resolve)));
      for (const el of entering) el.removeAttribute('data-starting-style');
      const exiting = /** @type {HTMLElement[]} */ ([...viewport.querySelectorAll(':scope > [data-blazix-nav-exiting]')]);
      if (!exiting.length) return true;
      return whenAnimationsFinish(exiting).done;
    },
    measure: () => measure(true),
    dispose() {
      clearTimers();
      contentChanges.disconnect();
      cancelAnimationFrame(measureFrame);
      cancelAnimationFrame(sizeFrame);
      for (const off of offs) off();
      popup.dispose();
    },
  };
}
