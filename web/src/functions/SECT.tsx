import { useMutation, useQuery } from '@tanstack/react-query'
import { useState } from 'react'
import { api, ApiError } from '../api/client'
import { Badge, Button, ErrorState, Num, Panel, Skeleton, Table, type BadgeTone, type Column } from '../components/ui'
import { usePane } from '../shell/workspace'
import { RotationChart, type RotationPoint } from './sect/RotationChart'
import { Treemap, type TreeItem } from './sect/Treemap'
import type { FunctionProps } from './types'
import s from './sect/SECT.module.css'

interface Member {
  code: string
  name: string
  marketCap: number | null
  weight: number
  last: number | null
  chgPct: number | null
}

interface Sector {
  name: string
  weighting: string
  d1: number | null
  w1: number | null
  m1: number | null
  m3: number | null
  rsM1: number | null
  rsM3: number | null
  rotation: RotationPoint | null
  trail: RotationPoint[]
  members: Member[]
}

interface Sectors {
  asOf: string
  sectors: Sector[]
}

const QUADRANT_TONE: Record<string, BadgeTone> = { leading: 'up', weakening: 'amber', lagging: 'down', improving: 'default' }

const pct = (v: number | null) => <Num value={v} format="pct" signed colored />

export default function SECT({ args }: FunctionProps) {
  const pane = usePane()
  const wantsResearch = args[0]?.toUpperCase() === 'INS'
  const [drill, setDrill] = useState<string | null>(null)
  const q = useQuery({
    queryKey: ['market', 'sectors'],
    queryFn: ({ signal }) => api<Sectors>('/market/sectors', { signal }),
    refetchInterval: 5 * 60_000,
  })

  const sectors = q.data?.sectors ?? []
  const current = drill ? sectors.find((x) => x.name === drill) : undefined

  const sectorItems: TreeItem[] = sectors.map((x) => {
    const cap = x.members.reduce((t, m) => t + (m.marketCap ?? 0), 0)
    return { id: x.name, label: x.name, size: cap || x.members.length, chg: x.d1 }
  })
  const memberItems: TreeItem[] = (current?.members ?? []).map((m) => ({
    id: m.code,
    label: m.code,
    sub: m.name,
    size: m.marketCap ?? m.weight,
    chg: m.chgPct,
  }))

  const columns: Column<Sector>[] = [
    { key: 'name', header: 'Sector', render: (x) => <span className={s.secName}>{x.name}</span> },
    { key: 'd1', header: '1D', align: 'right', render: (x) => pct(x.d1) },
    { key: 'w1', header: '1W', align: 'right', render: (x) => pct(x.w1) },
    { key: 'm1', header: '1M', align: 'right', render: (x) => pct(x.m1) },
    { key: 'm3', header: '3M', align: 'right', render: (x) => pct(x.m3) },
    { key: 'rsM1', header: 'RS 1M', align: 'right', render: (x) => pct(x.rsM1) },
    { key: 'rsM3', header: 'RS 3M', align: 'right', render: (x) => pct(x.rsM3) },
    {
      key: 'quadrant',
      header: 'Rotation',
      sortValue: (x) => x.rotation?.quadrant ?? null,
      render: (x) => (x.rotation ? <Badge tone={QUADRANT_TONE[x.rotation.quadrant] ?? 'muted'}>{x.rotation.quadrant}</Badge> : '—'),
    },
  ]

  const memberColumns: Column<Member>[] = [
    { key: 'code', header: 'Code', render: (m) => <span className={s.secName}>{m.code}</span> },
    { key: 'name', header: 'Name', render: (m) => <span className={s.memberName}>{m.name}</span> },
    { key: 'last', header: 'Last', align: 'right', render: (m) => <Num value={m.last} format="price" colorBy={m.chgPct} /> },
    { key: 'chgPct', header: 'Chg %', align: 'right', render: (m) => pct(m.chgPct) },
    { key: 'weight', header: 'Weight', align: 'right', render: (m) => <Num value={m.weight * 100} format="pct" decimals={1} /> },
    { key: 'marketCap', header: 'Mkt cap', align: 'right', render: (m) => <Num value={m.marketCap} format="compact" /> },
  ]

  return (
    <Panel
      title="SECT"
      subtitle={
        current ? (
          <span>
            <button className={s.crumb} onClick={() => setDrill(null)}>
              Sectors
            </button>{' '}
            › {current.name}
          </span>
        ) : (
          'Sector heatmap and rotation'
        )
      }
      actions={q.data && <span className="muted">as of {q.data.asOf}</span>}
      bodyClassName={s.body}
    >
      {q.isPending ? (
        <Skeleton rows={10} columns={6} />
      ) : q.isError ? (
        <ErrorState error={q.error} onRetry={() => q.refetch()} />
      ) : (
        <>
          {(wantsResearch || current) && (
            <ResearchBar sectors={sectors.map((x) => x.name)} initial={current?.name ?? sectors[0]?.name} prominent={wantsResearch} key={current?.name} />
          )}
          <div className={s.mapWrap}>
            {current ? (
              <Treemap
                key={current.name}
                label={`${current.name} members`}
                items={memberItems}
                maxChg={5}
                onOpen={(it) => pane.open({ code: 'DES', ticker: it.id, args: [] })}
                onBack={() => setDrill(null)}
              />
            ) : (
              <Treemap label="Sectors" items={sectorItems} maxChg={3} onOpen={(it) => setDrill(it.id)} />
            )}
          </div>
          <div className={s.legend}>
            {current ? 'Size = market cap · color = today · Enter opens DES · Backspace goes back' : 'Size = market cap · color = today · Enter drills in'}
          </div>

          {current ? (
            <div className={s.membersTable}>
              <Table
                columns={memberColumns}
                rows={current.members}
                rowKey={(m) => m.code}
                initialSort={{ key: 'weight', dir: 'desc' }}
                onOpen={(m) => pane.open({ code: 'DES', ticker: m.code, args: [] })}
              />
            </div>
          ) : (
            <div className={s.lower}>
              <section className={s.perf}>
                <h3 className={s.h}>Performance vs IHSG</h3>
                <Table
                  columns={columns}
                  rows={sectors}
                  rowKey={(x) => x.name}
                  initialSort={{ key: 'd1', dir: 'desc' }}
                  onOpen={(x) => setDrill(x.name)}
                />
              </section>
              <section className={s.rot}>
                <h3 className={s.h}>Rotation</h3>
                <RotationChart sectors={sectors} onOpen={setDrill} />
              </section>
            </div>
          )}
        </>
      )}
    </Panel>
  )
}

