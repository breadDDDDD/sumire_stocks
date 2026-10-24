import { useState } from 'react'

export type Mood = 'neutral' | 'smiling' | 'worried' | 'determined' | 'surprised'
export type Outfit = 'uniform' | 'gym'

/** Portraits the user may drop into web/public/assets/sumire/ (gitignored); see docs/mascot.md. */
const portraitUrl = (outfit: Outfit, mood: Mood) => `/assets/sumire/${outfit}-${mood}.png`

// remembered for the session so a missing file is only requested once
const missing = new Set<string>()

export function Portrait({ outfit, mood, size }: { outfit: Outfit; mood: Mood; size: number }) {
  const src = portraitUrl(outfit, mood)
  const [, rerender] = useState(0)
  if (missing.has(src)) return <Placeholder outfit={outfit} mood={mood} size={size} />
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

const MOUTH: Record<Mood, string> = {
  neutral: 'M54 77 q6 2 12 0',
  smiling: 'M52 75 q8 8 16 0',
  worried: 'M53 79 q7 -5 14 0',
  determined: 'M53 77 h14',
  surprised: 'M60 74 a3.5 4.5 0 1 0 0.1 0',
}

const BROWS: Record<Mood, [string, string]> = {
  neutral: ['M41 52 q6 -3 12 0', 'M67 52 q6 -3 12 0'],
  smiling: ['M41 51 q6 -4 12 0', 'M67 51 q6 -4 12 0'],
  worried: ['M41 51 q6 1 12 -3', 'M67 48 q6 4 12 3'],
  determined: ['M41 49 l12 4', 'M79 49 l-12 4'],
  surprised: ['M41 48 q6 -5 12 0', 'M67 48 q6 -5 12 0'],
}

/**
 * Original placeholder bust (no game art): crimson hair with a ponytail, round red glasses,
 * a dark blazer with a red ribbon (uniform) or a red track jacket (gym).
 */
function Placeholder({ outfit, mood, size }: { outfit: Outfit; mood: Mood; size: number }) {
  const gym = outfit === 'gym'
  const [browL, browR] = BROWS[mood]
  return (
    <svg width={size} height={size} viewBox="0 0 120 120" role="img" aria-label={`Sumire (${mood})`}>
      {/* ponytail */}
      <path d="M84 30 q22 6 18 40 q-3 16 -12 22 q6 -20 -2 -36 z" fill="#a50d26" />
      {/* body */}
      <path
        d="M22 120 q2 -24 24 -30 l28 0 q22 6 24 30 z"
        fill={gym ? '#c8102e' : '#23232d'}
        stroke={gym ? '#7d0a1d' : '#3a3a46'}
        strokeWidth="1.5"
      />
      {gym ? (
        <>
          <path d="M60 92 v28" stroke="#f2f2f5" strokeWidth="2" />
          <path d="M44 92 l16 10 l16 -10" fill="none" stroke="#f2f2f5" strokeWidth="2.5" />
        </>
      ) : (
        <>
          <path d="M48 90 l12 16 l12 -16 z" fill="#f2f2f5" />
          <path d="M53 96 l7 5 l7 -5 l-2 7 h-10 z" fill="#c8102e" />
        </>
      )}
      {/* neck + face */}
      <rect x="53" y="80" width="14" height="12" rx="4" fill="#f1d3c2" />
      <ellipse cx="60" cy="60" rx="25" ry="27" fill="#f6dccd" />
      {/* hair: crown and side bangs */}
      <path d="M33 62 q-4 -36 27 -38 q31 2 27 38 q-4 -18 -14 -24 q-10 10 -30 8 q-6 6 -10 16 z" fill="#c8102e" />
      <path d="M36 50 q2 -14 14 -18 q-6 10 -6 22 z" fill="#a50d26" />
      {/* brows, glasses, eyes */}
      <path d={browL} stroke="#7d0a1d" strokeWidth="2" fill="none" strokeLinecap="round" />
      <path d={browR} stroke="#7d0a1d" strokeWidth="2" fill="none" strokeLinecap="round" />
      <circle cx="47" cy="62" r="9" fill="rgba(255,255,255,0.25)" stroke="#c8102e" strokeWidth="2.2" />
      <circle cx="73" cy="62" r="9" fill="rgba(255,255,255,0.25)" stroke="#c8102e" strokeWidth="2.2" />
      <path d="M56 62 q4 -3 8 0" stroke="#c8102e" strokeWidth="2" fill="none" />
      {mood === 'smiling' ? (
        <>
          <path d="M43 63 q4 -4 8 0" stroke="#2b1a1a" strokeWidth="2" fill="none" strokeLinecap="round" />
          <path d="M69 63 q4 -4 8 0" stroke="#2b1a1a" strokeWidth="2" fill="none" strokeLinecap="round" />
        </>
      ) : (
        <>
          <ellipse cx="47" cy="62" rx="2.6" ry={mood === 'surprised' ? 3.6 : 3} fill="#2b1a1a" />
          <ellipse cx="73" cy="62" rx="2.6" ry={mood === 'surprised' ? 3.6 : 3} fill="#2b1a1a" />
        </>
      )}
      {mood === 'smiling' && (
        <>
          <ellipse cx="42" cy="72" rx="4" ry="2" fill="#f4a3a8" opacity="0.7" />
          <ellipse cx="78" cy="72" rx="4" ry="2" fill="#f4a3a8" opacity="0.7" />
        </>
      )}
      <path
        d={MOUTH[mood]}
        stroke="#8a2a32"
        strokeWidth="2"
        fill={mood === 'surprised' ? '#8a2a32' : 'none'}
        strokeLinecap="round"
      />
    </svg>
  )
}
