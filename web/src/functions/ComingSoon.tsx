import { Empty, Panel } from '../components/ui'
import { formatEntry } from '../lib/command'
import { FUNCTION_META, type FunctionProps } from './types'

/** Placeholder used by function stubs until their screen is built. */
export default function ComingSoon(props: FunctionProps) {
  const meta = FUNCTION_META[props.code]
  return (
    <Panel title={props.ticker ? `${props.ticker} ${meta.code}` : meta.code} subtitle={meta.name}>
      <Empty title={`${meta.name} · coming soon`} message={meta.description}>
        <span className="muted">{formatEntry(props)}</span>
      </Empty>
    </Panel>
  )
}
