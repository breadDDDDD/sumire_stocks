import type { KeyboardEvent, ReactNode } from 'react'
import { useQuotes } from '../api/signalr'
import { Badge, Button, Empty, ErrorState, FreshnessBadge, formatNum, Num, Panel, Skeleton } from '../components/ui'
import { formatDateWib } from '../lib/time'
import { usePane } from '../shell/workspace'
import { quotePrice, useProfile, useTechnicals, type LiveQuote, type Profile, type Technicals } from './des/api'
import s from './des/DES.module.css'
import type { FunctionProps } from './types'

const LINKS: { code: string; key: string; label: string; stocksOnly?: boolean }[] = [
  { code: 'GP', key: 'g', label: 'Chart' },
  { code: 'FA', key: 'a', label: 'Fundamentals', stocksOnly: true },
  { code: 'FCST', key: 'f', label: 'Forecast' },
  { code: 'INS', key: 'n', label: 'Insight' },
  { code: 'REL', key: 'r', label: 'Relations' },
]

export default function DES({ ticker }: FunctionProps) {
  const code = (ticker ?? '').toUpperCase()
  const profile = useProfile(code)
  const tech = useTechnicals(code)
  const live = useQuotes(code ? [code] : [])[code] as LiveQuote | undefined
  const pane = usePane()

  if (!code) {
    return (
      <Panel title="DES">
        <Empty title="No ticker" message="Type a ticker, e.g. BBCA, to see its description." />
      </Panel>
    )
  }

  const links = LINKS.filter((l) => !(l.stocksOnly && profile.data?.isIndex))
  const onKeyDown = (e: KeyboardEvent) => {
    if (e.altKey || e.ctrlKey || e.metaKey || e.target !== e.currentTarget) return
    // n and r are global shortcuts already (INS / REL)
    const link = links.find((l) => l.key === e.key && l.key !== 'n' && l.key !== 'r')
    if (link) {
      e.preventDefault()
      pane.open(`${code} ${link.code}`)
    }
  }

  return (
    <Panel
      title={`${code} DES`}
      subtitle={profile.data?.name}
      actions={
        live?.time ? (
          <FreshnessBadge asOf={live.time} />
        ) : profile.data?.asOf ? (
          <Badge tone="muted">Close · {formatDateWib(profile.data.asOf)}</Badge>
        ) : null
      }
    >
      {profile.isPending ? (
        <Skeleton rows={10} />
      ) : profile.isError ? (
        <ErrorState error={profile.error} onRetry={() => profile.refetch()} />
      ) : (
        <div className={s.body} tabIndex={0} onKeyDown={onKeyDown} aria-label={`${code} description`}>
          <Header p={profile.data} tech={tech.data} live={live} />
          <nav className={s.links} aria-label="Open function">
            {links.map((l) => (
              <Button key={l.code} onClick={() => pane.open(`${code} ${l.code}`)} title={`${l.label} (${l.key})`}>
                <span className={s.chipCode}>{l.code}</span> {l.label} <kbd className={s.kbd}>{l.key}</kbd>
              </Button>
            ))}
          </nav>
          <Returns tech={tech.data} pending={tech.isPending} failed={tech.isError} />
          <KeyStatsGrid p={profile.data} beta={profile.data.keyStats.beta ?? tech.data?.risk?.beta ?? null} live={live} />
          <section className={s.section}>
            <h3 className={s.h}>Business</h3>
            {profile.data.description ? (
              <p className={s.summary}>{profile.data.description}</p>
            ) : (
              <p className="muted">No business summary available.</p>
            )}
            {profile.data.website && (
              <a className={s.site} href={profile.data.website} target="_blank" rel="noreferrer">
                {profile.data.website.replace(/^https?:\/\//, '')}
              </a>
            )}
          </section>
        </div>
      )}
    </Panel>
  )
}

function Header({ p, tech, live }: { p: Profile; tech?: Technicals; live?: LiveQuote }) {
  const price = quotePrice(live) ?? p.keyStats.last ?? tech?.technicals?.close ?? null
  const change = live ? live.change : (tech?.technicals?.change ?? null)
  const changePct = live ? live.changePct : (tech?.technicals?.changePct ?? null)
  return (
    <div className={s.header}>
      <div className={s.ident}>
        <span className={`${s.code} display`}>{p.code}</span>
        <span className={s.name}>{p.name}</span>
        <div className={s.badges}>
          {p.isIndex && <Badge tone="amber">Index</Badge>}
          {p.sector && <Badge>{p.sector}</Badge>}
          {p.industry && p.industry !== p.sector && <Badge tone="muted">{p.industry}</Badge>}
          {p.board && <Badge tone="muted">{p.board}</Badge>}
          {p.groupName && <Badge tone="amber">{p.groupName} group</Badge>}
          {p.indexMemberships?.map((m) => (
            <Badge key={m} tone="muted">
              {m}
            </Badge>
          ))}
        </div>
      </div>
      <div className={s.quote}>
        <Num value={price} format="price" className={s.price} colorBy={change ?? 0} />
        <div className={s.change}>
          <Num value={change} format="price" signed colored arrow /> <Num value={changePct} format="pct" signed colored />
        </div>
        <span className={s.source}>{live ? 'Live (delayed)' : 'Last close'}</span>
      </div>
    </div>
  )
}

const RETURN_KEYS: [keyof NonNullable<Technicals['risk']>['returns'], string][] = [
  ['d1', '1D'],
  ['w1', '1W'],
  ['m1', '1M'],
  ['m3', '3M'],
  ['ytd', 'YTD'],
  ['y1', '1Y'],
]

function Returns({ tech, pending, failed }: { tech?: Technicals; pending: boolean; failed: boolean }) {
  const r = tech?.risk?.returns
  return (
    <div className={s.returns} aria-label="Returns">
      {RETURN_KEYS.map(([k, label]) => {
        const v = r?.[k] ?? null
        return (
          <div key={k} className={`${s.ret} ${v == null ? '' : v > 0 ? s.retUp : v < 0 ? s.retDown : ''}`}>
            <span className={s.label}>{label}</span>
            {pending ? <span className="muted">…</span> : <Num value={v} format="pct" signed colored decimals={1} />}
          </div>
        )
      })}
      {failed && <span className={s.note}>Returns unavailable</span>}
    </div>
  )
}

function Stat({ label, children, hint }: { label: string; children: ReactNode; hint?: string }) {
  return (
    <div className={s.stat} title={hint}>
      <span className={s.label}>{label}</span>
      <span className={s.statValue}>{children}</span>
    </div>
  )
}

function KeyStatsGrid({ p, beta, live }: { p: Profile; beta: number | null; live?: LiveQuote }) {
  const k = p.keyStats
  const last = quotePrice(live) ?? k.last
  const pct = (v: number | null) => (v == null ? null : v * 100)
  return (
    <section className={s.section}>
      <h3 className={s.h}>Key stats</h3>
      <div className={s.grid}>
        {!p.isIndex && (
          <>
            <Stat label="Market cap">{k.marketCap == null ? 'n/a' : `Rp ${formatNum(k.marketCap, 'compact')}`}</Stat>
            <Stat label="P/E">
              <Num value={k.pe} decimals={2} />
            </Stat>
            <Stat label="P/B">
              <Num value={k.pb} decimals={2} />
            </Stat>
            <Stat label="Div yield">
              <Num value={pct(k.divYield)} format="pct" />
            </Stat>
            <Stat label="ROE">
              <Num value={pct(k.roe)} format="pct" decimals={1} />
            </Stat>
            <Stat label="EPS">
              <Num value={k.eps} format="price" />
            </Stat>
          </>
        )}
        <Stat label="Beta" hint="vs IHSG, 1 year">
          <Num value={beta} decimals={2} />
        </Stat>
        <Stat label="Avg value 20D">{k.avgValueTraded20 == null ? 'n/a' : `Rp ${formatNum(k.avgValueTraded20, 'compact')}`}</Stat>
        {!p.isIndex && (
          <>
            <Stat label="Lot value" hint="100 shares at the last price">
              <Num value={k.lotValue} format="idr" />
            </Stat>
            <Stat label="Tick size">
              <Num value={k.tickSize} format="price" />
            </Stat>
            <Stat
              label={`ARA / ARB${k.nextTradingDay ? ` ${formatDateWib(k.nextTradingDay).slice(0, 6)}` : ''}`}
              hint="Auto-rejection limits for the next trading day"
            >
              <span className="up">
                <Num value={k.araNext} format="price" />
              </span>
              <span className="muted"> / </span>
              <span className="down">
                <Num value={k.arbNext} format="price" />
              </span>
            </Stat>
          </>
        )}
        <div className={`${s.stat} ${s.wide}`}>
          <span className={s.label}>52W range</span>
          <RangeBar low={k.low52w} high={k.high52w} value={last} />
        </div>
      </div>
    </section>
  )
}

function RangeBar({ low, high, value }: { low: number | null; high: number | null; value: number | null }) {
  if (low == null || high == null || value == null || high <= low) return <span className="muted">n/a</span>
  const pos = Math.min(100, Math.max(0, ((value - low) / (high - low)) * 100))
  return (
    <span className={s.range}>
      <Num value={low} format="price" />
      <span className={s.track} role="meter" aria-valuemin={low} aria-valuemax={high} aria-valuenow={value}>
        <span className={s.marker} style={{ left: `${pos}%` }} />
      </span>
      <Num value={high} format="price" />
      <span className="muted">{pos.toFixed(0)}%</span>
    </span>
  )
}
