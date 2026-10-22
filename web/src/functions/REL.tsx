import { useMemo, useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { api } from '../api/client'
import { useQuotes } from '../api/signalr'
import { Button, Empty, ErrorState, Num, Panel, Skeleton } from '../components/ui'
import { usePane } from '../shell/workspace'
import { RelationsGraph } from './rel/RelationsGraph'
import { RelationsList } from './rel/RelationsList'
import { buildNodes, type Relations } from './rel/types'
import type { FunctionProps } from './types'
import s from './rel/Rel.module.css'

type View = 'map' | 'list'

export default function REL({ ticker, args }: FunctionProps) {
  const code = ticker ?? ''
  const [view, setView] = useState<View>(args[0]?.toLowerCase() === 'list' ? 'list' : 'map')
  const { open } = usePane()

  const q = useQuery({
    queryKey: ['relations', code],
    queryFn: ({ signal }) => api<Relations>(`/stocks/${encodeURIComponent(code)}/relations`, { signal }),
    enabled: !!code,
    refetchInterval: 5 * 60_000,
  })

  const nodes = useMemo(() => (q.data ? buildNodes(q.data) : []), [q.data])
  const codes = useMemo(() => {
    const d = q.data
    if (!d) return []
    const all = new Set<string>([d.code])
    d.groups.forEach((g) => g.peers.forEach((p) => all.add(p.code)))
    d.factors.forEach((f) => f.proxy && all.add(f.proxy.code))
    d.supplyChain.forEach((c) => c.stages.forEach((st) => st.tickers.forEach((t) => all.add(t.code))))
    d.correlated.forEach((c) => all.add(c.code))
    d.leadLag.forEach((l) => all.add(l.proxy))
    return [...all]
  }, [q.data])
  const live = useQuotes(codes)

  const openTicker = (c: string) => open(`${c} DES`)
  const d = q.data

  return (
    <Panel
      title={`${code} REL`}
      subtitle={d ? [d.name, d.sector].filter(Boolean).join(' · ') : 'Relations map'}
      actions={
        <>
          {d && <Num value={live[d.code]?.changePct ?? d.quote?.chgPct} format="pct" signed colored arrow />}
          <Button active={view === 'map'} onClick={() => setView('map')} title="Hub-and-spoke map">
            Map
          </Button>
          <Button active={view === 'list'} onClick={() => setView('list')} title="All links as a list">
            List
          </Button>
        </>
      }
    >
      {!code ? (
        <Empty title="Pick a ticker" message="e.g. BREN REL" />
      ) : q.isPending ? (
        <Skeleton rows={10} />
      ) : q.isError ? (
        <ErrorState error={q.error} onRetry={() => q.refetch()} />
      ) : d && nodes.length === 0 && view === 'map' ? (
        <Empty
          title="No links yet"
          message={`${d.code} is not in the curated relations map${d.statsAsOf ? ' and has no strong statistical links' : ', and statistics are computed after the next EOD run'}.`}
        />
      ) : d && view === 'map' ? (
        <>
          <RelationsGraph code={d.code} chgPct={d.quote?.chgPct ?? null} nodes={nodes} live={live} onOpen={openTicker} />
          <p className={s.footnote}>
            Tab to a node, Enter opens its DES. Moves are today&apos;s %.{' '}
            {d.statsAsOf ? `Statistics as of ${d.statsAsOf}.` : 'Statistical links appear after the next EOD run.'}{' '}
            {d.mapUpdated && `Curated map updated ${d.mapUpdated}.`}
          </p>
        </>
      ) : d ? (
        <RelationsList data={d} live={live} onOpen={openTicker} />
      ) : null}
    </Panel>
  )
}
