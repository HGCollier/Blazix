// @ts-check
//
// Anchor positioning (after UI SDK's platform/position). Places a positioner element beside its
// anchor, flips to the other side when there isn't room, shifts along the anchor to stay on screen
// (but never past the anchor's far edge, so a popup whose anchor scrolls away leaves with it),
// places an arrow, and keeps following while anything moves. It sleeps while nothing does.
//
// The positioner is ours (a popover="manual" element in the top layer), so the viewport is its
// containing block and it has no transform of its own: offsetWidth/Height are its real size even
// while the popup inside plays a scale-in animation.
//
// Writes on the positioner: left/top, --anchor-width, --anchor-height, --available-width,
// --available-height, --transform-origin, --positioner-width, --positioner-height.
// Writes data-side/data-align on the positioner, the popup and the arrow, data-uncentered on the
// arrow, and data-anchor-hidden on the popup when the anchor has scrolled out of view.

import { clamp } from './dom.js';

/**
 * @typedef {'top' | 'bottom' | 'left' | 'right' | 'inline-start' | 'inline-end'} Side
 * @typedef {'top' | 'bottom' | 'left' | 'right'} PhysicalSide
 * @typedef {'start' | 'center' | 'end'} Align
 * @typedef {{ x: number, y: number, width: number, height: number }} Rect
 * @typedef {HTMLElement | { getBoundingClientRect(): Rect } | { x: number, y: number }} Anchor
 * @typedef {{
 *   side?: Side, align?: Align, sideOffset?: number, alignOffset?: number,
 *   collisionPadding?: number, arrowPadding?: number, flip?: boolean, sticky?: boolean,
 *   fallbackAxis?: boolean, trackAnchor?: boolean
 * }} PositionOptions
 */

/** @type {Record<PhysicalSide, PhysicalSide>} */
const opposite = { top: 'bottom', bottom: 'top', left: 'right', right: 'left' };

/** @param {Side} side @param {boolean} rtl @returns {PhysicalSide} */
function physical(side, rtl) {
  if (side === 'inline-start') return rtl ? 'right' : 'left';
  if (side === 'inline-end') return rtl ? 'left' : 'right';
  return side;
}

const vertical = (/** @type {PhysicalSide} */ side) => side === 'top' || side === 'bottom';

/** @param {Anchor} anchor @returns {Rect | null} */
function anchorRect(anchor) {
  if (anchor instanceof Element) {
    if (!anchor.isConnected) return null;
    if (!anchor.getClientRects().length) {
      // display: contents has no box of its own: use the box around what it renders.
      if (getComputedStyle(anchor).display === 'contents') {
        const range = document.createRange();
        range.selectNodeContents(anchor);
        if (range.getClientRects().length) {
          const r = range.getBoundingClientRect();
          return { x: r.x, y: r.y, width: r.width, height: r.height };
        }
      }
      // A hidden anchor (display: none, e.g. a trigger only shown on small screens) measures as an
      // empty box at the top left, as in floating-ui: the popup
      // goes there and keeps taking the room available, rather than freezing where it was.
      return { x: 0, y: 0, width: 0, height: 0 };
    }
  }
  if ('getBoundingClientRect' in anchor) {
    const r = anchor.getBoundingClientRect();
    return { x: r.x, y: r.y, width: r.width, height: r.height };
  }
  return { x: anchor.x, y: anchor.y, width: 0, height: 0 };
}

function viewport() {
  const visual = window.visualViewport;
  return {
    x: visual?.offsetLeft ?? 0,
    y: visual?.offsetTop ?? 0,
    width: visual?.width ?? document.documentElement.clientWidth,
    height: visual?.height ?? document.documentElement.clientHeight,
  };
}

/**
 * @param {Rect} anchor @param {{ width: number, height: number }} size
 * @param {PhysicalSide} side @param {Align} align @param {number} sideOffset @param {number} alignOffset
 */
function place(anchor, size, side, align, sideOffset, alignOffset) {
  let x = 0;
  let y = 0;
  if (side === 'bottom') y = anchor.y + anchor.height + sideOffset;
  if (side === 'top') y = anchor.y - size.height - sideOffset;
  if (side === 'right') x = anchor.x + anchor.width + sideOffset;
  if (side === 'left') x = anchor.x - size.width - sideOffset;
  if (vertical(side)) {
    if (align === 'start') x = anchor.x + alignOffset;
    if (align === 'center') x = anchor.x + anchor.width / 2 - size.width / 2 + alignOffset;
    if (align === 'end') x = anchor.x + anchor.width - size.width - alignOffset;
  } else {
    if (align === 'start') y = anchor.y + alignOffset;
    if (align === 'center') y = anchor.y + anchor.height / 2 - size.height / 2 + alignOffset;
    if (align === 'end') y = anchor.y + anchor.height - size.height - alignOffset;
  }
  return { x, y };
}

