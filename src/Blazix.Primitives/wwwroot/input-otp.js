// @ts-check
//
// The browser side of InputOTP, ported from the input-otp library (guilhermerodz/input-otp):
// one transparent input laid over the slots. It mirrors the input's selection onto the slots
// (data-active and the fake caret), snaps a lone caret onto a character so it reads as a slot selection,
// rejects input that fails the input's pattern, and fixes iOS paste. Blazor owns the value (through the
// input event) and renders each slot's character.

import { isRTL, on } from './core/dom.js';

/** @param {CSSStyleSheet} sheet @param {string} rule */
function safeInsertRule(sheet, rule) {
  try {
    sheet.insertRule(rule);
  } catch {
    console.warn('input-otp could not insert CSS rule:', rule);
  }
}

function ensureStyles() {
  if (document.getElementById('input-otp-style')) return;
  const styleEl = document.createElement('style');
  styleEl.id = 'input-otp-style';
  document.head.appendChild(styleEl);
  const sheet = styleEl.sheet;
  if (!sheet) return;
  const autofill =
    'background: transparent !important; color: transparent !important; border-color: transparent !important; opacity: 0 !important; box-shadow: none !important; -webkit-box-shadow: none !important; -webkit-text-fill-color: transparent !important;';
  safeInsertRule(sheet, '[data-input-otp]::selection { background: transparent !important; color: transparent !important; }');
  safeInsertRule(sheet, `[data-input-otp]:autofill { ${autofill} }`);
  safeInsertRule(sheet, `[data-input-otp]:-webkit-autofill { ${autofill} }`);
  safeInsertRule(
    sheet,
    '@supports (-webkit-touch-callout: none) { [data-input-otp] { letter-spacing: -.6em !important; font-weight: 100 !important; font-stretch: ultra-condensed; font-optical-sizing: none !important; left: -1px !important; right: 1px !important; } }',
  );
  safeInsertRule(sheet, '[data-input-otp] + * { pointer-events: all !important; }');
}

/**
 * @param {HTMLInputElement} input
 * @param {HTMLElement} container
 */
