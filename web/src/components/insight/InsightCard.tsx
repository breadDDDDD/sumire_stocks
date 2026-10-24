import { useMemo, useState, type ReactNode } from 'react'
import { useQuotes } from '../../api/signalr'
import { formatDateWib, formatTimeWib } from '../../lib/time'
import { Badge } from '../ui'
import { SentimentMeter } from './SentimentMeter'
import { TickerChip } from './TickerChip'
import { ageText, type Insight, type InsightSource } from './types'
import s from './InsightCard.module.css'

export interface InsightCardProps {
  insight: Insight
  /** Opens a ticker (e.g. its DES) when a chip is clicked. */
  onOpenTicker?: (code: string) => void
  /** Briefs get a bigger summary and the linked-tickers strip on top. */
  variant?: 'stock' | 'brief'
  /** Extra content for the meta row (e.g. a refresh button or a "refreshing" badge). */
  actions?: ReactNode
}

const CONFIDENCE_TONE = { high: 'default', med: 'amber', low: 'muted' } as const

function shortDate(d: string): string {
  const t = Date.parse(d)
  if (Number.isNaN(t)) return d
  const full = formatDateWib(t)
  return full.endsWith(String(new Date().getFullYear())) ? full.slice(0, -5) : full
}

const isoDayWib = new Intl.DateTimeFormat('en-CA', { timeZone: 'Asia/Jakarta' })

function hostOf(url: string): string {
  try {
    return new URL(url).hostname.replace(/^www\./, '')
  } catch {
    return 'source'
  }
}

function SourceChip({ source, index }: { source: InsightSource; index: number }) {
  return (
    <a className={s.source} href={source.url} target="_blank" rel="noreferrer noopener" title={source.title}>
      <span className={s.sourceIdx}>{index}</span>
      {source.publisher || hostOf(source.url)}
      {source.publishedAt && <span> · {shortDate(source.publishedAt)}</span>}
    </a>
  )
}

