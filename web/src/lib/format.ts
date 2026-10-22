export type NumFormat = 'price' | 'idr' | 'pct' | 'compact' | 'number'

const cache = new Map<string, Intl.NumberFormat>()
function nf(min: number, max: number): Intl.NumberFormat {
  const k = `${min}:${max}`
  let f = cache.get(k)
  if (!f) {
    f = new Intl.NumberFormat('en-US', { minimumFractionDigits: min, maximumFractionDigits: max })
    cache.set(k, f)
  }
  return f
}

const UNITS: [number, string][] = [
  [1e12, 'T'],
  [1e9, 'B'],
  [1e6, 'M'],
  [1e3, 'K'],
]

/**
 * Format a number for display. Missing values become an em dash.
 * - price: IDX prices are whole rupiah; decimals only when present (macro symbols, FX)
 * - idr:   "Rp 1,234"
 * - pct:   value is already a percent (0.42 -> "0.42%")
 * - compact: 1.2T / 345.6B / 12.3M / 4.5K
 */
export function formatNum(
  value: number | null | undefined,
  format: NumFormat = 'number',
  opts: { decimals?: number; signed?: boolean } = {},
): string {
  if (value === null || value === undefined || !Number.isFinite(value)) return '—'
  const sign = opts.signed && value > 0 ? '+' : ''
  const abs = Math.abs(value)
  switch (format) {
    case 'pct':
      return `${sign}${nf(opts.decimals ?? 2, opts.decimals ?? 2).format(value)}%`
    case 'compact': {
      const unit = UNITS.find(([n]) => abs >= n)
      if (!unit) return sign + nf(0, opts.decimals ?? 0).format(value)
      return `${sign}${nf(0, opts.decimals ?? 1).format(value / unit[0])}${unit[1]}`
    }
    case 'idr':
      return `${value < 0 ? '-' : sign}Rp ${nf(0, opts.decimals ?? 0).format(abs)}`
    case 'price': {
      const d = opts.decimals ?? (Number.isInteger(value) ? 0 : abs < 10 ? 4 : 2)
      return sign + nf(d, d).format(value)
    }
    default:
      return sign + nf(opts.decimals ?? 0, opts.decimals ?? 2).format(value)
  }
}

/** 'up' | 'down' | 'flat' for a signed change. */
export function direction(value: number | null | undefined): 'up' | 'down' | 'flat' {
  if (!value || !Number.isFinite(value)) return 'flat'
  return value > 0 ? 'up' : 'down'
}
