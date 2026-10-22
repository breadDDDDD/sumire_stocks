import { useState, type KeyboardEvent } from 'react'
import { Badge, Button, Empty, ErrorState, FreshnessBadge, formatNum, Num, Panel, Skeleton } from '../components/ui'
import { useFundamentals, type Fundamentals, type Ratios, type Statement } from './fa/api'
import s from './fa/FA.module.css'
import type { FunctionProps } from './types'

type Kind = 'ratio' | 'pct' | 'price' | 'idr'
interface RatioRow {
  key: keyof Ratios
  label: string
  kind: Kind
  median?: 'pe' | 'pb' | 'roe' | 'divYield'
  /** valuation: lower than the sector is cheap; quality: higher than the sector is better */
  compare?: 'valuation' | 'quality'
}

const RATIOS: RatioRow[] = [
  { key: 'pe', label: 'P/E', kind: 'ratio', median: 'pe', compare: 'valuation' },
  { key: 'pb', label: 'P/B', kind: 'ratio', median: 'pb', compare: 'valuation' },
  { key: 'divYield', label: 'Dividend yield', kind: 'pct', median: 'divYield', compare: 'quality' },
  { key: 'roe', label: 'ROE', kind: 'pct', median: 'roe', compare: 'quality' },
  { key: 'npm', label: 'Net margin', kind: 'pct' },
  { key: 'der', label: 'Debt / equity', kind: 'ratio' },
  { key: 'eps', label: 'EPS', kind: 'price' },
  { key: 'revenueGrowth', label: 'Revenue growth', kind: 'pct' },
  { key: 'earningsGrowth', label: 'Earnings growth', kind: 'pct' },
  { key: 'marketCap', label: 'Market cap', kind: 'idr' },
]

const TABS = [
  { id: 'income', label: 'Income', key: 'i' },
  { id: 'balance', label: 'Balance sheet', key: 'b' },
  { id: 'cashflow', label: 'Cash flow', key: 'c' },
] as const
type Tab = (typeof TABS)[number]['id']

export default function FA({ ticker }: FunctionProps) {
  const code = (ticker ?? '').toUpperCase()
  const q = useFundamentals(code)
  const [tab, setTab] = useState<Tab>('income')
  const [quarterly, setQuarterly] = useState(false)

  const onKeyDown = (e: KeyboardEvent) => {
    if (e.altKey || e.ctrlKey || e.metaKey) return
    const t = TABS.find((x) => x.key === e.key)
    if (t) {
      e.preventDefault()
      setTab(t.id)
    } else if (e.key === 'p' && tab === 'income') {
      e.preventDefault()
      setQuarterly((v) => !v)
    }
  }

  return (
    <Panel
      title={`${code} FA`}
      subtitle={q.data?.sector ? `Fundamentals · ${q.data.sector}` : 'Fundamentals'}
      actions={q.data?.updatedAt ? <FreshnessBadge asOf={q.data.updatedAt} label="Updated" staleAfterSec={8 * 86400} /> : null}
    >
      {!code ? (
        <Empty title="No ticker" message="Try TLKM FA." />
      ) : q.isPending ? (
        <Skeleton rows={12} columns={4} />
      ) : q.isError ? (
        <ErrorState error={q.error} onRetry={() => q.refetch()} />
      ) : (
        <div className={s.body} tabIndex={0} onKeyDown={onKeyDown} aria-label={`${code} fundamentals`}>
          <RatioTable data={q.data} />
          <section className={s.section}>
            <div className={s.tabs} role="tablist">
              {TABS.map((t) => (
                <Button
                  key={t.id}
                  role="tab"
                  aria-selected={tab === t.id}
                  active={tab === t.id}
                  variant="ghost"
                  onClick={() => setTab(t.id)}
                  title={`${t.label} (${t.key})`}
                >
                  {t.label}
                </Button>
              ))}
              {tab === 'income' && (
                <span className={s.period}>
                  <Button active={!quarterly} variant="ghost" onClick={() => setQuarterly(false)} title="Toggle with p">
                    Annual
                  </Button>
                  <Button active={quarterly} variant="ghost" onClick={() => setQuarterly(true)} title="Toggle with p">
                    Quarterly
                  </Button>
                </span>
              )}
            </div>
            {q.data.statementsError && <p className={s.warn}>Statements unavailable: {q.data.statementsError}</p>}
            <StatementTable rows={pickStatements(q.data, tab, quarterly)} />
          </section>
        </div>
      )}
    </Panel>
  )
}

