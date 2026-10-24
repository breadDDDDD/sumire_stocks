import { useQuery } from '@tanstack/react-query'
import { api } from '../api/client'

/** GET /api/status (docs/api/market.md). */
export interface MarketStatus {
  time: string
  session: string
  lastBarDate: string | null
  expectedBarDate: string | null
  lastQuoteTime: string | null
  db: string
  dbError: string | null
  eod: { running: boolean; lastError: string | null }
}

/** Shared by the status bar and the boot readout (one query, one cache entry). */
export function useMarketStatus() {
  return useQuery({
    queryKey: ['status'],
    queryFn: ({ signal }) => api<MarketStatus>('/status', { signal }),
    refetchInterval: 60_000,
    retry: false,
  })
}
