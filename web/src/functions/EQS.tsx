import { useQuery } from '@tanstack/react-query'
import { useState, type FormEvent } from 'react'
import { api } from '../api/client'
import { Badge, Button, Empty, ErrorState, Num, Panel, Skeleton, type BadgeTone, type Column } from '../components/ui'
import { usePane } from '../shell/workspace'
import { GridTable } from './eqs/GridTable'
import f from './eqs/form.module.css'
import type { FunctionProps } from './types'
import s from './eqs/EQS.module.css'

interface Presets {
  presets: { name: string; description: string }[]
  numericFields: string[]
  textFields: string[]
}

interface Filter {
  id: number
  field: string
  op: string
  value: string
}

interface ScreenRequest {
  preset?: string
  filters?: { field: string; op: string; value: string | number }[]
  sort?: string
  desc?: boolean
  limit?: number
}

interface ScreenRow {
  code: string
  name: string
  sector: string
  last: number | null
  chgPct: number | null
  score: number | null
  label: string | null
  rsi: number | null
  pe: number | null
  pb: number | null
  divYield: number | null
  volRatio: number | null
}

interface ScreenResult {
  preset: string | null
  asOf: string
  count: number
  rows: ScreenRow[]
}

const NUM_OPS = ['gt', 'gte', 'lt', 'lte', 'eq', 'neq']
const TEXT_OPS = ['eq', 'neq', 'contains']
const OP_LABEL: Record<string, string> = { gt: '>', gte: '≥', lt: '<', lte: '≤', eq: '=', neq: '≠', contains: '∋' }
const DEFAULT_PRESET = 'top-score'

function labelTone(label: string | null | undefined): BadgeTone {
  if (!label) return 'muted'
  if (label.includes('Bullish')) return 'up'
  if (label.includes('Bearish')) return 'down'
  return 'muted'
}

let nextId = 1

