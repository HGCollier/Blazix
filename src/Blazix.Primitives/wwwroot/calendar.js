const keys = ['ArrowLeft', 'ArrowRight', 'ArrowUp', 'ArrowDown', 'Home', 'End', 'PageUp', 'PageDown'];

// react-day-picker calls preventDefault on its navigation keys so they don't scroll the page.
// Blazor can only do that per element for every key, so it happens here.
export function attach(root) {
  const onKeyDown = (event) => {
    if (keys.includes(event.key) && event.target.matches('td > button')) {
      event.preventDefault();
    }
  };
  root.addEventListener('keydown', onKeyDown);
  return {
    focusDay() {
      root.querySelector('td[data-focused] > button')?.focus();
    },
    dispose() {
      root.removeEventListener('keydown', onKeyDown);
    },
  };
}
