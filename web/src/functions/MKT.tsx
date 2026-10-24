import { useQuery } from '@tanstack/react-query'
import { api } from '../api/client'
import { Badge, ErrorState, FreshnessBadge, Num, Panel, Skeleton, type BadgeTone } from '../components/ui'
import { usePane } from '../shell/workspace'
import { MoversTable } from './mkt/MoversTable'
import { MACRO_LABELS, useLiveQuotes, type Mover, type Quote } from './mkt/quotes'
import { Sparkline } from './mkt/Sparkline'
import type { FunctionProps } from './types'
import s from './mkt/MKT.module.css'

interface Breadth {
  date: string
  total: number
  advancers: number
  decliners: number
  unchanged: number
  pctAboveSma50: number
  pctAboveSma200: number
  newHighs: number
  newLows: number
}

interface Overview {
  asOf: string
  time: string
  ihsg: Quote | null
  lq45: Quote | null
  regime: { label: string; score: number; reasons: string[] } | null
  breadth: Breadth | null
  gainers: Mover[]
  losers: Mover[]
  mostActive: Mover[]
  macro: Quote[]
  ihsgForecast: {
    asOf: string
    last: number
    horizons: { horizon: number; p10: number; p50: number; p90: number; probUp: number }[]
    annualizedVol: number | null
    volPercentile: number | null
  } | null
  moversSource: string
}

interface Chart {
  prevClose: number | null
  bars: { time: number; close: number }[]
}

const REGIME_TONE: Record<string, BadgeTone> = { 'Risk-on': 'up', 'Risk-off': 'down', Neutral: 'amber' }

export default function MKT(_: FunctionProps) {
  const q = useQuery({
    queryKey: ['market', 'overview'],
    queryFn: ({ signal }) => api<Overview>('/market/overview', { signal }),
    refetchInterval: 60_000,
  })
  const spark = useQuery({
    queryKey: ['chart', 'IHSG', '1d', '5m'],
    queryFn: ({ signal }) => api<Chart>('/chart/IHSG?range=1d&interval=5m', { signal }),
    refetchInterval: 120_000,
    retry: 0,
  })
  const macroCodes = q.data?.macro.map((m) => m.code) ?? []
  const live = useLiveQuotes(['IHSG', 'LQ45', ...macroCodes])

  const d = q.data
  const ihsg = live.IHSG ?? d?.ihsg ?? null
  const lq45 = live.LQ45 ?? d?.lq45 ?? null

  return (
    <Panel
      title="MKT"
      subtitle="Market overview"
      actions={<FreshnessBadge asOf={ihsg?.time ?? d?.time} />}
      bodyClassName={s.body}
    >
      {q.isPending ? (
        <Skeleton rows={10} columns={5} />
      ) : q.isError || !d ? (
        <ErrorState error={q.error} onRetry={() => q.refetch()} />
      ) : (
        <div className={s.grid} tabIndex={-1}>
          <section className={`${s.card} ${s.hero}`}>
            <div className={s.heroTop}>
              <div>
                <div className={s.label}>IHSG</div>
                <div className={s.heroPrice}>
                  <Num value={ihsg?.price} format="price" decimals={2} />
                </div>
                <div className={s.heroChange}>
                  <Num value={ihsg?.change} format="number" decimals={2} signed colored arrow />{' '}
                  <Num value={ihsg?.changePct} format="pct" signed colored />
                </div>
                {lq45 && (
                  <div className={s.sub}>
                    LQ45 <Num value={lq45.price} format="price" decimals={2} />{' '}
                    <Num value={lq45.changePct} format="pct" signed colored />
                  </div>
                )}
              </div>
              {d.regime && (
                <div className={s.regime}>
                  <Badge tone={REGIME_TONE[d.regime.label] ?? 'default'} title={`Score ${d.regime.score} of ±4`}>
                    {d.regime.label}
                  </Badge>
                  <ul className={s.reasons}>
                    {d.regime.reasons.map((r) => (
                      <li key={r}>{r}</li>
                    ))}
                  </ul>
                </div>
              )}
            </div>
            <div className={s.spark}>
              {spark.data && spark.data.bars.length > 1 ? (
                <Sparkline values={spark.data.bars.map((b) => b.close)} baseline={spark.data.prevClose} />
              ) : (
                <span className="muted">{spark.isPending ? 'Loading intraday…' : 'No intraday data'}</span>
              )}
            </div>
          </section>

          <section className={`${s.card} ${s.breadth}`}>
            <h3 className={s.h}>Breadth</h3>
            {d.breadth ? <BreadthView b={d.breadth} /> : <p className="muted">No breadth yet.</p>}
          </section>

          <section className={`${s.card} ${s.fcst}`}>
            <h3 className={s.h}>IHSG forecast cone</h3>
            {d.ihsgForecast ? <Cone f={d.ihsgForecast} /> : <p className="muted">No forecast yet.</p>}
          </section>

          <section className={`${s.card} ${s.macro}`}>
            <h3 className={s.h}>Macro</h3>
            <MacroStrip quotes={d.macro.map((m) => live[m.code] ?? m)} />
          </section>

          <section className={`${s.card} ${s.movers}`}>
            <h3 className={s.h}>
              Top gainers <span className={s.hint}>Enter DES · Shift+Enter GP</span>
            </h3>
            <MoversTable rows={d.gainers} />
          </section>
          <section className={`${s.card} ${s.movers}`}>
            <h3 className={s.h}>Top losers</h3>
            <MoversTable rows={d.losers} />
          </section>
          <section className={`${s.card} ${s.movers}`}>
            <h3 className={s.h}>Most active (value)</h3>
            <MoversTable rows={d.mostActive} />
          </section>
        </div>
      )}
    </Panel>
  )
}

