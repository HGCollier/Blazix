// @ts-check
//
// Small DOM helpers every behavior module shares. Plain ES module: no bundler, no build step.

/**
 * @typedef {{ invokeMethodAsync(method: string, ...args: unknown[]): Promise<unknown> }} DotNetRef
 */

/**
 * Adds a listener and returns the function that removes it.
 * @template {Event} E
 * @param {EventTarget | null | undefined} target
 * @param {string} type
 * @param {(event: E) => void} listener
 * @param {AddEventListenerOptions | boolean} [options]
 */
export function on(target, type, listener, options) {
  if (!target) return () => {};
  const fn = /** @type {EventListener} */ (/** @type {unknown} */ (listener));
  target.addEventListener(type, fn, options);
  return () => target.removeEventListener(type, fn, options);
}

/** @param {string | null | undefined} id */
export function byId(id) {
  return id ? /** @type {HTMLElement | null} */ (document.getElementById(id)) : null;
}

/**
 * Calls .NET and ignores a circuit that went away (Blazor Server) or a disposed reference.
 * @param {DotNetRef | null | undefined} dotnet
 * @param {string} method
 * @param {...unknown} args
 */
export function call(dotnet, method, ...args) {
  dotnet?.invokeMethodAsync(method, ...args).catch(() => {});
}

/** Disabled as far as interaction goes: native `disabled`, or `aria-disabled="true"`. */
export function isDisabled(/** @type {Element} */ el) {
  return el.matches(':disabled') || el.getAttribute('aria-disabled') === 'true';
}

/** @param {Element} el */
export function isRTL(el) {
  return getComputedStyle(el).direction === 'rtl';
}

/** The focused element, looking into open shadow roots. */
export function activeElement() {
  let active = document.activeElement;
  while (active?.shadowRoot?.activeElement) active = active.shadowRoot.activeElement;
  return /** @type {HTMLElement | null} */ (active);
}

/** Whether `node` is `root` or inside it, crossing shadow roots. */
export function contains(/** @type {Node | null | undefined} */ root, /** @type {Node | null | undefined} */ node) {
  for (let at = node; at; at = at.parentNode ?? /** @type {ShadowRoot} */ (at).host ?? null) {
    if (at === root) return true;
  }
  return false;
}

/** @param {HTMLElement | null | undefined} el */
export function focus(el, preventScroll = true) {
  if (!el) return false;
  el.focus({ preventScroll });
  return activeElement() === el;
}

/**
 * Whether a key event is part of an input method composition (e.g. a Japanese candidate list).
 * Safari ends a composition with isComposing false, so key code 229 counts too.
 * @param {KeyboardEvent} event
 */
export function isComposing(event) {
  return event.isComposing || event.keyCode === 229;
}

/** One printable character, without a shortcut modifier. */
export function isTypeaheadKey(/** @type {KeyboardEvent} */ event) {
  return event.key.length === 1 && !event.ctrlKey && !event.metaKey && !event.altKey;
}

// Browsers fire synthetic pointer events when content shifts under a resting cursor (e.g. Blazor
// inserts or removes an item). Only real movement may take focus from the keyboard, so remember
// where the pointer was. This capture listener runs before any component's handlers.
const pointer = { x: NaN, y: NaN, moved: false, type: 'mouse' };
document.addEventListener('pointermove', (event) => {
  pointer.moved = event.clientX !== pointer.x || event.clientY !== pointer.y;
  pointer.x = event.clientX;
  pointer.y = event.clientY;
  pointer.type = event.pointerType;
}, { capture: true, passive: true });
document.addEventListener('pointerdown', (event) => {
  pointer.type = event.pointerType;
}, { capture: true, passive: true });

/** The last pointer position, and whether the last pointermove was a real move. */
export function lastPointer() {
  return pointer;
}

// Whether the most recent input was the keyboard. Decides focus-visible-like behavior,
// e.g. whether opening a popup focuses its first item.
let keyboardModality = false;
document.addEventListener('keydown', (event) => {
  if (!event.metaKey && !event.ctrlKey && !event.altKey) keyboardModality = true;
}, { capture: true, passive: true });
document.addEventListener('pointerdown', () => { keyboardModality = false; }, { capture: true, passive: true });

export function usingKeyboard() {
  return keyboardModality;
}

/**
 * Text a user would type to find an element, skipping hidden parts like shortcuts
 * marked aria-hidden, and parts that aren't shown.
 * @param {Element} el
 */
export function textOf(el) {
  let text = '';
  const walker = document.createTreeWalker(el, NodeFilter.SHOW_TEXT);
  while (walker.nextNode()) {
    const node = walker.currentNode;
    const hidden = node.parentElement?.closest('[aria-hidden="true"], [hidden]');
    if (hidden && el.contains(hidden) && (hidden !== el || hidden.getAttribute('aria-hidden') === 'true')) continue;
    text += node.textContent;
  }
  return text;
}

/**
 * What a user would type to find an item: its `data-label`, else its visible text, else its aria-label.
 * @param {HTMLElement} el
 */
export function labelOf(el) {
  if (el.dataset.label) return el.dataset.label;
  const text = textOf(el).replace(/\s+/g, ' ').trim();
  return text || (el.getAttribute('aria-label') ?? '');
}

/**
 * Elements matching `selector` inside `root`, skipping those that belong to a nested `scope`
 * (another instance of the same component inside this one).
 * @param {ParentNode} root
 * @param {string} selector
 * @param {string} [scope] selector of the owning container, e.g. '[role="menu"]'
 * @returns {HTMLElement[]}
 */
export function ownElements(root, selector, scope) {
  const all = /** @type {HTMLElement[]} */ ([...root.querySelectorAll(selector)]);
  if (!scope) return all;
  return all.filter((el) => el.parentElement?.closest(scope) === root);
}

/** @param {number} value @param {number} min @param {number} max */
export const clamp = (value, min, max) => Math.min(Math.max(value, min), Math.max(min, max));
