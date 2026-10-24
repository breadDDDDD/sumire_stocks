import { useEffect, useRef, useState } from 'react'
import { reducedMotion } from '../../lib/motion'

/**
 * Ease a continuous aggregate (index level, percentage, count) towards its new value.
 * Never use it for stock prices: those sit on tick bands and must snap.
 */
export function useTweened(target: number | null | undefined, ms = 200): number | null | undefined {
  const [shown, setShown] = useState(target)
  const from = useRef(target)
  useEffect(() => {
    const a = from.current
    if (target == null || a == null || a === target || reducedMotion() || document.hidden) {
      from.current = target
      setShown(target)
      return
    }
    const t0 = performance.now()
    let raf = 0
    const step = (t: number) => {
      const k = Math.min(1, (t - t0) / ms)
      const e = 1 - Math.pow(1 - k, 3)
      const v = a + (target - a) * e
      // re-targets from the value on screen if a new target arrives mid-tween
      from.current = v
      setShown(k < 1 ? v : target)
      if (k < 1) raf = requestAnimationFrame(step)
    }
    raf = requestAnimationFrame(step)
    return () => cancelAnimationFrame(raf)
  }, [target, ms])
  return shown
}
