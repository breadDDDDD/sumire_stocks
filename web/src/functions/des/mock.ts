import { api } from '../../api/client'

/**
 * Dev-only preview data for DES/GP/FA/FCST: open the app with `?mock=1` to render these screens
 * without the API (e.g. while the database is unreachable). Shapes follow docs/api/*.md.
 */
export const MOCK = (() => {
  try {
    return new URLSearchParams(window.location.search).has('mock')
  } catch {
    return false
  }
})()

export async function getJson<T>(path: string, signal?: AbortSignal): Promise<T> {
  if (!MOCK) return api<T>(path, { signal })
  await new Promise((r) => setTimeout(r, 250))
  return mockFor(path) as T
}

function rng(seed: number) {
  let s = seed
  return () => {
    s = (s * 1664525 + 1013904223) % 4294967296
    return s / 4294967296
  }
}

const LAST = 5175

function tradingDates(n: number, end = new Date()): string[] {
  const out: string[] = []
  const d = new Date(Date.UTC(end.getUTCFullYear(), end.getUTCMonth(), end.getUTCDate()))
  while (out.length < n) {
    const wd = d.getUTCDay()
    if (wd !== 0 && wd !== 6) out.push(d.toISOString().slice(0, 10))
    d.setUTCDate(d.getUTCDate() - 1)
  }
  return out.reverse()
}

function dailyBars(n: number) {
  const dates = tradingDates(n)
  const r = rng(42)
  const closes: number[] = new Array(n)
  closes[n - 1] = LAST
  for (let i = n - 2; i >= 0; i--) closes[i] = Math.max(500, Math.round(closes[i + 1] * (1 - (r() - 0.5) * 0.04)))
  return dates.map((date, i) => {
    const close = closes[i]
    const open = i ? closes[i - 1] : close
    const high = Math.round(Math.max(open, close) * (1 + r() * 0.012))
    const low = Math.round(Math.min(open, close) * (1 - r() * 0.012))
    return { time: Date.parse(date) / 1000, date, open, high, low, close, volume: Math.round(1e8 + r() * 3e8) }
  })
}

function intradayBars(days: number, stepMin: number) {
  const dates = tradingDates(days)
  const r = rng(7)
  let p = LAST * 1.01
  const bars = []
  for (const date of dates) {
    const base = Date.parse(date) / 1000
    for (let m = 9 * 60; m < 15 * 60 + 50; m += stepMin) {
      if (m >= 12 * 60 && m < 13 * 60 + 30) continue
      const open = p
      p = Math.max(100, Math.round(p * (1 + (r() - 0.505) * 0.006)))
      bars.push({
        time: base + m * 60,
        date: null,
        open,
        high: Math.max(open, p) + 25,
        low: Math.min(open, p) - 25,
        close: p,
        volume: Math.round(1e6 + r() * 5e6),
      })
    }
  }
  return bars
}

function sma(values: number[], n: number) {
  return values.map((_, i) => (i + 1 < n ? null : values.slice(i + 1 - n, i + 1).reduce((a, b) => a + b, 0) / n))
}

function technicalsSeries() {
  const bars = dailyBars(750)
  const closes = bars.map((b) => b.close)
  const s20 = sma(closes, 20)
  const sd = closes.map((_, i) => {
    if (i < 19) return null
    const w = closes.slice(i - 19, i + 1)
    const m = s20[i] as number
    return Math.sqrt(w.reduce((a, b) => a + (b - m) ** 2, 0) / 20)
  })
  const r = rng(3)
  return {
    dates: bars.map((b) => b.date),
    sma20: s20,
    sma50: sma(closes, 50),
    sma200: sma(closes, 200),
    bbUpper: s20.map((m, i) => (m == null ? null : m + 2 * (sd[i] as number))),
    bbLower: s20.map((m, i) => (m == null ? null : m - 2 * (sd[i] as number))),
    rsi: closes.map((_, i) => (i < 14 ? null : 30 + r() * 40)),
    macdHist: closes.map((_, i) => (i < 26 ? null : (r() - 0.5) * 120)),
    volume: bars.map((b) => b.volume),
    volumeSma20: sma(
      bars.map((b) => b.volume),
      20,
    ),
  }
}

