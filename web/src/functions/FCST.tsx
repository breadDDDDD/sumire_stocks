import { Badge, Empty, ErrorState, formatNum, Num, Panel, Skeleton } from '../components/ui'
import { formatDateWib } from '../lib/time'
import { modelStyle, useForecast, type Forecast, type ModelForecast, type ScoreComponent } from './fcst/api'
import s from './fcst/FCST.module.css'
import type { FunctionProps } from './types'

const HORIZONS = [5, 20, 60]

export default function FCST({ ticker }: FunctionProps) {
  const code = (ticker ?? '').toUpperCase()
  const q = useForecast(code)

  return (
    <Panel
      title={`${code} FCST`}
      subtitle="Forecast ranges, signal score, track record"
      actions={q.data?.asOf ? <Badge tone="muted">As of {formatDateWib(q.data.asOf)}</Badge> : null}
    >
      {!code ? (
        <Empty title="No ticker" message="Try ASII FCST." />
      ) : q.isPending ? (
        <Skeleton rows={12} columns={5} />
      ) : q.isError ? (
        <ErrorState error={q.error} onRetry={() => q.refetch()} />
      ) : (
        <div className={s.body} tabIndex={0} aria-label={`${code} forecast`}>
          <Horizons data={q.data} />
          <div className={s.split}>
            <Score data={q.data} />
            <Garch data={q.data} />
          </div>
          <TrackRecord data={q.data} />
          <p className={s.disclaimer}>
            Statistical ranges from past prices, not financial advice. 80% of outcomes should land between P10 and P90 if
            the model is calibrated; check the coverage column.
          </p>
        </div>
      )}
    </Panel>
  )
}

const pctFrom = (v: number, last: number) => (v / last - 1) * 100

function Horizons({ data }: { data: Forecast }) {
  if (!data.models.length) return <Empty title="No forecasts yet" message="The baseline runs after the EOD pipeline." />
  return (
    <section className={s.section}>
      <h3 className={s.h}>
        Price ranges <span className="muted">· last {formatNum(data.last, 'price')}</span>
      </h3>
      <div className={s.cards}>
        {HORIZONS.map((h) => (
          <div key={h} className={s.card}>
            <div className={s.cardHead}>
              <span className="display">{h}D</span>
              <span className="muted">{h === 5 ? '1 week' : h === 20 ? '1 month' : '3 months'}</span>
            </div>
            {data.models.map((m) => (
              <ModelRow key={m.model} m={m} h={h} last={data.last} />
            ))}
          </div>
        ))}
      </div>
    </section>
  )
}

function ModelRow({ m, h, last }: { m: ModelForecast; h: number; last: number }) {
  const f = m.horizons.find((x) => x.horizon === h)
  const st = modelStyle(m.model)
  if (!f) return null
  return (
    <div className={s.model}>
      <span className={s.modelName} style={{ color: st.color }}>
        {st.name}
      </span>
      <div className={s.quantiles}>
        {(['p10', 'p50', 'p90'] as const).map((k) => (
          <div key={k} className={k === 'p50' ? s.qMid : s.q}>
            <span className={s.qLabel}>{k.toUpperCase()}</span>
            <Num value={f[k]} format="price" decimals={0} />
            <Num value={pctFrom(f[k], last)} format="pct" decimals={1} signed colored />
          </div>
        ))}
      </div>
      {f.probUp != null && <ProbGauge p={f.probUp} />}
    </div>
  )
}

function ProbGauge({ p }: { p: number }) {
  const pct = Math.round(p * 100)
  return (
    <div className={s.prob} title="Probability the price is higher at the horizon">
      <span className={s.qLabel}>P(up)</span>
      <span className={s.probTrack} role="meter" aria-valuemin={0} aria-valuemax={100} aria-valuenow={pct}>
        <span className={s.probFill} style={{ width: `${pct}%`, background: p >= 0.5 ? 'var(--up)' : 'var(--down)' }} />
        <span className={s.probMid} />
      </span>
      <span className={p >= 0.55 ? 'up' : p <= 0.45 ? 'down' : 'value'}>{pct}%</span>
    </div>
  )
}

function Score({ data }: { data: Forecast }) {
  const sc = data.score
  if (!sc || sc.total == null) {
    return (
      <section className={s.section}>
        <h3 className={s.h}>Signal score</h3>
        <p className="muted">No score yet.</p>
      </section>
    )
  }
  const pos = (sc.total + 100) / 2
  const tone = sc.total >= 20 ? 'up' : sc.total <= -20 ? 'down' : 'value'
  return (
    <section className={s.section}>
      <h3 className={s.h}>
        Signal score <span className="muted">· coverage {Math.round(sc.coverage * 100)}%</span>
      </h3>
      <div className={s.scoreHead}>
        <span className={`${s.scoreNum} ${tone}`}>{formatNum(sc.total, 'number', { decimals: 0, signed: true })}</span>
        <span className={`${s.scoreLabel} ${tone}`}>{sc.label}</span>
      </div>
      <div className={s.gauge} role="meter" aria-valuemin={-100} aria-valuemax={100} aria-valuenow={sc.total}>
        <span className={s.gaugeNeedle} style={{ left: `${pos}%` }} />
        <span className={s.gaugeTicks}>
          <span>-100</span>
          <span>0</span>
          <span>+100</span>
        </span>
      </div>
      <ul className={s.components}>
        {sc.components.map((c) => (
          <ComponentBar key={c.component} c={c} />
        ))}
      </ul>
    </section>
  )
}

