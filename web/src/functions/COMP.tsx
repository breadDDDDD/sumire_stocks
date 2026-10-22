import { useQueries } from '@tanstack/react-query'
import { useState, type FormEvent } from 'react'
import { api } from '../api/client'
import { Button, Empty, ErrorState, Num, Panel, Skeleton } from '../components/ui'
import { usePane } from '../shell/workspace'
import { alignCloses, annualVolPct, correlation, logReturns, maxDrawdownPct, rebase, SERIES_COLORS, type Bar } from './comp/math'
import { PerfChart } from './comp/PerfChart'
import f from './eqs/form.module.css'
import type { FunctionProps } from './types'
import s from './comp/COMP.module.css'

interface Chart {
  code: string
  bars: Bar[]
}

interface Profile {
  code: string
  name: string
  sector: string | null
  keyStats: {
    last: number | null
    marketCap: number | null
    pe: number | null
    pb: number | null
    divYield: number | null
    roe: number | null
    beta: number | null
    high52w: number | null
    low52w: number | null
    avgValueTraded20: number | null
  } | null
}

const RANGES = ['3m', '6m', '1y', '3y'] as const
const MAX = 4

export default function COMP({ ticker, args }: FunctionProps) {
  const pane = usePane()
  const initial = [...new Set([ticker, ...args].filter(Boolean).map((t) => (t as string).toUpperCase()))].slice(0, MAX)
  const [codes, setCodes] = useState<string[]>(initial)
  const [range, setRange] = useState<(typeof RANGES)[number]>('1y')
  const [input, setInput] = useState('')

  const charts = useQueries({
    queries: codes.map((c) => ({
      queryKey: ['chart', c, range, '1d'],
      queryFn: ({ signal }: { signal: AbortSignal }) => api<Chart>(`/chart/${c}?range=${range}&interval=1d`, { signal }),
    })),
  })
  const profiles = useQueries({
    queries: codes.map((c) => ({
      queryKey: ['profile', c],
      queryFn: ({ signal }: { signal: AbortSignal }) => api<Profile>(`/stocks/${c}/profile`, { signal }),
      retry: 0,
    })),
  })

  const ready = charts.length > 0 && charts.every((q) => q.data)
  const failed = charts.find((q) => q.isError)
  const chartData = charts.map((q) => q.data)

  // a few thousand points at most: cheap enough to recompute on render
  const data = ready ? analyse(chartData.map((c) => c?.bars ?? [])) : null

  const add = (e: FormEvent) => {
    e.preventDefault()
    const c = input.trim().toUpperCase()
    if (c && !codes.includes(c) && codes.length < MAX) setCodes([...codes, c])
    setInput('')
  }

  const toolbar = (
    <div className={f.toolbar}>
      <div className={f.row}>
        {codes.map((c, k) => (
          <span key={c} className={s.tag} style={{ borderColor: SERIES_COLORS[k] }}>
            <button className={s.tagCode} style={{ color: SERIES_COLORS[k] }} onClick={() => pane.open({ code: 'DES', ticker: c, args: [] })}>
              {c}
            </button>
            <button className={f.x} title={`Remove ${c}`} onClick={() => setCodes(codes.filter((x) => x !== c))}>
              ×
            </button>
          </span>
        ))}
        {codes.length < MAX && (
          <form onSubmit={add} className={f.row}>
            <input
              className={`${f.input} ${f.code}`}
              placeholder="Add ticker"
              value={input}
              onChange={(e) => setInput(e.target.value.toUpperCase())}
              autoFocus={codes.length === 0}
              aria-label="Add ticker"
            />
          </form>
        )}
        <span className={s.spacer} />
        {RANGES.map((r) => (
          <Button key={r} variant="ghost" active={r === range} onClick={() => setRange(r)}>
            {r}
          </Button>
        ))}
      </div>
    </div>
  )

  return (
    <Panel title="COMP" subtitle={codes.join(' · ') || 'Compare'} bodyClassName={s.body}>
      {toolbar}
      {codes.length < 1 ? (
        <Empty title="Pick tickers" message="Type up to 4 tickers above, or run COMP BBCA BBRI BMRI." />
      ) : failed ? (
        <ErrorState error={failed.error} onRetry={() => failed.refetch()} />
      ) : !data ? (
        <Skeleton rows={8} columns={4} />
      ) : data.dates.length < 2 ? (
        <Empty title="No overlapping history" message="These tickers have no common trading days in this range." />
      ) : (
        <div className={s.content} tabIndex={0}>
          <PerfChart dates={data.dates} series={data.rebased} names={codes} />

          <div className={s.lower}>
            <section>
              <h3 className={s.h}>Key stats</h3>
              <table className={s.stats}>
                <thead>
                  <tr>
                    <th />
                    {codes.map((c, k) => (
                      <th key={c} style={{ color: SERIES_COLORS[k] }}>
                        {c}
                      </th>
                    ))}
                  </tr>
                </thead>
                <tbody>
                  <StatRow label="Last" values={codes.map((_, k) => data.closes[k].at(-1) ?? null)} format="price" />
                  <StatRow label={`Return ${range}`} values={data.rebased.map((r) => (r.at(-1) ?? 100) - 100)} format="pct" signed colored />
                  <StatRow label="Volatility" values={data.vol} format="pct" decimals={1} />
                  <StatRow label="Max drawdown" values={data.dd} format="pct" decimals={1} colored />
                  <StatRow label="Market cap" values={profiles.map((p) => p.data?.keyStats?.marketCap ?? null)} format="compact" />
                  <StatRow label="P/E" values={profiles.map((p) => p.data?.keyStats?.pe ?? null)} decimals={1} />
                  <StatRow label="P/B" values={profiles.map((p) => p.data?.keyStats?.pb ?? null)} decimals={2} />
                  <StatRow
                    label="Div yield"
                    values={profiles.map((p) => (p.data?.keyStats?.divYield != null ? p.data.keyStats.divYield * 100 : null))}
                    format="pct"
                    decimals={1}
                  />
                  <StatRow
                    label="ROE"
                    values={profiles.map((p) => (p.data?.keyStats?.roe != null ? p.data.keyStats.roe * 100 : null))}
                    format="pct"
                    decimals={1}
                  />
                  <StatRow label="Beta" values={profiles.map((p) => p.data?.keyStats?.beta ?? null)} decimals={2} />
                  <StatRow label="Avg value 20d" values={profiles.map((p) => p.data?.keyStats?.avgValueTraded20 ?? null)} format="compact" />
                </tbody>
              </table>
            </section>

            {codes.length > 1 && (
              <section>
                <h3 className={s.h}>Correlation (daily returns)</h3>
                <table className={s.corr}>
                  <thead>
                    <tr>
                      <th />
                      {codes.map((c) => (
                        <th key={c}>{c}</th>
                      ))}
                    </tr>
                  </thead>
                  <tbody>
                    {codes.map((a, i) => (
                      <tr key={a}>
                        <th>{a}</th>
                        {codes.map((b, j) => {
                          const v = data.corr[i][j]
                          return (
                            <td
                              key={b}
                              style={{ background: v == null || i === j ? undefined : `rgba(255, 176, 0, ${Math.abs(v) * 0.45})` }}
                              className={v != null && v < 0 ? 'down' : undefined}
                            >
                              {v == null ? '—' : v.toFixed(2)}
                            </td>
                          )
                        })}
                      </tr>
                    ))}
                  </tbody>
                </table>
                <p className={s.note}>{data.dates.length} common sessions.</p>
              </section>
            )}
          </div>
        </div>
      )}
    </Panel>
  )
}

function analyse(bars: Bar[][]) {
  const { dates, closes } = alignCloses(bars)
  const rets = closes.map(logReturns)
  return {
    dates,
    closes,
    rebased: closes.map(rebase),
    corr: rets.map((a) => rets.map((b) => correlation(a, b))),
    vol: rets.map(annualVolPct),
    dd: closes.map(maxDrawdownPct),
  }
}

function StatRow({
  label,
  values,
  ...fmt
}: {
  label: string
  values: (number | null)[]
  format?: 'price' | 'pct' | 'compact' | 'number'
  decimals?: number
  signed?: boolean
  colored?: boolean
}) {
  return (
    <tr>
      <th>{label}</th>
      {values.map((v, k) => (
        <td key={k}>
          <Num value={v} {...fmt} />
        </td>
      ))}
    </tr>
  )
}
