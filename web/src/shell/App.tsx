import { useCallback, useEffect, useMemo, useReducer, useRef, useState } from 'react'
import AlertToaster from '../components/alerts/AlertToaster'
import { Boot } from '../components/boot/Boot'
import TickerTape from '../components/tape/TickerTape'
import WatchlistRail from '../components/watchlist/WatchlistRail'
import type { FunctionProps } from '../functions/types'
import { parseCommand } from '../lib/command'
import { reducedMotion } from '../lib/motion'
import { save } from '../lib/storage'
import type { CommandLineHandle } from './CommandLine'
import { Pane } from './Pane'
import { ShortcutOverlay } from './ShortcutOverlay'
import { StatusBar } from './StatusBar'
import { TopBar } from './TopBar'
import { useLayoutSync } from './useLayoutSync'
import {
  currentEntry,
  initialWorkspace,
  STORAGE_KEY,
  WorkspaceContext,
  workspaceReducer,
  type WorkspaceApi,
} from './workspace'
import s from './App.module.css'

function isTypingTarget(el: EventTarget | null): boolean {
  if (!(el instanceof HTMLElement)) return false
  return el.isContentEditable || el.tagName === 'INPUT' || el.tagName === 'TEXTAREA' || el.tagName === 'SELECT'
}

function tickerOf(e: FunctionProps): string | undefined {
  return e.ticker ?? (e.code === 'COMP' ? e.args[0] : undefined)
}

export default function App() {
  const [state, dispatch] = useReducer(workspaceReducer, undefined, initialWorkspace)
  const [helpOpen, setHelpOpen] = useState(false)
  const [toast, setToast] = useState<string | null>(null)
  const cmdRef = useRef<CommandLineHandle>(null)

  useEffect(() => save(STORAGE_KEY, state), [state])
  useLayoutSync(state, dispatch)
  useEffect(() => {
    if (!toast) return
    const id = setTimeout(() => setToast(null), 2500)
    return () => clearTimeout(id)
  }, [toast])

  const focusedTicker = tickerOf(currentEntry(state.panes[state.focused]))

  const run = useCallback(
    (command: string | FunctionProps, pane?: number): string | null => {
      let entry: FunctionProps
      if (typeof command === 'string') {
        const parsed = parseCommand(command, focusedTicker)
        if (!parsed.ok) return parsed.error
        entry = parsed.entry
      } else entry = command
      dispatch({ type: 'open', entry, pane })
      return null
    },
    [focusedTicker],
  )

  const api = useMemo<WorkspaceApi>(() => ({ state, dispatch, run, focusedTicker }), [state, run, focusedTicker])

  // keep the latest values reachable from the single window listener
  const live = useRef({ api, helpOpen })
  useEffect(() => {
    live.current = { api, helpOpen }
  })

  useEffect(() => {
    const focusPaneContent = (index: number) => {
      requestAnimationFrame(() => {
        const el = document.querySelector<HTMLElement>(`[data-pane="${index}"] [tabindex="0"]`)
        if (el) el.focus({ preventScroll: true })
        else (document.activeElement as HTMLElement | null)?.blur?.()
      })
    }

    const onKey = (e: KeyboardEvent) => {
      if (e.defaultPrevented || e.isComposing) return
      const { api, helpOpen } = live.current
      if (helpOpen) return
      const { state, dispatch, run, focusedTicker } = api

      // shortcuts that also work while typing
      if ((e.ctrlKey || e.metaKey) && !e.altKey && e.key.toLowerCase() === 'k') {
        e.preventDefault()
        cmdRef.current?.focus()
        return
      }
      if (e.altKey && !e.ctrlKey && !e.metaKey) {
        const digit = /^Digit([1-4])$/.exec(e.code)
        if (digit) {
          e.preventDefault()
          const i = Number(digit[1]) - 1
          if (i < state.layout) {
            dispatch({ type: 'focus', pane: i })
            focusPaneContent(i)
          }
          return
        }
        if (e.key === 'ArrowLeft' || e.key === 'ArrowRight') {
          e.preventDefault()
          dispatch({ type: e.key === 'ArrowLeft' ? 'back' : 'forward' })
          return
        }
        if (e.code === 'KeyL') {
          e.preventDefault()
          dispatch({ type: 'layout' })
          return
        }
      }

      if (isTypingTarget(e.target) || e.ctrlKey || e.metaKey || e.altKey) return

      const needTicker = (fn: (t: string) => void) => {
        if (focusedTicker) fn(focusedTicker)
        else setToast('No ticker in this pane. Type one first, e.g. BBCA')
      }

      switch (e.key) {
        case '/':
          e.preventDefault()
          cmdRef.current?.focus()
          return
        case '?':
          e.preventDefault()
          setHelpOpen(true)
          return
        case 'Escape':
          e.preventDefault()
          dispatch({ type: 'back' })
          return
        case 'Tab': {
          if (state.layout === 1) return
          e.preventDefault()
          const next = (state.focused + (e.shiftKey ? -1 : 1) + state.layout) % state.layout
          dispatch({ type: 'cycle', delta: e.shiftKey ? -1 : 1 })
          focusPaneContent(next)
          return
        }
        case 'n':
          e.preventDefault()
          needTicker((t) => run({ code: 'INS', ticker: t, args: [] }))
          return
        case 'r':
          e.preventDefault()
          needTicker((t) => run({ code: 'REL', ticker: t, args: [] }))
          return
        case 'w':
          e.preventDefault()
          needTicker((t) => {
            window.dispatchEvent(new CustomEvent('watchlist:add', { detail: { ticker: t } }))
            setToast(`${t} → watchlist`)
          })
          return
      }

      // any other printable key starts a command
      if (e.key.length === 1 && /[\p{L}\p{N}^]/u.test(e.key)) {
        e.preventDefault()
        cmdRef.current?.focus(e.key.toUpperCase())
      }
    }

    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [])

  // layout change: the grid moves instantly, the panes crossfade
  const gridRef = useRef<HTMLElement>(null)
  const firstLayout = useRef(true)
  useEffect(() => {
    if (firstLayout.current) {
      firstLayout.current = false
      return
    }
    if (reducedMotion()) return
    gridRef.current?.animate([{ opacity: 0.4 }, { opacity: 1 }], { duration: 120, easing: 'cubic-bezier(0.2, 0.8, 0.2, 1)' })
  }, [state.layout])

  const visible = Array.from({ length: state.layout }, (_, i) => i)

  return (
    <WorkspaceContext.Provider value={api}>
      <div className={s.app}>
        <TopBar ref={cmdRef} />
        <div className={s.main}>
          <aside className={s.rail} aria-label="Watchlist">
            <WatchlistRail />
          </aside>
          <main ref={gridRef} className={`${s.grid} ${s[`layout${state.layout}`]}`}>
            {visible.map((i) => (
              <Pane key={i} index={i} />
            ))}
          </main>
        </div>
        <div className={s.tape}>
          <TickerTape />
        </div>
        <footer className={s.status}>
          <StatusBar />
        </footer>
      </div>
      <Boot />
      <AlertToaster />
      {helpOpen && (
        <ShortcutOverlay
          onClose={() => setHelpOpen(false)}
          onRun={(cmd) => {
            const err = run(cmd)
            if (err) setToast(err)
          }}
        />
      )}
      {toast && (
        <div className={s.toast} role="status">
          {toast}
        </div>
      )}
    </WorkspaceContext.Provider>
  )
}
