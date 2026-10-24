import { useMemo, useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { api } from '../api/client'
import { useQuotes } from '../api/signalr'
import { TickerChip } from '../components/insight/TickerChip'
import { Button, Empty, ErrorState, Panel, Skeleton, Table, type Column } from '../components/ui'
import { formatDateWib, formatTimeWib } from '../lib/time'
import { usePane } from '../shell/workspace'
import type { FunctionProps } from './types'
import s from './n/N.module.css'

interface Headline {
  title: string
  url: string
  publisher: string
  publishedAt: string | null
  summary: string | null
  tickers: string[]
}

function when(iso: string | null): string {
  if (!iso) return '—'
  const t = Date.parse(iso)
  if (Number.isNaN(t)) return '—'
  const time = formatTimeWib(t).slice(0, 5)
  return formatDateWib(t) === formatDateWib(new Date()) ? time : `${formatDateWib(t).slice(0, 6)} ${time}`
}

const openLink = (url: string) => window.open(url, '_blank', 'noopener,noreferrer')

/** `BMRI N` (one stock) or `N` (whole market): RSS headlines, newest first. Enter opens the article. */
export default function N({ ticker }: FunctionProps) {
  const [all, setAll] = useState(false)
  const code = all ? undefined : ticker
  const { open } = usePane()

  const q = useQuery({
    queryKey: ['news', code ?? null],
    queryFn: ({ signal }) =>
      api<Headline[]>(`/news?limit=${code ? 50 : 100}${code ? `&code=${encodeURIComponent(code)}` : ''}`, { signal }),
    refetchInterval: 5 * 60_000,
    staleTime: 60_000,
  })

  const codes = useMemo(() => [...new Set((q.data ?? []).flatMap((h) => h.tickers))].slice(0, 150), [q.data])
  const live = useQuotes(codes)

  const columns: Column<Headline>[] = [
    {
      key: 'publishedAt',
      header: 'Time',
      width: 96,
      render: (h) => <span className="muted">{when(h.publishedAt)}</span>,
      sortValue: (h) => (h.publishedAt ? Date.parse(h.publishedAt) : null),
    },
    { key: 'publisher', header: 'Source', width: 100, render: (h) => <span className={s.publisher}>{h.publisher}</span> },
    {
      key: 'title',
      header: 'Headline',
      render: (h) => (
        <span className={s.title} title={h.summary ?? h.title}>
          {h.title}
        </span>
      ),
    },
    {
      key: 'tickers',
      header: 'Tickers',
      sortable: false,
      render: (h) => (
        <span className={s.chips}>
          {h.tickers.slice(0, 4).map((t) => (
            <TickerChip key={t} code={t} live={live} onOpen={(c) => open(`${c} DES`)} />
          ))}
        </span>
      ),
    },
  ]

  return (
    <Panel
      title={code ? `${code} N` : 'N'}
      subtitle="Headlines · CNBC Indonesia, Kontan, Bisnis, IDX Channel"
      actions={
        <>
          {ticker && (
            <Button active={all} onClick={() => setAll((v) => !v)} title="Toggle between this stock and the whole market">
              {all ? `Only ${ticker}` : 'All market'}
            </Button>
          )}
          <Button variant="ghost" onClick={() => q.refetch()} disabled={q.isFetching} title="Reload (cached 15 min on the server)">
            {q.isFetching ? 'Loading…' : 'Reload'}
          </Button>
        </>
      }
    >
      {q.isPending ? (
        <Skeleton rows={12} columns={4} />
      ) : q.isError ? (
        <ErrorState error={q.error} onRetry={() => q.refetch()} />
      ) : (
        <Table
          columns={columns}
          rows={q.data}
          rowKey={(h) => h.url}
          onOpen={(h) => openLink(h.url)}
          autoFocus
          empty={
            <Empty
              title="No headlines"
              message={
                code
                  ? `Nothing in the feeds mentions ${code} right now. Headlines are matched by ticker or company name.`
                  : 'The news feeds returned nothing. Try again in a few minutes.'
              }
            >
              {code && <Button onClick={() => setAll(true)}>Show all market</Button>}
            </Empty>
          }
        />
      )}
      <p className={s.hint}>
        <kbd>j</kbd>/<kbd>k</kbd> to move · <kbd className="key-go">GO</kbd> opens the article in a new tab · third-party
        text, not verified
      </p>
    </Panel>
  )
}
