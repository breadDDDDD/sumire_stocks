# Sumire Platform: Implementation Plan

A personal, Bloomberg-Terminal-style market terminal for the **Indonesia Stock Exchange (IHSG / IDX)**, themed after Sumire Yoshizawa (Persona 5 Royal). It is light enough to leave running all day, and an AI layer searches the web and connects news, macro, sector and price data into insights.

**Decisions so far**
| Topic | Decision |
|---|---|
| Market data | Free Yahoo Finance (`.JK` tickers, `^JKSE` for IHSG). Quotes are ~10–15 min delayed; daily history is end-of-day. Accessed through a provider interface so a paid IDX feed can be added later. |
| App form | Local web app: one .NET 10 process serving a React/TypeScript frontend, opened in the browser on `localhost` |
| Footprint | **Lightweight is a requirement:** small memory, near-zero idle CPU, heavy work done once a day, nothing loaded that isn't used (targets in §2.1) |
| Database | Supabase Postgres via `SUPABASE_CONNECTION_STRING` in `.env` (the code reads it; nobody prints it) |
| Forecasting | Quant models (indicators, GARCH volatility, baseline, signal score) plus a **Hugging Face foundation model (Amazon Chronos-2, ONNX)**. Its weights come from a pinned Hugging Face repo, which serves as the free model bucket. The model runs **off your PC by default**: a nightly **GitHub Actions** job (free tier) loads it, forecasts and writes results to Supabase. Running it locally is a fallback (§5.4). |
| AI insights | A **research bridge** (§6): the app queues research jobs, and an agent CLI you are already signed into runs them on your own plan. **Antigravity CLI (`agy`) is the default**, using any model it offers (Gemini, Claude, GPT-OSS). Gemini CLI and Claude Code are switchable runners. Paid APIs (Gemini API / Claude API) are an optional fallback, off by default. Jobs run **on a schedule** (pre-open, close, occasional sweeps) and **on command**, with strict daily caps to stay inside your plan's limits. The AI explains and links events to stocks; it never makes up price numbers. |
| Dev tooling | Built in **Google Antigravity**: the repo ships agent rules so Antigravity's agents follow this plan phase by phase (§10) |
| Mascot/art | Sumire art from online sources (Shujin school uniform, and gym outfit with jacket), used locally for personal use |

> Antigravity has a terminal CLI, **`agy`**, with a headless mode (`agy -p`, `--model`, `--output-format json`, `--json-schema`) and MCP support. The app uses it to run research on your Antigravity plan, both on a schedule and from commands inside Antigravity. These details come from third-party guides and forum posts, not Google's docs, so Phase 6 starts by checking them on your machine.

---

## 1. What the product does

The terminal has four jobs:

1. **See and check stocks:** live (delayed) quotes, charts, company profile, fundamentals, news, and the key stats a trader needs.
2. **Predict individual stocks:** price ranges for the next 5, 20 and 60 trading days, the probability the price is higher, and a signal score that explains its reasons. Each model's past accuracy is shown, so the predictions can be checked.
3. **Forecast the market:** IHSG regime and outlook, market breadth, sector rotation, and macro drivers (USD/IDR, coal, nickel proxies, oil, gold, US yields, regional indexes).
4. **Connect the dots:** search today's news online and link it to stocks through sectors, commodities, conglomerate groups and macro factors. It answers *why* something moved, *what else is affected* and *what to watch next*, with sources.

### Inspiration (what real terminals do and what we take from each)
- **Bloomberg Terminal:** a command line (`TICKER FUNCTION <GO>`), short function codes (DES, GP, FA, N, W, EQS), a dense multi-pane layout, black background with amber text, and keyboard-first use. Bloomberg's AI document summaries are the model for our `INS` screen.
- **TradingView:** clean candlestick charts, an indicator overlay menu, and a "technical rating" gauge (our signal score follows this idea).
- **Stockbit / RTI Business (Indonesian):** what IDX traders look for: lot size, price ticks (fraksi harga), the daily price limits (ARA/ARB), value traded in Rupiah, IDX-IC sectors, and conglomerate group plays.
- **Koyfin:** macro dashboards and sector relative-strength views.

---

## 2. Architecture (one small process)

```
Browser (React SPA)  ──HTTP/JSON──►  Sumire.exe (ASP.NET Core, single project)
        ▲                              │  ├─ Market data (Yahoo) + RSS headlines
        └──SignalR (quotes, insights)──┤  ├─ Analytics (indicators, stats, GARCH, scoring)
                                       │  ├─ Relations map (groups, commodities, macro links)
                                       │  ├─ Research queue + validator (§6)
                                       │  ├─ Background jobs (intraday poll, EOD, research schedule)
                                       │  └─ In-memory cache (IMemoryCache)
                                       ▼                    │ spawns (headless)        ▲ MCP (stdio)
                              Supabase Postgres             ▼                          │
                              (schema `sumire`)   agy -p / gemini -p / claude -p   Antigravity IDE
                                                  (your plan's models + web search)  (/sumire-research)
```

- **One executable, four modes:**
  - `Sumire.exe forecast --once`: runs the foundation model forecasts and exits. Used by the nightly GitHub Action or, as the fallback, on your PC (§5.4).
  - `Sumire.exe` (default): web server plus UI at `http://localhost:5080`
  - `Sumire.exe research --once`: processes due research jobs and exits. Windows Task Scheduler runs this, so research happens even when the UI isn't open.
  - `Sumire.exe mcp`: a stdio MCP server that Antigravity, Gemini CLI or Claude Code can attach to
- **No microservices, message queues, Redis, Hangfire, Docker or local ML models by default.** Jobs are `BackgroundService` classes with `PeriodicTimer`, and caching uses `IMemoryCache`.

