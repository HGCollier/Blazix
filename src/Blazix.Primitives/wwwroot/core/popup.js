// @ts-check
//
// The popup layer every overlay shares (after UI SDK's popups/popup): top layer, positioning,
// the dismissal layer, Tab modes, initial and returned focus, modal side effects, and the exit wait.
// Components add only what's theirs (menu keyboard, hover opening, list navigation...).
//
// Who owns what: Blazor owns open/closed and renders every attribute, including the closed state
// (data-closed, data-ending-style, an inert host). This module never opens or closes anything by
// itself; it reports intent through `requestClose(reason)` and reflects what Blazor renders.
//
// Top layer: each popup renders inside one or more `popover="manual"` host elements, which this
// module styles inline (they're internal, never the author's): an anchored popup's host is its
// positioner; a dialog's host is a zero-size fixed box, so the author positions the popup itself
// with position: fixed and the user-agent popover styles never fight their CSS. Hosts stay where
// they are in the DOM, so cascading values, CSS isolation and theme classes keep working.

import { activeElement, byId, contains, focus, on } from './dom.js';
import { focusInitial, handleTab, returnFocus } from './focus.js';
import { findLayer, layerContains, pushLayer } from './layers.js';
import { inertOutside, lockScroll, scrollLocked } from './modal.js';
import { position } from './position.js';
import { animateIn, whenAnimationsFinish } from './presence.js';

const POSITIONER_STYLE = 'position:fixed;inset:auto;margin:0;padding:0;border:0;background:transparent;overflow:visible;color:inherit;width:max-content;max-width:none;max-height:none;';
const HOST_STYLE = 'position:fixed;inset:0 auto auto 0;width:0;height:0;margin:0;padding:0;border:0;background:transparent;overflow:visible;color:inherit;';

// Hosts Blazor renders with data-blazix-appear (a dialog or drawer that's open since its root's first
// render: it positions itself and doesn't animate in) show the moment they're inserted, rather than
// a round trip later when .NET calls show(). So a drawer that takes a dialog's place at a breakpoint
// replaces it in the same frame. Each one's backdrop goes into the top layer first, beneath it;
// show() then finds them open and sets up the rest.
const APPEAR = '[data-blazix-popup-host][data-blazix-appear]';

// The modal a host replaces lets go of the scroll lock after this render, before the new popup's
// show(): the page stays locked meanwhile, and show() takes it over (or lets it go, if not modal).
/** @type {Map<string, () => void>} */
const heldLocks = new Map();

/** @param {Element | null} host @param {boolean} [holdLock] */
function appear(host, holdLock = false) {
  if (!(host instanceof HTMLElement) || !host.isConnected || host.matches(':popover-open')) return;
  host.style.cssText = HOST_STYLE;
  try { host.showPopover(); } catch { return; /* not a popover */ }
  if (holdLock && scrollLocked() && !heldLocks.has(host.id)) heldLocks.set(host.id, lockScroll());
}

new MutationObserver((records) => {
  for (const record of records) {
    for (const node of record.addedNodes) {
      if (!(node instanceof Element)) continue;
      for (const host of [...(node.matches(APPEAR) ? [node] : []), ...node.querySelectorAll(APPEAR)]) {
        appear(document.querySelector(`[data-blazix-backdrop-for="${CSS.escape(host.id)}"]`));
        appear(host, true);
      }
    }
  }
  // A host that left before its popup showed doesn't keep the page locked.
  for (const [id, release] of heldLocks) {
    if (!byId(id)) {
      heldLocks.delete(id);
      release();
    }
  }
}).observe(document, { childList: true, subtree: true });

