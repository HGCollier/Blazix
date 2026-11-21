// A port of react-resizable-panels v4 (Group, Panel, Separator).
// Layouts are arrays of percentages, index-aligned with a group's panels; .NET receives them keyed by panel id.

const groups = new Set();
let interaction = { state: 'inactive', cursorFlags: 0 };
let cursorSheet;
let documentListeners = false;

const CURSOR_HORIZONTAL_MIN = 0b0001;
const CURSOR_HORIZONTAL_MAX = 0b0010;
const CURSOR_VERTICAL_MIN = 0b0100;
const CURSOR_VERTICAL_MAX = 0b1000;
const CURSOR_HORIZONTAL = 0b0011;
const CURSOR_VERTICAL = 0b1100;

// ---- Layout math (lib/global/utils) ----

export function formatLayoutNumber(number) {
  return parseFloat(number.toFixed(3));
}

function numbersEqual(actual, expected, minimumDelta = 0) {
  return Math.abs(formatLayoutNumber(actual) - formatLayoutNumber(expected)) <= minimumDelta;
}

function compareNumbers(actual, expected) {
  return numbersEqual(actual, expected) ? 0 : actual > expected ? 1 : -1;
}

function layoutsEqual(a, b) {
  return a.length === b.length && a.every((size, index) => compareNumbers(size, b[index]) === 0);
}

function validateItemSize({ overrideDisabledItems, constraints, prevSize, size }) {
  const { collapsedSize = 0, collapsedThreshold, collapsible, disabled, maxSize = 100, minSize = 0 } = constraints;
  if (disabled && !overrideDisabledItems) return prevSize;

  if (compareNumbers(size, minSize) < 0) {
    if (collapsible) {
      const threshold = collapsedThreshold ?? (minSize - collapsedSize) / 2;
      const wasCollapsed = compareNumbers(prevSize, collapsedSize) <= 0;
      const boundary = wasCollapsed ? collapsedSize + threshold : minSize - threshold;
      const comparison = compareNumbers(size, boundary);
      size = compareNumbers(size, collapsedSize) <= 0 || comparison < 0 ||
        (collapsedThreshold !== undefined && wasCollapsed && comparison === 0)
        ? collapsedSize
        : minSize;
    } else {
      size = minSize;
    }
  }

  return formatLayoutNumber(Math.min(maxSize, size));
}

export function adjustLayoutByDelta({ delta, initialLayout, constraints: all, pivotIndices, prevLayout, trigger }) {
  if (numbersEqual(delta, 0)) return initialLayout;

  const overrideDisabledItems = trigger === 'imperative-api';
  const nextLayout = [...initialLayout];
  const [firstPivot, secondPivot] = pivotIndices;
  const validate = (index, prevSize, size) =>
    validateItemSize({ overrideDisabledItems, constraints: all[index], prevSize, size });
  let deltaApplied = 0;

  if (trigger === 'keyboard') {
    {
      // Expand a collapsed panel all the way to its min size
      const index = delta < 0 ? secondPivot : firstPivot;
      const { collapsedSize = 0, collapsible, minSize = 0 } = all[index];
      if (collapsible && numbersEqual(initialLayout[index], collapsedSize)) {
        const localDelta = minSize - initialLayout[index];
        if (compareNumbers(localDelta, Math.abs(delta)) > 0) delta = delta < 0 ? -localDelta : localDelta;
      }
    }
    {
      // Collapse a panel that is at its min size
      const index = delta < 0 ? firstPivot : secondPivot;
      const { collapsedSize = 0, collapsible, minSize = 0 } = all[index];
      if (collapsible && numbersEqual(initialLayout[index], minSize)) {
        const localDelta = initialLayout[index] - collapsedSize;
        if (compareNumbers(localDelta, Math.abs(delta)) > 0) delta = delta < 0 ? -localDelta : localDelta;
      }
    }
  } else {
    // Dragging a collapsed panel past the threshold expands it
    const index = delta < 0 ? secondPivot : firstPivot;
    const prevSize = initialLayout[index];
    const { collapsedSize, collapsedThreshold, collapsible, minSize } = all[index];
    if (collapsible && compareNumbers(prevSize, minSize) < 0) {
      const gapSize = minSize - collapsedSize;
      const threshold = collapsedThreshold ?? gapSize / 2;
      if (compareNumbers(prevSize + Math.abs(delta), minSize) < 0) {
        const comparison = compareNumbers(Math.abs(delta), threshold);
        const expandAtBoundary = collapsedThreshold === undefined && delta < 0;
        delta = comparison > 0 || (comparison === 0 && expandAtBoundary) ? (delta < 0 ? -gapSize : gapSize) : 0;
      }
    }
  }

  {
    // The most the panels on the growing side can absorb limits the delta
    const increment = delta < 0 ? 1 : -1;
    let maxAvailableDelta = 0;
    for (let index = delta < 0 ? secondPivot : firstPivot; index >= 0 && index < all.length; index += increment) {
      maxAvailableDelta += validate(index, initialLayout[index], 100) - initialLayout[index];
    }
    const minAbsDelta = Math.min(Math.abs(delta), Math.abs(maxAvailableDelta));
    delta = delta < 0 ? -minAbsDelta : minAbsDelta;
  }

  // Take the delta from the shrinking side, cascading past panels at their min size
  for (let index = delta < 0 ? firstPivot : secondPivot; index >= 0 && index < all.length; index += delta < 0 ? -1 : 1) {
    const prevSize = initialLayout[index];
    const safeSize = validate(index, prevSize, prevSize - (Math.abs(delta) - Math.abs(deltaApplied)));
    if (!numbersEqual(prevSize, safeSize)) {
      deltaApplied += prevSize - safeSize;
      nextLayout[index] = safeSize;
      if (compareNumbers(deltaApplied, Math.abs(delta)) >= 0) break;
    }
  }

  if (prevLayout.every((size, index) => size === nextLayout[index])) return prevLayout;

  {
    // Give it to the pivot panel on the growing side
    const pivotIndex = delta < 0 ? secondPivot : firstPivot;
    const unsafeSize = initialLayout[pivotIndex] + deltaApplied;
    const safeSize = validate(pivotIndex, initialLayout[pivotIndex], unsafeSize);
    nextLayout[pivotIndex] = safeSize;

    // Expanding or contracting it changed another panel's collapsed state
    if (!numbersEqual(safeSize, unsafeSize)) {
      let deltaRemaining = unsafeSize - safeSize;
      for (let index = pivotIndex; index >= 0 && index < all.length; index += delta > 0 ? -1 : 1) {
        const prevSize = nextLayout[index];
        const size = validate(index, prevSize, prevSize + deltaRemaining);
        if (!numbersEqual(prevSize, size)) {
          deltaRemaining -= size - prevSize;
          nextLayout[index] = size;
        }
        if (numbersEqual(deltaRemaining, 0)) break;
      }
    }
  }

  const totalSize = nextLayout.reduce((total, size) => total + size, 0);
  return numbersEqual(totalSize, 100, 0.1) ? nextLayout : prevLayout;
}

