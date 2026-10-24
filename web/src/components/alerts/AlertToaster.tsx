import { useQueryClient } from '@tanstack/react-query'
import { useEffect, useState, type KeyboardEvent } from 'react'
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

type Toast = AlertEvent & { key: string; leaving?: boolean }
type Tone = 'amber' | 'up' | 'down' | 'cyan' | 'crimson'

const TTL_MS = 8_000
const MAX = 4

/** Kind word and left-bar color: rule hits are amber, research-driven alerts cyan. */
function kindOf(type: string): { word: string; tone: Tone } {
  switch (type) {
    case 'negative_insight':
      return { word: 'Research', tone: 'cyan' }
    case 'ara':
      return { word: 'ARA', tone: 'up' }
    case 'arb':
      return { word: 'ARB', tone: 'down' }
    case 'error':
      return { word: 'Error', tone: 'crimson' }
    default:
      return { word: 'Alert', tone: 'amber' }
  }
}

/** Message without the legacy "Violet alert:" prefix and the leading code (shown separately). */
function messageText(a: AlertEvent): string {
  const text = a.message.replace(/^Violet alert:\s*/i, '')
  return a.code && text.startsWith(`${a.code} `) ? text.slice(a.code.length + 1) : text
}

const markLeaving = (key: string) => (t: Toast[]) => t.map((x) => (x.key === key ? { ...x, leaving: true } : x))

/** Toasts for the hub's `alert` event, bottom-right above the tape. Mount once inside the workspace provider. */
export default function AlertToaster() {
  const ws = useWorkspace()
  const qc = useQueryClient()
  const [toasts, setToasts] = useState<Toast[]>([])
  const [hold, setHold] = useState(false)

  useHubEvent<AlertEvent>('alert', (a) => {
    setToasts((t) => [{ ...a, key: `${a.id}-${a.time}-${Date.now()}` }, ...t].slice(0, MAX))
    void qc.invalidateQueries({ queryKey: ['alerts'] })
  })

  // the oldest toast leaves after 8s, unless the stack is hovered or focused
  useEffect(() => {
    const oldest = [...toasts].reverse().find((t) => !t.leaving)
    if (!oldest || hold) return
    const id = setTimeout(() => setToasts(markLeaving(oldest.key)), TTL_MS)
    return () => clearTimeout(id)
  }, [toasts, hold])

  if (!toasts.length) return null

  const dismiss = (key: string) => setToasts(markLeaving(key))
  const remove = (key: string) => setToasts((t) => t.filter((x) => x.key !== key))

  const onKeyDown = (e: KeyboardEvent<HTMLDivElement>, key: string) => {
    if (e.key !== 'Escape') return
    e.preventDefault()
    e.stopPropagation()
    dismiss(key)
  }

  return (
    <div
      className={s.stack}
      role="status"
      aria-live="polite"
      onPointerEnter={() => setHold(true)}
      onPointerLeave={() => setHold(false)}
      onFocus={() => setHold(true)}
      onBlur={(e) => {
        if (!e.currentTarget.contains(e.relatedTarget as Node | null)) setHold(false)
      }}
    >
      {toasts.map((a) => {
        const kind = kindOf(a.type)
        return (
          <div
            key={a.key}
            className={`${s.toast} ${s[kind.tone]}`}
            data-state={a.leaving ? 'closing' : 'open'}
            onAnimationEnd={(e) => {
              if (a.leaving && e.target === e.currentTarget) remove(a.key)
            }}
            onKeyDown={(e) => onKeyDown(e, a.key)}
          >
            <button
              type="button"
              className={s.body}
              onClick={() => {
                if (a.code) ws.run({ code: 'DES', ticker: a.code, args: [] })
                dismiss(a.key)
              }}
              title={a.code ? `Open ${a.code} DES` : undefined}
            >
              <span className={s.head}>
                <span className={s.kind}>{kind.word}</span>
                <span className={s.sep} aria-hidden="true">
                  ·
                </span>
                {a.code && <span className={s.code}>{a.code}</span>}
                <span className={s.msg}>{messageText(a)}</span>
                <span className={s.time}>{formatTimeWib(a.time)}</span>
              </span>
              <span className={s.keys}>
                {a.code && (
                  <span>
                    <kbd className="key-go">Enter</kbd> Open DES
                  </span>
                )}
                <span>
                  <kbd className="key-cancel">Esc</kbd> Dismiss
                </span>
              </span>
            </button>
            <button type="button" className={s.close} onClick={() => dismiss(a.key)} aria-label="Dismiss">
              ×
            </button>
          </div>
        )
      })}
    </div>
  )
}
