import { useEffect, useState, type ReactNode } from 'react'
import { HubConnectionState } from '@microsoft/signalr'
import { useHubStatus } from '../api/signalr'
import ResearchUsage from '../components/research/ResearchUsage'
import { useResearchStatus } from '../components/research/api'
import { sessionState } from '../lib/session'
import { formatTimeWib } from '../lib/time'
import { useMarketStatus } from './useMarketStatus'
import s from './StatusBar.module.css'

type Tone = 'ok' | 'warn' | 'bad' | 'none'

/** A 6px square that carries the state color; the label stays muted. */
function Lamp({ tone, label, title, pulse }: { tone: Tone; label: ReactNode; title: string; pulse?: boolean }) {
  return (
    <span className={s.lamp} data-tone={tone} title={title}>
      <span className={`${s.sq} ${pulse ? s.pulse : ''}`} aria-hidden="true" />
      {label}
    </span>
  )
}

/** Same thresholds as FreshnessBadge: amber after 20 minutes, red after an hour. */
const STALE_SEC = 1200

function useNow(ms: number) {
  const [now, setNow] = useState(() => Date.now())
  useEffect(() => {
    const id = setInterval(() => setNow(Date.now()), ms)
    return () => clearInterval(id)
  }, [ms])
  return now
}

/** Bottom strip: feed, hub, DB and EOD lamps, then research usage with the runner that would go next. */
export function StatusBar() {
  const status = useMarketStatus()
  const research = useResearchStatus()
  const hub = useHubStatus()
  const now = useNow(15_000)
  const d = status.data
  const current = research.data?.current
  const researchRunning = research.data?.jobs.some((j) => j.status === 'running') ?? false
  const barsStale = !!(d?.lastBarDate && d.expectedBarDate && d.lastBarDate < d.expectedBarDate)

  const quoteT = d?.lastQuoteTime ? new Date(d.lastQuoteTime).getTime() : NaN
  const age = (now - quoteT) / 1000
  const feedTone: Tone = Number.isNaN(quoteT) ? 'none' : age > STALE_SEC * 3 ? 'bad' : age > STALE_SEC ? 'warn' : 'ok'
  const feedLabel = Number.isNaN(quoteT) ? 'Feed' : `Feed ${formatTimeWib(quoteT).slice(0, 5)}`
  const feedTitle = Number.isNaN(quoteT)
    ? 'No quotes yet'
    : `${feedTone === 'ok' ? 'Delayed' : 'Stale'} quotes, last at ${formatTimeWib(quoteT)} WIB`

  const hubTone: Tone =
    hub === HubConnectionState.Connected ? 'ok' : hub === HubConnectionState.Disconnected ? 'bad' : 'warn'
  const hubTitle =
    hub === HubConnectionState.Connected
      ? 'Live quote connection: connected'
      : hub === HubConnectionState.Disconnected
        ? 'Live quote connection: offline'
        : 'Live quote connection: reconnecting'

  const dbTone: Tone = !d ? 'none' : d.db === 'ok' ? 'ok' : 'bad'
  const dbTitle = !d ? 'Database: unknown' : d.db === 'ok' ? 'Database: ok' : (d.dbError ?? 'Database unreachable')

  const eodTone: Tone = !d ? 'none' : d.eod.running || barsStale ? 'warn' : d.eod.lastError ? 'bad' : 'ok'
  const eodTitle = !d
    ? 'Daily bars: unknown'
    : d.eod.running
      ? 'End-of-day job running'
      : barsStale
        ? `Last daily bar ${d.lastBarDate}, expected ${d.expectedBarDate}`
        : d.eod.lastError
          ? `Last end-of-day run failed: ${d.eod.lastError}`
          : `Daily bars current (${d.lastBarDate ?? '—'})`

  return (
    <div className={s.bar}>
      <span className={s.lamps}>
        <Lamp tone={feedTone} label={feedLabel} title={feedTitle} />
        <Lamp tone={hubTone} label="Hub" title={hubTitle} pulse={hubTone === 'ok' && sessionState(new Date(now)) === 'LIVE'} />
        <Lamp tone={dbTone} label="DB" title={dbTitle} />
        <Lamp tone={eodTone} label="EOD" title={eodTitle} />
        {status.isError && <Lamp tone="bad" label="API" title="GET /api/status failed" />}
      </span>
      <span className={s.research}>
        <ResearchUsage />
        {current && (
          <span className={s.runner} title="Runner and model the next research job would use">
            {researchRunning && <span className={s.running} aria-label="Research running" />}
            {current.runner} · {current.model}
          </span>
        )}
      </span>
    </div>
  )
}
