import ComingSoon from './ComingSoon'
import type { FunctionProps } from './types'

export default function INS({ code, ticker, args, force }: FunctionProps) {
  return <ComingSoon code={code} ticker={ticker} args={args} force={force} />
}
