import ComingSoon from './ComingSoon'
import type { FunctionProps } from './types'

export default function ASK({ code, ticker, args }: FunctionProps) {
  return <ComingSoon code={code} ticker={ticker} args={args} />
}
