# Sumire MCP server

`Sumire mcp` is a stdio [MCP](https://modelcontextprotocol.io) server (official C# SDK, `ModelContextProtocol` 2.3.0). An agent in Antigravity, Gemini CLI or Claude Code can attach to it, work the research queue on whatever model you have selected, and read Sumire's market facts.

stdout carries the protocol; all logging goes to stderr. Each tool call runs in its own DI scope, so a fresh `DbContext` per call.

## Tools

| Tool | Arguments | What it does |
|---|---|---|
| `list_pending_research` | `limit` (1-50, default 10) | Pending jobs by priority, plus jobs this runner (`mcp`) already claimed. |
| `get_research_job` | `id` | Claims the job (status `running`, runner `mcp`), builds and stores the fact packet, and returns `instructions` (the same prompt the headless runners get, plus how to submit), `packet` and `schema` (insight.schema.json). Calling it again returns the same packet. Jobs running under another runner are refused. |
| `submit_insight` | `job_id`, `json` (insight object as a string), `model` (optional) | Parses and validates against the stored packet (`InsightValidator`). The **first** submission with issues is rejected and the issues come back; the **second** is stored with them as warnings. Malformed JSON or schema errors are always rejected and don't use up that retry. On success: insight stored, job `done`, SignalR `insightReady` sent when the web app runs in the same process (otherwise the UI picks it up on its next read). |
| `get_stock_snapshot` | `code` | The stock fact packet without headlines: price action 1D/1W/1M/3M, technicals, risk, GARCH ±1σ range, signal score breakdown, unusual volume, fundamentals, sector and IHSG moves, related tickers. |
| `get_relations` | `code` | Same JSON as `GET /api/stocks/{code}/relations`. |
| `get_market_overview` | none | IHSG/LQ45 moves, regime, breadth, sector leaders/laggards, top core movers, macro strip. |
| `queue_research` | `kind` (stock, sector, ask, open, close, week, or the full brief_* names), `subject`, `question` | Same rules and dedupe as `POST /api/research`. Returns `{jobId, status: queued|duplicate|cached, insightId}`. |

Errors come back as `{"error": "..."}`. With the database down or unconfigured, the queue tools say so (for the Supabase direct host, switch to the pooler connection string); `get_stock_snapshot`, `get_relations` and `get_market_overview` still work from Yahoo.

### Fact packets

`Research/FactPacket/` builds every packet (also for the headless runners): `MarketFactPacketBuilder` gathers inputs (analytics snapshot from `AnalyticsStore`, `RelationsService`, macro quotes, watchlist, RSS headlines; each optional) and `FactPackets` composes them. Packets are trimmed to 14,000 chars (about 4k tokens). `moves` only holds instruments with full daily history, since the validator checks numbers against it; today-only quotes go in `macro` and `relations`. The last 5 headlines are flagged `"untrusted": true`, cleaned of control characters and packet markers, and come with a `notice`.

## Antigravity

`.agents/mcp_config.json` registers the server:

```json
{ "mcpServers": { "sumire": { "command": "dotnet",
  "args": ["run", "--no-launch-profile", "-v", "q", "--project", "src/Sumire.Api", "--", "mcp"] } } }
```

If Antigravity starts servers outside the repo folder, use an absolute path for `--project`. With a published build, point straight at the exe (starts faster, no build step):

```json
{ "mcpServers": { "sumire": { "command": "C:\\Tools\\Sumire\\Sumire.exe", "args": ["mcp"] } } }
```

Then run the workflow **`/sumire-research`** (`.agents/workflows/sumire-research.md`): it lists pending jobs, researches each with web search, submits, and stops after 5 jobs (or the number you pass).

## Gemini CLI

Add to `~/.gemini/settings.json` (or `.gemini/settings.json` in the repo):

```json
{ "mcpServers": { "sumire": { "command": "dotnet",
  "args": ["run", "--no-launch-profile", "-v", "q", "--project", "C:\\path\\to\\StockChecker\\src\\Sumire.Api", "--", "mcp"] } } }
```

Check with `/mcp` inside `gemini`, then ask it to follow `.agents/workflows/sumire-research.md`.

## Claude Code

```powershell
claude mcp add sumire -- dotnet run --no-launch-profile -v q --project C:\path\to\StockChecker\src\Sumire.Api -- mcp
# or, published:
claude mcp add sumire -- C:\Tools\Sumire\Sumire.exe mcp
```

`claude mcp list` shows it; `/mcp` inside a session shows the tools.

## Smoke test by hand

The server shuts down as soon as stdin closes, so a one-line pipe gets no answer. Keep stdin open and read the reply, e.g. in Python:

```python
import json, subprocess
p = subprocess.Popen([r"src\Sumire.Apiin\Debug
et9.0\Sumire.exe", "mcp"], stdin=subprocess.PIPE, stdout=subprocess.PIPE, text=True)
p.stdin.write(json.dumps({"jsonrpc": "2.0", "id": 1, "method": "initialize", "params": {
    "protocolVersion": "2025-06-18", "capabilities": {}, "clientInfo": {"name": "t", "version": "1"}}}) + "
"); p.stdin.flush()
print(p.stdout.readline())   # initialize result, serverInfo.name == "sumire"
p.stdin.write('{"jsonrpc":"2.0","method":"notifications/initialized"}
{"jsonrpc":"2.0","id":2,"method":"tools/list"}
'); p.stdin.flush()
print(p.stdout.readline())   # the seven tools
```

Use the absolute path to the exe when you run this from another folder.
