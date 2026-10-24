import s from './Skeleton.module.css'

const WIDTHS = [22, 38, 14, 18, 26, 12]

/** Placeholder rows while something loads (prefer this over a spinner for tables and lists). */
export function Skeleton({ rows = 6, columns = 4 }: { rows?: number; columns?: number }) {
  return (
    <div className={s.wrap} aria-busy="true" aria-label="Loading">
      {Array.from({ length: rows }, (_, r) => (
        <div key={r} className={s.row}>
          {Array.from({ length: columns }, (_, c) => (
            <div
              key={c}
              className={r === 0 && c === 0 ? `${s.bar} ${s.stub}` : s.bar}
              style={r === 0 && c === 0 ? undefined : { width: `${WIDTHS[(r + c) % WIDTHS.length]}%` }}
            />
          ))}
          {r === rows - 1 && <span className={s.cursor} aria-hidden="true" />}
        </div>
      ))}
    </div>
  )
}
