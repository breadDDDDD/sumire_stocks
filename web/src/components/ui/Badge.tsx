import type { ReactNode } from 'react'
import s from './Badge.module.css'

export type BadgeTone = 'default' | 'amber' | 'crimson' | 'up' | 'down' | 'muted' | 'solid'

export function Badge({ tone = 'default', title, children }: { tone?: BadgeTone; title?: string; children: ReactNode }) {
  return (
    <span className={`${s.badge} ${s[tone]}`} title={title}>
      {children}
    </span>
  )
}
