import { useEffect, useRef } from 'react'
import { Button, Panel } from '../components/ui'
import { CheatSheet } from '../functions/help/CheatSheet'
import s from './App.module.css'

export function ShortcutOverlay({ onClose, onRun }: { onClose: () => void; onRun: (cmd: string) => void }) {
  const ref = useRef<HTMLDivElement>(null)
  useEffect(() => {
    const prev = document.activeElement as HTMLElement | null
    ref.current?.focus()
    return () => prev?.focus?.()
  }, [])

  return (
    <div className={s.overlay} onMouseDown={(e) => e.target === e.currentTarget && onClose()}>
      <div
        ref={ref}
        className={s.dialog}
        role="dialog"
        aria-modal="true"
        aria-label="Shortcuts"
        tabIndex={-1}
        onKeyDown={(e) => {
          if (e.key === 'Escape' || e.key === '?') {
            e.preventDefault()
            e.stopPropagation()
            onClose()
          }
        }}
      >
        <Panel
          title="Shortcuts"
          subtitle="Everything is reachable from the keyboard"
          actions={
            <Button variant="ghost" onClick={onClose} aria-label="Close">
              <kbd className="key-cancel">ESC</kbd>
            </Button>
          }
        >
          <CheatSheet
            onRun={(cmd) => {
              onClose()
              onRun(cmd)
            }}
          />
        </Panel>
      </div>
    </div>
  )
}
