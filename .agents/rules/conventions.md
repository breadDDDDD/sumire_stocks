# Conventions

## C# (src/Sumire.Api)

- .NET 9, nullable enabled, file-scoped namespaces `Sumire.Api.<Area>`.
- Minimal APIs. Each area registers services in `<Area>Module.Add<Area>` and maps routes in `<Area>Module.Map<Area>`; keep endpoint handlers in the area folder. Routes start with `/api/`.
- No extra layers: inject `SumireDbContext` directly, no repositories, mediators or interface-per-class. Add an interface only where a second implementation is real (e.g. `IMarketDataProvider`, research runners).
- Records for DTOs. JSON is camelCase; enums serialize as camelCase strings. Prices are `decimal`; statistics are `double`.
- Time: inject `TimeProvider`; use `Wib.Now/Today/Iso`. Store `DateTimeOffset` in UTC. Dates as `DateOnly`.
- Background work: `BackgroundService` + `PeriodicTimer`; respect `MarketCalendar` and do nothing when idle.
- Outbound HTTP: typed `HttpClient` with `AddStandardResilienceHandler()`.
- Errors: let unexpected exceptions become ProblemDetails; return `Results.Problem`/`NotFound` for expected ones. Never put secrets, connection strings or hosts in messages or logs (use `Db.ShortError`).
- Seed files are read through `SeedPaths.File("name")`.
- Tests: xUnit, `Sumire.Api.Tests/<Area>/<Thing>Tests.cs`, no DB or network, fixtures under `Fixtures/<area>/`, fixed clocks via a fake `TimeProvider`.

## Frontend (web/)

- Vite + React + TypeScript, plain CSS modules, design tokens as CSS variables in `web/src/theme/tokens.css`. No UI framework.
- Palette: background `#0B0B0F`, amber text `#FFB000`, white values, crimson brand `#C8102E`, up `#22C55E`, down `#F43F5E`. Monospace tabular numbers (JetBrains Mono / IBM Plex Mono), Anton / Bebas Neue for slanted P5-style headers.
- Every screen is a lazy-loaded function component `web/src/functions/<CODE>.tsx` with loading (glasses spinner), empty and error states and a freshness badge ("Delayed · as of 14:32:10 WIB").
- Keyboard first: every action reachable without a mouse; follow the shortcuts in implementation-plan.md §7.3.
- Data fetching through `web/src/api/client.ts` (react-query); live quotes through `web/src/api/signalr.ts`.

## Commits

- Conventional Commits: `type(scope): subject` with types feat, fix, test, docs, chore, refactor, ci, build, style, perf. Imperative, lowercase subject, <= 72 characters, optional body.
- Small logical commits under the existing git identity; never change git config or pass `--author`.
- No AI attribution anywhere: no `Co-Authored-By` trailers, no "Generated with", no tool or model names in commits, code or comments.
