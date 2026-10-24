import { Suspense } from 'react'
import { Skeleton } from '../components/ui'
import { registry } from '../functions/registry'
import { formatEntry } from '../lib/command'
import { ErrorBoundary } from './ErrorBoundary'
import { currentEntry, PaneIndexContext, useWorkspace } from './workspace'
import s from './Pane.module.css'

export function Pane({ index }: { index: number }) {
  const { state, dispatch } = useWorkspace()
  const pane = state.panes[index]
  const entry = currentEntry(pane)
  const focused = state.focused === index
  const Fn = registry[entry.code]?.component ?? registry.HELP.component
  const canBack = pane.index > 0
  const canForward = pane.index < pane.history.length - 1

  return (
    <div
      className={`${s.pane} ${focused ? s.focused : ''}`}
      data-pane={index}
      onMouseDown={() => !focused && dispatch({ type: 'focus', pane: index })}
      onFocusCapture={() => !focused && dispatch({ type: 'focus', pane: index })}
    >
      {focused && <span className={s.ribbon} aria-hidden="true" />}
      <div className={s.strip}>
        <span className={s.num}>{index + 1}</span>
        <span className={s.cmd} title={formatEntry(entry)}>
          <kbd className={focused ? 'key-fn' : undefined}>{entry.code === 'INS' && entry.force ? 'INS!' : entry.code}</kbd>
          {entry.code !== 'COMP' && entry.ticker && <span className={s.ticker}>{entry.ticker}</span>}
          {entry.args.length > 0 && <span className={s.args}>{entry.args.join(' ')}</span>}
        </span>
        <span className="muted">{registry[entry.code]?.name}</span>
        <span className={s.nav}>
          <button
            type="button"
            className={s.navBtn}
            disabled={!canBack}
            onClick={() => dispatch({ type: 'back', pane: index })}
            title="Back (Alt+←)"
            aria-label="Back"
            tabIndex={-1}
          >
            ‹
          </button>
          <button
            type="button"
            className={s.navBtn}
            disabled={!canForward}
            onClick={() => dispatch({ type: 'forward', pane: index })}
            title="Forward (Alt+→)"
            aria-label="Forward"
            tabIndex={-1}
          >
            ›
          </button>
        </span>
      </div>
      <div className={s.body}>
        <PaneIndexContext.Provider value={index}>
          <ErrorBoundary key={`${pane.index}:${formatEntry(entry)}`} onBack={canBack ? () => dispatch({ type: 'back', pane: index }) : undefined}>
            <Suspense fallback={<Skeleton rows={8} />}>
              <Fn key={pane.index} {...entry} />
            </Suspense>
          </ErrorBoundary>
        </PaneIndexContext.Provider>
      </div>
    </div>
  )
}
