import s from './Spinner.module.css'

/** Sumire's round glasses as the loading indicator. */
export function Spinner({ size = 40, label, center }: { size?: number; label?: string; center?: boolean }) {
  return (
    <div className={`${s.wrap} ${center ? s.center : ''}`} role="status" aria-label={label ?? 'Loading'}>
      <svg className={s.glasses} width={size} height={size / 2.2} viewBox="0 0 48 22" aria-hidden="true">
        <circle className={s.lens} cx="11" cy="11" r="9" />
        <circle className={s.lens} cx="37" cy="11" r="9" />
        <circle className={s.shine} cx="11" cy="11" r="9" />
        <circle className={`${s.shine} ${s.shine2}`} cx="37" cy="11" r="9" />
        <path className={s.bridge} d="M20 9.5 Q24 6.5 28 9.5" />
      </svg>
      {label && <span>{label}</span>}
    </div>
  )
}
