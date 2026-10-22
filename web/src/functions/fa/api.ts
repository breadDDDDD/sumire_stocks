import { useQuery } from '@tanstack/react-query'
import { getJson } from '../des/mock'

export interface Ratios {
  marketCap: number | null
  pe: number | null
  pb: number | null
  roe: number | null
  der: number | null
  npm: number | null
  eps: number | null
  divYield: number | null
  revenueGrowth: number | null
  earningsGrowth: number | null
}

export interface Statement {
  endDate: string
  items: Record<string, number | null>
}

export interface Fundamentals {
  code: string
  sector: string | null
  updatedAt: string | null
  ratios: Ratios | null
  sectorMedians: { pe: number | null; pb: number | null; roe: number | null; divYield: number | null; n: number } | null
  statements: {
    incomeAnnual: Statement[]
    incomeQuarterly: Statement[]
    balanceSheet: Statement[]
    cashflow: Statement[]
  } | null
  statementsError: string | null
}

export function useFundamentals(code: string) {
  return useQuery({
    queryKey: ['fundamentals', code],
    enabled: !!code,
    queryFn: ({ signal }) => getJson<Fundamentals>(`/stocks/${code}/fundamentals`, signal),
  })
}
