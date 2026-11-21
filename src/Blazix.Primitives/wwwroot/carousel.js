// The carousel engine behind src/Blazix.Registry/Components/Carousel.razor.
//
// A port of Embla Carousel v8.6.0 and its Autoplay plugin, so
// dragging, flicking, snapping and looping move the same way.
// Embla Carousel: MIT License, Copyright (c) 2019 David Jerleke, https://github.com/davidjerleke/embla-carousel
// The Blazor bridge (create) is at the end of the file.

// utils

const mathAbs = (n) => Math.abs(n)
const mathSign = (n) => Math.sign(n)
const isNumber = (subject) => typeof subject === 'number'
const isString = (subject) => typeof subject === 'string'
const isBoolean = (subject) => typeof subject === 'boolean'
const isObject = (subject) => Object.prototype.toString.call(subject) === '[object Object]'
const deltaAbs = (valueB, valueA) => mathAbs(valueB - valueA)
const roundToTwoDecimals = (num) => Math.round(num * 100) / 100
const objectKeys = (object) => Object.keys(object)
const arrayKeys = (array) => objectKeys(array).map(Number)
const arrayLastIndex = (array) => Math.max(0, array.length - 1)
const arrayLast = (array) => array[arrayLastIndex(array)]
const arrayIsLastIndex = (array, index) => index === arrayLastIndex(array)
const arrayFromNumber = (n, startAt = 0) => Array.from(Array(n), (_, i) => startAt + i)

function factorAbs(valueB, valueA) {
  if (valueB === 0 || valueA === 0) return 0
  if (mathAbs(valueB) <= mathAbs(valueA)) return 0
  const diff = deltaAbs(mathAbs(valueB), mathAbs(valueA))
  return mathAbs(diff / valueB)
}

function objectsMergeDeep(objectA, objectB) {
  return [objectA, objectB].reduce((mergedObjects, currentObject) => {
    objectKeys(currentObject).forEach((key) => {
      const valueA = mergedObjects[key]
      const valueB = currentObject[key]
      const areObjects = isObject(valueA) && isObject(valueB)
      mergedObjects[key] = areObjects ? objectsMergeDeep(valueA, valueB) : valueB
    })
    return mergedObjects
  }, {})
}

function isMouseEvent(evt, ownerWindow) {
  return typeof ownerWindow.MouseEvent !== 'undefined' && evt instanceof ownerWindow.MouseEvent
}

// Primitives

function Vector1D(initialValue) {
  let value = initialValue
  const normalizeInput = (n) => (isNumber(n) ? n : n.get())
  return {
    get: () => value,
    set: (n) => { value = normalizeInput(n) },
    add: (n) => { value += normalizeInput(n) },
    subtract: (n) => { value -= normalizeInput(n) }
  }
}

function Limit(min = 0, max = 0) {
  const length = mathAbs(min - max)
  const reachedMin = (n) => n < min
  const reachedMax = (n) => n > max
  const reachedAny = (n) => reachedMin(n) || reachedMax(n)
  function constrain(n) {
    if (!reachedAny(n)) return n
    return reachedMin(n) ? min : max
  }
  function removeOffset(n) {
    if (!length) return n
    return n - length * Math.ceil((n - max) / length)
  }
  return { length, max, min, constrain, reachedAny, reachedMax, reachedMin, removeOffset }
}

function Counter(max, start, loop) {
  const { constrain } = Limit(0, max)
  const loopEnd = max + 1
  let counter = withinLimit(start)
  function withinLimit(n) {
    return !loop ? constrain(n) : mathAbs((loopEnd + n) % loopEnd)
  }
  const self = {
    get: () => counter,
    set: (n) => {
      counter = withinLimit(n)
      return self
    },
    add: (n) => self.clone().set(self.get() + n),
    clone: () => Counter(max, self.get(), loop)
  }
  return self
}

function Axis(axis, contentDirection) {
  const isRightToLeft = contentDirection === 'rtl'
  const isVertical = axis === 'y'
  const sign = !isVertical && isRightToLeft ? -1 : 1
  let startEdge = 'top'
  let endEdge = 'bottom'
  if (!isVertical) {
    startEdge = isRightToLeft ? 'right' : 'left'
    endEdge = isRightToLeft ? 'left' : 'right'
  }
  return {
    scroll: isVertical ? 'y' : 'x',
    cross: isVertical ? 'x' : 'y',
    startEdge,
    endEdge,
    measureSize: ({ height, width }) => (isVertical ? height : width),
    direction: (n) => n * sign
  }
}

function Alignment(align, viewSize) {
  const end = (n) => viewSize - n
  const predefined = { start: () => 0, center: (n) => end(n) / 2, end }
  function measure(n, index) {
    if (isString(align)) return predefined[align](n)
    return align(viewSize, n, index)
  }
  return { measure }
}

function NodeRects() {
  function measure(node) {
    const { offsetTop, offsetLeft, offsetWidth, offsetHeight } = node
    return {
      top: offsetTop,
      right: offsetLeft + offsetWidth,
      bottom: offsetTop + offsetHeight,
      left: offsetLeft,
      width: offsetWidth,
      height: offsetHeight
    }
  }
  return { measure }
}

function PercentOfView(viewSize) {
  return { measure: (n) => viewSize * (n / 100) }
}

function EventStore() {
  let listeners = []
  const self = {
    add(node, type, handler, options = { passive: true }) {
      let removeListener
      if ('addEventListener' in node) {
        node.addEventListener(type, handler, options)
        removeListener = () => node.removeEventListener(type, handler, options)
      } else {
        node.addListener(handler)
        removeListener = () => node.removeListener(handler)
      }
      listeners.push(removeListener)
      return self
    },
    clear() {
      listeners = listeners.filter((remove) => remove())
    }
  }
  return self
}

function EventHandler() {
  let listeners = {}
  let api
  const getListeners = (evt) => listeners[evt] || []
  const self = {
    init(emblaApi) {
      api = emblaApi
    },
    emit(evt) {
      getListeners(evt).forEach((e) => e(api, evt))
      return self
    },
    on(evt, cb) {
      listeners[evt] = getListeners(evt).concat([cb])
      return self
    },
    off(evt, cb) {
      listeners[evt] = getListeners(evt).filter((e) => e !== cb)
      return self
    },
    clear() {
      listeners = {}
    }
  }
  return self
}

// Animation: a fixed 60 updates per second, rendered with interpolation on every frame.

