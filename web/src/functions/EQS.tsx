import ComingSoon from './ComingSoon'
import type { FunctionProps } from './types'

export default function EQS({ code, ticker, args }: FunctionProps) {
  return <ComingSoon code={code} ticker={ticker} args={args} />
}
