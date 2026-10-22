import { useQuery } from '@tanstack/react-query'
import type { QuoteTick } from '../../api/signalr'
import { getJson } from './mock'

export interface KeyStats {
  last: number | null
  marketCap: number | null
  pe: number | null
  pb: number | null
  divYield: number | null
  roe: number | null
  eps: number | null
  high52w: number | null
  low52w: number | null
  avgValueTraded20: number | null
  lotValue: number | null
  tickSize: number | null
  nextTradingDay: string | null
  araNext: number | null
  arbNext: number | null
  beta: number | null
}

export interface Profile {
  code: string
  name: string
  sector: string | null
  industry: string | null
  board: string | null
  groupName: string | null
  indexMemberships: string[]
  isIndex: boolean
  description: string | null
  website: string | null
  asOf: string | null
  keyStats: KeyStats
  fundamentalsUpdatedAt: string | null
}

export interface Returns {
  d1: number | null
  w1: number | null
  m1: number | null
  m3: number | null
  ytd: number | null
  y1: number | null
}

export interface Technicals {
  code: string
  asOf: string
  source: string
  technicals: {
    date: string
    close: number
    change: number | null
    changePct: number | null
    rsi: number | null
    [k: string]: unknown
  } | null
  risk: {
    returns: Returns
    beta: number | null
    volatilityPct: number | null
    rel3MPct: number | null
    rel6MPct: number | null
  } | null
  series?: { dates: string[]; [key: string]: (number | null)[] | string[] }
}

export function useProfile(code: string) {
  return useQuery({
    queryKey: ['profile', code],
    enabled: !!code,
    queryFn: ({ signal }) => getJson<Profile>(`/stocks/${code}/profile`, signal),
  })
}

export function useTechnicals(code: string, series = '', enabled = true) {
  return useQuery({
    queryKey: ['technicals', code, series],
    enabled: !!code && enabled,
    queryFn: ({ signal }) =>
      getJson<Technicals>(`/stocks/${code}/technicals${series ? `?series=${series}` : ''}`, signal),
  })
}

export function quotePrice(q: QuoteTick | undefined): number | null {
  return q?.price != null && Number.isFinite(q.price) ? q.price : null
}

export type LiveQuote = QuoteTick
