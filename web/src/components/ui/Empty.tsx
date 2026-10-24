import type { ReactNode } from 'react'
import s from './Empty.module.css'

export function Empty({
  title,
  message,
  children,
  tone,
}: {
  title: ReactNode
  message?: ReactNode
  children?: ReactNode
  /** "error" keeps the message at full contrast and announces it as an alert. */
  tone?: 'error'
}) {
  const error = tone === 'error'
  return (
    <div className={`${s.empty} ${error ? s.error : ''}`} role={error ? 'alert' : undefined}>
      <h3 className={s.title}>{title}</h3>
      {message && <p className={s.message}>{message}</p>}
      {children && <div className={s.actions}>{children}</div>}
    </div>
  )
}
