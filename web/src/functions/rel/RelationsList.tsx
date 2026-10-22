import { TickerChip } from '../../components/insight/TickerChip'
import { Badge, Num, Table, type Column } from '../../components/ui'
import type { Relations } from './types'
import s from './Rel.module.css'

type Live = Record<string, { changePct?: number | null }>

interface Props {
  data: Relations
  live: Live
  onOpen: (code: string) => void
}

type Corr = Relations['correlated'][number]
type Lead = Relations['leadLag'][number]

/** Every link as text: groups, factors, supply chains, correlated names and lead-lag proxies. */
export function RelationsList({ data, live, onOpen }: Props) {
  const pct = (code: string, fallback: number | null) => live[code]?.changePct ?? fallback

  const corrColumns: Column<Corr>[] = [
    { key: 'code', header: 'Code', width: 70, render: (c) => <span className="value">{c.code}</span> },
    { key: 'name', header: 'Name', render: (c) => <span className="muted">{c.name ?? '—'}</span> },
    { key: 'rho', header: 'ρ 60d', align: 'right', width: 70, render: (c) => <Num value={c.rho} decimals={2} /> },
    {
      key: 'chgPct',
      header: 'Today',
      align: 'right',
      width: 80,
      render: (c) => <Num value={pct(c.code, c.chgPct)} format="pct" signed colored />,
      sortValue: (c) => pct(c.code, c.chgPct),
    },
  ]
  const leadColumns: Column<Lead>[] = [
    { key: 'proxy', header: 'Proxy', width: 70, render: (l) => <span className="value">{l.proxy}</span> },
    { key: 'name', header: 'Name', render: (l) => <span className="muted">{l.name ?? '—'}</span> },
    { key: 'rho', header: 'ρ (t-1)', align: 'right', width: 70, render: (l) => <Num value={l.rho} decimals={2} /> },
    { key: 'tStat', header: 't', align: 'right', width: 50, render: (l) => <Num value={l.tStat} decimals={1} /> },
    {
      key: 'chgPct',
      header: 'Last move',
      align: 'right',
      width: 90,
      render: (l) => <Num value={pct(l.proxy, l.chgPct)} format="pct" signed colored />,
      sortValue: (l) => pct(l.proxy, l.chgPct),
    },
  ]

  return (
    <div className={s.list}>
      <section>
        <h3 className={s.h}>
          Groups <Badge tone="muted">curated</Badge>
        </h3>
        {data.groups.length === 0 && <p className="muted">Not in a curated conglomerate group.</p>}
        {data.groups.map((g) => (
          <div key={g.name} className={s.row}>
            <div className={s.rowHead} title={g.note ?? undefined}>
              <span className="value">{g.name}</span>
              {g.note && <span className={s.note}>{g.note}</span>}
            </div>
            <div className={s.chips}>
              {g.peers.map((p) => (
                <TickerChip key={p.code} code={p.code} chgPct={p.chgPct} live={live} onOpen={onOpen} title={p.name ?? p.code} />
              ))}
            </div>
          </div>
        ))}
      </section>

      <section>
        <h3 className={s.h}>
          Factors <Badge tone="muted">curated</Badge>
        </h3>
        {data.factors.length === 0 && <p className="muted">No commodity or macro links on file.</p>}
        {data.factors.map((f) => (
          <div key={`${f.factor}:${f.direction}`} className={s.row}>
            <div className={s.rowHead}>
              <span className="value">{f.factor}</span>
              <Badge tone={f.direction === 'positive' ? 'up' : 'down'}>
                {f.direction === 'positive' ? 'moves with' : 'moves against'}
              </Badge>
              <span className="muted">{f.kind}</span>
              {f.proxy ? (
                <TickerChip code={f.proxy.code} chgPct={f.proxy.chgPct} live={live} title={f.proxy.name ?? f.proxy.code} />
              ) : (
                <span className="muted">no market proxy</span>
              )}
            </div>
            {f.note && <p className={s.note}>{f.note}</p>}
          </div>
        ))}
      </section>

      {data.supplyChain.length > 0 && (
        <section>
          <h3 className={s.h}>
            Supply chain <Badge tone="muted">curated</Badge>
          </h3>
          {data.supplyChain.map((c) => (
            <div key={c.name} className={s.row}>
              <div className={s.rowHead}>
                <span className="value">{c.name}</span>
              </div>
              <ol className={s.stages}>
                {c.stages.map((st) => (
                  <li key={st.stage} className={st.self ? s.selfStage : undefined}>
                    <span className={s.stageName}>{st.stage}</span>
                    <span className={s.chips}>
                      {st.tickers.map((t) => (
                        <TickerChip
                          key={t.code}
                          code={t.code}
                          chgPct={t.chgPct}
                          live={live}
                          highlight={t.code === data.code}
                          onOpen={t.code === data.code ? undefined : onOpen}
                          title={t.name ?? t.code}
                        />
                      ))}
                    </span>
                  </li>
                ))}
              </ol>
            </div>
          ))}
        </section>
      )}

      <section>
        <h3 className={s.h}>
          Correlated <Badge tone="muted">statistical</Badge>
        </h3>
        {data.correlated.length === 0 ? (
          <p className="muted">
            {data.statsAsOf ? 'No core stock with |ρ| ≥ 0.6 over 60 days.' : 'Statistics are computed after the next EOD run.'}
          </p>
        ) : (
          <Table columns={corrColumns} rows={data.correlated} rowKey={(c) => c.code} onOpen={(c) => onOpen(c.code)} />
        )}
      </section>

      <section>
        <h3 className={s.h}>
          Lead-lag <Badge tone="muted">statistical</Badge>
        </h3>
        {data.leadLag.length === 0 ? (
          <p className="muted">
            {data.statsAsOf
              ? 'No overnight market or commodity reliably leads this stock.'
              : 'Statistics are computed after the next EOD run.'}
          </p>
        ) : (
          <Table columns={leadColumns} rows={data.leadLag} rowKey={(l) => l.proxy} />
        )}
      </section>
    </div>
  )
}
