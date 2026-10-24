import { useId, type ReactNode } from 'react'

export type Mood = 'neutral' | 'smiling' | 'worried' | 'determined' | 'surprised'
export type Outfit = 'uniform' | 'gym'

// limited palette: two tones per material, one ink
const INK = '#0a0908'
const PAPER = '#f4efe6'
const HAIR = '#c8102e'
const HAIR_SH = '#7d0a1c'
const HAIR_HI = '#e8203f'
const SKIN = '#f6dcc8'
const SKIN_SH = '#e2b9a0'
const IRIS = '#2b1418'
const MOUTH = '#8a2a32'
const AMBER = '#ffb000'
const BLAZER = '#1c1a1d'
const SHIRT_SH = '#cfc6b8'
const JACKET = '#a30d27'
const JACKET_SH = '#5e0716'

const OUTLINE = { stroke: INK, strokeWidth: 2.25, strokeLinejoin: 'miter', strokeMiterlimit: 8 } as const
const LINE = { stroke: INK, strokeWidth: 1.25, strokeLinejoin: 'miter', strokeLinecap: 'square', fill: 'none' } as const

/**
 * Original hand-built bust (no game art): 3/4 view facing left, faceted crimson hair with a low
 * ponytail and ribbon, round red glasses, flat two-tone cel shading on a slanted cut-paper plate.
 * Only the brows, eyes, mouth and extras change with the mood, so the silhouette never moves.
 */