export function validateLayout(layout, constraints) {
  if (layout.length !== constraints.length) {
    throw Error(`Invalid ${constraints.length} panel layout: ${layout.map((size) => `${size}%`).join(', ')}`);
  }
  const nextLayout = [...layout];
  const total = nextLayout.reduce((sum, size) => sum + size, 0);
  if (!numbersEqual(total, 100) && nextLayout.length > 0) {
    for (let index = 0; index < nextLayout.length; index++) nextLayout[index] = (100 / total) * nextLayout[index];
  }

  let remainingSize = 0;
  for (let index = 0; index < constraints.length; index++) {
    const unsafeSize = nextLayout[index];
    const safeSize = validateItemSize({ overrideDisabledItems: true, constraints: constraints[index], prevSize: layout[index], size: unsafeSize });
    if (unsafeSize != safeSize) {
      remainingSize += unsafeSize - safeSize;
      nextLayout[index] = safeSize;
    }
  }

  // Give left over space to any panel(s) that permit it
  if (!numbersEqual(remainingSize, 0)) {
    for (let index = 0; index < constraints.length; index++) {
      const prevSize = nextLayout[index];
      const safeSize = validateItemSize({ overrideDisabledItems: true, constraints: constraints[index], prevSize, size: prevSize + remainingSize });
      if (prevSize !== safeSize) {
        remainingSize -= safeSize - prevSize;
        nextLayout[index] = safeSize;
        if (numbersEqual(remainingSize, 0)) break;
      }
    }
  }
  return nextLayout;
}

export function calculateDefaultLayout(constraints) {
  const explicit = constraints.filter((current) => current.defaultSize !== undefined);
  const total = explicit.reduce((sum, current) => sum + formatLayoutNumber(current.defaultSize), 0);
  const share = constraints.length === explicit.length ? 0 : formatLayoutNumber((100 - total) / (constraints.length - explicit.length));
  return constraints.map((current) => current.defaultSize !== undefined ? formatLayoutNumber(current.defaultSize) : share);
}

const normalizeLayout = (layout) => validateLayout(layout, layout.map(() => ({ collapsedSize: 0, collapsible: false, maxSize: 100, minSize: 0 })));

