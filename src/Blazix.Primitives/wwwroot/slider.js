// @ts-check
//
// The browser side of Slider. Blazor owns the values and renders every position; this turns
// pointer drags and key presses into values (snapped to the step, kept apart by the minimum
// distance, neighbours pushed along), writes the thumb and indicator positions at once so dragging
// stays smooth on Blazor Server, and reports each change (Change) and the end of a drag (Commit).
//
// Each thumb holds a visually hidden <input type="range"> that takes focus, so screen readers get
// a native slider; its arrow keys are handled here to add Shift for the large step.

import { byId, call, isRTL, on } from './core/dom.js';

/**
 * @typedef {{
 *   min: number, max: number, step: number, largeStep: number, minStepsBetweenValues: number,
 *   orientation: 'horizontal' | 'vertical', disabled: boolean, readOnly: boolean, values: number[],
 * }} SliderOptions
 */

/** @param {number} value @param {SliderOptions} o */
function snap(value, o) {
  const steps = Math.round((value - o.min) / o.step);
  const decimals = (String(o.step).split('.')[1] ?? '').length;
  const snapped = Number((o.min + steps * o.step).toFixed(decimals));
  return Math.min(o.max, Math.max(o.min, snapped));
}

/**
 * Moves one thumb, pushing its neighbours to keep the minimum distance; never past the ends.
 * @param {number[]} values @param {number} index @param {number} value @param {SliderOptions} o
 */
export function moveThumb(values, index, value, o) {
  const next = [...values];
  const gap = o.minStepsBetweenValues * o.step;
  const count = next.length;
  // The furthest this thumb can go while every other thumb still fits.
  const low = o.min + index * gap;
  const high = o.max - (count - 1 - index) * gap;
  next[index] = Math.min(high, Math.max(low, value));
  for (let j = index + 1; j < count; j++) next[j] = Math.max(next[j], next[j - 1] + gap);
  for (let j = index - 1; j >= 0; j--) next[j] = Math.min(next[j], next[j + 1] - gap);
  return next;
}

/**
 * @param {import('./core/dom.js').DotNetRef} dotnet
 * @param {string} controlId
 * @param {SliderOptions} initial
 */
