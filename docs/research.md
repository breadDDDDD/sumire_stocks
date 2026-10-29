# Research bridge

How Sumire runs AI research through the agent CLIs on your own plans (plan §6.3, §6.6). The endpoints are in [api/research.md](api/research.md).

## Flow

1. **Queue** (`research_jobs`): on-command requests (`INS!`, `ASK`, `SECT INS`) and scheduled batches enqueue jobs. A request is deduplicated by kind+subject: an open job is reused, and a fresh insight (under 6h) is returned unless forced. Priority: brief 100 > on-command 80 > watchlist 60 > sector 40 > mover 20.
2. **Fact packet** (`FactPacketBuilder`): instrument facts, `moves` (last, chg1d/5d/20d/60d, 52w high/low, volume vs 20d) for the subject, its group/industry peers, the indexes and macro symbols, sector averages, top movers (briefs), signal score and fundamentals. Other areas add sections by registering an `IFactPacketContributor` (quotes cache, RSS `headlines`, relations). The packet is capped at 14k chars, dropping headlines first.
3. **Runner**: the first `runner:model` in `RESEARCH_RUNNERS` that is installed, not exhausted and under its caps. It runs in `research-workspace/` (repo root, else `%LOCALAPPDATA%\Sumire\research-workspace`), which also gets a copy of `insight.schema.json`.
4. **Validation** (`InsightValidator`): every driver has a source id that exists, linked tickers exist (factor codes like COAL and USDIDR are allowed), sources are dated and no older than `RESEARCH_SOURCE_MAX_AGE_DAYS`, URLs are http(s), and a % move or Rp price stated for a single packet ticker matches the packet (tolerance max(0.6pp, 20%); YTD and yearly figures are skipped). On any issue the runner gets one retry with the issues as feedback. After that the insight is stored with `warnings`.
5. **Store and push**: the insight is saved, the job is marked `done` (its packet is cleared), and `insightReady` goes out over SignalR.

Jobs run one at a time. A timeout (`RESEARCH_TIMEOUT_SECONDS`, default 240 s) kills the process tree and requeues the job once. A quota error marks that runner:model exhausted until the reset time the CLI reports (otherwise until the next WIB midnight) and moves the same job to the next entry. When every entry is exhausted, the job goes back to pending and the queue stops. A job left in `running` by a crashed process goes back to pending after max(15 min, 3 × timeout + 1 min), and fails after 3 attempts; stopping the app mid-job puts it back without counting the attempt. The web worker claims jobs with `FOR UPDATE SKIP LOCKED` and the MCP `get_research_job` claims with a conditional update, so the two never run the same job.

## Schedule

| Batch | WIB | Days | Jobs (≤ `RESEARCH_BATCH_CAP`, briefs first) |
|---|---|---|---|
| morning | 07:45 | trading days | morning brief, plus watchlist names that moved ≥3% in the last session |
| midday | 12:05 | trading days, only with `RESEARCH_MIDDAY=true` | watchlist names moving ≥3%. Moves come from daily bars until an intraday source is wired in. |
| close | 16:45 | trading days | closing recap, top 2 and bottom 2 sectors, watchlist names without an insight in the last 20h, biggest core movers |
| weekend | Sun 19:00 | every Sunday | week-ahead brief |

- **Web mode**: `ResearchScheduler` (BackgroundService) checks every 30 s and wakes at once after `POST /api/research`.
- **UI closed**: `Sumire install-schedule` creates the Task Scheduler entries `SumireResearchMorning` (07:45), `SumireEod` (16:35), `SumireResearchClose` (16:45) and `SumireResearchWeekend` (Sun 19:00). Each runs `"<exe>" research --once`. Times are converted from WIB to the PC's time zone, and the tasks run while you are logged on. `Sumire uninstall-schedule` removes them.
- **Lock row**: a batch is claimed by inserting a `kind='batch'` row (subject such as `close:2026-10-22`) under an advisory lock, and jobs are claimed with `FOR UPDATE SKIP LOCKED`. The web app and Task Scheduler therefore never run the same batch or job twice.
- **Missed batches**: only the most recent due batch runs, and only if it is under 16 h old. Older briefs are never back-filled. No trading-day batches run on IDX holidays; the Sunday brief always runs.
- **Retention** (daily): pending jobs older than 1 day are marked `skipped`, jobs are deleted after 30 days, stock/sector/ask insights after 90 days and briefs after 1 year.

## Settings (.env)