export function Bust({ outfit, mood, size }: { outfit: Outfit; mood: Mood; size: number }) {
  const uid = useId().replace(/:/g, '')
  const tone = `tone-${uid}`
  const toneClip = `toneclip-${uid}`
  return (
    <svg width={size} height={size} viewBox="0 0 120 120" role="img" aria-label={`Sumire (${mood})`}>
      <defs>
        <pattern id={tone} width="4" height="4" patternUnits="userSpaceOnUse" patternTransform="rotate(20)">
          <circle cx="2" cy="2" r="0.9" fill={INK} />
        </pattern>
        <clipPath id={toneClip}>
          <path d="M58 25 L112 18 L116 74 Z" />
        </clipPath>
      </defs>

      {/* cut-paper backing plate with a hard black offset */}
      <path d="M18 34 L116 22 L120 120 L8 120 Z" fill={HAIR} />
      <path d="M14 30 L112 18 L118 120 L4 120 Z" fill={PAPER} {...OUTLINE} />
      <rect x="0" y="0" width="120" height="120" fill={`url(#${tone})`} clipPath={`url(#${toneClip})`} opacity="0.5" />

      {/* ponytail swinging out to the right, with the ribbon at the tie */}
      <path
        d="M82 66 Q97 66 104 80 L111 98 L113 113 L106 104 L103 116 L98 102 L93 108 L92 92 Q90 80 82 76 Z"
        fill={HAIR}
        {...OUTLINE}
      />
      <path d="M95 74 Q101 84 104 96 L106 104 L103 112 L99 100 L96 106 L94 90 Z" fill={HAIR_SH} />

      {/* back hair mass, falling past the shoulders */}
      <path
        d="M30 50 L31 38 Q40 21 60 19 Q83 19 90 40 L92 66 L95 96 L86 90 L82 100 L76 84 L36 86 L30 102 L26 92 L20 98 L25 70 Z"
        fill={HAIR}
        {...OUTLINE}
      />
      <path d="M84 44 L89 66 L92 92 L86 88 L82 96 L78 78 Z" fill={HAIR_SH} />
      <path d="M30 64 L28 84 L24 92 L27 70 Z" fill={HAIR_SH} />

      {outfit === 'gym' ? <Gym /> : <Uniform />}

      {/* neck */}
      <path d="M45 76 L45 94 L55 99 L65 93 L65 74 Z" fill={SKIN} {...OUTLINE} />
      <path d="M45 80 L65 76 L65 86 L56 88 L45 86 Z" fill={SKIN_SH} />

      {/* face, chin pointing left */}
      <path
        d="M34 45 Q32 56 34 64 Q37 75 45 83 Q48 86 51 84 Q61 79 68 72 Q74 65 75 56 L75 42 Q64 30 50 31 Q38 34 34 45 Z"
        fill={SKIN}
        {...OUTLINE}
      />
      <path d="M75 56 Q74 66 68 72 Q61 79 52 83 L58 76 Q67 70 70 60 Z" fill={SKIN_SH} />
      <path d="M34 47 L39 55 L44 51 L50 56 L56 52 L63 58 L70 54 L75 56 L75 44 L34 44 Z" fill={SKIN_SH} />
      {/* ear */}
      <path d="M72 55 Q80 51 80 60 Q79 68 71 68 Z" fill={SKIN} {...OUTLINE} strokeWidth={1.6} />
      <path d="M74 58 Q77 58 76 63" {...LINE} />
      {/* nose */}
      <path d="M43.5 68 L41.5 71.5 L44.5 71.8" {...LINE} />

      <Face mood={mood} />

      {/* glasses: far lens foreshortened */}
      <ellipse cx="41" cy="61.5" rx="7.2" ry="8.2" fill={PAPER} fillOpacity="0.18" stroke={HAIR} strokeWidth="2.2" />
      <circle cx="61.5" cy="61.5" r="9" fill={PAPER} fillOpacity="0.18" stroke={HAIR} strokeWidth="2.2" />
      <path d="M48.2 60 Q50.4 58.2 52.6 60" fill="none" stroke={HAIR} strokeWidth="2" />
      <path d="M70.4 60 L77 58.5" stroke={HAIR} strokeWidth="2" />
      <path d="M64 54.5 L67.5 56.5 L65.8 57.8 Z" fill={PAPER} />

      {/* bangs: faceted locks swept to her left, over the top of the glasses */}
      <path
        d="M31 52 L31 38 Q40 21 60 19 Q80 20 86 38 L80 50 L78 58 L72 49 L69 60 L63 47 L57 56 L52 45 L46 54 L44 43 L38 58 L35 50 Z"
        fill={HAIR}
        {...OUTLINE}
      />
      <path d="M44 43 L46 54 L49 48 Z M52 45 L57 56 L59 50 Z M63 47 L69 60 L70 52 Z M72 49 L78 58 L78 48 Z" fill={HAIR_SH} />
      <path d="M42 28 Q54 21 70 25 L62 27 Q52 25 45 31 Z" fill={HAIR_HI} />
      {/* side locks: one over the far cheek, one over the near shoulder */}
      <path d="M33 46 L28 62 L27 84 L32 72 L35 58 Z" fill={HAIR} {...OUTLINE} />
      <path d="M76 48 L82 66 L81 90 L76 76 L73 60 Z" fill={HAIR} {...OUTLINE} />
      <path d="M78 58 L81 72 L80 84 L77 74 Z" fill={HAIR_SH} />

      {/* hair ribbon at the ponytail tie, one tail edged in terminal amber */}
      <g strokeLinejoin="miter">
        <path d="M90 66 L104 54 L105 72 Z" fill={HAIR} stroke={INK} strokeWidth="1.6" />
        <path d="M90 66 L105 72 L101 64 Z" fill={HAIR_SH} />
        <path d="M89 68 L79 56 L82 75 Z" fill={HAIR} stroke={INK} strokeWidth="1.6" />
        <path d="M89 68 L82 75 L83 66 Z" fill={HAIR_SH} />
        <path d="M89 70 L93 88 L96 83 L100 88 L93 69 Z" fill={HAIR} stroke={INK} strokeWidth="1.4" />
        <path d="M93.5 69.5 L100 88" stroke={AMBER} strokeWidth="1.1" />
        <path d="M86 64 L93 63 L94 71 L87 72 Z" fill={HAIR_SH} stroke={INK} strokeWidth="1.6" />
      </g>

      <Brows mood={mood} />
      <Extra mood={mood} />
    </svg>
  )
}

