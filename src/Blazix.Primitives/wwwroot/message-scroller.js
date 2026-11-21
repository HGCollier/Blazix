// @ts-check
//
// The browser side of Message Scroller.
// Everything that reacts to layout runs here: following the end, anchoring a new turn
// near the top with a tail spacer, keeping the position when history is prepended, and the
// visibility of each message. Blazor renders the structure and hears only about state changes:
// the scrollable edges, the visibility snapshot and the end of the opening-position hold.

import { call, on } from './core/dom.js';

/**
 * @typedef {import('./core/dom.js').DotNetRef} DotNetRef
 * @typedef {'start' | 'end' | 'last-anchor'} DefaultScrollPosition
 * @typedef {{ autoScroll: boolean, defaultScrollPosition: DefaultScrollPosition, scrollEdgeThreshold: number,
 *   scrollPreviousItemPeek: number, scrollMargin: number }} Options
 * @typedef {'start' | 'center' | 'end' | 'nearest'} ScrollAlign
 * @typedef {{ align?: ScrollAlign, behavior?: ScrollBehavior, scrollMargin?: number | null }} ScrollOptions
 * @typedef {'following-bottom' | 'free-scrolling' | 'anchored-to-message' | 'settling-jump'} Mode
 */

const EPSILON = 0.5;
const AUTOSCROLLING_CLEAR_DELAY = 180;
const USER_SCROLL_KEYS = new Set(['ArrowDown', 'ArrowUp', 'End', 'Home', 'PageDown', 'PageUp', ' ']);

// --- Geometry (geometry.ts) ---

/** @param {string | undefined} value */
function px(value) {
  const number = Number.parseFloat(value ?? '');
  return Number.isFinite(number) ? number : 0;
}

/** @param {HTMLElement | null | undefined} element */
function blockPadding(element) {
  if (!element) return { start: 0, end: 0 };
  const style = getComputedStyle(element);
  return {
    start: px(style.paddingBlockStart || style.paddingTop),
    end: px(style.paddingBlockEnd || style.paddingBottom),
  };
}

/** @param {HTMLElement} content @param {HTMLElement | null} spacer */
function itemsOf(content, spacer) {
  return /** @type {HTMLElement[]} */ (Array.from(content.children).filter((child) => child instanceof HTMLElement && child !== spacer));
}

/** @param {HTMLElement} element */
const isAnchor = (element) => element.dataset.scrollAnchor === 'true';

/** @param {HTMLElement} element @param {HTMLElement} viewport */
const elementTop = (element, viewport) =>
  element.getBoundingClientRect().top - viewport.getBoundingClientRect().top + viewport.scrollTop;

/** @param {HTMLElement} element @param {HTMLElement} viewport */
const viewportTopOf = (element, viewport) => element.getBoundingClientRect().top - viewport.getBoundingClientRect().top;

/** @param {HTMLElement} viewport */
const maxScrollTop = (viewport) => Math.max(0, viewport.scrollHeight - viewport.clientHeight);

/** The bottom of the last item plus the content's end padding, in scroll coordinates (the spacer excluded). */
function contentBottom(/** @type {HTMLElement} */ content, /** @type {HTMLElement | null} */ spacer, /** @type {HTMLElement} */ viewport) {
  const padding = blockPadding(content);
  const top = viewport.getBoundingClientRect().top;
  let bottom = padding.start + padding.end;
  for (const item of itemsOf(content, spacer)) {
    bottom = Math.max(bottom, item.getBoundingClientRect().bottom - top + viewport.scrollTop + padding.end);
  }
  return bottom;
}

/**
 * @param {HTMLElement} element
 * @param {HTMLElement} viewport
 * @param {HTMLElement | null} spacer
 * @param {ScrollAlign} align
 * @param {number} margin
 */
function elementScrollTop(element, viewport, spacer, align, margin) {
  const top = elementTop(element, viewport);
  const height = element.getBoundingClientRect().height;
  const padding = blockPadding(spacer?.parentElement);
  if (align === 'center') {
    const inset = Math.max(0, viewport.clientHeight - padding.start - padding.end);
    return top - padding.start - (inset - height) / 2 - margin;
  }
  if (align === 'end') return top - viewport.clientHeight + height + padding.end + margin;
  if (align === 'nearest') {
    const bottom = top + height;
    const viewTop = viewport.scrollTop + padding.start;
    const viewBottom = viewport.scrollTop + viewport.clientHeight - padding.end;
    if (top >= viewTop && bottom <= viewBottom) return viewport.scrollTop;
    if (top < viewTop) return top - padding.start - margin;
    return bottom - viewport.clientHeight + padding.end + margin;
  }
  return top - padding.start - margin;
}