export function separatorAriaValues(layout, constraints, index) {
  const { collapsible, collapsedSize, maxSize, minSize } = constraints[index];
  const sizeAfter = (delta) => validateLayout(adjustLayoutByDelta({
    delta, initialLayout: layout, constraints, pivotIndices: [index, index + 1], prevLayout: layout,
  }), constraints)[index];
  return {
    valueMin: sizeAfter((collapsible ? collapsedSize : minSize) - layout[index]),
    valueMax: sizeAfter(maxSize - layout[index]),
    valueNow: layout[index],
  };
}

// Resizes one panel to `nextSize` (a percentage) the way v4's imperative `resize()` does
export function resizePanelLayout(layout, constraints, index, nextSize) {
  const isFirst = index === 0;
  const isLast = index === layout.length - 1;
  const targetSize = validateItemSize({ overrideDisabledItems: true, constraints: constraints[index], prevSize: layout[index], size: nextSize });
  const isCollapsed = (other, current) => other.collapsible && numbersEqual(other.collapsedSize, current);

  // First try to take space from (or give it to) the nearest panels without collapsing or expanding any
  const fitted = [...layout];
  let remainingDelta = targetSize - layout[index];
  for (const step of isFirst ? [1] : isLast ? [-1] : [1, -1]) {
    for (let current = index + step; current >= 0 && current < layout.length && !numbersEqual(remainingDelta, 0); current += step) {
      const other = constraints[current];
      const size = fitted[current];
      if (isCollapsed(other, size) ||
        (remainingDelta > 0 ? compareNumbers(size, other.minSize) <= 0 : compareNumbers(size, other.maxSize) >= 0)) continue;
      const nextOtherSize = remainingDelta > 0 ? Math.max(other.minSize, size - remainingDelta) : Math.min(other.maxSize, size - remainingDelta);
      remainingDelta -= size - nextOtherSize;
      fitted[current] = formatLayoutNumber(nextOtherSize);
    }
  }
  fitted[index] = formatLayoutNumber(targetSize - remainingDelta);
  if (numbersEqual(remainingDelta, 0)) return fitted;

  // Otherwise allow collapsing or expanding other panels, cascading away from this one
  // ponytail: v4 also prefers the side that doesn't change another panel's collapsed state; add if it matters.
  const adjustAgainst = (side, current, sizeDelta) => {
    const step = side === 'after' ? 1 : -1;
    const partner = (predicate) => {
      for (let other = index + step; other >= 0 && other < current.length; other += step) {
        if (!isCollapsed(constraints[other], current[other]) && predicate(other)) return other;
      }
    };
    const partnerIndex = partner((other) => sizeDelta > 0
      ? compareNumbers(current[other], constraints[other].minSize) > 0
      : compareNumbers(current[other], constraints[other].maxSize) < 0) ?? partner(() => true) ?? index + step;
    return adjustLayoutByDelta({
      delta: side === 'after' ? sizeDelta : -sizeDelta,
      initialLayout: current,
      constraints,
      pivotIndices: side === 'after' ? [index, partnerIndex] : [partnerIndex, index],
      prevLayout: current,
      trigger: 'imperative-api',
    });
  };
  if (isFirst || isLast) return adjustAgainst(isLast ? 'before' : 'after', layout, nextSize - layout[index]);
  const after = adjustAgainst('after', layout, nextSize - layout[index]);
  const remainder = nextSize - after[index];
  return numbersEqual(remainder, 0) ? after : adjustAgainst('before', after, remainder);
}

// ---- DOM measurement ----

const isHorizontal = (group) => group.options.orientation === 'horizontal';

function axisSizeOf(group) {
  return group.panels.reduce((total, panel) => {
    const rect = panel.element.getBoundingClientRect();
    return total + (isHorizontal(group) ? rect.width : rect.height);
  }, 0);
}

function toPixels(value, axisSize, element) {
  const numeric = parseFloat(value);
  const unit = /(%|px|rem|em|vh|vw)$/.exec(value)?.[1] ?? '%';
  const fontSize = (target) => parseFloat(getComputedStyle(target).fontSize);
  switch (unit) {
    case 'px': return numeric;
    case 'rem': return numeric * fontSize(document.documentElement);
    case 'em': return numeric * fontSize(element);
    case 'vh': return (numeric * window.innerHeight) / 100;
    case 'vw': return (numeric * window.innerWidth) / 100;
    default: return (numeric / 100) * axisSize;
  }
}

