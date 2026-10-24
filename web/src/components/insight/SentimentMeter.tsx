import { formatNum } from '../ui'
import s from './InsightCard.module.css'

function label(v: number): string {
  if (v >= 0.5) return 'Bullish'
  if (v >= 0.15) return 'Leaning bullish'
  if (v > -0.15) return 'Neutral'
  if (v > -0.5) return 'Leaning bearish'
  return 'Bearish'
}

/** Sentiment from -1 (bearish) to 1 (bullish): a hairline track with one needle and the signed score. */
export function SentimentMeter({ value }: { value: number | null | undefined }) {
  if (value == null || !Number.isFinite(value)) return null
  const v = Math.max(-1, Math.min(1, value))
  const pos = ((v + 1) / 2) * 100
  const dir = v > 0.05 ? 'up' : v < -0.05 ? 'down' : 'flat'
  return (
    <span className={s.sentiment} title={`${label(v)} · sentiment ${v.toFixed(2)} (−1 bearish … +1 bullish)`}>
      <span className={s.end}>Bearish</span>
      <span
        className={s.meter}
        role="meter"
        aria-valuemin={-1}
        aria-valuemax={1}
        aria-valuenow={v}
        aria-valuetext={`${label(v)} ${v.toFixed(2)}`}
        aria-label="Sentiment"
      >
        <span className={s.mid} />
        <span className={s.needle} style={{ left: `${pos}%` }} />
      </span>
      <span className={s.end}>Bullish</span>
      <span className={`${s.score} ${dir}`}>{formatNum(v, 'number', { decimals: 2, signed: true })}</span>
      <span className={s.sentLabel}>{label(v)}</span>
    </span>
  )
}
