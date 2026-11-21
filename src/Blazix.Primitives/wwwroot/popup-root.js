// @ts-check
//
// The browser side of Popover, Tooltip, Preview Card, Dialog and Drawer roots: the shared popup layer
// (core/popup.js) plus the trigger behaviors a popup opens with besides a press, which Blazor
// handles itself: resting the pointer on the trigger (with open/close delays and a bridge to the
// popup), keyboard focus on the trigger, and tooltip group delays.
//
// Like everything here, it never opens or closes anything: it asks .NET (RequestOpen/RequestClose)
// and reflects what Blazor renders.

import { byId, call, lastPointer, on, usingKeyboard } from './core/dom.js';
import { createHoverIntent } from './core/hover.js';
import { createPopup } from './core/popup.js';

/**
 * @typedef {{
 *   openOnHover?: boolean, delay?: number, closeDelay?: number,
 *   openOnFocus?: boolean, closeOnTriggerPress?: boolean, hoverable?: boolean,
 *   group?: string | null, groupTimeout?: number, triggerIds?: string[],
 * }} TriggerOptions
 */

// Tooltip groups (TooltipProvider): once one tooltip of a group has shown, the next opens at once
// if the pointer reaches it within the group's timeout.
/** @type {Map<string, { open: number, closedAt: number }>} */
const groups = new Map();

/**
 * @param {import('./core/dom.js').DotNetRef} dotnet
 * @param {string} triggerId
 * @param {TriggerOptions} initial
 */
export function createPopupRoot(dotnet, triggerId, initial) {
  let id = triggerId;
  let options = initial;
  let open = false;
  let openedByHover = false;
  /** @type {(() => void)[]} */
  let offs = [];

  const trigger = () => byId(id);
  // Every trigger that opens this popup (a handle can connect several); the active one anchors it.
  const triggers = () => /** @type {HTMLElement[]} */ ((options.triggerIds?.length ? options.triggerIds : [id]).map(byId).filter(Boolean));
  const triggerOf = (/** @type {EventTarget | null} */ target) => triggers().find((el) => target instanceof Node && el.contains(target)) ?? null;
  /** @type {HTMLElement | null} */
  let hovered = null;
  const popup = createPopup({
    trigger,
    requestClose: (reason) => call(dotnet, 'RequestClose', reason),
  });

  const group = () => (options.group ? groups.get(options.group) ?? groups.set(options.group, { open: 0, closedAt: 0 }).get(options.group) : null);
  const instant = () => {
    const g = group();
    return !!g && (g.open > 0 || Date.now() - g.closedAt < (options.groupTimeout ?? 400));
  };

  const hover = createHoverIntent({
    trigger: () => hovered ?? trigger(),
    popup: () => (options.hoverable === false ? null : popup.popup),
    isOpen: () => open,
    openDelay: () => (instant() ? 0 : options.delay ?? 0),
    closeDelay: () => options.closeDelay ?? 0,
    open: () => {
      openedByHover = true;
      call(dotnet, 'RequestOpenFrom', 'trigger-hover', instant() ? 'instant' : null, hovered?.id ?? id);
    },
    close: () => {
      if (openedByHover) call(dotnet, 'RequestClose', 'trigger-hover');
    },
  });

  function bind() {
    for (const off of offs) off();
    offs = [];
    // Delegated, so a trigger that Render swaps for another element keeps working.
    offs.push(on(document, 'pointerover', (/** @type {PointerEvent} */ event) => {
      const el = triggerOf(event.target);
      if (!options.openOnHover || !el) return;
      if (el.contains(/** @type {Node | null} */ (event.relatedTarget))) return;
      if (el.matches(':disabled') || el.getAttribute('aria-disabled') === 'true') return;
      hovered = el;
      // Open against another trigger: move there at once.
      if (open && el.id !== id && event.pointerType !== 'touch') {
        call(dotnet, 'RequestOpenFrom', 'trigger-hover', 'instant', el.id);
        return;
      }
      hover.enterTrigger(event);
    }));
    offs.push(on(document, 'pointerout', (/** @type {PointerEvent} */ event) => {
      const el = triggerOf(event.target) ?? trigger();
      const target = /** @type {Node} */ (event.target);
      const related = /** @type {Node | null} */ (event.relatedTarget);
      if (!options.openOnHover || !el) return;
      const pop = popup.popup;
      const leftTrigger = el.contains(target) && !el.contains(related) && !pop?.contains(related);
      const leftPopup = !!pop && pop.contains(target) && !pop.contains(related) && !el.contains(related);
      if (leftTrigger || leftPopup) hover.leave(event);
    }));
    offs.push(on(document, 'pointerover', (/** @type {PointerEvent} */ event) => {
      const pop = popup.popup;
      if (options.openOnHover && pop?.contains(/** @type {Node} */ (event.target))) hover.enterPopup(event);
    }));
    offs.push(on(document, 'focusin', (/** @type {FocusEvent} */ event) => {
      const el = triggerOf(event.target);
      if (!options.openOnFocus || !el || event.target !== el || (open && el.id === id)) return;
      // Tooltips and preview cards open for keyboard focus, not the focus a click gives.
      if (!usingKeyboard()) return;
      openedByHover = false;
      call(dotnet, 'RequestOpenFrom', 'trigger-focus', instant() ? 'instant' : null, el.id);
    }));
    offs.push(on(document, 'focusout', (/** @type {FocusEvent} */ event) => {
      const el = trigger();
      if (!options.openOnFocus || !el || event.target !== el || !open) return;
      const to = /** @type {Node | null} */ (event.relatedTarget);
      if (to && popup.contains(to)) return;
      call(dotnet, 'RequestClose', 'focus-out');
    }));
    offs.push(on(document, 'pointerdown', (/** @type {PointerEvent} */ event) => {
      const el = triggerOf(event.target);
      if (!options.closeOnTriggerPress || !el) return;
      hover.clear();
      if (open) call(dotnet, 'RequestClose', 'trigger-press');
    }, { capture: true }));
  }
  bind();

  return {
    /** @param {import('./core/popup.js').ShowOptions & { finalFocusId?: string | null, instant?: boolean }} next */
    show(next) {
      const wasOpen = open;
      open = true;
      if (!wasOpen) {
        const g = group();
        if (g) g.open += 1;
      }
      // The default: after a touch, focus the popup itself (no keyboard pops up); otherwise
      // the first tabbable element in it.
      const initialFocus = next.initialFocus ?? (lastPointer().type === 'touch' && !usingKeyboard() ? 'popup' : 'first');
      popup.show({ ...next, initialFocus: wasOpen ? 'none' : initialFocus });
      const el = popup.popup;
      if (el && !wasOpen) el.toggleAttribute('data-instant', instant() && (group()?.open ?? 0) > 1);
    },
    /** @param {boolean} restore */
    hide(restore) {
      if (open) {
        const g = group();
        if (g) {
          g.open = Math.max(0, g.open - 1);
          g.closedAt = Date.now();
        }
      }
      open = false;
      openedByHover = false;
      hover.clear();
      return popup.hide(restore, popup.options?.finalFocusId ?? null);
    },
    restoreFocus() {
      popup.restoreFocus();
    },
    reposition() {
      popup.reposition();
    },
    /** @param {string} triggerIdNext @param {TriggerOptions} next */
    setTrigger(triggerIdNext, next) {
      const moved = id !== triggerIdNext;
      id = triggerIdNext;
      options = next;
      bind();
      if (moved && open) popup.reposition();
    },
    dispose() {
      hover.clear();
      for (const off of offs) off();
      offs = [];
      popup.dispose();
    },
  };
}