function Animations(ownerDocument, ownerWindow, update, render) {
  const documentVisibleHandler = EventStore()
  const fixedTimeStep = 1000 / 60
  let lastTimeStamp = null
  let accumulatedTime = 0
  let animationId = 0

  function init() {
    documentVisibleHandler.add(ownerDocument, 'visibilitychange', () => {
      if (ownerDocument.hidden) reset()
    })
  }
  function destroy() {
    stop()
    documentVisibleHandler.clear()
  }
  function animate(timeStamp) {
    if (!animationId) return
    if (!lastTimeStamp) {
      lastTimeStamp = timeStamp
      update()
      update()
    }
    const timeElapsed = timeStamp - lastTimeStamp
    lastTimeStamp = timeStamp
    accumulatedTime += timeElapsed
    while (accumulatedTime >= fixedTimeStep) {
      update()
      accumulatedTime -= fixedTimeStep
    }
    render(accumulatedTime / fixedTimeStep)
    if (animationId) animationId = ownerWindow.requestAnimationFrame(animate)
  }
  function start() {
    if (animationId) return
    animationId = ownerWindow.requestAnimationFrame(animate)
  }
  function stop() {
    ownerWindow.cancelAnimationFrame(animationId)
    lastTimeStamp = null
    accumulatedTime = 0
    animationId = 0
  }
  function reset() {
    lastTimeStamp = null
    accumulatedTime = 0
  }
  return { init, destroy, start, stop, update, render }
}

// Dragging

function DragTracker(axis, ownerWindow) {
  const logInterval = 170
  let startEvent
  let lastEvent
  const readTime = (evt) => evt.timeStamp
  function readPoint(evt, evtAxis) {
    const property = evtAxis || axis.scroll
    const coord = `client${property === 'x' ? 'X' : 'Y'}`
    return (isMouseEvent(evt, ownerWindow) ? evt : evt.touches[0])[coord]
  }
  function pointerDown(evt) {
    startEvent = evt
    lastEvent = evt
    return readPoint(evt)
  }
  function pointerMove(evt) {
    const diff = readPoint(evt) - readPoint(lastEvent)
    const expired = readTime(evt) - readTime(startEvent) > logInterval
    lastEvent = evt
    if (expired) startEvent = evt
    return diff
  }
  function pointerUp(evt) {
    if (!startEvent || !lastEvent) return 0
    const diffDrag = readPoint(lastEvent) - readPoint(startEvent)
    const diffTime = readTime(evt) - readTime(startEvent)
    const expired = readTime(evt) - readTime(lastEvent) > logInterval
    const force = diffDrag / diffTime
    const isFlick = diffTime && !expired && mathAbs(force) > 0.1
    return isFlick ? force : 0
  }
  return { pointerDown, pointerMove, pointerUp, readPoint }
}

function DragHandler(
  axis, rootNode, ownerDocument, ownerWindow, target, dragTracker, location, animation, scrollTo,
  scrollBody, scrollTarget, index, eventHandler, percentOfView, dragFree, dragThreshold, skipSnaps,
  baseFriction, watchDrag
) {
  const { cross: crossAxis, direction } = axis
  const focusNodes = ['INPUT', 'SELECT', 'TEXTAREA']
  const nonPassiveEvent = { passive: false }
  const initEvents = EventStore()
  const dragEvents = EventStore()
  const goToNextThreshold = Limit(50, 225).constrain(percentOfView.measure(20))
  const snapForceBoost = { mouse: 300, touch: 400 }
  const freeForceBoost = { mouse: 500, touch: 600 }
  const baseSpeed = dragFree ? 43 : 25
  let isMoving = false
  let startScroll = 0
  let startCross = 0
  let pointerIsDown = false
  let preventScroll = false
  let preventClick = false
  let isMouse = false

  function init(emblaApi) {
    if (!watchDrag) return
    function downIfAllowed(evt) {
      if (isBoolean(watchDrag) || watchDrag(emblaApi, evt)) down(evt)
    }
    const node = rootNode
    initEvents
      .add(node, 'dragstart', (evt) => evt.preventDefault(), nonPassiveEvent)
      .add(node, 'touchmove', () => undefined, nonPassiveEvent)
      .add(node, 'touchend', () => undefined)
      .add(node, 'touchstart', downIfAllowed)
      .add(node, 'mousedown', downIfAllowed)
      .add(node, 'touchcancel', up)
      .add(node, 'contextmenu', up)
      .add(node, 'click', click, true)
  }
  function destroy() {
    initEvents.clear()
    dragEvents.clear()
  }
  function addDragEvents() {
    const node = isMouse ? ownerDocument : rootNode
    dragEvents
      .add(node, 'touchmove', move, nonPassiveEvent)
      .add(node, 'touchend', up)
      .add(node, 'mousemove', move, nonPassiveEvent)
      .add(node, 'mouseup', up)
  }
  function isFocusNode(node) {
    return focusNodes.includes(node.nodeName || '')
  }
  function forceBoost() {
    const boost = dragFree ? freeForceBoost : snapForceBoost
    return boost[isMouse ? 'mouse' : 'touch']
  }
  function allowedForce(force, targetChanged) {
    const next = index.add(mathSign(force) * -1)
    const baseForce = scrollTarget.byDistance(force, !dragFree).distance
    if (dragFree || mathAbs(force) < goToNextThreshold) return baseForce
    if (skipSnaps && targetChanged) return baseForce * 0.5
    return scrollTarget.byIndex(next.get(), 0).distance
  }
  function down(evt) {
    const isMouseEvt = isMouseEvent(evt, ownerWindow)
    isMouse = isMouseEvt
    preventClick = dragFree && isMouseEvt && !evt.buttons && isMoving
    isMoving = deltaAbs(target.get(), location.get()) >= 2
    if (isMouseEvt && evt.button !== 0) return
    if (isFocusNode(evt.target)) return
    pointerIsDown = true
    dragTracker.pointerDown(evt)
    scrollBody.useFriction(0).useDuration(0)
    target.set(location)
    addDragEvents()
    startScroll = dragTracker.readPoint(evt)
    startCross = dragTracker.readPoint(evt, crossAxis)
    eventHandler.emit('pointerDown')
  }
  function move(evt) {
    const isTouchEvt = !isMouseEvent(evt, ownerWindow)
    if (isTouchEvt && evt.touches.length >= 2) return up(evt)
    const lastScroll = dragTracker.readPoint(evt)
    const lastCross = dragTracker.readPoint(evt, crossAxis)
    const diffScroll = deltaAbs(lastScroll, startScroll)
    const diffCross = deltaAbs(lastCross, startCross)
    if (!preventScroll && !isMouse) {
      if (!evt.cancelable) return up(evt)
      preventScroll = diffScroll > diffCross
      if (!preventScroll) return up(evt)
    }
    const diff = dragTracker.pointerMove(evt)
    if (diffScroll > dragThreshold) preventClick = true
    scrollBody.useFriction(0.3).useDuration(0.75)
    animation.start()
    target.add(direction(diff))
    evt.preventDefault()
  }
  function up(evt) {
    const currentLocation = scrollTarget.byDistance(0, false)
    const targetChanged = currentLocation.index !== index.get()
    const rawForce = dragTracker.pointerUp(evt) * forceBoost()
    const force = allowedForce(direction(rawForce), targetChanged)
    const forceFactor = factorAbs(rawForce, force)
    const speed = baseSpeed - 10 * forceFactor
    const friction = baseFriction + forceFactor / 50
    preventScroll = false
    pointerIsDown = false
    dragEvents.clear()
    scrollBody.useDuration(speed).useFriction(friction)
    scrollTo.distance(force, !dragFree)
    isMouse = false
    eventHandler.emit('pointerUp')
  }
  function click(evt) {
    if (preventClick) {
      evt.stopPropagation()
      evt.preventDefault()
      preventClick = false
    }
  }
  return { init, destroy, pointerDown: () => pointerIsDown }
}