| Variable | Default | Notes |
|---|---|---|
| `RESEARCH_ENABLED` | `true` | `false` pauses everything (the RQ pause button sets the `research.paused` setting) |
| `RESEARCH_RUNNERS` | `agy:gemini-3.8-flash-medium, agy:claude-sonnet-4-6, claude-code:sonnet` | Ordered fallback list of `runner:model`. Runners: `agy`, `gemini-cli`, `claude-code` |
| `RESEARCH_DAILY_CAP` | `20` | Jobs per WIB day, across all runners |
| `RESEARCH_BATCH_CAP` | `8` | Jobs queued per scheduled batch |
| `RESEARCH_MODEL_CAPS` | (none) | e.g. `claude-*:6, agy:gemini-*:15`. The glob matches the model, or `runner:model` when it contains `:`. Each cap is one pool shared by every entry it matches |
| `RESEARCH_MIDDAY` | `false` | Turns on the 12:05 batch |
| `RESEARCH_TIMEOUT_SECONDS` | `240` | Per job |
| `RESEARCH_SOURCE_MAX_AGE_DAYS` | `14` | Validator staleness window |
| `RESEARCH_STOCK_CACHE_HOURS` | `6` | Dedupe window for stock, sector and brief requests |
| `RESEARCH_WORKSPACE` | (auto) | Override the sandbox folder |

Exhaustion state is kept in the `research.exhausted` setting as `{ "runner:model": "<until>" }`.

## CLI findings on this machine (2026-10-22)

Checked with `--version`, `--help` and `agy models`, plus one tiny smoke prompt.

**agy 1.4.4** (`%LOCALAPPDATA%\agy\bin\agy.exe`)
- `agy models` lists: `gemini-3.8-flash-high`, `gemini-3.8-flash-medium`, `gemini-3.8-flash-low`, `claude-sonnet-4-6`, `claude-opus-4-6-thinking`, `gpt-oss-120b-medium`.
- Print-mode flags: `-p/--print <prompt>`, `--model`, `--output-format text|json|stream-json`, `--json-schema <json or file path>`, `--print-timeout <dur>`, `--disable-slash-commands`, `--mode accept-edits|auto|plan`, `--sandbox`, `--effort`. `--dangerously-skip-permissions` exists, but Sumire never passes it.
- **`-p` requires the prompt as its value.** A bare `-p` fails with `flag needs an argument: -p`, and `-p --model x` fails with "-p took "--model" as its prompt". So `AgyRunner` passes the whole prompt plus packet as the `-p` argument, kept under 24k chars because Windows caps the command line at 32k. stdin is not used.
- JSON output (observed on error): `{"conversation_id", "status": "ERROR", "response": "", "error": "...", "duration_seconds", "num_turns", "json_schema", "usage"}`. Success is assumed to be `status: "SUCCESS"` with the result in `response` (string) or `structured_output`, and the parser accepts both.
- **Quota error** (recorded in `src/Sumire.Api.Tests/Fixtures/Research/agy-quota.*`): exit code 3; stdout error `Individual quota reached. Please upgrade your subscription to increase your limits. Resets in 65h3m22s.`; stderr `AGY_ERROR: {"short_error":"RESOURCE_EXHAUSTED (code 429): …","status":"RESOURCE_EXHAUSTED","error_code":429,"retryable":true}`. The `Resets in …` duration is parsed and used as the exhaustion end.
- On 2026-10-22 the Gemini Flash pool on this account was exhausted (resets in about 65 h), so the smoke prompt never reached a model. Headless web search, and the exact success shape, still need checking once quota is back.

**claude 2.1.308 (Claude Code)**: installed. Sumire runs `claude -p --output-format json --json-schema '<inline schema>' --model <m> --tools WebSearch,WebFetch --allowedTools WebSearch,WebFetch --strict-mcp-config` with the prompt on stdin. The parser reads `structured_output`, falls back to a JSON block inside `result`, and treats `is_error: true` with "usage limit" / "hit your limit" text as quota. Not exercised here, to save quota.

**gemini (Gemini CLI)**: not installed. `GeminiCliRunner` runs `gemini --model <m> --output-format json -p "<short instruction>"` with the prompt and schema on stdin, and reads `response` / `error.message`. npm `.cmd` shims are started through `cmd.exe /d /s /c` (so a path with spaces works). Arguments containing `% & | < > ^ !` or line breaks are refused for batch files, because cmd.exe would interpret them; prompts for such runners must go on stdin (agy is an `.exe`, so its `-p` prompt is unaffected).

## Notes for other tasks

- `insightReady` is pushed through `SignalRInsightNotifier`, which looks up `IHubContext<Sumire.Api.Hubs.QuotesHub>` by type name. polish-integration should switch it to a direct `IHubContext<QuotesHub>` once market-data has merged.
- The research-mcp task can reuse `ResearchQueue` (enqueue and claim), `IFactPacketBuilder`, `ResearchPrompts`, `InsightSchema.Text`, `InsightDocument.TryParse` and `InsightValidator.Validate` for `get_research_job` / `submit_insight`.
