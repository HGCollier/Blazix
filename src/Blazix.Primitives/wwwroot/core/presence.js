// @ts-check
//
// Enter and exit animations (after UI SDK's motion/presence). Blazor renders the closed state
// (data-closed, data-ending-style) and keeps the element mounted; this waits for the element's own
// finite animations and transitions to finish, then Blazor removes it. Infinite animations (a
// spinner inside) never hold it open, and a timeout from the computed durations is the safety net.

/**
 * Sets data-starting-style for the first painted frame, so CSS transitions have a start point.
 * Keyframe animations and @starting-style need nothing.
 * @param {(HTMLElement | null | undefined)[]} elements
 */
export function animateIn(elements) {
  const targets = /** @type {HTMLElement[]} */ (elements.filter(Boolean));
  for (const el of targets) el.setAttribute('data-starting-style', '');
  // Two frames: the first paints the starting style, the second removes it.
  const frame = requestAnimationFrame(() => {
    inner = requestAnimationFrame(() => {
      for (const el of targets) el.removeAttribute('data-starting-style');
    });
  });
  let inner = 0;
  return () => {
    cancelAnimationFrame(frame);
    cancelAnimationFrame(inner);
    for (const el of targets) el.removeAttribute('data-starting-style');
  };
}

/**
 * Jumps the elements' running CSS transitions to their end, for an element that should appear as it
 * is. Reading the animations flushes style first, so a transition a change just started is included.
 * Keyframe animations keep playing.
 * @param {(HTMLElement | null | undefined)[]} elements
 */
export function finishTransitions(elements) {
  for (const el of elements) {
    for (const animation of el?.getAnimations() ?? []) {
      if (animation instanceof CSSTransition) animation.finish();
    }
  }
}

/** @param {string} value */
function seconds(value) {
  return value.split(',').map((part) => {
    const number = parseFloat(part) || 0;
    return part.trim().endsWith('ms') ? number / 1000 : number;
  });
}

// The longest transition, or one iteration of the longest animation, in ms.
function longest(/** @type {HTMLElement} */ el) {
  const style = getComputedStyle(el);
  /** @param {string} durations @param {string} delays */
  const pairs = (durations, delays) => {
    const d = seconds(durations);
    const l = seconds(delays);
    return d.map((duration, index) => (duration + (l[index % l.length] ?? 0)) * 1000);
  };
  return Math.max(0, ...pairs(style.transitionDuration, style.transitionDelay), ...pairs(style.animationDuration, style.animationDelay));
}

/**
 * Resolves once the elements' running finite animations have finished (or there were none).
 * Reading animations flushes style, so a transition the closing render just started is included.
 * Returns a promise and a cancel function; a cancelled wait resolves false.
 * @param {(HTMLElement | null | undefined)[]} elements
 * @returns {{ done: Promise<boolean>, cancel: () => void }}
 */
export function whenAnimationsFinish(elements) {
  const targets = /** @type {HTMLElement[]} */ (elements.filter((el) => el?.isConnected));
  const animations = targets
    .flatMap((el) => el.getAnimations())
    .filter((a) => Number.isFinite(Number(a.effect?.getComputedTiming().endTime)));
  /** @type {(value: boolean) => void} */
  let resolve = () => {};
  const done = new Promise((r) => (resolve = r));
  let settled = false;
  /** @param {boolean} value */
  const finish = (value) => {
    if (settled) return;
    settled = true;
    clearTimeout(timer);
    resolve(value);
  };
  const timeout = animations.length ? Math.max(0, ...targets.map(longest)) + 250 : 0;
  const timer = setTimeout(() => finish(true), timeout);
  if (animations.length) Promise.allSettled(animations.map((a) => a.finished)).then(() => finish(true));
  return { done, cancel: () => finish(false) };
}

/**
 * Plays an open/close of an element whose size CSS animates, like a collapsible panel: sets the
 * size variables to the measured size while animating, `auto` once open and `0px` once closed.
 * @param {HTMLElement} el
 * @param {boolean} open
 * @param {(height: string, width: string) => void} size
 * @param {boolean} animate
 * @returns {{ done: Promise<boolean>, cancel: () => void }}
 */
export function animateSize(el, open, size, animate) {
  if (!animate) {
    for (const animation of el.getAnimations()) animation.finish();
    size(open ? 'auto' : '0px', open ? 'auto' : '0px');
    return { done: Promise.resolve(true), cancel: () => {} };
  }
  if (open) {
    // Measure at the closed size first, so nothing transitions from a size it never showed.
    el.setAttribute('data-starting-style', '');
    size(`${el.scrollHeight}px`, `${el.scrollWidth}px`);
    /** @type {{ done: Promise<boolean>, cancel: () => void } | null} */
    let wait = null;
    let cancelled = false;
    const frame = requestAnimationFrame(() => {
      el.removeAttribute('data-starting-style');
      if (cancelled) return;
      wait = whenAnimationsFinish([el]);
      wait.done.then((finished) => {
        if (finished && !cancelled) size('auto', 'auto');
      });
    });
    return {
      done: new Promise((resolve) => {
        const check = () => (wait ? wait.done.then(resolve) : requestAnimationFrame(check));
        requestAnimationFrame(check);
      }),
      cancel: () => {
        cancelled = true;
        cancelAnimationFrame(frame);
        el.removeAttribute('data-starting-style');
        wait?.cancel();
      },
    };
  }
  // Closing: from the size it has now, so an interrupted opening animates back from where it is.
  const style = getComputedStyle(el);
  size(style.height, style.width);
  void el.offsetHeight; // commit, so the change to zero animates from it
  const wait = whenAnimationsFinish([el]);
  wait.done.then((finished) => {
    if (finished) size('0px', '0px');
  });
  return wait;
}
