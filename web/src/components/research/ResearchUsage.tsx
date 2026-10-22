import { useWorkspace } from '../../shell/workspace'
import { blockedMessage, isActive, useInsightReady, useResearchStatus } from './api'
import s from './Research.module.css'

/** Top bar research usage ("RQ 9/20"); click opens RQ. Pulses while a job runs. */
export default function ResearchUsage() {
  const status = useResearchStatus()
  const { run } = useWorkspace()
  useInsightReady()

  const d = status.data
  const blocked = blockedMessage(d)
  const busy = d?.jobs.some(isActive)
  const full = d && d.usage.today >= d.usage.dailyCap
  const title = status.isError
    ? 'Research unavailable (database not reachable)'
    : d
      ? `Research runs today ${d.usage.today}/${d.usage.dailyCap}${d.pending ? ` · ${d.pending} pending` : ''}${blocked ? ` · ${blocked}` : ''}`
      : 'Research runs today vs cap'

  return (
    <button type="button" className={s.usage} title={title} onClick={() => run('RQ')}>
      {busy && <span className={s.dot} aria-label="Research running" />}
      RQ{' '}
      {d ? (
        <b className={full ? s.bad : blocked ? s.warn : undefined}>
          {d.usage.today}/{d.usage.dailyCap}
        </b>
      ) : (
        <b>–/–</b>
      )}
      {d?.paused && <span className={s.warn}>❚❚</span>}
    </button>
  )
}
