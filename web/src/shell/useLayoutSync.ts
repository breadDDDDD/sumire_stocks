import { useEffect, useRef } from 'react'
import { api } from '../api/client'
import { validWorkspace, type WorkspaceAction, type WorkspaceState } from './workspace'

const KEY = 'ui-layout'
const DEBOUNCE_MS = 1500

/**
 * Keeps the workspace (layout + pane history) in /api/settings/ui-layout so it follows you across browsers.
 * localStorage stays the instant source on load; the server copy wins once it arrives, unless you already
 * changed something. Without the DB both calls fail quietly and localStorage is all there is.
 */
export function useLayoutSync(state: WorkspaceState, dispatch: (a: WorkspaceAction) => void) {
  const touched = useRef(false)
  const lastSent = useRef<string | null>(null)
  const initial = useRef(state)

  useEffect(() => {
    const ctrl = new AbortController()
    api<unknown>(`/settings/${KEY}`, { signal: ctrl.signal })
      .then((remote) => {
        const ws = validWorkspace(remote)
        if (ws && !touched.current) {
          lastSent.current = JSON.stringify(ws)
          dispatch({ type: 'replace', state: ws })
        }
      })
      .catch(() => {})
    return () => ctrl.abort()
  }, [dispatch])

  useEffect(() => {
    if (state === initial.current) return
    touched.current = true
    const body = JSON.stringify(state)
    if (body === lastSent.current) return
    const id = setTimeout(() => {
      lastSent.current = body
      api(`/settings/${KEY}`, { method: 'PUT', body: state }).catch(() => {
        lastSent.current = null
      })
    }, DEBOUNCE_MS)
    return () => clearTimeout(id)
  }, [state])
}
