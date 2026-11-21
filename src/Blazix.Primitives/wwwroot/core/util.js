// @ts-check
//
// One-off reads Blazor can't do itself.

/**
 * An image's loading status, for an <img> that may have loaded (or failed) before Blazor attached
 * its load/error handlers (prerendered pages, cached images).
 * @param {string} id
 * @returns {'loading' | 'loaded' | 'error' | null}
 */
export function imageStatus(id) {
  const el = document.getElementById(id);
  if (!(el instanceof HTMLImageElement)) return null;
  if (!el.complete) return 'loading';
  return el.naturalWidth > 0 ? 'loaded' : 'error';
}

/**
 * Validity of a form control (constraint validation), for Field.
 * @param {string} id
 */
export function validity(id) {
  const el = /** @type {HTMLInputElement | null} */ (document.getElementById(id));
  if (!el || !('validity' in el)) return null;
  const v = el.validity;
  return {
    valid: v.valid,
    valueMissing: v.valueMissing,
    typeMismatch: v.typeMismatch,
    patternMismatch: v.patternMismatch,
    tooLong: v.tooLong,
    tooShort: v.tooShort,
    rangeUnderflow: v.rangeUnderflow,
    rangeOverflow: v.rangeOverflow,
    stepMismatch: v.stepMismatch,
    badInput: v.badInput,
    customError: v.customError,
    validationMessage: el.validationMessage,
    value: el.value,
  };
}

/**
 * Sets (or clears, with an empty message) a control's custom validity message.
 * @param {string} id @param {string} message
 */
export function setCustomValidity(id, message) {
  const el = /** @type {HTMLInputElement | null} */ (document.getElementById(id));
  el?.setCustomValidity?.(message);
}

/** @param {string} id */
export function focusElement(id) {
  document.getElementById(id)?.focus({ preventScroll: false });
}

/**
 * Sets an input's value from code, for a programmatic change whose rendered value attribute
 * wouldn't change (so Blazor's render wouldn't touch the DOM).
 * @param {string} id @param {string} value
 */
export function setValue(id, value) {
  const el = /** @type {HTMLInputElement | null} */ (document.getElementById(id));
  if (el && el.value !== value) el.value = value;
}

/**
 * Reports whether a media query matches, now and whenever that changes (MediaQuery).
 * @param {import('./dom.js').DotNetRef} dotnet
 * @param {string} query
 */
export function watchMediaQuery(dotnet, query) {
  const list = window.matchMedia(query);
  const report = () => dotnet.invokeMethodAsync('Changed', list.matches).catch(() => {});
  list.addEventListener('change', report);
  report();
  return { dispose: () => list.removeEventListener('change', report) };
}

/**
 * Calls .NET when a key is pressed with Ctrl or ⌘ (KeyboardShortcut), anywhere on the page.
 * @param {import('./dom.js').DotNetRef} dotnet
 * @param {string} key
 * @param {{ ctrlOrMeta?: boolean, shift?: boolean, alt?: boolean, preventDefault?: boolean }} options
 */
export function watchShortcut(dotnet, key, options) {
  /** @param {KeyboardEvent} event */
  const onKeyDown = (event) => {
    if (event.key.toLowerCase() !== key.toLowerCase()) return;
    if ((options.ctrlOrMeta ?? true) !== (event.ctrlKey || event.metaKey)) return;
    if (!!options.shift !== event.shiftKey || !!options.alt !== event.altKey) return;
    if (options.preventDefault !== false) event.preventDefault();
    dotnet.invokeMethodAsync('Pressed').catch(() => {});
  };
  window.addEventListener('keydown', onKeyDown);
  return { dispose: () => window.removeEventListener('keydown', onKeyDown) };
}

/**
 * Sets a cookie for the whole site (BrowserCookies.SetAsync), e.g. to remember a sidebar's state.
 * @param {string} name
 * @param {string} value
 * @param {number} maxAge seconds
 */
export function setCookie(name, value, maxAge) {
  document.cookie = `${encodeURIComponent(name)}=${encodeURIComponent(value)}; path=/; max-age=${maxAge}; samesite=lax`;
}

/**
 * Copies text to the clipboard (BrowserClipboard.CopyAsync).
 * @param {string} text
 * @returns {Promise<boolean>}
 */
export async function copyText(text) {
  try {
    await navigator.clipboard.writeText(text);
    return true;
  } catch {
    return false;
  }
}
