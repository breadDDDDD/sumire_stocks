# Analytics and forecast API

Base `http://localhost:5080`. JSON camelCase, enums camelCase strings, dates `YYYY-MM-DD`, times ISO-8601 WIB.
Errors are ProblemDetails: `404` unknown code, `400` bad screener request, `503` when neither the database nor Yahoo
can supply bars (`title` says which).

**Where the numbers come from.** Everything is computed once per trading day into an in-memory snapshot of the core
universe (≈110 stocks + IHSG/LQ45): indicators, risk stats, GARCH, baseline forecast, signal score, breadth, sector
indexes. It is rebuilt by the EOD pipeline after the bars land (16:30 WIB) and warmed up at startup, so requests are
served from memory (screener and sectors answer in milliseconds). When the database is unreachable the snapshot is
built from Yahoo bars (first build takes ~25 s; `technicals.source` = `"yahoo"`). A stock outside the core universe is
analysed on first request (3 years of bars fetched) and cached for the day (`source` = `"on-demand"`).

**Units.** Prices in Rupiah. Fields ending in `Pct`, `returns.*`, sector `d1/w1/m1/m3/rsM1/rsM3` and screener `chgPct`
are **percent** (−1.9 = −1.9 %). `divYield`, `roe`, `npm`, `der`, `revenueGrowth`, `earningsGrowth`, `probUp`,
`dirAccuracy`, `mape`, `coverage80` are **fractions** (0.0722 = 7.22 %). In `technicals`, `hv20/hv60` are annualized
fractions while `atrPct`, `changePct`, `roc20`, `pctFrom52wHigh/Low` are percent. GARCH `annualizedVol` is a fraction.

## GET /api/stocks/{code}/profile

Instrument info, business summary (Yahoo, cached 24 h; `null` if unavailable) and key stats. Works for indexes too
(fundamental fields `null`). `araNext`/`arbNext` are the auto-rejection limits for `nextTradingDay` from today's close.

```json
{
  "code": "BBCA",
  "name": "PT Bank Central Asia Tbk",
  "sector": "Financials",
  "industry": "Banks",
  "board": "Utama",
  "groupName": "Djarum",
  "indexMemberships": ["LQ45", "IDX30", "IDX80", "KOMPAS100"],
  "isIndex": false,
  "description": "PT Bank Central Asia Tbk provides commercial banking and other financial services…",
  "website": "https://www.bca.co.id",
  "asOf": "2026-10-22",
  "keyStats": {
    "last": 5175, "marketCap": 635706095435776, "pe": 10.96, "pb": 2.35, "divYield": 0.0722, "roe": 0.2182, "eps": 472,
    "high52w": 8269.37, "low52w": 4740, "avgValueTraded20": 1479870532275,
    "lotValue": 517500, "tickSize": 25, "nextTradingDay": "2026-10-23", "araNext": 5550, "arbNext": 4825, "beta": 0.951
  },
  "fundamentalsUpdatedAt": "2026-10-22T22:10:16+07:00"
}
```

## GET /api/stocks/{code}/technicals?series=

Latest indicator snapshot, risk stats and support/resistance. `series` (optional, comma-separated: `sma20`, `sma50`,
`sma200`, `ema20`, `bb`, `rsi`, `macd`, `vol`) adds per-bar overlay arrays aligned with `series.dates` (≈3 years; join
to chart bars by date). Values are `null` where an indicator has no history yet.

