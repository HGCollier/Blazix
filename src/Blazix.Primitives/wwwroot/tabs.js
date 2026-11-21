// @ts-check
//
// The Tabs indicator: measures the selected tab against its list and writes
// --active-tab-left/right/top/bottom/width/height on the indicator, so CSS can place and animate
// it. Follows size changes of the list and the tab (fonts loading, a label changing).

import { byId } from './core/dom.js';

/**
 * @param {string} indicatorId
 * @param {string} listId
 */
export function createIndicator(indicatorId, listId) {
  /** @type {ResizeObserver | null} */
  let observer = null;
  /** @type {Element | null} */
  let observedTab = null;

  function measure() {
    const indicator = byId(indicatorId);
    const list = byId(listId);
    if (!indicator || !list) return;
    const tab = /** @type {HTMLElement | null} */ (list.querySelector('[role="tab"][aria-selected="true"]'));
    if (!tab) {
      indicator.style.removeProperty('--active-tab-width');
      return;
    }
    const listRect = list.getBoundingClientRect();
    const tabRect = tab.getBoundingClientRect();
    // Lists may be scaled (an animation, zoom): measure in the list's own CSS pixels.
    const scaleX = list.offsetWidth > 0 ? listRect.width / list.offsetWidth : 1;
    const scaleY = list.offsetHeight > 0 ? listRect.height / list.offsetHeight : 1;
    const left = (tabRect.left - listRect.left) / scaleX + list.scrollLeft - list.clientLeft;
    const top = (tabRect.top - listRect.top) / scaleY + list.scrollTop - list.clientTop;
    const width = tab.offsetWidth;
    const height = tab.offsetHeight;
    const set = (/** @type {string} */ name, /** @type {number} */ value) => indicator.style.setProperty(name, `${value}px`);
    set('--active-tab-left', left);
    set('--active-tab-top', top);
    set('--active-tab-width', width);
    set('--active-tab-height', height);
    set('--active-tab-right', list.scrollWidth - left - width);
    set('--active-tab-bottom', list.scrollHeight - top - height);
    if (observedTab !== tab) {
      if (observedTab) observer?.unobserve(observedTab);
      observer?.observe(tab);
      observedTab = tab;
    }
  }

  observer = new ResizeObserver(() => measure());
  const list = byId(listId);
  if (list) observer.observe(list);
  measure();

  return {
    update: measure,
    dispose() {
      observer?.disconnect();
      observer = null;
    },
  };
}
