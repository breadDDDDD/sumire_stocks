import s from './Spinner.module.css'

/** Sumire's round glasses as the loading indicator. */
export function Spinner({ size = 40, label, center }: { size?: number; label?: string; center?: boolean }) {
  return (
    <div className={`${s.wrap} ${center ? s.center : ''}`} role="status" aria-label={label ?? 'Loading'}>
      <svg className={s.glasses} width={size} height={size / 2.2} viewBox="0 0 48 22" aria-hidden="true">
        <path className={s.temple} d="M2 9 l-3 -2 M46 9 l3 -2" />
        <circle className={s.lens} cx="11" cy="11" r="9" />
        <circle className={s.lens} cx="37" cy="11" r="9" />
        <circle className={s.shine} cx="11" cy="11" r="9" />
        <circle className={`${s.shine} ${s.shine2}`} cx="37" cy="11" r="9" />
        <path className={s.bridge} d="M20 9.5 L24 7 L28 9.5" />
      </svg>
      {label && <span className={s.label}>{label}</span>}
    </div>
  )
}