```json
{
  "code": "BBCA",
  "asOf": "2026-10-22",
  "source": "db",
  "technicals": {
    "date": "2026-10-22", "close": 5175, "change": -100, "changePct": -1.8957,
    "sma20": 5846, "sma50": 6188.48, "sma200": 6449.88, "ema20": 5741.38, "ema50": 6025.74, "ema200": 6493.96,
    "sma20Slope": -5.92, "sma50Slope": -2.42, "sma200Slope": -1.67,
    "lastGoldenCross": null, "lastDeathCross": { "kind": "death", "date": "2025-01-16" },
    "adx": 28.4, "plusDi": 21.33, "minusDi": 41.92,
    "rsi": 33.07, "macd": -286.64, "macdSignal": -198.41, "macdHist": -88.23, "stochK": 32.65, "stochD": 21.96, "roc20": -16.87,
    "bbUpper": 6702.51, "bbMiddle": 5846, "bbLower": 4989.49, "bbPercentB": 0.108, "bbWidth": 0.293,
    "atr": 210.33, "atrPct": 4.06, "hv20": 0.708, "hv60": 0.444,
    "obv": -5437611000, "obvSlope": -0.277, "volume": 286928500, "avgVolume20": 269747105, "volumeRatio": 1.06,
    "valueTraded": 1484854987500, "avgValueTraded20": 1479870532275,
    "high52w": 8269.37, "low52w": 4740, "pctFrom52wHigh": -37.42, "pctFrom52wLow": 9.18,
    "pivots": { "p": 5183.33, "r1": 5266.67, "r2": 5358.33, "r3": 5441.67, "s1": 5091.67, "s2": 5008.33, "s3": 4916.67 },
    "support": [{ "price": 4786.09, "touches": 1, "lastTouched": "2026-06-09" }],
    "resistance": [{ "price": 5528.57, "touches": 1, "lastTouched": "2026-06-30" }, { "price": 6083, "touches": 2, "lastTouched": "2026-07-24" }]
  },
  "risk": {
    "returns": { "d1": -1.9, "w1": -14.81, "m1": -17.86, "m3": -17.21, "ytd": -32.63, "y1": -27.23 },
    "beta": 0.951, "correlation": 0.703, "sharpe": -0.95, "volatilityPct": 38.63,
    "maxDrawdown": { "depthPct": 41.43, "peak": "2025-11-05", "trough": "2026-06-08" },
    "rel3MPct": -5.13, "rel6MPct": 8.89
  },
  "series": {
    "dates": ["2023-10-23", "…"],
    "sma50": [null, "…", 6188.483],
    "bbUpper": ["…"], "bbLower": ["…"],
    "volume": [93211200, "…"], "volumeSma20": [null, "…"]
  }
}
```

`series` keys: `sma20`, `sma50`, `sma200`, `ema20`, `bbUpper` + `bbLower` (for `bb`), `rsi`, `macdHist` (for `macd`),
`volume` + `volumeSma20` (for `vol`). Indicators use split/dividend-adjusted prices; `close`, `change`, `valueTraded`
are as traded. Beta/correlation are against IHSG over the last year. Levels are clustered swing highs/lows (nearest 3).

## GET /api/stocks/{code}/fundamentals

Ratios (stored weekly in `fundamentals`, refreshed on request when older than 7 days), sector medians from the core
universe and financial statements from Yahoo (cached 24 h). Yahoo's statements are sparse for IDX stocks (often only
revenue and net income); `items` holds whatever non-zero lines exist. 404 for indexes.

```json
{
  "code": "BBCA",
  "sector": "Financials",
  "updatedAt": "2026-10-22T22:10:16+07:00",
  "ratios": { "marketCap": 635706095435776, "pe": 10.96, "pb": 2.35, "roe": 0.218, "der": null, "npm": 0.531, "eps": 472,
              "divYield": 0.0722, "revenueGrowth": 0.025, "earningsGrowth": 0.004 },
  "sectorMedians": { "pe": 9.8, "pb": 1.4, "roe": 0.15, "divYield": 0.051, "n": 14 },
  "statements": {
    "incomeAnnual": [{ "endDate": "2025-12-31", "items": { "totalRevenue": 111861388000000, "netIncome": 57537287000000 } }],
    "incomeQuarterly": [{ "endDate": "2026-03-31", "items": { "totalRevenue": 27901529000000, "netIncome": 14684123000000 } }],
    "balanceSheet": [{ "endDate": "2025-12-31", "items": {} }],
    "cashflow": [{ "endDate": "2025-12-31", "items": { "netIncome": 57537287000000 } }]
  },
  "statementsError": null
}
```