// Scrolling

function ScrollBody(location, offsetLocation, previousLocation, target, baseDuration, baseFriction) {
  let scrollVelocity = 0
  let scrollDirection = 0
  let scrollDuration = baseDuration
  let scrollFriction = baseFriction
  let rawLocation = location.get()
  let rawLocationPrevious = 0

  const self = {
    seek() {
      const displacement = target.get() - location.get()
      const isInstant = !scrollDuration
      let scrollDistance = 0
      if (isInstant) {
        scrollVelocity = 0
        previousLocation.set(target)
        location.set(target)
        scrollDistance = displacement
      } else {
        previousLocation.set(location)
        scrollVelocity += displacement / scrollDuration
        scrollVelocity *= scrollFriction
        rawLocation += scrollVelocity
        location.add(scrollVelocity)
        scrollDistance = rawLocation - rawLocationPrevious
      }
      scrollDirection = mathSign(scrollDistance)
      rawLocationPrevious = rawLocation
      return self
    },
    settled: () => mathAbs(target.get() - offsetLocation.get()) < 0.001,
    duration: () => scrollDuration,
    direction: () => scrollDirection,
    velocity: () => scrollVelocity,
    useBaseDuration: () => self.useDuration(baseDuration),
    useBaseFriction: () => self.useFriction(baseFriction),
    useDuration(n) {
      scrollDuration = n
      return self
    },
    useFriction(n) {
      scrollFriction = n
      return self
    }
  }
  return self
}

function ScrollBounds(limit, location, target, scrollBody, percentOfView) {
  const pullBackThreshold = percentOfView.measure(10)
  const edgeOffsetTolerance = percentOfView.measure(50)
  const frictionLimit = Limit(0.1, 0.99)
  let disabled = false

  function shouldConstrain() {
    if (disabled) return false
    if (!limit.reachedAny(target.get())) return false
    if (!limit.reachedAny(location.get())) return false
    return true
  }
  function constrain(pointerDown) {
    if (!shouldConstrain()) return
    const edge = limit.reachedMin(location.get()) ? 'min' : 'max'
    const diffToEdge = mathAbs(limit[edge] - location.get())
    const diffToTarget = target.get() - location.get()
    const friction = frictionLimit.constrain(diffToEdge / edgeOffsetTolerance)
    target.subtract(diffToTarget * friction)
    if (!pointerDown && mathAbs(diffToTarget) < pullBackThreshold) {
      target.set(limit.constrain(target.get()))
      scrollBody.useDuration(25).useBaseFriction()
    }
  }
  return { shouldConstrain, constrain, toggleActive: (active) => { disabled = !active } }
}

function ScrollContain(viewSize, contentSize, snapsAligned, containScroll, pixelTolerance) {
  const scrollBounds = Limit(-contentSize + viewSize, 0)
  const snapsBounded = measureBounded()
  const scrollContainLimit = findScrollContainLimit()
  const snapsContained = measureContained()

  function usePixelTolerance(bound, snap) {
    return deltaAbs(bound, snap) <= 1
  }
  function findScrollContainLimit() {
    const startSnap = snapsBounded[0]
    const endSnap = arrayLast(snapsBounded)
    const min = snapsBounded.lastIndexOf(startSnap)
    const max = snapsBounded.indexOf(endSnap) + 1
    return Limit(min, max)
  }
  function measureBounded() {
    return snapsAligned
      .map((snapAligned, index) => {
        const { min, max } = scrollBounds
        const snap = scrollBounds.constrain(snapAligned)
        const isFirst = !index
        const isLast = arrayIsLastIndex(snapsAligned, index)
        if (isFirst) return max
        if (isLast) return min
        if (usePixelTolerance(min, snap)) return min
        if (usePixelTolerance(max, snap)) return max
        return snap
      })
      .map((scrollBound) => parseFloat(scrollBound.toFixed(3)))
  }
  function measureContained() {
    if (contentSize <= viewSize + pixelTolerance) return [scrollBounds.max]
    if (containScroll === 'keepSnaps') return snapsBounded
    const { min, max } = scrollContainLimit
    return snapsBounded.slice(min, max)
  }
  return { snapsContained, scrollContainLimit }
}

function ScrollLimit(contentSize, scrollSnaps, loop) {
  const max = scrollSnaps[0]
  const min = loop ? max - contentSize : arrayLast(scrollSnaps)
  return { limit: Limit(min, max) }
}

function ScrollLooper(contentSize, limit, location, vectors) {
  const jointSafety = 0.1
  const { reachedMin, reachedMax } = Limit(limit.min + jointSafety, limit.max + jointSafety)
  function shouldLoop(direction) {
    if (direction === 1) return reachedMax(location.get())
    if (direction === -1) return reachedMin(location.get())
    return false
  }
  function loop(direction) {
    if (!shouldLoop(direction)) return
    const loopDistance = contentSize * (direction * -1)
    vectors.forEach((v) => v.add(loopDistance))
  }
  return { loop }
}

function ScrollProgress(limit) {
  const { max, length } = limit
  return { get: (n) => (length ? (n - max) / -length : 0) }
}

function ScrollSnaps(axis, alignment, containerRect, slideRects, slidesToScroll) {
  const { startEdge, endEdge } = axis
  const { groupSlides } = slidesToScroll
  const alignments = groupSlides(slideRects)
    .map((rects) => arrayLast(rects)[endEdge] - rects[0][startEdge])
    .map(mathAbs)
    .map(alignment.measure)
  const snaps = slideRects
    .map((rect) => containerRect[startEdge] - rect[startEdge])
    .map((snap) => -mathAbs(snap))
  const snapsAligned = groupSlides(snaps)
    .map((g) => g[0])
    .map((snap, index) => snap + alignments[index])
  return { snaps, snapsAligned }
}

