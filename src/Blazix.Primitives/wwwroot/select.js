// @ts-check
//
// The browser side of Select, on the shared popup layer. Options take real focus while the list is
// open (like menu items): arrows, Home/End and typeahead move it, Enter and Space choose, and every
// move is reported so C# knows the highlighted option. Closed, arrows on the trigger open the list,
// and typing picks the matching option without opening it, like a native select.
//
// alignItemWithTrigger: the popup is placed so the selected option's text sits over the trigger's
// value text (data-side="none"), falling back to normal placement when there isn't room for that.

import { byId, call, focus, isDisabled, labelOf, lastPointer, on, usingKeyboard } from './core/dom.js';
import { createPopup } from './core/popup.js';
import { createTypeahead } from './core/typeahead.js';

const OPTION = '[role="option"]';
const PADDING = 5;

/** @typedef {{ loopFocus?: boolean, highlightItemOnHover?: boolean, alignItemWithTrigger?: boolean }} SelectOptions */

/**
 * @param {import('./core/dom.js').DotNetRef} dotnet
 * @param {string} triggerId
 * @param {SelectOptions} initial
 */
export function createSelect(dotnet, triggerId, initial) {
  let id = triggerId;
  let options = initial;
  let open = false;
  /** @type {string | null | undefined} */ let reported;
  /** @type {(() => void)[]} */ let openOffs = [];
  /** @type {MutationObserver | null} */ let observer = null;
  const typeahead = createTypeahead();
  const trigger = () => byId(id);
  const popup = createPopup({ trigger, requestClose: (reason) => call(dotnet, 'RequestClose', reason) });
  const popupEl = () => popup.popup;

  const listEl = () => /** @type {HTMLElement | null} */ (popupEl()?.querySelector('[role="listbox"]') ?? (popupEl()?.getAttribute('role') === 'listbox' ? popupEl() : null));
  const options_ = () => /** @type {HTMLElement[]} */ ([...(popupEl()?.querySelectorAll(OPTION) ?? [])]).filter((el) => !isDisabled(el));
  const selected = () => /** @type {HTMLElement | null} */ (popupEl()?.querySelector(`${OPTION}[aria-selected="true"]`) ?? null);

  function syncHighlight() {
    const el = popupEl();
    if (!el) return;
    const active = /** @type {HTMLElement | null} */ (document.activeElement);
    const item = active && el.contains(active) ? /** @type {HTMLElement | null} */ (active.closest(OPTION)) : null;
    for (const h of el.querySelectorAll('[data-highlighted]')) if (h !== item) h.removeAttribute('data-highlighted');
    if (item && !item.hasAttribute('data-highlighted')) item.setAttribute('data-highlighted', '');
    const next = item?.id || null;
    if (next !== reported) {
      reported = next;
      call(dotnet, 'Notify', 'highlight', next);
    }
    updateScrollArrows();
  }

  function updateScrollArrows() {
    const list = listEl() ?? popupEl();
    const el = popupEl();
    if (!list || !el) return;
    const up = /** @type {HTMLElement | null} */ (el.querySelector('[data-blazix-select-scroll="up"]'));
    const down = /** @type {HTMLElement | null} */ (el.querySelector('[data-blazix-select-scroll="down"]'));
    const canUp = list.scrollTop > 1;
    const canDown = list.scrollTop + list.clientHeight < list.scrollHeight - 1;
    if (up) {
      up.hidden = !canUp;
      up.toggleAttribute('data-visible', canUp);
    }
    if (down) {
      down.hidden = !canDown;
      down.toggleAttribute('data-visible', canDown);
    }
  }

  /** Places the popup so the selected option's text sits over the trigger's value. False when it doesn't fit. */
  function alignWithTrigger() {
    const t = trigger();
    const el = popupEl();
    const host = el?.parentElement;
    const item = selected() ?? options_()[0];
    if (!t || !el || !host || !item) return false;
    const list = listEl() ?? el;
    host.style.left = '0px';
    host.style.top = '0px';
    const tr = t.getBoundingClientRect();
    host.style.setProperty('--anchor-width', `${tr.width}px`);
    host.style.setProperty('--anchor-height', `${tr.height}px`);
    const value = /** @type {HTMLElement} */ (t.querySelector('[data-blazix-select-value]') ?? t);
    const text = /** @type {HTMLElement} */ (item.querySelector('[data-blazix-select-item-text]') ?? item);
    const hostRect = host.getBoundingClientRect();
    const textRect = text.getBoundingClientRect();
    const itemRect = item.getBoundingClientRect();
    const valueRect = value.getBoundingClientRect();
    const view = { width: document.documentElement.clientWidth, height: document.documentElement.clientHeight };
    if (hostRect.height > view.height - PADDING * 2) return false;
    let left = valueRect.left - (textRect.left - hostRect.left);
    // Center the option on the trigger. The list scrolls to keep it there when the popup would overflow.
    let top = tr.top + tr.height / 2 - (itemRect.top - hostRect.top + itemRect.height / 2);
    const overflowTop = PADDING - top;
    const overflowBottom = top + hostRect.height - (view.height - PADDING);
    if (overflowTop > 0) {
      const scroll = Math.min(overflowTop, list.scrollHeight - list.clientHeight - list.scrollTop);
      list.scrollTop += scroll;
      top += overflowTop;
    } else if (overflowBottom > 0) {
      const scroll = Math.min(overflowBottom, list.scrollTop);
      list.scrollTop -= scroll;
      top -= overflowBottom;
    }
    // The trigger must stay covered: if shifting moved the popup off it, use normal placement.
    if (top > tr.top || top + hostRect.height < tr.bottom) return false;
    left = Math.min(Math.max(left, PADDING), Math.max(PADDING, view.width - hostRect.width - PADDING));
    host.style.left = `${Math.round(left)}px`;
    host.style.top = `${Math.round(top)}px`;
    host.style.setProperty('--transform-origin', `${Math.round(tr.left + tr.width / 2 - left)}px ${Math.round(tr.top + tr.height / 2 - top)}px`);
    host.setAttribute('data-side', 'none');
    el.setAttribute('data-side', 'none');
    return true;
  }

  /** @param {KeyboardEvent} event */
  function onKeyDown(event) {
    if (event.defaultPrevented) return;
    const list = options_();
    const current = list.indexOf(/** @type {HTMLElement} */ (document.activeElement));
    const last = list.length - 1;
    const loop = options.loopFocus !== false;
    /** @param {number} index */
    const move = (index) => {
      typeahead.reset();
      list[index]?.focus();
      list[index]?.scrollIntoView?.({ block: 'nearest' });
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
          event.preventDefault();
          search(' ', list, current);
          return;
        }
        event.preventDefault();
        if (current >= 0) list[current].click();
        return;
      case 'Enter':
        event.preventDefault();
        if (current >= 0) list[current].click();
        return;
      default:
        if (event.key.length === 1 && !event.ctrlKey && !event.metaKey && !event.altKey) {
          event.preventDefault();
          search(event.key, list, current);
        }
    }
  }

  /** @param {string} key @param {HTMLElement[]} list @param {number} current */
  function search(key, list, current) {
    const index = typeahead(key, list.map(labelOf), current);
    if (index >= 0) {
      list[index].focus();
      list[index].scrollIntoView?.({ block: 'nearest' });
    }
  }

  /** @param {PointerEvent} event */
  function onPointerMove(event) {
    if (event.pointerType === 'touch' || !lastPointer().moved || options.highlightItemOnHover === false) return;
    const item = /** @type {HTMLElement | null} */ (/** @type {Element} */ (event.target).closest(OPTION));
    if (item && !isDisabled(item) && document.activeElement !== item) item.focus({ preventScroll: true });
  }

  const triggerOffs = [
    on(document, 'keydown', (/** @type {KeyboardEvent} */ event) => {
      const t = trigger();
      if (!t || event.target !== t || event.defaultPrevented || isDisabled(t) || open) return;
      if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
        event.preventDefault();
        call(dotnet, 'RequestOpen', 'list-navigation', event.key === 'ArrowUp' ? 'last' : 'selected');
      } else if (event.key.length === 1 && event.key !== ' ' && !event.ctrlKey && !event.metaKey && !event.altKey) {
        // Closed, typing picks the matching option, like a native select.
        event.preventDefault();
        call(dotnet, 'Notify', 'typeahead', event.key);
      }
    }),
  ];

  function detach() {
    for (const off of openOffs) off();
    openOffs = [];
    observer?.disconnect();
    observer = null;
  }

  return {
    /** @param {import('./core/popup.js').ShowOptions & { focus?: string, alignItemWithTrigger?: boolean }} next */
    show(next) {
      const wasOpen = open;
      open = true;
      detach();
      reported = undefined;
      // Touch screens get normal placement.
      const align = next.alignItemWithTrigger && lastPointer().type !== 'touch';
      popup.show({ ...next, initialFocus: 'none' });
      if (!wasOpen && align) {
        popup.stopPositioning();
        if (!alignWithTrigger()) popup.show({ ...next, initialFocus: 'none' });
      }
      const host = byId(next.hosts.at(-1));
      if (host) {
        openOffs.push(on(host, 'keydown', onKeyDown));
        openOffs.push(on(host, 'pointermove', onPointerMove));
        openOffs.push(on(host, 'focusin', syncHighlight));
        openOffs.push(on(host, 'scroll', updateScrollArrows, { capture: true, passive: true }));
        // Hovering a scroll arrow scrolls the list.
        /** @type {ReturnType<typeof setInterval> | undefined} */
        let scrolling;
        openOffs.push(on(host, 'pointerover', (/** @type {PointerEvent} */ event) => {
          const arrow = /** @type {HTMLElement | null} */ (/** @type {Element} */ (event.target).closest('[data-blazix-select-scroll]'));
          clearInterval(scrolling);
          if (!arrow) return;
          const list = listEl() ?? popupEl();
          scrolling = setInterval(() => {
            if (!list) return;
            list.scrollTop += arrow.dataset.blazixSelectScroll === 'up' ? -12 : 12;
            updateScrollArrows();
          }, 16);
        }));
        openOffs.push(() => clearInterval(scrolling));
        observer = new MutationObserver(() => syncHighlight());
        observer.observe(host, { childList: true, subtree: true, attributes: true, attributeFilter: ['aria-disabled', 'aria-selected'] });
      }
      if (!wasOpen) {
        const list = options_();
        const target = next.focus === 'last' ? list.at(-1)
          : next.focus === 'first' ? list[0]
            : selected() ?? (usingKeyboard() ? list[0] : null);
        focus(target ?? popupEl());
        target?.scrollIntoView?.({ block: 'nearest' });
        updateScrollArrows();
      }
    },
    /** @param {boolean} restore */
    hide(restore) {
      open = false;
      detach();
      typeahead.reset();
      return popup.hide(restore, popup.options?.finalFocusId ?? null);
    },
    restoreFocus() {
      popup.restoreFocus();
    },
    /** @param {string} triggerIdNext @param {SelectOptions} next */
    setTrigger(triggerIdNext, next) {
      id = triggerIdNext;
      options = next;
    },
    dispose() {
      detach();
      for (const off of triggerOffs) off();
      popup.dispose();
    },
  };
}