const today = new Date().toISOString().slice(0, 10)

const profile = {
  code: 'BBCA',
  name: 'PT Bank Central Asia Tbk',
  sector: 'Financials',
  industry: 'Banks',
  board: 'Utama',
  groupName: 'Djarum',
  indexMemberships: ['LQ45', 'IDX30', 'IDX80', 'KOMPAS100'],
  isIndex: false,
  description:
    'PT Bank Central Asia Tbk provides commercial banking and other financial services in Indonesia. It offers current, savings and time deposits, loans, credit cards, bancassurance and cash management, through a branch, ATM and digital network.',
  website: 'https://www.bca.co.id',
  asOf: today,
  keyStats: {
    last: LAST,
    marketCap: 635706095435776,
    pe: 10.96,
    pb: 2.35,
    divYield: 0.0722,
    roe: 0.2182,
    eps: 472,
    high52w: 8269.37,
    low52w: 4740,
    avgValueTraded20: 1479870532275,
    lotValue: 517500,
    tickSize: 25,
    nextTradingDay: today,
    araNext: 5550,
    arbNext: 4825,
    beta: 0.951,
  },
  fundamentalsUpdatedAt: new Date().toISOString(),
}

const technicals = {
  code: 'BBCA',
  asOf: today,
  source: 'mock',
  technicals: { date: today, close: LAST, change: -100, changePct: -1.8957, rsi: 33.07 },
  risk: {
    returns: { d1: -1.9, w1: -14.81, m1: -17.86, m3: -17.21, ytd: -32.63, y1: -27.23 },
    beta: 0.951,
    correlation: 0.703,
    sharpe: -0.95,
    volatilityPct: 38.63,
    rel3MPct: -5.13,
    rel6MPct: 8.89,
  },
}

const forecast = {
  code: 'BBCA',
  asOf: today,
  last: LAST,
  models: [
    {
      model: 'baseline',
      asOf: today,
      horizons: [
        { horizon: 5, p10: 4610.71, p50: 5142.46, p90: 5735.54, probUp: 0.4705, sigma: 0.08517 },
        { horizon: 20, p10: 4256.41, p50: 5046.07, p90: 5982.23, probUp: 0.4247, sigma: 0.132795 },
        { horizon: 60, p10: 3741.85, p50: 4797.76, p90: 6151.64, probUp: 0.3482, sigma: 0.19396 },
      ],
    },
    {
      model: 'chronos-2',
      asOf: today,
      horizons: [
        { horizon: 5, p10: 4900, p50: 5210, p90: 5480, probUp: 0.55, sigma: 0.06 },
        { horizon: 20, p10: 4700, p50: 5260, p90: 5800, probUp: 0.56, sigma: 0.1 },
        { horizon: 60, p10: 4400, p50: 5350, p90: 6300, probUp: 0.58, sigma: 0.16 },
      ],
    },
  ],
  garch: {
    annualizedVol: 0.6793,
    longRunVol: 0.3531,
    volPercentile: 98.2,
    persistence: 0.8317,
    converged: true,
    cone: [
      { horizon: 5, sigma: 0.08517, minus2: 4364.48, minus1: 4752.49, plus1: 5635.07, plus2: 6136.04 },
      { horizon: 20, sigma: 0.132795, minus2: 3967.95, minus1: 4531.46, plus1: 5909.93, plus2: 6749.24 },
      { horizon: 60, sigma: 0.19396, minus2: 3511.07, minus1: 4262.6, plus1: 6282.7, plus2: 7627.49 },
    ],
  },
  score: {
    total: -70.1,
    label: 'Strongly Bearish',
    coverage: 0.75,
    components: [
      { component: 'trend', score: -100, weight: 25, reason: 'Below 200-day MA, below 50-day, trend strong (ADX 28)' },
      { component: 'momentum', score: -51.7, weight: 20, reason: 'RSI 33, MACD histogram negative, -5.1% vs IHSG over 3M' },
      { component: 'volume', score: -33.2, weight: 10, reason: 'OBV falling (distribution)' },
      { component: 'volatility', score: -96.4, weight: 10, reason: 'GARCH vol at 98th percentile (elevated)' },
      { component: 'valuation', score: null, weight: 15, reason: 'No valuation data' },
      { component: 'forecast', score: -42.5, weight: 10, reason: 'baseline 20d: P(up) 42%, median -2.5%' },
      { component: 'news', score: 35, weight: 10, reason: 'Two positive insights this week' },
    ],
  },
  trackRecord: [
    { model: 'baseline', horizon: 20, scope: 'all', n: 5600, dirAccuracy: 0.51, mape: 0.071, coverage80: 0.78, beatsBaseline: null },
    { model: 'chronos-2', horizon: 20, scope: 'all', n: 220, dirAccuracy: 0.55, mape: 0.066, coverage80: 0.81, beatsBaseline: true },
    { model: 'baseline', horizon: 5, scope: 'all', n: 5600, dirAccuracy: 0.5, mape: 0.038, coverage80: 0.8, beatsBaseline: null },
    { model: 'baseline', horizon: 20, scope: 'Financials', n: 700, dirAccuracy: 0.53, mape: 0.05, coverage80: 0.8, beatsBaseline: null },
  ],
}