export function createInputOtp(input, container) {
  ensureStyles();
  const isIOS = window.CSS?.supports?.('-webkit-touch-callout', 'none') ?? false;
  const regexp = () => {
    const pattern = input.getAttribute('pattern');
    return pattern ? new RegExp(pattern) : null;
  };

  let focused = document.activeElement === input;
  /** @type {number | null} */ let mss = null;
  /** @type {number | null} */ let mse = null;
  /** @type {[number | null, number | null, 'forward' | 'backward' | 'none' | null]} */
  let prev = [input.selectionStart, input.selectionEnd, input.selectionDirection];
  /** The value and selection before the current edit, to put back when the pattern rejects it. */
  let before = { value: input.value, start: input.selectionStart, end: input.selectionEnd };
  /** @type {number[]} */ let timeouts = [];

  /** The slots' state, as input-otp's context computes it. */
  function paint() {
    const value = input.value;
    if (mss === null) input.removeAttribute('data-input-otp-mss');
    else input.setAttribute('data-input-otp-mss', String(mss));
    if (mse === null) input.removeAttribute('data-input-otp-mse');
    else input.setAttribute('data-input-otp-mse', String(mse));
    for (const slot of /** @type {NodeListOf<HTMLElement>} */ (container.querySelectorAll('[data-slot="input-otp-slot"]'))) {
      const index = Number(slot.dataset.index);
      const active =
        focused && mss !== null && mse !== null && ((mss === mse && index === mss) || (index >= mss && index < mse));
      slot.dataset.active = String(active);
      const caret = /** @type {HTMLElement | null} */ (slot.querySelector(':scope > [data-input-otp-caret]'));
      if (caret) caret.hidden = !(active && value[index] === undefined);
    }
  }

  /** @param {number | null} s @param {number | null} e */
  function mirror(s, e) {
    mss = s;
    mse = e;
    paint();
  }

  function onSelectionChange() {
    if (document.activeElement !== input) {
      mirror(null, null);
      return;
    }
    const s = input.selectionStart;
    const e = input.selectionEnd;
    const dir = input.selectionDirection;
    const ml = input.maxLength;
    const val = input.value;

    let start = -1;
    let end = -1;
    /** @type {'forward' | 'backward' | 'none' | undefined} */
    let direction;
    if (val.length !== 0 && s !== null && e !== null) {
      const isSingleCaret = s === e;
      const isInsertMode = s === val.length && val.length < ml;
      if (isSingleCaret && !isInsertMode) {
        const c = s;
        if (c === 0) {
          start = 0;
          end = 1;
          direction = 'forward';
        } else if (c === ml) {
          start = c - 1;
          end = c;
          direction = 'backward';
        } else if (ml > 1 && val.length > 1) {
          let offset = 0;
          if (prev[0] !== null && prev[1] !== null) {
            direction = c < prev[1] ? 'backward' : 'forward';
            const wasPreviouslyInserting = prev[0] === prev[1] && prev[0] < ml;
            if (direction === 'backward' && !wasPreviouslyInserting) offset = -1;
          }
          start = offset + c;
          end = offset + c + 1;
        }
      }
      if (start !== -1 && end !== -1 && start !== end) input.setSelectionRange(start, end, direction);
    }

    const ns = start !== -1 ? start : s;
    const ne = end !== -1 ? end : e;
    prev = [ns, ne, direction ?? dir];
    mirror(ns, ne);
  }

  /** input-otp re-reads the selection a few times after the value or focus changes. */
  function syncSoon() {
    timeouts.forEach(clearTimeout);
    timeouts = [0, 10, 50].map((delay) =>
      window.setTimeout(() => {
        const s = input.selectionStart;
        const e = input.selectionEnd;
        if (s !== null && e !== null) {
          prev = [s, e, input.selectionDirection];
          mirror(s, e);
        }
      }, delay),
    );
  }

  function updateRootHeight() {
    container.style.setProperty('--root-height', `${input.clientHeight}px`);
  }
  updateRootHeight();
  const resizeObserver = typeof ResizeObserver === 'undefined' ? null : new ResizeObserver(updateRootHeight);
  resizeObserver?.observe(input);

  const offs = [
    on(document, 'selectionchange', onSelectionChange, { capture: true }),
    // The input's characters read left to right as a number does, while the slots follow the text direction. In
    // right-to-left the caret would run against the slots, so Left and Right move it here: Left on to the next
    // slot, as Right does in left-to-right (collapsed where the native key would leave it; selectionchange
    // then picks the slot).
    on(input, 'keydown', (/** @type {KeyboardEvent} */ event) => {
      if (event.key !== 'ArrowLeft' && event.key !== 'ArrowRight') return;
      if (event.shiftKey || event.altKey || event.ctrlKey || event.metaKey || !isRTL(container)) return;
      event.preventDefault();
      const forward = event.key === 'ArrowLeft';
      const start = input.selectionStart ?? 0;
      const end = input.selectionEnd ?? start;
      const caret = start !== end
        ? (forward ? end : start)
        : Math.max(0, Math.min(input.value.length, start + (forward ? 1 : -1)));
      input.setSelectionRange(caret, caret);
    }),
    on(input, 'focus', () => {
      const start = Math.min(input.value.length, input.maxLength - 1);
      const end = input.value.length;
      input.setSelectionRange(start, end);
      focused = true;
      mirror(start, end);
      syncSoon();
    }),
    on(input, 'blur', () => {
      focused = false;
      paint();
      syncSoon();
    }),
    on(input, 'beforeinput', () => {
      before = { value: input.value, start: input.selectionStart, end: input.selectionEnd };
    }),
    // On the input itself, so a rejected edit never reaches Blazor's (delegated) oninput.
    on(input, 'input', (/** @type {Event} */ event) => {
      const value = input.value.slice(0, input.maxLength);
      const re = regexp();
      if (value.length > 0 && re && !re.test(value)) {
        event.stopImmediatePropagation();
        input.value = before.value;
        if (before.start !== null && before.end !== null) input.setSelectionRange(before.start, before.end);
        return;
      }
      if (value !== input.value) input.value = value;
      // Cutting or deleting doesn't fire selectionchange.
      if (value.length < before.value.length) document.dispatchEvent(new Event('selectionchange'));
      before = { value, start: input.selectionStart, end: input.selectionEnd };
      paint();
      syncSoon();
    }),
    // Fix iOS pasting.
    on(input, 'paste', (/** @type {ClipboardEvent} */ event) => {
      if (!isIOS || !event.clipboardData) return;
      const content = event.clipboardData.getData('text/plain');
      event.preventDefault();
      const value = input.value;
      const start = input.selectionStart ?? value.length;
      const end = input.selectionEnd ?? start;
      const next = (value.slice(0, start) + content + value.slice(end)).slice(0, input.maxLength);
      const re = regexp();
      if (next.length > 0 && re && !re.test(next)) return;
      before = { value, start, end };
      input.value = next;
      input.dispatchEvent(new Event('input', { bubbles: true }));
      const s = Math.min(next.length, input.maxLength - 1);
      input.setSelectionRange(s, next.length);
      mirror(s, next.length);
    }),
  ];

  onSelectionChange();
  if (document.activeElement === input) focused = true;
  paint();

  return {
    /** Re-mirrors the slots after Blazor set the value (a controlled change). */
    sync() {
      before = { value: input.value, start: input.selectionStart, end: input.selectionEnd };
      paint();
      syncSoon();
    },
    dispose() {
      offs.forEach((off) => off());
      timeouts.forEach(clearTimeout);
      resizeObserver?.disconnect();
    },
  };
}
