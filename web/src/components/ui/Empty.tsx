import type { ReactNode } from 'react'
import s from './Empty.module.css'
import { Starburst } from './Starburst'

export function Empty({
  title,
  message,
  children,
  tone,
}: {
  title: ReactNode
  message?: ReactNode
  children?: ReactNode
  /** "error" draws the burst as an outline and keeps the message at full contrast. */
  tone?: 'error'
}) {
  const error = tone === 'error'
  return (
    <div className={`${s.empty} ${error ? s.error : ''} halftone`} role={error ? 'alert' : undefined}>
      <div className={s.head}>
        <Starburst size={64} className={s.burst} outline={error} />
        <h3 className={s.title}>{title}</h3>
      </div>
      <span className={s.slash} aria-hidden="true" />
      {message && <p className={s.message}>{message}</p>}
      {children && <div className={s.actions}>{children}</div>}
    </div>
  )
}