function constraintsOf(group, axisSize) {
  return group.panels.map(({ element }) => {
    const attribute = (name) => element.getAttribute(`data-${name}`) ?? undefined;
    const collapsible = element.hasAttribute('data-collapsible');
    const disabled = element.hasAttribute('data-disabled');
    if (axisSize === 0) {
      // Can't calculate anything meaningful if the group has no size (e.g. it's in a hidden subtree)
      return { collapsedSize: 0, collapsible, defaultSize: undefined, disabled, minSize: 0, maxSize: 100 };
    }
    const percent = (name, fallback) => {
      const value = attribute(name);
      return value === undefined ? fallback : formatLayoutNumber((toPixels(value, axisSize, element) / axisSize) * 100);
    };
    return {
      collapsedSize: percent('collapsed-size', 0),
      collapsible,
      defaultSize: percent('default-size', undefined),
      disabled,
      minSize: percent('min-size', 0),
      maxSize: percent('max-size', 100),
    };
  });
}

// Hit regions are explicit separators or, without one, the space between two adjacent panels
function hitRegionsOf(group) {
  const regions = [];
  const enabled = group.panels.map((panel, index) => panel.element.hasAttribute('data-disabled') ? -1 : index).filter((index) => index >= 0);
  if (enabled.length < 2) return regions;

  const horizontal = isHorizontal(group);
  const minimum = matchMedia('(pointer:coarse)').matches ? 20 : 10;
  let axisSize;
  let disabledSeparator = false;
  let index = -1;
  let prevRect;
  let pending = [];
  // ponytail: static (non panel/separator) children between panels are ignored; v4 watches panel edges instead.
  for (const child of group.element.children) {
    if (child.hasAttribute('data-panel')) {
      index++;
      const rect = child.getBoundingClientRect();
      if (prevRect) {
        const targets = pending.length ? pending : [horizontal
          ? new DOMRect(prevRect.right, rect.top, rect.left - prevRect.right, rect.height)
          : new DOMRect(rect.left, prevRect.bottom, rect.width, rect.top - prevRect.bottom)];
        for (const target of targets) {
          if (!disabledSeparator && index > enabled[0] && index <= enabled[enabled.length - 1]) {
            let hit = target instanceof DOMRect ? target : target.getBoundingClientRect();
            if (hit.width < minimum) hit = new DOMRect(hit.x - (minimum - hit.width) / 2, hit.y, minimum, hit.height);
            if (hit.height < minimum) hit = new DOMRect(hit.x, hit.y - (minimum - hit.height) / 2, hit.width, minimum);
            axisSize ??= axisSizeOf(group);
            regions.push({ group, axisSize, panels: [index - 1, index], separator: target instanceof DOMRect ? undefined : target, rect: hit });
          }
          disabledSeparator = false;
        }
      }
      prevRect = rect;
      pending = [];
    } else if (child.hasAttribute('data-separator')) {
      if (child.hasAttribute('aria-disabled')) disabledSeparator = true;
      pending.push(child);
    }
  }
  return regions;
}

function matchingHitRegions(event) {
  const matches = [];
  for (const group of groups) {
    if (group.options.disabled) continue;
    let closest;
    let minDistance = { x: Infinity, y: Infinity };
    for (const region of hitRegionsOf(group)) {
      const { rect } = region;
      const distance = {
        x: event.clientX >= rect.left && event.clientX <= rect.right ? 0 : Math.min(Math.abs(event.clientX - rect.left), Math.abs(event.clientX - rect.right)),
        y: event.clientY >= rect.top && event.clientY <= rect.bottom ? 0 : Math.min(Math.abs(event.clientY - rect.top), Math.abs(event.clientY - rect.bottom)),
      };
      const [primary, cross] = isHorizontal(group) ? ['x', 'y'] : ['y', 'x'];
      if (distance[primary] < minDistance[primary] || (distance[primary] === minDistance[primary] && distance[cross] <= minDistance[cross])) {
        closest = region;
        minDistance = distance;
      }
    }
    // ponytail: v4 compares stacking order; anything at the pointer that isn't inside (or around) the group is treated as covering it.
    const target = event.target;
    const viable = !(target instanceof Element) || target.contains(group.element) || group.element.contains(target);
    if (closest && minDistance.x <= 0 && minDistance.y <= 0 && viable) matches.push(closest);
  }
  return matches;
}

// ---- Rendering state ----

function paint(group) {
  const { layout, constraints } = group.state;
  group.panels.forEach(({ element }, index) => {
    element.style.flexBasis = '0';
    element.style.flexShrink = '1';
    element.style.flexGrow = String(layout[index] ?? 1);
  });
  group.separators.forEach((element) => {
    const pair = group.separatorPanels.get(element);
    if (!pair) return;
    const { valueMin, valueMax, valueNow } = separatorAriaValues(layout, constraints, pair[0]);
    element.setAttribute('aria-controls', group.panels[pair[0]].id);
    element.setAttribute('aria-valuemin', String(valueMin));
    element.setAttribute('aria-valuemax', String(valueMax));
    element.setAttribute('aria-valuenow', String(valueNow));
  });
}

