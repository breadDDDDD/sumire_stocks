# Sumire Platform

<img src="docs/media/demo.gif" alt="Sumire" width="360">

[Original video (mp4)](m2-res_270p.mp4)

A personal, Bloomberg-style terminal for the Indonesia Stock Exchange (IHSG / IDX), themed after Sumire "Violet" Yoshizawa from Persona 5 Royal. One small .NET 9 process serves a React UI on **http://localhost:5080**. It reads free Yahoo Finance data and RSS headlines, and runs quant analytics (technicals, risk, GARCH, signal score, screener, relations). It also shows forecasts (a statistical baseline plus Chronos-2 via GitHub Actions) and queues AI research through agent CLIs you are already signed into (Antigravity `agy`, Gemini CLI, Claude Code). AI insights explain moves and link them to stocks; they never make up prices.

The plan behind it is [implementation-plan.md](implementation-plan.md). Coding rules for agents are in [AGENTS.md](AGENTS.md).

## Setup

Requirements: Windows, .NET 9 SDK, Node 22 / npm 10, a free Supabase project.

1. **Environment.** Copy `.env.example` to `.env` (repo root) and fill it in. Never commit `.env`.
2. **Database.** Set `SUPABASE_CONNECTION_STRING` to the **pooler** connection string: Supabase dashboard → Connect → *Session pooler* (or *Transaction pooler*). It looks like `postgresql://postgres.<ref>:<password>@aws-0-<region>.pooler.supabase.com:5432/postgres`. The app creates schema `sumire` and runs migrations on startup.
3. **Web UI.** Build it once, and again after UI changes. Run npm from inside `web\`, because `npm --prefix web install` doesn't work on Windows (it looks for `package.json` in the repo root). The build outputs to `src/Sumire.Api/wwwroot`.
   ```powershell
   cd web
   npm install
   npm run build
   cd ..
   ```
4. **Run.** From the repo root:
   ```powershell
   dotnet run --project src/Sumire.Api
   ```
   Open http://localhost:5080. Press **Ctrl+C** to stop it; nothing keeps running after that. On the first start it creates the tables and backfills 3 years of prices, which takes a few minutes.
   For UI work, also run `npm run dev` inside `web\` and open http://localhost:5173 (it proxies `/api` and `/hubs` to :5080).

The app starts and the market screens work even when the database is unreachable (Yahoo fallback). `GET /api/health` says why the DB is down, and the status bar shows **DB down**.

### Troubleshooting: "No such host" / "The requested name is valid, but no data of the requested type was found"

The connection string points at the **direct** host `db.<ref>.supabase.co:5432`. On the free tier that host is **IPv6-only**, and most home networks and Windows setups can't reach it. Switch to the Session or Transaction **pooler** string (`aws-0-<region>.pooler.supabase.com`, user `postgres.<ref>`) from the Connect dialog. Restart the app; `/api/health` should then report `"db": "ok"`. Don't paste the string into issues or logs.

Other symptoms:

- Research screens (INS, BRIEF, RQ, ASK), watchlists, alerts and settings return **503**: the DB is unreachable. Fix the connection string.
- Startup fails binding port 5080: another Sumire (or `dotnet run`) is already running. Close it first.
- A blank page, or a startup warning `The WebRootPath was not found`: the UI isn't built yet. Run `npm install` and `npm run build` inside `web\`, then restart the app.
- A `fail: … __ef_migrations_history` line on the very first start is harmless: EF Core checks for its history table before creating it.

## Using it

Type a ticker and/or a function into the command line (`/` or `Ctrl+K`, or just start typing): `BBCA`, `BBRI GP`, `ADRO INS!`, `COMP BBCA BBRI BMRI`, `ASK why did banks drop?`. A function typed alone uses the ticker in the focused pane.

| Code | Function | Example |
|---|---|---|
| MKT | Market: IHSG, breadth, movers, regime, macro | `MKT` |
| BRIEF | Morning brief / closing recap / week ahead | `BRIEF close` |
| DES | Company profile and key stats | `BBCA` |
| GP | Price chart with indicators and forecast cone | `BBRI GP` |
| FA | Fundamentals and valuation vs sector | `TLKM FA` |
| FCST | Forecast ranges, signal score, track record | `ASII FCST` |
| INS | AI insight with connections; `INS!` forces a fresh one; `INS sector Energy` | `ADRO INS!` |
| REL | Relations map: groups, commodities, macro, supply chain | `BREN REL` |
| ASK | Ask a question with market context | `ASK why did banks drop?` |
| RQ | Research queue, usage vs caps, runner status | `RQ` |
| N | Headlines for a ticker or the whole market | `BMRI N` |
| W | Watchlists | `W` |
| EQS | Equity screener with presets | `EQS` |
| SECT | Sector heatmap and rotation; `SECT INS` for a sector narrative | `SECT` |
| COMP | Compare up to 4 tickers | `COMP BBCA BBRI BMRI` |
| ALRT | Price and signal alerts | `ALRT` |
| HELP | Cheat sheet | `?` |

### Keyboard

| Keys | Action |
|---|---|
| `/`, `Ctrl+K`, or any letter | Command line (`Tab` accepts a suggestion, `↑`/`↓` move, `Esc` clears) |
| `Alt+1…4` | Focus a pane |
| `Tab` / `Shift+Tab` | Cycle panes |
| `Alt+L` | Layout: 1 / 2 / 4 panes |
| `Alt+←` / `Alt+→`, `Esc` | Pane history back / forward; `Esc` also closes overlays |
| `↑` `↓` `j` `k`, `Enter` | Move through rows, open the ticker |
| `1–6`, `i`, `f`, `v` | Chart: timeframes, indicator menu, forecast cone, volume |
| `n`, `r`, `w` | Current ticker: insight, relations, add to watchlist |
| `m` | Hide / show Sumire |
| `?` | Cheat sheet |

The layout and each pane's history are saved in the browser and synced to `/api/settings/ui-layout` (when the DB is up), so they follow you between browsers.

The status bar (bottom left) shows data freshness ("Delayed · as of 14:32 WIB", red once stale), DB and live-connection problems, and research usage (`RQ 9/20 · agy · gemini-3.8-flash-medium`, click for RQ).

## Free-data limits

- Yahoo Finance quotes are **~15 minutes delayed**. Intraday polling runs only during IDX sessions and only while a tab is open, and only for what's on screen, the watchlists and the indexes. Daily bars land after the close (EOD pipeline ~16:30 WIB; it catches up on startup if the PC was off).
- Fundamentals come from Yahoo and are refreshed at most weekly. Gaps are common for small caps.
- No order book, broker summary or foreign-flow data (not free). Headlines come from public RSS (CNBC Indonesia, Kontan, Bisnis, IDX Channel).
- IDX holidays and ARA/ARB rules live in `src/Sumire.Api/Seed/idx-*.json`. Entries marked `"verified": false` should be checked against IDX announcements.

## Research bridge

Details: [docs/research.md](docs/research.md), [docs/mcp.md](docs/mcp.md).

1. Sign in to the agent CLI once: `agy` (Antigravity, the default), and optionally `claude` (Claude Code) or `gemini`. `agy models` lists the models you can use.
2. Choose runners and caps in `.env`: `RESEARCH_RUNNERS` (fallback order, e.g. `agy:gemini-3.8-flash-medium,agy:claude-sonnet-4-6,claude-code:sonnet`), `RESEARCH_DAILY_CAP` (20), `RESEARCH_BATCH_CAP` (8), `RESEARCH_MODEL_CAPS` (e.g. `claude-*:6`). When a quota error hits, the next runner/model takes over; when all are exhausted, research pauses cleanly. RQ shows all of this, with a pause button.
3. Schedule with the UI closed: `Sumire install-schedule` (or `dotnet run --project src/Sumire.Api -- install-schedule`). This registers the morning brief (07:45 WIB), the post-close catch-up (16:35), the closing recap (16:45) and the Sunday week-ahead (19:00) in Task Scheduler. `uninstall-schedule` removes them. With the app open, the built-in scheduler runs the same batches.
4. From inside Antigravity / Gemini CLI / Claude Code: register `Sumire mcp` (see [docs/mcp.md](docs/mcp.md)) and run the `/sumire-research` workflow to clear the queue with the model you have selected.

Headless runs happen in an empty `research-workspace/` folder with only web search allowed. Headlines are marked as untrusted, and a validator checks sources, tickers and numbers before anything is stored.

## Forecasting

Details: [docs/forecasting.md](docs/forecasting.md). The baseline forecast is computed locally after each EOD run. Chronos-2 runs nightly on GitHub Actions (`.github/workflows/forecast.yml`, ~17:00 WIB on trading days, plus a manual "Run workflow" button):

1. Create the restricted Postgres role `sumire_forecaster` with the SQL in docs/forecasting.md (read bars, write forecasts only).
2. Add the repository secret `SUPABASE_CONNECTION_STRING`: the **pooler** string (port 6543) for that role.
3. Optional: `FORECAST_LOCAL_FALLBACK=true` runs `forecast --once` locally at 18:00 WIB when the Action wrote nothing that day.

FCST and the signal score switch from the baseline to Chronos only once its tracked accuracy beats the baseline.

## Sumire (mascot)

She sits bottom right and reacts to IHSG, alerts, new insights and running research. She wears gym clothes during the session and the school uniform otherwise; `m` hides her. No game art is included: an original placeholder is drawn unless you add your own portraits to `web/public/assets/sumire/` (gitignored). See [docs/mascot.md](docs/mascot.md) for file names, sources and the personal-use note.

## Publish

```powershell
dotnet publish src/Sumire.Api -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o publish
```

Publishing builds the web UI first (`npm ci` if needed, then `npm run build`; skip it with `-p:SkipWebBuild=true`). The result is `publish\Sumire.exe` (~57 MB, no .NET install needed) with `wwwroot\`, `Seed\` and `Research\` next to it. Put `.env` in that folder or one above it. The build is not trimmed, because EF Core and SignalR rely on reflection. Footprint numbers are in [docs/footprint.md](docs/footprint.md).

CLI verbs on the same exe: `forecast --once`, `research --once`, `install-schedule`, `uninstall-schedule`, `mcp`.

## Development

```sh
dotnet build SumirePlatform.sln
dotnet test SumirePlatform.sln
cd web
npm run build
npm run lint
```

Endpoint docs are in [docs/api/](docs/api/). Tests never touch the real DB or network.