function ScrollTarget(loop, scrollSnaps, contentSize, limit, targetVector) {
  const { reachedAny, removeOffset, constrain } = limit

  function minDistance(distances) {
    return distances.concat().sort((a, b) => mathAbs(a) - mathAbs(b))[0]
  }
  function findTargetSnap(target) {
    const distance = loop ? removeOffset(target) : constrain(target)
    const ascDiffsToSnaps = scrollSnaps
      .map((snap, index) => ({ diff: shortcut(snap - distance, 0), index }))
      .sort((d1, d2) => mathAbs(d1.diff) - mathAbs(d2.diff))
    const { index } = ascDiffsToSnaps[0]
    return { index, distance }
  }
  function shortcut(target, direction) {
    const targets = [target, target + contentSize, target - contentSize]
    if (!loop) return target
    if (!direction) return minDistance(targets)
    const matchingTargets = targets.filter((t) => mathSign(t) === direction)
    if (matchingTargets.length) return minDistance(matchingTargets)
    return arrayLast(targets) - contentSize
  }
  function byIndex(index, direction) {
    const diffToSnap = scrollSnaps[index] - targetVector.get()
    return { index, distance: shortcut(diffToSnap, direction) }
  }
  function byDistance(distance, snap) {
    const target = targetVector.get() + distance
    const { index, distance: targetSnapDistance } = findTargetSnap(target)
    const reachedBound = !loop && reachedAny(target)
    if (!snap || reachedBound) return { index, distance }
    const diffToSnap = scrollSnaps[index] - targetSnapDistance
    return { index, distance: distance + shortcut(diffToSnap, 0) }
  }
  return { byDistance, byIndex, shortcut }
}

function ScrollTo(animation, indexCurrent, indexPrevious, scrollBody, scrollTarget, targetVector, eventHandler) {
  function scrollTo(target) {
    const distanceDiff = target.distance
    const indexDiff = target.index !== indexCurrent.get()
    targetVector.add(distanceDiff)
    if (distanceDiff) {
      if (scrollBody.duration()) {
        animation.start()
      } else {
        animation.update()
        animation.render(1)
        animation.update()
      }
    }
    if (indexDiff) {
      indexPrevious.set(indexCurrent.get())
      indexCurrent.set(target.index)
      eventHandler.emit('select')
    }
  }
  return {
    distance: (n, snap) => scrollTo(scrollTarget.byDistance(n, snap)),
    index(n, direction) {
      const targetIndex = indexCurrent.clone().set(n)
      scrollTo(scrollTarget.byIndex(targetIndex.get(), direction))
    }
  }
}

// Slides

function SlideSizes(axis, containerRect, slideRects, slides, readEdgeGap, ownerWindow) {
  const { measureSize, startEdge, endEdge } = axis
  const withEdgeGap = slideRects[0] && readEdgeGap
  const startGap = withEdgeGap ? mathAbs(containerRect[startEdge] - slideRects[0][startEdge]) : 0
  const endGap = withEdgeGap
    ? parseFloat(ownerWindow.getComputedStyle(arrayLast(slides)).getPropertyValue(`margin-${endEdge}`))
    : 0
  const slideSizes = slideRects.map(measureSize)
  const slideSizesWithGaps = slideRects
    .map((rect, index, rects) => {
      if (!index) return slideSizes[index] + startGap
      if (arrayIsLastIndex(rects, index)) return slideSizes[index] + endGap
      return rects[index + 1][startEdge] - rect[startEdge]
    })
    .map(mathAbs)
  return { slideSizes, slideSizesWithGaps, startGap, endGap }
}

function SlidesToScroll(axis, viewSize, slidesToScroll, loop, containerRect, slideRects, startGap, endGap, pixelTolerance) {
  const { startEdge, endEdge, direction } = axis
  const groupByNumber = isNumber(slidesToScroll)

  function byNumber(array, groupSize) {
    return arrayKeys(array)
      .filter((i) => i % groupSize === 0)
      .map((i) => array.slice(i, i + groupSize))
  }
  function bySize(array) {
    if (!array.length) return []
    return arrayKeys(array)
      .reduce((groups, rectB, index) => {
        const rectA = arrayLast(groups) || 0
        const isFirst = rectA === 0
        const isLast = rectB === arrayLastIndex(array)
        const edgeA = containerRect[startEdge] - slideRects[rectA][startEdge]
        const edgeB = containerRect[startEdge] - slideRects[rectB][endEdge]
        const gapA = !loop && isFirst ? direction(startGap) : 0
        const gapB = !loop && isLast ? direction(endGap) : 0
        const chunkSize = mathAbs(edgeB - gapB - (edgeA + gapA))
        if (index && chunkSize > viewSize + pixelTolerance) groups.push(rectB)
        if (isLast) groups.push(array.length)
        return groups
      }, [])
      .map((currentSize, index, groups) => {
        const previousSize = Math.max(groups[index - 1] || 0)
        return array.slice(previousSize, currentSize)
      })
  }
  return { groupSlides: (array) => (groupByNumber ? byNumber(array, slidesToScroll) : bySize(array)) }
}

function SlideRegistry(containSnaps, containScroll, scrollSnaps, scrollContainLimit, slidesToScroll, slideIndexes) {
  const { groupSlides } = slidesToScroll
  const { min, max } = scrollContainLimit

  function createSlideRegistry() {
    const groupedSlideIndexes = groupSlides(slideIndexes)
    const doNotContain = !containSnaps || containScroll === 'keepSnaps'
    if (scrollSnaps.length === 1) return [slideIndexes]
    if (doNotContain) return groupedSlideIndexes
    return groupedSlideIndexes.slice(min, max).map((group, index, groups) => {
      if (!index) return arrayFromNumber(arrayLast(groups[0]) + 1)
      if (arrayIsLastIndex(groups, index)) {
        const range = arrayLastIndex(slideIndexes) - arrayLast(groups)[0] + 1
        return arrayFromNumber(range, arrayLast(groups)[0])
      }
      return group
    })
  }
  return { slideRegistry: createSlideRegistry() }
}

function SlideFocus(root, slides, slideRegistry, scrollTo, scrollBody, eventStore, eventHandler, watchFocus) {
  const focusListenerOptions = { passive: true, capture: true }
  let lastTabPressTime = 0

  function init(emblaApi) {
    if (!watchFocus) return
    function defaultCallback(index) {
      const diffTime = new Date().getTime() - lastTabPressTime
      if (diffTime > 10) return
      eventHandler.emit('slideFocusStart')
      root.scrollLeft = 0
      const group = slideRegistry.findIndex((g) => g.includes(index))
      if (!isNumber(group)) return
      scrollBody.useDuration(0)
      scrollTo.index(group, 0)
      eventHandler.emit('slideFocus')
    }
    eventStore.add(document, 'keydown', registerTabPress, false)
    slides.forEach((slide, slideIndex) => {
      eventStore.add(
        slide,
        'focus',
        (evt) => {
          if (isBoolean(watchFocus) || watchFocus(emblaApi, evt)) defaultCallback(slideIndex)
        },
        focusListenerOptions
      )
    })
  }
  function registerTabPress(event) {
    if (event.code === 'Tab') lastTabPressTime = new Date().getTime()
  }
  return { init }
}