function refreshSeparators() {
  for (const group of groups) {
    for (const element of group.separators) {
      let value;
      if (element.hasAttribute('aria-disabled')) {
        value = 'disabled';
      } else {
        const state = interaction.state !== 'inactive' && interaction.hitRegions.some((region) => region.separator === element)
          ? interaction.state
          : 'inactive';
        value = state === 'active' ? 'active' : element.matches(':focus') ? 'focus' : state;
      }
      if (element.getAttribute('data-separator') !== value) element.setAttribute('data-separator', value);
    }
  }
}

function cursorStyle() {
  let horizontal = 0;
  let vertical = 0;
  for (const { group } of interaction.hitRegions) {
    if (group.options.disableCursor) continue;
    if (isHorizontal(group)) horizontal++;
    else vertical++;
  }
  if (!horizontal && !vertical) return undefined;

  const advanced = navigator.userAgent.includes('Chrome') || navigator.userAgent.includes('Firefox');
  const flags = interaction.cursorFlags;
  if (interaction.state === 'active' && flags && advanced) {
    const verticalSuffix = (min, max, none) => (flags & CURSOR_VERTICAL_MIN ? min : flags & CURSOR_VERTICAL_MAX ? max : none);
    if (flags & CURSOR_HORIZONTAL_MIN) return verticalSuffix('se-resize', 'ne-resize', 'e-resize');
    if (flags & CURSOR_HORIZONTAL_MAX) return verticalSuffix('sw-resize', 'nw-resize', 'w-resize');
    return verticalSuffix('s-resize', 'n-resize', undefined);
  }
  if (horizontal && vertical) return advanced ? 'move' : 'grab';
  if (horizontal) return advanced ? 'ew-resize' : 'col-resize';
  return advanced ? 'ns-resize' : 'row-resize';
}

function updateCursor() {
  if (!document.adoptedStyleSheets) return;
  if (!cursorSheet) {
    cursorSheet = new CSSStyleSheet();
    document.adoptedStyleSheets = [...document.adoptedStyleSheets, cursorSheet];
  }
  const cursor = interaction.state === 'inactive' ? undefined : cursorStyle();
  cursorSheet.replaceSync(cursor ? `*, *:hover {cursor: ${cursor} !important; }` : '');
}

function setInteraction(next) {
  interaction = next;
  updateCursor();
  refreshSeparators();
}

function toObject(group, layout) {
  return Object.fromEntries(group.panels.map((panel, index) => [panel.id, layout[index]]));
}

function sameLayout(a, b) {
  const keys = Object.keys(a);
  return b !== undefined && keys.length === Object.keys(b).length && keys.every((key) => b[key] !== undefined && compareNumbers(a[key], b[key]) === 0);
}

function notify(group) {
  const { state } = group;
  if (state.deferred || state.constraints.length === 0) return;
  const layout = toObject(group, state.layout);
  const call = (method) => group.dotnet.invokeMethodAsync(method, layout).catch(() => { });
  if (!sameLayout(layout, group.notified.change)) {
    group.notified.change = layout;
    if (group.options.notifyChange) call('LayoutChange');
  }
  const completed = interaction.state !== 'active' || !interaction.hitRegions.some((region) => region.group === group);
  if (completed && !sameLayout(layout, group.notified.changed)) {
    group.notified.changed = layout;
    if (group.options.notifyChanged) call('LayoutChanged');
  }
}

function commit(group, next) {
  const prev = group.state?.deferred ? undefined : group.state;
  group.state = next;
  paint(group);
  if (next.deferred || next.constraints.length === 0) return;

  // Remember the layout for these panels, and the size of any panel that just collapsed
  group.cache[group.panels.map(({ id }) => id).join(',')] = next.requestedLayout;
  if (prev && prev.layout.length === next.layout.length) {
    next.constraints.forEach((constraints, index) => {
      if (constraints.collapsible &&
        numbersEqual(constraints.collapsedSize, next.layout[index]) &&
        !numbersEqual(constraints.collapsedSize, prev.layout[index])) {
        group.expanded[group.panels[index].id] = prev.layout[index];
      }
    });
  }
  notify(group);
}

const withLayout = (group, layout) => ({ ...group.state, layout, requestedLayout: layout });

// ---- Pointer interactions ----

