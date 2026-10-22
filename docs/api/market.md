# Market data API

Base `http://localhost:5080`. JSON camelCase, prices are numbers, times ISO-8601 in WIB (`+07:00`).
Errors are ProblemDetails: `404` unknown code, `400` bad parameter, `503` database or Yahoo unavailable (`title` says which).
Quotes are Yahoo's delayed quotes (~10 min); show a "Delayed · as of HH:mm:ss WIB" badge from `time`.

## GET /api/instruments?q=&limit=

Search by code or name. Ranking: exact code, code prefix, name word prefix, name contains; core stocks first within a rank.
Empty `q` returns core stocks A–Z. `limit` default 20, max 100. Works without the database (seeded from `instruments.csv`).

```json
[
  {
    "code": "BBCA",
    "name": "PT Bank Central Asia Tbk",
    "sector": "Financials",
    "industry": "Banks",
    "board": "Utama",
    "isIndex": false,
    "indexMemberships": ["LQ45", "IDX30", "IDX80", "KOMPAS100"],
    "groupName": "Djarum",
    "core": true
  }
]
```

Indexes and macro series are instruments too (`isIndex: true`): `IHSG`, `LQ45`, `SPX`, `N225`, `HSI`, `STI`, `SSE`, `USDIDR`, `WTI`, `BRENT`, `GOLD`, `US10Y`.

## GET /api/instruments/{code}

One instrument (same shape as above) or 404.

## GET /api/quotes?codes=BBCA,IHSG,USDIDR

Latest quote per known code (unknown codes are left out, order follows the request). During market hours these are
delayed live quotes (cached 60 s); outside hours the last daily close from the database (`source: "close"`).
Max 200 codes.

```json
[
  {
    "code": "BBCA",
    "name": "PT Bank Central Asia Tbk",
    "price": 5175,
    "change": -100,
    "changePct": -1.8957,
    "prevClose": 5275,
    "open": 5125,
    "high": 5275,
    "low": 5100,
    "volume": 286928500,
    "value": 1484854987500,
    "time": "2026-10-22T16:14:58+07:00",
    "source": "live"
  },
  {
    "code": "IHSG",
    "name": "IDX Composite (IHSG)",
    "price": 5387.905,
    "change": 59.5449,
    "changePct": 1.1175,
    "prevClose": 5278.89,
    "open": 5247.979,
    "high": 5422.587,
    "low": 5185.527,
    "volume": 0,
    "value": null,
    "time": "2026-10-22T16:00:05+07:00",
    "source": "live"
  }
]
```

`changePct` is in percent (−1.8957 = −1.90 %). `value` is price × volume in Rupiah (null for indexes/macro).

## GET /api/chart/{code}?range=&interval=

| range | interval | source |
|---|---|---|
| `1d` | `1m` (default), `2m`, `5m`, `15m`, `30m`, `60m` | Yahoo intraday, cached 60 s |
| `5d` | `5m` (default), same options | Yahoo intraday, cached 60 s |
| `1m`, `3m`, `6m` (default), `ytd`, `1y`, `3y` | `1d` (default) or `1wk` | Database daily bars; a stock outside the core universe gets 3 years fetched and stored on first request. Falls back to Yahoo when the DB is down. |

```json
{
  "code": "BBCA",
  "range": "1m",
  "interval": "1d",
  "source": "db",
  "prevClose": null,
  "bars": [
    { "time": 1790035200, "date": "2026-09-22", "open": 6225, "high": 6250, "low": 6175, "close": 6200, "volume": 93211200 }
  ]
}
```

- `time` is unix seconds of the bar in **WIB wall-clock** (the UTC timestamp shifted by +7 h), so `lightweight-charts`
  (which renders UTC) shows WIB times directly. Daily bars use 00:00 of `date`.
- `date` is `YYYY-MM-DD` for daily/weekly bars, `null` for intraday.
- `prevClose` is set for intraday ranges (previous session close, for the baseline).
- `source`: `db` or `yahoo`.

## GET /api/status

Freshness and job state for the status bar.

```json
{
  "time": "2026-10-22T14:32:10+07:00",
  "session": "session2",
  "isTradingDay": true,
  "holiday": null,
  "nextTradingDay": "2026-10-23",
  "lastBarDate": "2026-10-21",
  "expectedBarDate": "2026-10-21",
  "lastQuoteTime": "2026-10-22T14:22:05+07:00",
  "db": "ok",
  "dbError": null,
  "eod": {
    "running": false,
    "lastRun": "2026-10-21T16:31:40+07:00",
    "lastResult": "122 symbols, 1220 rows upserted, 0 failed, 41s",
    "lastError": null,
    "nextRun": "2026-10-22T16:30:00+07:00"
  },
  "quotes": { "connections": 1, "polling": true, "lastPoll": "2026-10-22T14:32:01+07:00" }
}
```

`session`: `closed`, `preOpen`, `session1`, `break`, `session2`, `preClose`. Bars are stale when `lastBarDate < expectedBarDate`.

## SignalR /hubs/quotes

```ts
const conn = new HubConnectionBuilder().withUrl("/hubs/quotes").withAutomaticReconnect().build();
conn.on("quotes", (quotes: Quote[]) => { /* same shape as GET /api/quotes */ });
await conn.start();
await conn.invoke("Subscribe", ["BBCA", "BBRI"]);   // adds codes; current quotes are sent back immediately
await conn.invoke("Unsubscribe", ["BBRI"]);
```

- Every 60 s while the IDX session is open (pre-open through pre-close, not during the lunch break) and at least one
  tab is connected, the server polls Yahoo and pushes one `quotes` message per connection with its subscribed codes
  plus the tape (all `isIndex` instruments). Nothing is polled when no tab is open. Re-subscribe after a reconnect.
- Other areas push their own events on this hub (e.g. `insight`); see their docs.

## Background jobs (no endpoints)

- **EOD pipeline**: 16:30 WIB on trading days, plus a catch-up at startup when bars are behind. Imports
  `Seed/instruments.csv` into `instruments`, syncs daily bars for core instruments (3-year backfill when missing,
  else the last ~10 days), then runs registered `IEodPipelineStep`s in `Order`.
- **Intraday quotes**: see SignalR above.
