export interface Bar {
  date: string | null
  close: number
}

/** Closes on the dates every series has, oldest first. */
export function alignCloses(series: Bar[][]): { dates: string[]; closes: number[][] } {
  if (!series.length) return { dates: [], closes: [] }
  const maps = series.map((bars) => new Map(bars.filter((b) => b.date && b.close > 0).map((b) => [b.date as string, b.close])))
  const dates = [...maps[0].keys()].filter((d) => maps.every((m) => m.has(d))).sort()
  return { dates, closes: maps.map((m) => dates.map((d) => m.get(d) as number)) }
}

export function rebase(closes: number[]): number[] {
  const first = closes[0]
  return first ? closes.map((c) => (c / first) * 100) : []
}

export function logReturns(closes: number[]): number[] {
  const out: number[] = []
  for (let i = 1; i < closes.length; i++) out.push(Math.log(closes[i] / closes[i - 1]))
  return out
}

export function correlation(a: number[], b: number[]): number | null {
  const n = Math.min(a.length, b.length)
  if (n < 5) return null
  let ma = 0
  let mb = 0
  for (let i = 0; i < n; i++) {
    ma += a[i]
    mb += b[i]
  }
  ma /= n
  mb /= n
  let cov = 0
  let va = 0
  let vb = 0
  for (let i = 0; i < n; i++) {
    const da = a[i] - ma
    const db = b[i] - mb
    cov += da * db
    va += da * da
    vb += db * db
  }
  return va && vb ? cov / Math.sqrt(va * vb) : null
}

/** Annualized volatility in percent from daily log returns (245 IDX sessions a year). */
export function annualVolPct(rets: number[]): number | null {
  if (rets.length < 5) return null
  const m = rets.reduce((x, y) => x + y, 0) / rets.length
  const v = rets.reduce((x, r) => x + (r - m) ** 2, 0) / (rets.length - 1)
  return Math.sqrt(v * 245) * 100
}

/** Deepest peak-to-trough fall in percent (negative number). */
export function maxDrawdownPct(closes: number[]): number | null {
  if (closes.length < 2) return null
  let peak = closes[0]
  let worst = 0
  for (const c of closes) {
    peak = Math.max(peak, c)
    worst = Math.min(worst, c / peak - 1)
  }
  return worst * 100
}

export const SERIES_COLORS = ['#ffa028', '#4fa3e0', '#f2f2f2', '#8c8c8c']
