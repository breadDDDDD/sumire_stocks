import { useEffect, useLayoutEffect, useMemo, useRef, useState, type KeyboardEvent, type ReactNode } from 'react'
import type { Column } from '../../components/ui'
import s from '../../components/ui/Table.module.css'

const DEFAULT_ROW_H = 25
const WINDOW_FROM = 200
const OVERSCAN = 10

export interface GridTableProps<T> {
  columns: Column<T>[]
  rows: T[]
  rowKey: (row: T) => string
  /** Enter or click. */
  onOpen?: (row: T) => void
  /** Extra keys for the active row; return true when handled (default is prevented). */
  onRowKey?: (e: KeyboardEvent<HTMLDivElement>, row: T, index: number) => boolean
  /** Sorting by header click (off for ordered lists like a watchlist). */
  sortable?: boolean
  initialSort?: { key: string; dir: 'asc' | 'desc' }
  autoFocus?: boolean
  empty?: ReactNode
}

/**
 * Keyboard table like ui/Table, but the active row is tracked by key (so it follows reorders), it accepts extra row
 * keys, and with more than 200 rows only the visible window is rendered.
 */
export function GridTable<T>({
  columns,
  rows,
  rowKey,
  onOpen,
  onRowKey,
  sortable = true,
  initialSort,
  autoFocus,
  empty,
}: GridTableProps<T>) {
  const [sort, setSort] = useState(initialSort)
  const [activeKey, setActiveKey] = useState<string | null>(null)
  const [scrollTop, setScrollTop] = useState(0)
  const [viewH, setViewH] = useState(600)
  const [rowH, setRowH] = useState(DEFAULT_ROW_H)
  const wrapRef = useRef<HTMLDivElement>(null)

  const sorted = useMemo(() => {
    if (!sortable || !sort) return rows
    const col = columns.find((c) => c.key === sort.key)
    if (!col) return rows
    const get = col.sortValue ?? ((r: T) => (r as Record<string, unknown>)[col.key] as number | string | null)
    const mul = sort.dir === 'asc' ? 1 : -1
    return [...rows].sort((a, b) => {
      const va = get(a)
      const vb = get(b)
      if (va == null && vb == null) return 0
      if (va == null) return 1
      if (vb == null) return -1
      if (typeof va === 'number' && typeof vb === 'number') return (va - vb) * mul
      return String(va).localeCompare(String(vb)) * mul
    })
  }, [rows, columns, sort, sortable])

  const found = activeKey == null ? -1 : sorted.findIndex((r) => rowKey(r) === activeKey)
  const active = found >= 0 ? found : 0
  const windowed = sorted.length > WINDOW_FROM

  useEffect(() => {
    if (autoFocus) wrapRef.current?.focus({ preventScroll: true })
  }, [autoFocus])

  useLayoutEffect(() => {
    const el = wrapRef.current
    if (!el) return
    const ro = new ResizeObserver(() => setViewH(el.clientHeight))
    ro.observe(el)
    return () => ro.disconnect()
  }, [])

  // rows can be taller than the default (badges): measure one so windowing and scrolling line up
  useLayoutEffect(() => {
    const tr = wrapRef.current?.querySelector<HTMLElement>('tbody tr[data-row]')
    if (tr && tr.offsetHeight && tr.offsetHeight !== rowH) setRowH(tr.offsetHeight)
  }, [rowH, sorted])

  // keep the active row in view
  useEffect(() => {
    const el = wrapRef.current
    if (!el || !sorted.length) return
    const head = el.querySelector('thead')?.clientHeight ?? rowH
    const top = active * rowH
    if (top < el.scrollTop) el.scrollTop = top
    else if (top + rowH + head > el.scrollTop + el.clientHeight) el.scrollTop = top + rowH + head - el.clientHeight
  }, [active, sorted.length, rowH])

  const move = (i: number) => {
    const r = sorted[Math.max(0, Math.min(sorted.length - 1, i))]
    if (r) setActiveKey(rowKey(r))
  }

  const onKeyDown = (e: KeyboardEvent<HTMLDivElement>) => {
    if (!sorted.length || e.ctrlKey || e.metaKey) return
    const row = sorted[active]
    // pin the active row by key so it follows the row through a reorder
    if (activeKey !== rowKey(row)) setActiveKey(rowKey(row))
    if (onRowKey?.(e, row, active)) {
      e.preventDefault()
      return
    }
    if (e.altKey) return
    const page = Math.max(1, Math.floor(viewH / rowH) - 2)
    switch (e.key) {
      case 'j':
      case 'ArrowDown':
        move(active + 1)
        break
      case 'k':
      case 'ArrowUp':
        move(active - 1)
        break
      case 'Home':
        move(0)
        break
      case 'End':
        move(sorted.length - 1)
        break
      case 'PageDown':
        move(active + page)
        break
      case 'PageUp':
        move(active - page)
        break
      case 'Enter':
        if (!onOpen) return
        onOpen(row)
        break
      default:
        return
    }
    e.preventDefault()
  }

  const toggleSort = (c: Column<T>) => {
    if (!sortable || c.sortable === false) return
    setSort((p) =>
      p?.key === c.key ? { key: c.key, dir: p.dir === 'desc' ? 'asc' : 'desc' } : { key: c.key, dir: c.align === 'right' ? 'desc' : 'asc' },
    )
  }

  let from = 0
  let to = sorted.length
  if (windowed) {
    from = Math.max(0, Math.floor(scrollTop / rowH) - OVERSCAN)
    to = Math.min(sorted.length, Math.ceil((scrollTop + viewH) / rowH) + OVERSCAN)
  }
  const alignCls = (a?: string) => (a === 'right' ? s.right : a === 'center' ? s.center : undefined)

  return (
    <div
      ref={wrapRef}
      className={s.wrap}
      tabIndex={0}
      onKeyDown={onKeyDown}
      onScroll={windowed ? (e) => setScrollTop(e.currentTarget.scrollTop) : undefined}
    >
      <table className={s.table}>
        <thead>
          <tr>
            {columns.map((c) => {
              const on = sortable && sort?.key === c.key
              return (
                <th
                  key={c.key}
                  style={c.width !== undefined ? { width: c.width } : undefined}
                  className={[alignCls(c.align), sortable && c.sortable !== false && s.sortable, on && s.sorted]
                    .filter(Boolean)
                    .join(' ')}
                  onClick={() => toggleSort(c)}
                >
                  {c.header}
                  {on && (sort.dir === 'asc' ? ' ▲' : ' ▼')}
                </th>
              )
            })}
          </tr>
        </thead>
        <tbody>
          {from > 0 && <tr style={{ height: from * rowH }} aria-hidden="true" />}
          {sorted.slice(from, to).map((row, j) => {
            const i = from + j
            return (
              <tr
                key={rowKey(row)}
                data-row={i}
                className={[s.row, onOpen && s.clickable, i === active && s.active].filter(Boolean).join(' ')}
                onClick={() => {
                  setActiveKey(rowKey(row))
                  onOpen?.(row)
                }}
              >
                {columns.map((c) => (
                  <td key={c.key} className={alignCls(c.align)}>
                    {c.render ? c.render(row) : String((row as Record<string, unknown>)[c.key] ?? '—')}
                  </td>
                ))}
              </tr>
            )
          })}
          {to < sorted.length && <tr style={{ height: (sorted.length - to) * rowH }} aria-hidden="true" />}
        </tbody>
      </table>
      {!sorted.length && empty}
    </div>
  )
}
