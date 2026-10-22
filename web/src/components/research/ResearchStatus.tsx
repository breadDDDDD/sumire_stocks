import { useEffect, useState } from 'react'
import { usePane } from '../../shell/workspace'
import { Badge, Button, Spinner } from '../ui'
import { blockedMessage, durationText, useResearchStatus, type ResearchJob } from './api'
import s from './Research.module.css'

/**
 * One-line research state: hidden when research can run; otherwise explains why it won't
 * (paused, disabled, caps reached, DB down) with a link to RQ.
 */
export default function ResearchStatus({ compact }: { compact?: boolean }) {
  const status = useResearchStatus()
  const { open } = usePane()
  const message = status.isError
    ? 'Research is unavailable: the database is not reachable.'
    : blockedMessage(status.data)
  if (!message) return null
  return (
    <div className={s.notice} role="status">
      <Badge tone="amber">Research</Badge>
      <span>{message}</span>
      {!compact && (
        <Button variant="ghost" onClick={() => open('RQ')}>
          Open RQ
        </Button>
      )}
    </div>
  )
}

/** Glasses spinner for a queued or running job, with elapsed time. */
export function JobProgress({ job, label }: { job: ResearchJob | null | undefined; label: string }) {
  const [, tick] = useState(0)
  useEffect(() => {
    const id = setInterval(() => tick((n) => n + 1), 1000)
    return () => clearInterval(id)
  }, [])
  const queuedSec = job ? Math.max(0, Math.round((Date.now() - Date.parse(job.createdAt)) / 1000)) : 0
  const state =
    !job || job.status === 'pending'
      ? `Queued${job ? ` · ${queuedSec < 60 ? `${queuedSec}s` : `${Math.floor(queuedSec / 60)}m`}` : ''}`
      : `Researching · ${durationText(job)}${job.runner ? ` · ${job.runner}/${job.model ?? ''}` : ''}`
  return (
    <div className={s.progress}>
      <Spinner size={44} />
      <div>
        <div className={s.progressLabel}>{label}</div>
        <div className="muted">{state} · usually 1–2 minutes</div>
      </div>
    </div>
  )
}
