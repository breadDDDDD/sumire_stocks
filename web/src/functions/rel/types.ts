/** GET /api/stocks/{code}/relations (docs/api/relations.md). chgPct is in percent. */
export interface Move {
  code: string
  name: string | null
  price: number | null
  chgPct: number | null
}

export interface Relations {
  code: string
  name: string | null
  sector: string | null
  quote: Move | null
  groups: { name: string; note: string | null; peers: Move[] }[]
  factors: {
    factor: string
    kind: 'commodity' | 'macro'
    direction: 'positive' | 'negative'
    note: string | null
    proxy: Move | null
    source: 'curated'
  }[]
  supplyChain: { name: string; stages: { stage: string; self: boolean; tickers: Move[] }[] }[]
  correlated: { code: string; name: string | null; rho: number; n: number; chgPct: number | null; source: 'statistical' }[]
  leadLag: { proxy: string; name: string | null; rho: number; n: number; tStat: number; chgPct: number | null; source: 'statistical' }[]
  statsAsOf: string | null
  mapUpdated: string | null
}

export type NodeKind = 'group' | 'factor' | 'chain' | 'correlated' | 'leadLag'

export interface MapNode {
  id: string
  label: string
  kind: NodeKind
  /** Instrument code to open and to read live moves for; null for factors without a market series. */
  code: string | null
  chgPct: number | null
  statistical: boolean
  /** |rho| for statistical links, drives edge strength. */
  strength?: number
  detail: string
}

export const KIND_META: Record<NodeKind, { label: string; color: string; max: number }> = {
  group: { label: 'Group', color: 'var(--crimson-bright)', max: 8 },
  factor: { label: 'Factor', color: 'var(--amber)', max: 6 },
  chain: { label: 'Supply chain', color: '#8b7cf6', max: 6 },
  correlated: { label: 'Correlated', color: '#38bdf8', max: 5 },
  leadLag: { label: 'Leads it', color: '#94a3b8', max: 3 },
}

/** Supply-chain neighbours: tickers in the stages right before and after the stages that contain the stock. */
export function chainNeighbours(r: Relations): { move: Move; chain: string; stage: string }[] {
  const out: { move: Move; chain: string; stage: string }[] = []
  const seen = new Set<string>([r.code])
  for (const chain of r.supplyChain) {
    chain.stages.forEach((st, i) => {
      if (!st.self) return
      for (const j of [i - 1, i + 1]) {
        const other = chain.stages[j]
        if (!other || other.self) continue
        for (const m of other.tickers)
          if (!seen.has(m.code)) {
            seen.add(m.code)
            out.push({ move: m, chain: chain.name, stage: other.stage })
          }
      }
    })
  }
  return out
}

/** Nodes around the stock, capped per kind; curated links win when a code appears twice. */
export function buildNodes(r: Relations): MapNode[] {
  const nodes: MapNode[] = []
  const seen = new Set<string>([r.code])
  const add = (n: MapNode) => {
    const key = n.code ?? n.id
    if (seen.has(key)) return
    if (nodes.filter((x) => x.kind === n.kind).length >= KIND_META[n.kind].max) return
    seen.add(key)
    nodes.push(n)
  }

  for (const g of r.groups)
    for (const p of g.peers)
      add({ id: `g:${p.code}`, label: p.code, kind: 'group', code: p.code, chgPct: p.chgPct, statistical: false, detail: `${g.name} group${p.name ? ` · ${p.name}` : ''}` })
  for (const f of r.factors)
    add({
      id: `f:${f.factor}:${f.direction}`,
      label: f.factor,
      kind: 'factor',
      code: f.proxy?.code ?? null,
      chgPct: f.proxy?.chgPct ?? null,
      statistical: false,
      detail: `${f.kind} factor, ${f.direction}${f.proxy ? ` · proxy ${f.proxy.code}` : ''}${f.note ? ` · ${f.note}` : ''}`,
    })
  for (const c of chainNeighbours(r))
    add({ id: `c:${c.move.code}`, label: c.move.code, kind: 'chain', code: c.move.code, chgPct: c.move.chgPct, statistical: false, detail: `${c.chain}: ${c.stage}` })
  for (const c of r.correlated)
    add({ id: `r:${c.code}`, label: c.code, kind: 'correlated', code: c.code, chgPct: c.chgPct, statistical: true, strength: Math.abs(c.rho), detail: `60-day correlation ${c.rho.toFixed(2)}${c.name ? ` · ${c.name}` : ''}` })
  for (const l of r.leadLag)
    add({ id: `l:${l.proxy}`, label: l.proxy, kind: 'leadLag', code: l.proxy, chgPct: l.chgPct, statistical: true, strength: Math.abs(l.rho), detail: `Previous-day ${l.proxy} move predicts it (rho ${l.rho.toFixed(2)}, t ${l.tStat.toFixed(1)})` })
  return nodes
}
