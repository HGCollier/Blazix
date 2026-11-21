// @ts-check
//
// The browser side of Drawer: a dialog on the shared popup layer that can be swiped away. While a
// finger or the pointer drags the popup toward its swipe direction, --drawer-swipe-movement-x/y on
// the popup and --drawer-swipe-progress (0 to 1) on the backdrop follow it, with data-swiping; a
// release past a quarter of its size, or a quick flick, closes it, and anything less springs back
// (your CSS transitions the variables back to 0). A swipe area opens the drawer the same way.
// Content that scrolls takes the gesture until it's scrolled to the edge.
//
// Touch: a finger dragging the popup would scroll the page instead (the browser cancels the
// pointer), so while the drawer is open a non-passive touchmove listener claims the gesture on its
// first move. It leaves the gesture to content that scrolls when the drag goes away from closing,
// and to something that scrolls across the swipe axis (a carousel...) when it goes that way. A
// finger can start a swipe on a button or a link; the mouse can't.
//
// Snap points (bottom and top drawers): the popup rests at one of several heights, translated by
// --drawer-snap-point-offset. A release settles on the snap point nearest to where the drag (plus
// a flick's momentum) would land, or closes the drawer when that's nearer to closed. The popup goes
// there at once and .NET is told, which renders back where it rests: there, or where it was if the
// change is canceled. With two or more, the backdrop's --drawer-swipe-progress at rest is where the
// popup sits between the two most open ones (0 at the most open, 1 at the next), so the overlay can
// stay lighter while the sheet peeks.
//
// Software keyboards (Drawer.VirtualKeyboardProvider): while the visual viewport is shorter than the
// layout viewport by more than a keyboard's worth, --drawer-keyboard-inset holds the difference,
// the focused field scrolls into the part left visible, and the page behind stays where it was.
//
// Variables: --drawer-height (the popup's height), --drawer-swipe-progress (0 to 1, on
// the popup, its backdrop and the drawer it's nested in), --drawer-swipe-strength (how fast a release
// was, 0.1 to 1, to scale its transition), --nested-drawers (rendered by .NET) and, on a drawer with
// one open inside it, --drawer-frontmost-height (the innermost open drawer's height). Blazor rewrites
// the style attribute when it renders a new one, so these are put back after it does.

import { byId, call, on } from './core/dom.js';
import { createPopup } from './core/popup.js';
import { finishTransitions } from './core/presence.js';

/** @typedef {{ swipeDirection: 'up' | 'down' | 'left' | 'right', swipeAreaId?: string | null }} DrawerOptions */
/** @typedef {{ index: number, height: number, offset: number }} SnapPoint */
/**
 * A press that may become a swipe. `distance` runs toward the swipe direction (positive closes). A
 * touch moves the popup only once its touchmove has `claimed` the gesture.
 * @typedef {{
 *   pointerId: number, touch: boolean, x: number, y: number, t: number, distance: number,
 *   opening: boolean, moved: boolean, claimed: boolean, pressed: boolean,
 *   scroller: HTMLElement | null, across: boolean,
 *   sample: { distance: number, t: number }, velocity: number, stationary: boolean,
 * }} Drag
 */

const FLICK_VELOCITY = 0.5; // px per ms
const CLOSE_FRACTION = 0.25;
const MIN_SWIPE = 10;
const SNAP_VELOCITY_THRESHOLD = 0.5;
const SNAP_VELOCITY_MULTIPLIER = 300;
const MAX_SNAP_VELOCITY = 4;
// A release more than this long after the last move is a hold, not a flick (ms).
const MAX_RELEASE_VELOCITY_AGE = 80;
const MIN_SAMPLE_DURATION = 4;
// The shortest time a drag's average speed is taken over (ms).
const MIN_AVERAGE_DURATION = 50;
// How far a finger goes along an axis, and by how much more than along the other, to be dragging along it (px).
const AXIS_LOCK_SLOP = 6;
const AXIS_LOCK_BIAS = 2;

