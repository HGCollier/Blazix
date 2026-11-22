// @ts-check
//
// Questionnaire's keyboard and focus handling. The questionnaire's state lives in .NET; this module
// reads it from the DOM (the active item carries data-active and data-status) and calls back for
// navigation. Ctrl/⌘+Enter confirms the active item, ↑/↓ move between its answers, ←/→ between
// items, Enter on a filled answer confirms it, and letter or number shortcuts pick an answer.

import { byId, call, isRTL, on } from './core/dom.js';

/** @typedef {import('./core/dom.js').DotNetRef} DotNetRef */

const ANSWER = 'input[data-type], [data-filled], [data-empty]';
const NAVIGABLE_INPUT_TYPES = ['email', 'password', 'search', 'tel', 'text', 'url'];

/** @param {Element} el */
function isChoice(el) {
  return el instanceof HTMLInputElement && (el.type === 'radio' || el.type === 'checkbox');
}

/** @param {Element} el */
function isRadio(el) {
  return el instanceof HTMLInputElement && el.type === 'radio';
}

/** @param {Element} el */
function isTextEntry(el) {
  if (el instanceof HTMLTextAreaElement || el instanceof HTMLSelectElement) return true;
  if (el instanceof HTMLInputElement) return !['button', 'checkbox', 'radio', 'reset', 'submit'].includes(el.type);
  return el instanceof HTMLElement && el.isContentEditable;
}

/** @param {Element} el */
function hasValue(el) {
  return 'value' in el && String(el.value).trim().length > 0;
}

/** A choice that's checked, or a freeform answer that has text. */
function isFilled(/** @type {HTMLElement} */ answer) {
  if (isChoice(answer)) return /** @type {HTMLInputElement} */ (answer).checked;
  return hasValue(answer);
}

/** A freeform answer that's empty, where ↑/↓ may leave for the choices. */
function isEmptyNavigableInput(/** @type {HTMLElement | undefined} */ answer) {
  return (
    answer instanceof HTMLInputElement &&
    !isChoice(answer) &&
    NAVIGABLE_INPUT_TYPES.includes(answer.type) &&
    !hasValue(answer)
  );
}

/** @param {HTMLElement} answer */
function choose(answer) {
  answer.focus();
  if (isChoice(answer)) answer.click();
}

/**
 * @param {string} formId
 * @param {DotNetRef} dotnet
 */
export function createQuestionnaire(formId, dotnet) {
  const form = byId(formId);

  /** The active item: the fieldset carrying data-active and data-status. */
  function activeItem() {
    return /** @type {HTMLElement | null} */ (form?.querySelector('[data-active][data-status]') ?? null);
  }

  /** @param {HTMLElement} item */
  function answersOf(item) {
    return /** @type {HTMLElement[]} */ (
      Array.from(item.querySelectorAll(ANSWER)).filter((el) => !(/** @type {HTMLInputElement} */ (el).disabled))
    );
  }

  /**
   * @param {HTMLElement} item
   * @param {Element} target
   * @param {1 | -1} direction
   */
  function moveAnswerFocus(item, target, direction) {
    const answers = answersOf(item);
    const index = answers.indexOf(/** @type {HTMLElement} */ (target));
    if (
      answers.length === 0 ||
      (isTextEntry(target) && !isEmptyNavigableInput(answers[index])) ||
      (index < 0 && target !== item)
    ) {
      return false;
    }

    const next =
      index < 0
        ? (answers.find(isFilled) ?? (direction === 1 ? answers[0] : answers[answers.length - 1]))
        : answers[(index + direction + answers.length) % answers.length];
    if (!next || next === target) return false;
    // Between radios the browser moves and checks by itself.
    if (index >= 0 && isRadio(target) && isRadio(next)) return false;

    next.focus();
    if (isRadio(next)) next.click();
    return true;
  }

  /** @param {KeyboardEvent} event */
  function onKeyDown(event) {
    const item = activeItem();
    const target = event.target;
    if (event.defaultPrevented || event.isComposing || event.keyCode === 229 || !item || !(target instanceof Element)) {
      return;
    }

    if (event.key === 'Enter' && (event.metaKey || event.ctrlKey) && !event.altKey && !event.shiftKey) {
      event.preventDefault();
      if (!event.repeat) call(dotnet, 'ConfirmAsync');
      return;
    }

    if (event.metaKey || event.ctrlKey || event.altKey) return;

    if (
      (event.key === 'ArrowUp' || event.key === 'ArrowDown') &&
      moveAnswerFocus(item, target, event.key === 'ArrowDown' ? 1 : -1)
    ) {
      event.preventDefault();
      return;
    }

    if ((event.key === 'ArrowLeft' || event.key === 'ArrowRight') && !isTextEntry(target) && !isRadio(target)) {
      event.preventDefault();
      if (event.repeat) return;
      // The previous question is the one the arrow points back to: Left, or Right in right-to-left text.
      if (event.key === (isRTL(item) ? 'ArrowRight' : 'ArrowLeft')) call(dotnet, 'PreviousAsync');
      else if (item.dataset.status !== 'unanswered') call(dotnet, 'NextAsync');
      return;
    }

    if (event.key === 'Enter') {
      if (!answersOf(item).includes(/** @type {HTMLElement} */ (target))) return;
      event.preventDefault();
      if (!event.repeat && isFilled(/** @type {HTMLElement} */ (target))) call(dotnet, 'ConfirmAsync');
      return;
    }

    const mode = form?.dataset.shortcuts;
    if (!mode || isTextEntry(target) || event.key.length !== 1) return;
    const key = mode === 'letters' ? event.key.toUpperCase() : event.key;
    if (!(mode === 'letters' ? /^[A-Z]$/ : /^[1-9]$/).test(key)) return;

    const answer = answersOf(item).find((el) => isChoice(el) && el.dataset.shortcut === key);
    if (!answer) return;
    event.preventDefault();
    if (!event.repeat) choose(answer);
  }

  // The answers' checked state and text live in .NET, which resets them; the browser's own reset
  // would change them behind its back.
  const offReset = on(form, 'reset', (event) => event.preventDefault());
  const offKeyDown = on(form, 'keydown', onKeyDown);

  return {
    /** @param {string} itemId */
    focusItem(itemId) {
      byId(itemId)?.focus();
    },

    /** Focuses the selected freeform answer, else the first control, else the item. @param {string} itemId */
    focusInvalid(itemId) {
      const item = byId(itemId);
      if (!item) return;
      const target =
        item.querySelector('input[data-filled][name]:not(:disabled)') ??
        item.querySelector('input:not([type=hidden]):not(:disabled), textarea:not(:disabled)') ??
        item;
      /** @type {HTMLElement} */ (target).focus();
    },

    /**
     * The browser's constraint validation for the item's filled answers (when NoValidate is false).
     * Reports the first problem and returns false.
     * @param {string} itemId
     */
    reportValidity(itemId) {
      const item = byId(itemId);
      if (!item) return true;
      const invalid = /** @type {HTMLInputElement[]} */ (answersOf(item)).find(
        (answer) => isFilled(answer) && answer.willValidate && !answer.validity.valid,
      );
      if (!invalid) return true;
      invalid.focus();
      invalid.reportValidity();
      return false;
    },

    dispose() {
      offReset();
      offKeyDown();
    },
  };
}
