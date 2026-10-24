import { useEffect, useLayoutEffect, useRef, useState } from 'react'
import { MACRO_LABELS, TAPE_CODES, useLiveQuotes } from '../../functions/mkt/quotes'
import { formatNum } from '../../lib/format'
import { sessionState } from '../../lib/session'
import { useWorkspace } from '../../shell/workspace'
import { Num } from '../ui'
import { loadActiveId, pickActive, useWatchlists } from '../watchlist/useWatchlists'
import s from './TickerTape.module.css'

/** Scroll speed in px per second; the duration follows the content width so the speed never changes. */
const SPEED = 48

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

  // IHSG in the tab title while the market is live
  const ihsg = quotes.IHSG
  useEffect(() => {
    const base = 'Sumire'
    if (!ihsg || sessionState() !== 'LIVE') {
      document.title = base
      return
    }
    document.title = `IHSG ${formatNum(ihsg.price, 'price', { decimals: 0 })} ${formatNum(ihsg.changePct, 'pct', { signed: true }).replace('-', '−')} · ${base}`
  }, [ihsg])

  // measure one copy and only rewrite the duration on a >2% width change, so new items don't jump the loop
  const groupRef = useRef<HTMLDivElement>(null)
  const trackRef = useRef<HTMLDivElement>(null)
  const width = useRef(0)
  const hasItems = shown.length > 0
  useLayoutEffect(() => {
    const group = groupRef.current
    const track = trackRef.current
    if (!group || !track) return
    const apply = () => {
      const w = group.getBoundingClientRect().width
      if (!w || Math.abs(w - width.current) / Math.max(1, width.current) < 0.02) return
      width.current = w
      track.style.setProperty('--tape-dur', `${(w / SPEED).toFixed(2)}s`)
    }
    apply()
    const ro = new ResizeObserver(apply)
    ro.observe(group)
    return () => ro.disconnect()
  }, [hasItems])

  // a hidden tab doesn't need to paint a moving strip
  const [hidden, setHidden] = useState(() => typeof document !== 'undefined' && document.hidden)
  useEffect(() => {
    const onVis = () => setHidden(document.hidden)
    document.addEventListener('visibilitychange', onVis)
    return () => document.removeEventListener('visibilitychange', onVis)
  }, [])

  if (!hasItems) {
    return (
      <div className={s.static} aria-label="Ticker tape">
        {TAPE_CODES.slice(0, 7).map((c) => (
          <span key={c} className={s.item}>
            <span className={s.code}>{MACRO_LABELS[c] ?? c}</span>
            <span className={s.empty}>—</span>
          </span>
        ))}
      </div>
    )
  }

  const macroCount = TAPE_CODES.filter((t) => quotes[t]).length
  const items = () =>
    shown.map((c, i) => {
      const q = quotes[c]
      return (
        <button
          key={c}
          type="button"
          className={[s.item, i === macroCount && watch.length > 0 && s.sep].filter(Boolean).join(' ')}
          onClick={() => ws.run({ code: TAPE_CODES.includes(c) ? 'GP' : 'DES', ticker: c, args: [] })}
          title={q.name ?? c}
          tabIndex={-1}
        >
          <span className={s.code}>{MACRO_LABELS[c] ?? c}</span>
          <Num value={q.price} format="price" flash className={s.price} />
          <Num value={q.changePct} format="pct" signed colored arrow className={s.pct} />
        </button>
      )
    })

  // two copies so the loop is seamless; the second is hidden from assistive tech
  return (
    <div className={`${s.tape} ${hidden ? s.paused : ''}`} aria-label="Ticker tape">
      <div ref={trackRef} className={s.track}>
        <div ref={groupRef} className={s.group}>
          {items()}
        </div>
        <div className={s.group} aria-hidden="true">
          {items()}
        </div>
      </div>
    </div>
  )
}