### 2.1 Keeping it light
| Lever | How |
|---|---|
| No heavy model on the PC | Chronos-2 runs in GitHub Actions by default, so the UI process never loads ONNX Runtime or model weights; it only reads the forecast rows. The quant models are plain C# math that finishes in milliseconds per stock. Search and reasoning run in the agent CLI on your plan, as a short-lived process that exits after each batch. |
| Smaller tracked universe | Full nightly analytics only for the **core universe**: Kompas100 + LQ45 + IDX80 + anything on a watchlist (~120–150 stocks). Any other IDX stock still works on demand: it is fetched, analyzed and cached when you open it. |
| Less history | 3 years of daily bars (enough for 200-day MAs, a 1-year beta and a GARCH fit), not 5 |
| Polling only when someone is looking | Intraday polling runs only during market hours **and** while a browser tab is connected. It covers only the symbols on screen, the watchlists and the indexes. With no tab open, the process sits idle. |
| Research is cached, capped and batched | Insights are cached (stock: until 6h old or a >3% move; briefs: one per session). Jobs run one at a time in a few scheduled batches, under daily and per-model caps (§6.6), with a counter visible in the status bar. |
| Lean frontend | Panes are lazy-loaded, tables are virtualized, and the chart library is ~45 KB. No UI framework. |
| Self-contained publish | `dotnet publish -c Release` with trimming produces one exe, so no separate runtime install is needed |

**Targets** (checked in Phase 8): idle RAM under ~150 MB, idle CPU ~0%, startup under 3s, EOD pipeline under 2 min for the core universe, frontend bundle under 400 KB gzipped (excluding mascot images).

