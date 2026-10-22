import ComingSoon from './ComingSoon'
import type { FunctionProps } from './types'

export default function RQ({ code, ticker, args }: FunctionProps) {
  return <ComingSoon code={code} ticker={ticker} args={args} />
}
