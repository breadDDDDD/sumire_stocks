import { useEffect, useRef, useState } from 'react'
import { direction, formatNum, type NumFormat } from '../../lib/format'
import s from './Num.module.css'

export interface NumProps {
  value: number | null | undefined
  format?: NumFormat
  decimals?: number
  /** Show a leading + for positive values. */
  signed?: boolean
  /** Color up/down by the value's sign, or by `colorBy` when given (e.g. color a price by its change). */
  colored?: boolean
  colorBy?: number | null
  /** Prefix with ▲ / ▼. */
  arrow?: boolean
  /** Live cells: flash green/red for one tick when the value changes (never on mount or on an equal value). */
  flash?: boolean
  className?: string
}

type Tick = { n: number; dir: 'up' | 'down' }

/** A number in tabular figures with optional up/down color, arrow and tick flash. */
export function Num({ value, format = 'number', decimals, signed, colored, colorBy, arrow, flash, className }: NumProps) {
  const dir = direction(colorBy ?? value)
  const useColor = colored || colorBy !== undefined
  const cls = [s.num, useColor ? dir : 'value', className].filter(Boolean).join(' ')
  const mark = arrow ? (dir === 'up' ? '▲ ' : dir === 'down' ? '▼ ' : '') : ''

  const prev = useRef(value)
  const [tick, setTick] = useState<Tick | null>(null)
  useEffect(() => {
    const p = prev.current
    prev.current = value
    if (!flash || p == null || value == null || p === value) return
    if (typeof document !== 'undefined' && document.hidden) return
    setTick((t) => ({ n: (t?.n ?? 0) + 1, dir: value > p ? 'up' : 'down' }))
  }, [value, flash])

  return (
    <span className={cls} data-tick={tick?.dir}>
      {mark}
      {formatNum(value, format, { decimals, signed })}
      {tick && (
        <span key={tick.n} className={s.flash} data-dir={tick.dir} onAnimationEnd={() => setTick(null)} aria-hidden="true" />
      )}
    </span>
  )
}
