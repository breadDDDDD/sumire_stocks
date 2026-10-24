import s from './CutText.module.css'

const STYLES = [s.l0, s.l1, s.l2, s.l3, s.l4, s.l5]

/** Ransom-note lettering: each letter cut from a different "page", fixed by its index. */
export function CutText({ text, className }: { text: string; className?: string }) {
  return (
    <span className={[s.cut, className].filter(Boolean).join(' ')} aria-label={text} role="img">
      {Array.from(text).map((ch, i) =>
        ch === ' ' ? (
          <span key={i} className={s.space} aria-hidden="true" />
        ) : (
          <span key={i} className={`${s.letter} ${STYLES[i % 6]}`} aria-hidden="true">
            {ch}
          </span>
        ),
      )}
    </span>
  )
}
