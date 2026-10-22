import { Panel } from '../components/ui'
import { usePane } from '../shell/workspace'
import { CheatSheet } from './help/CheatSheet'
import type { FunctionProps } from './types'

export default function HELP(_props: FunctionProps) {
  const pane = usePane()
  return (
    <Panel title="Help" subtitle="Cheat sheet · click a function to run its example">
      <CheatSheet onRun={(cmd) => pane.open(cmd)} />
    </Panel>
  )
}