interface QueueResult {
  jobId: number | null
  status: 'queued' | 'duplicate' | 'cached'
  insightId: number | null
}

function ResearchBar({ sectors, initial, prominent }: { sectors: string[]; initial?: string; prominent: boolean }) {
  const [subject, setSubject] = useState(initial ?? '')
  const m = useMutation({
    mutationFn: (name: string) => api<QueueResult>('/research', { method: 'POST', body: { kind: 'sector', subject: name } }),
  })
  const status = m.data
    ? m.data.status === 'cached'
      ? 'A fresh narrative already exists. See RQ / BRIEF.'
      : m.data.status === 'duplicate'
        ? `Already in the queue (job #${m.data.jobId}).`
        : `Queued (job #${m.data.jobId}). Sumire will notify you when it is ready.`
    : null

  return (
    <div className={[s.research, prominent && s.researchProminent].filter(Boolean).join(' ')}>
      <span className={s.h}>Sector narrative</span>
      {prominent && (
        <select className={s.select} value={subject} onChange={(e) => (setSubject(e.target.value), m.reset())} aria-label="Sector">
          {sectors.map((n) => (
            <option key={n}>{n}</option>
          ))}
        </select>
      )}
      <Button variant={prominent ? 'primary' : 'default'} disabled={!subject || m.isPending} onClick={() => m.mutate(subject)}>
        {m.isPending ? 'Queuing…' : `Research ${prominent ? '' : subject}`.trim()}
      </Button>
      {status && <span className={s.researchStatus}>{status}</span>}
      {m.isError && <span className="down">{m.error instanceof ApiError ? m.error.message : 'Could not queue research'}</span>}
    </div>
  )
}
