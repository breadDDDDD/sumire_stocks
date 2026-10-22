# Research

AI research queue, insights and briefs (plan §6). Every endpoint returns 503 ProblemDetails when `SUPABASE_CONNECTION_STRING` is not set.

SignalR: when a job finishes, the server sends `insightReady` on `/hubs/quotes` with `{ "kind": "stock", "subject": "ADRO", "insightId": 42 }`. Fetch it with `GET /api/insights/{id}`.

## POST /api/research

Queues a research job. On-command jobs get priority 80 (only briefs, at 100, rank higher).

Body:

```json
{ "kind": "stock", "subject": "ADRO", "question": null, "force": true }
```

| Field | Notes |
|---|---|
| `kind` | `stock`, `brief_open`, `brief_close`, `brief_week`, `sector`, `ask`. The shorthands `open`, `close` and `week` work too. |
| `subject` | Ticker for `stock`, sector name for `sector` (as in `instruments.sector`). Ignored for briefs (always `IHSG`). |
| `question` | Required for `ask`, which becomes the subject (trimmed to 200 chars). |
| `force` | `INS!`: research even when a fresh insight exists (default false). |

Response: 202 when queued, otherwise 200:

```json
{ "jobId": 118, "status": "queued", "insightId": null }
```

| `status` | Meaning |
|---|---|
| `queued` | New job created. The scheduler wakes at once. |
| `duplicate` | The same kind+subject is already pending or running. `jobId` is that job (its priority is raised if needed). |
| `cached` | A fresh insight exists (younger than 6h, `RESEARCH_STOCK_CACHE_HOURS`) and `force` was false. `jobId` is null and `insightId` is set. |

400 ProblemDetails for an unknown kind, a missing subject or question, or a stock subject that is not a ticker.

## GET /api/research

Queue and usage for the `RQ` screen and the status bar.

```json
{
  "enabled": true,
  "paused": false,
  "usage": { "today": 9, "dailyCap": 20, "batchCap": 8 },
  "current": { "runner": "agy", "model": "claude-sonnet-4-6" },
  "blockedReason": null,
  "runners": [
    { "runner": "agy", "model": "gemini-3.8-flash-medium", "installed": true, "used": 6, "cap": null,
      "exhaustedUntil": "2026-10-25T07:53:00+07:00" },
    { "runner": "agy", "model": "claude-sonnet-4-6", "installed": true, "used": 3, "cap": { "pattern": "claude-*", "cap": 6 },
      "exhaustedUntil": null },
    { "runner": "claude-code", "model": "sonnet", "installed": true, "used": 0, "cap": { "pattern": "claude-*", "cap": 6 },
      "exhaustedUntil": null }
  ],
  "pending": 2,
  "jobs": [
    { "id": 118, "kind": "stock", "subject": "ADRO", "priority": 80, "status": "done", "runner": "agy",
      "model": "claude-sonnet-4-6", "attempts": 1, "error": null, "createdAt": "2026-10-22T16:47:02+07:00",
      "startedAt": "2026-10-22T16:47:05+07:00", "finishedAt": "2026-10-22T16:48:40+07:00", "insightId": 42 }
  ],
  "nextBatches": [
    { "kind": "close", "at": "2026-10-22T16:45:00+07:00" },
    { "kind": "morning", "at": "2026-10-23T07:45:00+07:00" },
    { "kind": "weekend", "at": "2026-10-25T19:00:00+07:00" }
  ]
}
```

- `current`: the runner:model the next job would use, or null. `blockedReason` then says why (`daily cap reached (20/20)`, `all runners are exhausted or at their model caps`).
- `paused` is true when paused from RQ or when `RESEARCH_ENABLED=false` (`enabled` is false then).
- `used` counts jobs finished (done or failed) since WIB midnight. Quota errors don't count. A model cap is shared by every entry it matches.
- `jobs`: the last 50 jobs, newest first. `status` is `pending`, `running`, `done`, `failed` or `skipped`. `error` on a `done` job holds validator warnings, e.g. `2 validator warning(s)`.
- `nextBatches`: the next time of each batch kind (`morning`, `midday` when enabled, `close`, `weekend`).

## POST /api/research/pause, POST /api/research/resume

Response: `{ "paused": true }`. Pausing stops any new job from starting; a running job finishes. Resume still reports `paused: true` when `RESEARCH_ENABLED=false`.

## GET /api/stocks/{code}/insight

The latest stock insight, cached or stale (show its age with `ageMinutes`). Response is an Insight (below).

404 when none exists yet, with the most recent job so the UI can show "queued" or "failed":

```json
{ "title": "No insight for ADRO yet", "status": 404, "lastJob": { "id": 118, "status": "pending", "...": "..." } }
```

`lastJob` is null when nothing was ever queued.

## GET /api/briefs/latest?kind=open|close|week

The latest brief of that kind. Without `kind`, the newest brief of any kind. Returns an Insight, 404 ProblemDetails when there is none, or 400 for a bad kind.

## GET /api/insights/{id}

One insight by id (the id comes from `insightReady` or `jobs[].insightId`). 404 when it is missing.

## Insight

```json
{
  "id": 42,
  "kind": "stock",
  "subject": "ADRO",
  "createdAt": "2026-10-22T16:48:40+07:00",
  "ageMinutes": 12,
  "runner": "agy",
  "model": "claude-sonnet-4-6",
  "sentiment": 0.4,
  "confidence": "med",
  "summary": "ADRO rose with coal prices after China lifted imports.",
  "drivers": [
    { "claim": "Coal benchmark rallied", "evidence": "Newcastle coal rose 6% this week; ADRO rose 3.1% today.",
      "linked": ["ADRO", "COAL"], "sourceIds": ["s1"] }
  ],
  "connections": [ { "from": "COAL", "to": ["ADRO", "PTBA", "ITMG"], "text": "Coal benchmark +6% this week lifts coal miners." } ],
  "catalysts": [ { "date": "2026-10-30", "event": "ADRO Q3 results" } ],
  "risks": ["China import curbs return"],
  "whatWouldChangeThisView": "A reversal in coal prices.",
  "linked": ["ADRO", "COAL", "PTBA", "ITMG"],
  "sources": [
    { "id": "s1", "title": "Coal prices climb on China demand", "url": "https://…", "publisher": "Kontan", "publishedAt": "2026-10-21" }
  ],
  "warnings": [ { "code": "stale_source", "message": "Source \"…\" is from 2026-09-01, older than 14 days." } ]
}
```

- `sentiment` runs from -1 to 1. `confidence` is `low`, `med` or `high`.
- `warnings` is empty when every check passed. Otherwise show a warning badge. Codes: `no_sources`, `driver_unsourced`, `unknown_ticker`, `stale_source`, `source_date_missing`, `source_url_invalid`, `number_mismatch`.
- Always label insights "AI-generated". They never contain price targets.
