import { Panel } from '../components/ui'
import { InsightView } from './ins/InsightView'
import type { FunctionProps } from './types'

/**
 * `BBCA INS` shows the cached insight; `BBCA INS!` queues a fresh one.
 * `INS SECTOR Energy` (or `INS sector Basic Materials`) shows a sector narrative.
 */
export default function INS({ ticker, args, force }: FunctionProps) {
  const sector = args[0]?.toLowerCase() === 'sector' ? args.slice(1).join(' ').trim() : ''
  const kind = sector ? 'sector' : 'stock'
  const subject = sector || ticker || ''

  return (
    <Panel
      title={sector ? 'SECT INS' : `${subject} INS${force ? '!' : ''}`}
      subtitle={sector ? `${sector} · AI sector narrative` : 'AI insight · why it moved, connections, catalysts, risks'}
    >
      {subject ? (
        <InsightView key={`${kind}:${subject}`} kind={kind} subject={subject} force={force} />
      ) : (
        <p className="muted" style={{ padding: 12 }}>
          Type a ticker first, e.g. ADRO INS.
        </p>
      )}
    </Panel>
  )
}
