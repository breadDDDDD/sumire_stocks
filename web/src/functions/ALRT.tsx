import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState, type FormEvent } from 'react'
import { api, ApiError } from '../api/client'
import { Badge, Button, Empty, ErrorState, Panel, Skeleton, type Column } from '../components/ui'
import { formatDateWib, formatTimeWib } from '../lib/time'
import { usePane } from '../shell/workspace'
import { ALERT_TYPES, describeParams, typeLabel, type Alert, type FiredAlert } from './alrt/alerts'
import { GridTable } from './eqs/GridTable'
import f from './eqs/form.module.css'
import type { FunctionProps } from './types'
import s from './alrt/ALRT.module.css'

const when = (t: string | null) => (t ? `${formatDateWib(t)} ${formatTimeWib(t)}` : '—')

interface Draft {
  code: string
  type: string
  price: string
  pct: string
  value: string
  fast: string
  slow: string
  direction: string
  minConfidence: string
  maxSentiment: string
}

function paramsOf(d: Draft): { params: Record<string, unknown>; error?: string } {
  const num = (v: string, what: string) => {
    const x = Number(v)
    return v.trim() === '' || !Number.isFinite(x) ? { error: `Enter a ${what}` } : { x }
  }
  switch (d.type) {
    case 'price_above':
    case 'price_below': {
      const r = num(d.price, 'price')
      return r.error ? { params: {}, error: r.error } : { params: { price: r.x } }
    }
    case 'pct_change': {
      const r = num(d.pct, 'percentage')
      return r.error ? { params: {}, error: r.error } : { params: { pct: r.x } }
    }
    case 'rsi_above':
    case 'rsi_below': {
      const r = num(d.value, 'RSI level')
      return r.error ? { params: {}, error: r.error } : { params: { value: r.x } }
    }
    case 'ma_cross':
      return { params: { fast: d.fast, slow: d.slow, direction: d.direction } }
    case 'negative_insight': {
      const r = num(d.maxSentiment, 'max sentiment')
      return r.error ? { params: {}, error: r.error } : { params: { minConfidence: d.minConfidence, maxSentiment: r.x } }
    }
    default:
      return { params: {} }
  }
}

