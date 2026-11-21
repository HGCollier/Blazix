// @ts-check
//
// The browser side of Scroll Area: native scrolling in the viewport, custom scrollbars around it.
// Everything that changes as you scroll is written here directly (thumb size and position,
// data-scrolling, data-hovering, overflow attributes and variables, hiding scrollbars without
// overflow), since a Blazor round trip per scroll event would lag. Blazor renders the structure only.

import { byId, on } from './core/dom.js';

/** @typedef {{ overflowEdgeThreshold: number }} ScrollAreaOptions */

const MIN_THUMB = 16;
const SCROLL_END_MS = 300;

/**
 * @param {string} rootId
 * @param {ScrollAreaOptions} initial
 */
export function createScrollArea(rootId, initial) {
  let options = initial;
  let frame = 0;
  /** @type {ReturnType<typeof setTimeout> | undefined} */ let scrollTimer;
  /** @type {{ axis: 'x' | 'y', pointerId: number, start: number, scroll: number, ratio: number } | null} */ let drag = null;
  /** @type {(() => void)[]} */ let offs = [];
  /** @type {ResizeObserver | null} */ let sizes = null;
  /** @type {MutationObserver | null} */ let changes = null;

  const root = () => byId(rootId);
  const viewport = () => /** @type {HTMLElement | null} */ (root()?.querySelector('[data-blazix-scroll-viewport]') ?? null);
  const content = () => /** @type {HTMLElement | null} */ (viewport()?.querySelector('[data-blazix-scroll-content]') ?? null);
  const scrollbar = (/** @type {'x' | 'y'} */ axis) => /** @type {HTMLElement | null} */ (root()?.querySelector(`[data-blazix-scrollbar="${axis === 'y' ? 'vertical' : 'horizontal'}"]`) ?? null);
  const thumb = (/** @type {HTMLElement | null} */ bar) => /** @type {HTMLElement | null} */ (bar?.querySelector('[data-blazix-scroll-thumb]') ?? null);
  const corner = () => /** @type {HTMLElement | null} */ (root()?.querySelector('[data-blazix-scroll-corner]') ?? null);
  /** The scrollbar's inner length, without padding, which is what the thumb moves along. */
  const trackLength = (/** @type {HTMLElement} */ bar, /** @type {'x' | 'y'} */ axis) => {
    const s = getComputedStyle(bar);
    return axis === 'y'
      ? bar.clientHeight - parseFloat(s.paddingTop) - parseFloat(s.paddingBottom)
      : bar.clientWidth - parseFloat(s.paddingLeft) - parseFloat(s.paddingRight);
  };

  /** Sets or removes a presence attribute on every part that carries the scroll state. */
  function flag(/** @type {string} */ name, /** @type {boolean} */ present) {
    for (const el of [root(), viewport(), content(), scrollbar('x'), scrollbar('y')]) {
      if (el && el.hasAttribute(name) !== present) el.toggleAttribute(name, present);
    }
  }

  function update() {
    frame = 0;
    const vp = viewport();
    const r = root();
    if (!vp || !r) return;
    const t = options.overflowEdgeThreshold;
    const rtl = getComputedStyle(vp).direction === 'rtl';
    const maxY = vp.scrollHeight - vp.clientHeight;
    const maxX = vp.scrollWidth - vp.clientWidth;
    const top = vp.scrollTop;
    // Right to left, scrollLeft runs from 0 down to -maxX.
    const left = rtl ? -vp.scrollLeft : vp.scrollLeft;
    const hasY = maxY > 1;
    const hasX = maxX > 1;
    flag('data-has-overflow-y', hasY);
    flag('data-has-overflow-x', hasX);
    flag('data-overflow-y-start', hasY && top > t);
    flag('data-overflow-y-end', hasY && top < maxY - t);
    flag('data-overflow-x-start', hasX && left > t);
    flag('data-overflow-x-end', hasX && left < maxX - t);
    vp.style.setProperty('--scroll-area-overflow-y-start', `${Math.max(0, top)}px`);
    vp.style.setProperty('--scroll-area-overflow-y-end', `${Math.max(0, maxY - top)}px`);
    vp.style.setProperty('--scroll-area-overflow-x-start', `${Math.max(0, left)}px`);
    vp.style.setProperty('--scroll-area-overflow-x-end', `${Math.max(0, maxX - left)}px`);

    for (const axis of /** @type {const} */ (['y', 'x'])) {
      const bar = scrollbar(axis);
      if (!bar) continue;
      const has = axis === 'y' ? hasY : hasX;
      // Scrollbars without anything to scroll are hidden, unless kept mounted.
      const hide = !has && !bar.hasAttribute('data-keep-mounted');
      if (bar.hidden !== hide) bar.hidden = hide;
      const th = thumb(bar);
      if (!th || !has) continue;
      const track = trackLength(bar, axis);
      const client = axis === 'y' ? vp.clientHeight : vp.clientWidth;
      const scroll = axis === 'y' ? vp.scrollHeight : vp.scrollWidth;
      const size = Math.max(MIN_THUMB, (track * client) / scroll);
      const max = axis === 'y' ? maxY : maxX;
      const position = axis === 'y' ? top : left;
      const offset = max > 0 ? (position / max) * (track - size) : 0;
      bar.style.setProperty(axis === 'y' ? '--scroll-area-thumb-height' : '--scroll-area-thumb-width', `${size}px`);
      th.style.transform = axis === 'y' ? `translate3d(0,${offset}px,0)` : `translate3d(${rtl ? -offset : offset}px,0,0)`;
    }

    const both = hasX && hasY;
    const c = corner();
    if (c && c.hidden !== !both) c.hidden = !both;
    const barY = scrollbar('y');
    const barX = scrollbar('x');
    r.style.setProperty('--scroll-area-corner-width', `${both && barY ? barY.offsetWidth : 0}px`);
    r.style.setProperty('--scroll-area-corner-height', `${both && barX ? barX.offsetHeight : 0}px`);

    // Keyboard users can reach and scroll content that has nothing focusable in it.
    const focusable = vp.querySelector('a[href],button,input,select,textarea,[tabindex]:not([tabindex="-1"])');
    const wanted = (hasX || hasY) && !focusable;
    if (wanted && !vp.hasAttribute('tabindex')) vp.setAttribute('tabindex', '0');
    else if (!wanted && vp.getAttribute('tabindex') === '0' && vp.dataset.blazixOwnTabindex !== undefined) vp.removeAttribute('tabindex');
    if (wanted) vp.dataset.blazixOwnTabindex = '';
  }

  const schedule = () => {
    frame ||= requestAnimationFrame(update);
  };

  function bind() {
    for (const off of offs) off();
    offs = [];
    sizes?.disconnect();
    changes?.disconnect();
    const vp = viewport();
    const r = root();
    if (!vp || !r) return;
    // Hide the native scrollbars; ours replace them.
    vp.style.scrollbarWidth = 'none';

    offs.push(on(vp, 'scroll', () => {
      schedule();
      flag('data-scrolling', true);
      clearTimeout(scrollTimer);
      scrollTimer = setTimeout(() => flag('data-scrolling', false), SCROLL_END_MS);
    }, { passive: true }));
    offs.push(on(r, 'pointerenter', () => {
      for (const bar of [scrollbar('x'), scrollbar('y')]) bar?.setAttribute('data-hovering', '');
    }));
    offs.push(on(r, 'pointerleave', () => {
      for (const bar of [scrollbar('x'), scrollbar('y')]) bar?.removeAttribute('data-hovering');
    }));

    // Dragging a thumb scrolls proportionally; pressing the track jumps there.
    offs.push(on(r, 'pointerdown', (/** @type {PointerEvent} */ event) => {
      if (event.button !== 0) return;
      const target = /** @type {Element} */ (event.target);
      const bar = /** @type {HTMLElement | null} */ (target.closest('[data-blazix-scrollbar]'));
      if (!bar || bar.closest('[data-blazix-scroll-area]') !== r) return;
      const axis = bar.dataset.blazixScrollbar === 'vertical' ? 'y' : 'x';
      const th = thumb(bar);
      const viewportEl = viewport();
      if (!th || !viewportEl) return;
      event.preventDefault();
      const track = trackLength(bar, axis);
      const size = axis === 'y' ? th.offsetHeight : th.offsetWidth;
      const max = axis === 'y' ? viewportEl.scrollHeight - viewportEl.clientHeight : viewportEl.scrollWidth - viewportEl.clientWidth;
      const ratio = track - size > 0 ? max / (track - size) : 0;
      if (!th.contains(target)) {
        const rect = bar.getBoundingClientRect();
        const at = axis === 'y' ? event.clientY - rect.top : event.clientX - rect.left;
        const scrollTo = (at - size / 2) * ratio;
        if (axis === 'y') viewportEl.scrollTop = scrollTo;
        else viewportEl.scrollLeft = scrollTo;
      }
      drag = { axis, pointerId: event.pointerId, start: axis === 'y' ? event.clientY : event.clientX, scroll: axis === 'y' ? viewportEl.scrollTop : viewportEl.scrollLeft, ratio };
      bar.setPointerCapture?.(event.pointerId);
    }));
    offs.push(on(document, 'pointermove', (/** @type {PointerEvent} */ event) => {
      const viewportEl = viewport();
      if (!drag || event.pointerId !== drag.pointerId || !viewportEl) return;
      const delta = (drag.axis === 'y' ? event.clientY : event.clientX) - drag.start;
      if (drag.axis === 'y') viewportEl.scrollTop = drag.scroll + delta * drag.ratio;
      else viewportEl.scrollLeft = drag.scroll + delta * drag.ratio;
    }));
    offs.push(on(document, 'pointerup', () => { drag = null; }));
    // The scrollbars sit outside the viewport: wheel over them still scrolls it.
    offs.push(on(r, 'wheel', (/** @type {WheelEvent} */ event) => {
      const bar = /** @type {Element} */ (event.target).closest('[data-blazix-scrollbar]');
      const viewportEl = viewport();
      if (!bar || !viewportEl) return;
      event.preventDefault();
      viewportEl.scrollBy({ top: event.deltaY, left: event.deltaX });
    }, { passive: false }));

    sizes = new ResizeObserver(schedule);
    sizes.observe(vp);
    const c = content();
    if (c) sizes.observe(c);
    for (const bar of [scrollbar('x'), scrollbar('y')]) if (bar) sizes.observe(bar);
    // Content that changes without resizing the content element (Blazor patches) is caught here.
    changes = new MutationObserver(schedule);
    changes.observe(r, { childList: true, subtree: true });
    update();
  }
  bind();

  return {
    /** @param {ScrollAreaOptions} next */
    update(next) {
      options = next;
      bind();
    },
    dispose() {
      cancelAnimationFrame(frame);
      clearTimeout(scrollTimer);
      for (const off of offs) off();
      sizes?.disconnect();
      changes?.disconnect();
    },
  };
}
