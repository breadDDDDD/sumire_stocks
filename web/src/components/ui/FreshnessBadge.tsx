import { useEffect, useState } from 'react'
import { formatTimeWib } from '../../lib/time'
import { Badge } from './Badge'

export interface FreshnessBadgeProps {
  /** ISO-8601 timestamp of the data. */
  asOf: string | Date | null | undefined
  /** After this many seconds the badge turns red. Default 20 minutes (Yahoo is ~15 min delayed). */
  staleAfterSec?: number
  label?: string
}

/** "Delayed · as of 14:32:05 WIB", amber while fresh, red once stale. */
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
  return (
    <Badge tone={stale ? 'down' : 'amber'} title={new Date(t).toString()}>
      {stale ? 'Stale' : label} · as of {formatTimeWib(t)} WIB
    </Badge>
  )
}