function updateActive(event, useDownPoint) {
  let nextFlags = 0;
  for (const region of interaction.hitRegions) {
    const { group } = region;
    if (!groups.has(group) || !region.axisSize) continue;
    const horizontal = isHorizontal(group);
    const delta = useDownPoint
      ? (((horizontal ? event.clientX - interaction.downAt.x : event.clientY - interaction.downAt.y) / region.axisSize) * 100)
      : ((horizontal ? event.clientX : event.clientY) < 0 ? -100 : 100);
    const { layout, constraints } = group.state;
    const next = adjustLayoutByDelta({
      delta,
      initialLayout: interaction.initialLayouts.get(group),
      constraints,
      pivotIndices: region.panels,
      prevLayout: layout,
      trigger: 'mouse-or-touch',
    });
    if (layoutsEqual(next, layout)) {
      // The pointer has passed the allowed bounds
      if (delta !== 0 && !group.options.disableCursor) {
        nextFlags |= horizontal
          ? (delta < 0 ? CURSOR_HORIZONTAL_MIN : CURSOR_HORIZONTAL_MAX)
          : (delta < 0 ? CURSOR_VERTICAL_MIN : CURSOR_VERTICAL_MAX);
      }
    } else {
      commit(group, withLayout(group, next));
    }
  }

  // Keep the previous flags if the pointer didn't move along that axis (browsers round clientX/clientY)
  const prevFlags = interaction.cursorFlags;
  interaction.cursorFlags =
    ((event.movementX === 0 ? prevFlags : nextFlags) & CURSOR_HORIZONTAL) |
    ((event.movementY === 0 ? prevFlags : nextFlags) & CURSOR_VERTICAL);
  updateCursor();
}

function finishActive() {
  const { hitRegions } = interaction;
  setInteraction({ state: 'inactive', cursorFlags: 0 });
  for (const { group } of hitRegions) {
    if (groups.has(group)) notify(group);
  }
}

const isActivePointer = (event) => interaction.state === 'active' && (event?.pointerId === undefined || event.pointerId === interaction.pointerId);

function onPointerDown(event) {
  if (event.defaultPrevented || (event.pointerType === 'mouse' && event.button > 0)) return;
  if (interaction.state === 'active') {
    // The same pointer can't be pressed twice, so its release was missed
    if (isActivePointer(event)) finishActive();
    return;
  }

  const hitRegions = matchingHitRegions(event);
  if (hitRegions.length === 0) return;

  const initialLayouts = new Map();
  // A plain programmatic focus, as react-resizable-panels 4.5 does: Chromium shows the
  // focus ring for it, so the handle stays highlighted while it's dragged.
  hitRegions.find((region) => region.separator)?.separator.focus({ preventScroll: true });
  for (const { group } of hitRegions) initialLayouts.set(group, group.state.layout);

  setInteraction({
    state: 'active',
    cursorFlags: 0,
    hitRegions,
    initialLayouts,
    downAt: { x: event.clientX, y: event.clientY },
    pointerId: event.pointerId,
  });
  event.preventDefault();
}

function onPointerMove(event) {
  if (event.defaultPrevented) return;

  if (interaction.state === 'active') {
    if (!isActivePointer(event)) return;
    // The pointer was released outside of the document (e.g. over a cross-origin iframe)
    if (event.buttons === 0) {
      finishActive();
      return;
    }
    for (const { separator } of interaction.hitRegions) {
      if (separator?.isConnected && !separator.hasPointerCapture(event.pointerId)) separator.setPointerCapture(event.pointerId);
    }
    updateActive(event, true);
    return;
  }

  const hitRegions = matchingHitRegions(event);
  if (hitRegions.length === 0) {
    if (interaction.state !== 'inactive') setInteraction({ state: 'inactive', cursorFlags: 0 });
  } else if (interaction.state !== 'hover' || !sameRegions(interaction.hitRegions, hitRegions)) {
    setInteraction({ state: 'hover', cursorFlags: 0, hitRegions });
  }
}

function sameRegions(a, b) {
  return a.length === b.length && a.every((region, index) =>
    region.group === b[index].group && region.separator === b[index].separator && region.panels[0] === b[index].panels[0]);
}

function onPointerUp(event) {
  if (event.defaultPrevented || (event.pointerType === 'mouse' && event.button > 0) || !isActivePointer(event)) return;
  if (interaction.hitRegions.length > 0) event.preventDefault();
  finishActive();
}

function onPointerCancel(event) {
  if (isActivePointer(event)) finishActive();
}

function onLostPointerCapture(event) {
  if (interaction.state !== 'active') return;
  const separators = interaction.hitRegions.flatMap(({ separator }) => separator ? [separator] : []);
  if (!separators.includes(event.target) || separators.some((element) => element.hasPointerCapture(event.pointerId))) return;
  finishActive();
}

function onPointerLeave(event) {
  if (isActivePointer(event)) updateActive(event, false);
}

function onPointerOut(event) {
  if (event.relatedTarget?.localName === 'iframe' && interaction.state === 'hover') {
    setInteraction({ state: 'inactive', cursorFlags: 0 });
  }
}

function onContextMenu(event) {
  if (!event.defaultPrevented && interaction.state === 'active') finishActive();
}

