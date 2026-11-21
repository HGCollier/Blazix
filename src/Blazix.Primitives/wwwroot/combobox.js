// @ts-check
//
// The browser side of Combobox and Autocomplete, on the shared popup layer. Focus stays in the input
// (the WAI-ARIA combobox pattern): the highlighted option is virtual, shown with
// aria-activedescendant on the input and data-highlighted on the option, and moved by the arrow
// keys and by hovering. Enter chooses it. Every move is reported, so C# knows the highlight.
// Pressing an option doesn't take focus from the input.
//
// Inline autocomplete (Autocomplete's "both" and "inline" modes): while the keyboard moves the
// highlight, the input previews that option's text; what was typed comes back when it moves off.

import { byId, call, isDisabled, labelOf, lastPointer, on } from './core/dom.js';
import { createPopup } from './core/popup.js';

// Options, or cells of a grid list (Combobox.Row, with Grid on the root).
const OPTION = '[role="option"],[role="gridcell"]';

/**
 * @typedef {{
 *   loopFocus?: boolean, autoHighlight?: boolean, highlightItemOnHover?: boolean,
 *   openOnInputClick?: boolean, inlinePreview?: boolean, triggerId?: string | null,
 *   readOnly?: boolean, disabled?: boolean,
 *   grid?: boolean,
 * }} ComboboxOptions
 */

/**
 * @param {import('./core/dom.js').DotNetRef} dotnet
 * @param {string} inputId
 * @param {ComboboxOptions} initial
 */