function Translate(axis, container) {
  const translate = axis.scroll === 'x'
    ? (n) => `translate3d(${n}px,0px,0px)`
    : (n) => `translate3d(0px,${n}px,0px)`
  const containerStyle = container.style
  let previousTarget = null
  let disabled = false

  function to(target) {
    if (disabled) return
    const newTarget = roundToTwoDecimals(axis.direction(target))
    if (newTarget === previousTarget) return
    containerStyle.transform = translate(newTarget)
    previousTarget = newTarget
  }
  function clear() {
    if (disabled) return
    containerStyle.transform = ''
    if (!container.getAttribute('style')) container.removeAttribute('style')
  }
  return { clear, to, toggleActive: (active) => { disabled = !active } }
}

function SlideLooper(axis, viewSize, contentSize, slideSizes, slideSizesWithGaps, snaps, scrollSnaps, location, slides) {
  const roundingSafety = 0.5
  const ascItems = arrayKeys(slideSizesWithGaps)
  const descItems = arrayKeys(slideSizesWithGaps).reverse()
  const loopPoints = startPoints().concat(endPoints())

  function removeSlideSizes(indexes, from) {
    return indexes.reduce((a, i) => a - slideSizesWithGaps[i], from)
  }
  function slidesInGap(indexes, gap) {
    return indexes.reduce((a, i) => {
      const remainingGap = removeSlideSizes(a, gap)
      return remainingGap > 0 ? a.concat([i]) : a
    }, [])
  }
  function findSlideBounds(offset) {
    return snaps.map((snap, index) => ({
      start: snap - slideSizes[index] + roundingSafety + offset,
      end: snap + viewSize - roundingSafety + offset
    }))
  }
  function findLoopPoints(indexes, offset, isEndEdge) {
    const slideBounds = findSlideBounds(offset)
    return indexes.map((index) => {
      const initial = isEndEdge ? 0 : -contentSize
      const altered = isEndEdge ? contentSize : 0
      const loopPoint = slideBounds[index][isEndEdge ? 'end' : 'start']
      return {
        index,
        loopPoint,
        slideLocation: Vector1D(-1),
        translate: Translate(axis, slides[index]),
        target: () => (location.get() > loopPoint ? initial : altered)
      }
    })
  }
  function startPoints() {
    const indexes = slidesInGap(descItems, scrollSnaps[0])
    return findLoopPoints(indexes, contentSize, false)
  }
  function endPoints() {
    const indexes = slidesInGap(ascItems, viewSize - scrollSnaps[0] - 1)
    return findLoopPoints(indexes, -contentSize, true)
  }
  function canLoop() {
    return loopPoints.every(({ index }) => {
      const otherIndexes = ascItems.filter((i) => i !== index)
      return removeSlideSizes(otherIndexes, viewSize) <= 0.1
    })
  }
  function loop() {
    loopPoints.forEach(({ target, translate, slideLocation }) => {
      const shiftLocation = target()
      if (shiftLocation === slideLocation.get()) return
      translate.to(shiftLocation)
      slideLocation.set(shiftLocation)
    })
  }
  function clear() {
    loopPoints.forEach((loopPoint) => loopPoint.translate.clear())
  }
  return { canLoop, clear, loop, loopPoints }
}

function SlidesInView(container, slides, eventHandler, threshold) {
  const intersectionEntryMap = {}
  let inViewCache = null
  let notInViewCache = null
  let intersectionObserver
  let destroyed = false

  function init() {
    intersectionObserver = new IntersectionObserver(
      (entries) => {
        if (destroyed) return
        entries.forEach((entry) => {
          intersectionEntryMap[slides.indexOf(entry.target)] = entry
        })
        inViewCache = null
        notInViewCache = null
        eventHandler.emit('slidesInView')
      },
      { root: container.parentElement, threshold }
    )
    slides.forEach((slide) => intersectionObserver.observe(slide))
  }
  function destroy() {
    if (intersectionObserver) intersectionObserver.disconnect()
    destroyed = true
  }
  function createInViewList(inView) {
    return objectKeys(intersectionEntryMap).reduce((list, slideIndex) => {
      const index = parseInt(slideIndex)
      const { isIntersecting } = intersectionEntryMap[index]
      if ((inView && isIntersecting) || (!inView && !isIntersecting)) list.push(index)
      return list
    }, [])
  }
  function get(inView = true) {
    if (inView && inViewCache) return inViewCache
    if (!inView && notInViewCache) return notInViewCache
    const slideIndexes = createInViewList(inView)
    if (inView) inViewCache = slideIndexes
    if (!inView) notInViewCache = slideIndexes
    return slideIndexes
  }
  return { init, destroy, get }
}

function SlidesHandler(container, eventHandler, watchSlides) {
  let mutationObserver
  let destroyed = false

  function init(emblaApi) {
    if (!watchSlides) return
    function defaultCallback(mutations) {
      for (const mutation of mutations) {
        if (mutation.type === 'childList') {
          emblaApi.reInit()
          eventHandler.emit('slidesChanged')
          break
        }
      }
    }
    mutationObserver = new MutationObserver((mutations) => {
      if (destroyed) return
      if (isBoolean(watchSlides) || watchSlides(emblaApi, mutations)) defaultCallback(mutations)
    })
    mutationObserver.observe(container, { childList: true })
  }
  function destroy() {
    if (mutationObserver) mutationObserver.disconnect()
    destroyed = true
  }
  return { init, destroy }
}

function ResizeHandler(container, eventHandler, ownerWindow, slides, axis, watchResize, nodeRects) {
  const observeNodes = [container].concat(slides)
  let resizeObserver
  let containerSize
  let slideSizes = []
  let destroyed = false

  const readSize = (node) => axis.measureSize(nodeRects.measure(node))

  function init(emblaApi) {
    if (!watchResize) return
    containerSize = readSize(container)
    slideSizes = slides.map(readSize)
    function defaultCallback(entries) {
      for (const entry of entries) {
        if (destroyed) return
        const isContainer = entry.target === container
        const slideIndex = slides.indexOf(entry.target)
        const lastSize = isContainer ? containerSize : slideSizes[slideIndex]
        const newSize = readSize(isContainer ? container : slides[slideIndex])
        if (mathAbs(newSize - lastSize) >= 0.5) {
          emblaApi.reInit()
          eventHandler.emit('resize')
          break
        }
      }
    }
    resizeObserver = new ResizeObserver((entries) => {
      if (isBoolean(watchResize) || watchResize(emblaApi, entries)) defaultCallback(entries)
    })
    ownerWindow.requestAnimationFrame(() => {
      observeNodes.forEach((node) => resizeObserver.observe(node))
    })
  }
  function destroy() {
    destroyed = true
    if (resizeObserver) resizeObserver.disconnect()
  }
  return { init, destroy }
}

// Options and plugins

