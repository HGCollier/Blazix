// @ts-check
// The docs site's own browser code: theme, table-of-contents tracking, search keys, sidebar scroll.
// No single-character shortcuts (WCAG 2.1.4): they fire by accident for speech input and keyboard users.
// Everything here is site chrome; the components it documents load their own JS.

const THEME_COLORS = { light: '#ffffff', dark: '#09090b' };

/** @param {boolean} dark */
function applyTheme(dark) {
  const root = document.documentElement;
  // Switch at once (like next-themes' disableTransitionOnChange): while this attribute is
  // on, <html>'s data-theme-switching variants turn every transition off, so colors don't fade.
  root.setAttribute('data-theme-switching', '');
  root.classList.toggle('dark', dark);
  root.style.colorScheme = dark ? 'dark' : 'light';
  document.querySelector('meta[name="theme-color"]')?.setAttribute('content', dark ? THEME_COLORS.dark : THEME_COLORS.light);
  void getComputedStyle(document.body).opacity; // apply the new colors with transitions off
  setTimeout(() => root.removeAttribute('data-theme-switching'), 1);
}

export function toggleTheme() {
  const dark = !document.documentElement.classList.contains('dark');
  try {
    localStorage.setItem('theme', dark ? 'dark' : 'light');
  } catch {
    // Storage can be off; the toggle still works for this page.
  }
  applyTheme(dark);
}

/** @param {EventTarget | null} target */
function isTyping(target) {
  return (target instanceof HTMLElement && target.isContentEditable) || target instanceof HTMLInputElement ||
    target instanceof HTMLTextAreaElement || target instanceof HTMLSelectElement;
}

const reducedMotion = () => matchMedia('(prefers-reduced-motion: reduce)').matches;

/**
 * Moves focus to an in-page link's target, as following a fragment does, so the skip link and the table of contents
 * take keyboard and screen reader users there too. An element that isn't focusable gets tabindex="-1".
 * @param {HTMLElement} target
 */
function focusTarget(target) {
  if (!target.matches('a[href], button, input, select, textarea, [tabindex], [contenteditable]')) target.setAttribute('tabindex', '-1');
  target.focus({ preventScroll: true });
}

// In-page links (#usage): with <base href="/"> the browser resolves them against the site root and leaves the page.
// Scroll to the target instead, move focus there, and put the fragment on the current URL.
document.addEventListener('click', (e) => {
  if (e.defaultPrevented || e.button !== 0 || e.metaKey || e.ctrlKey || e.shiftKey || e.altKey) return;
  const link = /** @type {HTMLElement} */ (e.target).closest?.('a[href^="#"]');
  if (!link) return;
  const id = decodeURIComponent(/** @type {HTMLAnchorElement} */ (link).getAttribute('href')?.slice(1) ?? '');
  const target = id ? document.getElementById(id) : null;
  if (!target) return;
  e.preventDefault();
  e.stopPropagation();
  history.pushState(history.state, '', `${location.pathname}${location.search}#${encodeURIComponent(id)}`);
  target.scrollIntoView({ behavior: reducedMotion() ? 'auto' : 'smooth', block: 'start' });
  focusTarget(target);
}, true);

/**
 * ⌘K or Ctrl+K opens the search dialog, unless you're typing somewhere.
 * @param {{ invokeMethodAsync: (name: string) => Promise<unknown> }} dotnet
 */
export function watchSearchKeys(dotnet) {
  /** @param {KeyboardEvent} e */
  const onKey = (e) => {
    if (!(e.key === 'k' && (e.metaKey || e.ctrlKey)) || isTyping(e.target)) return;
    e.preventDefault();
    dotnet.invokeMethodAsync('Toggle').catch(() => {});
  };
  document.addEventListener('keydown', onKey);
  return { dispose: () => document.removeEventListener('keydown', onKey) };
}

/**
 * Marks the table-of-contents link of the heading that's at the top of the viewport (data-active).
 * @param {HTMLElement | null} toc Null when the list was removed before the call arrived.
 */
