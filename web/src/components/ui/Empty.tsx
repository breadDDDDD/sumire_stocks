import type { ReactNode } from 'react'
import s from './Empty.module.css'

export function Empty({ title, message, children }: { title: ReactNode; message?: ReactNode; children?: ReactNode }) {
  return (
    <div className={`${s.empty} halftone`}>
      <span className={s.slash} aria-hidden="true" />
      <h3 className={s.title}>{title}</h3>
      {message && <p className={s.message}>{message}</p>}
      {children && <div className={s.actions}>{children}</div>}
    </div>
  )
}