/**
 * @param {PhysicalSide} side @param {{ x: number, y: number }} point
 * @param {{ width: number, height: number }} size @param {Rect} view @param {number} padding
 */
function overflow(side, point, size, view, padding) {
  if (side === 'bottom') return point.y + size.height - (view.y + view.height - padding);
  if (side === 'top') return view.y + padding - point.y;
  if (side === 'right') return point.x + size.width - (view.x + view.width - padding);
  return view.x + padding - point.x;
}

/** @param {PhysicalSide} side @param {Rect} anchor @param {Rect} view @param {number} sideOffset @param {number} padding */
function available(side, anchor, view, sideOffset, padding) {
  const height = side === 'bottom' ? view.y + view.height - padding - (anchor.y + anchor.height + sideOffset)
    : side === 'top' ? anchor.y - sideOffset - padding - view.y
      : view.height - padding * 2;
  const width = side === 'right' ? view.x + view.width - padding - (anchor.x + anchor.width + sideOffset)
    : side === 'left' ? anchor.x - sideOffset - padding - view.x
      : view.width - padding * 2;
  return { width: Math.max(0, width), height: Math.max(0, height) };
}

/**
 * Pure placement: where the popup goes, after flipping and shifting.
 * @param {Rect} anchor @param {{ width: number, height: number }} size @param {Rect} view
 * @param {Required<Pick<PositionOptions, 'side' | 'align' | 'sideOffset' | 'alignOffset' | 'collisionPadding' | 'flip' | 'sticky' | 'fallbackAxis'>>} options
 * @param {boolean} rtl
 */
export function computePlacement(anchor, size, view, options, rtl) {
  const preferred = physical(options.side, rtl);
  // start and end follow the reading direction along a horizontal edge.
  const align = rtl && vertical(preferred) && options.align !== 'center'
    ? (options.align === 'start' ? 'end' : 'start')
    : options.align;
  const { sideOffset, alignOffset, collisionPadding: padding } = options;

  let side = preferred;
  let point = place(anchor, size, side, align, sideOffset, alignOffset);
  if (options.flip) {
    const over = overflow(side, point, size, view, padding);
    if (over > 0) {
      /** @type {PhysicalSide[]} */
      const candidates = [opposite[side]];
      if (options.fallbackAxis) {
        candidates.push(...(vertical(side) ? /** @type {PhysicalSide[]} */ (['right', 'left']) : /** @type {PhysicalSide[]} */ (['bottom', 'top'])));
      }
      let best = over;
      for (const candidate of candidates) {
        const p = place(anchor, size, candidate, align, sideOffset, alignOffset);
        const o = overflow(candidate, p, size, view, padding);
        if (o < best) {
          best = o;
          side = candidate;
          point = p;
          if (o <= 0) break;
        }
      }
    }
  }

  // Shift along the anchor to stay on screen, but not past the anchor's far edge unless sticky.
  if (vertical(side)) {
    const x = clamp(point.x, view.x + padding, view.x + view.width - size.width - padding);
    point.x = x === point.x || options.sticky ? x : clamp(x, anchor.x - size.width, anchor.x + anchor.width);
  } else {
    const y = clamp(point.y, view.y + padding, view.y + view.height - size.height - padding);
    point.y = y === point.y || options.sticky ? y : clamp(y, anchor.y - size.height, anchor.y + anchor.height);
  }

  return { ...point, side, align: options.align, available: available(side, anchor, view, sideOffset, padding) };
}

/** @param {PhysicalSide} side @param {Align} align @param {boolean} rtl @param {Rect} a @param {{ width: number, height: number }} size @param {{ x: number, y: number }} point */
function transformOrigin(side, align, rtl, a, size, point) {
  if (vertical(side)) {
    const start = rtl ? a.x + a.width - point.x : a.x - point.x;
    const end = rtl ? a.x - point.x : a.x + a.width - point.x;
    const x = align === 'center' ? a.x + a.width / 2 - point.x : align === 'start' ? start : end;
    return [clamp(x, 0, size.width), side === 'bottom' ? 0 : size.height];
  }
  const y = align === 'center' ? a.y + a.height / 2 - point.y : align === 'start' ? a.y - point.y : a.y + a.height - point.y;
  return [side === 'right' ? 0 : size.width, clamp(y, 0, size.height)];
}

