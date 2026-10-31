# User Guide: Sumire Platform

Welcome to the **Sumire Platform**, a keyboard-centric, Bloomberg-style market terminal built for the Indonesia Stock Exchange (IHSG / IDX) with Persona 5 Royal theming (Sumire "Violet" Yoshizawa).

This guide walks you through navigating the interface, using all functions, analyzing stocks, managing the workspace, and using the AI research bridge.

---

## Table of Contents

1. [Quick Start & Launch](#1-quick-start--launch)
2. [Interface Overview](#2-interface-overview)
3. [Command Line Navigation](#3-command-line-navigation)
4. [Keyboard Shortcuts Cheat Sheet](#4-keyboard-shortcuts-cheat-sheet)
5. [Functions Reference](#5-functions-reference)
   - [Market & Macro Overview (`MKT`, `SECT`, `BRIEF`)](#market--macro-overview)
   - [Ticker Analysis (`DES`, `GP`, `FA`, `FCST`, `REL`)](#ticker-analysis)
   - [AI Research & Insights (`INS`, `ASK`, `RQ`)](#ai-research--insights)
   - [Discovery & Monitoring (`EQS`, `COMP`, `W`, `N`, `ALRT`)](#discovery--monitoring)
   - [Help (`HELP` / `?`)](#help)
6. [Multi-Pane Workspace](#6-multi-pane-workspace)
7. [AI Research & Scheduled Briefings](#7-ai-research--scheduled-briefings)
8. [Data Freshness & Status Indicators](#8-data-freshness--status-indicators)
9. [Troubleshooting & Common Questions](#9-troubleshooting--common-questions)

---

## 1. Quick Start & Launch

Ensure you have your environment configured (`.env`) and the web client built:

```powershell
# Build frontend UI assets (outputs to src/Sumire.Api/wwwroot)
cd web
npm install
npm run build
cd ..

# Start the application server
dotnet run --project src/Sumire.Api
```

Open your browser at **[http://localhost:5080](http://localhost:5080)**.

> [!TIP]
> The app is fully keyboard-driven. You rarely need to touch the mouse once you learn the shortcuts.

---

## 2. Interface Overview

```
+-------------------------------------------------------------------------------+
| [SUMIRE LOGO]  [COMMAND LINE INPUT: e.g. BBCA GP                    ] [STATUS]|
+-------------------------------------------------------------------------------+
|                                                                               |
|  Pane 1 (Active)                         |  Pane 2                            |
|  [BBCA DES - Profile & Key Stats]        |  [BBCA GP - Daily Candlestick]     |
|                                          |                                    |
+------------------------------------------+------------------------------------+
|  Pane 3                                  |  Pane 4                            |
|  [MKT - Market Breadth & Movers]         |  [BBCA REL - Relations & Groups]   |
|                                          |                                    |
+-------------------------------------------------------------------------------+
| Status Bar: Delayed · 14:32 WIB | DB: OK | RQ: 2/20 · agy:gemini-3.8-flash-low|
+-------------------------------------------------------------------------------+
```

The terminal consists of three main zones:
1. **Top Command Bar:** Contains the command prompt, ticker search, command history suggestions, and quick actions.
2. **Main Workspace:** Displays 1, 2, or 4 independent function panes.
3. **Status Bar (Bottom):** Shows IDX market session status, data delay/freshness, database connection health, and AI research quota status.

---

## 3. Command Line Navigation

Activate the command bar at any time by pressing `/`, `Ctrl+K`, or simply by typing letters.

### Command Syntax

```text
[TICKER] [FUNCTION] [ARGS]
```

- **Bare Ticker:** Typing a ticker alone opens company profile (`DES`).
  - Example: `BBCA` <kbd>Enter</kbd> &rarr; opens `DES` for Bank Central Asia.
- **Ticker + Function:** Executes the function for that ticker.
  - Example: `BBRI GP` &rarr; opens candlestick chart for BBRI.
  - Example: `ADRO INS!` &rarr; queues a fresh AI research insight for ADRO.
  - Example: `TLKM FA` &rarr; opens fundamental valuation for TLKM.
- **Function Only:** Uses the active/focused pane's ticker.
  - Example: If Pane 1 is focused on `BBCA`, typing `FCST` opens forecast metrics for `BBCA`.
- **Global Function:** Doesn't require a ticker.
  - Example: `MKT` &rarr; Market summary and breadth.
  - Example: `EQS` &rarr; Equity screener.
  - Example: `COMP BBCA BBRI BMRI BBNI` &rarr; Compare four banking tickers.
  - Example: `ASK why did tech stocks fall today?` &rarr; Ask an AI question with market context.

---

## 4. Keyboard Shortcuts Cheat Sheet

Press `?` or run `HELP` at any time to display the full interactive cheat sheet.

### Command Line
| Key | Action |
|---|---|
| `/` or `Ctrl + K` or any letter | Focus command line |
| <kbd>Tab</kbd> | Accept auto-completion suggestion |
| <kbd>&uarr;</kbd> / <kbd>&darr;</kbd> | Navigate autocomplete suggestions or command history |
| <kbd>Esc</kbd> | Dismiss suggestions, clear command line, or unfocus |
| <kbd>Enter</kbd> (GO) | Execute entered command |

### Panes & Layout
| Key | Action |
|---|---|
| `Alt + 1` ... `Alt + 4` | Focus pane 1, 2, 3, or 4 directly |
| <kbd>Tab</kbd> / <kbd>Shift + Tab</kbd> | Cycle through panes (when command input is unfocused) |
| `Alt + L` | Toggle layout: 1 pane &harr; 2 panes &harr; 4 panes |
| `Alt + &larr;` / `Alt + &rarr;` | Go back / forward in focused pane's navigation history |
| <kbd>Esc</kbd> | Close open modals/overlays or navigate back |

### Lists, Tables & Grid Navigation
| Key | Action |
|---|---|
| <kbd>&uarr;</kbd> / <kbd>&darr;</kbd> or `j` / `k` | Move selection up / down through list rows |
| <kbd>Enter</kbd> | Open the selected row's ticker |

### Chart View (`GP`)
| Key | Action |
|---|---|
| `1` - `6` | Change chart timeframe (e.g. 1D, 1W, 1M, 3M, 1Y, ALL) |
| `i` | Open technical indicators menu (SMA, EMA, RSI, MACD, Bollinger Bands) |
| `f` | Toggle forecast cone overlay |
| `v` | Toggle volume histogram |

### Quick Ticker Actions (for currently active ticker)
| Key | Action |
|---|---|
| `n` | Open `INS` (AI Insight) |
| `r` | Open `REL` (Relations & supply chain map) |
| `w` | Add / edit ticker in Watchlist (`W`) |
| `m` | Toggle Sumire assistant persona avatar visibility |
| `?` | Show interactive cheat sheet (`HELP`) |

---

## 5. Functions Reference

### Market & Macro Overview

- **`MKT` - Market Overview**
  - Displays overall IHSG composite index performance, advancing/declining/unchanged market breadth, top gainers, top losers, most active by volume/turnover, and market regime.
  - *Example:* `MKT`
- **`SECT` - Sector Heatmap & Rotation**
  - Displays performance across IDX sectors (Financials, Energy, Basic Materials, Infrastructure, etc.) and rotation trends. Run `SECT INS` for sector-level narrative summaries.
  - *Example:* `SECT`
- **`BRIEF` - Market Briefings**
  - Morning brief (pre-market setup), closing recap (post-market summary), or Sunday week-ahead preview.
  - *Example:* `BRIEF` or `BRIEF close`

### Ticker Analysis

- **`DES` - Description & Profile**
  - Company overview, market cap, business summary, listed shares, 52-week high/low, and key trading stats.
  - *Example:* `BBCA` or `BBCA DES`
- **`GP` - Price Graph & Technicals**
  - Interactive candlestick chart powered by TradingView lightweight-charts.
  - Includes volume, moving averages, technical indicator menus (`i`), and forecast cones (`f`).
  - *Example:* `BBRI GP`
- **`FA` - Fundamentals**
  - Key financial ratios: P/E, P/B, EV/EBITDA, ROE, ROA, Debt-to-Equity, dividend yield, and peer comparisons against sector medians.
  - *Example:* `TLKM FA`
- **`FCST` - Forecast & Signal Score**
  - Multi-horizon forecast cone, directional signal score (combining trend, momentum, risk, and mean-reversion metrics), and historical track record evaluation.
  - *Example:* `ASII FCST`
- **`REL` - Relations Map**
  - Visual cluster map revealing business groups (e.g., Astra, Salim, Barito), shared commodity exposures (Coal, Nickel, CPO, Oil), customer/supplier supply chain ties, and macro sensitivities.
  - *Example:* `BREN REL`

### AI Research & Insights

- **`INS` - AI Insight**
  - Synthesized investment research linking news events, disclosures, price action, and related companies. Never hallucinates price data.
  - Use `INS!` with an exclamation mark to force queue a fresh research run.
  - *Example:* `ADRO INS` or `ADRO INS!`
- **`ASK` - Market Context Q&A**
  - Ask questions in natural language. Sumire injects current market stats, recent headlines, and relations into the prompt context.
  - *Example:* `ASK why did banking stocks drop this afternoon?`
- **`RQ` - Research Queue**
  - Monitor pending, running, and completed background AI tasks. Displays daily quota usage, active CLI runner status (`agy`, `gemini`, or `claude`), and allows pausing/resuming jobs.
  - *Example:* `RQ`

### Discovery & Monitoring

- **`EQS` - Equity Screener**
  - Screen IDX stocks using preset filters: high dividend yield, oversold RSI dip-buys, momentum breakout, value with low P/B and solid ROE.
  - *Example:* `EQS`
- **`COMP` - Ticker Comparison**
  - Compare up to 4 stocks side-by-side across performance, valuation ratios, and technical ratings.
  - *Example:* `COMP BBCA BBRI BMRI BBNI`
- **`W` - Watchlists**
  - Manage personal watchlists with real-time price updates and alert triggers.
  - *Example:* `W`
- **`N` - News & RSS Headlines**
  - Aggregated financial headlines from CNBC Indonesia, Kontan, Bisnis Indonesia, and IDX Channel. Filter by ticker or view macro market headlines.
  - *Example:* `BMRI N` or `N`
- **`ALRT` - Alerts**
  - Set price threshold alerts, percentage change alerts, or technical indicator signal triggers.
  - *Example:* `ALRT`

### Help

- **`HELP` / `?` - Interactive Cheat Sheet**
  - Complete list of function codes and keyboard commands. Click any item to run its example command immediately.

---

## 6. Multi-Pane Workspace

Sumire terminal supports flexible multi-tasking:
1. **Toggle Panes:** Press `Alt + L` to cycle through:
   - **Single-pane view:** Full focus on a single chart or deep dive.
   - **Two-pane split:** Ideal for comparing `GP` (chart) and `DES` (profile).
   - **Four-pane quad:** Classic Bloomberg terminal layout (e.g., `MKT`, `GP`, `FA`, `INS`).
2. **Switch Active Pane:** Press `Alt + 1`, `Alt + 2`, `Alt + 3`, or `Alt + 4`, or press <kbd>Tab</kbd> to cycle.
3. **Persistent Layouts:** Your pane arrangement and historical navigation states are saved locally and synced to the database when connected.

---

## 7. AI Research & Scheduled Briefings

Sumire bridges to AI agent CLIs already authenticated on your system (`agy`, `gemini`, or `claude`):

- **Automatic Briefings via Task Scheduler:**
  Install automated schedules with:
  ```powershell
  dotnet run --project src/Sumire.Api -- install-schedule
  ```
  This creates automated Task Scheduler runs for:
  - **Morning Brief:** 07:45 WIB
  - **Post-Close Catch-up:** 16:35 WIB
  - **Closing Recap:** 16:45 WIB
  - **Sunday Week Ahead:** 19:00 WIB
  To uninstall schedules:
  ```powershell
  dotnet run --project src/Sumire.Api -- uninstall-schedule
  ```

- **Run AI Research Manually:**
  Process the research queue from CLI on demand:
  ```powershell
  dotnet run --project src/Sumire.Api -- research --once
  ```

---

## 8. Data Freshness & Status Indicators

Look at the bottom status bar for quick operational diagnostics:

- **Market Session:** Displays current IDX trading session (Pre-opening, Session 1, Break, Session 2, Closed) in Western Indonesia Time (WIB).
- **Quote Delay:** Indicates free Yahoo Finance delayed quote timestamps (~15 minutes delay during market hours).
- **Database Status:**
  - `DB: OK`: Connected to Supabase PostgreSQL pooler.
  - `DB Down`: Database unreachable. The app falls back to live Yahoo queries for basic quotes, but historical watchlists and AI queue will be paused.
- **RQ Indicator:**
  - Shows current daily AI budget usage (e.g., `RQ 4/20 · agy · gemini-3.8-flash-low`). Clicking it opens the `RQ` queue screen.

---

## 9. Troubleshooting & Common Questions

### Database connection error ("No such host")
- **Cause:** Free-tier Supabase direct hosts (`db.<ref>.supabase.co`) are IPv6-only, which many Windows networks cannot resolve.
- **Fix:** In `.env`, ensure `SUPABASE_CONNECTION_STRING` uses the **Session pooler** or **Transaction pooler** address:
  `postgresql://postgres.<ref>:<password>@aws-0-<region>.pooler.supabase.com:5432/postgres`

### UI shows a blank page or 404
- **Cause:** Static frontend bundle not compiled.
- **Fix:** Run `cd web && npm run build` to output distribution files to `src/Sumire.Api/wwwroot`.

### Ticker returns "No data"
- Ensure you are using the valid IDX ticker symbol (e.g., `BBCA`, `TLKM`, `GOTO`, `ASII`). The backend automatically resolves them against the `.JK` Yahoo suffix.
- Small caps may occasionally lack complete fundamental statements from free Yahoo data feeds.

---

Enjoy using **Sumire Platform**! For questions on technical architecture and API endpoints, refer to [docs/api/](api/) and [implementation-plan.md](../implementation-plan.md).