/**
 * @typedef {import('./position.js').PositionOptions} PositionOptions
 * @typedef {'close' | 'flow' | 'trap' | 'native'} TabMode
 * @typedef {{
 *   hosts: string[],
 *   popupId: string,
 *   positionerId?: string | null,
 *   arrowId?: string | null,
 *   placement?: PositionOptions | null,
 *   anchorId?: string | null,
 *   anchorPoint?: { x: number, y: number } | null,
 *   modal?: boolean | 'trap-focus',
 *   initialFocus?: string | null,
 *   tab?: TabMode,
 *   outsidePress?: 'pointerdown' | 'click',
 *   dismissible?: boolean,
 *   closeOnFocusOut?: boolean,
 *   closeOnBlur?: boolean,
 *   keepTrigger?: boolean,
 *   shield?: boolean,
 *   insideIds?: string[],
 *   backdrops?: string[],
 *   finalFocusId?: string | null,
 *   animate?: boolean,
 * }} ShowOptions
 */

/**
 * @param {{
 *   trigger: () => HTMLElement | null,
 *   requestClose: (reason: string, event?: Event) => void,
 *   onShown?: () => void,
 * }} config
 */
export function createPopup(config) {
  /** @type {ShowOptions | null} */
  let options = null;
  /** @type {HTMLElement[]} */
  let hosts = [];
  /** @type {(() => void)[]} */
  let detachers = [];
  /** @type {(ReturnType<typeof position>) | null} */
  let positioning = null;
  /** @type {(() => void) | null} */
  let releaseInert = null;
  /** @type {(() => void) | null} */
  let releaseScroll = null;
  /** @type {(() => void) | null} */
  let popLayer = null;
  /** @type {{ cancel: () => void } | null} */
  let exit = null;
  /** @type {Promise<boolean>} */
  let exitDone = Promise.resolve(true);
  /** @type {MutationObserver | null} */
  let closeWatch = null;
  /** @type {HTMLElement | null} */
  let focusedBefore = null;
  let open = false;
  let layerOrder = 0;
  /** @type {ResizeObserver | null} */
  let viewportSizes = null;
  /** @type {string} */
  let viewportSize = '';

  // A Viewport part inside the popup: its current content's size, for CSS to animate between triggers.
  function measureViewport() {
    const el = popup();
    const viewport = /** @type {HTMLElement | null} */ (el?.querySelector('[data-blazix-viewport]') ?? null);
    const current = /** @type {HTMLElement | null} */ (viewport?.querySelector('[data-current]') ?? null);
    if (!el || !viewport || !current) return;
    const size = `${current.offsetWidth}x${current.offsetHeight}`;
    if (size === viewportSize) return;
    const changing = viewportSize !== '';
    viewportSize = size;
    for (const target of [el, viewport]) {
      target.style.setProperty('--popup-width', `${current.offsetWidth}px`);
      target.style.setProperty('--popup-height', `${current.offsetHeight}px`);
    }
    if (changing) {
      viewport.setAttribute('data-transitioning', '');
      whenAnimationsFinish([el, viewport]).done.then(() => viewport.removeAttribute('data-transitioning'));
    }
    if (!viewportSizes) {
      viewportSizes = new ResizeObserver(() => measureViewport());
    }
    viewportSizes.disconnect();
    viewportSizes.observe(current);
  }

  const popup = () => byId(options?.popupId);
  const anchor = () => {
    if (options?.anchorPoint) return options.anchorPoint;
    return byId(options?.anchorId) ?? config.trigger();
  };
  // Backdrops render in the top layer too, but pressing one is pressing outside the popup.
  const isBackdrop = (/** @type {HTMLElement} */ host) => (options?.backdrops ?? []).includes(host.id);
  const inside = (/** @type {Node | null | undefined} */ node) => {
    if (!node) return false;
    // What wraps the popup in its host (the host itself, a Dialog or Drawer Viewport) can fill the
    // screen; pressing it rather than the popup is pressing outside.
    const el = popup();
    const aroundPopup = !!el && node !== el && node.nodeType === Node.ELEMENT_NODE && node.contains(el);
    if (!aroundPopup && hosts.some((host) => !isBackdrop(host) && contains(host, node))) return true;
    if (options?.keepTrigger !== false && contains(config.trigger(), node)) return true;
    return (options?.insideIds ?? []).some((id) => contains(byId(id), node));
  };

  function detach() {
    for (const off of detachers) off();
    detachers = [];
    popLayer?.();
    popLayer = null;
    releaseInert?.();
    releaseInert = null;
  }

  function stopPositioning() {
    positioning?.();
    positioning = null;
    viewportSizes?.disconnect();
  }

  /**
   * Waits out the exit animation, then hides the hosts. Exit keyframes don't keep their end state, so
   * once they finish the popup shows at its open style again until the hosts hide: that has to happen
   * here, in the same frame, not a server round trip later.
   */
  function startExit() {
    const parts = hosts.flatMap((host) => [host, /** @type {HTMLElement | null} */ (host.firstElementChild)]);
    const el = popup();
    if (el) parts.push(el);
    const wait = whenAnimationsFinish(parts);
    exit = wait;
    exitDone = wait.done.then((completed) => {
      if (exit === wait || completed) finishExit(completed);
      return completed;
    });
    return exitDone;
  }

  /**
   * Starts the exit as soon as Blazor renders the closed state, rather than when .NET calls hide(),
   * which can arrive after a short exit animation has already ended.
   */
  function watchClose() {
    closeWatch?.disconnect();
    const host = hosts.find((h) => !isBackdrop(h));
    if (!host) return;
    closeWatch = new MutationObserver(() => {
      if (open && !exit && host.hasAttribute('data-closed')) startExit();
    });
    closeWatch.observe(host, { attributes: true, attributeFilter: ['data-closed'] });
  }

  /** @param {boolean} completed */
  function finishExit(completed) {
    exit = null;
    if (!completed) return;
    stopPositioning();
    for (const host of hosts) {
      if (host.isConnected && host.matches(':popover-open')) {
        try { host.hidePopover(); } catch { /* already hidden */ }
      }
    }
    releaseScroll?.();
    releaseScroll = null;
  }

  const controller = {
    get isOpen() { return open; },
    get popup() { return popup(); },
    get options() { return options; },
    /** Whether a node is inside this popup, a child popup of it, or its trigger. */
    contains(/** @type {Node} */ node) { return inside(node); },

    /**
     * Shows the popup, or updates it if it's already shown (reopening mid-exit cancels the exit).
     * @param {ShowOptions} next
     */
    show(next) {
      const reopening = !!exit;
      exit?.cancel();
      const wasOpen = open;
      detach();
      options = next;
      open = true;

      hosts = /** @type {HTMLElement[]} */ (next.hosts.map(byId).filter(Boolean));
      const el = popup();
      if (!el || hosts.length === 0) {
        console.warn(`Blazix.Primitives: no element with id "${next.popupId}". A Render fragment must spread props.Attributes onto its element.`);
        return;
      }
      const positioner = byId(next.positionerId);
      const entering = hosts.filter((host) => !host.matches(':popover-open'));
      // A popup that was open on its root's first render (animate: false) appears as it is, as in
      // No starting style, so enter transitions don't play. Keyframe animations still do.
      if (entering.length && !reopening && next.animate !== false) {
        // Each host's first element child is a part the author styles (popup, backdrop, viewport),
        // and a popup inside a viewport enters too. Mark them before the hosts show: showPopover()
        // computes their style at once, so a starting style set after it would be transitioned to,
        // not from (a drawer would sit still instead of sliding in).
        const parts = entering.flatMap((host) => {
          if (host === positioner) return [el];
          const first = /** @type {HTMLElement | null} */ (host.firstElementChild);
          return first !== el && host.contains(el) ? [first, el] : [first];
        });
        animateIn(parts);
      }
      for (const host of entering) {
        host.style.cssText = host === positioner ? POSITIONER_STYLE : HOST_STYLE;
        try { host.showPopover(); } catch { /* not connected */ }
      }

      if (positioner && next.placement) {
        if (positioning) positioning.update();
        else {
          positioning = position(anchor, positioner, () => options?.placement ?? {}, {
            popup: () => popup(),
            arrow: () => byId(options?.arrowId),
          });
        }
      } else {
        stopPositioning();
      }

      const trigger = config.trigger();
      if (!wasOpen && !reopening) {
        const active = activeElement();
        focusedBefore = active && active !== document.body ? active : null;
        layerOrder += 1;
      }

      /** @type {import('./layers.js').Layer} */
      const layer = {
        contains: inside,
        parent: findLayer(trigger ?? positioner ?? el),
        active: () => open,
        exiting: () => !!exit,
        modal: next.modal === true,
        outsidePress: next.outsidePress ?? 'pointerdown',
        shield: next.shield ?? false,
        onEscape: (event) => config.requestClose('escape-key', event),
        onOutsidePress: (event) => {
          if (next.dismissible !== false) config.requestClose('outside-press', event);
        },
        onBlur: next.closeOnBlur ? (event) => config.requestClose('focus-out', event) : undefined,
      };
      popLayer = pushLayer(layer);

      if (next.modal === true) {
        const keep = [...hosts];
        if (next.keepTrigger !== false && trigger) keep.push(trigger);
        for (const id of next.insideIds ?? []) {
          const extra = byId(id);
          if (extra) keep.push(extra);
        }
        releaseInert = inertOutside(keep);
        releaseScroll ??= lockScroll();
      } else if (!exit) {
        releaseScroll?.();
        releaseScroll = null;
      }
      // The lock held since these hosts appeared hands over to this popup's own.
      for (const host of hosts) {
        heldLocks.get(host.id)?.();
        heldLocks.delete(host.id);
      }

      for (const host of hosts) {
        detachers.push(on(host, 'keydown', (/** @type {KeyboardEvent} */ event) => {
          if (event.key !== 'Tab' || event.defaultPrevented) return;
          const mode = options?.tab ?? 'flow';
          if (mode === 'native') return;
          if (mode === 'close') {
            // Focus the trigger without preventDefault, so the browser carries on tabbing from there.
            focus(config.trigger());
            config.requestClose('focus-out', event);
            return;
          }
          const moved = handleTab(event, popup() ?? host, mode, config.trigger());
          if (moved === 'left-after' && next.closeOnFocusOut !== false) config.requestClose('focus-out', event);
        }));
        detachers.push(on(host, 'focusout', (/** @type {FocusEvent} */ event) => {
          const to = /** @type {Node | null} */ (event.relatedTarget);
          // Focus vanished (e.g. Blazor removed the element) or moved within this popup or a child popup.
          if (!to || inside(to) || layerContains(layer, to)) return;
          if (next.closeOnFocusOut !== false && next.tab !== 'native') config.requestClose('focus-out', event);
        }));
      }

      viewportSize = wasOpen ? viewportSize : '';
      measureViewport();
      watchClose();

      if (!wasOpen && !reopening) {
        const el2 = popup();
        if (el2 && next.initialFocus !== 'none' && next.initialFocus != null) focusInitial(el2, next.initialFocus);
        config.onShown?.();
      }
    },

    /** Re-reads placement now (after options changed). */
    reposition() {
      measureViewport();
      positioning?.update();
    },

    /** Stops following the anchor, for a component that places the popup itself (Select's item alignment). */
    stopPositioning() {
      stopPositioning();
    },

    /**
     * Starts the exit. Blazor has already rendered the closed state; this stops interaction, hands
     * focus back, waits for the exit animation, then hides the hosts.
     * @param {boolean} restore whether focus goes back to the trigger
     * @param {string | null} [finalFocusId] where focus goes instead of the trigger
     * @returns {Promise<boolean>} true once finished; false if `show` reopened it first
     */
    hide(restore, finalFocusId) {
      const wasOpen = open;
      open = false;
      closeWatch?.disconnect();
      closeWatch = null;
      detach();
      if (restore && wasOpen) returnFocus(popup() ?? hosts[0] ?? null, byId(finalFocusId) ?? config.trigger(), focusedBefore);
      // Usually the exit started when the closed state rendered (watchClose); join it.
      return exit ? exitDone : startExit();
    },

    /** Moves focus back to the trigger if it's inside the popup or was lost. */
    restoreFocus() {
      returnFocus(popup() ?? hosts[0] ?? null, config.trigger(), focusedBefore);
    },

    dispose() {
      exit?.cancel();
      closeWatch?.disconnect();
      closeWatch = null;
      open = false;
      detach();
      finishExit(true);
    },
  };
  return controller;
}
