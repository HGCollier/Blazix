// @ts-check
//
// Buttons that aren't <button> elements (a part's Render put the attributes on a <div>, <span> or
// <a>) still need to act like one: Enter activates on keydown, Space on keyup (like native buttons),
// and a disabled one does neither. Blazor marks them with data-blazix-button; one delegated listener
// handles every one of them, so nothing has to bind per element.

const key = Symbol.for('blazix.primitives.button');
if (!(/** @type {any} */ (globalThis)[key])) {
  /** @type {any} */ (globalThis)[key] = true;

  /** @param {KeyboardEvent} event */
  const buttonOf = (event) => {
    const el = /** @type {HTMLElement} */ (event.target);
    if (!el?.hasAttribute?.('data-blazix-button') || el instanceof HTMLButtonElement) return null;
    return el;
  };

  document.addEventListener('keydown', (event) => {
    const el = buttonOf(event);
    if (!el || event.defaultPrevented || event.repeat && event.key === ' ') return;
    const disabled = el.getAttribute('aria-disabled') === 'true';
    const isLink = el instanceof HTMLAnchorElement && el.hasAttribute('href');
    if (event.key === ' ') {
      // Stop the page scrolling; activation happens on keyup.
      event.preventDefault();
    } else if (event.key === 'Enter' && !isLink) {
      event.preventDefault();
      if (!disabled) el.click();
    } else if (event.key === 'Enter' && disabled) {
      event.preventDefault();
    }
  });

  document.addEventListener('keyup', (event) => {
    const el = buttonOf(event);
    if (!el || event.key !== ' ' || event.defaultPrevented) return;
    if (el.getAttribute('aria-disabled') !== 'true') el.click();
  });

  // Checkbox, switch and radio roots (a <span> with a hidden <input> beside it): Space checks, like a
  // native checkbox; Enter submits the form, like implicit submission from a native checkbox.
  /** @param {Event} event */
  const checkableOf = (event) => {
    const el = /** @type {HTMLElement} */ (event.target);
    return el?.hasAttribute?.('data-blazix-checkable') ? el : null;
  };
  document.addEventListener('keydown', (event) => {
    const el = checkableOf(event);
    if (!el || event.defaultPrevented) return;
    if (event.key === ' ') {
      event.preventDefault();
    } else if (event.key === 'Enter') {
      event.preventDefault();
      const input = /** @type {HTMLInputElement | null} */ (el.nextElementSibling instanceof HTMLInputElement ? el.nextElementSibling : null);
      const form = input?.form ?? el.closest('form');
      form?.requestSubmit();
    }
  });
  document.addEventListener('keyup', (event) => {
    const el = checkableOf(event);
    if (!el || event.key !== ' ' || event.defaultPrevented) return;
    if (el.getAttribute('aria-disabled') !== 'true') el.click();
  });
  // A click on the root inside a <label> would also click the hidden input the label points at,
  // toggling twice. The root handles its own clicks, so stop the label from forwarding this one.
  document.addEventListener('click', (event) => {
    const root = /** @type {Element} */ (event.target).closest?.('[data-blazix-checkable]');
    if (root && root.closest('label')) event.preventDefault();
  });
  // A <label for="..."> click focuses the hidden input it points at (and clicks it, which toggles the
  // control). Move that focus to the visible root.
  document.addEventListener('focusin', (event) => {
    const rootId = /** @type {Element} */ (event.target).getAttribute?.('data-blazix-checkable-input');
    if (rootId) document.getElementById(rootId)?.focus();
  });
}

let labelCount = 0;

/**
 * The ids of an element's <label>s: a <label for> pointing at it and a <label> around it. A label without an id gets
 * one. Names what a label can't reach: a checkbox, switch or radio's visible root (a <span>, which no <label> can
 * label; Firefox won't follow aria-labelledby to its aria-hidden input's labels), a select's list of options, and a
 * slider's thumbs (a <label for> at the slider's root).
 * @param {string} id an element's id: a labelable one (<input>, <button>, <select>...) or one a <label for> points at
 * @returns {string[]}
 */
export function labelIds(id) {
  const el = /** @type {HTMLElement & { labels?: NodeListOf<HTMLLabelElement> | null }} */ (document.getElementById(id));
  if (!el) return [];
  // An element no <label> can label (a slider's group) still has the labels that point at it with for="...".
  const labels = el.labels ?? document.querySelectorAll(`label[for="${CSS.escape(id)}"]`);
  return [.../** @type {Iterable<HTMLLabelElement>} */ (labels)].map((label) => {
    if (!label.id) label.id = `${id}-label-${++labelCount}`;
    return label.id;
  });
}

/**
 * Is the element with this id a real <button>? Lets a part work out NativeButton by itself.
 * @param {string} id
 * @param {boolean | null} declared what the part was told, if anything
 * @param {string} part the part's name, for the warning
 */
export function isNativeButton(id, declared, part) {
  const el = document.getElementById(id);
  if (!el) return null;
  const native = el instanceof HTMLButtonElement;
  if (declared != null && declared !== native) {
    console.warn(`Blazix.Primitives: ${part} has NativeButton="${declared}" but renders a <${el.localName}>. Remove NativeButton to have it detected, or correct it.`);
  }
  return native;
}