const defaultOptions = {
  align: 'center',
  axis: 'x',
  container: null,
  slides: null,
  containScroll: 'trimSnaps',
  direction: 'ltr',
  slidesToScroll: 1,
  inViewThreshold: 0,
  breakpoints: {},
  dragFree: false,
  dragThreshold: 10,
  loop: false,
  skipSnaps: false,
  duration: 25,
  startIndex: 0,
  active: true,
  watchDrag: true,
  watchResize: true,
  watchSlides: true,
  watchFocus: true
}

function OptionsHandler(ownerWindow) {
  function mergeOptions(optionsA, optionsB) {
    return objectsMergeDeep(optionsA, optionsB || {})
  }
  function optionsAtMedia(options) {
    const breakpoints = options.breakpoints || {}
    const matchedMediaOptions = objectKeys(breakpoints)
      .filter((media) => ownerWindow.matchMedia(media).matches)
      .map((media) => breakpoints[media])
      .reduce((a, mediaOption) => mergeOptions(a, mediaOption), {})
    return mergeOptions(options, matchedMediaOptions)
  }
  function optionsMediaQueries(optionsList) {
    return optionsList
      .map((options) => objectKeys(options.breakpoints || {}))
      .reduce((acc, mediaQueries) => acc.concat(mediaQueries), [])
      .map((query) => ownerWindow.matchMedia(query))
  }
  return { mergeOptions, optionsAtMedia, optionsMediaQueries }
}

function PluginsHandler(optionsHandler) {
  let activePlugins = []
  function init(emblaApi, plugins) {
    activePlugins = plugins.filter(({ options }) => optionsHandler.optionsAtMedia(options).active !== false)
    activePlugins.forEach((plugin) => plugin.init(emblaApi, optionsHandler))
    return plugins.reduce((map, plugin) => Object.assign(map, { [plugin.name]: plugin }), {})
  }
  function destroy() {
    activePlugins = activePlugins.filter((plugin) => plugin.destroy())
  }
  return { init, destroy }
}

// Engine

function Engine(root, container, slides, ownerDocument, ownerWindow, options, eventHandler) {
  const {
    align, axis: scrollAxis, direction, startIndex, loop, duration, dragFree, dragThreshold,
    inViewThreshold, slidesToScroll: groupSlides, skipSnaps, containScroll, watchResize, watchSlides,
    watchDrag, watchFocus
  } = options

  // Measurements
  const pixelTolerance = 2
  const nodeRects = NodeRects()
  const containerRect = nodeRects.measure(container)
  const slideRects = slides.map(nodeRects.measure)
  const axis = Axis(scrollAxis, direction)
  const viewSize = axis.measureSize(containerRect)
  const percentOfView = PercentOfView(viewSize)
  const alignment = Alignment(align, viewSize)
  const containSnaps = !loop && !!containScroll
  const readEdgeGap = loop || !!containScroll
  const { slideSizes, slideSizesWithGaps, startGap, endGap } = SlideSizes(
    axis, containerRect, slideRects, slides, readEdgeGap, ownerWindow)
  const slidesToScroll = SlidesToScroll(
    axis, viewSize, groupSlides, loop, containerRect, slideRects, startGap, endGap, pixelTolerance)
  const { snaps, snapsAligned } = ScrollSnaps(axis, alignment, containerRect, slideRects, slidesToScroll)
  const contentSize = -arrayLast(snaps) + arrayLast(slideSizesWithGaps)
  const { snapsContained, scrollContainLimit } = ScrollContain(
    viewSize, contentSize, snapsAligned, containScroll, pixelTolerance)
  const scrollSnaps = containSnaps ? snapsContained : snapsAligned
  const { limit } = ScrollLimit(contentSize, scrollSnaps, loop)

  // Indexes
  const index = Counter(arrayLastIndex(scrollSnaps), startIndex, loop)
  const indexPrevious = index.clone()
  const slideIndexes = arrayKeys(slides)

  // Animation
  const update = ({ dragHandler, scrollBody, scrollBounds, options: { loop } }) => {
    if (!loop) scrollBounds.constrain(dragHandler.pointerDown())
    scrollBody.seek()
  }

  const render = (engine, alpha) => {
    const {
      scrollBody, translate, location, offsetLocation, previousLocation, scrollLooper, slideLooper,
      dragHandler, animation, eventHandler, scrollBounds, options: { loop }
    } = engine
    const shouldSettle = scrollBody.settled()
    const withinBounds = !scrollBounds.shouldConstrain()
    const hasSettled = loop ? shouldSettle : shouldSettle && withinBounds
    const hasSettledAndIdle = hasSettled && !dragHandler.pointerDown()

    if (hasSettledAndIdle) animation.stop()

    offsetLocation.set(location.get() * alpha + previousLocation.get() * (1 - alpha))

    if (loop) {
      scrollLooper.loop(scrollBody.direction())
      slideLooper.loop()
    }

    translate.to(offsetLocation.get())

    if (hasSettledAndIdle) eventHandler.emit('settle')
    if (!hasSettled) eventHandler.emit('scroll')
  }

  const animation = Animations(ownerDocument, ownerWindow, () => update(engine), (alpha) => render(engine, alpha))

  // Shared
  const friction = 0.68
  const startLocation = scrollSnaps[index.get()]
  const location = Vector1D(startLocation)
  const previousLocation = Vector1D(startLocation)
  const offsetLocation = Vector1D(startLocation)
  const target = Vector1D(startLocation)
  const scrollBody = ScrollBody(location, offsetLocation, previousLocation, target, duration, friction)
  const scrollTarget = ScrollTarget(loop, scrollSnaps, contentSize, limit, target)
  const scrollTo = ScrollTo(animation, index, indexPrevious, scrollBody, scrollTarget, target, eventHandler)
  const scrollProgress = ScrollProgress(limit)
  const eventStore = EventStore()
  const slidesInView = SlidesInView(container, slides, eventHandler, inViewThreshold)
  const { slideRegistry } = SlideRegistry(
    containSnaps, containScroll, scrollSnaps, scrollContainLimit, slidesToScroll, slideIndexes)
  const slideFocus = SlideFocus(
    root, slides, slideRegistry, scrollTo, scrollBody, eventStore, eventHandler, watchFocus)

  const engine = {
    ownerDocument,
    ownerWindow,
    eventHandler,
    containerRect,
    slideRects,
    animation,
    axis,
    dragHandler: DragHandler(
      axis, root, ownerDocument, ownerWindow, target, DragTracker(axis, ownerWindow), location, animation,
      scrollTo, scrollBody, scrollTarget, index, eventHandler, percentOfView, dragFree, dragThreshold,
      skipSnaps, friction, watchDrag),
    eventStore,
    percentOfView,
    index,
    indexPrevious,
    limit,
    location,
    offsetLocation,
    previousLocation,
    options,
    resizeHandler: ResizeHandler(container, eventHandler, ownerWindow, slides, axis, watchResize, nodeRects),
    scrollBody,
    scrollBounds: ScrollBounds(limit, offsetLocation, target, scrollBody, percentOfView),
    scrollLooper: ScrollLooper(contentSize, limit, offsetLocation, [
      location, offsetLocation, previousLocation, target
    ]),
    scrollProgress,
    scrollSnapList: scrollSnaps.map(scrollProgress.get),
    scrollSnaps,
    scrollTarget,
    scrollTo,
    slideLooper: SlideLooper(
      axis, viewSize, contentSize, slideSizes, slideSizesWithGaps, snaps, scrollSnaps, offsetLocation, slides),
    slideFocus,
    slidesHandler: SlidesHandler(container, eventHandler, watchSlides),
    slidesInView,
    slideIndexes,
    slideRegistry,
    slidesToScroll,
    target,
    translate: Translate(axis, container)
  }

  return engine
}

