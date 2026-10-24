import { useEffect, useLayoutEffect, useRef, useState } from 'react'
import { ApiError } from '../../api/client'
import { useLiveQuotes } from '../../functions/mkt/quotes'
import { useWorkspace } from '../../shell/workspace'
import { Num, Skeleton } from '../ui'
import { loadActiveId, pickActive, saveActiveId, useWatchlistMutations, useWatchlists } from './useWatchlists'
import s from './WatchlistRail.module.css'

/** Left rail: compact live view of the active watchlist. Also handles the shell's `w` (watchlist:add) shortcut. */
export default function WatchlistRail() {
  const ws = useWorkspace()
  const q = useWatchlists()
  const { addItem } = useWatchlistMutations()
  const [activeId, setActiveId] = useState(loadActiveId)
  const [note, setNote] = useState<{ text: string; error?: boolean } | null>(null)

  const list = pickActive(q.data, activeId)
  const codes = list?.items.map((i) => i.code) ?? []
  const quotes = useLiveQuotes(codes)

  // rows added after the list first loaded enter with a short slide; a list switch does not animate
  const codesKey = `${list?.id ?? ''}:${codes.join(',')}`
  const [seen, setSeen] = useState<{ key: string; listId?: number; codes: string[]; added: string[] }>({
    key: codesKey,
    listId: list?.id,
    codes,
    added: [],
  })
  if (seen.key !== codesKey) {
    const added = seen.listId === list?.id && seen.codes.length ? codes.filter((c) => !seen.codes.includes(c)) : []
    setSeen({ key: codesKey, listId: list?.id, codes, added })
  }

  // one crimson marker that slides to the focused ticker's row
  const listRef = useRef<HTMLUListElement>(null)
  const [marker, setMarker] = useState<{ y: number; instant: boolean } | null>(null)
  const current = ws.focusedTicker
  useLayoutEffect(() => {
    const row = current ? listRef.current?.querySelector<HTMLElement>(`[data-code="${CSS.escape(current)}"]`) : null
    setMarker((m) => (row ? { y: row.offsetTop, instant: m === null } : null))
  }, [current, codesKey])

  // keep the W screen and the rail on the same list
  useEffect(() => {
    const onActive = (e: Event) => setActiveId((e as CustomEvent<{ id: number }>).detail.id)
    window.addEventListener('watchlist:active', onActive)
    return () => window.removeEventListener('watchlist:active', onActive)
  }, [])

  const target = list
  const mutate = addItem.mutate
  useEffect(() => {
    const onAdd = (e: Event) => {
      const ticker = (e as CustomEvent<{ ticker?: string }>).detail?.ticker?.toUpperCase()
      if (!ticker) return
      if (!target) {
        setNote({ text: 'Watchlists are unavailable right now', error: true })
        return
      }
      if (target.items.some((i) => i.code === ticker)) {
        setNote({ text: `${ticker} is already in ${target.name}` })
        return
      }
      mutate(
        { id: target.id, code: ticker },
        {
          onSuccess: () => setNote({ text: `${ticker} added to ${target.name}` }),
          onError: (err) =>
            setNote({ text: err instanceof ApiError ? `Could not add ${ticker}: ${err.message}` : `Could not add ${ticker}`, error: true }),
        },
      )
    }
    window.addEventListener('watchlist:add', onAdd)
    return () => window.removeEventListener('watchlist:add', onAdd)
  }, [target, mutate])

  useEffect(() => {
    if (!note) return
    const id = setTimeout(() => setNote(null), 3000)
    return () => clearTimeout(id)
  }, [note])

  return (
    <div className={s.rail}>
      <div className={s.head}>
        <span className={s.title}>Watchlist</span>
        {q.data && q.data.length > 1 ? (
          <select
            className={s.select}
            value={list?.id}
            aria-label="Active watchlist"
            onChange={(e) => {
              const id = Number(e.target.value)
              setActiveId(id)
              saveActiveId(id)
            }}
          >
            {q.data.map((l) => (
              <option key={l.id} value={l.id}>
                {l.name}
              </option>
            ))}
          </select>
        ) : (
          list && <span className={s.listName}>{list.name}</span>
        )}
        <button type="button" className={s.link} onClick={() => ws.run('W')} title="Manage watchlists (W)">
          Edit
        </button>
      </div>

      {q.isPending ? (
        <Skeleton rows={8} columns={2} />
      ) : q.isError ? (
        <div className={s.msg}>
          <p className={s.msgTitle}>Watchlists unavailable</p>
          <p>They need the database, which is not reachable.</p>
          <button type="button" className={s.link} onClick={() => q.refetch()}>
            Retry
          </button>
        </div>
      ) : !codes.length ? (
        <div className={s.msg}>
          <p>
            No tickers yet. Type a ticker and press <kbd>w</kbd>.
          </p>
        </div>
      ) : (
        <ul ref={listRef} className={s.list}>
          {marker && (
            <li
              className={`${s.marker} ${marker.instant ? s.instant : ''}`}
              style={{ transform: `translateY(${marker.y}px)` }}
              aria-hidden="true"
            />
          )}
          {codes.map((code) => {
            const qt = quotes[code]
            return (
              <li key={code} data-code={code} className={seen.added.includes(code) ? s.added : undefined}>
                <button
                  type="button"
                  className={[s.row, current === code && s.current].filter(Boolean).join(' ')}
                  onClick={() => ws.run({ code: 'DES', ticker: code, args: [] })}
                  title={qt?.name ?? code}
                  aria-current={current === code || undefined}
                >
                  <span className={s.code}>{code}</span>
                  <Num value={qt?.price} format="price" flash className={s.last} />
                  <Num value={qt?.changePct} format="pct" signed colored className={s.pct} />
                </button>
              </li>
            )
          })}
        </ul>
      )}

      {note && (
        <div className={[s.note, note.error && s.noteError].filter(Boolean).join(' ')} role="status">
          {note.text}
        </div>
      )}
    </div>
  )
}