export function createSlider(dotnet, controlId, initial) {
  let options = initial;
  /** @type {{ index: number, pointerId: number } | null} */
  let drag = null;
  let frame = 0;
  /** @type {number[] | null} */
  let pending = null;

  const control = () => byId(controlId);
  const root = () => control()?.closest('[data-blazix-slider]') ?? null;
  const vertical = () => options.orientation === 'vertical';
  const percent = (/** @type {number} */ value) => (options.max > options.min ? ((value - options.min) / (options.max - options.min)) * 100 : 0);

  /** Writes positions at once, ahead of Blazor's render of the same values. @param {number[]} values */
  function paint(values) {
    const el = root();
    if (!el) return;
    const edge = vertical() ? 'bottom' : 'insetInlineStart';
    const size = vertical() ? 'height' : 'width';
    for (const thumb of el.querySelectorAll('[data-blazix-slider-thumb]')) {
      const index = Number(/** @type {HTMLElement} */ (thumb).dataset.index ?? 0);
      if (values[index] !== undefined) /** @type {HTMLElement} */ (thumb).style[edge] = `${percent(values[index])}%`;
    }
    const indicator = /** @type {HTMLElement | null} */ (el.querySelector('[data-blazix-slider-indicator]'));
    if (indicator) {
      const range = values.length > 1;
      const start = range ? percent(values[0]) : 0;
      const end = percent(values[values.length - 1]);
      indicator.style[edge] = `${start}%`;
      indicator.style[size] = `${end - start}%`;
    }
  }

  /** @param {number[]} values */
  function change(values, commit = false) {
    options = { ...options, values };
    paint(values);
    pending = values;
    if (commit) {
      cancelAnimationFrame(frame);
      frame = 0;
      call(dotnet, 'Change', values, true);
      pending = null;
      return;
    }
    frame ||= requestAnimationFrame(() => {
      frame = 0;
      if (pending) call(dotnet, 'Change', pending, false);
      pending = null;
    });
  }

  /** @param {PointerEvent} event */
  function valueAt(event) {
    const el = control();
    if (!el) return options.min;
    const rect = el.getBoundingClientRect();
    let fraction = vertical()
      ? (rect.bottom - event.clientY) / rect.height
      : (event.clientX - rect.left) / rect.width;
    if (!vertical() && isRTL(el)) fraction = 1 - fraction;
    return snap(options.min + Math.min(1, Math.max(0, fraction)) * (options.max - options.min), options);
  }

  /** The thumb a press at this value moves: the closest; between two equal ones, the one it moves away from. */
  function closestThumb(/** @type {number} */ value) {
    const values = options.values;
    let best = 0;
    for (let i = 1; i < values.length; i++) {
      const d = Math.abs(values[i] - value);
      const b = Math.abs(values[best] - value);
      if (d < b || (d === b && value > values[i])) best = i;
    }
    return best;
  }

  const focusThumb = (/** @type {number} */ index) => {
    const input = /** @type {HTMLElement | null} */ (root()?.querySelector(`[data-blazix-slider-input][data-index="${index}"]`) ?? null);
    input?.focus({ preventScroll: true });
  };

  const offs = [
    on(document, 'pointerdown', (/** @type {PointerEvent} */ event) => {
      const el = control();
      if (!el || !el.contains(/** @type {Node} */ (event.target)) || event.button !== 0) return;
      if (options.disabled || options.readOnly) return;
      event.preventDefault();
      const value = valueAt(event);
      const thumb = /** @type {HTMLElement | null} */ (/** @type {Element} */ (event.target).closest('[data-blazix-slider-thumb]'));
      const index = thumb ? Number(thumb.dataset.index ?? 0) : closestThumb(value);
      drag = { index, pointerId: event.pointerId };
      el.setPointerCapture?.(event.pointerId);
      focusThumb(index);
      call(dotnet, 'DragStart', index);
      // Pressing a thumb itself doesn't move it; pressing the track jumps the closest thumb there.
      if (!thumb) change(moveThumb(options.values, index, value, options));
    }),
    on(document, 'pointermove', (/** @type {PointerEvent} */ event) => {
      if (!drag || event.pointerId !== drag.pointerId) return;
      change(moveThumb(options.values, drag.index, valueAt(event), options));
    }),
    ...['pointerup', 'pointercancel'].map((type) => on(document, type, (/** @type {PointerEvent} */ event) => {
      if (!drag || event.pointerId !== drag.pointerId) return;
      drag = null;
      change(options.values, true);
      call(dotnet, 'DragEnd');
    })),
    on(document, 'keydown', (/** @type {KeyboardEvent} */ event) => {
      const input = /** @type {HTMLInputElement} */ (event.target);
      if (!input?.matches?.('[data-blazix-slider-input]') || root() !== input.closest('[data-blazix-slider]') || options.disabled || options.readOnly) return;
      const index = Number(input.dataset.index ?? 0);
      const current = options.values[index] ?? options.min;
      const step = event.shiftKey ? options.largeStep : options.step;
      const rtl = !vertical() && isRTL(input);
      let next;
      switch (event.key) {
        case 'ArrowUp': next = current + step; break;
        case 'ArrowDown': next = current - step; break;
        case 'ArrowRight': next = current + (rtl ? -step : step); break;
        case 'ArrowLeft': next = current - (rtl ? -step : step); break;
        case 'PageUp': next = current + options.largeStep; break;
        case 'PageDown': next = current - options.largeStep; break;
        case 'Home': next = options.min; break;
        case 'End': next = options.max; break;
        default: return;
      }
      event.preventDefault();
      change(moveThumb(options.values, index, snap(next, options), options), true);
    }),
  ];

  return {
    /** @param {SliderOptions} next */
    update(next) {
      // Keep the values a drag is producing until Blazor has caught up with them.
      options = drag ? { ...next, values: options.values } : next;
    },
    dispose() {
      cancelAnimationFrame(frame);
      for (const off of offs) off();
    },
  };
}