function Bar({ label, pct, tone, value }: { label: string; pct: number; tone: 'up' | 'down' | 'amber'; value: string }) {
  return (
    <div className={s.barRow}>
      <span className={s.barLabel}>{label}</span>
      <span className={s.barTrack}>
        <span className={`${s.barFill} ${s[tone]}`} style={{ width: `${Math.max(0, Math.min(100, pct))}%` }} />
      </span>
      <span className={s.barValue}>{value}</span>
    </div>
  )
}

function BreadthView({ b }: { b: Breadth }) {
  const total = b.advancers + b.decliners + b.unchanged || 1
  return (
    <div className={s.breadthBody}>
      <div className={s.advdec} title={`${b.advancers} up · ${b.unchanged} flat · ${b.decliners} down`}>
        <span className={s.up} style={{ flex: b.advancers / total }} />
        <span className={s.flatSeg} style={{ flex: b.unchanged / total }} />
        <span className={s.down} style={{ flex: b.decliners / total }} />
      </div>
      <div className={s.advdecLegend}>
        <span className="up">▲ {b.advancers}</span>
        <span className="muted">= {b.unchanged}</span>
        <span className="down">▼ {b.decliners}</span>
      </div>
      <Bar label="> 50-day MA" pct={b.pctAboveSma50} tone={b.pctAboveSma50 >= 50 ? 'up' : 'down'} value={`${b.pctAboveSma50.toFixed(0)}%`} />
      <Bar label="> 200-day MA" pct={b.pctAboveSma200} tone={b.pctAboveSma200 >= 50 ? 'up' : 'down'} value={`${b.pctAboveSma200.toFixed(0)}%`} />
      <div className={s.hilo}>
        <span>
          52w highs <b className="up">{b.newHighs}</b>
        </span>
        <span>
          52w lows <b className="down">{b.newLows}</b>
        </span>
        <span className="muted">of {b.total}</span>
      </div>
    </div>
  )
}

function Cone({ f }: { f: NonNullable<Overview['ihsgForecast']> }) {
  const lo = Math.min(...f.horizons.map((h) => h.p10))
  const hi = Math.max(...f.horizons.map((h) => h.p90))
  const span = hi - lo || 1
  const pos = (v: number) => `${((v - lo) / span) * 100}%`
  return (
    <div className={s.cone}>
      {f.horizons.map((h) => (
        <div key={h.horizon} className={s.coneRow}>
          <span className={s.coneH}>{h.horizon}d</span>
          <span className={s.coneTrack} title={`80% range ${h.p10.toFixed(0)} – ${h.p90.toFixed(0)}, median ${h.p50.toFixed(0)}`}>
            <span className={s.coneBand} style={{ left: pos(h.p10), width: `${((h.p90 - h.p10) / span) * 100}%` }} />
            <span className={s.coneMid} style={{ left: pos(h.p50) }} />
            <span className={s.coneLast} style={{ left: pos(f.last) }} />
          </span>
          <span className={s.coneNums}>
            <Num value={h.p50} format="price" decimals={0} colorBy={h.p50 - f.last} />
            <span className="muted"> P(up) </span>
            <Num value={h.probUp * 100} format="pct" decimals={0} />
          </span>
        </div>
      ))}
      <p className={s.coneNote}>
        Bands are the 10–90% range; white tick = last. Vol {f.annualizedVol != null ? `${(f.annualizedVol * 100).toFixed(0)}%` : '—'}
        {f.volPercentile != null && ` (${f.volPercentile.toFixed(0)}th pct)`}.
      </p>
    </div>
  )
}

function MacroStrip({ quotes }: { quotes: Quote[] }) {
  const pane = usePane()
  if (!quotes.length) return <p className="muted">No macro quotes.</p>
  return (
    <div className={s.macroStrip}>
      {quotes.map((m) => (
        <button key={m.code} className={s.macroItem} onClick={() => pane.open({ code: 'GP', ticker: m.code, args: [] })}>
          <span className={s.macroCode}>{MACRO_LABELS[m.code] ?? m.code}</span>
          <Num value={m.price} format="price" />
          <Num value={m.changePct} format="pct" signed colored />
        </button>
      ))}
    </div>
  )
}