// --- Controller (use-message-scroller-controller.ts, use-message-scroller-commands.ts) ---

/**
 * @param {string} key the provider's key: its root, viewport and content carry it in data attributes
 * @param {DotNetRef} dotnet
 * @param {Options} initial
 */
export function createMessageScroller(key, dotnet, initial) {
  let options = initial;
  /** @type {Mode} */ let mode = options.autoScroll ? 'following-bottom' : 'free-scrolling';
  let defaultApplied = false;
  let pendingDefault = options.defaultScrollPosition !== 'start';
  let autoscrolling = false;
  /** @type {ReturnType<typeof setTimeout> | undefined} */ let autoscrollingTimer;
  let itemCount = 0;
  /** @type {HTMLElement | null} */ let firstItem = null;
  let lastScrollTop = 0;
  let spacerHeight = 0;
  let spacerGap = 0;
  /** @type {HTMLElement | null} */ let streamingTurn = null;
  /** @type {{ messageId: string, options: ScrollOptions } | null} */ let pendingScrollToMessage = null;
  /** @type {{ element: HTMLElement, viewportTop: number } | null} */ let prependAnchor = null;
  const handledAnchors = new WeakSet();
  let published = { start: false, end: false };

  let stateFrame = 0;
  let visibilityFrame = 0;
  let resizeFrame = 0;
  let watchingVisibility = false;
  /** @type {IntersectionObserver | null} */ let visibilityObserver = null;
  /** @type {Set<HTMLElement>} */ const observed = new Set();
  /** @type {Set<string>} */ const visibleIds = new Set();
  let visibility = { currentAnchorId: /** @type {string | null} */ (null), visibleMessageIds: /** @type {string[]} */ ([]) };

  /** @type {HTMLElement | null} */ let root = null;
  /** @type {HTMLElement | null} */ let viewport = null;
  /** @type {HTMLElement | null} */ let content = null;
  /** @type {HTMLElement | null} */ let spacer = null;
  /** @type {(() => void)[]} */ let offs = [];

  const find = (/** @type {string} */ part) =>
    /** @type {HTMLElement | null} */ (document.querySelector(`[data-blazix-message-scroller${part}="${CSS.escape(key)}"]`));

  const items = () => (content ? itemsOf(content, spacer) : []);

  const messageElement = (/** @type {string} */ id) => items().find((item) => item.dataset.messageId === id) ?? null;

  function clearPendingDefault() {
    if (!pendingDefault) return;
    pendingDefault = false;
    root?.removeAttribute('data-pending-scroll');
    viewport?.removeAttribute('data-pending-scroll');
    call(dotnet, 'PendingScrollCleared');
  }

  function markDefaultApplied() {
    defaultApplied = true;
    clearPendingDefault();
  }

  function writeStateAttributes() {
    const scrollable = [published.start && 'start', published.end && 'end'].filter(Boolean).join(' ');
    for (const element of [root, viewport]) {
      if (!element) continue;
      if (scrollable) element.setAttribute('data-scrollable', scrollable);
      else element.removeAttribute('data-scrollable');
      element.toggleAttribute('data-autoscrolling', autoscrolling);
    }
  }

  /** Arms following at the end, and releases it when the reader scrolls up (not when content grows). */
  function reconcileFollowMode(/** @type {{ start: boolean, end: boolean }} */ scrollable) {
    const scrollTop = viewport?.scrollTop ?? 0;
    const scrolledUp = scrollTop < lastScrollTop - EPSILON;
    lastScrollTop = scrollTop;
    if (options.autoScroll && !scrollable.end && spacerHeight === 0 && mode !== 'settling-jump' && mode !== 'anchored-to-message') {
      mode = 'following-bottom';
    } else if (mode === 'following-bottom' && scrollable.end && scrolledUp && !autoscrolling) {
      mode = 'free-scrolling';
    }
  }

  function commitScrollState() {
    let next = { start: false, end: false };
    if (viewport && content) {
      const bottom = contentBottom(content, spacer, viewport);
      next = {
        start: viewport.scrollTop > options.scrollEdgeThreshold,
        end: bottom - viewport.scrollTop - viewport.clientHeight > options.scrollEdgeThreshold,
      };
    }
    reconcileFollowMode(next);
    // While following, the gap a streamed chunk opens is about to close: don't strobe the button.
    if (mode === 'following-bottom') next.end = false;
    const changed = next.start !== published.start || next.end !== published.end;
    published = next;
    writeStateAttributes();
    if (changed) call(dotnet, 'Scrollable', next.start, next.end);
  }

  function scheduleStateCommit() {
    if (stateFrame) return;
    stateFrame = requestAnimationFrame(() => {
      stateFrame = 0;
      commitScrollState();
    });
  }

  function computeVisibility() {
    if (!content || !viewport) return { currentAnchorId: null, visibleMessageIds: [] };
    const viewportRect = viewport.getBoundingClientRect();
    const line = viewportRect.top + options.scrollMargin + options.scrollPreviousItemPeek;
    /** @type {string[]} */ const visible = [];
    /** @type {string | null} */ let current = null;
    for (const item of items()) {
      const id = item.dataset.messageId;
      if (!id) continue;
      if (visibleIds.has(id)) visible.push(id);
      if (isAnchor(item) && item.getBoundingClientRect().top <= line + EPSILON) current = id;
    }
    return { currentAnchorId: current, visibleMessageIds: visible };
  }

  function scheduleVisibilitySync() {
    if (!watchingVisibility || visibilityFrame) return;
    visibilityFrame = requestAnimationFrame(() => {
      visibilityFrame = 0;
      if (!watchingVisibility) return;
      const next = computeVisibility();
      if (next.currentAnchorId === visibility.currentAnchorId && next.visibleMessageIds.join('\n') === visibility.visibleMessageIds.join('\n')) return;
      visibility = next;
      call(dotnet, 'Visibility', next.currentAnchorId, next.visibleMessageIds);
    });
  }

  /** Observes the items present, and forgets the ones that left. */
  function syncObservedItems() {
    if (!visibilityObserver) return;
    const present = new Set(items().filter((item) => item.dataset.messageId));
    for (const element of observed) {
      if (present.has(element)) continue;
      visibilityObserver.unobserve(element);
      observed.delete(element);
      if (element.dataset.messageId && !messageElement(element.dataset.messageId)) visibleIds.delete(element.dataset.messageId);
    }
    for (const element of present) {
      if (observed.has(element)) continue;
      visibilityObserver.observe(element);
      observed.add(element);
    }
  }

  function setAutoScrolling(/** @type {boolean} */ value) {
    clearTimeout(autoscrollingTimer);
    autoscrollingTimer = undefined;
    if (autoscrolling !== value) {
      autoscrolling = value;
      commitScrollState();
    }
    if (value) {
      autoscrollingTimer = setTimeout(() => {
        autoscrollingTimer = undefined;
        autoscrolling = false;
        commitScrollState();
      }, AUTOSCROLLING_CLEAR_DELAY);
    }
  }

  /** Reserves room below the last item so an anchored turn can sit at the top. Hidden at 0. */
  function setTailSpacerHeight(/** @type {number} */ height) {
    if (!spacer) return;
    const next = Math.max(0, Math.ceil(height));
    if (spacerHeight === next) return;
    spacerHeight = next;
    spacer.hidden = next === 0;
    spacer.style.height = `${next}px`;
    spacer.style.marginTop = next > 0 ? `${-spacerGap}px` : '';
  }

  /** @param {number} scrollTop @param {ScrollBehavior} behavior @param {boolean} [auto] */
  function scrollToPosition(scrollTop, behavior, auto = false) {
    if (!viewport) return;
    const next = Math.max(0, scrollTop);
    if (Math.abs(viewport.scrollTop - next) <= EPSILON) {
      viewport.scrollTop = next;
      commitScrollState();
      return;
    }
    if (auto) setAutoScrolling(true);
    viewport.scrollTo({ top: next, behavior });
    scheduleStateCommit();
  }

  function scrollToStart(/** @type {ScrollBehavior} */ behavior = 'auto') {
    if (!viewport) return false;
    setTailSpacerHeight(0);
    streamingTurn = null;
    mode = 'free-scrolling';
    scrollToPosition(0, behavior);
    scheduleVisibilitySync();
    return true;
  }

  function scrollToEnd(/** @type {ScrollBehavior} */ behavior = 'auto') {
    if (!viewport) return false;
    setTailSpacerHeight(0);
    streamingTurn = null;
    mode = options.autoScroll ? 'following-bottom' : 'free-scrolling';
    scrollToPosition(maxScrollTop(viewport), behavior, true);
    scheduleVisibilitySync();
    return true;
  }

  /** @param {HTMLElement} element @param {ScrollOptions} [scrollOptions] @param {boolean} [keepPreviousPeek] */
  function scrollToElement(element, scrollOptions = {}, keepPreviousPeek = false) {
    if (!content || !viewport || !content.contains(element)) return false;
    const margin = scrollOptions.scrollMargin ?? options.scrollMargin;
    const scrollTop = elementScrollTop(element, viewport, spacer, scrollOptions.align ?? 'start',
      keepPreviousPeek ? margin + options.scrollPreviousItemPeek : margin);
    setTailSpacerHeight(scrollTop + viewport.clientHeight - contentBottom(content, spacer, viewport));
    // A prepend that lands before this scroll settles still keeps the jumped-to row in place.
    prependAnchor = { element, viewportTop: viewportTopOf(element, viewport) };
    mode = keepPreviousPeek ? 'anchored-to-message' : 'settling-jump';
    streamingTurn = keepPreviousPeek ? element : null;
    scrollToPosition(scrollTop, scrollOptions.behavior ?? 'auto');
    scheduleVisibilitySync();
    return true;
  }

  /** Holds the anchored turn at the reading line as the content below it resizes. */
  function reanchor() {
    if (!streamingTurn?.isConnected || mode !== 'anchored-to-message') return false;
    return scrollToElement(streamingTurn, { align: 'start' }, true);
  }

  /** @param {string} messageId @param {ScrollOptions} scrollOptions */
  function scrollToMessage(messageId, scrollOptions) {
    const element = messageElement(messageId);
    if (!element) {
      if (itemCount > 0) return false;
      // Not rendered yet (e.g. a transcript still loading): jump when it arrives.
      pendingScrollToMessage = { messageId, options: scrollOptions };
      markDefaultApplied();
      return true;
    }
    markDefaultApplied();
    pendingScrollToMessage = scrollToElement(element, scrollOptions) ? null : { messageId, options: scrollOptions };
    return true;
  }

  function flushPendingScrollToMessage() {
    if (!pendingScrollToMessage) return false;
    const element = messageElement(pendingScrollToMessage.messageId);
    if (!element || !scrollToElement(element, pendingScrollToMessage.options)) return false;
    pendingScrollToMessage = null;
    markDefaultApplied();
    return true;
  }

  function restorePrependedAnchor() {
    if (!prependAnchor || !viewport || !prependAnchor.element.isConnected) return;
    // Viewport-relative: a no-op where native scroll anchoring already kept the row in place.
    const delta = viewportTopOf(prependAnchor.element, viewport) - prependAnchor.viewportTop;
    if (Math.abs(delta) <= EPSILON) return;
    viewport.scrollTop += delta;
    prependAnchor.viewportTop = viewportTopOf(prependAnchor.element, viewport);
    scheduleStateCommit();
    scheduleVisibilitySync();
  }

  function capturePrependAnchor() {
    prependAnchor = null;
    if (!content || !viewport) return;
    const rect = viewport.getBoundingClientRect();
    const anchor = items().find((item) => {
      if (!item.dataset.messageId) return false;
      const itemRect = item.getBoundingClientRect();
      return itemRect.bottom > rect.top && itemRect.top < rect.bottom;
    });
    if (anchor) prependAnchor = { element: anchor, viewportTop: viewportTopOf(anchor, viewport) };
  }

  function applyDefaultScrollPosition() {
    if (defaultApplied || itemCount === 0) return false;
    let handled;
    if (options.defaultScrollPosition === 'last-anchor') {
      const anchor = content && viewport ? items().findLast(isAnchor) : null;
      if (!content || !viewport || !anchor) {
        handled = scrollToEnd();
      } else {
        // A short last turn already fits: opening at the end shows all of it without a blank gap.
        const fits = contentBottom(content, spacer, viewport) - elementTop(anchor, viewport) <= viewport.clientHeight;
        handled = fits ? scrollToEnd() : scrollToElement(anchor, { align: 'start' }, true);
      }
    } else {
      handled = options.defaultScrollPosition === 'end' ? scrollToEnd() : scrollToStart();
    }
    if (handled) markDefaultApplied();
    return handled;
  }

  /** Reconciles the position with the items: first content, prepended, appended, updated, in that order. */
  function reconcileContent(/** @type {HTMLElement[]} */ list, /** @type {number} */ previousCount, /** @type {HTMLElement | null} */ previousFirst) {
    if (flushPendingScrollToMessage()) return;

    if (previousCount === 0) {
      if (applyDefaultScrollPosition()) return;
      if (list.length > 0 && options.autoScroll && scrollToEnd()) return;
      commitScrollState();
      scheduleVisibilitySync();
      return;
    }

    const preserve = viewport?.dataset.blazixPreserveScroll !== 'false';
    if (preserve && previousFirst && list.indexOf(previousFirst) > 0) {
      restorePrependedAnchor();
      return;
    }

    if (list.length > previousCount) {
      const added = list.slice(previousCount).filter(isAnchor);
      if (added.length > 0) {
        // A batch of turns while following the end keeps following, rather than anchoring the first.
        if (options.autoScroll && mode === 'following-bottom' && added.length > 1) {
          scrollToEnd();
          return;
        }
        scrollToElement(added[0], { align: 'start' }, true);
        handledAnchors.add(added[0]);
        return;
      }
    }

    if (list.length === previousCount) {
      const anchor = list.find((item) => isAnchor(item) && !handledAnchors.has(item));
      if (anchor) {
        scrollToElement(anchor, { align: 'start' }, true);
        handledAnchors.add(anchor);
        return;
      }
    }

    if (mode === 'following-bottom' && options.autoScroll) {
      scrollToEnd();
    } else {
      commitScrollState();
      scheduleVisibilitySync();
    }
  }

  function handleContentChange() {
    if (!content) return;
    const list = items();
    const previousCount = itemCount;
    const previousFirst = firstItem;
    itemCount = list.length;
    firstItem = list[0] ?? null;
    syncObservedItems();
    reconcileContent(list, previousCount, previousFirst);
    capturePrependAnchor();
  }

  /**
   * A reader who scrolls away mid-stream strands the tail spacer. Shrink it as the reply grows or as
   * they scroll to the real bottom, and follow the end again once it's gone. True when it handed off.
   */
  function drainReleasedSpacer() {
    if (!options.autoScroll || spacerHeight <= 0 || mode !== 'free-scrolling' || autoscrolling || !viewport || !content) return false;
    const bottom = contentBottom(content, spacer, viewport);
    const viewBottom = viewport.scrollTop + viewport.clientHeight;
    if (viewBottom - bottom <= 0) return false;
    const next = Math.max(0, bottom + spacerHeight - viewBottom);
    if (next === 0) {
      scrollToEnd();
      return true;
    }
    setTailSpacerHeight(next);
    return false;
  }

  function handleResize() {
    resizeFrame = 0;
    if (mode === 'following-bottom' && options.autoScroll) {
      scrollToEnd();
      return;
    }
    const previousSpacer = spacerHeight;
    if (reanchor()) {
      // The reply consumed the spacer and fills the viewport: hand off from the anchor to following.
      if (options.autoScroll && previousSpacer > 0 && spacerHeight === 0) scrollToEnd();
      return;
    }
    if (drainReleasedSpacer()) return;
    scheduleStateCommit();
    scheduleVisibilitySync();
  }

  // Coalesced into a frame: handleResize resizes the spacer inside the observed content.
  const onResize = () => {
    cancelAnimationFrame(resizeFrame);
    resizeFrame = requestAnimationFrame(handleResize);
  };

  function userScrollIntent() {
    if (mode === 'free-scrolling') return;
    streamingTurn = null;
    mode = 'free-scrolling';
  }

  function syncAfterScroll() {
    drainReleasedSpacer();
    commitScrollState();
    scheduleVisibilitySync();
    capturePrependAnchor();
  }

  function disconnect() {
    for (const off of offs) off();
    offs = [];
    cancelAnimationFrame(resizeFrame);
    resizeFrame = 0;
  }

  /** Binds the rendered elements. Content calls it once it's rendered; again with the same elements is a no-op. */
  function connect() {
    const nextContent = find('-content');
    if (!nextContent || nextContent === content) return;
    disconnect();
    root = find('');
    viewport = find('-viewport');
    content = nextContent;
    spacer = /** @type {HTMLElement | null} */ (content.querySelector(':scope > [data-message-scroller-spacer]'));
    const style = spacer?.parentElement ? getComputedStyle(spacer.parentElement) : null;
    spacerGap = style ? px(style.rowGap === 'normal' ? style.gap : style.rowGap) : 0;
    spacerHeight = 0;
    itemCount = 0;
    firstItem = null;

    const sizes = new ResizeObserver(onResize);
    if (viewport) sizes.observe(viewport);
    sizes.observe(content);
    const children = new MutationObserver(handleContentChange);
    children.observe(content, { childList: true });
    offs = [
      () => sizes.disconnect(),
      () => children.disconnect(),
      on(viewport, 'scroll', syncAfterScroll, { passive: true }),
      on(viewport, 'wheel', userScrollIntent, { passive: true }),
      on(viewport, 'touchmove', userScrollIntent, { passive: true }),
      on(viewport, 'keydown', (/** @type {KeyboardEvent} */ event) => {
        if (USER_SCROLL_KEYS.has(event.key)) userScrollIntent();
      }),
    ];
    if (watchingVisibility) {
      stopVisibility();
      observeVisibility();
    }

    handleContentChange();
    if (!applyDefaultScrollPosition() && itemCount === 0) clearPendingDefault();
    if (options.autoScroll && mode === 'following-bottom' && itemCount > 0) scrollToEnd();
    else commitScrollState();
  }

  function observeVisibility() {
    watchingVisibility = true;
    if (!viewport) return;
    visibilityObserver ??= new IntersectionObserver((entries) => {
      for (const entry of entries) {
        const id = /** @type {HTMLElement} */ (entry.target).dataset.messageId;
        if (!id) continue;
        if (entry.isIntersecting) visibleIds.add(id);
        else visibleIds.delete(id);
      }
      scheduleVisibilitySync();
    }, {
      root: viewport,
      // The top edge sits at the reading line, so a previous turn peeking above it isn't visible.
      rootMargin: `${-(options.scrollMargin + options.scrollPreviousItemPeek)}px 0px 0px 0px`,
      threshold: [0, 0.01, 0.5, 1],
    });
    syncObservedItems();
    scheduleVisibilitySync();
  }

  function stopVisibility() {
    cancelAnimationFrame(visibilityFrame);
    visibilityFrame = 0;
    visibilityObserver?.disconnect();
    visibilityObserver = null;
    observed.clear();
    visibleIds.clear();
    visibility = { currentAnchorId: null, visibleMessageIds: [] };
  }

  connect();

  return {
    connect,
    /** @param {Options} next */
    update(next) {
      const previous = options;
      options = next;
      if (next.defaultScrollPosition !== previous.defaultScrollPosition) {
        defaultApplied = false;
        if (!applyDefaultScrollPosition() && itemCount === 0) clearPendingDefault();
      }
      if (watchingVisibility && (next.scrollMargin !== previous.scrollMargin || next.scrollPreviousItemPeek !== previous.scrollPreviousItemPeek)) {
        stopVisibility();
        observeVisibility();
      }
      if (next.autoScroll !== previous.autoScroll) {
        if (next.autoScroll && mode === 'following-bottom' && itemCount > 0) scrollToEnd();
        else commitScrollState();
      }
    },
    scrollToStart: (/** @type {ScrollBehavior} */ behavior) => scrollToStart(behavior),
    scrollToEnd: (/** @type {ScrollBehavior} */ behavior) => scrollToEnd(behavior),
    scrollToMessage: (/** @type {string} */ id, /** @type {ScrollOptions} */ scrollOptions) => scrollToMessage(id, scrollOptions),
    /** The scroll button: blur it (it's about to become inert), then go. */
    press(/** @type {string} */ buttonId, /** @type {'start' | 'end'} */ direction, /** @type {ScrollBehavior} */ behavior) {
      document.getElementById(buttonId)?.blur();
      if (direction === 'start') scrollToStart(behavior);
      else scrollToEnd(behavior);
    },
    observeVisibility,
    unobserveVisibility() {
      watchingVisibility = false;
      stopVisibility();
    },
    dispose() {
      disconnect();
      stopVisibility();
      watchingVisibility = false;
      cancelAnimationFrame(stateFrame);
      clearTimeout(autoscrollingTimer);
    },
  };
}
