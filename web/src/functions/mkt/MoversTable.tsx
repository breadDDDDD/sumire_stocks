import { useRef } from 'react'
import { usePane } from '../../shell/workspace'
import { Num, Table, type Column } from '../../components/ui'
import type { Mover } from './quotes'

const columns: Column<Mover>[] = [
  { key: 'code', header: 'Code', render: (r) => <span title={r.name}>{r.code}</span> },
  { key: 'price', header: 'Last', align: 'right', render: (r) => <Num value={r.price} format="price" colorBy={r.changePct} flash /> },
  { key: 'changePct', header: 'Chg', align: 'right', render: (r) => <Num value={r.changePct} format="pct" signed colored flash /> },
  { key: 'value', header: 'Value', align: 'right', render: (r) => <Num value={r.value} format="compact" /> },
]

/** Movers list: Enter / click opens DES, Shift+Enter / Shift+click opens GP, in this pane. */
export function MoversTable({ rows }: { rows: Mover[] }) {
  const pane = usePane()
  const shift = useRef(false)
  return (
    <div
      onKeyDownCapture={(e) => (shift.current = e.shiftKey)}
      onMouseDownCapture={(e) => (shift.current = e.shiftKey)}
      style={{ height: '100%' }}
    >
      <Table
        columns={columns}
        rows={rows}
        rowKey={(r) => r.code}
        onOpen={(r) => pane.open({ code: shift.current ? 'GP' : 'DES', ticker: r.code, args: [] })}
        empty={<p className="muted" style={{ padding: 8 }}>Nothing yet.</p>}
      />
    </div>
  )
}
