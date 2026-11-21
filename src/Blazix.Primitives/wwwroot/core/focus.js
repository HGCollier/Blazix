// @ts-check
//
// Tab order helpers (after UI SDK's focus/tabbable) and the Tab modes popups share.

import { activeElement, focus } from './dom.js';

const candidates = [
  'a[href]', 'area[href]', 'button', 'input:not([type="hidden"])', 'select', 'textarea', 'iframe',
  'summary', 'audio[controls]', 'video[controls]', '[contenteditable]:not([contenteditable="false"])', '[tabindex]',
].join(',');

/** @param {Element} el @returns {el is HTMLElement} */
export function isTabbable(el) {
  if (!(el instanceof HTMLElement) || el.tabIndex < 0) return false;
  if (el.matches(':disabled') || el.closest('[inert]')) return false;
  // Only one radio of a native group takes Tab: the checked one, or the first.
  if (el instanceof HTMLInputElement && el.type === 'radio' && el.name && !el.checked) {
    const scope = el.form ?? el.getRootNode();
    const group = /** @type {HTMLInputElement[]} */ ([...(/** @type {ParentNode} */ (scope)).querySelectorAll(`input[type="radio"][name="${CSS.escape(el.name)}"]`)]);
    if (group.some((radio) => radio.checked && radio !== el)) return false;
  }
  if (typeof el.checkVisibility === 'function') return el.checkVisibility({ visibilityProperty: true });
  return el.getClientRects().length > 0 && getComputedStyle(el).visibility !== 'hidden';
}

/** @param {ParentNode} root */
export function tabbables(root) {
  return [...root.querySelectorAll(candidates)].filter(isTabbable);
}

/** The first tabbable element after `node` in the document, skipping anything inside `skip`. */
export function nextTabbable(/** @type {Node} */ node, /** @type {Element | null} */ skip) {
  return tabbables(document.body).find((el) => !el.contains(node) && !skip?.contains(el)
    && !!(node.compareDocumentPosition(el) & Node.DOCUMENT_POSITION_FOLLOWING));
}

/** The last tabbable element before `node`, skipping anything inside `skip`. */
export function previousTabbable(/** @type {Node} */ node, /** @type {Element | null} */ skip) {
  return tabbables(document.body).reverse().find((el) => !el.contains(node) && !skip?.contains(el)
    && !!(node.compareDocumentPosition(el) & Node.DOCUMENT_POSITION_PRECEDING));
}

/**
 * Handles Tab inside a popup.
 * - "trap": Tab wraps inside the popup (modal dialogs).
 * - "flow": Tab moves from the popup back to the trigger, and on to what follows the trigger, as if
 *   the popup rendered right after it (non-modal popovers render in the top layer, wherever they are).
 * - "close" and "native": the caller handles it.
 * @param {KeyboardEvent} event
 * @param {HTMLElement} popup
 * @param {'trap' | 'flow'} mode
 * @param {HTMLElement | null} trigger
 * @returns {'stayed' | 'left-before' | 'left-after' | null} where focus went, when this moved it
 */
export function handleTab(event, popup, mode, trigger) {
  if (event.key !== 'Tab' || event.defaultPrevented) return null;
  const list = tabbables(popup);
  const first = list[0];
  const last = list.at(-1);
  const active = activeElement();
  const atStart = !first || active === first || active === popup;
  const atEnd = !last || active === last;

  if (mode === 'trap') {
    if (event.shiftKey ? atStart : atEnd) {
      event.preventDefault();
      focus((event.shiftKey ? last : first) ?? popup);
      return 'stayed';
    }
    return null;
  }

  if (!trigger) return null;
  if (event.shiftKey && atStart) {
    event.preventDefault();
    focus(trigger);
    return 'left-before';
  }
  if (!event.shiftKey && atEnd) {
    const next = nextTabbable(trigger, popup);
    if (!next) return null;
    event.preventDefault();
    next.focus();
    return 'left-after';
  }
  return null;
}

/**
 * Moves focus into a popup when it opens.
 * @param {HTMLElement} popup
 * @param {string | null | undefined} target "popup" (the container), "first" (first tabbable),
 *   "none", or a CSS selector inside the popup. Defaults to an element with [autofocus], then the
 *   first tabbable, then the popup.
 */
export function focusInitial(popup, target) {
  if (target === 'none') return;
  if (target === 'popup') {
    focus(popup);
    return;
  }
  if (target && target !== 'first') {
    const el = /** @type {HTMLElement | null} */ (popup.querySelector(target));
    if (el && focus(el)) return;
  }
  const auto = /** @type {HTMLElement | null} */ (popup.querySelector('[autofocus],[data-autofocus]'));
  if (target !== 'first' && auto && focus(auto)) return;
  const first = tabbables(popup)[0];
  if (first && focus(first)) return;
  if (popup.tabIndex < 0 && !popup.hasAttribute('tabindex')) popup.setAttribute('tabindex', '-1');
  focus(popup);
}

/**
 * Hands focus back when a popup closes: to `target` if it can take it, else to `fallback`.
 * Only when focus is inside the popup or was lost to <body>; never steals it from elsewhere.
 * @param {HTMLElement | null} popup
 * @param {HTMLElement | null} target
 * @param {HTMLElement | null} [fallback]
 */
export function returnFocus(popup, target, fallback) {
  const active = document.activeElement;
  const lost = !active || active === document.body;
  const inside = !!active && !!popup?.contains(active);
  if (!lost && !inside) return;
  if (target?.isConnected && focus(target)) return;
  if (fallback?.isConnected) focus(fallback);
}
