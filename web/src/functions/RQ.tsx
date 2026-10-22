import { useMemo } from 'react'
import {
  blockedMessage,
  durationText,
  researchErrorText,
  useEnqueue,
  useInsightReady,
  usePauseToggle,
  useResearchStatus,
  type EnqueueBody,
  type ResearchJob,
  type RunnerStatus,
} from '../components/research/api'
import { Badge, Button, Empty, Panel, Skeleton, Table, type BadgeTone, type Column } from '../components/ui'
import { formatDateWib, formatTimeWib } from '../lib/time'
import { usePane } from '../shell/workspace'
import type { FunctionProps } from './types'
import s from './rq/Rq.module.css'

const STATUS_TONE: Record<string, BadgeTone> = {
  pending: 'amber',
  running: 'solid',
  done: 'up',
  failed: 'down',
  skipped: 'muted',
}

const KIND_LABEL: Record<string, string> = {
  stock: 'Stock',
  sector: 'Sector',
  ask: 'Ask',
  brief_open: 'Morning brief',
  brief_close: 'Close recap',
  brief_week: 'Week ahead',
}

function when(iso: string): string {
  const today = formatDateWib(new Date())
  const d = formatDateWib(iso)
  return `${d === today ? '' : `${d.slice(0, 6)} `}${formatTimeWib(iso).slice(0, 5)}`
}

/** Re-queue body that raises a pending job's priority and wakes the scheduler. */
function rerunBody(j: ResearchJob): EnqueueBody | null {
  if (j.kind === 'stock' || j.kind === 'sector') return { kind: j.kind, subject: j.subject }
  if (j.kind === 'ask') return { kind: 'ask', question: j.subject }
  if (j.kind.startsWith('brief_')) return { kind: j.kind as EnqueueBody['kind'] }
  return null
}

function Meter({ used, cap, label }: { used: number; cap: number | null; label?: string }) {
  const pct = cap ? Math.min(100, (used / cap) * 100) : 0
  const tone = cap && used >= cap ? s.full : cap && used / cap >= 0.75 ? s.high : ''
  return (
    <span className={s.meterWrap} title={cap ? `${used} of ${cap}` : `${used} (no model cap)`}>
      <span className={s.meter}>{cap ? <span className={`${s.meterFill} ${tone}`} style={{ width: `${pct}%` }} /> : null}</span>
      <span className="value">
        {used}
        {cap ? `/${cap}` : ''}
      </span>
      {label && <span className="muted">{label}</span>}
    </span>
  )
}

