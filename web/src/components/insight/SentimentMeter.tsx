import s from './InsightCard.module.css'

function label(v: number): string {
  if (v >= 0.5) return 'Bullish'
  if (v >= 0.15) return 'Leaning bullish'
  if (v > -0.15) return 'Neutral'
  if (v > -0.5) return 'Leaning bearish'
  return 'Bearish'
}

/** Sentiment from -1 (bearish) to 1 (bullish) as a centred bar with a marker. */
export function SentimentMeter({ value }: { value: number | null | undefined }) {
  if (value == null || !Number.isFinite(value)) return null
  const v = Math.max(-1, Math.min(1, value))
  const pos = ((v + 1) / 2) * 100
  const dir = v > 0.05 ? 'up' : v < -0.05 ? 'down' : 'flat'
  return (
    <span className={s.sentiment} title={`Sentiment ${v.toFixed(2)} (−1 bearish … +1 bullish)`}>
      <span className={s.meter} role="meter" aria-valuemin={-1} aria-valuemax={1} aria-valuenow={v} aria-label="Sentiment">
        <span
          className={`${s.fill} ${s[dir]}`}
          style={v >= 0 ? { left: '50%', width: `${pos - 50}%` } : { left: `${pos}%`, width: `${50 - pos}%` }}
        />
        <span className={s.mid} />
        <span className={s.marker} style={{ left: `${pos}%` }} />
      </span>
      <span className={dir}>
        {label(v)} {v > 0 ? '+' : ''}
        {v.toFixed(2)}
      </span>
    </span>
  )
}
