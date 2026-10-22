import { useState, type KeyboardEvent } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { api, ApiError } from '../api/client'
import { InsightCard } from '../components/insight/InsightCard'
import { ageText, type Insight } from '../components/insight/types'
import {
  isActive,
  researchErrorText,
  useEnqueue,
  useInsightReady,
  useResearchStatus,
} from '../components/research/api'
import ResearchStatus, { JobProgress } from '../components/research/ResearchStatus'
import { Badge, Button, Empty, ErrorState, Panel, Skeleton } from '../components/ui'
import { formatDateWib, formatTimeWib, wibParts } from '../lib/time'
import { usePane } from '../shell/workspace'
import type { FunctionProps } from './types'
import s from './brief/Brief.module.css'

type Tab = 'open' | 'close' | 'week'

const TABS: { id: Tab; label: string; batch: string; schedule: string }[] = [
  { id: 'open', label: 'Morning', batch: 'morning', schedule: 'Every trading day at 07:45 WIB: overnight US/Asia moves, commodities and what to watch on IHSG.' },
  { id: 'close', label: 'Close', batch: 'close', schedule: 'Every trading day at 16:45 WIB, after the EOD pipeline: why IHSG moved, sector drivers, watchlist moves.' },
  { id: 'week', label: 'Week', batch: 'weekend', schedule: 'Sundays at 19:00 WIB: macro calendar, BI/Fed events, earnings and dividends for the week ahead.' },
]

/** Weekend → week-ahead, after the close recap → close, otherwise morning. */
function defaultTab(): Tab {
  const { weekday, minutes } = wibParts()
  if (weekday === 0 || weekday === 6) return 'week'
  return minutes >= 16 * 60 + 45 ? 'close' : 'open'
}

function parseTab(arg: string | undefined): Tab | null {
  const a = arg?.toLowerCase()
  if (!a) return null
  if (a.startsWith('morn') || a === 'open') return 'open'
  if (a.startsWith('clos') || a === 'recap') return 'close'
  if (a.startsWith('week')) return 'week'
  return null
}

export default function BRIEF({ args }: FunctionProps) {
  const [tab, setTab] = useState<Tab>(() => parseTab(args[0]) ?? defaultTab())
  const qc = useQueryClient()
  const { open } = usePane()
  const status = useResearchStatus()
  const enqueue = useEnqueue()
  const meta = TABS.find((t) => t.id === tab)!

  const q = useQuery({
    queryKey: ['brief', tab],
    queryFn: ({ signal }) => api<Insight>(`/briefs/latest?kind=${tab}`, { signal }),
    retry: (n, e) => !(e instanceof ApiError && e.status < 500) && n < 1,
  })

  useInsightReady((e) => {
    if (e.kind.startsWith('brief')) void qc.invalidateQueries({ queryKey: ['brief'] })
  })

  const job = status.data?.jobs.find((j) => j.kind === `brief_${tab}` && isActive(j))
  const next = status.data?.nextBatches.find((b) => b.kind === meta.batch)
  const notFound = q.error instanceof ApiError && q.error.status === 404

  const onTabKey = (e: KeyboardEvent) => {
    const i = TABS.findIndex((t) => t.id === tab)
    if (e.key === 'ArrowRight' || e.key === 'l') setTab(TABS[(i + 1) % TABS.length].id)
    else if (e.key === 'ArrowLeft' || e.key === 'h') setTab(TABS[(i + TABS.length - 1) % TABS.length].id)
    else if (e.key === '1' || e.key === '2' || e.key === '3') setTab(TABS[Number(e.key) - 1].id)
    else return
    e.preventDefault()
  }

  const generate = (
    <Button
      onClick={() => enqueue.mutate({ kind: tab })}
      disabled={enqueue.isPending || !!job}
      title="Queue this brief now (counts against today's cap)"
    >
      {job ? 'Queued' : 'Generate now'}
    </Button>
  )

  let body
  if (q.isPending) body = <Skeleton rows={10} />
  else if (q.data)
    body = (
      <>
        {job && <JobProgress job={job} label={`Writing a new ${meta.label.toLowerCase()} brief`} />}
        <InsightCard
          insight={q.data}
          variant="brief"
          onOpenTicker={(c) => open(`${c} DES`)}
          actions={
            <Badge tone="muted" title={q.data.createdAt}>
              {formatDateWib(q.data.createdAt)} {formatTimeWib(q.data.createdAt).slice(0, 5)} WIB · {ageText(q.data.ageMinutes)}
            </Badge>
          }
        />
      </>
    )
  else if (notFound)
    body = job ? (
      <JobProgress job={job} label={`Writing the ${meta.label.toLowerCase()} brief`} />
    ) : (
      <Empty title={`No ${meta.label.toLowerCase()} brief yet`} message={meta.schedule}>
        {next && (
          <span className="muted">
            Next: {formatDateWib(next.at)} {formatTimeWib(next.at).slice(0, 5)} WIB
          </span>
        )}
        {generate}
      </Empty>
    )
  else
    body = (
      <ErrorState
        error={q.error}
        title={q.error instanceof ApiError && q.error.status >= 500 ? 'Briefs unavailable' : 'Could not load'}
        onRetry={() => q.refetch()}
      />
    )

  return (
    <Panel
      title="BRIEF"
      subtitle="Morning brief · closing recap · week ahead"
      actions={
        <>
          {next && (
            <span className="muted" title="Next scheduled run">
              next {formatTimeWib(next.at).slice(0, 5)}
            </span>
          )}
          {q.data && generate}
        </>
      }
    >
      <div className={s.tabs} role="tablist" tabIndex={0} onKeyDown={onTabKey} aria-label="Brief kind (← → or 1–3)">
        {TABS.map((t, i) => (
          <button
            key={t.id}
            type="button"
            role="tab"
            aria-selected={tab === t.id}
            tabIndex={-1}
            className={`${s.tab} ${tab === t.id ? s.active : ''}`}
            onClick={() => setTab(t.id)}
          >
            <kbd>{i + 1}</kbd> {t.label}
          </button>
        ))}
      </div>
      <ResearchStatus compact />
      {enqueue.error && (
        <div className="muted" style={{ padding: '6px 12px' }} role="alert">
          Could not queue the brief: {researchErrorText(enqueue.error)}
        </div>
      )}
      {body}
    </Panel>
  )
}