// Controls keep a mouse press for themselves; a finger can still swipe from a button or a link.
const POINTER_IGNORE = 'button,a,input,select,textarea,label,[role="button"],[contenteditable]';
const TOUCH_IGNORE = 'input,select,textarea,[contenteditable]';
// Opt-outs: the attribute and its shorter name. Marked with the other axis ("x" on a bottom
// sheet), an element takes only touch drags across the swipe axis.
const SWIPE_IGNORE = ['data-blazix-swipe-ignore', 'data-swipe-ignore'];

const KEYBOARD_THRESHOLD = 60;
const KEYBOARD_INPUTS = new Set(['email', 'number', 'password', 'search', 'tel', 'text', 'url']);

/** Whether focusing an element brings up a software keyboard. @param {Element | null} el */
const typesText = (el) => !!el && (/** @type {HTMLElement} */ (el).isContentEditable || el instanceof HTMLTextAreaElement
  || (el instanceof HTMLInputElement && KEYBOARD_INPUTS.has(el.type)));

const clamp = (/** @type {number} */ value, /** @type {number} */ min, /** @type {number} */ max) => Math.min(Math.max(value, min), max);

/** A snap point's height in pixels: a fraction of the viewport (up to 1), pixels, or a px/rem string. */
function snapHeight(/** @type {number | string} */ point, /** @type {number} */ viewport) {
  if (typeof point === 'number') return point <= 1 ? clamp(point, 0, 1) * viewport : point;
  const text = point.trim();
  const value = Number.parseFloat(text);
  if (!Number.isFinite(value)) return null;
  if (text.endsWith('rem')) return value * (Number.parseFloat(getComputedStyle(document.documentElement).fontSize) || 16);
  return text.endsWith('px') ? value : null;
}

const closestIndex = (/** @type {number[]} */ values, /** @type {number} */ target) => {
  let best = -1;
  let distance = Infinity;
  values.forEach((value, index) => {
    if (Math.abs(value - target) < distance) {
      distance = Math.abs(value - target);
      best = index;
    }
  });
  return best;
};

/** Whether something between the target and the popup can still scroll the way the swipe goes. */
function scrollsFirst(/** @type {Element} */ target, /** @type {Element} */ popup, /** @type {string} */ direction) {
  for (let el = /** @type {Element | null} */ (target); el && el !== popup.parentElement; el = el.parentElement) {
    if (!(el instanceof HTMLElement)) continue;
    const style = getComputedStyle(el);
    if (direction === 'down' && /auto|scroll/.test(style.overflowY) && el.scrollTop > 0) return true;
    if (direction === 'up' && /auto|scroll/.test(style.overflowY) && el.scrollTop + el.clientHeight < el.scrollHeight - 1) return true;
    if (direction === 'right' && /auto|scroll/.test(style.overflowX) && el.scrollLeft > 0) return true;
    if (direction === 'left' && /auto|scroll/.test(style.overflowX) && el.scrollLeft + el.clientWidth < el.scrollWidth - 1) return true;
  }
  return false;
}

/** The nearest element from the target up to the popup that scrolls along an axis and has content to scroll. */
function scrollerFor(/** @type {Element} */ target, /** @type {Element} */ popup, /** @type {boolean} */ y) {
  for (let el = /** @type {Element | null} */ (target); el && el !== popup.parentElement; el = el.parentElement) {
    if (!(el instanceof HTMLElement)) continue;
    const overflow = y ? getComputedStyle(el).overflowY : getComputedStyle(el).overflowX;
    if (/auto|scroll/.test(overflow) && (y ? el.scrollHeight > el.clientHeight : el.scrollWidth > el.clientWidth)) return el;
  }
  return null;
}

/** Whether the page's text selection is in the popup. */
const selectionIn = (/** @type {Element} */ popup) => {
  const selection = document.getSelection();
  return !!selection && !selection.isCollapsed && (popup.contains(selection.anchorNode) || popup.contains(selection.focusNode));
};

