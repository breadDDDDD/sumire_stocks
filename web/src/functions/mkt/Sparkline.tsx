import { direction } from '../../lib/format'
import s from './Sparkline.module.css'

/** Tiny SVG line, colored by last vs baseline (prevClose, else first point). */
export function Sparkline({
  values,
  baseline,
  width = 220,
  height = 56,
  className,
}: {
  values: number[]
  baseline?: number | null
  width?: number
  height?: number
  className?: string
}) {
  if (values.length < 2) return <svg width={width} height={height} className={className} aria-hidden="true" />
  const base = baseline ?? values[0]
  const lo = Math.min(base, ...values)
  const hi = Math.max(base, ...values)
  const span = hi - lo || 1
  const x = (i: number) => (i / (values.length - 1)) * width
  const y = (v: number) => height - 2 - ((v - lo) / span) * (height - 4)
  const d = values.map((v, i) => `${i ? 'L' : 'M'}${x(i).toFixed(1)},${y(v).toFixed(1)}`).join('')
  const dir = direction(values[values.length - 1] - base)
  const color = dir === 'up' ? 'var(--up)' : dir === 'down' ? 'var(--down)' : 'var(--flat)'
  return (
    <svg
      width="100%"
      height={height}
      viewBox={`0 0 ${width} ${height}`}
      preserveAspectRatio="none"
      className={className}
      role="img"
      aria-label="Intraday sparkline"
    >
      <line x1={0} x2={width} y1={y(base)} y2={y(base)} stroke="var(--line-strong)" strokeDasharray="2 3" strokeWidth={1} />
      {height >= 40 && <path className={s.area} d={`${d}L${width},${height}L0,${height}Z`} fill={color} />}
      <path d={d} fill="none" stroke={color} strokeWidth={1.25} vectorEffect="non-scaling-stroke" />
      {/* draw-in shutter: mounts once with the first data, shrinks to the right, then stays at scaleX(0) */}
      <rect className={s.shutter} x={0} y={0} width={width} height={height} />
    </svg>
  )
}