export default function EQS({ args }: FunctionProps) {
  const pane = usePane()
  const meta = useQuery({
    queryKey: ['screener', 'presets'],
    queryFn: ({ signal }) => api<Presets>('/screener/presets', { signal }),
    staleTime: Infinity,
  })
  const argPreset = args[0]?.toLowerCase()
  const [preset, setPreset] = useState<string | null>(argPreset || DEFAULT_PRESET)
  const [filters, setFilters] = useState<Filter[]>([])
  const [limit, setLimit] = useState(100)
  const [req, setReq] = useState<ScreenRequest>({ preset: argPreset || DEFAULT_PRESET })

  const res = useQuery({
    queryKey: ['screener', req],
    queryFn: ({ signal }) => api<ScreenResult>('/screener', { method: 'POST', body: req, signal }),
    retry: 0,
  })

  const isText = (field: string) => meta.data?.textFields.includes(field) ?? false

  const build = (p: string | null, fs: Filter[], lim: number): ScreenRequest => {
    // a numeric field with a non-number value would be sent as null and rejected with a 400
    const valid = fs.filter((x) => x.field && x.value.trim() !== '' && (isText(x.field) || Number.isFinite(Number(x.value))))
    return {
      ...(p ? { preset: p } : {}),
      ...(valid.length
        ? { filters: valid.map((x) => ({ field: x.field, op: x.op, value: isText(x.field) ? x.value.trim() : Number(x.value) })) }
        : {}),
      limit: lim,
    }
  }

  const run = (e?: FormEvent) => {
    e?.preventDefault()
    setReq(build(preset, filters, limit))
  }

  const pickPreset = (p: string) => {
    const next = preset === p ? null : p
    setPreset(next)
    setReq(build(next, filters, limit))
  }

  const addFilter = () => {
    const field = meta.data?.numericFields[0] ?? 'score'
    setFilters((fs) => [...fs, { id: nextId++, field, op: 'gt', value: '' }])
  }
  const update = (id: number, patch: Partial<Filter>) =>
    setFilters((fs) =>
      fs.map((x) => {
        if (x.id !== id) return x
        const n = { ...x, ...patch }
        if (patch.field) {
          const ops = isText(patch.field) ? TEXT_OPS : NUM_OPS
          if (!ops.includes(n.op)) n.op = ops[0]
        }
        return n
      }),
    )

  const columns: Column<ScreenRow>[] = [
    { key: 'code', header: 'Code', render: (r) => <span className={s.code}>{r.code}</span> },
    { key: 'name', header: 'Name', render: (r) => <span className={s.name} title={r.name}>{r.name}</span> },
    { key: 'sector', header: 'Sector', render: (r) => <span className="muted">{r.sector}</span> },
    { key: 'last', header: 'Last', align: 'right', render: (r) => <Num value={r.last} format="price" colorBy={r.chgPct} /> },
    { key: 'chgPct', header: 'Chg %', align: 'right', render: (r) => <Num value={r.chgPct} format="pct" signed colored /> },
    { key: 'score', header: 'Score', align: 'right', render: (r) => <Num value={r.score} decimals={0} signed colored /> },
    {
      key: 'label',
      header: 'Signal',
      render: (r) => (r.label ? <Badge tone={labelTone(r.label)}>{r.label}</Badge> : <span className="muted">—</span>),
    },
    { key: 'rsi', header: 'RSI', align: 'right', render: (r) => <Num value={r.rsi} decimals={0} /> },
    { key: 'pe', header: 'P/E', align: 'right', render: (r) => <Num value={r.pe} decimals={1} /> },
    { key: 'pb', header: 'P/B', align: 'right', render: (r) => <Num value={r.pb} decimals={2} /> },
    {
      key: 'divYield',
      header: 'Yield',
      align: 'right',
      render: (r) => <Num value={r.divYield != null ? r.divYield * 100 : null} format="pct" decimals={1} />,
    },
    { key: 'volRatio', header: 'Vol ×', align: 'right', render: (r) => <Num value={r.volRatio} decimals={2} /> },
  ]

  const fields = [...(meta.data?.numericFields ?? []), ...(meta.data?.textFields ?? [])]
  const presetDesc = meta.data?.presets.find((p) => p.name === preset)?.description

  return (
    <Panel
      title="EQS"
      subtitle="Equity screener"
      actions={res.data && <span className="muted">{`${res.data.count} match${res.data.count === 1 ? '' : 'es'} · ${res.data.asOf}`}</span>}
      bodyClassName={s.body}
    >
      <form className={f.toolbar} onSubmit={run}>
        <div className={f.row}>
          <span className={f.label}>Presets</span>
          {meta.isError && <span className={f.error}>presets unavailable</span>}
          {meta.data?.presets.map((p) => (
            <button
              key={p.name}
              type="button"
              className={[f.chip, preset === p.name && f.chipOn].filter(Boolean).join(' ')}
              title={p.description}
              onClick={() => pickPreset(p.name)}
            >
              {p.name}
            </button>
          ))}
        </div>
        {presetDesc && <div className={s.desc}>{presetDesc}</div>}
        {filters.map((x) => (
          <div key={x.id} className={f.row}>
            <select className={f.select} value={x.field} onChange={(e) => update(x.id, { field: e.target.value })} aria-label="Field">
              {fields.map((fl) => (
                <option key={fl} value={fl}>
                  {fl}
                </option>
              ))}
            </select>
            <select className={f.select} value={x.op} onChange={(e) => update(x.id, { op: e.target.value })} aria-label="Operator">
              {(isText(x.field) ? TEXT_OPS : NUM_OPS).map((o) => (
                <option key={o} value={o}>
                  {OP_LABEL[o]} {o}
                </option>
              ))}
            </select>
            <input
              className={`${f.input} ${isText(x.field) ? '' : f.num}`}
              value={x.value}
              inputMode={isText(x.field) ? 'text' : 'decimal'}
              placeholder={isText(x.field) ? 'text' : 'number'}
              aria-invalid={!isText(x.field) && x.value.trim() !== '' && !Number.isFinite(Number(x.value))}
              title={!isText(x.field) && x.value.trim() !== '' && !Number.isFinite(Number(x.value)) ? 'Not a number; this filter is ignored' : undefined}
              onChange={(e) => update(x.id, { value: e.target.value })}
              aria-label="Value"
            />
            <button type="button" className={f.x} title="Remove filter" onClick={() => setFilters((fs) => fs.filter((y) => y.id !== x.id))}>
              ×
            </button>
          </div>
        ))}
        <div className={f.row}>
          <Button type="button" variant="ghost" onClick={addFilter} disabled={!meta.data}>
            + Filter
          </Button>
          <span className={f.label}>Limit</span>
          <select className={f.select} value={limit} onChange={(e) => setLimit(Number(e.target.value))} aria-label="Limit">
            {[50, 100, 250, 500].map((n) => (
              <option key={n}>{n}</option>
            ))}
          </select>
          <Button type="submit" variant="primary">
            Run
          </Button>
          <span className={s.keys}>
            <kbd className="key-go">GO</kbd> DES · <kbd>g</kbd> GP · <kbd>w</kbd> watchlist
          </span>
        </div>
      </form>

      <div className={s.results}>
        {res.isPending ? (
          <Skeleton rows={10} columns={6} />
        ) : res.isError ? (
          <ErrorState error={res.error} onRetry={() => res.refetch()} title="Screen failed" />
        ) : (
          <GridTable
            columns={columns}
            rows={res.data.rows}
            rowKey={(r) => r.code}
            autoFocus
            onOpen={(r) => pane.open({ code: 'DES', ticker: r.code, args: [] })}
            onRowKey={(e, r) => {
              if (e.altKey || e.shiftKey) return false
              if (e.key === 'w') {
                window.dispatchEvent(new CustomEvent('watchlist:add', { detail: { ticker: r.code } }))
                return true
              }
              if (e.key === 'g') {
                pane.open({ code: 'GP', ticker: r.code, args: [] })
                return true
              }
              return false
            }}
            empty={<Empty title="No matches" message="Loosen a filter or pick another preset." />}
          />
        )}
      </div>
    </Panel>
  )
}
