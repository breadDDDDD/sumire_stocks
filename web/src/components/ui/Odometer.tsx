import { formatNum, type NumFormat } from '../../lib/format'
import s from './Odometer.module.css'

export interface OdometerProps {
  value: number | null | undefined
  format?: NumFormat
  decimals?: number
  signed?: boolean
  className?: string
}

const DIGITS = ['0', '1', '2', '3', '4', '5', '6', '7', '8', '9']

/**
 * A number whose digits roll to their new value (the IHSG hero on MKT only).
 * Separators stay put; each digit column re-targets from where it is, so a second tick mid-roll stays smooth.
 */
export function Odometer({ value, format = 'number', decimals, signed, className }: OdometerProps) {
  const text = formatNum(value, format, { decimals, signed })
  const chars = [...text]
  return (
    <span className={[s.odo, className].filter(Boolean).join(' ')} aria-label={text} role="img">
      {chars.map((c, i) => {
        // keyed from the right so the ones digit keeps its column when the length changes
        const key = chars.length - i
        const d = DIGITS.indexOf(c)
        if (d < 0)
          return (
            <span key={`c${key}`} className={s.glyph} aria-hidden="true">
              {c}
            </span>
          )
        return (
          <span key={`d${key}`} className={s.window} aria-hidden="true">
            <span className={s.column} style={{ transform: `translateY(${-d * 10}%)` }}>
              {DIGITS.map((g) => (
                <span key={g}>{g}</span>
              ))}
            </span>
            <span className={s.sizer}>0</span>
          </span>
        )
      })}
    </span>
  )
}
