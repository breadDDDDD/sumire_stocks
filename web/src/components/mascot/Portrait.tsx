import { useState } from 'react'
import { Bust, type Mood, type Outfit } from './Bust'

export type { Mood, Outfit }

/** Portraits the user may drop into web/public/assets/sumire/ (gitignored); see docs/mascot.md. */
const portraitUrl = (outfit: Outfit, mood: Mood) => `/assets/sumire/${outfit}-${mood}.png`

// remembered for the session so a missing file is only requested once
const missing = new Set<string>()

export function Portrait({ outfit, mood, size }: { outfit: Outfit; mood: Mood; size: number }) {
  const src = portraitUrl(outfit, mood)
  const [, rerender] = useState(0)
  if (missing.has(src)) return <Bust outfit={outfit} mood={mood} size={size} />
  return (
    <img
      src={src}
      width={size}
      height={size}
      alt=""
      draggable={false}
      style={{ objectFit: 'contain', objectPosition: 'bottom' }}
      onError={() => {
        missing.add(src)
        rerender((n) => n + 1)
      }}
    />
  )
}
