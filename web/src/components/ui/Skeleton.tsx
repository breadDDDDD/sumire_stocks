import s from './Skeleton.module.css'

const WIDTHS = [22, 38, 14, 18, 26, 12]

/** Static placeholder rows while something loads; shows after 150ms so fast loads never flash it. */
export function Skeleton({ rows = 6, columns = 4 }: { rows?: number; columns?: number }) {
  return (
    <div className={s.wrap} aria-busy="true" aria-label="Loading">
      {Array.from({ length: rows }, (_, r) => (
        <div key={r} className={s.row}>
          {Array.from({ length: columns }, (_, c) => (
            <div
              key={c}
              className={c === 0 ? `${s.bar} ${s.code}` : c === columns - 1 && columns > 1 ? `${s.bar} ${s.end}` : s.bar}
              style={c === 0 ? undefined : { width: `${WIDTHS[(r + c) % WIDTHS.length]}%` }}
            />
          ))}
        </div>
      ))}
    </div>
  )
}