`ratios` is `null` when Yahoo has no data and nothing is stored; `sectorMedians` is `null` until fundamentals are stored
for the sector; `statementsError` is a message when Yahoo is down (the rest still returns).

## GET /api/stocks/{code}/forecast

Forecasts per model (the baseline is always present; `chronos-2` etc. appear once their nightly rows exist), the GARCH
range, the signal-score breakdown and the track record.

```json
{
  "code": "BBCA",
  "asOf": "2026-10-22",
  "last": 5175,
  "models": [
    {
      "model": "baseline",
      "asOf": "2026-10-22",
      "horizons": [
        { "horizon": 5, "p10": 4610.71, "p50": 5142.46, "p90": 5735.54, "probUp": 0.4705, "sigma": 0.08517 },
        { "horizon": 20, "p10": 4256.41, "p50": 5046.07, "p90": 5982.23, "probUp": 0.4247, "sigma": 0.132795 },
        { "horizon": 60, "p10": 3741.85, "p50": 4797.76, "p90": 6151.64, "probUp": 0.3482, "sigma": 0.19396 }
      ]
    }
  ],
  "garch": {
    "annualizedVol": 0.6793, "longRunVol": 0.3531, "volPercentile": 98.2, "persistence": 0.8317, "converged": true,
    "cone": [
      { "horizon": 5, "sigma": 0.08517, "minus2": 4364.48, "minus1": 4752.49, "plus1": 5635.07, "plus2": 6136.04 },
      { "horizon": 20, "sigma": 0.132795, "minus2": 3967.95, "minus1": 4531.46, "plus1": 5909.93, "plus2": 6749.24 },
      { "horizon": 60, "sigma": 0.19396, "minus2": 3511.07, "minus1": 4262.6, "plus1": 6282.7, "plus2": 7627.49 }
    ]
  },
  "score": {
    "total": -70.1,
    "label": "Strongly Bearish",
    "coverage": 0.75,
    "components": [
      { "component": "trend", "score": -100, "weight": 25, "reason": "Below 200-day MA, below 50-day, trend strong (ADX 28)" },
      { "component": "momentum", "score": -51.7, "weight": 20, "reason": "RSI 33, MACD histogram negative, -5.1% vs IHSG over 3M" },
      { "component": "volume", "score": -33.2, "weight": 10, "reason": "OBV falling (distribution)" },
      { "component": "volatility", "score": -96.4, "weight": 10, "reason": "GARCH vol at 98th percentile (elevated)" },
      { "component": "valuation", "score": null, "weight": 15, "reason": "No valuation data" },
      { "component": "forecast", "score": -42.5, "weight": 10, "reason": "baseline 20d: P(up) 42%, median -2.5%" },
      { "component": "news", "score": null, "weight": 10, "reason": "No recent insights" }
    ]
  },
  "trackRecord": [
    { "model": "baseline", "horizon": 20, "scope": "all", "n": 5600, "dirAccuracy": 0.51, "mape": 0.071, "coverage80": 0.78, "beatsBaseline": null },
    { "model": "chronos-2", "horizon": 20, "scope": "all", "n": 220, "dirAccuracy": 0.55, "mape": 0.066, "coverage80": 0.81, "beatsBaseline": true },
    { "model": "baseline", "horizon": 20, "scope": "Financials", "n": 700, "dirAccuracy": 0.53, "mape": 0.05, "coverage80": 0.8, "beatsBaseline": null }
  ]
}
```

- `sigma` is the σ of the h-day log return; the cone is `last × exp(±kσ)` (no drift).
- `score.components[].score` is −100…100 or `null` (no data; skipped and the other weights renormalized). `coverage` is
  the share of total weight that had data. Labels: ≥60 Strongly Bullish, ≥20 Bullish, >−20 Neutral, >−60 Bearish, else
  Strongly Bearish.
- The forecast component uses a model only when its 20-day track record (scope `all`, ≥30 forecasts) beats the
  baseline on direction without a worse MAPE; otherwise the baseline.
- `trackRecord` scopes: `all` (all stocks) and the stock's sector; for `IHSG`, scope `IHSG`. Empty until forecasts
  mature (a one-time 12-month weekly backtest fills it on the first EOD run).