### 2.2 Tech stack
**Backend (.NET 10, C#)**
- ASP.NET Core Minimal APIs and SignalR
- EF Core with `Npgsql.EntityFrameworkCore.PostgreSQL`
- `DotNetEnv` to load `.env`
- `Microsoft.Extensions.Http.Resilience` for retries, backoff and rate limits on outbound calls
- `Skender.Stock.Indicators` for indicators; `MathNet.Numerics` for statistics and the GARCH optimizer
- **Research bridge:** `System.Diagnostics.Process` to run the agent CLIs headless (no SDK needed); the official C# MCP SDK (`ModelContextProtocol`) for `Sumire.exe mcp`
- **Optional paid fallback:** Google's Gen AI .NET SDK (`Google.GenAI`) for the Gemini API and the official Anthropic C# SDK (`Anthropic`) for the Claude API. Referenced only if you enable a direct API runner.
- `Microsoft.ML.OnnxRuntime` for Chronos-2, loaded only in `forecast --once` mode (never in the UI process)
- xUnit for tests

**Frontend**
- Vite, React and TypeScript
- `lightweight-charts` (TradingView's open-source charting library)
- `@tanstack/react-query`, `@tanstack/react-table` (with virtualization)
- `@microsoft/signalr`
- `d3-hierarchy`, used only for the sector treemap; the relations map is drawn as plain SVG
- Plain CSS with CSS variables (CSS modules)

### 2.3 Repository layout
```
StockChecker/
├─ .env                         (SUPABASE_CONNECTION_STRING, RESEARCH_* settings, optional API keys)
├─ AGENTS.md                    (project rules for Antigravity / any coding agent)
├─ .agents/                     (Antigravity rules, workflows and mcp_config.json, see §10)
├─ research-workspace/          (empty sandbox folder the headless agent runs in; gitignored)
├─ .github/workflows/forecast.yml  (nightly Chronos-2 run on GitHub Actions)
├─ plan.md / implementation-plan.md
├─ SumirePlatform.sln
├─ src/
│  ├─ Sumire.Api/
│  │  ├─ Program.cs
│  │  ├─ Data/            DbContext, entities, migrations
│  │  ├─ Market/          IMarketDataProvider, YahooClient, RssNewsClient, MarketCalendar
│  │  ├─ Analytics/       Indicators, RiskStats, Garch, SignalScore, Breadth, Screener, Correlations
│  │  ├─ Forecasting/     BaselineForecaster, ForecastModelStore (HF download + checksum), ChronosForecaster, ForecastEvaluator
│  │  ├─ Research/        ResearchQueue, FactPacketBuilder, InsightValidator, RelationsMap,
│  │  │                   Runners/ (AgyRunner, GeminiCliRunner, ClaudeCodeRunner, ApiRunner),
│  │  │                   Mcp/ (SumireMcpTools), Prompts/, insight.schema.json
│  │  ├─ Jobs/            IntradayQuoteJob, EodPipelineJob, FundamentalsJob, ResearchScheduler, AlertJob
│  │  ├─ Endpoints/       one file per area
│  │  ├─ Hubs/            QuotesHub
│  │  └─ Seed/            instruments.csv, relations.json, idx-holidays.json, idx-rules.json
│  └─ Sumire.Api.Tests/
└─ web/
   ├─ src/ (app shell, command bar, panes, functions, theme, mascot)
   └─ public/assets/sumire/     (gitignored; mascot images)
```

---

## 3. Data sources and freshness

| Data | Source | Refresh | Stored? |
|---|---|---|---|
| Stock list (code, name, sector, board, index membership) | IDX "Daftar Saham" list, imported into `Seed/instruments.csv` | Manual / monthly | Yes |
| Daily OHLCV, dividends, splits | Yahoo `v8/finance/chart` | EOD job at 16:30 WIB; 3-year backfill for the core universe on first run | Yes |
| Intraday quotes | Yahoo `v7/finance/quote`, batched (cookie+crumb handling) | Every 60s, only in market hours with a tab open | No (memory) |
| Intraday bars | Yahoo chart endpoint | On request, cached 60s | No |
| Fundamentals and key ratios | Yahoo `quoteSummary` | Weekly for the core universe; on request otherwise | Key ratios: yes. Statements: memory cache 24h |
| Macro and global | Yahoo: `IDR=X`, `CL=F`/`BZ=F`, `GC=F`, `^TNX`, `^GSPC`, `^N225`, `^HSI`, `^STI`, `000001.SS`, plus coal/nickel proxies where available | Same as intraday | Daily closes: yes |
| Headlines (free, no AI) | RSS: CNBC Indonesia Market, Kontan, Bisnis, IDX Channel, matched to tickers | Every 15 min while a tab is open | No |
| Deep news and context | **Research bridge** (§6): the agent CLI's own web search, on your plan | Scheduled batches + on command | Only the resulting insight (§4) |
| Relations | `Seed/relations.json` (hand-curated, §6.2) + nightly correlation clusters | Edited by hand / nightly | File + `signal_scores` |
| Market calendar | `idx-holidays.json` + `idx-rules.json` (sessions, tick sizes, ARA/ARB bands, lot) | Yearly, edited by hand | File |

**Staying up to date:** `MarketCalendar` knows WIB sessions (including Friday hours), weekends and IDX holidays. Every panel shows a **"Delayed · as of 14:32:10 WIB"** badge, which turns amber or red when stale. After the close the EOD pipeline runs (§5). If the PC was off when it was due, it catches up on startup.

**Limits of free data:** Yahoo has no order book, broker summary, foreign net buy/sell or IDX disclosure feed. AI search partly covers the gap: it can find news *about* foreign flows and corporate disclosures, but those are reported figures, not a data feed. A paid IDX API can be added later through `IMarketDataProvider`.

---

## 4. Database (Supabase): only what matters

All tables go in a dedicated schema **`sumire`**, not `public`, which Supabase exposes through its auto-generated REST API. There is no users table, because the app is single-user.

| Table | Purpose | Key columns |
|---|---|---|
| `instruments` | Stock and index universe | `code` PK, `yahoo_symbol`, `name`, `sector`, `industry`, `board`, `is_index`, `index_memberships` text[], `group_name` (conglomerate), `core` bool, `active` |
| `daily_bars` | EOD history (3 years) | PK(`code`,`date`), `open`,`high`,`low`,`close`,`adj_close`,`volume` |
| `fundamentals` | Latest key ratios (one row per stock) | `code` PK, `updated_at`, `market_cap`, `pe`, `pb`, `roe`, `der`, `npm`, `eps`, `div_yield`, `revenue_growth`, `earnings_growth` |
| `forecasts` | Daily forecast snapshot (for display and accuracy scoring) | PK(`code`,`as_of`,`horizon`,`model`), `p10`,`p50`,`p90`, `prob_up`, `sigma` |
| `forecast_metrics` | Rolling accuracy per model/horizon | PK(`model`,`horizon`,`scope`), `mape`, `dir_accuracy`, `coverage_80`, `n` |
| `signal_scores` | Latest score per stock (feeds the screener) | `code` PK, `as_of`, `total` + one column per component, including `news` |
| `research_jobs` | The research queue and usage log | `id`, `kind` (stock / brief_open / brief_close / sector / ask), `subject`, `priority`, `packet` jsonb, `status` (pending / running / done / failed / skipped), `runner`, `model`, `attempts`, `error`, `created_at`, `started_at`, `finished_at`, `insight_id`. Packets are cleared once a job is done; rows are kept 30 days for usage counts. |
| `insights` | Validated AI insight results | `id`, `kind`, `subject`, `created_at`, `runner`, `model`, `sentiment` (−1…1), `confidence`, `body` jsonb (summary, drivers, connections, catalysts, risks, linked tickers), `sources` jsonb (title, url, publisher, date) |
| `watchlists` / `watchlist_items` | Your lists | `id`, `name` / (`watchlist_id`,`code`,`sort`) |
| `alerts` | Price, %, indicator and news alerts | `id`, `code`, `type`, `params` jsonb, `active`, `last_triggered_at` |
| `settings` | UI layout, research runners and caps (override `.env`), mascot | `key` PK, `value` jsonb |

**Size check:** ~150 core stocks × 3 years ≈ 110k bar rows, plus on-demand stocks: a few MB to a few dozen MB. Insights are kept for 90 days (stock ones) or 1 year (briefs). Forecasts are pruned after one year once they have been scored.

**Not stored:** ticks, intraday bars, raw news articles, full statements, indicator values (recomputed), and raw AI responses (only the validated, structured result is saved).

**Connection notes:** use Supabase's **pooler** connection string (the direct host is IPv6-only on the free tier). For the transaction pooler (port 6543), add `Max Auto Prepare=0;No Reset On Close=true` in code. The free tier pauses projects after ~7 days of inactivity.

---

## 5. Analytics and forecasting (quant, cheap to run)

Principle: **give a probability range, not a single target price,** and compare every model against a naive baseline.

### 5.1 Indicators and stats
- **Trend:** SMA/EMA 20/50/200, golden and death crosses, ADX(14)
- **Momentum:** RSI(14), MACD(12,26,9), Stochastic(14,3), rate of change
- **Volatility:** Bollinger(20,2), ATR(14)/ATR%, historical volatility 20/60d
- **Volume:** OBV, volume vs. 20-day average, value traded in Rp, intraday VWAP
- **Levels:** 52-week high/low, pivot points, swing highs/lows as support/resistance
- **Risk and return:** returns over 1D/1W/1M/3M/YTD/1Y, beta vs IHSG, Sharpe, max drawdown, correlation with IHSG
- **IDX-specific:** lot value, tick size, tomorrow's ARA/ARB limits (from `idx-rules.json`)

### 5.2 GARCH(1,1) volatility
Fit by maximum likelihood on ~2 years of daily log returns, refit nightly. It gives the expected range (the ±1σ/±2σ cone) for 5/20/60 days and the volatility regime. It is also run on IHSG.

### 5.3 Baseline forecaster (always on)
A random walk with drift plus the GARCH cone. It produces P10/P50/P90 and P(up), and it is the benchmark everything else must beat.

### 5.4 Foundation model: Chronos-2 from Hugging Face, run remotely

**Which model.** **Amazon Chronos-2** (~120M parameters) is the primary model. It is a pre-trained time-series foundation model that returns quantile forecasts in one pass. Unlike earlier Chronos versions, it can forecast related series together and use **covariates**. That lets the quant side "connect the dots" too: a stock is forecast alongside IHSG, its sector index, USD/IDR and its relevant commodity (from `relations.json`). **`chronos-bolt-small`** (smaller and univariate) is kept as the light fallback, configured with `FORECAST_MODEL`.

**Where the weights live (the free "bucket").** The Hugging Face Hub is used as the bucket. It is free, built for model files, and has a CDN and versioning. Supabase Storage doesn't fit: its free tier reportedly caps files at ~50 MB, while the Chronos-2 ONNX files are ~125 MB (INT8) to ~480 MB (FP32).
- Source: the community ONNX export `sktime/chronos2-onnx`. Amazon has no official ONNX release; this export is unofficial, and its author reports it matches the official pipeline closely. `kashif/chronos-2-onnx` (with an INT8 file) is the alternative.
- **Optionally mirror the files into your own private HF model repo** (free), so an upstream change or deletion can't break the app.
- Configured with `FORECAST_MODEL_REPO`, `FORECAST_MODEL_REVISION` (**pinned to a commit hash**, never `main`) and `FORECAST_MODEL_SHA256`. The file is downloaded once, its checksum verified, and then cached (the Actions cache remotely, `%LOCALAPPDATA%\Sumire\models` locally). A mismatched checksum is refused.

**Where it runs.**
| Mode | How | Load on your PC | Cost |
|---|---|---|---|
| **GitHub Actions (default)** | `.github/workflows/forecast.yml`, cron at ~17:00 WIB on trading days (after the EOD bars land) plus a manual "Run workflow" button. Steps: set up .NET, restore the cached model, `dotnet run -- forecast --once`, which reads bars from Supabase, runs ONNX on the ~150 core stocks + IHSG and writes `forecasts`. | None | Free: a private repo gets 2,000 Actions minutes/month, and this job needs ~5–10 min × ~22 trading days. Public repos are unlimited. |
| **Local fallback** | The same `forecast --once` command, run by `ResearchScheduler` or Task Scheduler after the EOD job | A short spike (~0.5–1 GB RAM for 1–3 min), then the process exits and memory is freed | Free |
| Off | `FORECAST_MODE=off` | None | The baseline and GARCH still work |

- **Why not a Hugging Face Space or Inference Endpoint?** Free CPU Spaces now reportedly need a paid plan for Docker/Gradio, sleep after 48h, and have had quota problems. Inference Endpoints cost money. Nightly batch forecasts suit a scheduled job better than an always-on API anyway.
- **Secrets:** the GitHub Action needs `SUPABASE_CONNECTION_STRING` as an **encrypted repository secret** (and `HF_TOKEN` if you mirror into a private HF repo). You add these yourself in GitHub settings; they never go in the repo. The Action only reads `daily_bars` and `instruments` and writes `forecasts`. A dedicated Postgres role with only those grants limits what a leaked secret could do.
- **Freshness check:** the app shows the forecast's `as_of` date. If the nightly forecast is missing by 18:00 WIB (the Action failed or GitHub is down) and `FORECAST_LOCAL_FALLBACK=true`, the app runs it locally once.

**Output:** P10–P90 for 5/20/60 trading days and P(up). These are stored per model in `forecasts`, shown as a cone on `GP` and in `FCST`, and scored against the baseline in the track record. The signal score only uses Chronos-2 if its track record beats the baseline.

### 5.5 Forecast evaluation (walk-forward)
Each night, forecasts whose horizon has passed are scored on **direction hit rate**, **MAPE of P50** and **80% band coverage**, per model and horizon. A one-time historical backtest at launch fills the track record right away.

### 5.6 Signal score (explainable, −100 … +100)
| Component | Inputs | Weight |
|---|---|---|
| Trend | price vs MA20/50/200, MA slopes, ADX | 25% |
| Momentum | RSI, MACD histogram, 3/6-month return relative to IHSG | 20% |
| Volume | OBV slope, volume surge with price direction | 10% |
| Volatility | GARCH vol percentile (high vol counts against) | 10% |
| Valuation | PE/PB vs sector median, dividend yield | 15% |
| Forecast | Chronos-2 P(up) and P50 return if it beats the baseline, otherwise the baseline; weighted by track record | 10% |
| **News & catalysts** | AI insight sentiment × confidence, decaying over ~5 days | 10% |

Labels run **Strongly Bearish → Strongly Bullish**, with a one-line reason per component. Components with missing data are skipped and the weights renormalized. The news component's own predictive value is tracked (§6.5), and its weight drops automatically if it has no edge.

### 5.7 Market forecast (IHSG level)
- **Regime:** IHSG vs its 50/200-day MA, breadth and GARCH vol percentile → *Risk-on / Neutral / Risk-off*
- **Breadth:** advancers/decliners, % above the 50/200-day MA, new highs vs lows
- **Sector rotation:** our own cap-weighted sector indexes, relative strength vs IHSG, and a *Leading / Weakening / Lagging / Improving* quadrant
- **IHSG outlook:** baseline/GARCH cone, track record, **plus the AI brief's narrative** (§6.4)
- **Macro drivers:** USD/IDR, oil, gold, coal/nickel proxies, US 10Y, regional indexes, plus 60-day correlation of each with IHSG

### 5.8 EOD pipeline (16:30 WIB)
Daily bars → indicators and stats → GARCH and baseline → breadth, sectors and correlation clusters → forecast evaluation → signal scores → closing brief (AI, §6.4) → alert checks. Expected runtime is under 2 minutes locally. Chronos-2 runs separately in GitHub Actions at ~17:00. The signal scores are refreshed once its rows land (or at the next app start), so the score never waits on the Action.

---

## 6. AI insights: connecting the dots

### 6.1 Division of labour
The **quant engine produces the facts and numbers**. The **AI searches the web, links events to those facts, and explains them**. The AI never produces a price target or a forecast number, and any number it states must come from the fact packet or from a cited source. This is how professional desks use LLMs today: for research and synthesis, not as a price oracle.

### 6.2 The relations map (the "dots")
`Seed/relations.json` is hand-curated and small, but it is what makes the links meaningful for IDX:
- **Conglomerate groups:** e.g. Djarum (BBCA, TOWR), Salim (INDF, ICBP), Astra (ASII, UNTR, AALI), Barito/Prajogo (BRPT, TPIA, BREN, CUAN), Sinar Mas (SMMA, BSDE, DSSA), and others. IDX stocks often move as groups.
- **Commodity exposure:** coal → ADRO/AADI, PTBA, ITMG, HRUM…; nickel → INCO, ANTM, MBMA, NCKL…; CPO → AALI, LSIP, DSNG…; gold → ANTM, MDKA, BRMS; oil → MEDC, ENRG.
- **Macro sensitivities:** BI rate → banks and property; USD/IDR → importers (pharma, autos) vs exporters (coal, CPO); China demand → coal and nickel; US yields → foreign flows into big banks.
- **Supply chain:** e.g. nickel ore → smelters → EV battery names.

On top of the hand-curated map, the nightly job adds **statistical links**: 60-day return correlation clusters, and which stocks consistently move after a given commodity or index (a simple lead-lag check). Each link shows whether it comes from the curated map or from statistics, so the source is clear.

### 6.3 How one insight is made
1. **Fact packet (local, free):** `FactPacketBuilder` gathers compact JSON: price action, key indicators, signal score breakdown, GARCH range, sector and IHSG moves, related tickers' moves (from the relations map), relevant macro moves, unusual volume, upcoming dividend/ex-dates, and recent RSS headlines. The packet stays around 2–4k tokens.
2. **Queue:** the packet is saved as a `research_jobs` row with a priority.
3. **Research (runner):** the runner gives the agent CLI the packet and a fixed research prompt. The agent searches the web in Indonesian and English (company news, IDX disclosures, OJK/BI announcements, commodity news, analyst commentary) with whatever model the runner selects.
4. **Structured output:** JSON matching `insight.schema.json`, enforced with `agy --json-schema` (or the matching option of other runners):
   - `summary`: 2–3 sentences in plain language
   - `drivers[]`: claim, evidence, linked tickers/factors, source ids
   - `connections[]`: e.g. "Coal benchmark +6% this week → ADRO, PTBA, ITMG up 3–5%; BUMI lagging (debt news)"
   - `catalysts[]`: upcoming events with dates
   - `risks[]`
   - `sentiment` (−1…1), `confidence` (low/med/high), `what_would_change_this_view`
   - `sources[]`: title, url, publisher, published date
5. **Validation (local):** reject or flag the result if a driver has no source, a number contradicts the packet, a source is older than the configured window, or a linked ticker doesn't exist. Failed checks trigger one retry, then the insight is shown with a warning badge.
6. **Store and show:** the validated JSON goes into `insights`. If the UI is open, it is pushed over SignalR and rendered in `INS` with clickable sources.

### 6.4 Where insights appear
| Function | What it is | When it runs |
|---|---|---|
| `BBCA INS` | Stock insight: why it moved, connections, catalysts, risks, sources | Shows the cached insight right away. `INS!` queues a fresh one at high priority, which arrives in roughly 1–2 minutes. |
| `BRIEF` | **Morning brief** (07:45 WIB): overnight US/Asia moves, commodities, what to watch on IHSG today. **Closing recap** (16:45): why IHSG moved, sector drivers, notable watchlist moves | Scheduled (§6.6), even when the UI is closed |
| `SECT INS` | Sector narrative: why a sector is leading or lagging | Included in the closing batch for the top/bottom 2 sectors; otherwise on command |
| `REL` | Relations map for a ticker: groups, commodities, macro links and correlated names, plus today's moves along each link | Local only, no AI cost |
| `ASK` | Free-form question with the current context attached, e.g. `ASK why are nickel stocks down today?` | On command, counts against the cap |
| `RQ` | Research queue: pending/done jobs, runner and model used, today's usage vs caps, a pause button | Local |
| Mascot | Sumire delivers the brief headlines and news alerts in her speech bubble | Follows the briefs and alerts |

**News alerts:** the alert system can watch for "new high-confidence negative insight on a watchlist stock" and notify you.

### 6.5 Making sure it helps
- **Track news sentiment:** sentiment is stored with each insight. The evaluator checks whether sentiment predicted the next 5-day relative return (hit rate and rank correlation) and shows the result in `FCST`. The signal score lowers the news weight if there is no edge.
- **Model comparison:** every insight records its runner and model, so the `RQ` screen can show sentiment hit rate per model. That tells you which of your plan's models is worth its quota.

### 6.6 Research bridge: runners, schedule and plan limits

**Runners (switchable; a priority list, so the next one is tried when one is out of quota)**
| Runner | How it is called | Uses | Notes |
|---|---|---|---|
| `agy` **(default)** | `agy -p --model <slug> --output-format json --json-schema insight.schema.json`, with the prompt and packet on stdin, run inside `research-workspace/` | Your **Antigravity plan**: any model `agy models` lists (Gemini, Claude, GPT-OSS…) | Antigravity shows two quota pools (Gemini vs Claude/GPT), so the model list can alternate between them |
| `gemini-cli` | `gemini -p … --output-format json` | Your Google account / Google AI plan | Same Google login, separate CLI |
| `claude-code` | `claude -p … --output-format json --json-schema …` | Your **Claude plan** | Claude Code headless mode |
| `gemini-api` / `claude-api` | Official SDKs | Pay-per-use API keys | Off by default; the only option that works without a CLI login |

Configured as an ordered list of `runner:model` entries, for example:
```
RESEARCH_RUNNERS=agy:gemini-3.5-flash, agy:claude-sonnet-4-6, claude-code:sonnet
```
(Model slugs are placeholders until checked with `agy models` in Phase 6.) Each runner is a small class that builds the command line and parses stdout. Adding another CLI later is one file.

**When research runs**
| Batch | Time (WIB, trading days) | Jobs |
|---|---|---|
| Morning | 07:45 | Morning brief, plus insights for watchlist stocks with big overnight news or ex-dates today |
| Midday (optional, off by default) | 12:05, during the lunch break | Watchlist stocks moving >3% |
| Close | 16:45, after the EOD pipeline | Closing recap, top/bottom 2 sectors, stale watchlist insights, the day's biggest core movers (up to the cap) |
| Weekend | Sunday 19:00 | Week-ahead brief (macro calendar, BI/Fed events, earnings, dividends) |
| On command | Anytime | `INS!`, `ASK`, `SECT INS`, or `/sumire-research` inside Antigravity |

- **Two ways the schedule runs:** while the UI is running, `ResearchScheduler` (a `BackgroundService`) runs the batches. When it isn't, a **Windows Task Scheduler** entry (created by `Sumire.exe install-schedule`) calls `Sumire.exe research --once` at the same times, which processes due jobs and exits. A lock row in `research_jobs` stops both from running the same batch.
- **Missed batches:** if the PC was off, only the latest missed brief is run on the next start. Old briefs are not back-filled, which saves quota.
- No batches run on IDX holidays, apart from the weekend brief.

**Staying inside your plan's limits**
| Guard | Default | Behaviour |
|---|---|---|
| `RESEARCH_DAILY_CAP` | 20 jobs/day | A hard stop; the next day's batches start fresh |
| `RESEARCH_MODEL_CAPS` | e.g. `claude-*:6` | Per-model daily caps, so one quota pool isn't drained |
| `RESEARCH_BATCH_CAP` | 8 jobs/batch | Briefs are always first in the batch |
| Concurrency | 1 | Jobs run one after another, never in parallel |
| Priority | brief > on-command > watchlist > sectors > movers | Low-priority jobs are skipped first when near the cap |
| Dedupe | by subject + cache window | No re-research of a stock with a fresh insight unless forced with `INS!` |
| Quota detection | exit code / error text matching ("quota", "rate limit", "429", "credits") | Marks that `runner:model` exhausted until the next day (or the reset time if the CLI reports one) and moves to the next entry in `RESEARCH_RUNNERS`. If all are exhausted, the queue pauses and the status bar shows it. |
| Timeout | 4 min per job | The process is killed and the job retried once later |
| Pause | `RQ` screen / `RESEARCH_ENABLED=false` | Stops everything immediately |

The status bar shows, for example, `Research 9/20 · agy · gemini-3.5-flash`. The CLIs don't expose remaining plan quota in a form the app can read reliably (in `agy` it is the interactive `/usage` command), so the app counts its own usage and you set the caps to fit your plan.

**Commands from inside Antigravity (MCP)**
`Sumire.exe mcp` is registered in `.agents/mcp_config.json`, so the Antigravity agent, Gemini CLI or Claude Code can use these tools:
- `list_pending_research`, `get_research_job(id)`: the packet, instructions and output schema
- `get_stock_snapshot(code)`, `get_relations(code)`, `get_market_overview()`: to dig deeper while researching
- `submit_insight(job_id, json)`: runs `InsightValidator` and returns any errors so the agent can fix and resubmit
- `queue_research(kind, subject)`: e.g. "research nickel names"

The Antigravity workflow **`/sumire-research`** tells the agent to clear the pending queue with these tools, using whatever model you have selected in the IDE. This is the manual, interactive way to spend quota you choose to spend.

**Safety:** headless runs happen in an empty `research-workspace/` folder. Permissions are pre-approved only for web search and the Sumire MCP tools (never `--dangerously-skip-permissions`), so the agent can't edit files or run shell commands. Headlines in the packet are marked as untrusted text, and the validator checks every result before it is stored.

**Terms of use:** whether driving `agy` from a script is allowed on a personal plan is not settled. A community answer on Google's forum says yes, at the same limits, but Google hasn't confirmed it. Low-volume personal use with a schedule is the most conservative pattern. Check Antigravity's terms; if they forbid it, switch the default to `/sumire-research` (manual) or an API runner.

**Without any runner** (not logged in, all caps reached, or disabled), the platform still works fully on quant and RSS. `INS` shows the last cached insight with its age.

---

## 7. UI / UX

### 7.1 Layout
```
┌────────────────────────────────────────────────────────────────────────┐
│ SUMIRE ▸ [ command line: BBCA INS_                    ] 14:32 WIB ● LIVE  RQ 9/20 │
├──────────────┬──────────────────────────────┬──────────────────────────┤
│ Watchlist    │  Pane 1 (BBCA GP chart)       │ Pane 2 (BBCA INS insight)│
│ (live)       ├──────────────────────────────┼──────────────────────────┤
│              │  Pane 3 (MKT / BRIEF)         │ Pane 4 (REL map / N)     │
├──────────────┴──────────────────────────────┴───────────────┬──────────┤
│ IHSG 7,312 +0.42% │ LQ45 │ USD/IDR │ Coal │ Oil │ Gold │ tape │ [Sumire] │
└──────────────────────────────────────────────────────────────┴──────────┘
```
Layouts can have 1, 2 or 4 panes, and the layout is saved to `settings`. Everything is reachable from the keyboard.

### 7.2 Command line
Syntax: `[TICKER] [FUNCTION]`, then Enter, with autocomplete for both.

| Code | Function | Example |
|---|---|---|
| `MKT` | Market overview: IHSG, breadth, movers, regime, macro | `MKT` |
| `BRIEF` | Today's morning brief / closing recap | `BRIEF` |
| `DES` | Company description and key stats (the default for a bare ticker) | `BBCA` |
| `GP` | Price chart with indicators and forecast cone | `BBRI GP` |
| `FA` | Fundamentals and valuation vs sector | `TLKM FA` |
| `FCST` | Forecast ranges, signal score breakdown, track record | `ASII FCST` |
| `INS` / `INS!` | AI insight with connections and sources (cached / queue a fresh one) | `ADRO INS!` |
| `REL` | Relations map | `BREN REL` |
| `ASK` | Ask a question with the current context (queued, answer pushed when ready) | `ASK why did banks drop?` |
| `RQ` | Research queue, usage vs caps, runner/model status, pause | `RQ` |
| `N` | Headlines (one ticker or the whole market) | `BMRI N` |
| `W` | Watchlists | `W` |
| `EQS` | Screener with presets | `EQS` |
| `SECT` | Sector heatmap and rotation (`SECT INS` for the narrative) | `SECT` |
| `COMP` | Compare up to 4 tickers | `COMP BBCA BBRI BMRI` |
| `ALRT` | Alerts | `ALRT` |
| `HELP` / `?` | Cheat sheet | `?` |

### 7.3 Keyboard shortcuts
| Key | Action |
|---|---|
| `/` or `Ctrl+K` | Focus the command line (typing anywhere also starts a command) |
| `Esc` | Close, or go back |
| `Alt+1…4` / `Tab` | Focus a pane / cycle panes |
| `Alt+←` / `Alt+→` | Pane history back/forward |
| `↑↓` or `j/k`, `Enter` | Move through rows, open the ticker |
| Chart: `1`–`6`, `i`, `f`, `v` | Timeframes, indicator menu, forecast cone, volume |
| `n` | Open `INS` for the current ticker |
| `r` | Open `REL` for the current ticker |
| `w` | Add the current ticker to a watchlist |
| `m` | Hide or show the mascot |
| `?` | Shortcut overlay |

### 7.4 Visual style: Bloomberg × Sumire
- **Base:** near-black `#0B0B0F` background, amber `#FFB000` text, white values, monospace tabular numbers (JetBrains Mono / IBM Plex Mono).
- **Sumire accents:** crimson `#C8102E` (her hair and the Shujin uniform) for branding, the command line and active borders. Persona 5 style touches: slanted panel headers, a bold condensed heading font (Anton or Bebas Neue as a P5-like stand-in), light halftone in empty states. Small motifs: a ribbon on the active tab, round glasses as the loading spinner, and her codename **"Violet"** for alerts ("Violet alert: BBCA crossed MA200").
- **Up/down:** green `#22C55E` and red-pink `#F43F5E`, kept separate from the crimson brand color.
- **Insight cards:** each driver shows small source chips (publisher and date). Connections are drawn as `factor → ticker` rows with live % moves, so the chain is visible at a glance.

### 7.5 Mascot: Sumire
- A small bust-up portrait (~120px) in the bottom-right corner with a speech bubble.
- **Outfits:** Shujin uniform, or gym outfit with jacket (toggle, or automatic: gym outfit during market hours).
- **Expressions follow market state:** neutral (flat), smiling (IHSG > +0.5% or a target hit), worried (IHSG < −0.5% or a negative news alert), determined (pipeline or AI running: "Researching today's news…"), surprised (a >3% move on a watchlist stock).
- **Speech bubble:** brief headlines, alerts, and shortcut tips. `m` hides it.
- **Asset sourcing (Phase 8):** I find candidate images (P5R in-game bust-up portraits in uniform and gym outfit, official Atlus art, the Megami Tensei Wiki, The Spriters Resource) and you pick. They go in the **gitignored** `web/public/assets/sumire/`, for personal use only. The app works with a styled placeholder if no images are present.

---

## 8. API surface
```
GET  /api/instruments?q=bbc
GET  /api/quotes?codes=BBCA,BBRI
GET  /api/chart/{code}?range=1y&interval=1d
GET  /api/stocks/{code}/profile | /fundamentals | /technicals | /forecast
GET  /api/stocks/{code}/relations          → relations map + today's moves (local)
GET  /api/stocks/{code}/insight           → latest cached insight
POST /api/research                         → { kind, subject, question? } → queued job id
GET  /api/research                         → queue, today's usage vs caps, runner status
POST /api/research/pause | /resume
GET  /api/market/overview | /sectors
GET  /api/briefs/latest?kind=open|close|week
GET  /api/news?code=BBCA                   → RSS headlines
POST /api/screener
CRUD /api/watchlists, /api/alerts, /api/settings
GET  /api/status                           → data freshness, research usage, job status
WS   /hubs/quotes                          → quote updates + "insight ready" events
```
CLI verbs on the same exe: `forecast --once`, `research --once`, `install-schedule` / `uninstall-schedule`, `mcp`.
The server binds to `localhost` only. API keys never reach the browser.

---

## 9. Build phases

Each phase ends with something working that you can run and try.

### Phase 0: Foundation
- Solution, API, tests project, Vite app; `.env` loading; EF Core on the `sumire` schema; `/health` (DB check, without printing secrets)
- App shell: theme, pane grid, command line with routing, shortcut overlay
- `AGENTS.md` and Antigravity rules (§10)
- **Done when:** `dotnet run` opens the themed shell and `/health` reports the DB as OK.

### Phase 1: Market data pipeline
- `YahooClient` (chart, batched quotes with crumb, resilience, throttle), instrument import, `MarketCalendar`, `idx-rules.json`
- EOD job (3-year backfill for the core universe), on-demand fetch for other stocks, intraday job that runs only while a tab is connected, `QuotesHub`
- **Done when:** core stocks have 3 years of bars, quotes update live during market hours, and idle CPU is ~0% with no tab open.

### Phase 2: Core terminal screens
- `DES`, `GP`, `W`, `MKT`, ticker tape, autocomplete, pane history, saved layout
- **Done when:** `BBCA GP` works, `1`–`6` switch timeframes, `w` adds to a watchlist, and `MKT` updates live.

### Phase 3: Analytics
- Indicators, risk stats, levels, IDX limits; breadth, sector indexes, rotation → `SECT`; fundamentals job → `FA`; `EQS` with presets (oversold in uptrend, 52-week-high breakout, golden cross, value, dividend, volume surge, top score)
- **Done when:** the screener answers in under 1s and `SECT` shows today's heatmap.

### Phase 4: Forecasting
- GARCH, baseline, walk-forward evaluator with the historical backtest, signal score (without the news component yet), `FCST`, IHSG regime and outlook in `MKT`
- **Done when:** `ASII FCST` shows ranges, a score with reasons, and a track record.

### Phase 5: Relations map
- `relations.json` (groups, commodities, macro, supply chain), nightly correlation clusters and the lead-lag check, `REL` screen
- **Done when:** `ADRO REL` shows the coal link, group peers and correlated names with today's moves.

### Phase 6: Research bridge and AI insights
- **6a. `agy` check (short, first):** on your machine, confirm `agy -p` headless runs on your plan, the `--model` / `--output-format json` / `--json-schema` flags, that web search works headlessly, and the error text for quota exhaustion; list `agy models`. Check the same for `claude -p` if you want Claude Code as a runner. Write down the real flags and slugs in `AGENTS.md`.
- **6b. Queue and runners:** `research_jobs`, `FactPacketBuilder`, prompts, `insight.schema.json`, `InsightValidator`, `AgyRunner` (default), `ClaudeCodeRunner`, `GeminiCliRunner`, runner fallback list, caps, quota detection
- **6c. Schedule:** `ResearchScheduler` batches, `research --once`, `install-schedule` (Windows Task Scheduler), lock row, missed-batch rule
- **6d. Screens:** `INS`/`INS!`, `BRIEF`, `SECT INS`, `ASK`, `RQ`; "insight ready" SignalR push; news component in the signal score; sentiment and per-model evaluation
- **6e. MCP + Antigravity command:** `Sumire.exe mcp` tools, `.agents/mcp_config.json`, `/sumire-research` workflow
- **Done when:** the 16:45 batch runs with the UI closed (via Task Scheduler) and `BRIEF` shows the recap the next time you open it; `ADRO INS!` returns a sourced insight linking coal prices to the move; hitting a cap or quota error moves to the next runner/model, then pauses cleanly; `/sumire-research` in Antigravity clears the queue; the app works with research disabled.
- *Optional:* `ApiRunner` for the Gemini/Claude APIs, only if you want a pay-per-use fallback.

### Phase 7: Headlines, alerts and compare
- RSS headlines → `N`; `ALRT` (price, %, MA cross, RSI, 52-week high, negative-news alerts); `COMP`
- **Done when:** a watchlist alert fires in market hours and `N` shows relevant headlines.

### Phase 8: Sumire, polish and footprint check
- Mascot art selection with you, expression and outfit states, speech bubble with briefs
- Full P5 styling pass, loading and error states, README
- **Measure the §2.1 targets** (RAM, CPU, startup, bundle size, EOD time) and fix anything over budget; set up the single-file publish
- **Done when:** the mascot reacts to the market, every screen is reachable by keyboard, and the footprint targets are met.

### Phase 4b: Chronos-2 from Hugging Face (runs right after Phase 4)
- **Check first (short):** download the pinned `sktime/chronos2-onnx` revision, inspect its inputs and outputs (context length, how grouped series and covariates are passed, quantile outputs), and run it from C# with ONNX Runtime on a few stocks. Compare against the published model card examples, and fall back to `chronos-bolt-small` if the Chronos-2 export doesn't behave.
- `ForecastModelStore` (HF download, checksum, cache), `ChronosForecaster` (input building with covariates from `relations.json`, quantile mapping), `forecast --once`
- `.github/workflows/forecast.yml` (cron + manual run, model cache), restricted Postgres role, local fallback, freshness badge
- **Done when:** the Action runs nightly in under 10 minutes, `ASII FCST` shows Chronos-2 next to the baseline with its own track record, and the UI process's memory is unchanged.
- Covariates start with IHSG and USD/IDR only. Sector and commodity covariates are added once `relations.json` exists (Phase 5).
- The signal score uses Chronos-2 only once its track record beats the baseline (a backtest at launch, then live).

---

## 10. Building with Antigravity
Antigravity's agents (Gemini-powered) do the implementation, so the repo carries the context they need:
- **`AGENTS.md`** (root): project summary, tech stack, the "do not overengineer" rules, "never read or print `.env` values", folder conventions, how to run and test, and a pointer to this plan.
- **Antigravity rules** in `.agents/rules/`: coding conventions (C# minimal APIs, one file per endpoint area, no extra layers), the frontend style tokens, and the commit message style. Workflows in `.agents/workflows/`: one per phase ("implement Phase N: read §9, build, run tests, check the Done-when list"), plus the runtime workflow **`/sumire-research`** (§6.6). `.agents/mcp_config.json` registers `Sumire.exe mcp`. The exact folder and file names will be checked against the current Antigravity docs in Phase 0.
- **The same `agy` login does two jobs:** it builds the app in the IDE and runs the app's scheduled research, so both use your one Antigravity plan. The caps in §6.6 exist so research doesn't eat the quota you need for coding.
- **Task-sized phases:** each phase above is small enough for one agent session, with a concrete "Done when" the agent can verify with tests and the browser.
- I can keep working on it in Claude Code too, since `AGENTS.md` is a shared convention.

---

## 11. Testing
- **Unit (xUnit):** tick size and ARA/ARB, market calendar, GARCH on synthetic data, signal score normalization, forecast metrics, risk stats, relations lookups, **`InsightValidator`** (missing sources, numbers that contradict the packet, unknown tickers, stale sources), fact packet size.
- **Forecasting:** input building (context window, covariate alignment on trading days, missing-value masking), quantile → P10/P50/P90/P(up) mapping, checksum refusal, and a golden test running the cached ONNX model on a saved series (skipped when the model isn't downloaded).
- **Research bridge:** each runner's command building and output parsing against recorded CLI outputs (success, malformed JSON, quota error, timeout), with no real CLI or quota used; scheduler rules (caps, priority, dedupe, fallback order, missed batches, holiday skip) tested with a fake clock; MCP tools tested in-process.
- **Integration:** Yahoo client against recorded JSON fixtures; a DB smoke test behind a flag.
- **AI spot-check set:** ~10 saved fact packets with known recent events. Run them manually after prompt changes and compare outputs, without automatic scoring, to keep it simple.
- **Manual:** the "Done when" list for each phase.

---

## 12. Risks and mitigations
| Risk | Mitigation |
|---|---|
| Yahoo is unofficial and can break | Provider interface, retry/backoff, fixtures, stale-data badges |
| Yahoo fundamentals for IDX are patchy | Show "n/a", and skip the valuation component when data is missing |
| The AI states wrong facts or makes up links | Numbers only from the packet or sources, a validator, required sources, confidence labels, a visible "AI-generated" tag |
| Research uses up your plan quota (including what you need for coding) | Daily, per-batch and per-model caps; one job at a time; dedupe and caching; quota-error fallback, then pause; a pause button in `RQ` |
| `agy` headless flags or behaviour change (the CLI is new and moving fast) | Runners are one small class each; recorded-output tests catch parsing breaks; Claude Code / Gemini CLI as fallbacks |
| Terms of use for scripted `agy` on a personal plan are unclear | Low-volume scheduled use; check the terms in 6a; the manual `/sumire-research` command and API runners remain as alternatives |
| Prompt injection via news headlines | Headless agent restricted to web search + Sumire MCP, empty workspace, untrusted-text marking, validator on every result |
| News sentiment adds noise to the score | Small weight (10%), measured predictive value, and the weight drops if there is no edge |
| Forecasts get over-trusted | Ranges not targets, track record vs baseline, "not financial advice" label |
| IDX changes rules | Kept in `idx-rules.json` |
| `relations.json` goes stale (group ownership changes) | Small hand-edited file, with each entry dated; statistical links catch new relationships |
| Supabase free tier pauses / size limits | 3-year history, pruning, catch-up job |
| Mascot art copyright | Gitignored, personal use only |
| The community Chronos-2 ONNX export is unofficial and could change or vanish | Pinned commit + SHA-256, an optional mirror in your own HF repo, `chronos-bolt-small` fallback, and the baseline always available |
| Chronos-2 doesn't beat the baseline on IDX stocks | The track record decides: it stays out of the signal score until it earns a place |
| GitHub Actions minutes, outages, or a leaked secret | ~10 min/night fits the free tier; the local fallback runs if the forecast is missing by 18:00; a restricted Postgres role for the Action's secret |
| A malicious or tampered model file | ONNX files only (no pickle), a checksum check, a pinned revision |

## 13. Out of scope for now
- Portfolio and P&L tracking, trade journal
- Order book, broker summary, foreign flow data feed (needs a paid IDX API)
- Markets outside IDX
- Mobile layout; deploying and hosting with login