// EmblaCarousel(root, options, plugins): root is the viewport; its first child is the track.

export function EmblaCarousel(root, userOptions, userPlugins) {
  const ownerDocument = root.ownerDocument
  const ownerWindow = ownerDocument.defaultView
  const optionsHandler = OptionsHandler(ownerWindow)
  const pluginsHandler = PluginsHandler(optionsHandler)
  const mediaHandlers = EventStore()
  const eventHandler = EventHandler()
  const { mergeOptions, optionsAtMedia, optionsMediaQueries } = optionsHandler
  const { on, off, emit } = eventHandler
  let destroyed = false
  let engine
  let optionsBase = mergeOptions(defaultOptions)
  let options = mergeOptions(optionsBase)
  let pluginList = []
  let pluginApis
  let container
  let slides

  function storeElements() {
    const { container: userContainer, slides: userSlides } = options
    const customContainer = isString(userContainer) ? root.querySelector(userContainer) : userContainer
    container = customContainer || root.children[0]
    const customSlides = isString(userSlides) ? container.querySelectorAll(userSlides) : userSlides
    slides = [].slice.call(customSlides || container.children)
  }

  function createEngine(engineOptions) {
    const newEngine = Engine(root, container, slides, ownerDocument, ownerWindow, engineOptions, eventHandler)
    if (engineOptions.loop && !newEngine.slideLooper.canLoop()) {
      return createEngine(Object.assign({}, engineOptions, { loop: false }))
    }
    return newEngine
  }

  function activate(withOptions, withPlugins) {
    if (destroyed) return
    optionsBase = mergeOptions(optionsBase, withOptions)
    options = optionsAtMedia(optionsBase)
    pluginList = withPlugins || pluginList
    storeElements()
    engine = createEngine(options)
    optionsMediaQueries([optionsBase, ...pluginList.map(({ options }) => options)])
      .forEach((query) => mediaHandlers.add(query, 'change', reActivate))
    if (!options.active) return
    engine.translate.to(engine.location.get())
    engine.animation.init()
    engine.slidesInView.init()
    engine.slideFocus.init(self)
    engine.eventHandler.init(self)
    engine.resizeHandler.init(self)
    engine.slidesHandler.init(self)
    if (engine.options.loop) engine.slideLooper.loop()
    if (container.offsetParent && slides.length) engine.dragHandler.init(self)
    pluginApis = pluginsHandler.init(self, pluginList)
  }

  function reActivate(withOptions, withPlugins) {
    const startIndex = selectedScrollSnap()
    deActivate()
    activate(mergeOptions({ startIndex }, withOptions), withPlugins)
    eventHandler.emit('reInit')
  }

  function deActivate() {
    engine.dragHandler.destroy()
    engine.eventStore.clear()
    engine.translate.clear()
    engine.slideLooper.clear()
    engine.resizeHandler.destroy()
    engine.slidesHandler.destroy()
    engine.slidesInView.destroy()
    engine.animation.destroy()
    pluginsHandler.destroy()
    mediaHandlers.clear()
  }

  function destroy() {
    if (destroyed) return
    destroyed = true
    mediaHandlers.clear()
    deActivate()
    eventHandler.emit('destroy')
    eventHandler.clear()
  }

  function scrollTo(index, jump, direction) {
    if (!options.active || destroyed) return
    engine.scrollBody.useBaseFriction().useDuration(jump === true ? 0 : options.duration)
    engine.scrollTo.index(index, direction || 0)
  }

  function selectedScrollSnap() {
    return engine.index.get()
  }

  const self = {
    canScrollNext: () => engine.index.add(1).get() !== selectedScrollSnap(),
    canScrollPrev: () => engine.index.add(-1).get() !== selectedScrollSnap(),
    containerNode: () => container,
    internalEngine: () => engine,
    destroy,
    off,
    on,
    emit,
    plugins: () => pluginApis,
    previousScrollSnap: () => engine.indexPrevious.get(),
    reInit: reActivate,
    rootNode: () => root,
    scrollNext: (jump) => scrollTo(engine.index.add(1).get(), jump, -1),
    scrollPrev: (jump) => scrollTo(engine.index.add(-1).get(), jump, 1),
    scrollProgress: () => engine.scrollProgress.get(engine.offsetLocation.get()),
    scrollSnapList: () => engine.scrollSnapList,
    scrollTo,
    selectedScrollSnap,
    slideNodes: () => slides,
    slidesInView: () => engine.slidesInView.get(),
    slidesNotInView: () => engine.slidesInView.get(false)
  }

  activate(userOptions, userPlugins)
  setTimeout(() => eventHandler.emit('init'), 0)
  return self
}

// Autoplay plugin (embla-carousel-autoplay)

const autoplayDefaultOptions = {
  active: true,
  breakpoints: {},
  delay: 4000,
  jump: false,
  playOnInit: true,
  stopOnFocusIn: true,
  stopOnInteraction: true,
  stopOnMouseEnter: false,
  stopOnLastSnap: false,
  rootNode: null
}