// Writes a style or attribute only when it changed, so a still popup costs no style recalcs.
function createWriter(/** @type {HTMLElement} */ el) {
  /** @type {Map<string, string | null>} */
  const written = new Map();
  return {
    /** @param {string} name @param {string} value */
    style(name, value) {
      if (written.get(`s:${name}`) === value) return;
      written.set(`s:${name}`, value);
      el.style.setProperty(name, value);
    },
    /** @param {string} name @param {string | null} value */
    attribute(name, value) {
      if (written.get(name) === value) return;
      written.set(name, value);
      if (value === null) el.removeAttribute(name);
      else el.setAttribute(name, value);
    },
  };
}

// What can move an anchor without the popup hearing it: scrolling, resizing, the page changing,
// input, loads and animations. Positioning sleeps while none happen and wakes on the first.
/** @type {Map<() => void, Element>} */
const wakers = new Map();
/** @type {(() => void) | null} */
let unlisten = null;

function listen() {
  const controller = new AbortController();
  const options = { capture: true, passive: true, signal: controller.signal };
  /** @param {Event} event */
  const wakeOutside = (event) => wakers.forEach((popup, wake) => {
    if (!(event.target instanceof Node && popup.contains(event.target))) wake();
  });
  for (const type of ['scroll', 'load', 'input', 'pointermove', 'focusin', 'transitionstart', 'animationstart']) {
    document.addEventListener(type, wakeOutside, options);
  }
  window.addEventListener('resize', wakeOutside, options);
  window.visualViewport?.addEventListener('resize', wakeOutside, options);
  window.visualViewport?.addEventListener('scroll', wakeOutside, options);
  const changes = new MutationObserver((records) => wakers.forEach((popup, wake) => {
    if (records.some((record) => !popup.contains(record.target))) wake();
  }));
  changes.observe(document, { subtree: true, childList: true, attributes: true, characterData: true });
  return () => {
    controller.abort();
    changes.disconnect();
  };
}

/** @param {() => void} wake @param {HTMLElement} popup */
function watchMoves(wake, popup) {
  if (!wakers.size) unlisten = listen();
  const sizes = new ResizeObserver(() => wake());
  sizes.observe(popup);
  wakers.set(wake, popup);
  return () => {
    sizes.disconnect();
    wakers.delete(wake);
    if (!wakers.size) {
      unlisten?.();
      unlisten = null;
    }
  };
}

const STILL_FRAMES = 3;
const SLOW_CHECK_MS = 500;

/**
 * Positions `el` (the positioner) against an anchor and keeps it there. Both getters are read on
 * every check, so a new anchor or new options apply while open. Returns a stop function with
 * `update()` (check now) attached.
 * @param {() => Anchor | null | undefined} getAnchor
 * @param {HTMLElement} el
 * @param {() => PositionOptions} getOptions
 * @param {{ popup?: () => HTMLElement | null, arrow?: () => HTMLElement | null }} [parts]
 */