export function watchToc(toc) {
  if (!toc) return { dispose: () => {} };
  /** @type {IntersectionObserver | undefined} */
  let observer;
  const connect = () => {
    observer?.disconnect();
    const links = /** @type {HTMLAnchorElement[]} */ ([...toc.querySelectorAll('a[href^="#"]')]);
    const ids = links.map((a) => decodeURIComponent(a.hash.slice(1)));
    observer = new IntersectionObserver((entries) => {
      for (const entry of entries) {
        if (!entry.isIntersecting) continue;
        for (const a of links) {
          const active = a.hash.slice(1) === entry.target.id;
          a.setAttribute('data-active', String(active));
          if (active) a.setAttribute('aria-current', 'location');
          else a.removeAttribute('aria-current');
        }
      }
    }, { rootMargin: '0% 0% -80% 0%' });
    for (const id of ids) {
      const heading = document.getElementById(id);
      if (heading) observer.observe(heading);
    }
  };
  connect();
  const mutations = new MutationObserver(connect);
  mutations.observe(toc, { childList: true, subtree: true });
  return { dispose: () => { observer?.disconnect(); mutations.disconnect(); } };
}

/**
 * The search dialog's list: filters as you type, moves the selection with the arrow keys, Enter follows it,
 * and ⌘C/Ctrl+C copies the selected component's install command. The footer (data-search-footer) says what
 * Enter and ⌘C do for the selection.
 * @param {HTMLInputElement | null} input
 * @param {HTMLElement | null} list
 * @param {HTMLElement | null} footer
 * Null when the dialog closed before the call arrived.
 */
export function searchList(input, list, footer) {
  if (!input || !list || !footer) return { dispose: () => {} };
  const dialog = input.closest('[role=dialog]') ?? document;
  const empty = dialog.querySelector('[data-search-empty]');
  const status = dialog.querySelector('[data-search-status]');
  /** @type {ReturnType<typeof setTimeout> | undefined} */
  let announce;
  // A moment after typing stops, say how many pages match (a status message: focus stays in the input).
  const announceCount = () => {
    clearTimeout(announce);
    announce = setTimeout(() => {
      const count = items().length;
      if (status) status.textContent = count === 0 ? 'No results found.' : `${count} ${count === 1 ? 'result' : 'results'}.`;
    }, 600);
  };
  const items = () => /** @type {HTMLElement[]} */ ([...list.querySelectorAll('[data-search-item]')]).filter((el) => !el.hidden);
  /** @param {HTMLElement | undefined} item */
  const select = (item) => {
    for (const el of list.querySelectorAll('[data-search-item]')) {
      el.setAttribute('data-selected', String(el === item));
      el.setAttribute('aria-selected', String(el === item));
    }
    if (item) {
      input.setAttribute('aria-activedescendant', item.id);
      item.scrollIntoView({ block: 'nearest' });
    } else {
      input.removeAttribute('aria-activedescendant');
    }
    const copy = item?.dataset.copy ?? '';
    const action = footer.querySelector('[data-search-action]');
    if (action) action.textContent = item ? 'Go to Page' : '';
    for (const el of /** @type {NodeListOf<HTMLElement>} */ (footer.querySelectorAll('[data-search-copy]'))) el.hidden = copy === '';
    const text = footer.querySelector('[data-search-copy-text]');
    if (text) text.textContent = copy;
  };
  const filter = () => {
    const query = input.value.trim().toLowerCase();
    let any = false;
    for (const group of list.querySelectorAll('[data-search-group]')) {
      let shown = false;
      for (const el of /** @type {NodeListOf<HTMLElement>} */ (group.querySelectorAll('[data-search-item]'))) {
        const text = `${el.dataset.value ?? ''} ${el.dataset.keywords ?? ''}`.toLowerCase();
        // Full-text results come from the server, already matched to what was typed.
        el.hidden = !el.hasAttribute('data-search-keep') && query !== '' && !text.includes(query);
        shown ||= !el.hidden;
      }
      /** @type {HTMLElement} */ (group).hidden = !shown;
      any ||= shown;
    }
    empty?.toggleAttribute('hidden', any);
    select(items()[0]);
    if (query !== '') announceCount();
  };
  /** @param {KeyboardEvent} e */
  const onKey = (e) => {
    const all = items();
    const current = all.findIndex((el) => el.getAttribute('data-selected') === 'true');
    if (e.key === 'ArrowDown' || e.key === 'ArrowUp') {
      e.preventDefault();
      if (all.length === 0) return;
      const next = e.key === 'ArrowDown' ? (current + 1) % all.length : (current - 1 + all.length) % all.length;
      select(all[next]);
    } else if (e.key === 'Home' && e.ctrlKey) {
      e.preventDefault();
      select(all[0]);
    } else if (e.key === 'End' && e.ctrlKey) {
      e.preventDefault();
      select(all[all.length - 1]);
    } else if (e.key === 'Enter' && current >= 0) {
      e.preventDefault();
      all[current].click();
    } else if (e.key === 'c' && (e.metaKey || e.ctrlKey) && input.selectionStart === input.selectionEnd && current >= 0) {
      const copy = all[current].dataset.copy;
      if (copy) {
        e.preventDefault();
        navigator.clipboard?.writeText(copy).catch(() => {});
      }
    }
  };
  /** @param {PointerEvent} e */
  const onMove = (e) => {
    const item = /** @type {HTMLElement | null} */ (/** @type {HTMLElement} */ (e.target).closest('[data-search-item]'));
    if (item && item.getAttribute('data-selected') !== 'true') select(item);
  };
  // Full-text results arrive after the filtering: update the empty message, and select one if nothing is.
  const results = new MutationObserver(() => {
    const any = items().length > 0;
    empty?.toggleAttribute('hidden', any);
    if (input.value.trim() !== '') announceCount();
    if (!items().some((el) => el.getAttribute('data-selected') === 'true')) select(items()[0]);
  });
  results.observe(list, { childList: true, subtree: true });
  input.addEventListener('input', filter);
  input.addEventListener('keydown', onKey);
  list.addEventListener('pointermove', onMove);
  filter();
  return {
    dispose: () => {
      results.disconnect();
      clearTimeout(announce);
      input.removeEventListener('input', filter);
      input.removeEventListener('keydown', onKey);
      list.removeEventListener('pointermove', onMove);
    },
  };
}