export function Autoplay(userOptions = {}) {
  let options
  let emblaApi
  let destroyed
  let delay
  let timerStartTime = null
  let timerId = 0
  let autoplayActive = false
  let mouseIsOver = false
  let playOnDocumentVisible = false
  let jump = false

  function init(emblaApiInstance, optionsHandler) {
    emblaApi = emblaApiInstance
    const { mergeOptions, optionsAtMedia } = optionsHandler
    options = optionsAtMedia(mergeOptions(mergeOptions(autoplayDefaultOptions), userOptions))
    if (emblaApi.scrollSnapList().length <= 1) return
    jump = options.jump
    destroyed = false
    delay = normalizeDelay(options.delay)
    const { eventStore, ownerDocument } = emblaApi.internalEngine()
    const isDraggable = !!emblaApi.internalEngine().options.watchDrag
    const root = (options.rootNode && options.rootNode(emblaApi.rootNode())) || emblaApi.rootNode()

    eventStore.add(ownerDocument, 'visibilitychange', visibilityChange)
    if (isDraggable) emblaApi.on('pointerDown', pointerDown)
    if (isDraggable && !options.stopOnInteraction) emblaApi.on('pointerUp', pointerUp)
    if (options.stopOnMouseEnter) eventStore.add(root, 'mouseenter', mouseEnter)
    if (options.stopOnMouseEnter && !options.stopOnInteraction) eventStore.add(root, 'mouseleave', mouseLeave)
    if (options.stopOnFocusIn) emblaApi.on('slideFocusStart', stopAutoplay)
    if (options.stopOnFocusIn && !options.stopOnInteraction) {
      eventStore.add(emblaApi.containerNode(), 'focusout', startAutoplay)
    }
    if (options.playOnInit) startAutoplay()
  }

  function normalizeDelay(delayOption) {
    const scrollSnaps = emblaApi.scrollSnapList()
    if (typeof delayOption === 'number') return scrollSnaps.map(() => delayOption)
    return delayOption(scrollSnaps, emblaApi)
  }

  function destroy() {
    emblaApi.off('pointerDown', pointerDown).off('pointerUp', pointerUp).off('slideFocusStart', stopAutoplay)
    stopAutoplay()
    destroyed = true
    autoplayActive = false
  }

  function setTimer() {
    const { ownerWindow } = emblaApi.internalEngine()
    ownerWindow.clearTimeout(timerId)
    timerId = ownerWindow.setTimeout(next, delay[emblaApi.selectedScrollSnap()])
    timerStartTime = new Date().getTime()
    emblaApi.emit('autoplay:timerset')
  }

  function clearTimer() {
    const { ownerWindow } = emblaApi.internalEngine()
    ownerWindow.clearTimeout(timerId)
    timerId = 0
    timerStartTime = null
    emblaApi.emit('autoplay:timerstopped')
  }

  function startAutoplay() {
    if (destroyed) return
    if (documentIsHidden()) {
      playOnDocumentVisible = true
      return
    }
    if (!autoplayActive) emblaApi.emit('autoplay:play')
    setTimer()
    autoplayActive = true
  }

  function stopAutoplay() {
    if (destroyed) return
    if (autoplayActive) emblaApi.emit('autoplay:stop')
    clearTimer()
    autoplayActive = false
  }

  function visibilityChange() {
    if (documentIsHidden()) {
      playOnDocumentVisible = autoplayActive
      return stopAutoplay()
    }
    if (playOnDocumentVisible) startAutoplay()
  }

  function documentIsHidden() {
    return emblaApi.internalEngine().ownerDocument.visibilityState === 'hidden'
  }

  function pointerDown() {
    if (!mouseIsOver) stopAutoplay()
  }

  function pointerUp() {
    if (!mouseIsOver) startAutoplay()
  }

  function mouseEnter() {
    mouseIsOver = true
    stopAutoplay()
  }

  function mouseLeave() {
    mouseIsOver = false
    startAutoplay()
  }

  function next() {
    const { index } = emblaApi.internalEngine()
    const nextIndex = index.clone().add(1).get()
    const lastIndex = emblaApi.scrollSnapList().length - 1
    const kill = options.stopOnLastSnap && nextIndex === lastIndex
    if (emblaApi.canScrollNext()) {
      emblaApi.scrollNext(jump)
    } else {
      emblaApi.scrollTo(0, jump)
    }
    emblaApi.emit('autoplay:select')
    if (kill) return stopAutoplay()
    startAutoplay()
  }

  return {
    name: 'autoplay',
    options: userOptions,
    init,
    destroy,
    play(jumpOverride) {
      if (typeof jumpOverride !== 'undefined' && jumpOverride !== null) jump = jumpOverride
      startAutoplay()
    },
    stop() {
      if (autoplayActive) stopAutoplay()
    },
    reset() {
      if (autoplayActive) startAutoplay()
    },
    isPlaying: () => autoplayActive,
    timeUntilNext() {
      if (!timerStartTime) return null
      const currentDelay = delay[emblaApi.selectedScrollSnap()]
      return currentDelay - (new Date().getTime() - timerStartTime)
    }
  }
}

// Blazor bridge

const pluginFactories = { autoplay: Autoplay }

/** Makes a plugin available to Carousel's Plugins by name: factory(options) returns an Embla plugin. */
export function registerPlugin(name, factory) {
  pluginFactories[name] = factory
}

function createPlugins(descriptors) {
  return descriptors.map(({ name, options }) => {
    const factory = pluginFactories[name]
    if (!factory) throw new Error(`Unknown carousel plugin "${name}". Register it with registerPlugin().`)
    return factory(options || {})
  })
}

function viewportOf(root) {
  return [...root.querySelectorAll('[data-slot="carousel-content"]')]
    .find((viewport) => viewport.parentElement.closest('[data-slot="carousel"]') === root)
}

/**
 * Starts the carousel inside root (the Carousel's region) and returns its controller.
 * dotNet.invokeMethodAsync('OnEvent', name, state) reports select and reInit, and any event passed to listen().
 */
export function create(root, dotNet, options, plugins) {
  const viewport = viewportOf(root)
  if (!viewport) throw new Error('A Carousel needs a CarouselContent.')
  const api = EmblaCarousel(viewport, options, createPlugins(plugins || []))
  const forwarded = new Set()

  const state = () => ({
    selectedScrollSnap: api.selectedScrollSnap(),
    previousScrollSnap: api.previousScrollSnap(),
    scrollSnapList: api.scrollSnapList(),
    canScrollPrev: api.canScrollPrev(),
    canScrollNext: api.canScrollNext(),
    scrollProgress: api.scrollProgress(),
    slidesInView: api.slidesInView(),
    slidesNotInView: api.slidesNotInView()
  })

  const forward = (_, name) => {
    dotNet.invokeMethodAsync('OnEvent', name, state()).catch(() => undefined)
  }

  function listen(name) {
    if (forwarded.has(name)) return
    forwarded.add(name)
    api.on(name, forward)
  }

  // Arrow keys on the region (captured).
  function onKeyDown(event) {
    if (event.key === 'ArrowLeft') {
      event.preventDefault()
      api.scrollPrev()
    } else if (event.key === 'ArrowRight') {
      event.preventDefault()
      api.scrollNext()
    }
  }
  root.addEventListener('keydown', onKeyDown, true)
  listen('select')
  listen('reInit')

  return {
    state,
    listen,
    scrollPrev: (jump) => api.scrollPrev(jump),
    scrollNext: (jump) => api.scrollNext(jump),
    scrollTo: (index, jump) => api.scrollTo(index, jump),
    reInit: (newOptions, newPlugins) => api.reInit(newOptions, newPlugins ? createPlugins(newPlugins) : undefined),
    plugin(name, method, args) {
      const plugin = api.plugins()?.[name]
      if (!plugin) throw new Error(`The carousel has no "${name}" plugin.`)
      return plugin[method](...(args || []))
    },
    dispose() {
      root.removeEventListener('keydown', onKeyDown, true)
      api.destroy()
    }
  }
}
