import { Suspense, useEffect, useRef, useState } from 'react'
import { Skeleton } from '../components/ui'
import { registry } from '../functions/registry'
import { formatEntry } from '../lib/command'
import { reducedMotion } from '../lib/motion'
import { ErrorBoundary } from './ErrorBoundary'
import { RELATED } from './related'
import { currentEntry, PaneIndexContext, useWorkspace, type PaneState } from './workspace'
import s from './Pane.module.css'

type Dir = 'open' | 'back' | 'fwd'

/** Whether the pane's last change was a new command or a history step, so the body enters from the matching side. */
function useEnterDirection(pane: PaneState): Dir {
  const [last, setLast] = useState<{ index: number; history: PaneState['history']; dir: Dir }>({
    index: pane.index,
    history: pane.history,
    dir: 'open',
  })
  if (last.index !== pane.index || last.history !== pane.history) {
    const dir: Dir = last.history !== pane.history ? 'open' : pane.index < last.index ? 'back' : 'fwd'
    setLast({ index: pane.index, history: pane.history, dir })
    return dir
  }
  return last.dir
}

export function Pane({ index }: { index: number }) {
  const { state, dispatch, run } = useWorkspace()
  const pane = state.panes[index]
  const entry = currentEntry(pane)
  const focused = state.focused === index
  const Fn = registry[entry.code]?.component ?? registry.HELP.component
  const canBack = pane.index > 0
  const canForward = pane.index < pane.history.length - 1
  const dir = useEnterDirection(pane)
  const related = entry.ticker && entry.code !== 'COMP' ? RELATED[entry.code] : undefined
  const bodyKey = `${pane.index}:${formatEntry(entry)}`
  const code = entry.code === 'INS' && entry.force ? 'INS!' : entry.code

  // when the boot overlay lifts, the panes underneath enter once more, 30 ms apart
  const bodyRef = useRef<HTMLDivElement>(null)
  useEffect(() => {
    const onBoot = () => {
      if (reducedMotion()) return
      bodyRef.current?.animate(
        [
          { opacity: 0, transform: 'translateY(3px)' },
          { opacity: 1, transform: 'none' },
        ],
        { duration: 120, delay: index * 30, easing: 'cubic-bezier(0.2, 0.8, 0.2, 1)', fill: 'backwards' },
      )
    }
    window.addEventListener('sumire:boot-out', onBoot)
    return () => window.removeEventListener('sumire:boot-out', onBoot)
  }, [index])

  return (
    <div
      className={`${s.pane} ${focused ? s.focused : ''}`}
      data-focused={focused || undefined}
      data-pane={index}
      onMouseDown={() => !focused && dispatch({ type: 'focus', pane: index })}
      onFocusCapture={() => !focused && dispatch({ type: 'focus', pane: index })}
    >
      <div className={s.strip}>
        <span className={s.num}>{index + 1}</span>
        <span className={s.cmd} title={formatEntry(entry)}>
          {focused ? <kbd className={`key-sector ${s.code}`}>{code}</kbd> : <span className={s.codePlain}>{code}</span>}
          {entry.code !== 'COMP' && entry.ticker && <span className={s.ticker}>{entry.ticker}</span>}
          {entry.args.length > 0 && <span className={s.args}>{entry.args.join(' ')}</span>}
          <span className={s.name}>{registry[entry.code]?.name}</span>
        </span>
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
      <div ref={bodyRef} className={s.body}>
        <PaneIndexContext.Provider value={index}>
          <ErrorBoundary key={bodyKey} onBack={canBack ? () => dispatch({ type: 'back', pane: index }) : undefined}>
            <div className={s.enter} data-dir={dir}>
              <Suspense
                fallback={
                  <div className={s.delayed}>
                    <Skeleton rows={8} />
                  </div>
                }
              >
                <Fn key={pane.index} {...entry} />
              </Suspense>
            </div>
          </ErrorBoundary>
        </PaneIndexContext.Provider>
      </div>
      {related && (
        <nav className={s.related} aria-label="Related functions">
          {related.map((rc) => (
            <button
              key={rc}
              type="button"
              className={s.relBtn}
              tabIndex={-1}
              onClick={() => run({ code: rc, ticker: entry.ticker, args: [] }, index)}
              title={`${entry.ticker} ${rc}`}
            >
              <span className={s.relCode}>{rc}</span>
              <span className={s.relName}>{registry[rc]?.name}</span>
            </button>
          ))}
        </nav>
      )}
    </div>
  )
}