/**
 * Focuses the code in a container: "View Code" shows an example's whole source and removes itself, and focus would
 * otherwise drop to the page.
 * @param {HTMLElement | null} container
 */
export function focusCode(container) {
  /** @type {HTMLElement | null | undefined} */ (container?.querySelector('pre[tabindex], code[tabindex]'))?.focus({ preventScroll: true });
}

/**
 * Scrolls the sidebar so the active page's link is in view after navigating.
 * @param {HTMLElement | null} container Null when the sidebar was removed before the call arrived.
 */
export function revealActive(container) {
  if (!container) return;
  // The registry's SidebarMenuButton marks the active page with a bare data-active attribute.
  const items = /** @type {HTMLElement[]} */ ([...container.querySelectorAll('[data-active]:not([data-active="false"])')]);
  const active = items.sort((a, b) => (b.getAttribute('href')?.length ?? 0) - (a.getAttribute('href')?.length ?? 0))[0];
  if (!active) return;
  const box = container.getBoundingClientRect();
  const rect = active.getBoundingClientRect();
  if (rect.top < box.top || rect.bottom > box.bottom) {
    container.scrollTop += rect.top - box.top - (container.clientHeight - rect.height) / 2;
  }
}

// ---- Copy Page: the page as Markdown, for pasting into an LLM -------------------------

/** @param {Node} node */
function inlineMarkdown(node) {
  let out = '';
  for (const child of node.childNodes) {
    if (child.nodeType === Node.TEXT_NODE) {
      out += (child.textContent ?? '').replace(/\s+/g, ' ');
    } else if (child instanceof HTMLElement) {
      if (child.getAttribute('aria-hidden') === 'true' || child.classList.contains('sr-only')) continue;
      const text = inlineMarkdown(child);
      switch (child.tagName) {
        case 'CODE': out += '`' + (child.textContent ?? '') + '`'; break;
        case 'STRONG': case 'B': out += `**${text.trim()}**`; break;
        case 'EM': case 'I': out += `*${text.trim()}*`; break;
        case 'A': {
          const href = child.getAttribute('href') ?? '';
          out += href.startsWith('#') ? text : `[${text.trim()}](${new URL(href, location.href).href})`;
          break;
        }
        case 'BR': out += '\n'; break;
        // A block inside inline content (a table cell's description): set it apart.
        case 'P': case 'DIV': out += out.trim() ? ` — ${text.trim()}` : text; break;
        default: out += text;
      }
    }
  }
  return out;
}

