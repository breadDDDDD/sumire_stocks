import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, ApiError } from '../../api/client'
import { useHubEvent } from '../../api/signalr'
import type { Insight } from '../insight/types'

export type JobStatus = 'pending' | 'running' | 'done' | 'failed' | 'skipped'
export type ResearchKind = 'stock' | 'brief_open' | 'brief_close' | 'brief_week' | 'sector' | 'ask'

export interface ResearchJob {
  id: number
  kind: ResearchKind | string
  subject: string
  priority: number
  status: JobStatus
  runner: string | null
  model: string | null
  attempts: number
  error: string | null
  createdAt: string
  startedAt: string | null
  finishedAt: string | null
  insightId: number | null
}

export interface RunnerStatus {
  runner: string
  model: string
  installed: boolean
  used: number
  cap: { pattern: string; cap: number } | null
  exhaustedUntil: string | null
}

export interface ResearchStatusData {
  enabled: boolean
  paused: boolean
  usage: { today: number; dailyCap: number; batchCap: number }
  current: { runner: string; model: string } | null
  blockedReason: string | null
  runners: RunnerStatus[]
  pending: number
  jobs: ResearchJob[]
  nextBatches: { kind: string; at: string }[]
}

export interface EnqueueBody {
  kind: ResearchKind | 'open' | 'close' | 'week'
  subject?: string
  question?: string
  force?: boolean
}

export interface EnqueueResult {
  jobId: number | null
  status: 'queued' | 'duplicate' | 'cached'
  insightId: number | null
}

export interface InsightReadyEvent {
  kind: string
  subject: string
  insightId: number
}

export const researchKey = ['research'] as const

/** GET /api/research. Polls faster while jobs are pending or running. */
export function useResearchStatus() {
  return useQuery({
    queryKey: researchKey,
    queryFn: ({ signal }) => api<ResearchStatusData>('/research', { signal }),
    refetchInterval: (q) => {
      const d = q.state.data
      if (q.state.status === 'error') return 60_000
      return d && d.jobs.some((j) => j.status === 'pending' || j.status === 'running') ? 5_000 : 30_000
    },
    retry: false,
  })
}

export function useInsightById(id: number | null | undefined) {
  return useQuery({
    queryKey: ['insight', id],
    queryFn: ({ signal }) => api<Insight>(`/insights/${id}`, { signal }),
    enabled: id != null,
    staleTime: Infinity,
  })
}

export function useEnqueue() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (body: EnqueueBody) => api<EnqueueResult>('/research', { method: 'POST', body }),
    onSettled: () => qc.invalidateQueries({ queryKey: researchKey }),
  })
}

export function usePauseToggle() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (pause: boolean) => api<{ paused: boolean }>(pause ? '/research/pause' : '/research/resume', { method: 'POST' }),
    onSettled: () => qc.invalidateQueries({ queryKey: researchKey }),
  })
}

/** Refresh the queue whenever any insight lands, and call `onReady` for it. */
export function useInsightReady(onReady?: (e: InsightReadyEvent) => void) {
  const qc = useQueryClient()
  useHubEvent<InsightReadyEvent>('insightReady', (e) => {
    void qc.invalidateQueries({ queryKey: researchKey })
    onReady?.(e)
  })
}

export const isActive = (j: ResearchJob) => j.status === 'pending' || j.status === 'running'

export const sameSubject = (a: string, b: string) => a.trim().toLowerCase() === b.trim().toLowerCase()

/** Why new research will not run right now, or null when it will. */
export function blockedMessage(s: ResearchStatusData | undefined): string | null {
  if (!s) return null
  if (!s.enabled) return 'Research is turned off (RESEARCH_ENABLED=false in .env).'
  if (s.paused) return 'Research is paused. Resume it in RQ.'
  if (s.blockedReason) return `Research is on hold: ${s.blockedReason}.`
  return null
}

/** Friendly text for errors from the research endpoints (503 = DB not configured or down). */
export function researchErrorText(error: unknown): string {
  if (error instanceof ApiError && error.status >= 500)
    return 'Research needs the database, which is not reachable right now.'
  if (error instanceof ApiError) return error.message
  if (error instanceof TypeError) return 'Cannot reach the Sumire server.'
  return error instanceof Error ? error.message : 'Something went wrong.'
}

export function durationText(j: ResearchJob): string {
  const start = j.startedAt ? Date.parse(j.startedAt) : NaN
  const end = j.finishedAt ? Date.parse(j.finishedAt) : j.status === 'running' ? Date.now() : NaN
  if (Number.isNaN(start) || Number.isNaN(end)) return '—'
  const sec = Math.max(0, Math.round((end - start) / 1000))
  return sec < 60 ? `${sec}s` : `${Math.floor(sec / 60)}m ${String(sec % 60).padStart(2, '0')}s`
}
