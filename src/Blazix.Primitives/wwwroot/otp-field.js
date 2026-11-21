// @ts-check
//
// The browser side of OTP Field: one input per character. Typing fills the slot and moves on,
// Backspace clears and moves back, arrows move, and a paste (or the browser's one-time-code autofill,
// which types the whole code into the first slot) is spread across the slots from where it lands.
// Blazor owns the value; this writes the slots at once and reports the new value.

import { byId, call, on } from './core/dom.js';

/** @typedef {{ length: number, pattern: string, disabled: boolean, readOnly: boolean }} OtpOptions */

/**
 * @param {import('./core/dom.js').DotNetRef} dotnet
 * @param {string} rootId
 * @param {OtpOptions} initial
 */
export function createOtpField(dotnet, rootId, initial) {
  let options = initial;
  const root = () => byId(rootId);
  const slots = () => /** @type {HTMLInputElement[]} */ ([...(root()?.querySelectorAll('[data-blazix-otp-input]') ?? [])]);
  const allowed = (/** @type {string} */ c) => !options.pattern || new RegExp(options.pattern).test(c);
  const sanitize = (/** @type {string} */ text) => [...text.replace(/\s/g, '')].filter(allowed).join('');
  const value = () => slots().map((slot) => slot.value.slice(0, 1)).join('');
  const blocked = () => options.disabled || options.readOnly;

  /** @param {number} index */
  function focusSlot(index) {
    const list = slots();
    const slot = list[Math.max(0, Math.min(list.length - 1, index))];
    if (!slot) return;
    slot.focus();
    slot.select();
  }

  /** Writes `text` into the slots starting at `index`, reports it, and focuses the next slot. */
  function fill(/** @type {number} */ index, /** @type {string} */ text, /** @type {string} */ reason) {
    const list = slots();
    const chars = [...sanitize(text)].slice(0, Math.max(0, options.length - index));
    if (!chars.length) {
      // Nothing allowed was typed: put the slot back as it was.
      call(dotnet, 'SetValue', value(), 'none');
      return;
    }
    chars.forEach((char, offset) => {
      const slot = list[index + offset];
      if (slot) slot.value = char;
    });
    // Keep the value contiguous: what's left of a gap moves up.
    const next = value();
    call(dotnet, 'SetValue', next, reason);
    focusSlot(Math.min(index + chars.length, options.length - 1));
  }

  /** @param {Event} event */
  const slotOf = (event) => {
    const el = /** @type {HTMLInputElement} */ (event.target);
    if (!el?.matches?.('[data-blazix-otp-input]') || el.closest('[data-blazix-otp-field]') !== root()) return -1;
    return slots().indexOf(el);
  };

  const offs = [
    on(document, 'beforeinput', (/** @type {InputEvent} */ event) => {
      const index = slotOf(event);
      if (index < 0) return;
      if (blocked()) {
        event.preventDefault();
        return;
      }
      if (event.inputType.startsWith('delete')) {
        event.preventDefault();
        const list = slots();
        list[index].value = '';
        call(dotnet, 'SetValue', value(), 'input-clear');
        return;
      }
      const data = event.data ?? event.dataTransfer?.getData('text') ?? '';
      if (!data) return;
      event.preventDefault();
      fill(index, data, data.length > 1 ? 'input-paste' : 'input-change');
    }),
    on(document, 'paste', (/** @type {ClipboardEvent} */ event) => {
      const index = slotOf(event);
      if (index < 0 || blocked()) return;
      event.preventDefault();
      fill(index, event.clipboardData?.getData('text') ?? '', 'input-paste');
    }),
    on(document, 'keydown', (/** @type {KeyboardEvent} */ event) => {
      const index = slotOf(event);
      if (index < 0 || event.ctrlKey || event.metaKey || event.altKey) return;
      const list = slots();
      switch (event.key) {
        case 'Backspace':
          event.preventDefault();
          if (blocked()) return;
          if (list[index].value) {
            list[index].value = '';
          } else if (index > 0) {
            list[index - 1].value = '';
            focusSlot(index - 1);
          }
          call(dotnet, 'SetValue', value(), 'input-clear');
          return;
        case 'Delete':
          event.preventDefault();
          if (blocked()) return;
          list[index].value = '';
          call(dotnet, 'SetValue', value(), 'input-clear');
          return;
        case 'ArrowLeft':
          event.preventDefault();
          focusSlot(index - 1);
          return;
        case 'ArrowRight':
          event.preventDefault();
          focusSlot(index + 1);
          return;
        case 'Home':
          event.preventDefault();
          focusSlot(0);
          return;
        case 'End':
          event.preventDefault();
          focusSlot(list.length - 1);
          return;
        default:
          // Typing the character a slot already holds still moves on.
          if (event.key.length === 1 && list[index].value === event.key && !blocked()) {
            event.preventDefault();
            focusSlot(index + 1);
          }
      }
    }),
    on(document, 'focusin', (/** @type {FocusEvent} */ event) => {
      const index = slotOf(event);
      if (index < 0) return;
      // Focus goes to the first empty slot rather than past a gap.
      const list = slots();
      const firstEmpty = list.findIndex((slot) => !slot.value);
      if (firstEmpty >= 0 && index > firstEmpty) {
        focusSlot(firstEmpty);
        return;
      }
      list[index].select();
    }),
  ];

  return {
    /** @param {OtpOptions} next */
    update(next) {
      options = next;
    },
    /** Submits the field's form, for autoSubmit once the code is complete. */
    submit() {
      const form = root()?.closest('form');
      form?.requestSubmit();
    },
    dispose() {
      for (const off of offs) off();
    },
  };
}