/** @param {HTMLTableElement} table */
function tableMarkdown(table) {
  const rows = [...table.rows].map((row) => [...row.cells].map((cell) => inlineMarkdown(cell).trim().replace(/\|/g, '\\|')));
  if (rows.length === 0) return '';
  const head = `| ${rows[0].join(' | ')} |\n| ${rows[0].map(() => '---').join(' | ')} |`;
  return [head, ...rows.slice(1).map((r) => `| ${r.join(' | ')} |`)].join('\n');
}

/** @param {Element} figure */
function codeMarkdown(figure) {
  const code = figure.querySelector('code');
  const lang = code?.getAttribute('data-language') ?? '';
  const title = figure.querySelector('[data-code-title]')?.textContent?.trim();
  return `${title ? `${title}:\n\n` : ''}\`\`\`${lang}\n${code?.textContent ?? ''}\n\`\`\``;
}

/** @param {Element} container @param {string[]} out */
function blocksMarkdown(container, out) {
  for (const el of container.children) {
    if (!(el instanceof HTMLElement) || el.hidden) continue;
    const tag = el.tagName;
    const heading = /^H([2-6])$/.exec(tag);
    if (heading) {
      out.push(`${'#'.repeat(Number(heading[1]))} ${inlineMarkdown(el).replace(/#\s*$/, '').trim()}`);
    } else if (tag === 'P') {
      out.push(inlineMarkdown(el).trim());
    } else if (tag === 'UL' || tag === 'OL') {
      out.push([...el.children].map((li, i) => `${tag === 'OL' ? `${i + 1}.` : '-'} ${inlineMarkdown(li).trim()}`).join('\n'));
    } else if (tag === 'BLOCKQUOTE' || el.dataset.slot === 'alert') {
      out.push(inlineMarkdown(el).trim().split('\n').map((l) => `> ${l}`).join('\n'));
    } else if (el.matches('[data-code-figure]')) {
      out.push(codeMarkdown(el));
    } else if (el.querySelector(':scope > template[data-example-source]')) {
      // A preview: the example's whole source, which the page only shows a few lines of.
      const source = /** @type {HTMLTemplateElement} */ (el.querySelector(':scope > template[data-example-source]'));
      // Prerendered, the text is in the template's content; rendered by Blazor, it's in the template itself.
      out.push('```razor\n' + (source.content.textContent || source.textContent || '').trim() + '\n```');
    } else if (el.querySelector(':scope > table')) {
      out.push(tableMarkdown(/** @type {HTMLTableElement} */ (el.querySelector(':scope > table'))));
    } else if (el.matches('[data-slot=tabs]')) {
      // Installation: the command the reader has chosen.
      const command = el.querySelector('[data-slot=tabs-content]:not([hidden]) code[data-language=bash]');
      if (command) out.push('```bash\n' + command.textContent?.trim() + '\n```');
    } else {
      blocksMarkdown(el, out);
    }
  }
}

/**
 * Copies the docs page ([data-slot=docs]) as Markdown. Resolves false when there's no page or the clipboard refused.
 */
export async function copyPage() {
  const page = document.querySelector('[data-slot=docs]');
  if (!page) return false;
  const out = [];
  const title = page.querySelector('h1')?.textContent?.trim();
  if (title) out.push(`# ${title}`);
  const description = page.querySelector('[data-docs-description]')?.textContent?.trim();
  if (description) out.push(description);
  const content = page.querySelector('[data-docs-content]');
  if (content) blocksMarkdown(content, out);
  try {
    await navigator.clipboard.writeText(out.filter((b) => b).join('\n\n') + '\n');
    return true;
  } catch {
    return false;
  }
}
