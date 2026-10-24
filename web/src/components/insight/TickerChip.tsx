import { Num } from '../ui'
import s from './TickerChip.module.css'

export interface TickerChipProps {
  code: string
  /** Today's move in percent; live hub quotes override it when present. */
  chgPct?: number | null
  /** Live quote map from useQuotes, so a list subscribes once instead of per chip. */
  live?: Record<string, { changePct?: number | null }>
  onOpen?: (code: string) => void
  title?: string
  /** Hide the % (e.g. for factor names without a market series). */
  plain?: boolean
  highlight?: boolean
}

/** Ticker label with today's % move, colored up/down and flashing on a live tick. Click/Enter opens it. */
export function TickerChip({ code, chgPct, live, onOpen, title, plain, highlight }: TickerChipProps) {
  const pct = live?.[code]?.changePct ?? chgPct ?? null
  const cls = [s.chip, onOpen && s.clickable, highlight && s.highlight].filter(Boolean).join(' ')
  const body = (
    <>
      <span className={s.code}>{code}</span>
      {!plain && <Num value={pct} format="pct" signed colored flash className={s.pct} />}
    </>
  )
  if (!onOpen)
    return (
      <span className={cls} title={title}>
        {body}
      </span>
    )
  return (
    <button
      type="button"
      className={cls}
      title={title ?? `Open ${code}`}
      onClick={(e) => {
        e.stopPropagation()
        onOpen(code)
      }}
    >
      {body}
    </button>
  )
}
