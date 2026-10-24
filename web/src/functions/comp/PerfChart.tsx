import { SERIES_COLORS } from './math'
import { useState, type MouseEvent } from 'react'
import { formatNum } from '../../lib/format'
import s from './COMP.module.css'


const W = 800
const H = 260
const PAD = { l: 8, r: 48, t: 10, b: 20 }

/** Rebased (=100) performance lines with a hover crosshair. */
export function PerfChart({ dates, series, names }: { dates: string[]; series: number[][]; names: string[] }) {
  const [hover, setHover] = useState<number | null>(null)
  const all = series.flat()
  const lo = Math.min(100, ...all)
  const hi = Math.max(100, ...all)
  const span = hi - lo || 1
  const n = dates.length
  const x = (i: number) => PAD.l + (i / Math.max(1, n - 1)) * (W - PAD.l - PAD.r)
  const y = (v: number) => PAD.t + (1 - (v - lo) / span) * (H - PAD.t - PAD.b)

  const ticks = Array.from({ length: 5 }, (_, i) => lo + (span * i) / 4)
  const xTicks = n > 1 ? [0, Math.floor(n / 3), Math.floor((2 * n) / 3), n - 1] : []

  const onMove = (e: MouseEvent<SVGSVGElement>) => {
    const r = e.currentTarget.getBoundingClientRect()
    const px = ((e.clientX - r.left) / r.width) * W
    const i = Math.round(((px - PAD.l) / (W - PAD.l - PAD.r)) * (n - 1))
    setHover(Math.max(0, Math.min(n - 1, i)))
  }

  const idx = hover ?? n - 1

  return (
    <div className={s.chartWrap}>
      <div className={s.legend}>
        <span className="muted">{dates[idx]}</span>
        {names.map((name, k) => (
          <span key={name} style={{ color: SERIES_COLORS[k] }}>
            {name}{' '}
            <span className={(series[k][idx] ?? 100) >= 100 ? 'up' : 'down'}>
              {formatNum((series[k][idx] ?? 100) - 100, 'pct', { signed: true, decimals: 1 })}
            </span>
          </span>
        ))}
      </div>
      <svg
        viewBox={`0 0 ${W} ${H}`}
        className={s.chart}
        onMouseMove={onMove}
        onMouseLeave={() => setHover(null)}
        role="img"
        aria-label={`Performance rebased to 100: ${names.join(', ')}`}
      >
        {ticks.map((t) => (
          <g key={t}>
            <line x1={PAD.l} x2={W - PAD.r} y1={y(t)} y2={y(t)} stroke="var(--border)" strokeWidth={1} vectorEffect="non-scaling-stroke" />
            <text x={W - PAD.r + 4} y={y(t) + 4} className={s.axis}>
              {t.toFixed(0)}
            </text>
          </g>
        ))}
        <line x1={PAD.l} x2={W - PAD.r} y1={y(100)} y2={y(100)} stroke="var(--amber-deep)" strokeDasharray="4 4" vectorEffect="non-scaling-stroke" />
        {xTicks.map((i) => (
          <text key={i} x={x(i)} y={H - 4} className={s.axis} textAnchor={i === 0 ? 'start' : i === n - 1 ? 'end' : 'middle'}>
            {dates[i]}
          </text>
        ))}
        {series.map((vals, k) => (
          <path
            key={names[k]}
            d={vals.map((v, i) => `${i ? 'L' : 'M'}${x(i).toFixed(1)},${y(v).toFixed(1)}`).join('')}
            fill="none"
            stroke={SERIES_COLORS[k]}
            strokeWidth={1.6}
            vectorEffect="non-scaling-stroke"
          />
        ))}
        {hover !== null && (
          <line x1={x(hover)} x2={x(hover)} y1={PAD.t} y2={H - PAD.b} stroke="var(--amber)" strokeWidth={1} strokeDasharray="2 2" vectorEffect="non-scaling-stroke" />
        )}
      </svg>
    </div>
  )
}
