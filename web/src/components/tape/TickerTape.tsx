const ITEMS = ['IHSG', 'LQ45', 'USD/IDR', 'COAL', 'WTI', 'GOLD', 'US10Y']

/** Bottom ticker tape. Stub: a later task wires live quotes. */
export default function TickerTape() {
  return (
    <div style={{ display: 'flex', gap: 22, alignItems: 'center', height: '100%', padding: '0 12px', overflow: 'hidden' }}>
      {ITEMS.map((i) => (
        <span key={i} style={{ whiteSpace: 'nowrap' }}>
          <span style={{ color: 'var(--amber)' }}>{i}</span> <span className="muted">—</span>
        </span>
      ))}
    </div>
  )
}
