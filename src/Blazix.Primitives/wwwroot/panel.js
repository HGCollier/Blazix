// @ts-check
//
// Panels that open and close with a size animation: Collapsible and Accordion (after UI SDK's
// motion/panel). Blazor renders open/closed, `hidden` and data-starting-style/data-ending-style;
// this measures the panel and writes its size variables, so CSS can transition or animate height:
//
//   .panel { height: var(--collapsible-panel-height); transition: height 150ms; }
//   .panel[data-starting-style], .panel[data-ending-style] { height: 0; }
//
// The variable holds the measured size while animating and `auto` once open, so content can grow.
// Closing pins it to the current size first (prepareClose), so the change to zero animates from it.

import { byId, call, on } from './core/dom.js';
import { whenAnimationsFinish } from './core/presence.js';

/** @type {WeakMap<HTMLElement, { cancel: () => void }>} */
const running = new WeakMap();

/** Exits being watched for (see watchExit), so a new close or an open can stop the old watch. */
/** @type {WeakMap<HTMLElement, () => void>} */
const exits = new WeakMap();

/** Panels hidden here at the end of their exit, before Blazor's render with `hidden` arrived. */
/** @type {WeakSet<HTMLElement>} */
const hiddenEarly = new WeakSet();

/**
 * Hides the panel the moment its exit animation ends. Exit keyframes don't keep their end state
 * (accordion-up returns to the panel's full height), so waiting for Blazor to render `hidden` a
 * round trip later shows the panel open again in between.
 * @param {HTMLElement} el @param {string} prefix
 */
function watchExit(el, prefix) {
  exits.get(el)?.();
  /** @type {{ done: Promise<boolean>, cancel: () => void } | null} */
  let wait = null;
  const observer = new MutationObserver(() => {
    if (wait || !el.hasAttribute('data-closed')) return;
    observer.disconnect();
    wait = whenAnimationsFinish([el]);
    wait.done.then((finished) => {
      if (exits.get(el) !== stop) return;
      exits.delete(el);
      if (!finished || !el.hasAttribute('data-closed')) return;
      size(el, prefix, '0px', '0px');
      el.hidden = true;
      hiddenEarly.add(el);
    });
  });
  const stop = () => {
    observer.disconnect();
    wait?.cancel();
    if (exits.get(el) === stop) exits.delete(el);
  };
  observer.observe(el, { attributes: true, attributeFilter: ['data-closed'] });
  exits.set(el, stop);
}

/** @param {HTMLElement} el @param {string} prefix @param {string} height @param {string} width */
function size(el, prefix, height, width) {
  el.style.setProperty(`--${prefix}-height`, height);
  el.style.setProperty(`--${prefix}-width`, width);
}

/**
 * The panel's open size, measured at `height: auto` while it's held closed. scrollHeight can't be
 * used: overflow toward the start isn't scrollable, so a panel whose content is pushed to the end
 * (`justify-content: end`) reports 0. Transitions are off while measuring
 * and while the closed size is restored, so neither change starts one.
 * @param {HTMLElement} el
 */
function measure(el) {
  const { style } = el;
  const saved = ['transition', 'height', 'width'].map((name) => [name, style.getPropertyValue(name), style.getPropertyPriority(name)]);
  style.setProperty('transition', 'none', 'important');
  style.setProperty('height', 'auto', 'important');
  style.setProperty('width', 'auto', 'important');
  const result = { height: el.scrollHeight, width: el.scrollWidth };
  for (const [name, value, priority] of saved.slice(1)) style.setProperty(name, value, priority);
  void getComputedStyle(el).height; // back at the closed size before transitions return
  style.setProperty(...saved[0]);
  return result;
}

/**
 * The panel was rendered open. Animates from data-starting-style unless `animate` is false
 * (open from the start, or opened by find in page).
 * @param {string} id @param {string} prefix @param {boolean} animate
 */
export async function opened(id, prefix, animate) {
  const el = byId(id);
  if (!el) return true;
  exits.get(el)?.();
  if (hiddenEarly.has(el)) {
    // Reopened before Blazor rendered `hidden`, so its render won't remove what watchExit set.
    hiddenEarly.delete(el);
    el.hidden = false;
  }
  running.get(el)?.cancel();
  if (!animate) {
    for (const animation of el.getAnimations()) animation.finish();
    el.removeAttribute('data-starting-style');
    size(el, prefix, 'auto', 'auto');
    return true;
  }
  // Measure while data-starting-style holds it at its closed size, so nothing transitions from a
  // size it never showed.
  const natural = measure(el);
  size(el, prefix, `${natural.height}px`, `${natural.width}px`);
  await new Promise((resolve) => requestAnimationFrame(resolve));
  el.removeAttribute('data-starting-style');
  const wait = whenAnimationsFinish([el]);
  running.set(el, wait);
  const finished = await wait.done;
  if (finished && running.get(el) === wait) {
    running.delete(el);
    if (el.hasAttribute('data-open')) size(el, prefix, 'auto', 'auto');
  }
  return finished;
}

/**
 * Called before Blazor renders the closed state: pins the size variables to the current size,
 * so the exit transition or keyframes start from it. Moves focus out of the panel to `triggerId`.
 * @param {string} id @param {string} prefix @param {string | null} triggerId
 */
export function prepareClose(id, prefix, triggerId) {
  const el = byId(id);
  if (!el) return;
  running.get(el)?.cancel();
  running.delete(el);
  const style = getComputedStyle(el);
  size(el, prefix, style.height === 'auto' ? `${el.scrollHeight}px` : style.height, style.width === 'auto' ? `${el.scrollWidth}px` : style.width);
  void el.offsetHeight; // commit, so the change to zero animates from it
  watchExit(el, prefix);
  // Focus inside a closing panel would drop to the page. Its trigger is the closest place.
  if (el.contains(document.activeElement)) byId(triggerId)?.focus({ preventScroll: true });
}

/**
 * Blazor rendered the closed state (data-closed, data-ending-style). Resolves once the exit
 * animation finished (true), or false if the panel reopened first.
 * @param {string} id @param {string} prefix
 */
export async function closed(id, prefix) {
  const el = byId(id);
  if (!el) return true;
  running.get(el)?.cancel();
  const wait = whenAnimationsFinish([el]);
  running.set(el, wait);
  const finished = await wait.done;
  if (running.get(el) === wait) running.delete(el);
  if (finished) size(el, prefix, '0px', '0px');
  return finished;
}

/**
 * Find in page reveals a `hidden="until-found"` panel by removing `hidden` and firing beforematch.
 * Reports it, so Blazor opens the panel; returns the function that stops listening.
 * @param {string} id @param {import('./core/dom.js').DotNetRef} dotnet
 */
export function watchFind(id, dotnet) {
  const el = byId(id);
  if (!el) return { dispose() {} };
  const off = on(el, 'beforematch', () => call(dotnet, 'RevealAsync'));
  return { dispose: off };
}

export function dispose() {}
