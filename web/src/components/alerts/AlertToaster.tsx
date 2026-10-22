import { useQueryClient } from '@tanstack/react-query'
import { useEffect, useState } from 'react'
import { useHubEvent } from '../../api/signalr'
import { formatTimeWib } from '../../lib/time'
import { useWorkspace } from '../../shell/workspace'
import s from './AlertToaster.module.css'

interface AlertEvent {
  id: number
  code: string | null
  type: string
  message: string
  time: string
}

const TTL_MS = 10_000
const MAX = 4

/** "Violet alert" toasts for the hub's `alert` event. Mount once inside the workspace provider. */
export default function AlertToaster() {
  const ws = useWorkspace()
  const qc = useQueryClient()
  const [toasts, setToasts] = useState<(AlertEvent & { key: string })[]>([])

  useHubEvent<AlertEvent>('alert', (a) => {
    setToasts((t) => [{ ...a, key: `${a.id}-${a.time}-${Date.now()}` }, ...t].slice(0, MAX))
    void qc.invalidateQueries({ queryKey: ['alerts'] })
  })

  useEffect(() => {
    if (!toasts.length) return
    const id = setTimeout(() => setToasts((t) => t.slice(0, -1)), TTL_MS)
    return () => clearTimeout(id)
  }, [toasts])

  if (!toasts.length) return null
  const dismiss = (key: string) => setToasts((t) => t.filter((x) => x.key !== key))

  return (
    <div className={s.stack} role="status" aria-live="polite">
      {toasts.map((a) => (
        <div key={a.key} className={s.toast}>
          <button
            className={s.body}
            onClick={() => {
              if (a.code) ws.run({ code: 'DES', ticker: a.code, args: [] })
              dismiss(a.key)
            }}
            title={a.code ? `Open ${a.code}` : undefined}
          >
            <span className={s.head}>
              <span className={s.brand}>Violet alert</span>
              <span className={s.time}>{formatTimeWib(a.time)} WIB</span>
            </span>
            <span className={s.msg}>{a.message.replace(/^Violet alert:\s*/i, '')}</span>
          </button>
          <button className={s.close} onClick={() => dismiss(a.key)} aria-label="Dismiss">
            ×
          </button>
        </div>
      ))}
    </div>
  )
}
