import { reducedMotion } from '../../lib/motion'
import { CHART_COLORS } from './useChart'

/**
 * Draw-in for a chart canvas: an overlay in the chart background colour covers the plot area
 * (the right price axis stays visible) and shrinks towards the right, so the series appears
 * to draw from left to right. Transform-only, 240 ms, removed when done.
 */
export function revealChart(container: HTMLElement, rightInset = 0) {
  if (reducedMotion() || typeof container.animate !== 'function') return
  const cover = document.createElement('div')
  Object.assign(cover.style, {
    position: 'absolute',
    top: '0',
    left: '0',
    bottom: '0',
    right: `${rightInset}px`,
    background: `linear-gradient(${CHART_COLORS.bgTop}, ${CHART_COLORS.bgBottom})`,
    transformOrigin: 'right center',
    pointerEvents: 'none',
    zIndex: '3',
    willChange: 'transform',
  } satisfies Partial<CSSStyleDeclaration>)
  if (getComputedStyle(container).position === 'static') container.style.position = 'relative'
  container.appendChild(cover)
  const anim = cover.animate([{ transform: 'scaleX(1)' }, { transform: 'scaleX(0)' }], {
    duration: 240,
    easing: 'cubic-bezier(0.65, 0, 0.35, 1)',
    fill: 'forwards',
  })
  const done = () => cover.remove()
  anim.onfinish = done
  anim.oncancel = done
}