## GET /api/market/overview

```json
{
  "asOf": "2026-10-22",
  "time": "2026-10-22T16:40:02+07:00",
  "ihsg": { "code": "IHSG", "name": "IDX Composite (IHSG)", "price": 5387.905, "change": 59.54, "changePct": 1.1175, "prevClose": 5328.36,
            "open": 5247.98, "high": 5422.59, "low": 5185.53, "volume": 0, "value": null, "time": "2026-10-22T16:00:05+07:00", "source": "live" },
  "lq45": { "code": "LQ45", "…": "same Quote shape as /api/quotes" },
  "regime": {
    "label": "Risk-off",
    "score": -4,
    "reasons": ["IHSG below 200-day MA", "IHSG below 50-day MA", "Breadth weak: 9% above 50-day MA", "Volatility elevated (GARCH 99th percentile)"]
  },
  "breadth": { "date": "2026-10-22", "total": 110, "advancers": 73, "decliners": 29, "unchanged": 8,
               "pctAboveSma50": 9.1, "pctAboveSma200": 17.3, "newHighs": 1, "newLows": 4 },
  "gainers": [{ "code": "TOBA", "name": "PT TBS Energi Utama Tbk", "price": 416, "changePct": 24.55, "value": 74641881600 }],
  "losers": [{ "code": "…", "name": "…", "price": 0, "changePct": -6.5, "value": 0 }],
  "mostActive": [{ "code": "BBCA", "name": "PT Bank Central Asia Tbk", "price": 5175, "changePct": -1.9, "value": 1484854987500 }],
  "macro": [{ "code": "USDIDR", "…": "Quote" }, { "code": "GOLD", "…": "Quote" }],
  "ihsgForecast": {
    "asOf": "2026-10-22", "last": 5387.9,
    "horizons": [{ "horizon": 5, "p10": 4715.48, "p50": 5344.87, "p90": 6058.28, "probUp": 0.4673 },
                 { "horizon": 20, "p10": 4174.5, "p50": 5217.84, "p90": 6521.94, "probUp": 0.4269 },
                 { "horizon": 60, "p10": 3581.63, "p50": 4893.63, "p90": 6686.23, "probUp": 0.3464 }],
    "annualizedVol": 0.7187, "volPercentile": 99.4
  },
  "moversSource": "quotes"
}
```

- Movers (top 5 each, core stocks) use delayed live quotes during the session and the last close afterwards
  (`moversSource: "quotes"`); if quotes fail they come from the snapshot (`"snapshot"`). `changePct` is percent.
- `regime.score` −4…4: ±1 for IHSG vs its 200-day and 50-day MA, ±1 for breadth (≥60 % / ≤40 % above the 50-day MA),
  −1 when GARCH vol ≥80th percentile, +1 when ≤30th. ≥2 Risk-on, ≤−2 Risk-off, else Neutral.
- `macro`: tape instruments other than IHSG/LQ45 (regional indexes, USD/IDR, oil, gold, US10Y).

## GET /api/market/sectors

Own sector indexes (level 1000 one year ago), cap-weighted with today's market caps (`weighting: "marketCap"`;
members without a cap get the median cap) or equal-weighted when no caps are stored (`"equal"`).

```json
{
  "asOf": "2026-10-22",
  "sectors": [
    {
      "name": "Basic Materials",
      "weighting": "marketCap",
      "d1": 1.87, "w1": -11.48, "m1": -16.38, "m3": -10.66,
      "rsM1": -2.21, "rsM3": 4.28,
      "rotation": { "rsRatio": 98.51, "rsMomentum": 97.31, "quadrant": "lagging", "date": "2026-10-22" },
      "trail": [{ "rsRatio": 100.8, "rsMomentum": 99.61, "quadrant": "weakening", "date": "2026-10-15" }],
      "members": [{ "code": "TINS", "name": "PT Timah Tbk", "marketCap": 28600000000000, "weight": 0.0588, "last": 3840, "chgPct": 1.59 }]
    }
  ]
}
```