/** Structured AI insight: summary, sentiment, drivers with sources, connections with live moves, catalysts, risks. */
export function InsightCard({ insight, onOpenTicker, variant = 'stock', actions }: InsightCardProps) {
  const [showWarnings, setShowWarnings] = useState(false)
  const sources = useMemo(() => insight.sources ?? [], [insight.sources])
  const sourceIndex = useMemo(() => new Map(sources.map((src, i) => [src.id, i + 1])), [sources])
  const sourceById = useMemo(() => new Map(sources.map((src) => [src.id, src])), [sources])

  const codes = useMemo(() => {
    const set = new Set<string>([insight.subject.toUpperCase()])
    for (const c of insight.connections ?? []) {
      set.add(c.from.toUpperCase())
      c.to.forEach((t) => set.add(t.toUpperCase()))
    }
    insight.linked?.forEach((t) => set.add(t.toUpperCase()))
    insight.drivers?.forEach((d) => d.linked?.forEach((t) => set.add(t.toUpperCase())))
    return [...set]
  }, [insight])
  const live = useQuotes(codes)

  const catalysts = useMemo(
    () => [...(insight.catalysts ?? [])].sort((a, b) => a.date.localeCompare(b.date)),
    [insight.catalysts],
  )
  const today = isoDayWib.format(new Date())
  const warnings = insight.warnings ?? []
  const confidence = (insight.confidence ?? '').toLowerCase()
  const confTone = CONFIDENCE_TONE[confidence as keyof typeof CONFIDENCE_TONE] ?? 'muted'

  /** A chip for codes with a quote or a ticker shape; a cyan factor label otherwise. */
  const node = (code: string, highlight?: boolean) => {
    const c = code.toUpperCase()
    const quoted = live[c] !== undefined
    if (!quoted && !/^[A-Z]{4}$/.test(c))
      return (
        <span key={c} className={s.factor}>
          {code}
        </span>
      )
    return (
      <TickerChip
        key={c}
        code={c}
        live={live}
        plain={!quoted}
        highlight={highlight}
        onOpen={quoted || /^[A-Z]{4}$/.test(c) ? onOpenTicker : undefined}
      />
    )
  }

  return (
    <article className={`${s.card} ${variant === 'brief' ? s.brief : ''}`}>
      <div className={s.meta}>
        <SentimentMeter value={insight.sentiment} />
        {insight.confidence && (
          <Badge tone={confTone} title="How sure the research agent is">
            {confidence === 'med' ? 'medium' : confidence} confidence
          </Badge>
        )}
        {warnings.length > 0 && (
          <button type="button" className={s.warnBtn} onClick={() => setShowWarnings((v) => !v)} aria-expanded={showWarnings}>
            <Badge tone="amber" title={warnings.map((w) => w.message).join('\n')}>
              {warnings.length} check{warnings.length > 1 ? 's' : ''} flagged {showWarnings ? '▴' : '▾'}
            </Badge>
          </button>
        )}
        {actions && <div className={s.metaActions}>{actions}</div>}
      </div>

      {showWarnings && warnings.length > 0 && (
        <ul className={s.warnings}>
          {warnings.map((w, i) => (
            <li key={i}>{w.message}</li>
          ))}
        </ul>
      )}

      <p className={s.summary}>{insight.summary}</p>

      {variant === 'brief' && insight.linked && insight.linked.length > 0 && (
        <div className={s.chips}>{insight.linked.map((c) => node(c))}</div>
      )}

      {insight.connections && insight.connections.length > 0 && (
        <section className={s.section}>
          <h3 className={s.heading}>Connections</h3>
          <ul className={s.connections}>
            {insight.connections.map((c, i) => (
              <li key={i} className={s.connection}>
                <div className={s.chain}>
                  {node(c.from)}
                  <span className={s.arrow} aria-label="moves">
                    →
                  </span>
                  <span className={s.chips}>{c.to.map((t) => node(t, t.toUpperCase() === insight.subject.toUpperCase()))}</span>
                </div>
                <p className={s.connText}>{c.text}</p>
              </li>
            ))}
          </ul>
        </section>
      )}

      {insight.drivers && insight.drivers.length > 0 && (
        <section className={s.section}>
          <h3 className={s.heading}>Drivers</h3>
          <ol className={s.drivers}>
            {insight.drivers.map((d, i) => (
              <li key={i} className={s.driver}>
                <strong className={s.claim}>{d.claim}</strong>
                <p className={s.evidence}>{d.evidence}</p>
                <div className={s.chips}>
                  {(d.sourceIds ?? []).map((id) => {
                    const src = sourceById.get(id)
                    return src ? <SourceChip key={id} source={src} index={sourceIndex.get(id) ?? 0} /> : null
                  })}
                  {(d.sourceIds ?? []).length === 0 && <Badge tone="amber">No source</Badge>}
                </div>
              </li>
            ))}
          </ol>
        </section>
      )}

      {catalysts.length > 0 && (
        <section className={s.section}>
          <h3 className={s.heading}>Catalysts</h3>
          <ol className={s.timeline}>
            {catalysts.map((c, i) => (
              <li key={i} className={c.date < today ? s.past : undefined}>
                <span className={s.date}>{shortDate(c.date)}</span>
                <span>{c.event}</span>
              </li>
            ))}
          </ol>
        </section>
      )}

      {insight.risks && insight.risks.length > 0 && (
        <section className={s.section}>
          <h3 className={s.heading}>Risks</h3>
          <ul className={s.risks}>
            {insight.risks.map((r, i) => (
              <li key={i}>{r}</li>
            ))}
          </ul>
        </section>
      )}

      {insight.whatWouldChangeThisView && (
        <section className={s.section}>
          <h3 className={s.heading}>What would change this view</h3>
          <p className={s.evidence}>{insight.whatWouldChangeThisView}</p>
        </section>
      )}

      {sources.length > 0 && (
        <section className={s.section}>
          <h3 className={s.heading}>Sources</h3>
          <ol className={s.sources}>
            {sources.map((src, i) => (
              <li key={src.id}>
                <span className={s.sourceIdx}>{i + 1}</span>
                <a href={src.url} target="_blank" rel="noreferrer noopener">
                  {src.title}
                </a>
                <span className="muted">
                  {' '}
                  · {src.publisher}
                  {src.publishedAt && ` · ${shortDate(src.publishedAt)}`}
                </span>
              </li>
            ))}
          </ol>
        </section>
      )}

      <footer className={s.footer} title={`Created ${formatDateWib(insight.createdAt)} ${formatTimeWib(insight.createdAt)} WIB`}>
        AI-generated
        {(insight.runner || insight.model) && ` · ${[insight.runner, insight.model].filter(Boolean).join('/')}`}
        {` · ${ageText(insight.ageMinutes)}`} · not investment advice
      </footer>
    </article>
  )
}
