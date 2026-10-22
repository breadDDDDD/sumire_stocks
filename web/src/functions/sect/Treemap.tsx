import { hierarchy, treemap, treemapSquarify } from 'd3-hierarchy'
import { useLayoutEffect, useMemo, useRef, useState, type KeyboardEvent } from 'react'
import { formatNum } from '../../lib/format'
import s from './SECT.module.css'

export interface TreeItem {
  id: string
  label: string
  sub?: string
  size: number
  chg: number | null
}

/** Background for a % change: deeper green/red as the move grows, saturating at ±`max`. */
function heatColor(chg: number | null, max: number): string {
  if (chg == null || !Number.isFinite(chg) || chg === 0) return 'var(--panel-2)'
  const a = 0.18 + 0.67 * Math.min(1, Math.abs(chg) / max)
  return chg > 0 ? `rgba(34, 197, 94, ${a.toFixed(2)})` : `rgba(244, 63, 94, ${a.toFixed(2)})`
}

/**
 * Squarified treemap sized by `size`, colored by `chg`. Arrow keys / j k move, Enter opens,
 * Backspace calls onBack (when given).
 */
export function Treemap({
  items,
  maxChg,
  onOpen,
  onBack,
  label,
}: {
  items: TreeItem[]
  maxChg: number
  onOpen: (item: TreeItem) => void
  onBack?: () => void
  label: string
}) {
  const ref = useRef<HTMLDivElement>(null)
  const [size, setSize] = useState({ w: 0, h: 0 })
  const [active, setActive] = useState(0)

  useLayoutEffect(() => {
    const el = ref.current
    if (!el) return
    const ro = new ResizeObserver(() => setSize({ w: el.clientWidth, h: el.clientHeight }))
    ro.observe(el)
    return () => ro.disconnect()
  }, [])

  const leaves = useMemo(() => {
    if (!size.w || !size.h || !items.length) return []
    const root = hierarchy<{ children?: TreeItem[] } & Partial<TreeItem>>({ children: items })
      .sum((d) => (d.children ? 0 : Math.max(d.size ?? 0, 0)))
      .sort((a, b) => (b.value ?? 0) - (a.value ?? 0))
    treemap<typeof root.data>().tile(treemapSquarify).size([size.w, size.h]).paddingInner(2).round(true)(root)
    return root.leaves().map((l) => {
      const r = l as typeof l & { x0: number; y0: number; x1: number; y1: number }
      return { item: l.data as TreeItem, x: r.x0, y: r.y0, w: r.x1 - r.x0, h: r.y1 - r.y0 }
    })
  }, [items, size])

  const idx = Math.min(active, Math.max(0, leaves.length - 1))

  const onKeyDown = (e: KeyboardEvent<HTMLDivElement>) => {
    if (e.altKey || e.ctrlKey || e.metaKey || !leaves.length) return
    switch (e.key) {
      case 'ArrowRight':
      case 'ArrowDown':
      case 'j':
        setActive((idx + 1) % leaves.length)
        break
      case 'ArrowLeft':
      case 'ArrowUp':
      case 'k':
        setActive((idx - 1 + leaves.length) % leaves.length)
        break
      case 'Enter':
        onOpen(leaves[idx].item)
        break
      case 'Backspace':
        if (!onBack) return
        onBack()
        break
      default:
        return
    }
    e.preventDefault()
  }

  return (
    <div ref={ref} className={s.treemap} tabIndex={0} onKeyDown={onKeyDown} role="grid" aria-label={label}>
      {leaves.map((l, i) => {
        const roomy = l.w > 54 && l.h > 30
        return (
          <button
            key={l.item.id}
            tabIndex={-1}
            className={[s.cell, i === idx && s.cellActive].filter(Boolean).join(' ')}
            style={{ left: l.x, top: l.y, width: l.w, height: l.h, background: heatColor(l.item.chg, maxChg) }}
            title={`${l.item.label}${l.item.sub ? ` · ${l.item.sub}` : ''}: ${formatNum(l.item.chg, 'pct', { signed: true })}`}
            onClick={() => {
              setActive(i)
              onOpen(l.item)
            }}
          >
            {l.w > 30 && l.h > 16 && <span className={s.cellLabel}>{l.item.label}</span>}
            {roomy && <span className={s.cellChg}>{formatNum(l.item.chg, 'pct', { signed: true })}</span>}
          </button>
        )
      })}
    </div>
  )
}
