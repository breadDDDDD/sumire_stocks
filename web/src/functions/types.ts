export const FUNCTION_CODES = [
  'MKT',
  'BRIEF',
  'DES',
  'GP',
  'FA',
  'FCST',
  'INS',
  'REL',
  'ASK',
  'RQ',
  'N',
  'W',
  'EQS',
  'SECT',
  'COMP',
  'ALRT',
  'HELP',
] as const

export type FunctionCode = (typeof FUNCTION_CODES)[number]

/** What a pane shows. Also the props every function component receives. */
export interface FunctionProps {
  code: FunctionCode
  /** Upper-case ticker code, e.g. "BBCA". */
  ticker?: string
  /** Remaining command tokens. ASK gets the whole question as args[0]; COMP gets tickers. */
  args: string[]
  /** `INS!`: queue a fresh insight instead of showing the cached one. */
  force?: boolean
}

export interface FunctionMeta {
  code: FunctionCode
  name: string
  description: string
  example: string
  /** Needs a ticker; falls back to the focused pane's ticker when typed alone. */
  needsTicker?: boolean
}

export const FUNCTION_META: Record<FunctionCode, FunctionMeta> = {
  MKT: { code: 'MKT', name: 'Market', description: 'IHSG, breadth, movers, regime, macro', example: 'MKT' },
  BRIEF: { code: 'BRIEF', name: 'Brief', description: "Today's morning brief / closing recap", example: 'BRIEF' },
  DES: { code: 'DES', name: 'Description', description: 'Company profile and key stats', example: 'BBCA', needsTicker: true },
  GP: { code: 'GP', name: 'Price chart', description: 'Chart with indicators and forecast cone', example: 'BBRI GP', needsTicker: true },
  FA: { code: 'FA', name: 'Fundamentals', description: 'Fundamentals and valuation vs sector', example: 'TLKM FA', needsTicker: true },
  FCST: { code: 'FCST', name: 'Forecast', description: 'Forecast ranges, signal score, track record', example: 'ASII FCST', needsTicker: true },
  INS: { code: 'INS', name: 'Insight', description: 'AI insight with connections (INS! = fresh)', example: 'ADRO INS!', needsTicker: true },
  REL: { code: 'REL', name: 'Relations', description: 'Relations map: groups, commodities, macro', example: 'BREN REL', needsTicker: true },
  ASK: { code: 'ASK', name: 'Ask', description: 'Ask a question with the current context', example: 'ASK why did banks drop?' },
  RQ: { code: 'RQ', name: 'Research queue', description: 'Queue, usage vs caps, runner status', example: 'RQ' },
  N: { code: 'N', name: 'Headlines', description: 'News for one ticker or the whole market', example: 'BMRI N' },
  W: { code: 'W', name: 'Watchlists', description: 'Manage watchlists', example: 'W' },
  EQS: { code: 'EQS', name: 'Screener', description: 'Equity screener with presets', example: 'EQS' },
  SECT: { code: 'SECT', name: 'Sectors', description: 'Sector heatmap and rotation (SECT INS)', example: 'SECT' },
  COMP: { code: 'COMP', name: 'Compare', description: 'Compare up to 4 tickers', example: 'COMP BBCA BBRI BMRI' },
  ALRT: { code: 'ALRT', name: 'Alerts', description: 'Price and signal alerts', example: 'ALRT' },
  HELP: { code: 'HELP', name: 'Help', description: 'Cheat sheet: functions and keys', example: '?' },
}

export function isFunctionCode(s: string): s is FunctionCode {
  return (FUNCTION_CODES as readonly string[]).includes(s)
}
