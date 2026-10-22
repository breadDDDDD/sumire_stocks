import { wibParts } from './time'

export type SessionState = 'PRE-OPEN' | 'LIVE' | 'BREAK' | 'PRE-CLOSE' | 'POST' | 'CLOSED'

const hm = (h: number, m: number) => h * 60 + m

/**
 * Rough IDX session state from the WIB clock. Holidays are not known client side;
 * the server's /api/status is authoritative once it exists.
 */
export function sessionState(d: Date = new Date()): SessionState {
  const { weekday, minutes: t } = wibParts(d)
  if (weekday === 0 || weekday === 6) return 'CLOSED'
  const friday = weekday === 5
  const s1End = friday ? hm(11, 30) : hm(12, 0)
  const s2Start = friday ? hm(14, 0) : hm(13, 30)

  if (t >= hm(8, 45) && t < hm(9, 0)) return 'PRE-OPEN'
  if (t >= hm(9, 0) && t < s1End) return 'LIVE'
  if (t >= s1End && t < s2Start) return 'BREAK'
  if (t >= s2Start && t < hm(15, 50)) return 'LIVE'
  if (t >= hm(15, 50) && t < hm(16, 0)) return 'PRE-CLOSE'
  if (t >= hm(16, 0) && t < hm(16, 15)) return 'POST'
  return 'CLOSED'
}