function ComponentBar({ c }: { c: ScoreComponent }) {
  const v = c.score
  const width = v == null ? 0 : Math.abs(v) / 2
  return (
    <li className={s.comp} title={c.reason}>
      <span className={s.compName}>
        {c.component} <span className="muted">{c.weight}%</span>
      </span>
      <span className={s.compTrack}>
        {v != null && (
          <span
            className={s.compFill}
            style={{
              width: `${width}%`,
              left: v >= 0 ? '50%' : `${50 - width}%`,
              background: v >= 0 ? 'var(--up)' : 'var(--down)',
            }}
          />
        )}
        <span className={s.compMid} />
      </span>
      <span className={s.compScore}>
        {v == null ? <span className="muted">n/a</span> : <Num value={v} decimals={0} signed colored />}
      </span>
      <span className={s.compReason}>{c.reason}</span>
    </li>
  )
}

function Garch({ data }: { data: Forecast }) {
  const g = data.garch
  return (
    <section className={s.section}>
      <h3 className={s.h}>GARCH volatility range</h3>
      {!g ? (
        <p className="muted">Not enough history for GARCH.</p>
      ) : (
        <>
          <div className={s.garchStats}>
            <span>
              <span className={s.qLabel}>Vol (ann.)</span> <Num value={g.annualizedVol == null ? null : g.annualizedVol * 100} format="pct" decimals={1} />
            </span>
            <span>
              <span className={s.qLabel}>Long-run</span> <Num value={g.longRunVol == null ? null : g.longRunVol * 100} format="pct" decimals={1} />
            </span>
            <span>
              <span className={s.qLabel}>Percentile</span>{' '}
              <span className={g.volPercentile != null && g.volPercentile >= 80 ? 'down' : 'value'}>
                {g.volPercentile == null ? '—' : `${Math.round(g.volPercentile)}th`}
              </span>
            </span>
            {!g.converged && <Badge tone="muted">Fit did not converge</Badge>}
          </div>
          <table className={s.table}>
            <thead>
              <tr>
                <th>Horizon</th>
                <th className={`${s.num} ${s.sigma}`}>−2σ</th>
                <th className={`${s.num} ${s.sigma}`}>−1σ</th>
                <th className={`${s.num} ${s.sigma}`}>+1σ</th>
                <th className={`${s.num} ${s.sigma}`}>+2σ</th>
              </tr>
            </thead>
            <tbody>
              {g.cone.map((c) => (
                <tr key={c.horizon}>
                  <td className={s.metric}>{c.horizon}D</td>
                  <td className={`${s.num} down`}>{formatNum(c.minus2, 'price', { decimals: 0 })}</td>
                  <td className={`${s.num} down`}>{formatNum(c.minus1, 'price', { decimals: 0 })}</td>
                  <td className={`${s.num} up`}>{formatNum(c.plus1, 'price', { decimals: 0 })}</td>
                  <td className={`${s.num} up`}>{formatNum(c.plus2, 'price', { decimals: 0 })}</td>
                </tr>
              ))}
            </tbody>
          </table>
          <p className={s.hint}>±1σ ≈ 68% and ±2σ ≈ 95% of outcomes, no drift.</p>
        </>
      )}
    </section>
  )
}

function TrackRecord({ data }: { data: Forecast }) {
  const rows = [...data.trackRecord].sort(
    (a, b) =>
      (a.scope === 'all' ? 0 : 1) - (b.scope === 'all' ? 0 : 1) ||
      a.scope.localeCompare(b.scope) ||
      a.horizon - b.horizon ||
      (a.model === 'baseline' ? 0 : 1) - (b.model === 'baseline' ? 0 : 1),
  )
  const pct = (v: number | null) => (v == null ? '—' : formatNum(v * 100, 'pct', { decimals: 1 }))
  return (
    <section className={s.section}>
      <h3 className={s.h}>Track record</h3>
      {!rows.length ? (
        <p className="muted">No matured forecasts yet. The first EOD run backtests the last 12 months.</p>
      ) : (
        <div className={s.scroll}>
          <table className={s.table}>
            <thead>
              <tr>
                <th>Model</th>
                <th>Scope</th>
                <th className={s.num}>Horizon</th>
                <th className={s.num} title="Share of forecasts that got the direction right">
                  Dir hit
                </th>
                <th className={s.num} title="Mean absolute % error of the median">
                  MAPE
                </th>
                <th className={s.num} title="Share of outcomes inside P10..P90 (target 80%)">
                  80% cover
                </th>
                <th className={s.num}>n</th>
                <th />
              </tr>
            </thead>
            <tbody>
              {rows.map((r) => {
                const st = modelStyle(r.model)
                return (
                  <tr key={`${r.model}-${r.scope}-${r.horizon}`}>
                    <td style={{ color: st.color }}>{st.name}</td>
                    <td className="muted">{r.scope === 'all' ? 'All stocks' : r.scope}</td>
                    <td className={s.num}>{r.horizon}D</td>
                    <td className={`${s.num} ${r.dirAccuracy != null && r.dirAccuracy > 0.5 ? 'up' : 'value'}`}>{pct(r.dirAccuracy)}</td>
                    <td className={s.num}>{pct(r.mape)}</td>
                    <td
                      className={`${s.num} ${r.coverage80 != null && Math.abs(r.coverage80 - 0.8) > 0.1 ? 'down' : 'value'}`}
                    >
                      {pct(r.coverage80)}
                    </td>
                    <td className={`${s.num} muted`}>{formatNum(r.n, 'number')}</td>
                    <td>
                      {r.beatsBaseline === true && <Badge tone="up">Beats baseline</Badge>}
                      {r.beatsBaseline === false && <Badge tone="muted">Below baseline</Badge>}
                    </td>
                  </tr>
                )
              })}
            </tbody>
          </table>
        </div>
      )}
    </section>
  )
}
