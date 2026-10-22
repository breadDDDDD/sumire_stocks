import { useEffect, useMemo, useRef } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { api, ApiError } from '../../api/client'
import { InsightCard } from '../../components/insight/InsightCard'
import { ageText, type Insight } from '../../components/insight/types'
import {
  blockedMessage,
  isActive,
  researchErrorText,
  sameSubject,
  useEnqueue,
  useInsightById,
  useInsightReady,
  useResearchStatus,
  type ResearchJob,
} from '../../components/research/api'
import ResearchStatus, { JobProgress } from '../../components/research/ResearchStatus'
import { usePane } from '../../shell/workspace'
import { Badge, Button, Empty, ErrorState, Skeleton } from '../../components/ui'

export interface InsightViewProps {
  kind: 'stock' | 'sector'
  /** Ticker for stock, sector name for sector. */
  subject: string
  /** INS!: queue a fresh insight on open. */
  force?: boolean
}

// INS! entries are re-mounted on pane history navigation; don't queue the same subject twice in a short time.
const recentlyForced = new Map<string, number>()

interface NoInsightBody {
  lastJob?: ResearchJob | null
}

/** Cached insight for a stock or sector with refresh, queued/running state and live arrival over SignalR. */
export function InsightView({ kind, subject, force }: InsightViewProps) {
  const qc = useQueryClient()
  const { open } = usePane()
  const status = useResearchStatus()
  const enqueue = useEnqueue()
  const key = `${kind}:${subject.toUpperCase()}`

  // stock: dedicated endpoint; sector: the latest finished job's insight from the queue
  const stockQuery = useQuery({
    queryKey: ['stockInsight', subject],
    queryFn: ({ signal }) => api<Insight>(`/stocks/${encodeURIComponent(subject)}/insight`, { signal }),
    enabled: kind === 'stock',
    retry: (n, e) => !(e instanceof ApiError && e.status < 500) && n < 1,
  })
  const jobs = useMemo(
    () => (status.data?.jobs ?? []).filter((j) => j.kind === kind && sameSubject(j.subject, subject)),
    [status.data, kind, subject],
  )
  const lastSectorJob = kind === 'sector' ? jobs.find((j) => j.status === 'done' && j.insightId) : undefined
  const sectorQuery = useInsightById(lastSectorJob?.insightId)
  const q = kind === 'stock' ? stockQuery : sectorQuery

  const notFound = kind === 'stock' && stockQuery.error instanceof ApiError && stockQuery.error.status === 404
  const lastJobFrom404 = notFound ? ((stockQuery.error as ApiError).body as NoInsightBody | undefined)?.lastJob : null
  const activeJob = jobs.find(isActive) ?? (lastJobFrom404 && isActive(lastJobFrom404) ? lastJobFrom404 : undefined)
  const lastFailed = jobs[0]?.status === 'failed' ? jobs[0] : undefined
  const insight = q.data
  const justQueued =
    enqueue.isSuccess && enqueue.data.status !== 'cached' && !jobs.some((j) => j.id === enqueue.data.jobId)

  const refetchInsight = () => {
    if (kind === 'stock') void qc.invalidateQueries({ queryKey: ['stockInsight', subject] })
  }

  useInsightReady((e) => {
    if (e.kind === kind && sameSubject(e.subject, subject)) refetchInsight()
  })

  // fallback when the hub event is missed: the tracked job finished according to the queue
  const prevActive = useRef<number | undefined>(undefined)
  useEffect(() => {
    if (prevActive.current !== undefined && !activeJob) refetchInsight()
    prevActive.current = activeJob?.id
  })

  const research = () => enqueue.mutate({ kind, subject, force: true })

  useEffect(() => {
    if (!force) return
    const last = recentlyForced.get(key) ?? 0
    if (Date.now() - last < 120_000) return
    recentlyForced.set(key, Date.now())
    enqueue.mutate({ kind, subject, force: true })
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [force, key])

  const blocked = blockedMessage(status.data)
  const label = kind === 'stock' ? `Researching ${subject}` : `Researching ${subject} sector`
  const refreshBtn = (
    <Button
      onClick={research}
      disabled={enqueue.isPending || !!activeJob || justQueued}
      title={blocked ?? 'Queue fresh research (INS!)'}
    >
      {activeJob ? 'Queued' : 'Refresh'}
    </Button>
  )

  const enqueueError = enqueue.error ? (
    <div className="muted" style={{ padding: '6px 12px' }} role="alert">
      Could not queue research: {researchErrorText(enqueue.error)}
    </div>
  ) : null

  if ((q.isPending && q.fetchStatus !== 'idle') || (kind === 'sector' && status.isPending)) return <Skeleton rows={8} />

  if (insight) {
    return (
      <>
        <ResearchStatus />
        {enqueueError}
        {activeJob && <JobProgress job={activeJob} label={`${label} · showing the cached insight meanwhile`} />}
        <InsightCard
          insight={insight}
          onOpenTicker={(c) => open(`${c} DES`)}
          actions={
            <>
              <Badge tone={insight.ageMinutes > 24 * 60 ? 'down' : 'muted'} title={insight.createdAt}>
                {ageText(insight.ageMinutes)}
              </Badge>
              {refreshBtn}
            </>
          }
        />
      </>
    )
  }

  if (q.isError && !notFound) {
    return (
      <ErrorState
        error={q.error}
        title={q.error instanceof ApiError && q.error.status >= 500 ? 'Research unavailable' : 'Could not load'}
        onRetry={() => q.refetch()}
      />
    )
  }

  if (activeJob || enqueue.isPending || justQueued) {
    return (
      <>
        <ResearchStatus />
        <JobProgress job={activeJob} label={label} />
      </>
    )
  }

  if (status.isError && kind === 'sector') {
    return <Empty title="Research unavailable" message={researchErrorText(status.error)} />
  }

  return (
    <>
      <ResearchStatus />
      {enqueueError}
      <Empty
        title={lastFailed ? 'Last research failed' : 'No insight yet'}
        message={
          lastFailed
            ? `${lastFailed.error ?? 'The research agent did not return a valid answer.'} You can try again.`
            : blocked
              ? `${blocked} Insights for watchlist stocks are also made in the scheduled batches.`
              : `Nothing researched for ${subject} yet. Research takes about 1–2 minutes and counts against today's cap.`
        }
      >
        <Button variant="primary" onClick={research} disabled={enqueue.isPending}>
          Research now
        </Button>
        {blocked && <Button onClick={() => open('RQ')}>Open RQ</Button>}
      </Empty>
    </>
  )
}
