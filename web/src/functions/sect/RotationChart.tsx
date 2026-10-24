import s from './SECT.module.css'

export interface RotationPoint {
  rsRatio: number
  rsMomentum: number
  quadrant: string
  date: string
}

export interface RotationSector {
  name: string
  rotation: RotationPoint | null
  trail: RotationPoint[]
}

const QUADRANT_COLOR: Record<string, string> = {
  leading: 'var(--up)',
  weakening: 'var(--amber)',
  lagging: 'var(--down)',
  improving: 'var(--cyan)',
}

const SIZE = 320
const PAD = 26

/** RRG-style scatter: RS-ratio (x) vs RS-momentum (y), centred on 100, with a 5-session tail. */
export function RotationChart({ sectors, onOpen }: { sectors: RotationSector[]; onOpen: (name: string) => void }) {
  const pts = sectors.flatMap((x) => [...x.trail, ...(x.rotation ? [x.rotation] : [])])
  if (!pts.length) return <p className="muted">No rotation data yet.</p>
  const ext = Math.max(1, ...pts.map((p) => Math.max(Math.abs(p.rsRatio - 100), Math.abs(p.rsMomentum - 100)))) * 1.15
  const x = (v: number) => PAD + ((v - (100 - ext)) / (2 * ext)) * (SIZE - 2 * PAD)
  const y = (v: number) => SIZE - PAD - ((v - (100 - ext)) / (2 * ext)) * (SIZE - 2 * PAD)
  const mid = x(100)
  const q = (SIZE - 2 * PAD) / 2

  return (
    <svg viewBox={`0 0 ${SIZE} ${SIZE}`} className={s.rotation} role="img" aria-label="Sector rotation quadrant">
      <rect x={mid} y={PAD} width={q} height={q} fill="var(--up)" fillOpacity={0.06} />
      <rect x={mid} y={mid} width={q} height={q} fill="var(--amber)" fillOpacity={0.06} />
      <rect x={PAD} y={mid} width={q} height={q} fill="var(--down)" fillOpacity={0.06} />
      <rect x={PAD} y={PAD} width={q} height={q} fill="var(--cyan)" fillOpacity={0.05} />
      <line x1={PAD} x2={SIZE - PAD} y1={mid} y2={mid} stroke="var(--line-strong)" />
      <line x1={mid} x2={mid} y1={PAD} y2={SIZE - PAD} stroke="var(--line-strong)" />
      <text x={SIZE - PAD - 2} y={PAD + 10} textAnchor="end" className={s.quadLabel} fill={QUADRANT_COLOR.leading}>
        Leading
      </text>
      <text x={SIZE - PAD - 2} y={SIZE - PAD - 4} textAnchor="end" className={s.quadLabel} fill={QUADRANT_COLOR.weakening}>
        Weakening
      </text>
      <text x={PAD + 2} y={SIZE - PAD - 4} className={s.quadLabel} fill={QUADRANT_COLOR.lagging}>
        Lagging
      </text>
      <text x={PAD + 2} y={PAD + 10} className={s.quadLabel} fill={QUADRANT_COLOR.improving}>
        Improving
      </text>
      <text x={SIZE / 2} y={SIZE - 6} textAnchor="middle" className={s.axisLabel}>
        RS-Ratio →
      </text>
      <text x={10} y={SIZE / 2} textAnchor="middle" className={s.axisLabel} transform={`rotate(-90 10 ${SIZE / 2})`}>
        RS-Momentum →
      </text>
      {sectors.map((sec) => {
        if (!sec.rotation) return null
        const path = [...sec.trail, sec.rotation]
        const color = QUADRANT_COLOR[sec.rotation.quadrant] ?? 'var(--muted)'
        const cx = x(sec.rotation.rsRatio)
        const cy = y(sec.rotation.rsMomentum)
        return (
          <g key={sec.name} className={s.rotPoint} onClick={() => onOpen(sec.name)}>
            <title>
              {`${sec.name}: RS-Ratio ${sec.rotation.rsRatio.toFixed(1)}, RS-Momentum ${sec.rotation.rsMomentum.toFixed(1)} (${sec.rotation.quadrant})`}
            </title>
            <polyline
              points={path.map((p) => `${x(p.rsRatio).toFixed(1)},${y(p.rsMomentum).toFixed(1)}`).join(' ')}
              fill="none"
              stroke={color}
              strokeOpacity={0.5}
              strokeWidth={1.2}
            />
            {sec.trail.map((p) => (
              <circle key={p.date} cx={x(p.rsRatio)} cy={y(p.rsMomentum)} r={1.6} fill={color} opacity={0.5} />
            ))}
            <circle cx={cx} cy={cy} r={4} fill={color} />
            <text x={cx + 6} y={cy + 3} className={s.rotLabel}>
              {sec.name.length > 14 ? `${sec.name.slice(0, 13)}…` : sec.name}
            </text>
          </g>
        )
      })}
    </svg>
  )
}