- `d1…m3`: index performance in percent. `rsM1`/`rsM3`: index return minus IHSG's, percentage points.
- Rotation (RRG style, centred on 100): `rsRatio` = 100 × (index/IHSG) ÷ its 60-day mean, `rsMomentum` = 100 × rsRatio ÷
  rsRatio 10 days ago. Quadrants `leading` (≥100, ≥100), `weakening` (≥100, <100), `lagging` (<100, <100),
  `improving` (<100, ≥100). `trail` = last 5 sessions (oldest first) for drawing the tail.
- `members` sorted by weight (sums to 1); use `marketCap` (or `weight`) for the treemap size and `chgPct` for colour.

## GET /api/screener/presets

```json
{
  "presets": [{ "name": "oversold-uptrend", "description": "RSI under 35 while still above the 200-day MA" }, "…"],
  "numericFields": ["score", "last", "chgPct", "rsi", "…"],
  "textFields": ["sector", "industry", "group", "board", "label", "index"]
}
```

## POST /api/screener

Body: a preset, filters, or both (all conditions AND-ed). `sort` any numeric field (default: the preset's, else
`score`), `desc` (default true), `limit` 1–500 (default 100; `top-score` 30).

```json
{ "preset": "oversold-uptrend", "filters": [{ "field": "sector", "op": "eq", "value": "Energy" }], "sort": "rsi", "desc": false, "limit": 50 }
```

```json
{
  "preset": "oversold-uptrend",
  "asOf": "2026-10-22",
  "count": 3,
  "rows": [
    { "code": "PTRO", "name": "PT Petrosea Tbk", "sector": "Energy", "last": 3070, "chgPct": 0.33, "score": -79.9, "label": "Strongly Bearish",
      "rsi": 17.2, "pe": null, "pb": null, "divYield": null, "volRatio": 0.89 }
  ]
}
```

- `count` is the number of matches before `limit`.
- Numeric ops: `gt`, `gte`, `lt`, `lte`, `eq`, `neq` (number `value`). Text ops: `eq`, `neq`, `contains` (string value,
  case-insensitive). A stock missing the field never matches. Unknown field/op/preset → 400 with `detail`.
- Numeric fields: `score`, `last`, `chgPct`, `rsi`, `adx`, `stochK`, `macdHist`, `volRatio`, `valueTraded` (20-day avg,
  Rp), `atrPct`, `hv20` (%), `distSma50`/`distSma200` (% above the MA), `pctFrom52wHigh`, `pctFrom52wLow`,
  `goldenCrossDays`/`deathCrossDays` (sessions since), `ret1w`, `ret1m`, `ret3m`, `ret1y`, `rel3m` (% vs IHSG), `beta`,
  `volPercentile`, `pe`, `pb`, `roe`, `der`, `divYield` (fractions for the last three), `marketCap`.
- Text fields: `sector`, `industry`, `group`, `board`, `label`, `index` (pipe-joined memberships, use `contains`).
- Presets: `oversold-uptrend` (RSI < 35, above MA200), `breakout-52w` (within 1 % of the 52-week high, volume ≥ 1.5×),
  `golden-cross` (≤ 10 sessions ago), `value` (0 < PE ≤ 12, PB ≤ 1.5), `dividend` (yield ≥ 4 %), `volume-surge`
  (volume ≥ 2×), `top-score`.

## EOD pipeline steps (no endpoints)

Run by the market EOD job after bars land, in this order (other areas may slot between):

| Order | Step | Writes |
|---|---|---|
| 100 | analytics snapshot | memory (indicators, GARCH, baseline, breadth, sectors) |
| 200 | baseline forecasts | `forecasts` (model `baseline`, core stocks + IHSG, horizons 5/20/60) |
| 300 | forecast evaluation | `forecasts` (one-time 12-month weekly backtest), `forecast_metrics` (scopes `all`, `IHSG`, sectors; `news` untouched) |
| 400 | signal scores | `signal_scores` (one row per core stock, `reasons` jsonb keyed by component) |

The fundamentals job refreshes stale (> 7 days) core-stock ratios once a day, starting one minute after startup.
