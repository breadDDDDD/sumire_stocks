export interface Alert {
  id: number
  code: string | null
  type: string
  params: Record<string, unknown>
  active: boolean
  createdAt: string
  lastTriggeredAt: string | null
}

export interface FiredAlert {
  id: number
  code: string | null
  type: string
  message: string
  time: string
}

export const ALERT_TYPES: { type: string; label: string }[] = [
  { type: 'price_above', label: 'Price above' },
  { type: 'price_below', label: 'Price below' },
  { type: 'pct_change', label: 'Move ≥ % (abs)' },
  { type: 'ma_cross', label: 'MA cross' },
  { type: 'rsi_above', label: 'RSI above' },
  { type: 'rsi_below', label: 'RSI below' },
  { type: 'new_52w_high', label: 'New 52w high' },
  { type: 'negative_insight', label: 'Negative insight' },
]

export const typeLabel = (t: string) => ALERT_TYPES.find((x) => x.type === t)?.label ?? t

export function describeParams(a: Pick<Alert, 'type' | 'params'>): string {
  const p = a.params ?? {}
  const n = (v: unknown) => (typeof v === 'number' ? v.toLocaleString('en-US') : String(v ?? '—'))
  switch (a.type) {
    case 'price_above':
      return `≥ ${n(p.price)}`
    case 'price_below':
      return `≤ ${n(p.price)}`
    case 'pct_change':
      return `|Δ| ≥ ${n(p.pct)}%`
    case 'ma_cross':
      return `${p.fast ?? 'price'} crosses ${p.direction && p.direction !== 'any' ? `${p.direction} ` : ''}${p.slow ?? 'sma200'}`
    case 'rsi_above':
      return `RSI ≥ ${n(p.value)}`
    case 'rsi_below':
      return `RSI ≤ ${n(p.value)}`
    case 'negative_insight':
      return `sentiment ≤ ${n(p.maxSentiment ?? -0.3)}, ${p.minConfidence ?? 'high'} confidence`
    default:
      return ''
  }
}