function onWindowBlur(event) {
  if (event.target === window && interaction.state === 'active') finishActive();
}

// Double-clicking a separator resets the adjacent panel that has a default size
function onDoubleClick(event) {
  if (event.defaultPrevented) return;
  for (const { group, panels, separator } of matchingHitRegions(event)) {
    if (!separator) continue;
    const index = panels.find((current) => group.panels[current].element.hasAttribute('data-default-size'));
    if (index === undefined) continue;
    const { state } = group;
    const element = group.panels[index].element;
    const size = formatLayoutNumber((toPixels(element.getAttribute('data-default-size'), state.axisSize, element) / state.axisSize) * 100);
    const layout = validateLayout(resizePanelLayout(state.layout, state.constraints, index, size), state.constraints);
    if (!layoutsEqual(layout, state.layout)) commit(group, withLayout(group, layout));
    event.preventDefault();
  }
}

function toggleDocumentListeners(add) {
  const method = add ? 'addEventListener' : 'removeEventListener';
  document[method]('contextmenu', onContextMenu, true);
  document[method]('dblclick', onDoubleClick, true);
  document[method]('lostpointercapture', onLostPointerCapture, true);
  document[method]('pointercancel', onPointerCancel, true);
  document[method]('pointerdown', onPointerDown, true);
  document[method]('pointerleave', onPointerLeave);
  document[method]('pointermove', onPointerMove);
  document[method]('pointerout', onPointerOut);
  document[method]('pointerup', onPointerUp, true);
  window[method]('blur', onWindowBlur);
  documentListeners = add;
}

// ---- Keyboard ----

function onSeparatorKeyDown(event) {
  if (event.defaultPrevented) return;
  const element = event.currentTarget;
  const group = [...groups].find((current) => current.separators.includes(element));
  if (!group || group.options.disabled || element.hasAttribute('aria-disabled')) return;
  const pair = group.separatorPanels.get(element);
  if (!pair) return;

  const adjust = (delta) => {
    const { state } = group;
    const prevLayout = state.deferred ? [] : state.layout;
    if (prevLayout.length === 0) return;
    const next = validateLayout(adjustLayoutByDelta({
      delta, initialLayout: prevLayout, constraints: state.constraints, pivotIndices: pair, prevLayout, trigger: 'keyboard',
    }), state.constraints);
    if (!layoutsEqual(prevLayout, next)) commit(group, withLayout(group, next));
  };
  const horizontal = isHorizontal(group);

  switch (event.key) {
    case 'ArrowDown':
      event.preventDefault();
      if (!horizontal) adjust(5);
      break;
    case 'ArrowLeft':
      event.preventDefault();
      if (horizontal) adjust(-5);
      break;
    case 'ArrowRight':
      event.preventDefault();
      if (horizontal) adjust(5);
      break;
    case 'ArrowUp':
      event.preventDefault();
      if (!horizontal) adjust(-5);
      break;
    case 'End':
      // Gives the primary panel its largest allowed size; this may collapse the secondary panel
      event.preventDefault();
      adjust(100);
      break;
    case 'Home':
      // Gives the primary panel its smallest allowed size; this may collapse it
      event.preventDefault();
      adjust(-100);
      break;
    case 'Enter': {
      // Collapses the primary panel, or restores it to its size before it collapsed
      event.preventDefault();
      const [index] = pair;
      const constraints = group.state.constraints[index];
      if (constraints.collapsible) {
        const prevSize = group.state.layout[index];
        const nextSize = constraints.collapsedSize === prevSize
          ? group.expanded[group.panels[index].id] ?? constraints.minSize
          : constraints.collapsedSize;
        adjust(nextSize - prevSize);
      }
      break;
    }
    case 'F6': {
      // Cycles focus through the group's separators
      event.preventDefault();
      const enabled = group.separators.filter((current) => !current.hasAttribute('aria-disabled'));
      const index = enabled.indexOf(element);
      const next = event.shiftKey
        ? (index > 0 ? index - 1 : enabled.length - 1)
        : (index + 1 < enabled.length ? index + 1 : 0);
      enabled[next].focus({ preventScroll: true });
      break;
    }
  }
}

// ---- Groups ----

