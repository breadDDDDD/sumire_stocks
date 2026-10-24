import { useEffect, useMemo, useRef, useState, type KeyboardEvent, type ReactNode } from 'react'
import s from './Table.module.css'

export interface Column<T> {
  key: string
  header: ReactNode
  /** Cell content; defaults to row[key]. */
  render?: (row: T) => ReactNode
  /** Value used for sorting; defaults to row[key]. Omit `sortable: false` columns. */
  sortValue?: (row: T) => number | string | null | undefined
  sortable?: boolean
  align?: 'left' | 'right' | 'center'
  width?: number | string
}

export interface TableProps<T> {
  columns: Column<T>[]
  rows: T[]
  rowKey: (row: T) => string
  /** Enter or click on a row. */
  onOpen?: (row: T) => void
  initialSort?: { key: string; dir: 'asc' | 'desc' }
  autoFocus?: boolean
  className?: string
  /** Shown instead of the body when rows is empty. */
  empty?: ReactNode
}

function readKey<T>(row: T, key: string): unknown {
  return (row as Record<string, unknown>)[key]
}

/**
 * Sortable table with a sticky header and keyboard navigation:
 * j/k or ↑/↓ move, Home/End jump, Enter opens. Click a header to sort.
 */
export function Table<T>({ columns, rows, rowKey, onOpen, initialSort, autoFocus, className, empty }: TableProps<T>) {
  const [sort, setSort] = useState(initialSort)
  const [active, setActive] = useState(0)
  const wrapRef = useRef<HTMLDivElement>(null)

  const sorted = useMemo(() => {
    if (!sort) return rows
    const col = columns.find((c) => c.key === sort.key)
    if (!col) return rows
    const get = col.sortValue ?? ((r: T) => readKey(r, col.key) as number | string | null | undefined)
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
  }, [rows, columns, sort])

  const activeIndex = Math.min(active, Math.max(0, sorted.length - 1))

  useEffect(() => {
    if (autoFocus) wrapRef.current?.focus({ preventScroll: true })
  }, [autoFocus])

  useEffect(() => {
    wrapRef.current?.querySelector(`[data-row="${activeIndex}"]`)?.scrollIntoView({ block: 'nearest' })
  }, [activeIndex])

  const toggleSort = (col: Column<T>) => {
    if (col.sortable === false) return
    setSort((prev) =>
      prev?.key === col.key
        ? { key: col.key, dir: prev.dir === 'desc' ? 'asc' : 'desc' }
        : { key: col.key, dir: col.align === 'right' ? 'desc' : 'asc' },
    )
  }

  const onKeyDown = (e: KeyboardEvent<HTMLDivElement>) => {
    if (e.altKey || e.ctrlKey || e.metaKey || !sorted.length) return
    let next = activeIndex
    switch (e.key) {
      case 'j':
      case 'ArrowDown':
        next = Math.min(sorted.length - 1, activeIndex + 1)
        break
      case 'k':
      case 'ArrowUp':
        next = Math.max(0, activeIndex - 1)
        break
      case 'Home':
        next = 0
        break
      case 'End':
        next = sorted.length - 1
        break
      case 'PageDown':
        next = Math.min(sorted.length - 1, activeIndex + 15)
        break
      case 'PageUp':
        next = Math.max(0, activeIndex - 15)
        break
      case 'Enter':
        if (onOpen) {
          e.preventDefault()
          onOpen(sorted[activeIndex])
        }
        return
      default:
        return
    }
    e.preventDefault()
    setActive(next)
  }

  const alignCls = (a?: string) => (a === 'right' ? s.right : a === 'center' ? s.center : undefined)

  return (
    <div ref={wrapRef} className={[s.wrap, className].filter(Boolean).join(' ')} tabIndex={0} onKeyDown={onKeyDown}>
      <table className={s.table}>
        <thead>
          <tr>
            {columns.map((c) => {
              const isSorted = sort?.key === c.key
              return (
                <th
                  key={c.key}
                  style={c.width !== undefined ? { width: c.width } : undefined}
                  className={[alignCls(c.align), c.sortable !== false && s.sortable, isSorted && s.sorted]
                    .filter(Boolean)
                    .join(' ')}
                  onClick={() => toggleSort(c)}
                  aria-sort={isSorted ? (sort.dir === 'asc' ? 'ascending' : 'descending') : undefined}
                >
                  {c.header}
                  {isSorted && (
                    <span className={s.arrow} aria-hidden="true">
                      {sort.dir === 'asc' ? '▲' : '▼'}
                    </span>
                  )}
                </th>
              )
            })}
          </tr>
        </thead>
        <tbody>
          {sorted.map((row, i) => (
            <tr
              key={rowKey(row)}
              data-row={i}
              className={[s.row, onOpen && s.clickable, i === activeIndex && s.active].filter(Boolean).join(' ')}
              onClick={() => {
                setActive(i)
                onOpen?.(row)
              }}
            >
              {columns.map((c) => (
                <td key={c.key} className={alignCls(c.align)}>
                  {c.render ? c.render(row) : String(readKey(row, c.key) ?? '—')}
                </td>
              ))}
            </tr>
          ))}
        </tbody>
      </table>
      {!sorted.length && empty}
    </div>
  )
}
