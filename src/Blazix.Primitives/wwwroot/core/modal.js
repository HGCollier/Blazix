// @ts-check
//
// The page-wide side effects of modal popups (after UI SDK's popups/modal): the scroll lock and
// making the rest of the page inert. Several modals, and other libraries, can hold them at once, so
// holders are counted and only what was changed is put back, one style property at a time.

import { persistentLayers } from './layers.js';

const shared = /** @type {{ locks: number, restore: (() => void) | null, inert: WeakMap<Element, number> }} */ (
  /** @type {any} */ (globalThis)[Symbol.for('blazix.primitives.modal')] ??= { locks: 0, restore: null, inert: new WeakMap() });

/** @param {HTMLElement} el @param {string} name @param {string} value */
function setStyle(el, name, value) {
  const before = el.style.getPropertyValue(name);
  const priority = el.style.getPropertyPriority(name);
  el.style.setProperty(name, value);
  return () => {
    // Someone else changed it since: leave theirs.
    if (el.style.getPropertyValue(name) !== value) return;
    if (before) el.style.setProperty(name, before, priority);
    else el.style.removeProperty(name);
  };
}

function freeze() {
  const root = document.documentElement;
  const body = document.body;
  // The root scrolls most pages; a body with its own overflow under a clipping root scrolls the rest.
  const scroller = getComputedStyle(root).overflowY !== 'visible' && /auto|scroll/.test(getComputedStyle(body).overflowY) ? body : root;
  const gutter = scroller === root ? window.innerWidth - root.clientWidth : scroller.offsetWidth - scroller.clientWidth;
  const restores = [
    setStyle(scroller, 'overflow', 'hidden'),
    // Smooth scrolling would animate when the lock lifts.
    setStyle(scroller, 'scroll-behavior', 'auto'),
    // A public styling hook: fixed elements, like a header, can pad themselves by it.
    setStyle(root, '--scrollbar-width', `${Math.max(0, gutter)}px`),
  ];
  if (gutter > 0) {
    // A classic scrollbar takes room, so keep its gutter and the page doesn't shift.
    if (CSS.supports('scrollbar-gutter', 'stable')) restores.push(setStyle(scroller, 'scrollbar-gutter', 'stable'));
    else restores.push(setStyle(scroller, 'padding-right', `${(parseFloat(getComputedStyle(scroller).paddingRight) || 0) + gutter}px`));
  }
  return () => restores.forEach((restore) => restore());
}

/** Whether something holds the scroll lock now. */
export const scrollLocked = () => shared.locks > 0;

/** Stops the page from scrolling. Returns a release that's safe to call twice. */
export function lockScroll() {
  if (shared.locks === 0) shared.restore = freeze();
  shared.locks += 1;
  let released = false;
  return () => {
    if (released) return;
    released = true;
    shared.locks -= 1;
    if (shared.locks === 0) {
      shared.restore?.();
      shared.restore = null;
    }
  };
}

const liveRegions = '[aria-live]:not([aria-live="off"]),[role="status"],[role="alert"],[role="log"],output';

/** @param {Node} node */
const parentOf = (node) => {
  const parent = node.parentNode;
  return parent instanceof ShadowRoot ? parent.host : parent;
};

/** @param {Element[]} keep */
function applyInert(keep) {
  const all = [...keep, ...document.querySelectorAll(liveRegions), ...persistentLayers()];
  const targets = all.filter((el) => !all.some((other) => other !== el && other.contains(el)));
  /** @type {Set<Node>} */
  const keepSet = new Set();
  for (const el of targets) for (let node = /** @type {Node | null} */ (el); node; node = parentOf(node)) keepSet.add(node);
  /** @type {HTMLElement[]} */
  const changed = [];
  for (const el of targets) {
    for (let node = /** @type {Node | null} */ (el); node && node !== document.body; node = parentOf(node)) {
      for (const sibling of node.parentNode?.children ?? []) {
        if (keepSet.has(sibling) || !(sibling instanceof HTMLElement) || sibling.tagName === 'SCRIPT') continue;
        const count = shared.inert.get(sibling) ?? (sibling.inert ? Infinity : 0);
        if (count === 0) sibling.inert = true;
        if (count !== Infinity) shared.inert.set(sibling, count + 1);
        changed.push(sibling);
      }
    }
  }
  return () => {
    for (const el of changed) {
      const count = shared.inert.get(el);
      if (count === undefined) continue;
      if (count <= 1) {
        el.inert = false;
        shared.inert.delete(el);
      } else {
        shared.inert.set(el, count - 1);
      }
    }
  };
}

/**
 * Makes everything inert except the given elements, their ancestors and live regions. Works without
 * portals: the siblings along each ancestor path become inert. Returns a release, with `keep(el)`
 * to exempt another element later (a child popup).
 * @param {HTMLElement[]} keep
 */
export function inertOutside(keep) {
  /** @type {Element[]} */
  let current = keep.filter(Boolean);
  let release = applyInert(current);
  let done = false;
  /** @param {Element[]} next */
  const swap = (next) => {
    const previous = release;
    current = next;
    release = applyInert(next);
    previous();
  };
  return Object.assign(() => {
    if (done) return;
    done = true;
    release();
  }, {
    /** @param {HTMLElement} el */
    keep(el) {
      if (done) return () => {};
      swap([...current, el]);
      return () => {
        if (!done) swap(current.filter((node) => node !== el));
      };
    },
  });
}
