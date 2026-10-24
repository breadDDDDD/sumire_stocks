import { useWorkspace } from '../../shell/workspace'
import { blockedMessage, isActive, useInsightReady, useResearchStatus } from './api'
import s from './Research.module.css'

/** Status bar research usage ("Research 9/20" plus a usage bar); click opens RQ. */
export default function ResearchUsage() {
  const status = useResearchStatus()
  const { run } = useWorkspace()
  useInsightReady()

  const d = status.data
  const blocked = blockedMessage(d)
  const busy = d?.jobs.some(isActive)
  const ratio = d && d.usage.dailyCap > 0 ? Math.min(1, d.usage.today / d.usage.dailyCap) : 0
  const full = d != null && d.usage.today >= d.usage.dailyCap
  const title = status.isError
    ? 'Research unavailable (database not reachable)'
    : d
      ? `Research runs today ${d.usage.today}/${d.usage.dailyCap}${d.pending ? ` · ${d.pending} pending` : ''}${busy ? ' · running' : ''}${blocked ? ` · ${blocked}` : ''}`
      : 'Research runs today vs cap'

  return (
    <button type="button" className={s.usage} title={title} onClick={() => run('RQ')}>
      <span className={s.usageLabel}>Research</span>
      <span className={`${s.count} ${full ? s.bad : blocked || status.isError ? s.warn : ''}`}>
        {d ? `${d.usage.today}/${d.usage.dailyCap}` : '–/–'}
      </span>
      <span className={s.meter} aria-hidden="true">
        <span className={`${s.meterFill} ${ratio >= 0.9 ? s.meterHot : ''}`} style={{ transform: `scaleX(${ratio})` }} />
      </span>
      {d?.paused && <span className={s.paused}>Paused</span>}
    </button>
  )
}
