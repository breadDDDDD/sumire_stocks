import { useQuery } from '@tanstack/react-query'
import { useMemo } from 'react'
import { api } from '../../api/client'
import { useQuotes } from '../../api/signalr'

/** Same shape as GET /api/quotes and the hub's `quotes` event. */
export interface Quote {
  code: string
  name?: string
  price: number
  change: number
  changePct: number
  prevClose?: number | null
  open?: number | null
  high?: number | null
  low?: number | null
  volume?: number | null
  value?: number | null
  time: string
  source?: string
}

export interface Mover {
  code: string
  name: string
  price: number
  changePct: number
  value: number | null
}

/** Codes on the ticker tape and macro strip (all `isIndex` instruments). */
export const TAPE_CODES = ['IHSG', 'LQ45', 'USDIDR', 'WTI', 'BRENT', 'GOLD', 'US10Y', 'SPX', 'N225', 'HSI', 'STI', 'SSE']

export const MACRO_LABELS: Record<string, string> = {
  IHSG: 'IHSG',
  LQ45: 'LQ45',
  USDIDR: 'USD/IDR',
  WTI: 'WTI',
  BRENT: 'Brent',
  GOLD: 'Gold',
  US10Y: 'US10Y',
  SPX: 'S&P 500',
  N225: 'Nikkei',
  HSI: 'Hang Seng',
  STI: 'STI',
  SSE: 'Shanghai',
}

/**
 * Latest quotes for `codes`: one REST fetch (refreshed every 2 min) overlaid with live hub pushes.
 * The hub sends the REST shape (`price`); `last` is accepted too for safety.
 */
export function useLiveQuotes(codes: string[]): Record<string, Quote> {
  const key = [...new Set(codes.map((c) => c.toUpperCase()))].sort().join(',')
  const rest = useQuery({
    queryKey: ['quotes', key],
    queryFn: ({ signal }) => api<Quote[]>(`/quotes?codes=${encodeURIComponent(key)}`, { signal }),
    enabled: key.length > 0,
    staleTime: 60_000,
    refetchInterval: 120_000,
    retry: 0,
  })
  const live = useQuotes(key ? key.split(',') : [])

  return useMemo(() => {
    const out: Record<string, Quote> = {}
    for (const q of rest.data ?? []) out[q.code] = q
    for (const [code, t] of Object.entries(live)) {
      const tick = t as unknown as Partial<Quote> & { last?: number }
      const price = tick.price ?? tick.last
      if (price == null) continue
      const prev = out[code]
      if (prev && new Date(prev.time).getTime() > new Date(tick.time ?? 0).getTime()) continue
      out[code] = { ...prev, ...tick, code, price } as Quote
    }
    return out
  }, [rest.data, live])
}
