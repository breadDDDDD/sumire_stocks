import { useEffect, useMemo, useRef, useState, type FormEvent } from 'react'
import { InsightCard } from '../components/insight/InsightCard'
import {
  isActive,
  researchErrorText,
  sameSubject,
  useEnqueue,
  useInsightById,
  useInsightReady,
  useResearchStatus,
  type ResearchJob,
} from '../components/research/api'
import ResearchStatus, { JobProgress } from '../components/research/ResearchStatus'
import { Badge, Button, Empty, ErrorState, Panel, Skeleton } from '../components/ui'
import { formatTimeWib, formatDateWib } from '../lib/time'
import { usePane } from '../shell/workspace'
import type { FunctionProps } from './types'
import s from './ask/Ask.module.css'

const REUSE_HOURS = 6
const MAX_QUESTION = 200

const norm = (q: string) => q.trim().slice(0, MAX_QUESTION)

/** `ASK <question>`: free-form research with the current context. Answers arrive over SignalR. */
export default function ASK({ args }: FunctionProps) {
  const initial = args[0] ?? ''
  const [draft, setDraft] = useState('')
  const [question, setQuestion] = useState(() => norm(initial))
  const [selectedId, setSelectedId] = useState<number | null>(null)
  const status = useResearchStatus()
  const enqueue = useEnqueue()
  const { mutate } = enqueue
  const { open } = usePane()
  const inputRef = useRef<HTMLInputElement>(null)
  const asked = useRef(new Set<string>())

  const askJobs = useMemo(() => (status.data?.jobs ?? []).filter((j) => j.kind === 'ask'), [status.data])
  const current: ResearchJob | undefined = question ? askJobs.find((j) => sameSubject(j.subject, question)) : undefined

  useInsightReady()

  // Ask once per question: reuse a recent answer or a job already in the queue instead of spending quota.
  useEffect(() => {
    if (!question || !status.data || asked.current.has(question)) return
    asked.current.add(question)
    const recent =
      current &&
      (isActive(current) ||
        (current.status === 'done' && Date.now() - Date.parse(current.createdAt) < REUSE_HOURS * 3600_000))
    if (!recent) mutate({ kind: 'ask', question })
  }, [question, status.data, current, mutate])

  useEffect(() => {
    if (!initial) inputRef.current?.focus()
  }, [initial])

  const shownId = selectedId ?? (current?.status === 'done' ? current.insightId : null)
  const insight = useInsightById(shownId)

  const submit = (e: FormEvent) => {
    e.preventDefault()
    const q = norm(draft)
    if (!q) return
    asked.current.delete(q)
    setSelectedId(null)
    setQuestion(q)
    setDraft('')
  }

  const history = askJobs.slice(0, 10)

  let answer
  if (!question && selectedId == null)
    answer = (
      <Empty
        title="Ask Sumire"
        message="Ask anything about the market, e.g. “why are nickel stocks down today?”. The answer is researched on the web with today's market context and arrives in about 1–2 minutes."
      />
    )
  else if (selectedId == null && (enqueue.isPending || (current && isActive(current)) || (!current && enqueue.isSuccess)))
    answer = <JobProgress job={current} label="Researching your question" />
  else if (insight.isFetching && !insight.data) answer = <Skeleton rows={8} />
  else if (insight.data)
    answer = (
      <InsightCard insight={insight.data} variant="brief" onOpenTicker={(c) => open(`${c} DES`)} />
    )
  else if (current?.status === 'failed')
    answer = (
      <Empty title="No answer" message={current.error ?? 'The research agent did not return a valid answer.'}>
        <Button
          variant="primary"
          onClick={() => enqueue.mutate({ kind: 'ask', question })}
          disabled={enqueue.isPending}
        >
          Try again
        </Button>
      </Empty>
    )
  else if (insight.isError) answer = <ErrorState error={insight.error} onRetry={() => insight.refetch()} />
  else if (enqueue.isError)
    answer = <Empty title="Could not ask" message={researchErrorText(enqueue.error)} />
  else if (status.isError) answer = <Empty title="Research unavailable" message={researchErrorText(status.error)} />
  else answer = <Skeleton rows={4} />

  return (
    <Panel title="ASK" subtitle={question ? `“${question}”` : 'Free-form research question'}>
      <div className={s.layout}>
        <div className={s.main}>
          <form className={s.form} onSubmit={submit}>
            <input
              ref={inputRef}
              className={s.input}
              value={draft}
              maxLength={MAX_QUESTION}
              placeholder={question ? 'Ask another question…' : 'Why are nickel stocks down today?'}
              onChange={(e) => setDraft(e.target.value)}
              aria-label="Question"
            />
            <Button type="submit" variant="primary" disabled={!draft.trim() || enqueue.isPending}>
              Ask
            </Button>
          </form>
          <ResearchStatus />
          {answer}
        </div>
        <aside className={s.history} aria-label="Previous questions">
          <h3 className={s.historyTitle}>Previous asks</h3>
          {history.length === 0 && <p className="muted">None yet.</p>}
          <ul>
            {history.map((j) => (
              <li key={j.id}>
                <button
                  type="button"
                  className={`${s.historyItem} ${shownId === j.insightId && j.insightId ? s.selected : ''}`}
                  disabled={!j.insightId}
                  onClick={() => {
                    setSelectedId(j.insightId)
                    setQuestion(j.subject)
                    asked.current.add(norm(j.subject))
                  }}
                  title={j.subject}
                >
                  <span className={s.historyQ}>{j.subject}</span>
                  <span className="muted">
                    {formatDateWib(j.createdAt).slice(0, 6)} {formatTimeWib(j.createdAt).slice(0, 5)}{' '}
                    {j.status !== 'done' && <Badge tone={j.status === 'failed' ? 'down' : 'amber'}>{j.status}</Badge>}
                  </span>
                </button>
              </li>
            ))}
          </ul>
        </aside>
      </div>
    </Panel>
  )
}