function mount(group) {
  const children = [...group.element.children];
  group.panels = children.filter((child) => child.hasAttribute('data-panel')).map((element) => ({ element, id: element.id }));
  group.separators = children.filter((child) => child.hasAttribute('data-separator'));

  // Each separator resizes the panels on either side of it
  group.separatorPanels = new Map();
  let panelIndex = -1;
  let pending = [];
  for (const child of children) {
    if (child.hasAttribute('data-panel')) {
      panelIndex++;
      if (panelIndex > 0) pending.forEach((separator) => group.separatorPanels.set(separator, [panelIndex - 1, panelIndex]));
      pending = [];
    } else if (child.hasAttribute('data-separator')) {
      pending.push(child);
    }
  }

  for (const separator of group.separators) {
    separator.addEventListener('keydown', onSeparatorKeyDown);
    separator.addEventListener('focus', refreshSeparators);
    separator.addEventListener('blur', refreshSeparators);
  }

  const axisSize = axisSizeOf(group);
  const constraints = constraintsOf(group, axisSize);
  const requested = defaultLayoutOf(group, constraints);
  commit(group, {
    deferred: axisSize === 0,
    constraints,
    axisSize,
    layout: validateLayout(requested, constraints),
    requestedLayout: normalizeLayout(requested),
  });
  refreshSeparators();
}

function unmount(group) {
  for (const separator of group.separators ?? []) {
    separator.removeEventListener('keydown', onSeparatorKeyDown);
    separator.removeEventListener('focus', refreshSeparators);
    separator.removeEventListener('blur', refreshSeparators);
  }
}

// The cached layout for these panels, else the default layout (if it names every panel), else one from the panels' default sizes
function defaultLayoutOf(group, constraints) {
  const ids = group.panels.map(({ id }) => id);
  const cached = group.cache[ids.join(',')];
  if (cached) return cached;
  const defaultLayout = group.options.defaultLayout;
  if (defaultLayout && Object.keys(defaultLayout).length === ids.length && ids.every((id) => defaultLayout[id] !== undefined)) {
    return ids.map((id) => defaultLayout[id]);
  }
  return calculateDefaultLayout(constraints);
}

// The group was resized: re-derive pixel based constraints and revalidate the requested layout against them
function sync(group) {
  const axisSize = axisSizeOf(group);
  if (axisSize === 0) return;
  const constraints = constraintsOf(group, axisSize);
  const requestedLayout = group.state.deferred ? normalizeLayout(defaultLayoutOf(group, constraints)) : group.state.requestedLayout;
  const layout = validateLayout(requestedLayout, constraints);
  const changed = group.state.deferred || group.state.axisSize !== axisSize || !layoutsEqual(group.state.layout, layout) ||
    JSON.stringify(group.state.constraints) !== JSON.stringify(constraints);
  if (changed) commit(group, { deferred: false, constraints, axisSize, layout, requestedLayout });
}

export function createGroup(element, dotnet, options) {
  const group = { element, dotnet, options, cache: {}, expanded: {}, notified: {} };
  groups.add(group);
  if (!documentListeners) toggleDocumentListeners(true);
  mount(group);

  const remount = () => {
    unmount(group);
    mount(group);
  };
  const resizeObserver = new ResizeObserver(() => sync(group));
  resizeObserver.observe(element);

  // Panels and separators are Blazor components; re-read them when they're added, removed or reconfigured
  const mutationObserver = new MutationObserver((records) => {
    if (records.some((record) => record.type === 'childList' ? record.target === element : record.target.parentElement === element)) remount();
  });
  mutationObserver.observe(element, {
    childList: true,
    subtree: true,
    attributeFilter: ['id', 'data-default-size', 'data-min-size', 'data-max-size', 'data-collapsed-size', 'data-collapsible', 'data-disabled', 'aria-disabled'],
  });

  return {
    update(next) {
      group.options = next;
      remount();
    },
    /**
     * Sets panel sizes (percentages, by panel id) within the panels' limits; panels left out share what remains in
     * proportion. A way to resize without dragging, which WCAG 2.5.7 asks for: buttons, a menu, a number field.
     * @param {Record<string, number>} sizes
     */
    setLayout(sizes) {
      const state = group.state;
      if (!state || state.deferred || state.layout.length !== group.panels.length) return;
      const given = group.panels.map(({ id }) => (typeof sizes[id] === 'number' ? sizes[id] : undefined));
      const fixed = given.reduce((total, size) => total + (size ?? 0), 0);
      const rest = state.layout.reduce((total, size, index) => total + (given[index] === undefined ? size : 0), 0);
      const others = given.filter((size) => size === undefined).length;
      const requested = state.layout.map((size, index) =>
        given[index] ?? (rest > 0 ? (size / rest) * (100 - fixed) : (100 - fixed) / Math.max(others, 1)));
      commit(group, withLayout(group, validateLayout(requested, state.constraints)));
    },
    dispose() {
      mutationObserver.disconnect();
      resizeObserver.disconnect();
      unmount(group);
      groups.delete(group);
      if (interaction.state !== 'inactive' && interaction.hitRegions.some((region) => region.group === group)) {
        setInteraction({ state: 'inactive', cursorFlags: 0 });
      }
      if (groups.size === 0) toggleDocumentListeners(false);
    },
  };
}
