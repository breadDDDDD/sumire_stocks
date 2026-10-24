import { forwardRef, useEffect, useState } from 'react'
import { SumireMark } from '../components/brand/SumireMark'
import { registry } from '../functions/registry'
import { sessionState, type SessionState } from '../lib/session'
import { formatTimeWib } from '../lib/time'
import { CommandLine, type CommandLineHandle } from './CommandLine'
import { currentEntry, useWorkspace, type Layout } from './workspace'
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
  POST: 'Post',
  CLOSED: 'Closed',
}

function SessionLamp({ state }: { state: SessionState }) {
  const tone = state === 'LIVE' ? s.live : state === 'CLOSED' ? s.closed : s.pending
  return (
    <span className={`${s.session} ${tone}`} title={`IDX session (WIB clock): ${SESSION_LABEL[state]}`}>
      <span className={s.lamp} aria-hidden="true" />
      {SESSION_LABEL[state]}
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
  return (
    <span className={`${s.glyph} ${s[`glyph${layout}`]}`} aria-hidden="true">
      {Array.from({ length: layout }, (_, i) => (
        <span key={i} className={s.cell} />
      ))}
    </span>
  )
}

export const TopBar = forwardRef<CommandLineHandle>(function TopBar(_props, ref) {
  const now = useNow()
  const { state, dispatch, focusedTicker } = useWorkspace()
  const entry = currentEntry(state.panes[state.focused])
  const fnName = registry[entry.code]?.name
  return (
    <header className={s.bar}>
      <div className={s.brand}>
        <SumireMark size={16} />
        <span className={s.wordmark}>Sumire</span>
      </div>
      {focusedTicker && (
        <span className={s.security} title="Ticker in the focused pane">
          {focusedTicker}
          <span className={s.caret} aria-hidden="true">
            ▾
          </span>
        </span>
      )}
      <CommandLine ref={ref} />
      {fnName && (
        <span className={s.slab} title={`Focused pane ${state.focused + 1}: ${entry.code}`}>
          {fnName}
        </span>
      )}
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
        <SessionLamp state={sessionState(now)} />
      </div>
    </header>
  )
})
