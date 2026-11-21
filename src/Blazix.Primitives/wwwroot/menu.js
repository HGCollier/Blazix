// @ts-check
//
// The browser side of Menu, Context Menu and Menubar menus, on the shared popup layer (core/popup.js).
//
// Who owns what (and how they stay in sync):
//   Blazor owns all state: open/closed, checked, disabled, ids, ARIA, and which item is highlighted.
//            This never opens or closes a menu itself; it asks .NET (RequestOpen/RequestClose) and
//            reflects whatever Blazor renders.
//   JS owns DOM focus, because it has to move inside the key event (Blazor can't preventDefault
//            conditionally, and a round trip per key press would lag on Blazor Server). Every focus
//            move is reported back (Notify "highlight"), so C# always knows the highlighted item.
//   After every Blazor DOM patch a MutationObserver re-syncs focus and data-highlighted: if Blazor
//            removed the focused item, focus moves to its neighbour instead of dropping to <body>.
//
// Submenus render inside their parent menu's popup (where you wrote them), so key and pointer events
// from a submenu bubble through the parent: every handler first checks that the closest menu is its own.

import { byId, call, focus, isDisabled, isRTL, labelOf, lastPointer, on, usingKeyboard } from './core/dom.js';
import { createPopup } from './core/popup.js';
import { createTypeahead } from './core/typeahead.js';

const ITEM = '[role="menuitem"], [role="menuitemcheckbox"], [role="menuitemradio"]';
const REST_MS = 300;

/**
 * @typedef {{
 *   openOnHover?: boolean, delay?: number, closeDelay?: number, loopFocus?: boolean,
 *   highlightItemOnHover?: boolean, submenu?: boolean, menubar?: boolean, context?: boolean,
 *   orientation?: 'vertical' | 'horizontal', triggerIds?: string[],
 * }} MenuOptions
 */

/** The popup id of the menu an element belongs to. @param {Element} el */
const ownerOf = (el) => el.closest('[data-blazix-menu]')?.getAttribute('data-blazix-menu') ?? null;
/** Text as the filter compares it: no case, accents or punctuation. @param {string} text */
const fold = (text) => text.normalize('NFD').replace(/[\p{Diacritic}\p{P}]/gu, '').replace(/\s+/g, ' ').toLocaleLowerCase().trim();

/**
 * @param {import('./core/dom.js').DotNetRef} dotnet
 * @param {string} triggerId
 * @param {MenuOptions} initial
 */
