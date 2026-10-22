export interface InsightDriver {
  claim: string
  evidence: string
  linked?: string[]
  sourceIds?: string[]
}

export interface InsightConnection {
  from: string
  to: string[]
  text: string
}

export interface InsightSource {
  id: string
  title: string
  url: string
  publisher: string
  publishedAt: string
}

export interface InsightWarning {
  code: string
  message: string
}

/** GET /api/insights/{id}, /api/stocks/{code}/insight, /api/briefs/latest. */
export interface Insight {
  id: number
  kind: string
  subject: string
  createdAt: string
  ageMinutes: number
  runner: string | null
  model: string | null
  sentiment: number | null
  confidence: 'low' | 'med' | 'high' | string | null
  summary: string
  drivers?: InsightDriver[]
  connections?: InsightConnection[]
  catalysts?: { date: string; event: string }[]
  risks?: string[]
  whatWouldChangeThisView?: string | null
  linked?: string[]
  sources?: InsightSource[]
  warnings?: InsightWarning[]
}

/** "12 min ago", "3 h ago", "2 d ago". */
export function ageText(minutes: number | null | undefined): string {
  if (minutes == null || !Number.isFinite(minutes)) return ''
  if (minutes < 1) return 'just now'
  if (minutes < 60) return `${Math.round(minutes)} min ago`
  if (minutes < 60 * 48) return `${Math.round(minutes / 60)} h ago`
  return `${Math.round(minutes / 1440)} d ago`
}

/** IDX-style ticker (4 letters) or a catalog code such as IHSG/GOLD; factor names like COAL also match, so callers check moves. */
export const looksLikeCode = (s: string) => /^[A-Z0-9^.=]{2,8}$/.test(s)
