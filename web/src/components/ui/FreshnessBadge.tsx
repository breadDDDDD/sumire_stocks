import { useEffect, useState } from 'react'
import { formatTimeWib } from '../../lib/time'
import { Badge } from './Badge'
import s from './FreshnessBadge.module.css'

export interface FreshnessBadgeProps {
  /** ISO-8601 timestamp of the data. */
  asOf: string | Date | null | undefined
  /** After this many seconds the badge turns amber (red at 3x). Default 20 minutes (Yahoo is ~15 min delayed). */
  staleAfterSec?: number
  label?: string
}

/** "Delayed 14:32:05" (WIB): muted while fresh, amber once stale, crimson when very stale. */
export function FreshnessBadge({ asOf, staleAfterSec = 1200, label = 'Delayed' }: FreshnessBadgeProps) {
  const [now, setNow] = useState(() => Date.now())
  useEffect(() => {
    const id = setInterval(() => setNow(Date.now()), 15_000)
    return () => clearInterval(id)
  }, [])

  if (!asOf) return <Badge tone="muted">No data yet</Badge>
  const t = new Date(asOf).getTime()
  if (Number.isNaN(t)) return <Badge tone="muted">No data yet</Badge>
  const ageSec = (now - t) / 1000
  const stale = ageSec > staleAfterSec
  const veryStale = ageSec > staleAfterSec * 3
  return (
    <Badge tone={veryStale ? 'crimson' : stale ? 'amber' : 'muted'} title={`As of ${formatTimeWib(t)} WIB`}>
      {stale ? 'Stale' : label}
      <span className={s.time}>{formatTimeWib(t)}</span>
    </Badge>
  )
}