export function createMenu(dotnet, triggerId, initial) {
  let id = triggerId;
  let options = initial;
  let open = false;
  /** @type {HTMLElement | null} */ let focusedItem = null;
  let focusedIndex = -1;
  /** @type {string | null | undefined} */ let reported;
  /** @type {MutationObserver | null} */ let observer = null;
  /** @type {(() => void)[]} */ let openOffs = [];
  /** @type {(() => void)[]} */ let triggerOffs = [];
  /** @type {ReturnType<typeof setTimeout> | undefined} */ let openTimer;
  /** @type {ReturnType<typeof setTimeout> | undefined} */ let closeTimer;
  /** @type {(() => void) | null} */ let stopBridge = null;
  /** @type {{ x: number, y: number } | null} */ let contextPoint = null;
  const typeahead = createTypeahead();

  const trigger = () => byId(id);
  const triggers = () => /** @type {HTMLElement[]} */ ((options.triggerIds?.length ? options.triggerIds : [id]).map(byId).filter(Boolean));
  const triggerOf = (/** @type {EventTarget | null} */ target) => triggers().find((el) => target instanceof Node && el.contains(target)) ?? null;
  const popup = createPopup({ trigger, requestClose: (reason) => call(dotnet, 'RequestClose', reason) });
  const popupEl = () => popup.popup;
  const popupId = () => popup.options?.popupId ?? null;
  const own = (/** @type {Element | null} */ el) => !!el && ownerOf(el) === popupId();
  /** The filter input of a filterable menu (Menu.FilterProvider + Menu.Input). */
  const filterInput = () => /** @type {HTMLInputElement | null} */ ([...(popupEl()?.querySelectorAll('[data-blazix-menu-input]') ?? [])].find((el) => own(el)) ?? null);
  /** @type {HTMLElement | null} */
  let virtual = null;

  /** This menu's enabled items in DOM order: not a submenu's. */
  function items() {
    const el = popupEl();
    if (!el) return [];
    return /** @type {HTMLElement[]} */ ([...el.querySelectorAll(ITEM)]).filter((item) => own(item) && !isDisabled(item) && !item.hidden);
  }

  /** @param {Event} event */
  const ownEvent = (event) => {
    const el = popupEl();
    const target = /** @type {Element} */ (event.target);
    return !!el && (target === el || own(target));
  };

  /** @param {HTMLElement | null} [item] defaults to the focused item */
  function syncHighlight(item) {
    const el = popupEl();
    if (!el) return;
    if (item === undefined) {
      const active = /** @type {HTMLElement | null} */ (document.activeElement);
      item = active && own(active) ? /** @type {HTMLElement | null} */ (active.closest(ITEM)) : null;
      if (item && !own(item)) item = null;
    }
    for (const highlighted of el.querySelectorAll('[data-highlighted]')) {
      if (highlighted !== item && own(highlighted)) highlighted.removeAttribute('data-highlighted');
    }
    if (item && !item.hasAttribute('data-highlighted')) item.setAttribute('data-highlighted', '');
    const next = item?.id || null;
    if (next !== reported) {
      reported = next;
      call(dotnet, 'Notify', 'highlight', next);
    }
  }

  /** Moves the virtual highlight (filterable menus): data-highlighted, and aria-activedescendant on the input. */
  function highlightVirtual(/** @type {HTMLElement | null} */ item) {
    const input = filterInput();
    if (virtual && virtual !== item) virtual.removeAttribute('data-highlighted');
    virtual = item;
    if (item) {
      item.setAttribute('data-highlighted', '');
      item.scrollIntoView?.({ block: 'nearest' });
      input?.setAttribute('aria-activedescendant', item.id);
    } else {
      input?.removeAttribute('aria-activedescendant');
    }
    const next = item?.id || null;
    if (next !== reported) {
      reported = next;
      call(dotnet, 'Notify', 'highlight', next);
    }
  }

  /**
   * Hides the items whose text doesn't contain the query, and the groups left empty.
   * @param {boolean} changed whether the query just changed (auto highlight moves to the first match)
   */
  function applyFilter(changed) {
    const input = filterInput();
    const el = popupEl();
    if (!input || !el) return;
    const query = fold(input.value);
    if (!input.hasAttribute('data-manual-filtering')) {
      const all = /** @type {HTMLElement[]} */ ([...el.querySelectorAll(ITEM)]).filter((item) => own(item));
      for (const item of all) item.hidden = query.length > 0 && !fold(labelOf(item)).includes(query);
      for (const group of el.querySelectorAll('[role="group"]')) {
        if (!own(group)) continue;
        const members = /** @type {HTMLElement[]} */ ([...group.querySelectorAll(ITEM)]).filter((item) => own(item));
        /** @type {HTMLElement} */ (group).hidden = members.length > 0 && members.every((item) => item.hidden);
      }
      for (const separator of el.querySelectorAll('[role="separator"]')) {
        if (own(separator)) /** @type {HTMLElement} */ (separator).hidden = query.length > 0;
      }
    }
    const visible = items();
    for (const empty of el.querySelectorAll('[data-blazix-menu-empty]')) {
      if (own(empty)) /** @type {HTMLElement} */ (empty).hidden = visible.length > 0;
    }
    const mode = input.dataset.autoHighlight;
    const auto = mode === 'always' || (mode === 'query' && query.length > 0);
    if (changed && mode) highlightVirtual(auto ? visible[0] ?? null : null);
    else if (virtual && !visible.includes(virtual)) highlightVirtual(auto ? visible[0] ?? null : null);
    else if (!virtual && auto) highlightVirtual(visible[0] ?? null);
  }

  /** @param {KeyboardEvent} event */
  function onInputKeyDown(event) {
    const input = filterInput();
    const list = items();
    const current = virtual ? list.indexOf(virtual) : -1;
    const last = list.length - 1;
    // Without auto highlight, moving past either end goes back to the input; with it, the keys wrap.
    const wrap = !!input?.dataset.autoHighlight;
    switch (event.key) {
      case 'ArrowDown':
        event.preventDefault();
        if (current < 0) highlightVirtual(list[0] ?? null);
        else if (current < last) highlightVirtual(list[current + 1]);
        else highlightVirtual(wrap ? list[0] ?? null : null);
        return;
      case 'ArrowUp':
        event.preventDefault();
        if (current < 0) highlightVirtual(list[last] ?? null);
        else if (current > 0) highlightVirtual(list[current - 1]);
        else highlightVirtual(wrap ? list[last] ?? null : null);
        return;
      case 'Enter':
        if (virtual) {
          event.preventDefault();
          virtual.click();
        }
        return;
      default:
    }
    // The forward arrow at the end of the text opens a highlighted submenu trigger.
    const forward = isRTL(popupEl() ?? document.body) ? 'ArrowLeft' : 'ArrowRight';
    const atEnd = !input || input.selectionStart === input.value.length;
    if (event.key === forward && atEnd && virtual?.getAttribute('aria-haspopup') === 'menu') {
      event.preventDefault();
      virtual.click();
    }
  }

  /** @param {'first' | 'last' | 'popup' | 'none'} target */
  function focusItem(target) {
    const el = popupEl();
    if (!el || target === 'none') return;
    const list = items();
    const item = target === 'first' ? list[0] : target === 'last' ? list.at(-1) : null;
    const input = filterInput();
    if (input) {
      highlightVirtual(item ?? null);
      focus(input);
      return;
    }
    focus(item ?? el);
  }

  // ---- Hover: opening from the trigger (submenus, menubar, openOnHover) -----------------------

  function clearHover() {
    clearTimeout(openTimer);
    clearTimeout(closeTimer);
    stopBridge?.();
    stopBridge = null;
  }

  function scheduleClose() {
    clearTimeout(closeTimer);
    stopBridge?.();
    stopBridge = null;
    const delay = options.closeDelay ?? 0;
    const close = () => call(dotnet, 'RequestClose', 'trigger-hover');
    if (delay <= 0) close();
    else closeTimer = setTimeout(close, delay);
  }

  /**
   * The pointer left the trigger toward the open popup. While it moves inside the triangle from
   * where it left to the popup's near edge (or rests there briefly), other items it crosses in the
   * parent menu don't take the highlight, and the popup stays open.
   * @param {PointerEvent} event
   */
  function bridgeToPopup(event) {
    const el = trigger();
    const pop = popupEl();
    if (!el || !pop) return scheduleClose();
    const t = el.getBoundingClientRect();
    const p = pop.getBoundingClientRect();
    const apex = { x: Math.min(Math.max(event.clientX, t.left), t.right), y: Math.min(Math.max(event.clientY, t.top), t.bottom) };
    const beside = p.left >= t.right - 1 || p.right <= t.left + 1;
    const near = beside ? (p.left >= apex.x ? p.left : p.right) : (p.top >= apex.y ? p.top : p.bottom);
    /** @param {number} x @param {number} y */
    const inside = (x, y) => {
      if (x >= p.left && x <= p.right && y >= p.top && y <= p.bottom) return true;
      const b = beside ? { x: near, y: p.top - 8 } : { x: p.left - 8, y: near };
      const c = beside ? { x: near, y: p.bottom + 8 } : { x: p.right + 8, y: near };
      const sign = (/** @type {{x:number,y:number}} */ p1, /** @type {{x:number,y:number}} */ p2, /** @type {{x:number,y:number}} */ p3) => (p1.x - p3.x) * (p2.y - p3.y) - (p2.x - p3.x) * (p1.y - p3.y);
      const pt = { x, y };
      const d1 = sign(pt, apex, b);
      const d2 = sign(pt, b, c);
      const d3 = sign(pt, c, apex);
      return !((d1 < 0 || d2 < 0 || d3 < 0) && (d1 > 0 || d2 > 0 || d3 > 0));
    };
    if (!inside(event.clientX, event.clientY)) return scheduleClose();
    stopBridge?.();
    /** @type {ReturnType<typeof setTimeout> | undefined} */
    let rest;
    const off = on(document, 'pointermove', (/** @type {PointerEvent} */ move) => {
      clearTimeout(rest);
      if (!inside(move.clientX, move.clientY)) {
        scheduleClose();
        return;
      }
      // Keep the parent menu from moving its highlight while the pointer crosses its items.
      if (!pop.contains(/** @type {Node} */ (move.target))) move.stopPropagation();
      // Resting on the way gives the highlight back to whatever the pointer is over.
      rest = setTimeout(() => {
        stopBridge?.();
        stopBridge = null;
      }, REST_MS);
    }, { capture: true });
    stopBridge = () => {
      clearTimeout(rest);
      off();
    };
  }

  // ---- Trigger listeners (delegated, so Render can swap the trigger element) ------------------

  function bindTrigger() {
    for (const off of triggerOffs) off();
    triggerOffs = [];

    triggerOffs.push(on(document, 'keydown', (/** @type {KeyboardEvent} */ event) => {
      const el = triggerOf(event.target);
      if (!el || event.defaultPrevented || isDisabled(el)) return;
      const rtl = isRTL(el);
      if (options.submenu) {
        // A submenu trigger is an item of its parent: the "forward" arrow opens it and focuses its first item.
        if (event.key === (rtl ? 'ArrowLeft' : 'ArrowRight')) {
          event.preventDefault();
          if (open) focusItem('first');
          else call(dotnet, 'RequestOpen', 'list-navigation', 'first');
        }
        return;
      }
      if (options.context) return;
      if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
        event.preventDefault();
        const target = event.key === 'ArrowUp' ? 'last' : 'first';
        if (open && el.id === id) focusItem(target);
        else call(dotnet, 'RequestOpenFrom', 'list-navigation', target, el.id);
      }
    }));

    triggerOffs.push(on(document, 'pointerover', (/** @type {PointerEvent} */ event) => {
      const el = triggerOf(event.target);
      if (!el || el.contains(/** @type {Node | null} */ (event.relatedTarget))) return;
      if (!options.openOnHover || event.pointerType === 'touch' || isDisabled(el)) return;
      clearHover();
      if (open && el.id === id) return;
      const delay = open ? 0 : options.delay ?? 0;
      const request = () => call(dotnet, 'RequestOpenFrom', 'trigger-hover', options.submenu ? 'none' : 'popup', el.id);
      if (delay <= 0) request();
      else openTimer = setTimeout(request, delay);
    }));

    triggerOffs.push(on(document, 'pointerout', (/** @type {PointerEvent} */ event) => {
      const el = trigger();
      const related = /** @type {Node | null} */ (event.relatedTarget);
      if (!el || !options.openOnHover || event.pointerType === 'touch') return;
      if (!el.contains(/** @type {Node} */ (event.target)) || el.contains(related)) return;
      clearTimeout(openTimer);
      const pop = popupEl();
      if (!open || pop?.contains(related)) return;
      // Menubar menus stay open when the pointer leaves: another trigger, or a press, switches them.
      if (options.menubar) return;
      bridgeToPopup(event);
    }));

    if (options.context) {
      // Right click (and the context menu key) opens at the pointer; a long press does on touch.
      triggerOffs.push(on(document, 'contextmenu', (/** @type {MouseEvent} */ event) => {
        const el = trigger();
        if (!el || !el.contains(/** @type {Node} */ (event.target)) || isDisabled(el)) return;
        event.preventDefault();
        contextPoint = { x: event.clientX, y: event.clientY };
        call(dotnet, 'RequestOpen', 'trigger-press', `${event.clientX},${event.clientY}`);
      }));
      /** @type {ReturnType<typeof setTimeout> | undefined} */
      let longPress;
      /** @type {{ x: number, y: number } | null} */
      let start = null;
      triggerOffs.push(on(document, 'pointerdown', (/** @type {PointerEvent} */ event) => {
        const el = trigger();
        if (event.pointerType !== 'touch' || !el || !el.contains(/** @type {Node} */ (event.target))) return;
        start = { x: event.clientX, y: event.clientY };
        clearTimeout(longPress);
        longPress = setTimeout(() => {
          if (!start) return;
          contextPoint = start;
          call(dotnet, 'RequestOpen', 'trigger-press', `${start.x},${start.y}`);
        }, 500);
      }));
      triggerOffs.push(on(document, 'pointermove', (/** @type {PointerEvent} */ event) => {
        if (start && Math.hypot(event.clientX - start.x, event.clientY - start.y) > 10) {
          clearTimeout(longPress);
          start = null;
        }
      }, { passive: true }));
      for (const type of ['pointerup', 'pointercancel']) {
        triggerOffs.push(on(document, type, () => {
          clearTimeout(longPress);
          start = null;
        }));
      }
    }
  }
  bindTrigger();

  // ---- Popup listeners ------------------------------------------------------------------------

  /** @param {KeyboardEvent} event */
  function onKeyDown(event) {
    if (!ownEvent(event) || event.defaultPrevented) return;
    if (event.target === filterInput()) {
      onInputKeyDown(event);
      return;
    }
    const list = items();
    const active = /** @type {HTMLElement} */ (document.activeElement);
    const current = list.indexOf(active);
    const loop = options.loopFocus !== false;
    const last = list.length - 1;
    const rtl = isRTL(popupEl() ?? document.body);
    const back = rtl ? 'ArrowRight' : 'ArrowLeft';
    const forward = rtl ? 'ArrowLeft' : 'ArrowRight';
    /** @param {number} index */
    const move = (index) => {
      typeahead.reset();
      list[index]?.focus();
    };

    switch (event.key) {
      case 'ArrowDown':
        event.preventDefault();
        move(current < 0 ? 0 : current < last ? current + 1 : loop ? 0 : last);
        return;
      case 'ArrowUp':
        event.preventDefault();
        move(current < 0 ? last : current > 0 ? current - 1 : loop ? last : 0);
        return;
      case 'Home':
      case 'PageUp':
        event.preventDefault();
        move(0);
        return;
      case 'End':
      case 'PageDown':
        event.preventDefault();
        move(last);
        return;
      case ' ':
        if (typeahead.active()) {
          // A space in the middle of typing is part of the search ("log out").
          event.preventDefault();
          search(' ', list, current);
          return;
        }
        event.preventDefault();
        if (current >= 0) list[current].click();
        return;
      case 'Enter':
        event.preventDefault();
        // Fires Blazor's onclick, so pointer and keyboard share one activation path.
        if (current >= 0) list[current].click();
        return;
      default:
        break;
    }

    if (event.key === back && options.submenu) {
      event.preventDefault();
      focus(trigger());
      call(dotnet, 'RequestClose', 'list-navigation');
      return;
    }
    // A submenu trigger opens its submenu with the forward arrow (its own listener does that).
    const opensSubmenu = event.key === forward && current >= 0 && list[current].getAttribute('aria-haspopup') === 'menu';
    if (options.menubar && !options.submenu && !opensSubmenu && (event.key === forward || event.key === back)) {
      event.preventDefault();
      call(dotnet, 'Notify', 'menubar', event.key === forward ? 'next' : 'previous');
      return;
    }

    if (event.key.length === 1 && !event.ctrlKey && !event.metaKey && !event.altKey) {
      event.preventDefault();
      search(event.key, list, current);
    }
  }

  /** @param {string} key @param {HTMLElement[]} list @param {number} current */
  function search(key, list, current) {
    const index = typeahead(key, list.map(labelOf), current);
    if (index >= 0) list[index].focus();
  }

  /** @param {PointerEvent} event */
  function onPointerMove(event) {
    const el = popupEl();
    if (!el || event.pointerType === 'touch' || !lastPointer().moved || options.highlightItemOnHover === false) return;
    const target = /** @type {Element} */ (event.target);
    // Moves over a submenu (rendered inside this popup) belong to the submenu.
    if (!own(target) && target !== el) return;
    const item = /** @type {HTMLElement | null} */ (target.closest(ITEM));
    if (filterInput()) {
      // A filterable menu keeps focus in its input: hover moves the virtual highlight.
      if (item && own(item) && !isDisabled(item)) highlightVirtual(item);
      return;
    }
    if (item && own(item) && !isDisabled(item)) {
      if (document.activeElement !== item) item.focus({ preventScroll: true });
    } else if (document.activeElement !== el && own(/** @type {Element} */ (document.activeElement ?? el))) {
      el.focus({ preventScroll: true });
    }
  }

  /** @param {PointerEvent} event */
  function onPointerLeave(event) {
    const el = popupEl();
    const pointer = lastPointer();
    // A real move dispatches pointerleave before its pointermove, so the coordinates differ from the
    // last known position. Same coordinates means the menu moved, not the pointer: ignore it.
    const resting = event.clientX === pointer.x && event.clientY === pointer.y;
    if (!el || event.pointerType === 'touch' || resting || options.highlightItemOnHover === false) return;
    // Only when focus is on this menu's own items (not in an open submenu).
    const active = document.activeElement;
    if (filterInput()) return;
    if (active && own(active) && active !== el) el.focus({ preventScroll: true });
  }

  /** @param {FocusEvent} event */
  function onFocusIn(event) {
    if (!ownEvent(event)) return;
    const item = /** @type {HTMLElement | null} */ (/** @type {Element} */ (event.target).closest(ITEM));
    focusedItem = item && own(item) ? item : null;
    focusedIndex = focusedItem ? items().indexOf(focusedItem) : -1;
    // A filterable menu's highlight is virtual: focus in its input doesn't move it.
    if (!filterInput()) syncHighlight();
  }

  /** @param {FocusEvent} event */
  function onFocusOut(event) {
    const next = /** @type {Node | null} */ (event.relatedTarget);
    if (!next || popup.contains(next)) return;
    focusedItem = null;
    focusedIndex = -1;
    syncHighlight(null);
  }

  /** Runs after each Blazor DOM patch inside the menu. */
  function reconcile() {
    const el = popupEl();
    if (!el) return;
    if (filterInput()) {
      // Items Blazor added or replaced: filter them too.
      applyFilter(false);
      return;
    }
    const active = document.activeElement;
    if (focusedItem && !focusedItem.isConnected && (!active || active === document.body)) {
      // Blazor removed the focused item: move to the item now in its place (or the new last one).
      const list = items();
      const next = list[Math.min(focusedIndex, list.length - 1)];
      (next ?? el).focus({ preventScroll: true });
      if (!next) {
        focusedItem = null;
        syncHighlight(null);
      }
      return;
    }
    syncHighlight();
  }

  // A submenu closes when the parent's highlight moves to another of its items.
  /** @param {FocusEvent} event */
  function onParentFocusIn(event) {
    const el = trigger();
    const parent = el ? ownerOf(el) : null;
    const target = /** @type {Element} */ (event.target);
    if (!el || !parent || target === el || popup.contains(target) || ownerOf(target) !== parent) return;
    if (!target.closest(ITEM) && target.id !== parent) return;
    call(dotnet, 'RequestClose', 'sibling-open');
  }

  function detachPopup() {
    for (const off of openOffs) off();
    openOffs = [];
    observer?.disconnect();
    observer = null;
  }

  return {
    /**
     * @param {import('./core/popup.js').ShowOptions & { focus?: 'first' | 'last' | 'popup' | 'none', anchorPoint?: string | null }} next
     */
    show(next) {
      const wasOpen = open;
      open = true;
      clearHover();
      detachPopup();
      reported = undefined;
      const point = next.anchorPoint?.split(',').map(Number);
      const anchorPoint = point && point.length === 2 ? { x: point[0], y: point[1] } : contextPoint;
      popup.show({ ...next, anchorPoint: options.context ? anchorPoint : null, initialFocus: 'none' });
      const el = popupEl();
      if (!el) return;
      if (el.childElementCount > 0 && !el.querySelector(ITEM)) {
        console.warn('Blazix.Primitives: this menu has no element with a menuitem role. A MenuItem Render fragment must spread props.Attributes onto its element.');
      }
      // Listeners go on the host (ours, stable while open) rather than the popup, which a Render
      // fragment may swap for another element between renders. These events all bubble.
      const host = byId(next.hosts.at(-1));
      if (host) {
        openOffs.push(on(host, 'keydown', onKeyDown));
        openOffs.push(on(host, 'pointermove', onPointerMove));
        openOffs.push(on(host, 'pointerleave', onPointerLeave));
        openOffs.push(on(host, 'focusin', onFocusIn));
        openOffs.push(on(host, 'focusout', onFocusOut));
        openOffs.push(on(host, 'pointerover', () => clearHover()));
        openOffs.push(on(host, 'input', (/** @type {Event} */ event) => {
          if (event.target === filterInput()) applyFilter(true);
        }));
        // Pressing Clear keeps focus in the input.
        openOffs.push(on(host, 'mousedown', (/** @type {MouseEvent} */ event) => {
          if (/** @type {Element} */ (event.target).closest('[data-blazix-menu-clear]')) event.preventDefault();
        }));
        observer = new MutationObserver(reconcile);
        observer.observe(host, { childList: true, subtree: true, attributes: true, attributeFilter: ['aria-disabled', 'role', 'id'] });
      }
      if (options.submenu) openOffs.push(on(document, 'focusin', onParentFocusIn));
      const input = filterInput();
      if (!wasOpen && input) {
        // A filterable menu opens with focus in its input.
        virtual = null;
        applyFilter(false);
        // Opening from the keyboard highlights the first (or last) item either way.
        const list = items();
        if (next.focus === 'first' && !virtual) highlightVirtual(list[0] ?? null);
        else if (next.focus === 'last') highlightVirtual(list.at(-1) ?? null);
        requestAnimationFrame(() => open && input.focus({ preventScroll: true }));
      } else if (!wasOpen) {
        // Keyboard opens focus the first or last item; pointer opens focus the menu itself.
        const target = next.focus ?? (usingKeyboard() ? 'first' : 'popup');
        // After a pointer open, wait a frame so the browser's own mousedown focus doesn't undo it.
        if (target === 'popup' && !usingKeyboard()) requestAnimationFrame(() => open && focusItem('popup'));
        else focusItem(target);
      }
    },

    /** @param {boolean} restore */
    hide(restore) {
      open = false;
      clearHover();
      detachPopup();
      typeahead.reset();
      focusedItem = null;
      focusedIndex = -1;
      virtual = null;
      return popup.hide(restore, popup.options?.finalFocusId ?? null);
    },

    restoreFocus() {
      popup.restoreFocus();
    },

    /** The filter query was set from .NET (Clear, a controlled value). @param {string} value */
    refilter(value) {
      const input = filterInput();
      if (input && input.value !== value) input.value = value;
      applyFilter(true);
    },

    /** @param {'first' | 'last' | 'popup' | 'none'} target */
    focusItem(target) {
      focusItem(target);
    },

    /** @param {string} triggerIdNext @param {MenuOptions} next */
    setTrigger(triggerIdNext, next) {
      const moved = id !== triggerIdNext;
      id = triggerIdNext;
      options = next;
      bindTrigger();
      if (moved && open) popup.reposition();
    },

    dispose() {
      clearHover();
      detachPopup();
      for (const off of triggerOffs) off();
      triggerOffs = [];
      popup.dispose();
    },
  };
}
