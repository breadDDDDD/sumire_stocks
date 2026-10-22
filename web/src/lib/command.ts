import { FUNCTION_META, isFunctionCode, type FunctionCode, type FunctionProps } from '../functions/types'

export type ParseResult = { ok: true; entry: FunctionProps } | { ok: false; error: string }

const TICKER_RE = /^[A-Z0-9^.=-]{1,15}$/

/** Turn a token into a function code, handling `INS!` and `?`. */
function asFunction(token: string): { code: FunctionCode; force: boolean } | null {
  const t = token.toUpperCase()
  if (t === '?') return { code: 'HELP', force: false }
  if (t === 'INS!') return { code: 'INS', force: true }
  return isFunctionCode(t) ? { code: t, force: false } : null
}

function build(code: FunctionCode, ticker: string | undefined, rest: string[], raw: string, force: boolean): FunctionProps {
  if (code === 'ASK') {
    const question = raw.trim()
    return { code, ticker, args: question ? [question] : [] }
  }
  if (code === 'COMP') {
    const tickers = [ticker, ...rest.map((r) => r.toUpperCase())].filter((t): t is string => !!t)
    return { code, args: [...new Set(tickers)].slice(0, 4) }
  }
  return force ? { code, ticker, args: rest, force } : { code, ticker, args: rest }
}

/**
 * Parse `[TICKER] [FUNCTION] [args...]`.
 * `fallbackTicker` (the focused pane's ticker) fills in when a ticker function is typed alone.
 */
export function parseCommand(input: string, fallbackTicker?: string): ParseResult {
  const text = input.trim()
  if (!text) return { ok: false, error: 'Type a ticker or a function' }
  const tokens = text.split(/\s+/)

  // tail of the raw string after the first n tokens, used for ASK's free text
  const tail = (n: number) => {
    let s = text
    for (let i = 0; i < n; i++) s = s.replace(/^\S+\s*/, '')
    return s
  }

  let ticker: string | undefined
  let fn = asFunction(tokens[0])
  let restStart = 1

  if (!fn) {
    const t = tokens[0].toUpperCase()
    if (!TICKER_RE.test(t)) return { ok: false, error: `Unknown ticker or function "${tokens[0]}"` }
    ticker = t
    fn = tokens[1] ? asFunction(tokens[1]) : { code: 'DES', force: false }
    if (!fn) return { ok: false, error: `Unknown function "${tokens[1]}"` }
    restStart = 2
  }

  const { code, force } = fn
  if (!ticker && FUNCTION_META[code].needsTicker) {
    if (!fallbackTicker) return { ok: false, error: `${code} needs a ticker, e.g. ${FUNCTION_META[code].example}` }
    ticker = fallbackTicker
  }
  if (code === 'ASK' && !tail(restStart)) return { ok: false, error: 'ASK needs a question, e.g. ASK why did banks drop?' }

  return { ok: true, entry: build(code, ticker, tokens.slice(restStart), tail(restStart), force) }
}

/** Readable command for an entry, e.g. "BBCA INS!". */
export function formatEntry(e: FunctionProps): string {
  const code = e.code === 'INS' && e.force ? 'INS!' : e.code
  if (e.code === 'COMP') return ['COMP', ...e.args].join(' ')
  if (e.code === 'DES' && e.ticker && !e.args.length) return e.ticker
  return [e.ticker, code, ...e.args].filter(Boolean).join(' ')
}
