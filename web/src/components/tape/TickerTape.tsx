import { useEffect, useState } from 'react'
import { MACRO_LABELS, TAPE_CODES, useLiveQuotes } from '../../functions/mkt/quotes'
import { useWorkspace } from '../../shell/workspace'
import { Num } from '../ui'
import { loadActiveId, pickActive, useWatchlists } from '../watchlist/useWatchlists'
import s from './TickerTape.module.css'

/** Bottom tape: indexes, FX, commodities, regional markets, then the active watchlist. Pauses on hover. */
export default function TickerTape() {
  const ws = useWorkspace()
  const lists = useWatchlists()
  const [activeId, setActiveId] = useState(loadActiveId)
  useEffect(() => {
    const onActive = (e: Event) => setActiveId((e as CustomEvent<{ id: number }>).detail.id)
    window.addEventListener('watchlist:active', onActive)
    return () => window.removeEventListener('watchlist:active', onActive)
  }, [])
  const watch = pickActive(lists.data, activeId)?.items.map((i) => i.code) ?? []
  const codes = [...TAPE_CODES, ...watch.filter((c) => !TAPE_CODES.includes(c))]
  const quotes = useLiveQuotes(codes)

  const shown = codes.filter((c) => quotes[c])
  if (!shown.length) {
    return (
      <div className={s.static}>
        {TAPE_CODES.slice(0, 7).map((c) => (
          <span key={c} className={s.item}>
            <span className={s.code}>{MACRO_LABELS[c] ?? c}</span> <span className="muted">—</span>
          </span>
        ))}
      </div>
    )
  }

  const items = shown.map((c, i) => {
    const q = quotes[c]
    const sep = i === TAPE_CODES.filter((t) => quotes[t]).length && watch.length > 0
    return (
      <button
        key={c}
        className={[s.item, sep && s.sep].filter(Boolean).join(' ')}
        onClick={() => ws.run({ code: TAPE_CODES.includes(c) ? 'GP' : 'DES', ticker: c, args: [] })}
        title={q.name ?? c}
        tabIndex={-1}
      >
        <span className={s.code}>{MACRO_LABELS[c] ?? c}</span>
        <Num value={q.price} format="price" />
        <Num value={q.changePct} format="pct" signed colored arrow />
      </button>
    )
  })

  // two copies so the loop is seamless; the second is hidden from assistive tech
  return (
    <div className={s.tape} aria-label="Ticker tape">
      <div className={s.track} style={{ animationDuration: `${Math.max(30, shown.length * 4)}s` }}>
        <div className={s.group}>{items}</div>
        <div className={s.group} aria-hidden="true">
          {items}
        </div>
      </div>
    </div>
  )
}
