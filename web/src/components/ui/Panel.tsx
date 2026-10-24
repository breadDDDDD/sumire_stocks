import type { ReactNode } from 'react'
import s from './Panel.module.css'

export interface PanelProps {
  title: ReactNode
  subtitle?: ReactNode
  actions?: ReactNode
  children?: ReactNode
  padded?: boolean
  /** 1px border, for panels nested next to other panels in a pane body. */
  boxed?: boolean
  className?: string
  bodyClassName?: string
}

export function Panel({ title, subtitle, actions, children, padded, boxed, className, bodyClassName }: PanelProps) {
  return (
    <section className={[s.panel, boxed && s.boxed, className].filter(Boolean).join(' ')}>
      <header className={s.header}>
        <h2 className={s.title}>{title}</h2>
        {subtitle && <span className={s.subtitle}>{subtitle}</span>}
        {actions && <div className={s.actions}>{actions}</div>}
      </header>
      <div className={[s.body, padded && s.padded, bodyClassName].filter(Boolean).join(' ')}>{children}</div>
    </section>
  )
}
