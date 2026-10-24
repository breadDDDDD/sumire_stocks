import { useQuery } from '@tanstack/react-query'
import { HubConnectionState } from '@microsoft/signalr'
import { api } from '../api/client'
import { useHubStatus } from '../api/signalr'
import ResearchUsage from '../components/research/ResearchUsage'
import { useResearchStatus } from '../components/research/api'
import { Badge, FreshnessBadge } from '../components/ui'
import s from './StatusBar.module.css'

/** GET /api/status (docs/api/market.md). */
interface MarketStatus {
  time: string
  session: string
  lastBarDate: string | null
  expectedBarDate: string | null
  lastQuoteTime: string | null
  db: string
  dbError: string | null
  eod: { running: boolean; lastError: string | null }
}

function useMarketStatus() {
  return useQuery({
    queryKey: ['status'],
    queryFn: ({ signal }) => api<MarketStatus>('/status', { signal }),
    refetchInterval: 60_000,
    retry: false,
  })
}

/** Bottom-left strip: data freshness, DB and hub state, research usage with the runner that would go next. */
export function StatusBar() {
  const status = useMarketStatus()
  const research = useResearchStatus()
  const hub = useHubStatus()
  const d = status.data
  const current = research.data?.current
  const researchRunning = research.data?.jobs.some((j) => j.status === 'running') ?? false
  const barsStale = d?.lastBarDate && d.expectedBarDate && d.lastBarDate < d.expectedBarDate

  return (
    <div className={s.bar}>
      <span className={s.seg}>
        {status.isError ? (
          <Badge tone="down" title="GET /api/status failed">API offline</Badge>
        ) : (
          <FreshnessBadge asOf={d?.lastQuoteTime ?? null} />
        )}
        {d && d.db !== 'ok' && (
          <Badge tone="amber" title={d.dbError ?? 'Database unreachable'}>
            DB down
          </Badge>
        )}
        {barsStale && (
          <Badge tone="amber" title={`Last daily bar ${d.lastBarDate}, expected ${d.expectedBarDate}`}>
            {d.eod.running ? 'EOD running' : 'Bars stale'}
          </Badge>
        )}
        {hub !== HubConnectionState.Connected && (
          <Badge tone="muted" title="Live quote connection">
            {hub === HubConnectionState.Disconnected ? 'Offline' : 'Reconnecting'}
          </Badge>
        )}
      </span>
      <span className={`${s.seg} ${s.research}`}>
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