function Uniform() {
  return (
    <g>
      <path d="M6 120 L11 103 Q16 93 34 89 L46 87 L66 87 Q90 89 101 99 L108 120 Z" fill={BLAZER} {...OUTLINE} />
      <path d="M78 90 Q94 94 101 101 L108 120 L86 120 Z" fill={INK} />
      {/* shirt + collar points */}
      <path d="M44 88 L55 104 L66 88 Z" fill={PAPER} {...OUTLINE} strokeWidth={1.4} />
      <path d="M55 104 L66 88 L62 88 L55 98 Z" fill={SHIRT_SH} />
      <path d="M44 88 L38 97 L50 99 Z" fill={PAPER} {...OUTLINE} strokeWidth={1.4} />
      <path d="M66 88 L74 96 L61 99 Z" fill={PAPER} {...OUTLINE} strokeWidth={1.4} />
      {/* notched lapels */}
      <path d="M38 97 L50 99 L54 120 L30 120 L34 106 Z" {...LINE} fill={BLAZER} />
      <path d="M74 96 L61 99 L57 120 L82 120 L78 106 Z" {...LINE} fill={BLAZER} />
      <path d="M34 106 L40 104 M78 106 L72 104" {...LINE} />
      {/* neck ribbon with a plaid hint */}
      <path d="M55 100 L45 95 L46 106 Z" fill={HAIR} {...OUTLINE} strokeWidth={1.4} />
      <path d="M55 100 L65 95 L64 106 Z" fill={HAIR} {...OUTLINE} strokeWidth={1.4} />
      <path d="M53 100 L49 113 L52 111 L54 115 L56 103 Z" fill={HAIR_SH} {...OUTLINE} strokeWidth={1.2} />
      <path d="M57 101 L61 113 L58 111 L56.5 115" fill={HAIR_SH} {...OUTLINE} strokeWidth={1.2} />
      <path d="M48 97 L47.5 104 M51 98 L51 103 M46 100 L54 101 M62 97 L62.5 104 M59 98 L59 103 M56 101 L64 100" stroke={INK} strokeWidth="0.6" opacity="0.7" />
      <path d="M52.5 98 L57.5 98 L57.5 103 L52.5 103 Z" fill={HAIR} {...OUTLINE} strokeWidth={1.4} />
    </g>
  )
}

function Gym() {
  return (
    <g>
      <path d="M6 120 L10 102 Q15 92 33 88 L46 85 L66 85 Q91 88 102 99 L108 120 Z" fill={JACKET} {...OUTLINE} />
      <path d="M80 89 Q95 93 102 100 L108 120 L88 120 Z" fill={JACKET_SH} />
      {/* high collar with white piping */}
      <path d="M41 84 L45 97 L55 102 L65 97 L69 82 L73 90 L66 104 L55 108 L44 104 L37 92 Z" fill={JACKET} {...OUTLINE} strokeWidth={1.6} />
      <path d="M38 93 L45 105 L55 109 L66 105 L73 91" fill="none" stroke={PAPER} strokeWidth="1.6" />
      {/* zip */}
      <path d="M56 108 L57 120" stroke={INK} strokeWidth="2" />
      <path d="M56 108 L57 120" stroke={AMBER} strokeWidth="0.8" strokeDasharray="1.2 1.2" />
      <path d="M54.5 110 L58 110 L57.6 115 L55 115 Z" fill={PAPER} stroke={INK} strokeWidth="0.8" />
      {/* shoulder stripes */}
      <path d="M12 101 Q20 93 34 90" fill="none" stroke={PAPER} strokeWidth="2" />
      <path d="M84 91 Q96 95 101 102" fill="none" stroke={PAPER} strokeWidth="2" />
    </g>
  )
}

