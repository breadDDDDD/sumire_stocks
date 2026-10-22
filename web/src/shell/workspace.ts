import { createContext, useContext } from 'react'
import type { FunctionProps } from '../functions/types'
import { load } from '../lib/storage'

export type Layout = 1 | 2 | 4
export const LAYOUTS: Layout[] = [1, 2, 4]
export const PANE_COUNT = 4
const HISTORY_LIMIT = 50

export interface PaneState {
  history: FunctionProps[]
  index: number
}

export interface WorkspaceState {
  layout: Layout
  focused: number
  panes: PaneState[]
}

export type WorkspaceAction =
  | { type: 'open'; entry: FunctionProps; pane?: number }
  | { type: 'back'; pane?: number }
  | { type: 'forward'; pane?: number }
  | { type: 'focus'; pane: number }
  | { type: 'cycle'; delta: 1 | -1 }
  | { type: 'layout'; layout?: Layout }

export const STORAGE_KEY = 'sumire.workspace.v1'

const DEFAULT_ENTRIES: FunctionProps[] = [
  { code: 'MKT', args: [] },
  { code: 'BRIEF', args: [] },
  { code: 'N', args: [] },
  { code: 'RQ', args: [] },
]

export function initialWorkspace(): WorkspaceState {
  const fallback: WorkspaceState = {
    layout: 4,
    focused: 0,
    panes: DEFAULT_ENTRIES.map((e) => ({ history: [e], index: 0 })),
  }
  const saved = load<WorkspaceState | null>(STORAGE_KEY, null)
  if (
    !saved ||
    !LAYOUTS.includes(saved.layout) ||
    !Array.isArray(saved.panes) ||
    saved.panes.length !== PANE_COUNT ||
    saved.panes.some((p) => !Array.isArray(p?.history) || !p.history.length || !p.history[p.index])
  ) {
    return fallback
  }
  return { ...saved, focused: Math.min(Math.max(0, saved.focused | 0), saved.layout - 1) }
}

function sameEntry(a: FunctionProps, b: FunctionProps) {
  return a.code === b.code && a.ticker === b.ticker && !!a.force === !!b.force && a.args.join(' ') === b.args.join(' ')
}

function updatePane(state: WorkspaceState, i: number, fn: (p: PaneState) => PaneState): WorkspaceState {
  const panes = state.panes.slice()
  panes[i] = fn(panes[i])
  return { ...state, panes }
}

export function workspaceReducer(state: WorkspaceState, action: WorkspaceAction): WorkspaceState {
  switch (action.type) {
    case 'open': {
      const i = action.pane ?? state.focused
      const next = updatePane(state, i, (p) => {
        if (sameEntry(p.history[p.index], action.entry) && !action.entry.force) return p
        const history = [...p.history.slice(0, p.index + 1), action.entry].slice(-HISTORY_LIMIT)
        return { history, index: history.length - 1 }
      })
      return { ...next, focused: i }
    }
    case 'back':
      return updatePane(state, action.pane ?? state.focused, (p) => (p.index > 0 ? { ...p, index: p.index - 1 } : p))
    case 'forward':
      return updatePane(state, action.pane ?? state.focused, (p) =>
        p.index < p.history.length - 1 ? { ...p, index: p.index + 1 } : p,
      )
    case 'focus':
      return action.pane < state.layout ? { ...state, focused: action.pane } : state
    case 'cycle':
      return { ...state, focused: (state.focused + action.delta + state.layout) % state.layout }
    case 'layout': {
      const layout = action.layout ?? LAYOUTS[(LAYOUTS.indexOf(state.layout) + 1) % LAYOUTS.length]
      return { ...state, layout, focused: Math.min(state.focused, layout - 1) }
    }
  }
}

export function currentEntry(p: PaneState): FunctionProps {
  return p.history[p.index]
}

export interface WorkspaceApi {
  state: WorkspaceState
  dispatch: (a: WorkspaceAction) => void
  /** Run a command string or entry in a pane (default: focused). Returns an error message or null. */
  run: (command: string | FunctionProps, pane?: number) => string | null
  /** Ticker shown in the focused pane, if any. */
  focusedTicker?: string
}

export const WorkspaceContext = createContext<WorkspaceApi | null>(null)
export const PaneIndexContext = createContext<number | null>(null)

export function useWorkspace(): WorkspaceApi {
  const ctx = useContext(WorkspaceContext)
  if (!ctx) throw new Error('useWorkspace outside WorkspaceProvider')
  return ctx
}

/**
 * For function components: open something in the pane they live in.
 * `open('BBRI GP')` or `open({ code: 'DES', ticker: 'BBRI', args: [] })`.
 * Outside a pane it targets the focused pane.
 */
export function usePane() {
  const ws = useWorkspace()
  const index = useContext(PaneIndexContext)
  return {
    index,
    focused: index === null || index === ws.state.focused,
    open: (command: string | FunctionProps) => ws.run(command, index ?? undefined),
  }
}
