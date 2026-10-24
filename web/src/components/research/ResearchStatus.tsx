import { useEffect, useState } from 'react'
import { usePane } from '../../shell/workspace'
import { Button, Spinner } from '../ui'
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
      <span>{message}</span>
      {!compact && (
        <Button variant="ghost" onClick={() => open('RQ')}>
          <kbd className="key-fn">RQ</kbd> Open queue
        </Button>
      )}
    </div>
  )
}

/** Progress bar for a queued or running job, with elapsed time. */
export function JobProgress({ job, label }: { job: ResearchJob | null | undefined; label: string }) {
  const [, tick] = useState(0)
  useEffect(() => {
    const id = setInterval(() => tick((n) => n + 1), 1000)
    return () => clearInterval(id)
  }, [])
  const queuedSec = job ? Math.max(0, Math.round((Date.now() - Date.parse(job.createdAt)) / 1000)) : 0
  const running = job?.status === 'running'
  const elapsed = running
    ? durationText(job)
    : job
      ? queuedSec < 60
        ? `${queuedSec}s`
        : `${Math.floor(queuedSec / 60)}m`
      : null
  return (
    <div className={s.progress}>
      <div className={s.progressLabel}>{label}</div>
      <Spinner size={40} label={running ? 'Researching' : 'Queued'} />
      <div className={s.progressState}>
        {running && <span className={s.dot} aria-hidden="true" />}
        {elapsed && <b>{elapsed}</b>}
        {running && job?.runner && <span>{`${job.runner}/${job.model ?? ''}`}</span>}
        <span>· usually 1–2 minutes</span>
      </div>
    </div>
  )
}
