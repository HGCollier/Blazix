// @ts-check
//
// The DOM side of the registry's Command (a port of cmdk): key handling that must preventDefault
// synchronously, pointer selection without a server round trip per pointermove, reading items'
// text (their default value), and scrolling the selected item into view.

const ITEM = '[cmdk-item]';

/**
 * @param {HTMLElement} root The [cmdk-root] element.
 * @param {any} dotnet A DotNetObjectReference to the Command.
 * @param {{ vimBindings: boolean, disablePointerSelection: boolean }} options
 */
export function init(root, dotnet, options) {
  let opts = options;
  /** @type {string | null} */
  let pending = null;

  /** @param {KeyboardEvent} e */
  const onKeyDown = (e) => {
    // Let IME composition finish before acting on keys (keyCode 229: legacy CJK IMEs).
    if (e.defaultPrevented || e.isComposing || e.keyCode === 229) return;
    let action = null;
    switch (e.key) {
      case 'n':
      case 'j':
        if (opts.vimBindings && e.ctrlKey) action = 'next';
        break;
      case 'ArrowDown':
        action = 'next';
        break;
      case 'p':
      case 'k':
        if (opts.vimBindings && e.ctrlKey) action = 'prev';
        break;
      case 'ArrowUp':
        action = 'prev';
        break;
      case 'Home':
        action = 'first';
        break;
      case 'End':
        action = 'last';
        break;
      case 'Enter':
        action = 'enter';
        break;
    }
    if (!action) return;
    e.preventDefault();
    dotnet.invokeMethodAsync('OnKeyAsync', action, e.metaKey, e.altKey).catch(() => {});
  };

  /** @param {PointerEvent} e */
  const onPointerMove = (e) => {
    if (opts.disablePointerSelection || !(e.target instanceof Element)) return;
    const item = e.target.closest(ITEM);
    if (!item || !root.contains(item) || item.id === pending) return;
    if (item.getAttribute('aria-disabled') === 'true' || item.getAttribute('aria-selected') === 'true') return;
    pending = item.id;
    dotnet.invokeMethodAsync('OnPointerAsync', item.id).catch(() => {}).finally(() => {
      pending = null;
    });
  };

  root.addEventListener('keydown', onKeyDown);
  root.addEventListener('pointermove', onPointerMove);
  return {
    /** @param {{ vimBindings: boolean, disablePointerSelection: boolean }} o */
    update(o) {
      opts = o;
    },
    dispose() {
      root.removeEventListener('keydown', onKeyDown);
      root.removeEventListener('pointermove', onPointerMove);
    },
  };
}

/**
 * The items and groups in document order, with each item's text (cmdk's default item value).
 * @param {HTMLElement} root
 * @returns {(string | null)[][]}
 */
export function read(root) {
  return Array.from(root.querySelectorAll('[cmdk-item],[cmdk-group]'), (el) => [
    el.id,
    el.hasAttribute('cmdk-item') ? text(el) : null,
  ]);
}

// The item's textContent as JSX would have it: whitespace-only text spanning lines (markup indentation) is dropped.
function text(el) {
  const walker = document.createTreeWalker(el, NodeFilter.SHOW_TEXT);
  let value = '';
  for (let node = walker.nextNode(); node; node = walker.nextNode()) {
    if (!/^\s*\n\s*$/.test(node.data)) value += node.data;
  }
  return value.trim();
}

/** @param {HTMLElement} root */
export function scrollSelectedIntoView(root) {
  const item = root.querySelector(`${ITEM}[aria-selected="true"]`);
  if (!item) return;
  // Items are ranked with CSS `order`, so "first in its group" is the topmost, not the first child.
  const parent = item.parentElement;
  if (parent && item.getBoundingClientRect().top - parent.getBoundingClientRect().top < 1) {
    item.closest('[cmdk-group]')?.querySelector('[cmdk-group-heading]')?.scrollIntoView({ block: 'nearest' });
  }
  item.scrollIntoView({ block: 'nearest' });
}
