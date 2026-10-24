import { useQuery } from '@tanstack/react-query'
import { getJson } from '../des/mock'

export interface HorizonForecast {
  horizon: number
  p10: number
  p50: number
  p90: number
  probUp: number | null
  sigma?: number | null
}

export interface ModelForecast {
  model: string
  asOf: string
  horizons: HorizonForecast[]
}

export interface GarchCone {
  horizon: number
  sigma: number
  minus2: number
  minus1: number
  plus1: number
  plus2: number
}

export interface ScoreComponent {
  component: string
  score: number | null
  weight: number
  reason: string
}

export interface TrackRecordRow {
  model: string
  horizon: number
  scope: string
  n: number
  dirAccuracy: number | null
  mape: number | null
  coverage80: number | null
  beatsBaseline: boolean | null
}

export interface Forecast {
  code: string
  asOf: string
  last: number
  models: ModelForecast[]
  garch: {
    annualizedVol: number | null
    longRunVol: number | null
    volPercentile: number | null
    persistence: number | null
    converged: boolean
    cone: GarchCone[]
  } | null
  score: { total: number | null; label: string; coverage: number; components: ScoreComponent[] } | null
  trackRecord: TrackRecordRow[]
}

export function useForecast(code: string, enabled = true) {
  return useQuery({
    queryKey: ['forecast', code],
    enabled: !!code && enabled,
    queryFn: ({ signal }) => getJson<Forecast>(`/stocks/${code}/forecast`, signal),
  })
}

/** Display name and color for a forecast model. */
export function modelStyle(model: string): { name: string; color: string } {
  if (model === 'baseline') return { name: 'Baseline', color: '#ffb000' }
  if (model.startsWith('chronos')) return { name: 'Chronos-2', color: '#f4efe6' }
  return { name: model, color: '#ff7a1a' }
}
