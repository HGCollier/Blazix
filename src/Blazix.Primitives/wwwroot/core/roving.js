// @ts-check
//
// Arrow keys between the items of a group with one tab stop (after UI SDK's focus/roving-focus):
// Tabs, Radio Group, Toggle Group, Toolbar, Accordion and Menubar run on it.
//
// Declarative: Blazor renders the group and its items with marker attributes, and one delegated
// document listener does the rest, so nothing is cached and Blazor can add, remove, disable or
// swap items (Render) at any time.
//   group:  data-blazix-roving="horizontal|vertical|both", data-blazix-loop, data-blazix-activate (click on move)
//   items:  data-blazix-roving-item; data-focusable keeps a disabled item reachable (toolbars)
// Blazor owns the tab stop: each item renders tabindex 0 or -1 from C# state, which follows the
// items' focus events. This only moves real focus, because it has to preventDefault inside the event.

import { isRTL } from './dom.js';

/**
 * Whether a text field keeps an arrow key for its caret instead of letting the group move focus.
 * Left and Right leave the field only from the end they point at, with nothing selected.
 * @param {KeyboardEvent} event @param {Element} el @param {boolean} rtl
 */
function fieldKeepsKey(event, el, rtl) {
  if (!(el instanceof HTMLInputElement || el instanceof HTMLTextAreaElement)) return false;
  const key = event.key;
  if (key === 'Home' || key === 'End') return el.type !== 'checkbox' && el.type !== 'radio';
  if (key === 'ArrowUp' || key === 'ArrowDown') return el instanceof HTMLTextAreaElement || el.type === 'number';
  if (key !== 'ArrowLeft' && key !== 'ArrowRight') return false;
  let start;
  let end;
  try {
    start = el.selectionStart;
    end = el.selectionEnd;
  } catch {
    return false; // checkboxes and the like have no caret
  }
  if (start === null || end === null) return false;
  if (start !== end) return true;
  const backward = key === (rtl ? 'ArrowRight' : 'ArrowLeft');
  return backward ? start > 0 : end < el.value.length;
}

/** @param {string} key @param {string} orientation @param {boolean} rtl */
export function stepFor(key, orientation, rtl) {
  const right = rtl ? -1 : 1;
  if (key === 'Home') return 'first';
  if (key === 'End') return 'last';
  if (orientation !== 'horizontal') {
    if (key === 'ArrowDown') return 1;
    if (key === 'ArrowUp') return -1;
  }
  if (orientation !== 'vertical') {
    if (key === 'ArrowRight') return right;
    if (key === 'ArrowLeft') return -right;
  }
  return undefined;
}

/** Disabled items are skipped, unless they say they stay focusable (data-focusable). */
export function canFocus(/** @type {HTMLElement} */ el) {
  if (el.hasAttribute('data-focusable')) return true;
  return !el.matches(':disabled') && el.getAttribute('aria-disabled') !== 'true' && !el.hasAttribute('data-disabled');
}

/** A group's own items in DOM order: not those of a nested group. */
export function groupItems(/** @type {Element} */ group) {
  return /** @type {HTMLElement[]} */ ([...group.querySelectorAll('[data-blazix-roving-item]')])
    .filter((el) => el.parentElement?.closest('[data-blazix-roving]') === group);
}

/**
 * The item a key press moves to, or undefined for keys the group doesn't handle.
 * @param {KeyboardEvent} event @param {HTMLElement} group @param {HTMLElement} from
 */
export function rovingTarget(event, group, from) {
  const orientation = group.getAttribute('data-blazix-roving') || 'horizontal';
  const rtl = group.closest('[dir]')?.getAttribute('dir') === 'rtl' || isRTL(group);
  const step = stepFor(event.key, orientation, rtl);
  if (step === undefined) return undefined;
  const list = groupItems(group).filter((item) => item === from || canFocus(item));
  const index = list.indexOf(from);
  if (step === 'first') return list[0];
  if (step === 'last') return list.at(-1);
  const to = index + step;
  if (group.hasAttribute('data-blazix-loop')) return list[(to + list.length) % list.length];
  return list[Math.max(0, Math.min(list.length - 1, to))];
}

const installed = /** @type {any} */ (globalThis)[Symbol.for('blazix.primitives.roving')];
if (!installed) {
  /** @type {any} */ (globalThis)[Symbol.for('blazix.primitives.roving')] = true;
  document.addEventListener('keydown', (event) => {
    if (event.defaultPrevented || event.altKey || event.ctrlKey || event.metaKey) return;
    const target = /** @type {HTMLElement} */ (event.target);
    const from = /** @type {HTMLElement | null} */ (target.closest?.('[data-blazix-roving-item]'));
    if (!from) return;
    // A popup rendered inside an item handles its own keys.
    const host = target.closest('[data-blazix-popup-host]');
    if (host && from.contains(host)) return;
    const group = /** @type {HTMLElement | null} */ (from.parentElement?.closest('[data-blazix-roving]'));
    if (!group) return;
    const rtl = isRTL(group);
    if (fieldKeepsKey(event, target, rtl)) return;
    const next = rovingTarget(event, group, from);
    if (next === undefined) return;
    event.preventDefault();
    if (!next || next === from) return;
    next.focus();
    if (group.hasAttribute('data-blazix-activate') && canFocus(next) && !next.hasAttribute('data-disabled')) next.click();
  });
}

export function ready() {
  return true;
}