export default function RQ(_props: FunctionProps) {
  const status = useResearchStatus()
  const pause = usePauseToggle()
  const enqueue = useEnqueue()
  const { open } = usePane()
  useInsightReady()

  const d = status.data
  const blocked = blockedMessage(d)

  const jobColumns = useMemo<Column<ResearchJob>[]>(
    () => [
      {
        key: 'status',
        header: 'Status',
        width: 84,
        render: (j) => <Badge tone={STATUS_TONE[j.status] ?? 'default'}>{j.status}</Badge>,
      },
      { key: 'kind', header: 'Kind', width: 110, render: (j) => KIND_LABEL[j.kind] ?? j.kind },
      {
        key: 'subject',
        header: 'Subject',
        render: (j) => (
          <span className={s.subject} title={j.subject}>
            {j.subject}
          </span>
        ),
      },
      {
        key: 'runner',
        header: 'Runner / model',
        render: (j) => (j.runner ? `${j.runner}/${j.model ?? ''}` : <span className="muted">—</span>),
        sortValue: (j) => `${j.runner ?? ''}/${j.model ?? ''}`,
      },
      {
        key: 'createdAt',
        header: 'Queued',
        align: 'right',
        width: 90,
        render: (j) => when(j.createdAt),
      },
      {
        key: 'duration',
        header: 'Took',
        align: 'right',
        width: 70,
        render: (j) => durationText(j),
        sortValue: (j) =>
          j.startedAt && j.finishedAt ? Date.parse(j.finishedAt) - Date.parse(j.startedAt) : null,
      },
      {
        key: 'error',
        header: 'Note',
        render: (j) =>
          j.error ? (
            <span className={j.status === 'failed' ? 'down' : s.warn} title={j.error}>
              {j.error}
            </span>
          ) : j.attempts > 1 ? (
            <span className="muted">{j.attempts} attempts</span>
          ) : null,
        sortable: false,
      },
      {
        key: 'actions',
        header: '',
        width: 84,
        sortable: false,
        render: (j) => {
          const body = j.status === 'pending' ? rerunBody(j) : null
          return body ? (
            <Button
              variant="ghost"
              title="Raise priority and wake the scheduler"
              disabled={enqueue.isPending || !!blocked}
              onClick={(e) => {
                e.stopPropagation()
                enqueue.mutate(body)
              }}
            >
              Run now
            </Button>
          ) : null
        },
      },
    ],
    [enqueue, blocked],
  )

  const openJob = (j: ResearchJob) => {
    if (j.kind === 'stock') open(`${j.subject} INS`)
    else if (j.kind.startsWith('brief_')) open(`BRIEF ${j.kind.slice(6)}`)
    else if (j.kind === 'ask') open({ code: 'ASK', args: [j.subject] })
  }

  if (status.isPending)
    return (
      <Panel title="RQ" subtitle="Research queue">
        <Skeleton rows={10} />
      </Panel>
    )

  if (status.isError || !d)
    return (
      <Panel title="RQ" subtitle="Research queue">
        <Empty title="Research unavailable" message={researchErrorText(status.error)}>
          <Button variant="primary" onClick={() => status.refetch()}>
            Retry
          </Button>
        </Empty>
      </Panel>
    )

  const exhausted = d.runners.filter((r) => r.exhaustedUntil)
  const runnerKey = (r: RunnerStatus) => `${r.runner}:${r.model}`

  return (
    <Panel
      title="RQ"
      subtitle="Research queue · usage vs caps"
      actions={
        <Button
          variant={d.paused ? 'primary' : 'default'}
          onClick={() => pause.mutate(!d.paused)}
          disabled={pause.isPending || !d.enabled}
          title={d.enabled ? undefined : 'Disabled in .env (RESEARCH_ENABLED=false)'}
        >
          {d.paused ? '▶ Resume' : '❚❚ Pause'}
        </Button>
      }
    >
      <div className={s.top}>
        <section className={s.card}>
          <h3 className={s.h}>Today</h3>
          <Meter used={d.usage.today} cap={d.usage.dailyCap} label="runs" />
          <div className="muted">Batch cap {d.usage.batchCap} · {d.pending} pending</div>
          <div>
            {!d.enabled ? (
              <Badge tone="down">Disabled</Badge>
            ) : d.paused ? (
              <Badge tone="amber">Paused</Badge>
            ) : d.current ? (
              <Badge tone="up" title="Runner the next job will use">
                Next: {d.current.runner}/{d.current.model}
              </Badge>
            ) : (
              <Badge tone="down">Blocked</Badge>
            )}
          </div>
          {d.blockedReason && <div className={s.warn}>{d.blockedReason}</div>}
        </section>

        <section className={`${s.card} ${s.grow}`}>
          <h3 className={s.h}>Runners</h3>
          <ul className={s.runners}>
            {d.runners.map((r) => (
              <li key={runnerKey(r)} className={r.exhaustedUntil || !r.installed ? s.dim : undefined}>
                <span className={s.runnerName} title={r.cap ? `Cap ${r.cap.cap} shared by ${r.cap.pattern}` : undefined}>
                  {r.runner}:{r.model}
                </span>
                <Meter used={r.used} cap={r.cap?.cap ?? null} />
                {!r.installed && <Badge tone="down">not installed</Badge>}
                {r.exhaustedUntil && (
                  <Badge tone="amber" title="Plan quota exhausted">
                    out until {when(r.exhaustedUntil)}
                  </Badge>
                )}
              </li>
            ))}
          </ul>
          {exhausted.length > 0 && (
            <div className="muted">
              {exhausted.length} exhausted · the next runner in the list is used meanwhile.
            </div>
          )}
        </section>

        <section className={s.card}>
          <h3 className={s.h}>Next batches</h3>
          <ul className={s.batches}>
            {d.nextBatches.map((b) => (
              <li key={b.kind}>
                <span className={s.batchKind}>{b.kind}</span>
                <span className="value">{when(b.at)}</span>
              </li>
            ))}
          </ul>
        </section>
      </div>

      {enqueue.error && (
        <div className={s.warn} style={{ padding: '4px 12px' }} role="alert">
          {researchErrorText(enqueue.error)}
        </div>
      )}

      <Table
        columns={jobColumns}
        rows={d.jobs}
        rowKey={(j) => String(j.id)}
        onOpen={openJob}
        empty={<Empty title="Queue is empty" message="INS!, ASK and the scheduled batches add jobs here." />}
      />
    </Panel>
  )
}
