import { useQuery } from '@tanstack/react-query'
import { getJson } from '../des/mock'

export interface Bar {
  time: number
  date: string | null
  open: number
  high: number
  low: number
  close: number
  volume: number
}

export interface ChartData {
  code: string
  range: string
  interval: string
  source: string
  prevClose: number | null
  bars: Bar[]
}

export const RANGES = [
  { key: '1', label: '1D', range: '1d', intraday: true },
  { key: '2', label: '5D', range: '5d', intraday: true },
  { key: '3', label: '1M', range: '1m', intraday: false },
  { key: '4', label: '6M', range: '6m', intraday: false },
  { key: '5', label: '1Y', range: '1y', intraday: false },
  { key: '6', label: '3Y', range: '3y', intraday: false },
] as const
export type RangeKey = (typeof RANGES)[number]['range']

export const INDICATORS = [
  { id: 'sma20', label: 'SMA 20', color: '#ffd27a' },
  { id: 'sma50', label: 'SMA 50', color: '#ff7a1a' },
  { id: 'sma200', label: 'SMA 200', color: '#f4efe6' },
  { id: 'bb', label: 'Bollinger (20, 2)', color: '#9a8f7c' },
  { id: 'rsi', label: 'RSI 14 pane', color: '#ffb000' },
  { id: 'macd', label: 'MACD histogram pane', color: '#ff7a1a' },
] as const
export type IndicatorId = (typeof INDICATORS)[number]['id']

/** Every overlay at once, so toggling indicators never refetches. */
export const SERIES_PARAM = 'sma20,sma50,sma200,bb,rsi,macd'

export function useChartBars(code: string, range: RangeKey, intraday: boolean) {
  return useQuery({
    queryKey: ['chart', code, range],
    enabled: !!code,
    staleTime: intraday ? 60_000 : 5 * 60_000,
    refetchInterval: intraday ? 60_000 : false,
    queryFn: ({ signal }) => getJson<ChartData>(`/chart/${code}?range=${range}`, signal),
  })
}

export interface GpPrefs {
  range: RangeKey
  indicators: IndicatorId[]
  volume: boolean
  forecast: boolean
}

export const PREFS_KEY = 'sumire.gp.v1'
export const DEFAULT_PREFS: GpPrefs = { range: '6m', indicators: ['sma50', 'sma200'], volume: true, forecast: false }

/** Adds `n` weekdays to a YYYY-MM-DD date (holidays ignored; it only places the cone). */
export function addTradingDays(date: string, n: number): string {
  const d = new Date(`${date}T00:00:00Z`)
  let left = n
  while (left > 0) {
    d.setUTCDate(d.getUTCDate() + 1)
    const wd = d.getUTCDay()
    if (wd !== 0 && wd !== 6) left--
  }
  return d.toISOString().slice(0, 10)
}

/** Unix seconds of 00:00 on a date, matching the API's WIB wall-clock bar times. */
export const dayTime = (date: string) => Date.parse(`${date}T00:00:00Z`) / 1000
