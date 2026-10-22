import { useState, type FormEvent, type KeyboardEvent } from 'react'
import { ApiError } from '../api/client'
import { Button, Empty, ErrorState, FreshnessBadge, Num, Panel, Skeleton, type Column } from '../components/ui'
import {
  loadActiveId,
  pickActive,
  saveActiveId,
  useWatchlistMutations,
  useWatchlists,
} from '../components/watchlist/useWatchlists'
import { usePane } from '../shell/workspace'
import { GridTable } from './eqs/GridTable'
import f from './eqs/form.module.css'
import { useLiveQuotes, type Quote } from './mkt/quotes'
import type { FunctionProps } from './types'
import s from './w/W.module.css'

interface Row {
  code: string
  q?: Quote
}

function errText(e: unknown): string | null {
  if (!e) return null
  return e instanceof ApiError ? e.message : 'Request failed'
}

export default function W(_: FunctionProps) {
  const pane = usePane()
  const q = useWatchlists()
  const m = useWatchlistMutations()
  const [activeId, setActiveId] = useState(loadActiveId)
  const [code, setCode] = useState('')
  const [newName, setNewName] = useState('')
  const [renaming, setRenaming] = useState<string | null>(null)

  const list = pickActive(q.data, activeId)
  const codes = list?.items.map((i) => i.code) ?? []
  const quotes = useLiveQuotes(codes)
  const rows: Row[] = codes.map((c) => ({ code: c, q: quotes[c] }))
  const latest = Object.values(quotes).reduce<string | undefined>((t, x) => (!t || x.time > t ? x.time : t), undefined)

  const select = (id: number) => {
    setActiveId(id)
    saveActiveId(id)
    setRenaming(null)
  }

  const add = (e: FormEvent) => {
    e.preventDefault()
    const c = code.trim().toUpperCase()
    if (!list || !c) return
    m.addItem.mutate({ id: list.id, code: c }, { onSuccess: () => setCode('') })
  }

  const create = (e: FormEvent) => {
    e.preventDefault()
    const n = newName.trim()
    if (!n) return
    m.create.mutate(n, {
      onSuccess: (w) => {
        setNewName('')
        select(w.id)
      },
    })
  }

  const columns: Column<Row>[] = [
    { key: 'code', header: 'Code', render: (r) => <span className={s.code}>{r.code}</span> },
    { key: 'name', header: 'Name', render: (r) => <span className={s.name}>{r.q?.name ?? ''}</span> },
    { key: 'price', header: 'Last', align: 'right', render: (r) => <Num value={r.q?.price} format="price" colorBy={r.q?.changePct ?? null} /> },
    { key: 'change', header: 'Chg', align: 'right', render: (r) => <Num value={r.q?.change} format="price" signed colored /> },
    { key: 'changePct', header: 'Chg %', align: 'right', render: (r) => <Num value={r.q?.changePct} format="pct" signed colored /> },
    { key: 'value', header: 'Value', align: 'right', render: (r) => <Num value={r.q?.value} format="compact" /> },
    {
      key: 'x',
      header: '',
      align: 'right',
      width: 24,
      render: (r) => (
        <button
          className={f.x}
          title={`Remove ${r.code}`}
          onClick={(e) => {
            e.stopPropagation()
            if (list) m.removeItem.mutate({ id: list.id, code: r.code })
          }}
        >
          ×
        </button>
      ),
    },
  ]

  const onRowKey = (e: KeyboardEvent, row: Row, i: number) => {
    if (!list) return false
    if (e.altKey && (e.key === 'ArrowUp' || e.key === 'ArrowDown')) {
      const j = e.key === 'ArrowUp' ? i - 1 : i + 1
      if (j < 0 || j >= codes.length) return true
      const next = [...codes]
      ;[next[i], next[j]] = [next[j], next[i]]
      m.reorder.mutate({ id: list.id, codes: next })
      return true
    }
    if (e.key === 'Delete' || e.key === 'Backspace') {
      m.removeItem.mutate({ id: list.id, code: row.code })
      return true
    }
    if (e.key === 'g' && !e.altKey) {
      pane.open({ code: 'GP', ticker: row.code, args: [] })
      return true
    }
    return false
  }

  const error =
    errText(m.addItem.error) ?? errText(m.create.error) ?? errText(m.rename.error) ?? errText(m.remove.error) ?? errText(m.reorder.error)

  return (
    <Panel title="W" subtitle="Watchlists" actions={codes.length ? <FreshnessBadge asOf={latest} /> : undefined} bodyClassName={s.body}>
      {q.isPending ? (
        <Skeleton rows={8} columns={5} />
      ) : q.isError ? (
        <ErrorState error={q.error} onRetry={() => q.refetch()} title="Watchlists unavailable" />
      ) : (
        <>
          <div className={f.toolbar}>
            <div className={s.tabs} role="tablist">
              {q.data.map((l) => (
                <button
                  key={l.id}
                  role="tab"
                  aria-selected={l.id === list?.id}
                  className={[s.tab, l.id === list?.id && s.tabOn].filter(Boolean).join(' ')}
                  onClick={() => select(l.id)}
                >
                  {l.name} <span className="muted">{l.items.length}</span>
                </button>
              ))}
              <form onSubmit={create} className={f.row}>
                <input
                  className={f.input}
                  placeholder="New list"
                  maxLength={40}
                  value={newName}
                  onChange={(e) => setNewName(e.target.value)}
                  style={{ width: 110 }}
                />
                <Button type="submit" variant="ghost" disabled={!newName.trim()}>
                  + List
                </Button>
              </form>
            </div>

            {list && (
              <div className={f.row}>
                <form onSubmit={add} className={f.row}>
                  <input
                    className={`${f.input} ${f.code}`}
                    placeholder="Ticker"
                    value={code}
                    onChange={(e) => setCode(e.target.value.toUpperCase())}
                    aria-label="Ticker to add"
                  />
                  <Button type="submit" variant="primary" disabled={!code.trim() || m.addItem.isPending}>
                    Add
                  </Button>
                </form>
                <span className={s.spacer} />
                {renaming !== null ? (
                  <form
                    className={f.row}
                    onSubmit={(e) => {
                      e.preventDefault()
                      if (renaming.trim()) m.rename.mutate({ id: list.id, name: renaming.trim() }, { onSuccess: () => setRenaming(null) })
                    }}
                  >
                    <input
                      autoFocus
                      className={f.input}
                      maxLength={40}
                      value={renaming}
                      onChange={(e) => setRenaming(e.target.value)}
                      onKeyDown={(e) => e.key === 'Escape' && (e.preventDefault(), setRenaming(null))}
                    />
                    <Button type="submit">Save</Button>
                  </form>
                ) : (
                  <Button variant="ghost" onClick={() => setRenaming(list.name)}>
                    Rename
                  </Button>
                )}
                <Button
                  variant="ghost"
                  disabled={q.data.length < 2}
                  title={q.data.length < 2 ? 'Keep at least one list' : undefined}
                  onClick={() => {
                    if (confirm(`Delete watchlist "${list.name}"?`)) m.remove.mutate(list.id, { onSuccess: () => setActiveId(null) })
                  }}
                >
                  Delete
                </Button>
              </div>
            )}
            {error && <div className={f.error}>{error}</div>}
          </div>

          <div className={s.table}>
            {rows.length ? (
              <GridTable
                columns={columns}
                rows={rows}
                rowKey={(r) => r.code}
                sortable={false}
                autoFocus
                onOpen={(r) => pane.open({ code: 'DES', ticker: r.code, args: [] })}
                onRowKey={onRowKey}
              />
            ) : (
              <Empty title="Empty watchlist" message="Add a ticker above, or press w on any ticker screen." />
            )}
          </div>
          <div className={s.keys}>
            Enter DES · g GP · Alt+↑/↓ reorder · Del remove
          </div>
        </>
      )}
    </Panel>
  )
}