function pickStatements(d: Fundamentals, tab: Tab, quarterly: boolean): Statement[] {
  const st = d.statements
  if (!st) return []
  if (tab === 'income') return quarterly ? st.incomeQuarterly : st.incomeAnnual
  return tab === 'balance' ? st.balanceSheet : st.cashflow
}

function fmt(v: number | null | undefined, kind: Kind) {
  if (v == null || !Number.isFinite(v)) return 'n/a'
  switch (kind) {
    case 'pct':
      return formatNum(v * 100, 'pct', { decimals: 1 })
    case 'ratio':
      return `${formatNum(v, 'number', { decimals: 2 })}×`
    case 'idr':
      return `Rp ${formatNum(v, 'compact')}`
    default:
      return formatNum(v, 'price')
  }
}

function verdict(row: RatioRow, v: number | null, m: number | null | undefined) {
  if (!row.compare || v == null || m == null || m <= 0) return null
  if (row.compare === 'valuation' && v <= 0) return { text: 'Loss', tone: 'muted' as const }
  const r = v / m
  if (row.compare === 'valuation') {
    if (r < 0.8) return { text: 'Cheap', tone: 'up' as const }
    if (r > 1.2) return { text: 'Expensive', tone: 'down' as const }
  } else {
    if (r > 1.2) return { text: 'Above', tone: 'up' as const }
    if (r < 0.8) return { text: 'Below', tone: 'down' as const }
  }
  return { text: 'In line', tone: 'muted' as const }
}

function RatioTable({ data }: { data: Fundamentals }) {
  const r = data.ratios
  const m = data.sectorMedians
  if (!r) {
    return <Empty title="No ratios yet" message="Yahoo has no fundamentals for this ticker and nothing is stored." />
  }
  return (
    <section className={s.section}>
      <h3 className={s.h}>
        Ratios vs sector median
        {m ? <span className="muted"> · {m.n} peers</span> : <span className="muted"> · no sector medians yet</span>}
      </h3>
      <table className={s.table}>
        <thead>
          <tr>
            <th>Metric</th>
            <th className={s.num}>{data.code}</th>
            <th className={s.num}>Sector</th>
            <th className={s.num}>Diff</th>
            <th />
          </tr>
        </thead>
        <tbody>
          {RATIOS.map((row) => {
            const v = r[row.key]
            const med = row.median ? m?.[row.median] : undefined
            const vd = verdict(row, v, med)
            const diff = v != null && med != null && med > 0 && row.compare ? (v / med - 1) * 100 : null
            return (
              <tr key={row.key} className={vd?.tone === 'up' ? s.good : vd?.tone === 'down' ? s.bad : undefined}>
                <td className={s.metric}>{row.label}</td>
                <td className={`${s.num} ${v == null ? s.na : 'value'}`}>{fmt(v, row.kind)}</td>
                <td className={`${s.num} muted`}>{row.median ? fmt(med, row.kind) : ''}</td>
                <td className={s.num}>{diff == null ? '' : <Num value={diff} format="pct" decimals={0} signed />}</td>
                <td>{vd && <Badge tone={vd.tone}>{vd.text}</Badge>}</td>
              </tr>
            )
          })}
        </tbody>
      </table>
    </section>
  )
}

function humanize(key: string) {
  const t = key.replace(/([a-z0-9])([A-Z])/g, '$1 $2').toLowerCase()
  return t.charAt(0).toUpperCase() + t.slice(1)
}

function StatementTable({ rows }: { rows: Statement[] }) {
  const periods = [...rows].sort((a, b) => b.endDate.localeCompare(a.endDate))
  const keys = [...new Set(periods.flatMap((p) => Object.keys(p.items ?? {})))]
  if (!periods.length || !keys.length) {
    return <p className={s.na}>n/a · Yahoo has no line items for this statement (common for IDX stocks).</p>
  }
  return (
    <div className={s.scroll}>
      <table className={s.table}>
        <thead>
          <tr>
            <th>Rp</th>
            {periods.map((p) => (
              <th key={p.endDate} className={s.num}>
                {p.endDate}
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          {keys.map((k) => (
            <tr key={k}>
              <td className={s.metric}>{humanize(k)}</td>
              {periods.map((p) => {
                const v = p.items?.[k]
                return (
                  <td key={p.endDate} className={`${s.num} ${v == null ? s.na : v < 0 ? 'down' : 'value'}`}>
                    {v == null ? 'n/a' : formatNum(v, 'compact', { decimals: 2 })}
                  </td>
                )
              })}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}