const fundamentals = {
  code: 'BBCA',
  sector: 'Financials',
  updatedAt: new Date().toISOString(),
  ratios: {
    marketCap: 635706095435776,
    pe: 10.96,
    pb: 2.35,
    roe: 0.218,
    der: null,
    npm: 0.531,
    eps: 472,
    divYield: 0.0722,
    revenueGrowth: 0.025,
    earningsGrowth: 0.004,
  },
  sectorMedians: { pe: 9.8, pb: 1.4, roe: 0.15, divYield: 0.051, n: 14 },
  statements: {
    incomeAnnual: [
      { endDate: '2025-12-31', items: { totalRevenue: 111861388000000, netIncome: 57537287000000, operatingIncome: 71200000000000 } },
      { endDate: '2024-12-31', items: { totalRevenue: 108100000000000, netIncome: 54800000000000 } },
      { endDate: '2023-12-31', items: { totalRevenue: 98600000000000, netIncome: 48600000000000 } },
    ],
    incomeQuarterly: [
      { endDate: '2026-06-30', items: { totalRevenue: 28400000000000, netIncome: 14900000000000 } },
      { endDate: '2026-03-31', items: { totalRevenue: 27901529000000, netIncome: 14684123000000 } },
    ],
    balanceSheet: [{ endDate: '2025-12-31', items: {} }],
    cashflow: [{ endDate: '2025-12-31', items: { netIncome: 57537287000000, freeCashFlow: -3200000000000 } }],
  },
  statementsError: null,
}

function mockFor(path: string): unknown {
  const code = /\/stocks\/([^/]+)\//.exec(path)?.[1] ?? /\/chart\/([^/?]+)/.exec(path)?.[1] ?? 'BBCA'
  const withCode = <T extends object>(o: T) => ({ ...o, code })
  if (path.includes('/profile')) return { ...profile, code, name: code === 'BBCA' ? profile.name : `PT ${code} Tbk` }
  if (path.includes('/technicals')) return path.includes('series=') ? { ...withCode(technicals), series: technicalsSeries() } : withCode(technicals)
  if (path.includes('/forecast')) return withCode(forecast)
  if (path.includes('/fundamentals')) return withCode(fundamentals)
  if (path.includes('/chart/')) {
    const range = /range=([^&]+)/.exec(path)?.[1] ?? '6m'
    const n = { '1m': 21, '3m': 63, '6m': 126, ytd: 200, '1y': 250, '3y': 750 }[range]
    const bars = range === '1d' ? intradayBars(1, 5) : range === '5d' ? intradayBars(5, 15) : dailyBars(n ?? 126)
    return { code, range, interval: range.endsWith('d') ? '5m' : '1d', source: 'mock', prevClose: range.endsWith('d') ? LAST * 1.01 : null, bars }
  }
  throw new Error(`No mock for ${path}`)
}

