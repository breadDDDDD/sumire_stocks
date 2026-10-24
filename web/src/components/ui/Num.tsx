import { direction, formatNum, type NumFormat } from '../../lib/format'

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
  className?: string
}

/** A number in tabular figures with optional up/down color and arrow. */
export function Num({ value, format = 'number', decimals, signed, colored, colorBy, arrow, className }: NumProps) {
  const dir = direction(colorBy ?? value)
  const useColor = colored || colorBy !== undefined
  const cls = [useColor ? dir : 'value', className].filter(Boolean).join(' ')
  const mark = arrow ? (dir === 'up' ? '▲ ' : dir === 'down' ? '▼ ' : '') : ''
  return (
    <span className={cls} style={{ fontVariantNumeric: 'tabular-nums slashed-zero', whiteSpace: 'nowrap' }}>
      {mark}
      {formatNum(value, format, { decimals, signed })}
    </span>
  )
}