export function position(getAnchor, el, getOptions, parts = {}) {
  const writer = createWriter(el);
  /** @type {WeakMap<HTMLElement, ReturnType<typeof createWriter>>} */
  const writers = new WeakMap();
  const writerOf = (/** @type {HTMLElement} */ target) => {
    let w = writers.get(target);
    if (!w) writers.set(target, (w = createWriter(target)));
    return w;
  };
  let last = '';
  let placed = false;
  let stopped = false;
  /** @type {HTMLElement | null} */
  let lastPopup = null;
  /** @type {HTMLElement | null} */
  let lastArrow = null;

  function update() {
    if (stopped) return false;
    const options = getOptions();
    const anchor = getAnchor();
    if (!anchor) return false;
    const a = anchorRect(anchor);
    // An anchor that left the page or stopped rendering: stay where we were.
    if (!a) return false;

    const popup = parts.popup?.() ?? null;
    const arrow = parts.arrow?.() ?? null;
    const view = viewport();
    // The popup's own size: CSS can size the positioner from --positioner-width/--positioner-height
    // (Navigation Menu's does), so measuring the positioner would only read back the last value.
    const box = popup?.isConnected && popup.offsetWidth > 0 ? popup : el;
    const size = { width: box.offsetWidth, height: box.offsetHeight };
    const arrowSize = arrow ? { width: arrow.offsetWidth, height: arrow.offsetHeight } : null;
    const resolved = {
      side: options.side ?? 'bottom',
      align: options.align ?? 'center',
      sideOffset: Number(options.sideOffset ?? 0),
      alignOffset: Number(options.alignOffset ?? 0),
      collisionPadding: Number(options.collisionPadding ?? 5),
      flip: options.flip ?? true,
      sticky: options.sticky ?? false,
      fallbackAxis: options.fallbackAxis ?? false,
    };
    const arrowPadding = Number(options.arrowPadding ?? 5);
    const rtl = getComputedStyle(el).direction === 'rtl';
    const key = [a.x, a.y, a.width, a.height, size.width, size.height, view.x, view.y, view.width, view.height,
      arrowSize?.width, arrowSize?.height, ...Object.values(resolved), arrowPadding, rtl].join();
    if (key === last && popup === lastPopup && arrow === lastArrow) return false;
    last = key;
    lastPopup = popup;
    lastArrow = arrow;

    // The first placement never animates: a popup coming into view has nowhere to move from.
    const first = !placed;
    const transition = el.style.transition;
    if (first) el.style.transition = 'none';

    const result = computePlacement(a, size, view, resolved, rtl);
    writer.style('left', `${Math.round(result.x)}px`);
    writer.style('top', `${Math.round(result.y)}px`);
    if (first) {
      placed = true;
      void getComputedStyle(el).left; // apply before transitions come back
      el.style.transition = transition;
    }

    writer.style('--anchor-width', `${a.width}px`);
    writer.style('--anchor-height', `${a.height}px`);
    writer.style('--available-width', `${Math.floor(result.available.width)}px`);
    writer.style('--available-height', `${Math.floor(result.available.height)}px`);
    writer.style('--positioner-width', `${size.width}px`);
    writer.style('--positioner-height', `${size.height}px`);
    const [ox, oy] = transformOrigin(result.side, result.align, rtl, a, size, result);
    writer.style('--transform-origin', `${Math.round(ox)}px ${Math.round(oy)}px`);
    writer.attribute('data-side', result.side);
    writer.attribute('data-align', result.align);

    if (popup) {
      const w = writerOf(popup);
      w.attribute('data-side', result.side);
      w.attribute('data-align', result.align);
      const hidden = a.y + a.height < view.y || a.y > view.y + view.height || a.x + a.width < view.x || a.x > view.x + view.width;
      w.attribute('data-anchor-hidden', hidden ? '' : null);
    }

    if (arrow && arrowSize) {
      const along = vertical(result.side);
      const length = along ? arrowSize.width : arrowSize.height;
      const center = along ? a.x + a.width / 2 - result.x : a.y + a.height / 2 - result.y;
      const ideal = center - length / 2;
      const max = (along ? size.width : size.height) - length - arrowPadding;
      const offset = Math.round(clamp(ideal, arrowPadding, max));
      const w = writerOf(arrow);
      w.style('position', 'absolute');
      w.style(along ? 'left' : 'top', `${offset}px`);
      arrow.style.removeProperty(along ? 'top' : 'left');
      w.attribute('data-side', result.side);
      w.attribute('data-align', result.align);
      w.attribute('data-uncentered', Math.abs(offset - ideal) > 0.5 ? '' : null);
    }
    return true;
  }

  let frame = 0;
  let still = 0;
  function loop() {
    frame = 0;
    still = update() ? 0 : still + 1;
    if (still < STILL_FRAMES) frame = requestAnimationFrame(loop);
  }
  function wake() {
    still = 0;
    frame ||= requestAnimationFrame(loop);
  }
  const unwatch = watchMoves(wake, el);
  // While asleep, a slow check catches what no event reports (e.g. an anchor moved by an animation).
  const slow = setInterval(() => {
    if (!frame && update()) wake();
  }, SLOW_CHECK_MS);
  update();
  wake();

  return Object.assign(() => {
    stopped = true;
    cancelAnimationFrame(frame);
    clearInterval(slow);
    unwatch();
  }, {
    /** Checks now (e.g. after options changed). */
    update() {
      last = '';
      update();
      wake();
    },
  });
}