export default function ALRT({ ticker }: FunctionProps) {
  const pane = usePane()
  const qc = useQueryClient()
  const alerts = useQuery({ queryKey: ['alerts'], queryFn: ({ signal }) => api<Alert[]>('/alerts', { signal }) })
  const recent = useQuery({
    queryKey: ['alerts', 'recent'],
    queryFn: ({ signal }) => api<FiredAlert[]>('/alerts/recent', { signal }),
    refetchInterval: 60_000,
    retry: 0,
  })
  const [d, setD] = useState<Draft>({
    code: ticker ?? '',
    type: 'price_above',
    price: '',
    pct: '3',
    value: '70',
    fast: 'price',
    slow: 'sma200',
    direction: 'any',
    minConfidence: 'high',
    maxSentiment: '-0.3',
  })
  const [formError, setFormError] = useState<string | null>(null)
  const set = (patch: Partial<Draft>) => setD((x) => ({ ...x, ...patch }))
  const refresh = () => qc.invalidateQueries({ queryKey: ['alerts'] })

  const create = useMutation({
    mutationFn: (body: { code: string | null; type: string; params: Record<string, unknown>; active: boolean }) =>
      api<Alert>('/alerts', { method: 'POST', body }),
    onSuccess: () => {
      refresh()
      set({ price: '' })
    },
  })
  const toggle = useMutation({
    mutationFn: (a: Alert) => api<Alert>(`/alerts/${a.id}`, { method: 'PUT', body: { active: !a.active } }),
    onSuccess: refresh,
  })
  const remove = useMutation({
    mutationFn: (id: number) => api<void>(`/alerts/${id}`, { method: 'DELETE' }),
    onSuccess: refresh,
  })

  const submit = (e: FormEvent) => {
    e.preventDefault()
    const code = d.code.trim().toUpperCase()
    if (!code && d.type !== 'negative_insight') return setFormError('Enter a ticker')
    const { params, error } = paramsOf(d)
    if (error) return setFormError(error)
    setFormError(null)
    create.mutate({ code: code || null, type: d.type, params, active: true })
  }

  const mutError = [create.error, toggle.error, remove.error].find(Boolean)
  const shownError = formError ?? (mutError ? (mutError instanceof ApiError ? mutError.message : 'Request failed') : null)

  const columns: Column<Alert>[] = [
    { key: 'code', header: 'Code', render: (a) => <span className={s.code}>{a.code ?? 'Watchlist'}</span> },
    { key: 'type', header: 'Type', render: (a) => typeLabel(a.type) },
    { key: 'params', header: 'Condition', sortable: false, render: (a) => <span className="muted">{describeParams(a)}</span> },
    {
      key: 'active',
      header: 'State',
      render: (a) => (
        <button
          className={s.toggle}
          title={a.active ? 'Pause (space)' : 'Resume (space)'}
          onClick={(e) => {
            e.stopPropagation()
            toggle.mutate(a)
          }}
        >
          <Badge tone={a.active ? 'up' : 'muted'}>{a.active ? 'On' : 'Off'}</Badge>
        </button>
      ),
      sortValue: (a) => (a.active ? 1 : 0),
    },
    { key: 'lastTriggeredAt', header: 'Last fired', align: 'right', render: (a) => when(a.lastTriggeredAt) },
    { key: 'createdAt', header: 'Created', align: 'right', render: (a) => when(a.createdAt) },
    {
      key: 'x',
      header: '',
      sortable: false,
      width: 24,
      render: (a) => (
        <button
          className={f.x}
          title="Delete (Del)"
          onClick={(e) => {
            e.stopPropagation()
            remove.mutate(a.id)
          }}
        >
          ×
        </button>
      ),
    },
  ]

  const numInput = (key: 'price' | 'pct' | 'value' | 'maxSentiment', label: string, step = 'any') => (
    <label className={f.row}>
      <span className={f.label}>{label}</span>
      <input
        className={`${f.input} ${f.num}`}
        type="number"
        step={step}
        value={d[key]}
        onChange={(e) => set({ [key]: e.target.value })}
      />
    </label>
  )
  const pick = (key: 'fast' | 'slow' | 'direction' | 'minConfidence', label: string, options: string[]) => (
    <label className={f.row}>
      <span className={f.label}>{label}</span>
      <select className={f.select} value={d[key]} onChange={(e) => set({ [key]: e.target.value })}>
        {options.map((o) => (
          <option key={o}>{o}</option>
        ))}
      </select>
    </label>
  )

  return (
    <Panel title="ALRT" subtitle="Alerts" bodyClassName={s.body}>
      <form className={f.toolbar} onSubmit={submit}>
        <div className={f.row}>
          <input
            className={`${f.input} ${f.code}`}
            placeholder={d.type === 'negative_insight' ? 'All' : 'Ticker'}
            value={d.code}
            onChange={(e) => set({ code: e.target.value.toUpperCase() })}
            aria-label="Ticker"
          />
          <select className={f.select} value={d.type} onChange={(e) => set({ type: e.target.value })} aria-label="Alert type">
            {ALERT_TYPES.map((t) => (
              <option key={t.type} value={t.type}>
                {t.label}
              </option>
            ))}
          </select>
          {(d.type === 'price_above' || d.type === 'price_below') && numInput('price', 'Price')}
          {d.type === 'pct_change' && numInput('pct', '%')}
          {(d.type === 'rsi_above' || d.type === 'rsi_below') && numInput('value', 'RSI')}
          {d.type === 'ma_cross' && (
            <>
              {pick('fast', 'Fast', ['price', 'sma20', 'sma50'])}
              {pick('slow', 'Slow', ['sma50', 'sma200'])}
              {pick('direction', 'Dir', ['any', 'above', 'below'])}
            </>
          )}
          {d.type === 'negative_insight' && (
            <>
              {pick('minConfidence', 'Min conf.', ['high', 'med'])}
              {numInput('maxSentiment', 'Max sent.', '0.1')}
            </>
          )}
          <Button type="submit" variant="primary" disabled={create.isPending}>
            Add alert
          </Button>
        </div>
        {d.type === 'negative_insight' && <div className={s.hint}>Leave the ticker empty to watch every watchlist stock.</div>}
        {shownError && <div className={f.error}>{shownError}</div>}
      </form>

      <div className={s.list}>
        {alerts.isPending ? (
          <Skeleton rows={6} columns={5} />
        ) : alerts.isError ? (
          <ErrorState error={alerts.error} onRetry={() => alerts.refetch()} title="Alerts unavailable" />
        ) : (
          <GridTable
            columns={columns}
            rows={alerts.data}
            rowKey={(a) => String(a.id)}
            initialSort={{ key: 'createdAt', dir: 'desc' }}
            onOpen={(a) => a.code && pane.open({ code: 'DES', ticker: a.code, args: [] })}
            onRowKey={(e, a) => {
              if (e.key === 'Delete' || e.key === 'Backspace') {
                remove.mutate(a.id)
                return true
              }
              if (e.key === ' ') {
                toggle.mutate(a)
                return true
              }
              return false
            }}
            empty={<Empty title="No alerts" message="Add one above. Alerts fire at most once per day and show up as a toast." />}
          />
        )}
      </div>

      <section className={s.recent}>
        <h3 className={s.h}>Fired since server start</h3>
        {recent.data?.length ? (
          <ul className={s.fired}>
            {recent.data.map((r, i) => (
              <li key={`${r.id}-${r.time}-${i}`}>
                <span className="muted">{formatTimeWib(r.time)}</span> {r.message}
              </li>
            ))}
          </ul>
        ) : (
          <p className="muted">{recent.isError ? 'Unavailable.' : 'Nothing fired yet.'}</p>
        )}
      </section>
    </Panel>
  )
}
