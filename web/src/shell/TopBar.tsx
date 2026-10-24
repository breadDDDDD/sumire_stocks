import { forwardRef, useEffect, useState } from 'react'
import { CutText } from '../components/ui'
import { sessionState, type SessionState } from '../lib/session'
import { formatTimeWib } from '../lib/time'
import { CommandLine, type CommandLineHandle } from './CommandLine'
import { useWorkspace, type Layout } from './workspace'
import s from './TopBar.module.css'

function useNow(ms = 1000) {
  const [now, setNow] = useState(() => new Date())
  useEffect(() => {
    const id = setInterval(() => setNow(new Date()), ms)
    return () => clearInterval(id)
  }, [ms])
  return now
}

const SESSION_LABEL: Record<SessionState, string> = {
  'PRE-OPEN': 'Pre-open',
  LIVE: 'Live',
  BREAK: 'Break',
  'PRE-CLOSE': 'Pre-close',
  POST: 'Post-trading',
  CLOSED: 'Closed',
}

function SessionPill({ state }: { state: SessionState }) {
  const tone = state === 'LIVE' ? s.live : state === 'CLOSED' ? s.closed : s.pending
  return (
    <span className={`${s.pill} ${tone}`} title={`IDX session (WIB clock): ${SESSION_LABEL[state]}`}>
      <span className={s.dot} />
      {state}
    </span>
  )
}

function Clock({ time }: { time: string }) {
  // "14:32:07": seconds are secondary, so they sit in a dimmer tone
  const cut = time.lastIndexOf(':')
  return (
    <span className={s.clock}>
      {cut > 0 ? time.slice(0, cut) : time}
      {cut > 0 && <span className={s.sec}>{time.slice(cut)}</span>}
      <small>WIB</small>
    </span>
  )
}

function LayoutIcon({ layout }: { layout: Layout }) {
  if (layout === 4)
    return (
      <span className={s.grid4}>
        <span className={s.cell} />
        <span className={s.cell} />
        <span className={s.cell} />
        <span className={s.cell} />
      </span>
    )
  return (
    <>
      {Array.from({ length: layout }, (_, i) => (
        <span key={i} className={s.cell} style={{ height: 14 }} />
      ))}
    </>
  )
}

export const TopBar = forwardRef<CommandLineHandle>(function TopBar(_props, ref) {
  const now = useNow()
  const { state, dispatch } = useWorkspace()
  return (
    <header className={s.bar}>
      <div className={s.brand}>
        <CutText text="SUMIRE" className={s.wordmark} />
        <span className={s.codename}>VIOLET</span>
      </div>
      <CommandLine ref={ref} />
      <div className={s.right}>
        <button
          type="button"
          className={s.layoutBtn}
          onClick={() => dispatch({ type: 'layout' })}
          title={`Layout: ${state.layout} pane${state.layout > 1 ? 's' : ''} (Alt+L)`}
          aria-label="Change layout"
        >
          <LayoutIcon layout={state.layout} />
        </button>
        <Clock time={formatTimeWib(now)} />
        <SessionPill state={sessionState(now)} />
      </div>
    </header>
  )
})
