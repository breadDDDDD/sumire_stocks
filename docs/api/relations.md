# Relations API

Base `http://localhost:5080`. JSON camelCase. `chgPct` is today's move in percent (2.25 = +2.25 %), taken from
`/api/quotes` (delayed live quote during market hours, last close otherwise); `price`/`chgPct` are `null` when no quote is
available (e.g. a ticker that is not in the instrument catalog).

Two kinds of links, each marked with `source`:

- `curated`: hand-edited `Seed/relations.json` (groups, commodity and macro factors, supply chains). Reloaded automatically when the file changes.
- `statistical`: computed by the EOD pipeline (step `relations-stats`) from daily bars of core stocks:
  - `correlated`: 60-day daily log-return correlation, top 5 per stock with |rho| >= 0.6 (negative rho = moves opposite).
  - `leadLag`: correlation of the stock's return on day t with a proxy's return on the previous common trading day,
    over 120 days, kept when |t| >= 2.6 and |rho| >= 0.2 (up to 3 per stock). Proxies: `SPX`, `N225`, `HSI`, `STI`, `SSE`,
    `USDIDR`, `US10Y`, `WTI`, `BRENT`, `GOLD`.
  - Returns are taken on dates both series traded. Series with no bar in the 10 days before the as-of date (suspended,
    delisted, missing data) are left out.
  - Stored in memory and in `%LOCALAPPDATA%/Sumire/cache/relations-stats.json`. When none exist yet they are computed on the
    first request (needs the DB); without the DB the lists are empty and `statsAsOf` is `null`.

## GET /api/stocks/{code}/relations

404 when the code is neither an instrument nor mentioned in the relations map. A known instrument without links returns empty lists.

```json
{
  "code": "PTBA",
  "name": "PT Bukit Asam Tbk",
  "sector": "Energy",
  "quote": { "code": "PTBA", "name": "PT Bukit Asam Tbk", "price": 3180, "chgPct": 2.25 },
  "groups": [
    {
      "name": "MIND ID (state mining)",
      "note": "State mining holding under Danantara...",
      "peers": [ { "code": "ANTM", "name": "PT Aneka Tambang Tbk", "price": 2840, "chgPct": 4.41 } ]
    }
  ],
  "factors": [
    { "factor": "COAL", "kind": "commodity", "direction": "positive", "note": "Newcastle/ICI thermal coal...", "proxy": null, "source": "curated" },
    { "factor": "CHINA", "kind": "macro", "direction": "positive", "note": "China is the largest buyer...",
      "proxy": { "code": "SSE", "name": "SSE Composite Index", "price": 3658.03, "chgPct": -1.48 }, "source": "curated" }
  ],
  "supplyChain": [
    {
      "name": "Coal to power",
      "stages": [
        { "stage": "Mining contractors", "self": false, "tickers": [ { "code": "UNTR", "name": "PT United Tractors Tbk", "price": 24300, "chgPct": 1.25 } ] },
        { "stage": "Coal producers", "self": true, "tickers": [ { "code": "PTBA", "name": "PT Bukit Asam Tbk", "price": 3180, "chgPct": 2.25 } ] }
      ]
    }
  ],
  "correlated": [
    { "code": "ITMG", "name": "PT Indo Tambangraya Megah Tbk", "rho": 0.78, "n": 60, "chgPct": 1.61, "source": "statistical" }
  ],
  "leadLag": [
    { "proxy": "HSI", "name": "Hang Seng Index", "rho": 0.27, "n": 120, "tStat": 3.05, "chgPct": -0.8, "source": "statistical" }
  ],
  "statsAsOf": "2026-10-21",
  "mapUpdated": "2026-10-22"
}
```

- `factors[].kind`: `commodity` or `macro`; `direction`: `positive` (moves with the factor) or `negative`.
  `proxy` is the market series that tracks the factor (`GOLD`, `BRENT`, `USDIDR`, `US10Y`, `SSE` for CHINA) or `null`.
- `supplyChain[].stages[].self` marks the stage(s) that contain the requested ticker; the stages before and after it are its neighbours.
- `leadLag[].chgPct` is the proxy's latest move (what may carry over into the stock next session).

## GET /api/relations/factors

Factor names in the curated map, A–Z.

```json
["BI_RATE", "CHINA", "COAL", "COPPER", "CPO", "DOMESTIC_CONSUMPTION", "GAS", "GOLD", "NICKEL", "OIL", "TIN", "US10Y", "USDIDR"]
```

## GET /api/relations/factors/{factor}

Reverse lookup: tickers linked to a factor, with moves. `factor` is a factor name (case-insensitive) or a proxy code such as
`SPX` (then `entries` is empty and only the statistical followers are listed). 404 when unknown.

```json
{
  "factor": "GOLD",
  "kind": "commodity",
  "proxy": { "code": "GOLD", "name": "Gold Futures", "price": 4079.2, "chgPct": -1.71 },
  "entries": [
    {
      "direction": "positive",
      "note": "HRTA is a gold jewellery maker; UNTR via Agincourt (Martabe).",
      "tickers": [ { "code": "ANTM", "name": "PT Aneka Tambang Tbk", "price": 2840, "chgPct": 4.41 } ],
      "sectors": []
    }
  ],
  "leadLag": [ { "code": "MDKA", "name": "PT Merdeka Copper Gold Tbk", "rho": 0.31, "n": 120, "chgPct": 1.82 } ],
  "statsAsOf": "2026-10-21"
}
```

`entries` has one item per curated entry (OIL has a `positive` producers entry and a `negative` fuel-users entry).
`leadLag` lists stocks whose next-day move follows the proxy, strongest first.

## C# use (research fact packets)

`RelationsService` (scoped): `GetAsync(code)` and `GetFactorAsync(factor)` return the same records as the endpoints;
`Map` exposes `RelationsMap` lookups without quotes (`GroupsFor`, `FactorsFor`, `Factor`, `SupplyChainNeighbours`,
`RelatedTickers`); `Stats` is the latest `StatisticalSnapshot`.
