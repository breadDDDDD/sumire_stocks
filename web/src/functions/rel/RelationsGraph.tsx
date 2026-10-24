import { useMemo, type KeyboardEvent } from 'react'
import { formatNum, direction } from '../../components/ui'
import { KIND_META, type MapNode, type NodeKind } from './types'
import s from './Rel.module.css'

const W = 640
const H = 430
const CX = W / 2
const CY = H / 2

export interface RelationsGraphProps {
  code: string
  chgPct: number | null
  nodes: MapNode[]
  live: Record<string, { changePct?: number | null }>
  onOpen: (code: string) => void
}

const pctOf = (n: MapNode, live: RelationsGraphProps['live']) => (n.code ? (live[n.code]?.changePct ?? n.chgPct) : null)

/** Hub-and-spoke map: the stock in the middle, links around it grouped by kind. Solid = curated, dashed = statistical. */
export function RelationsGraph({ code, chgPct, nodes, live, onOpen }: RelationsGraphProps) {
  const placed = useMemo(() => {
    const n = nodes.length
    // two staggered rings once it gets crowded so labels don't collide
    const twoRings = n > 14
    return nodes.map((node, i) => {
      const angle = -Math.PI / 2 + (i / Math.max(1, n)) * Math.PI * 2
      const r = twoRings ? (i % 2 ? 190 : 135) : 165
      return { node, x: CX + Math.cos(angle) * r * 1.42, y: CY + Math.sin(angle) * r * 0.92 }
    })
  }, [nodes])

  const kinds = [...new Set(nodes.map((n) => n.kind))] as NodeKind[]

  const onKey = (e: KeyboardEvent, target: string | null) => {
    if ((e.key === 'Enter' || e.key === ' ') && target) {
      e.preventDefault()
      onOpen(target)
    }
  }

  const centerDir = direction(live[code]?.changePct ?? chgPct)

  return (
    <div className={s.graphWrap}>
      <svg className={s.graph} viewBox={`0 0 ${W} ${H}`} role="img" aria-label={`Relations map for ${code}`}>
        {placed.map(({ node, x, y }) => (
          <line
            key={`e:${node.id}`}
            x1={CX}
            y1={CY}
            x2={x}
            y2={y}
            stroke={KIND_META[node.kind].color}
            strokeWidth={node.statistical ? 1 + (node.strength ?? 0.5) * 1.5 : 1.6}
            strokeDasharray={node.statistical ? '5 4' : undefined}
            strokeOpacity={node.statistical ? 0.35 + (node.strength ?? 0.5) * 0.5 : 0.6}
          />
        ))}

        <g className={s.center}>
          <rect x={CX - 80} y={CY - 30} width={160} height={60} />
          <text x={CX} y={CY + 1} className={s.centerCode}>
            {code}
          </text>
          <text x={CX} y={CY + 21} className={`${s.centerPct} ${centerDir}`}>
            {formatNum(live[code]?.changePct ?? chgPct, 'pct', { signed: true })}
          </text>
        </g>

        {placed.map(({ node, x, y }) => {
          const pct = pctOf(node, live)
          const dir = direction(pct)
          const clickable = node.kind !== 'factor' && !!node.code
          const w = Math.max(72, node.label.length * 10.5 + 18)
          return (
            <g
              key={node.id}
              className={`${s.node} ${clickable ? s.clickable : ''}`}
              transform={`translate(${x} ${y})`}
              tabIndex={clickable ? 0 : -1}
              role={clickable ? 'button' : undefined}
              aria-label={`${node.label} ${formatNum(pct, 'pct', { signed: true })}, ${node.detail}`}
              onClick={clickable ? () => onOpen(node.code!) : undefined}
              onKeyDown={(e) => onKey(e, clickable ? node.code : null)}
            >
              <title>{`${node.label} · ${node.detail}`}</title>
              <rect x={-w / 2} y={-20} width={w} height={40} style={{ stroke: KIND_META[node.kind].color }} />
              <text y={-3} className={s.nodeLabel}>
                {node.label}
              </text>
              <text y={14} className={`${s.nodePct} ${dir}`}>
                {node.code ? formatNum(pct, 'pct', { signed: true }) : node.kind === 'factor' ? 'no proxy' : '—'}
              </text>
            </g>
          )
        })}
      </svg>
      <div className={s.legend}>
        {kinds.map((k) => (
          <span key={k}>
            <i style={{ background: KIND_META[k].color }} />
            {KIND_META[k].label}
          </span>
        ))}
        <span>
          <b className={s.solid} /> curated
        </span>
        <span>
          <b className={s.dashed} /> statistical
        </span>
      </div>
    </div>
  )
}