/** Whether text is selected in the popup, so a finger there is moving a selection handle. */
function selecting(/** @type {Element} */ popup) {
  const active = document.activeElement;
  if ((active instanceof HTMLInputElement || active instanceof HTMLTextAreaElement) && popup.contains(active)) {
    try {
      if ((active.selectionStart ?? 0) < (active.selectionEnd ?? 0)) return true;
    } catch { /* an input type without a selection */ }
  }
  return selectionIn(popup);
}

/**
 * What a press on the target leaves to it: everything ('ignore'), only touch drags across the
 * swipe axis ('across'), or nothing (null).
 */
function optOut(/** @type {Element} */ target, /** @type {boolean} */ touch, /** @type {string} */ crossAxis) {
  for (const name of SWIPE_IGNORE) {
    const el = target.closest(`[${name}]`);
    if (el) return touch && el.getAttribute(name) === crossAxis ? 'across' : 'ignore';
  }
  return target.closest(touch ? TOUCH_IGNORE : POINTER_IGNORE) ? 'ignore' : null;
}

/**
 * @param {import('./core/dom.js').DotNetRef} dotnet
 * @param {string} triggerId
 * @param {DrawerOptions} initial
 */
export function createDrawer(dotnet, triggerId, initial) {
  let id = triggerId;
  let options = initial;
  let open = false;
  /** @type {Drag | null} */
  let drag = null;
  /** @type {{ snapPoints?: (number | string)[] | null, snapIndex?: number, sequential?: boolean, keyboard?: boolean, modal?: boolean | string | null } | null} */
  let snapOptions = null;
  /** @type {(() => void) | null} */
  let stopKeyboard = null;
  /** @type {(() => void) | null} */
  let stopTouch = null;
  /** The variables this module set on the popup, to put back when Blazor rewrites its style. */
  /** @type {Map<string, string>} */
  const vars = new Map();
  /** @type {MutationObserver | null} */
  let styleWatch = null;
  /** @type {ResizeObserver | null} */
  let sizeWatch = null;
  /** @type {HTMLElement | null} */
  let watched = null;
  /** @type {HTMLElement | null} */
  let watchedViewport = null;

  function setVar(/** @type {string} */ name, /** @type {string} */ value) {
    vars.set(name, value);
    popupEl()?.style.setProperty(name, value);
  }

  /** The drawer this one is nested in (its popup is an ancestor in the DOM). */
  const parentDrawer = () => /** @type {HTMLElement | null} */ (popupEl()?.parentElement?.closest('[data-blazix-drawer]') ?? null);

  function watchPopup() {
    const el = popupEl();
    if (!el) return;
    const frame = viewportEl();
    if (el !== watched) {
      styleWatch?.disconnect();
      styleWatch = new MutationObserver(() => {
        for (const [name, value] of vars) {
          if (el.style.getPropertyValue(name) !== value) el.style.setProperty(name, value);
        }
      });
      styleWatch.observe(el, { attributes: true, attributeFilter: ['style'] });
    }
    if (el !== watched || frame !== watchedViewport) {
      sizeWatch?.disconnect();
      // Snap points sit at heights from the popup's and the viewport's: either resizing moves them.
      sizeWatch = new ResizeObserver(() => {
        measure();
        if (open && !drag?.moved) rest();
      });
      sizeWatch.observe(el);
      if (frame) sizeWatch.observe(frame);
    }
    watched = el;
    watchedViewport = frame;
  }

  /** --drawer-height here, and --drawer-frontmost-height up the stack when this is the innermost open drawer. */
  function measure() {
    const el = popupEl();
    if (!el) return;
    const height = `${el.offsetHeight}px`;
    setVar('--drawer-height', height);
    if (!open || el.querySelector('[data-blazix-drawer][data-open]')) return;
    for (let parent = parentDrawer(); parent; parent = /** @type {HTMLElement | null} */ (parent.parentElement?.closest('[data-blazix-drawer]') ?? null)) {
      parent.style.setProperty('--drawer-frontmost-height', height);
    }
  }

  function clearStack() {
    for (let parent = parentDrawer(); parent; parent = /** @type {HTMLElement | null} */ (parent.parentElement?.closest('[data-blazix-drawer]') ?? null)) {
      parent.style.removeProperty('--drawer-frontmost-height');
      parent.style.setProperty('--drawer-swipe-progress', '0');
      parent.removeAttribute('data-nested-drawer-swiping');
    }
  }

  const trigger = () => byId(id);
  const popup = createPopup({ trigger, requestClose: (reason) => call(dotnet, 'RequestClose', reason) });
  const popupEl = () => popup.popup;
  const backdrop = () => /** @type {HTMLElement | null} */ (byId(popup.options?.backdrops?.[0])?.firstElementChild ?? null);
  const vertical = () => options.swipeDirection === 'up' || options.swipeDirection === 'down';
  // Movement toward the swipe direction is positive.
  const along = (/** @type {number} */ dx, /** @type {number} */ dy) => {
    switch (options.swipeDirection) {
      case 'up': return -dy;
      case 'left': return -dx;
      case 'right': return dx;
      default: return dy;
    }
  };

  /** The element that carries --drawer-keyboard-inset: the viewport, or the popup without one. */
  const keyboardRoot = () => /** @type {HTMLElement | null} */ (byId(popup.options?.hosts.at(-1))?.firstElementChild ?? popupEl());

  /** The Drawer.Viewport around the popup, if it has one. */
  const viewportEl = () => {
    const first = /** @type {HTMLElement | null} */ (byId(popup.options?.hosts.at(-1))?.firstElementChild ?? null);
    const el = popupEl();
    return first && el && first !== el && first.contains(el) ? first : null;
  };

  function watchKeyboard() {
    const visual = window.visualViewport;
    if (!visual) return () => {};
    const baseX = window.scrollX;
    const baseY = window.scrollY;
    let inset = 0;
    const measure = () => {
      const root = keyboardRoot();
      if (!root) return;
      const covered = visual.scale === 1 ? window.innerHeight - (visual.offsetTop + visual.height) : 0;
      inset = covered > KEYBOARD_THRESHOLD ? Math.ceil(covered) : 0;
      root.style.setProperty('--drawer-keyboard-inset', `${inset}px`);
      const active = document.activeElement;
      if (inset > 0 && typesText(active) && popupEl()?.contains(active)) {
        requestAnimationFrame(() => /** @type {HTMLElement} */ (active).scrollIntoView({ block: 'nearest' }));
      }
    };
    // The browser's own reveal scroll moves the page behind a modal sheet: put it back.
    const pin = () => {
      if (snapOptions?.modal === true && inset > 0 && (window.scrollX !== baseX || window.scrollY !== baseY)) {
        window.scrollTo({ left: baseX, top: baseY, behavior: 'instant' });
      }
    };
    const offs = [
      on(visual, 'resize', measure),
      on(visual, 'scroll', measure),
      on(window, 'scroll', pin),
      on(document, 'focusin', (/** @type {FocusEvent} */ event) => {
        if (inset > 0 && typesText(/** @type {Element} */ (event.target))) measure();
      }),
    ];
    measure();
    return () => {
      for (const off of offs) off();
      keyboardRoot()?.style.removeProperty('--drawer-keyboard-inset');
    };
  }

  const hasSnaps = () => vertical() && (snapOptions?.snapPoints?.length ?? 0) > 0;

  /** The viewport's height, which fractions are of: the Drawer.Viewport's, or the window's without one. */
  const viewportHeight = () => viewportEl()?.offsetHeight || document.documentElement.clientHeight;

  /** The snap points resolved against the viewport and the popup: the offset is how far it's moved toward closed. */
  function snaps() {
    const el = popupEl();
    /** @type {SnapPoint[]} */
    const points = [];
    if (!el || !hasSnaps()) return { points, viewport: 0, max: 0 };
    const viewport = viewportHeight();
    const height = el.offsetHeight;
    const max = Math.min(height, viewport);
    (snapOptions?.snapPoints ?? []).forEach((point, index) => {
      const px = snapHeight(point, viewport);
      if (px === null) return;
      const clamped = clamp(px, 0, max);
      // Two points at the same height are one: the later wins.
      const same = points.findIndex((other) => Math.abs(other.height - clamped) <= 1);
      if (same >= 0) points.splice(same, 1);
      points.push({ index, height: clamped, offset: Math.max(0, height - clamped) });
    });
    return { points, viewport, max };
  }

  /** The snap point the popup rests at: the active one, else the nearest to it (it merged into a later one), else the first. */
  function activeSnap() {
    const { points, viewport, max } = snaps();
    const index = snapOptions?.snapIndex ?? -1;
    const exact = points.find((point) => point.index === index);
    if (exact) return exact;
    const wanted = index >= 0 ? snapOptions?.snapPoints?.[index] : undefined;
    const px = wanted === undefined ? null : snapHeight(wanted, viewport);
    if (px === null) return points[0] ?? null;
    return points[closestIndex(points.map((point) => point.height), clamp(px, 0, max))] ?? null;
  }

  /** With two or more snap points: the offsets of the two most open, which the backdrop's progress runs between. */
  function snapRange() {
    const { points } = snaps();
    if (points.length < 2) return null;
    const offsets = points.map((point) => point.offset).sort((a, b) => a - b);
    const span = offsets[1] - offsets[0];
    return span > 0 ? { min: offsets[0], span } : null;
  }

  function applySnap() {
    const el = popupEl();
    if (!el) return;
    const offset = hasSnaps() ? activeSnap()?.offset ?? 0 : 0;
    setVar('--drawer-snap-point-offset', `${options.swipeDirection === 'up' ? -offset : offset}px`);
  }

  /** Where the popup rests: at its snap point, with the backdrop's progress for it (0 without snap points). */
  function rest() {
    applySnap();
    const range = snapRange();
    const point = range ? activeSnap() : null;
    const progress = range && point ? clamp((point.offset - range.min) / range.span, 0, 1) : 0;
    const shade = backdrop();
    shade?.style.setProperty('--drawer-swipe-progress', String(progress));
    // The backdrop's exit lasts 400ms times this. It stays 1 however fast the popup is swiped away.
    shade?.style.setProperty('--drawer-swipe-strength', '1');
  }

  function paint(/** @type {number} */ distance) {
    const el = popupEl();
    if (!el) return;
    const size = vertical() ? el.offsetHeight : el.offsetWidth;
    const base = hasSnaps() ? activeSnap()?.offset ?? 0 : 0;
    // Dragging past fully open stretches a little, then stops.
    const next = base + distance;
    const moved = next >= 0 ? distance : -Math.sqrt(-next) - base;
    const sign = options.swipeDirection === 'up' || options.swipeDirection === 'left' ? -1 : 1;
    setVar(vertical() ? '--drawer-swipe-movement-y' : '--drawer-swipe-movement-x', `${sign * moved}px`);
    // Between snap points, where the popup is between the two most open; otherwise how far toward closed.
    const range = snapRange();
    const value = range ? (clamp(next, 0, size) - range.min) / range.span : size > 0 && distance > 0 ? distance / size : 0;
    const progress = String(clamp(value, 0, 1));
    setVar('--drawer-swipe-progress', progress);
    const shade = backdrop();
    shade?.style.setProperty('--drawer-swipe-progress', progress);
    shade?.setAttribute('data-swiping', '');
    // The drawer behind grows back toward its full size as this one is swiped away.
    const parent = parentDrawer();
    parent?.style.setProperty('--drawer-swipe-progress', progress);
    parent?.setAttribute('data-nested-drawer-swiping', '');
  }

  /**
   * Where a release with snap points lands: a snap point or closed. A new snap point applies at once
   * and goes to .NET.
   * @param {number} distance toward closed
   * @param {number} velocity at the release, toward closed (px per ms)
   * @param {number} average over the whole drag
   * @returns {boolean} whether it closes
   */
  function releaseOnSnaps(distance, velocity, average) {
    const el = popupEl();
    const list = snaps().points;
    const height = el?.offsetHeight ?? 0;
    if (!el || !list.length || !height) return false;
    const current = activeSnap()?.offset ?? 0;
    const direction = Math.sign(distance);
    // A reversal at the end of the drag doesn't flip the decision: the drag's average speed counts instead.
    const reversed = direction !== 0 && Math.abs(distance) >= MIN_SWIPE && Math.sign(velocity) !== 0 && Math.sign(velocity) !== direction;
    const speed = reversed ? average : velocity;
    const dragTarget = clamp(current + distance, 0, height);
    const momentum = Math.abs(speed) >= SNAP_VELOCITY_THRESHOLD ? clamp(speed, -MAX_SNAP_VELOCITY, MAX_SNAP_VELOCITY) * SNAP_VELOCITY_MULTIPLIER : 0;
    const target = snapOptions?.sequential ? dragTarget : clamp(dragTarget + momentum, 0, height);
    const settle = (/** @type {SnapPoint} */ point) => {
      if (snapOptions && point.index !== snapOptions.snapIndex) {
        // It goes there now, rather than back first and then there a round trip later. .NET renders
        // back where it rests, which moves it back if the change is canceled.
        snapOptions.snapIndex = point.index;
        call(dotnet, 'Notify', 'snap', String(point.index));
      }
      return false;
    };

    if (snapOptions?.sequential) {
      const ordered = [...list].sort((a, b) => a.offset - b.offset);
      const offsets = ordered.map((point) => point.offset);
      const at = closestIndex(offsets, current);
      let point = ordered[closestIndex(offsets, target)];
      let landing = target;
      if (direction !== 0 && Math.sign(speed) === direction && Math.abs(speed) >= SNAP_VELOCITY_THRESHOLD) {
        const adjacent = clamp(at + direction, 0, ordered.length - 1);
        if (adjacent === at && direction > 0) return true;
        const beyond = direction > 0 ? target < ordered[adjacent].offset : target > ordered[adjacent].offset;
        if (adjacent !== at && beyond) {
          point = ordered[adjacent];
          landing = point.offset;
        }
      }
      return Math.abs(landing - height) < Math.abs(landing - point.offset) ? true : settle(point);
    }

    const point = list[closestIndex(list.map((p) => p.offset), target)];
    if (speed >= FLICK_VELOCITY && distance > 0) return true;
    return Math.abs(target - height) < Math.abs(target - point.offset) ? true : settle(point);
  }

  /** Ends a swipe's movement: the popup transitions back to rest, at its (maybe new) snap point. */
  function reset() {
    const el = popupEl();
    el?.removeAttribute('data-swiping');
    setVar('--drawer-swipe-movement-x', '0px');
    setVar('--drawer-swipe-movement-y', '0px');
    setVar('--drawer-swipe-progress', '0');
    backdrop()?.removeAttribute('data-swiping');
    const parent = parentDrawer();
    parent?.style.setProperty('--drawer-swipe-progress', '0');
    parent?.removeAttribute('data-nested-drawer-swiping');
    rest();
  }

  /** Starts tracking a press that may become a swipe. @param {PointerEvent} event @param {Partial<Drag>} extra */
  const press = (event, extra) => /** @type {Drag} */ ({
    pointerId: event.pointerId, touch: event.pointerType === 'touch', x: event.clientX, y: event.clientY, t: event.timeStamp,
    distance: 0, opening: false, moved: false, claimed: false, pressed: false, scroller: null, across: false,
    sample: { distance: 0, t: event.timeStamp }, velocity: 0, stationary: false, ...extra,
  });

  /**
   * Records a move for the release velocity: the speed of the last move. One move that went nowhere
   * just before a release is skipped (some Android devices send one); two in a row mean it stopped.
   */
  function sample(/** @type {Drag} */ d, /** @type {number} */ time) {
    const last = d.sample;
    if (time > last.t) {
      const stationary = Math.abs(d.distance - last.distance) < 1;
      const skip = stationary && !d.stationary;
      d.stationary = stationary;
      if (skip) return;
      d.velocity = (d.distance - last.distance) / Math.max(time - last.t, MIN_SAMPLE_DURATION);
    }
    d.sample = { distance: d.distance, t: time };
  }

  /** How fast the drag went toward closed as it was released (px per ms): 0 after a hold. */
  function releaseVelocity(/** @type {Drag} */ d, /** @type {number} */ time) {
    const age = time - d.sample.t;
    if (age > MAX_RELEASE_VELOCITY_AGE) return 0;
    const moved = d.distance - d.sample.distance;
    return Math.abs(moved) >= 1 ? moved / Math.max(age, MIN_SAMPLE_DURATION) : d.velocity;
  }

  /** Moves the popup with a drag that's the drawer's, from 3px along the swipe axis. */
  function follow(/** @type {Drag} */ d) {
    if (!d.moved) {
      if (Math.abs(d.distance) < 3) return;
      d.moved = true;
      // A mouse swipe that started over text has begun selecting it.
      const el = popupEl();
      if (!d.touch && el && selectionIn(el)) document.getSelection()?.removeAllRanges();
    }
    popupEl()?.setAttribute('data-swiping', '');
    paint(d.distance);
  }

  /** A press ends: a swipe settles at a snap point, closes the drawer, or springs back. */
  function end(/** @type {PointerEvent} */ event, /** @type {string} */ type) {
    const d = drag;
    drag = null;
    // A touch the drawer never claimed was a tap, or the page's own gesture.
    if (!d || d.opening || (d.touch && !d.claimed)) return;
    const el = popupEl();
    const size = el ? (vertical() ? el.offsetHeight : el.offsetWidth) : 0;
    const elapsed = event.timeStamp - d.t;
    const velocity = d.distance / Math.max(1, elapsed);
    const recent = releaseVelocity(d, event.timeStamp);
    const released = type === 'pointerup' && d.moved;
    const closes = hasSnaps()
      ? released && releaseOnSnaps(d.distance, recent, d.distance / Math.max(MIN_AVERAGE_DURATION, elapsed))
      : released && (d.distance > size * CLOSE_FRACTION || (velocity > FLICK_VELOCITY && d.distance > MIN_SWIPE));
    if (closes) {
      // A fast release finishes faster: the exit transition scales with --drawer-swipe-strength.
      setVar('--drawer-swipe-strength', String(clamp(1 / (1 + Math.abs(recent)), 0.1, 1)));
      el?.setAttribute('data-swipe-dismiss', '');
      el?.removeAttribute('data-swiping');
      backdrop()?.removeAttribute('data-swiping');
      call(dotnet, 'RequestClose', 'swipe');
    } else if (d.moved) {
      reset();
    }
  }

  /**
   * A finger's moves while the drawer is open. The first one decides whether the drawer or the page
   * takes the gesture; after that, the drawer's keep the page from scrolling.
   */
  function onTouchMove(/** @type {TouchEvent} */ event) {
    const d = drag;
    // A second finger is a pinch: the browser's.
    if (!d?.touch || d.opening || event.touches.length !== 1) return;
    if (d.claimed) {
      if (event.cancelable) event.preventDefault();
      return;
    }
    // The browser has already begun scrolling.
    if (!event.cancelable) {
      drag = null;
      return;
    }
    const touch = event.touches[0];
    const main = along(touch.clientX - d.x, touch.clientY - d.y);
    const cross = vertical() ? touch.clientX - d.x : touch.clientY - d.y;
    if (d.across) {
      // Under the finger is something that scrolls across the swipe axis (a carousel...): a drag that way is its.
      if (Math.abs(cross) >= AXIS_LOCK_SLOP && Math.abs(cross) > Math.abs(main) + AXIS_LOCK_BIAS) {
        drag = null;
        return;
      }
      // Too early to tell. Leave this move be: on iOS, preventing it would stop that scroll for the whole gesture.
      if (Math.abs(main) < AXIS_LOCK_SLOP) return;
    }
    // Content that scrolls takes a drag away from closing. (It's at the edge it closes from, or the press wouldn't be a drag.)
    if (d.scroller && main < 0) {
      drag = null;
      return;
    }
    d.claimed = true;
    event.preventDefault();
    d.distance = main;
    follow(d);
  }

  const offs = [
    on(document, 'pointerdown', (/** @type {PointerEvent} */ event) => {
      if (event.button !== 0 || !event.isPrimary) return;
      const target = /** @type {Element} */ (event.target);
      const area = byId(options.swipeAreaId);
      if (!open && area?.contains(target) && !area.hasAttribute('data-disabled')) {
        drag = press(event, { opening: true });
        return;
      }
      const el = popupEl();
      // While another drawer is open on top of this one, that one takes the swipe.
      if (!open || !el?.contains(target) || el.hasAttribute('data-nested-drawer-open')) return;
      const touch = event.pointerType === 'touch';
      const leave = optOut(target, touch, vertical() ? 'x' : 'y');
      if (leave === 'ignore' || (touch && selecting(el)) || scrollsFirst(target, el, options.swipeDirection)) return;
      drag = press(event, touch ? {
        scroller: scrollerFor(target, el, vertical()),
        across: leave === 'across' || !!scrollerFor(target, el, !vertical()),
      } : {});
    }),
    on(document, 'pointermove', (/** @type {PointerEvent} */ event) => {
      if (!drag || event.pointerId !== drag.pointerId) return;
      if (!drag.touch) {
        // A move without the button, after moves with it: the button came up outside the window.
        if (event.buttons & 1) drag.pressed = true;
        else if (drag.pressed) {
          end(event, 'pointerup');
          return;
        }
      }
      drag.distance = along(event.clientX - drag.x, event.clientY - drag.y);
      sample(drag, event.timeStamp);
      if (drag.opening) {
        // A swipe from the area toward the inside of the page opens the drawer.
        if (drag.distance < -10) {
          drag = null;
          call(dotnet, 'RequestOpen', 'swipe', null);
        }
        return;
      }
      // A finger moves the popup once its touchmove has claimed the gesture.
      if (drag.touch && !drag.claimed) return;
      follow(drag);
    }),
    ...['pointerup', 'pointercancel'].map((type) => on(document, type, (/** @type {PointerEvent} */ event) => {
      if (drag && event.pointerId === drag.pointerId) end(event, type);
    })),
  ];

  return {
    /** @param {import('./core/popup.js').ShowOptions & { keyboard?: boolean }} next */
    show(next) {
      const wasOpen = open;
      open = true;
      snapOptions = /** @type {any} */ (next);
      popup.show({ ...next, initialFocus: wasOpen ? 'none' : next.initialFocus ?? 'first' });
      if (!wasOpen) {
        popupEl()?.removeAttribute('data-swipe-dismiss');
        setVar('--drawer-swipe-strength', '1');
      }
      watchPopup();
      // A finger on the open drawer: its touchmoves can claim the gesture from the page's scrolling.
      stopTouch ??= on(document, 'touchmove', onTouchMove, { capture: true, passive: false });
      // Opening, or a new snap point: the popup transitions to its offset. A drag under way carries on from there.
      if (drag?.moved) {
        rest();
        paint(drag.distance);
      } else {
        reset();
      }
      measure();
      // Open since its first render: it appears at its snap point, and its backdrop at that point's
      // opacity, rather than moving there.
      if (!wasOpen && next.animate === false) finishTransitions([popupEl(), backdrop()]);
      if (next.keyboard && !stopKeyboard) stopKeyboard = watchKeyboard();
      else if (!next.keyboard && stopKeyboard) {
        stopKeyboard();
        stopKeyboard = null;
      }
    },
    /** @param {boolean} restore */
    hide(restore) {
      open = false;
      drag = null;
      stopTouch?.();
      stopTouch = null;
      stopKeyboard?.();
      stopKeyboard = null;
      clearStack();
      return popup.hide(restore, popup.options?.finalFocusId ?? null);
    },
    restoreFocus() {
      popup.restoreFocus();
    },
    /** @param {string} triggerIdNext @param {DrawerOptions} next */
    setTrigger(triggerIdNext, next) {
      id = triggerIdNext;
      options = next;
    },
    dispose() {
      for (const off of offs) off();
      stopTouch?.();
      stopKeyboard?.();
      styleWatch?.disconnect();
      sizeWatch?.disconnect();
      popup.dispose();
    },
  };
}
