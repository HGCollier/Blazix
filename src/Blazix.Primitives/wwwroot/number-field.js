// @ts-check
//
// The browser side of Number Field. Blazor owns the value, parsing, formatting and clamping; this
// handles what has to happen inside the event: arrow keys (Shift for the large step, Alt for the
// small one) without moving the caret, filtering characters the format can't contain, wheel scrub,
// press-and-hold on the increment/decrement buttons, and scrubbing with pointer lock.

import { byId, call, on } from './core/dom.js';

/**
 * @typedef {{
 *   disabled: boolean, readOnly: boolean, allowWheelScrub: boolean, allowedChars: string,
 *   scrubDirection: 'horizontal' | 'vertical', pixelSensitivity: number,
 * }} NumberFieldOptions
 */

const REPEAT_DELAY = 400;
const REPEAT_INTERVAL = 60;

/**
 * @param {import('./core/dom.js').DotNetRef} dotnet
 * @param {string} rootId
 * @param {NumberFieldOptions} initial
 */
export function createNumberField(dotnet, rootId, initial) {
  let options = initial;
  /** @type {ReturnType<typeof setTimeout> | undefined} */ let repeatTimer;
  /** @type {ReturnType<typeof setInterval> | undefined} */ let repeatInterval;
  /** @type {{ pointerId: number, delta: number, x: number, y: number } | null} */ let scrub = null;

  const root = () => byId(rootId);
  const input = () => /** @type {HTMLInputElement | null} */ (root()?.querySelector('[data-blazix-number-input]') ?? null);
  const inField = (/** @type {EventTarget | null} */ target) => {
    const el = root();
    return !!el && target instanceof Node && el.contains(target);
  };
  const blocked = () => options.disabled || options.readOnly;

  /** @param {number} direction @param {'small' | 'normal' | 'large'} amount */
  const step = (direction, amount) => call(dotnet, 'Step', direction, amount, input()?.value ?? '');

  function stopRepeat() {
    clearTimeout(repeatTimer);
    clearInterval(repeatInterval);
  }

  const offs = [
    on(document, 'keydown', (/** @type {KeyboardEvent} */ event) => {
      const el = input();
      if (!el || event.target !== el || blocked()) return;
      const amount = event.shiftKey ? 'large' : event.altKey ? 'small' : 'normal';
      switch (event.key) {
        case 'ArrowUp': event.preventDefault(); step(1, amount); return;
        case 'ArrowDown': event.preventDefault(); step(-1, amount); return;
        case 'PageUp': event.preventDefault(); step(1, 'large'); return;
        case 'PageDown': event.preventDefault(); step(-1, 'large'); return;
        case 'Home': event.preventDefault(); call(dotnet, 'Edge', 'min'); return;
        case 'End': event.preventDefault(); call(dotnet, 'Edge', 'max'); return;
        case 'Enter': call(dotnet, 'Commit', el.value); return;
        default: return;
      }
    }),
    // Characters the format can't contain never reach the input (typed or pasted).
    on(document, 'beforeinput', (/** @type {InputEvent} */ event) => {
      const el = input();
      if (!el || event.target !== el || !event.data || !options.allowedChars) return;
      if ([...event.data].some((c) => !options.allowedChars.includes(c))) event.preventDefault();
    }),
    on(document, 'wheel', (/** @type {WheelEvent} */ event) => {
      const el = input();
      if (!el || !options.allowWheelScrub || blocked() || document.activeElement !== el || !inField(event.target)) return;
      event.preventDefault();
      if (event.deltaY !== 0) step(event.deltaY < 0 ? 1 : -1, event.shiftKey ? 'large' : event.altKey ? 'small' : 'normal');
    }, { passive: false }),

    // Increment/decrement: a press steps at once, then repeats while held. Focus stays in the input.
    on(document, 'pointerdown', (/** @type {PointerEvent} */ event) => {
      const button = /** @type {HTMLElement | null} */ (/** @type {Element} */ (event.target).closest?.('[data-blazix-number-step]'));
      if (!button || !inField(button) || event.button !== 0 || blocked() || button.hasAttribute('disabled')) return;
      event.preventDefault();
      const direction = button.dataset.blazixNumberStep === 'up' ? 1 : -1;
      const amount = event.shiftKey ? 'large' : event.altKey ? 'small' : 'normal';
      if (event.pointerType !== 'touch') input()?.focus({ preventScroll: true });
      step(direction, amount);
      stopRepeat();
      repeatTimer = setTimeout(() => {
        repeatInterval = setInterval(() => step(direction, amount), REPEAT_INTERVAL);
      }, REPEAT_DELAY);
    }),
    ...['pointerup', 'pointercancel', 'blur'].map((type) => on(type === 'blur' ? window : document, type, stopRepeat)),
    on(document, 'pointerout', (/** @type {PointerEvent} */ event) => {
      if ((/** @type {Element} */ (event.target)).closest?.('[data-blazix-number-step]')) stopRepeat();
    }),
    // A click from the keyboard (or a screen reader) on a stepper button: one step.
    on(document, 'click', (/** @type {MouseEvent} */ event) => {
      const button = /** @type {HTMLElement | null} */ (/** @type {Element} */ (event.target).closest?.('[data-blazix-number-step]'));
      if (!button || !inField(button) || event.detail !== 0 || blocked()) return;
      step(button.dataset.blazixNumberStep === 'up' ? 1 : -1, 'normal');
    }),

    // Scrub area: drag to change the value, with the pointer locked so it can go on past the screen edge.
    on(document, 'pointerdown', (/** @type {PointerEvent} */ event) => {
      const area = /** @type {HTMLElement | null} */ (/** @type {Element} */ (event.target).closest?.('[data-blazix-number-scrub]'));
      if (!area || !inField(area) || event.button !== 0 || blocked()) return;
      event.preventDefault();
      scrub = { pointerId: event.pointerId, delta: 0, x: event.clientX, y: event.clientY };
      if (event.pointerType === 'mouse') area.requestPointerLock?.();
      else area.setPointerCapture?.(event.pointerId);
      input()?.focus({ preventScroll: true });
      call(dotnet, 'Scrub', true);
    }),
    on(document, 'pointermove', (/** @type {PointerEvent} */ event) => {
      if (!scrub) return;
      const vertical = options.scrubDirection === 'vertical';
      const movement = vertical ? -event.movementY : event.movementX;
      scrub.x += event.movementX;
      scrub.y += event.movementY;
      const cursor = /** @type {HTMLElement | null} */ (root()?.querySelector('[data-blazix-number-scrub-cursor]') ?? null);
      if (cursor) {
        // While the pointer is locked, the real cursor is hidden: this stands in for it.
        const x = ((scrub.x % innerWidth) + innerWidth) % innerWidth;
        const y = ((scrub.y % innerHeight) + innerHeight) % innerHeight;
        cursor.style.cssText = `position:fixed;top:0;left:0;pointer-events:none;transform:translate3d(${x}px,${y}px,0)`;
      }
      scrub.delta += movement;
      const sensitivity = Math.max(1, options.pixelSensitivity);
      while (Math.abs(scrub.delta) >= sensitivity) {
        const direction = Math.sign(scrub.delta);
        scrub.delta -= direction * sensitivity;
        step(direction, event.shiftKey ? 'large' : event.altKey ? 'small' : 'normal');
      }
    }),
    ...['pointerup', 'pointercancel'].map((type) => on(document, type, () => {
      if (!scrub) return;
      scrub = null;
      if (document.pointerLockElement) document.exitPointerLock();
      call(dotnet, 'Scrub', false);
    })),
  ];

  return {
    /** @param {NumberFieldOptions} next */
    update(next) {
      options = next;
    },
    dispose() {
      stopRepeat();
      for (const off of offs) off();
    },
  };
}
