// @ts-check
//
// Typeahead as the WAI-ARIA Authoring Practices describe it for menus and listboxes (after UI SDK's
// text/typeahead): typed characters build a query that clears after 500 ms, and the next item whose
// label starts with it is the match. Pressing the same character again steps through the items
// that start with it. Case and accents fold.

const fold = (/** @type {string} */ text) => text.normalize('NFD').replace(/\p{Diacritic}/gu, '').toLocaleLowerCase().trim();

export function createTypeahead(timeout = 500) {
  let query = '';
  let lastKey = '';
  let stepping = false;
  /** @type {ReturnType<typeof setTimeout> | undefined} */
  let timer;

  function reset() {
    query = '';
    lastKey = '';
    stepping = false;
    clearTimeout(timer);
  }

  /** @param {string[]} labels @param {string} prefix @param {number} start */
  function scan(labels, prefix, start) {
    const count = labels.length;
    for (let step = 0; step < count; step++) {
      const index = (((start + step) % count) + count) % count;
      if (fold(labels[index]).startsWith(prefix)) return index;
    }
    return -1;
  }

  /**
   * The index to focus for a key press, or -1.
   * @param {string} key @param {string[]} labels @param {number} current
   */
  function search(key, labels, current) {
    // Space only continues a query. Before one, it's the key that activates.
    if (key.length !== 1 || (key === ' ' && !query)) return -1;
    clearTimeout(timer);
    timer = setTimeout(reset, timeout);
    const char = fold(key) || key;
    stepping = char === lastKey && (stepping || query.length === 1);
    lastKey = char;
    query += char;
    const start = (/** @type {string} */ q) => (q.length === 1 ? current + 1 : Math.max(current, 0));
    if (stepping) {
      const whole = scan(labels, query, Math.max(current, 0));
      return whole !== -1 ? whole : scan(labels, char, start(char));
    }
    return scan(labels, query, start(query));
  }

  return Object.assign(search, { reset, active: () => query.length > 0 });
}
