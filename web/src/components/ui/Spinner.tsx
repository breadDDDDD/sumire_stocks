import type { CSSProperties } from 'react'
import s from './Spinner.module.css'

/** Indeterminate amber bar on a hairline track, plus a label. Prefer Skeleton for tables and lists. */
export function Spinner({ size = 40, label, center }: { size?: number; label?: string; center?: boolean }) {
  const text = label ?? 'Loading'
  const width = size * 3
  return (
    <div className={`${s.wrap} ${center ? s.center : ''}`} role="status" aria-label={text}>
      <span className={s.track} style={{ width, '--sweep-to': `${width}px` } as CSSProperties} aria-hidden="true">
        <span className={s.bar} />
      </span>
      <span className={s.label}>{text}</span>
    </div>
  )
}