export function createCombobox(dotnet, inputId, initial) {
  let id = inputId;
  let options = initial;
  let open = false;
  /** @type {HTMLElement | null} */ let highlighted = null;
  /** @type {string | null | undefined} */ let reported;
  /** @type {string | null} */ let typed = null;
  /** @type {(() => void)[]} */ let openOffs = [];
  /** @type {MutationObserver | null} */ let observer = null;

  const input = () => /** @type {HTMLInputElement | null} */ (byId(id));
  const popup = createPopup({ trigger: input, requestClose: (reason) => call(dotnet, 'RequestClose', reason) });
  const popupEl = () => popup.popup;
  const items = () => /** @type {HTMLElement[]} */ ([...(popupEl()?.querySelectorAll(OPTION) ?? [])]).filter((el) => !isDisabled(el));

  /** @param {HTMLElement | null} item @param {boolean} [fromKeyboard] */
  function highlight(item, fromKeyboard = false) {
    const el = input();
    if (highlighted && highlighted !== item) highlighted.removeAttribute('data-highlighted');
    highlighted = item;
    if (item) {
      item.setAttribute('data-highlighted', '');
      item.scrollIntoView?.({ block: 'nearest' });
      // NVDA stops echoing typed characters once aria-activedescendant is set, so only set it while highlighting.
      el?.setAttribute('aria-activedescendant', item.id);
    } else {
      el?.removeAttribute('aria-activedescendant');
    }
    if (options.inlinePreview && el && fromKeyboard) {
      if (item) {
        typed ??= el.value;
        el.value = labelOf(item);
      } else if (typed !== null) {
        el.value = typed;
        typed = null;
      }
    }
    const next = item?.id || null;
    if (next !== reported) {
      reported = next;
      call(dotnet, 'Notify', 'highlight', next);
    }
  }

  /** @param {1 | -1 | 'first' | 'last'} step */
  function move(step) {
    const list = items();
    if (!list.length) return highlight(null, true);
    const last = list.length - 1;
    const current = highlighted ? list.indexOf(highlighted) : -1;
    let index;
    if (step === 'first') index = 0;
    else if (step === 'last') index = last;
    else if (current < 0) index = step === 1 ? 0 : last;
    else {
      index = current + step;
      if (index < 0 || index > last) {
        // Past either end without looping, the highlight leaves the list and the typed text comes back.
        if (options.loopFocus === false) return highlight(null, true);
        index = (index + list.length) % list.length;
      }
    }
    highlight(list[index], true);
  }

  /**
   * Grid lists: the arrow keys move between rows (to the same column, or the row's last cell) and,
   * once a cell is highlighted, along the row.
   * @param {'up' | 'down' | 'left' | 'right'} direction
   * @returns {boolean} whether it moved (false: not a grid, or nothing highlighted for left/right)
   */
  function moveInGrid(direction) {
    if (!options.grid) return false;
    const list = items();
    const rows = /** @type {HTMLElement[]} */ ([...(popupEl()?.querySelectorAll('[role="row"]') ?? [])])
      .map((row) => list.filter((cell) => cell.closest('[role="row"]') === row))
      .filter((cells) => cells.length > 0);
    if (!rows.length) return false;
    if (direction === 'left' || direction === 'right') {
      if (!highlighted) return false;
      // Along the row; past its end the highlight wraps to the row's other end (or stays, without looping).
      const row = rows.find((cells) => highlighted && cells.includes(highlighted)) ?? [];
      const rtl = getComputedStyle(popupEl() ?? document.body).direction === 'rtl';
      const forward = (direction === 'right') !== rtl;
      const index = row.indexOf(highlighted) + (forward ? 1 : -1);
      const loop = options.loopFocus !== false;
      const next = row[index] ?? (loop ? row[(index + row.length) % row.length] : null);
      if (next) highlight(next, true);
      return true;
    }
    const rowIndex = highlighted ? rows.findIndex((cells) => cells.includes(highlighted)) : -1;
    const column = rowIndex >= 0 && highlighted ? rows[rowIndex].indexOf(highlighted) : 0;
    let target = rowIndex < 0 ? (direction === 'down' ? 0 : rows.length - 1) : rowIndex + (direction === 'down' ? 1 : -1);
    if (target < 0 || target >= rows.length) {
      if (options.loopFocus === false) {
        highlight(null, true);
        return true;
      }
      target = (target + rows.length) % rows.length;
    }
    const cells = rows[target];
    highlight(cells[Math.min(column, cells.length - 1)], true);
    return true;
  }

  // Typing goes to .NET, which filters and patches the list. Keys pressed before that patch lands
  // (ArrowDown, Enter right after typing) wait for it, so they act on the filtered list.
  /** @type {(() => void)[]} */
  let queued = [];
  /** @type {ReturnType<typeof setTimeout> | undefined} */
  let filterWait;
  /** @type {string | null} what was typed last, until the list is filtered by it */
  let expected = null;
  const filtering = () => expected !== null;
  function filtered() {
    clearTimeout(filterWait);
    filterWait = undefined;
    expected = null;
    const run = queued;
    queued = [];
    for (const action of run) action();
  }

  // After every Blazor patch (filtering): keep the highlighted option if it's still listed.
  function reconcile() {
    if (filtering() && popupEl()?.getAttribute('data-blazix-query') === expected) {
      // The patch for what was typed: run the keys that waited for it once it's reconciled.
      queueMicrotask(filtered);
    }
    const list = items();
    if (highlighted && !list.includes(highlighted)) {
      const same = highlighted.id ? list.find((item) => item.id === highlighted?.id) : undefined;
      highlight(same ?? (options.autoHighlight ? list[0] ?? null : null));
    } else if (!highlighted && options.autoHighlight && list.length) {
      highlight(list[0]);
    }
  }

  const offs = [
    on(document, 'input', (/** @type {Event} */ event) => {
      const el = input();
      if (!open || !el || event.target !== el || popupEl()?.getAttribute('data-blazix-query') == null) return;
      expected = el.value;
      clearTimeout(filterWait);
      // In case the patch never says so (a filter of your own that ignores the text): stop waiting.
      filterWait = setTimeout(filtered, 600);
    }),
    on(document, 'keydown', (/** @type {KeyboardEvent} */ event) => {
      const el = input();
      if (!el || event.target !== el || event.defaultPrevented || options.disabled) return;
      if (filtering() && open && ['ArrowDown', 'ArrowUp', 'Enter', 'Home', 'End'].includes(event.key)) {
        // Replay the key once the filtered list is in.
        event.preventDefault();
        const replay = new KeyboardEvent('keydown', { key: event.key, ctrlKey: event.ctrlKey, altKey: event.altKey, shiftKey: event.shiftKey, metaKey: event.metaKey, bubbles: true, cancelable: true });
        queued.push(() => el.dispatchEvent(replay));
        return;
      }
      switch (event.key) {
        case 'ArrowDown':
          event.preventDefault();
          if (!open) call(dotnet, 'RequestOpen', 'list-navigation', event.altKey ? 'none' : 'first');
          else if (!moveInGrid('down')) move(1);
          return;
        case 'ArrowUp':
          event.preventDefault();
          if (!open) call(dotnet, 'RequestOpen', 'list-navigation', 'last');
          else if (!moveInGrid('up')) move(-1);
          return;
        case 'ArrowLeft':
        case 'ArrowRight':
          // In a grid, once a cell is highlighted these move along the row instead of the caret.
          if (open && moveInGrid(event.key === 'ArrowLeft' ? 'left' : 'right')) event.preventDefault();
          return;
        case 'Enter':
          if (open && highlighted) {
            event.preventDefault();
            typed = null;
            highlighted.click();
          }
          return;
        case 'Home':
        case 'End':
          if (open && highlighted && event.ctrlKey) {
            event.preventDefault();
            move(event.key === 'Home' ? 'first' : 'last');
          }
          return;
        default:
          // Typing a character replaces the inline preview with what's typed.
          if (typed !== null && event.key.length === 1) typed = null;
      }
    }),
    on(document, 'click', (/** @type {MouseEvent} */ event) => {
      const el = input();
      if (!el || event.target !== el || open || !options.openOnInputClick || options.disabled || options.readOnly) return;
      call(dotnet, 'RequestOpen', 'trigger-press', 'none');
    }),
    on(document, 'focusout', (/** @type {FocusEvent} */ event) => {
      const el = input();
      if (!el || event.target !== el || !open) return;
      const to = /** @type {Node | null} */ (event.relatedTarget);
      if (to && (popup.contains(to) || byId(options.triggerId)?.contains(to))) return;
      call(dotnet, 'RequestClose', 'focus-out');
    }),
    // A press on an option, the popup or the trigger keeps focus in the input.
    on(document, 'pointerdown', (/** @type {PointerEvent} */ event) => {
      const target = /** @type {Node} */ (event.target);
      const pop = popupEl();
      const trigger = byId(options.triggerId);
      if ((pop?.contains(target) && !(/** @type {Element} */ (target)).closest('input,textarea,select')) || trigger?.contains(target)) {
        if (document.activeElement === input()) event.preventDefault();
      }
    }, { capture: true }),
  ];

  function detach() {
    for (const off of openOffs) off();
    openOffs = [];
    observer?.disconnect();
    observer = null;
  }

  return {
    /** @param {import('./core/popup.js').ShowOptions & { focus?: string }} next */
    show(next) {
      const wasOpen = open;
      open = true;
      detach();
      popup.show({ ...next, initialFocus: 'none', tab: 'native', insideIds: [id, ...(options.triggerId ? [options.triggerId] : [])] });
      const host = byId(next.hosts.at(-1));
      if (host) {
        openOffs.push(on(host, 'pointermove', (/** @type {PointerEvent} */ event) => {
          if (event.pointerType === 'touch' || !lastPointer().moved || options.highlightItemOnHover === false) return;
          const item = /** @type {HTMLElement | null} */ (/** @type {Element} */ (event.target).closest(OPTION));
          if (item && !isDisabled(item) && item !== highlighted) highlight(item);
        }));
        openOffs.push(on(host, 'pointerleave', () => {
          if (options.highlightItemOnHover !== false && highlighted && lastPointer().type !== 'touch') highlight(null);
        }));
        observer = new MutationObserver(reconcile);
        observer.observe(host, { childList: true, subtree: true, attributes: true, attributeFilter: ['data-blazix-query'] });
      }
      if (!wasOpen) {
        const list = items();
        const selected = list.find((item) => item.getAttribute('aria-selected') === 'true');
        if (next.focus === 'first') highlight(list[0] ?? null);
        else if (next.focus === 'last') highlight(list.at(-1) ?? null);
        else if (options.autoHighlight) highlight(list[0] ?? null);
        else if (selected && next.focus !== 'none') highlight(selected);
      }
    },
    /** @param {boolean} restore */
    hide(restore) {
      open = false;
      detach();
      highlight(null);
      typed = null;
      // Focus is in the input already; there's nothing to hand back.
      return popup.hide(restore && document.activeElement !== input(), popup.options?.finalFocusId ?? null);
    },
    restoreFocus() {
      if (document.activeElement !== input()) popup.restoreFocus();
    },
    /** @param {'first' | 'last'} which */
    highlightEdge(which) {
      move(which);
    },
    /** @param {string} inputIdNext @param {ComboboxOptions} next */
    setTrigger(inputIdNext, next) {
      id = inputIdNext;
      options = next;
    },
    dispose() {
      detach();
      for (const off of offs) off();
      popup.dispose();
    },
  };
}