function Face({ mood }: { mood: Mood }) {
  const eyes =
    mood === 'smiling' ? (
      <g>
        <path d="M37 63 L41 59.5 L45 63" fill="none" stroke={INK} strokeWidth="2" strokeLinejoin="miter" />
        <path d="M56 63.5 L61.5 59 L67 63.5" fill="none" stroke={INK} strokeWidth="2.2" strokeLinejoin="miter" />
      </g>
    ) : (
      <g>
        {/* far eye */}
        <path d="M45 57.4 Q41 55.6 36.6 57.6 L34.8 56.6 L35.6 59.6 Q40 57.4 45.2 59.4 Z" fill={INK} />
        <ellipse cx="41.4" cy={mood === 'worried' ? 63 : 62.2} rx="2.9" ry={mood === 'surprised' ? 2.6 : 4.1} fill={IRIS} />
        {/* near eye, with the lash flick at the outer corner */}
        <path d="M67.6 57.4 Q62 54.6 55.4 57.4 L55 59.6 Q61 56.4 67.4 59.2 L69.6 56 Z" fill={INK} />
        <ellipse cx="61.6" cy={mood === 'worried' ? 63.2 : 62.4} rx="3.6" ry={mood === 'surprised' ? 3 : 4.7} fill={IRIS} />
        {mood === 'determined' && (
          <>
            <path d="M36.4 59.4 L45 58.6" stroke={INK} strokeWidth="2" />
            <path d="M55 59.4 L67.6 58.4" stroke={INK} strokeWidth="2.2" />
          </>
        )}
        {mood === 'surprised' ? (
          <>
            <ellipse cx="41.6" cy="62.4" rx="3.4" ry="4.2" fill="none" stroke={INK} strokeWidth="0.9" />
            <ellipse cx="61.8" cy="62.6" rx="4.2" ry="5" fill="none" stroke={INK} strokeWidth="0.9" />
          </>
        ) : null}
        <rect x={mood === 'worried' ? 40.6 : 40.4} y={mood === 'worried' ? 63.4 : 60} width="1.3" height="1.3" fill={PAPER} />
        <rect x={mood === 'worried' ? 60.6 : 60.2} y={mood === 'worried' ? 63.8 : 59.8} width="1.6" height="1.6" fill={PAPER} />
        <path d="M39 67.4 L43 67.2 M58.6 67.8 L64.6 67.4" stroke={INK} strokeWidth="0.9" />
      </g>
    )

  const mouth: Record<Mood, ReactNode> = {
    neutral: <path d="M43.5 76.5 L49 76" {...LINE} />,
    smiling: (
      <path d="M41.6 74.8 Q47 74 51.8 74.2 Q49.6 80 45 79.6 Z" fill={MOUTH} stroke={INK} strokeWidth="1.25" strokeLinejoin="miter" />
    ),
    worried: <path d="M42.5 77 L44.5 75.8 L46.5 77 L48.5 75.8 L50 76.6" {...LINE} />,
    determined: <path d="M42.5 77.2 L44 76 L50 76" {...LINE} strokeWidth={1.5} />,
    surprised: <ellipse cx="46.5" cy="77" rx="1.8" ry="2.4" fill={MOUTH} stroke={INK} strokeWidth="1.25" />,
  }

  return (
    <g data-part="eyes-mouth">
      {eyes}
      {mouth[mood]}
    </g>
  )
}

const BROWS: Record<Mood, [string, string]> = {
  neutral: ['M36.5 51.5 L40.5 50 L45 50.8', 'M56 50.2 L61.5 48.8 L67.5 50'],
  smiling: ['M36.5 50.5 L40.5 48.4 L45 49.4', 'M56 49 L61.5 47 L67.5 48.8'],
  worried: ['M36.5 50.4 L41 50.6 L45 48', 'M56 47.6 L61 50 L67.5 51'],
  determined: ['M36.5 49.4 L41 50.6 L45.5 52.6', 'M55.5 52.6 L61 50.2 L67.5 49'],
  surprised: ['M37 48.6 L40.5 46.6 L44.5 47.6', 'M56.5 47 L61.5 45 L67 46.6'],
}

function Brows({ mood }: { mood: Mood }) {
  const [l, r] = BROWS[mood]
  const w = mood === 'determined' ? 2.5 : 1.8
  return (
    <g data-part="brows" fill="none" stroke={INK} strokeWidth={w} strokeLinejoin="miter" strokeLinecap="square">
      <path d={l} />
      <path d={r} />
    </g>
  )
}

function Extra({ mood }: { mood: Mood }) {
  switch (mood) {
    case 'smiling':
      // blush as hatching, not pink ovals
      return (
        <g data-part="extra" stroke={HAIR} strokeWidth="1" strokeLinecap="square">
          <path d="M36 70 L37.5 67.5 M38.5 70 L40 67.5 M41 70 L42.5 67.5" />
          <path d="M60 71.5 L61.5 69 M62.5 71.5 L64 69 M65 71.5 L66.5 69" />
        </g>
      )
    case 'worried':
      return (
        <path data-part="extra" d="M76 40 L79.5 47.5 L76.5 50.5 L73 47 Z" fill={PAPER} stroke={INK} strokeWidth="1.25" strokeLinejoin="miter" />
      )
    case 'determined':
      // the glasses flash: two hard white bars across the near lens
      return (
        <g data-part="extra" fill={PAPER}>
          <path d="M55 64 L64 53.5 L67 54.5 L58 66 Z" />
          <path d="M60.5 68 L68.5 58 L69.6 60 L62.2 69.2 Z" />
        </g>
      )
    case 'surprised':
      return (
        <g data-part="extra" stroke={INK} strokeWidth="2" strokeLinecap="square">
          <path d="M93 32 L99 24" />
          <path d="M96 39 L105 36" />
          <path d="M96 46 L104 49" />
        </g>
      )
    default:
      return null
  }
}
