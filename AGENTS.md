# Sumire Platform

A personal, Bloomberg-style market terminal for the Indonesia Stock Exchange (IHSG / IDX), themed after Sumire Yoshizawa (Persona 5 Royal). One small .NET process serves a React SPA on `http://localhost:5080`, reads free Yahoo data, runs quant analytics and forecasts, and queues AI research through agent CLIs.

**Source of truth: [implementation-plan.md](implementation-plan.md).** Read the section for the area you touch before coding.

## Stack

- Backend: .NET 9 (`net9.0`), ASP.NET Core Minimal APIs, SignalR, EF Core + Npgsql (Supabase Postgres, schema `sumire`), DotNetEnv, Microsoft.Extensions.Http.Resilience, Skender.Stock.Indicators, MathNet.Numerics. Tests: xUnit.
- Frontend: Vite + React + TypeScript in `web/`, plain CSS modules + CSS variables, `lightweight-charts`, `@tanstack/react-query`, `@microsoft/signalr`. Node 22 / npm 10.

## Rules

1. **Do not overengineer.** No repositories, CQRS, mediators, extra layers, microservices, Docker, Redis or Hangfire. Jobs are `BackgroundService` + `PeriodicTimer`; caching is `IMemoryCache`.
2. **UX first:** fast, keyboard-friendly, clear loading / empty / error states.
3. **Secrets:** never read, print, grep or log values in `.env`. Code loads it with DotNetEnv and uses env var names only (see `.env.example`). Never commit `.env`. Never log connection strings, hosts or user names.
4. **Database:** all entities and migrations live in `src/Sumire.Api/Data/`. Schema changes need a migration (`dotnet ef migrations add <Name> --project src/Sumire.Api --output-dir Data/Migrations`). Tables are snake_case in schema `sumire`; jsonb columns are strings; write timestamps in UTC.
5. **Tests** never need the real DB or network: pure functions, fakes, recorded JSON fixtures in `src/Sumire.Api.Tests/Fixtures/<area>/`.
6. **Git:** Conventional Commits (`feat(market): add Yahoo chart client`), imperative, lowercase, <= 72 chars, small logical commits, the existing git identity. No AI attribution anywhere (no Co-Authored-By, no "Generated with", no tool or model names in commits or comments).
7. Document every HTTP endpoint in `docs/api/<area>.md` (method, path, query, JSON example).
8. Comments only where they add information; match the surrounding style.

## Run and test

```sh
dotnet build SumirePlatform.sln
dotnet test SumirePlatform.sln
dotnet run --project src/Sumire.Api                      # web + UI on http://localhost:5080
dotnet run --project src/Sumire.Api -- research --once   # other verbs: forecast --once, install-schedule, uninstall-schedule, mcp
curl http://localhost:5080/api/health

cd web && npm install && npm run dev                      # Vite dev server, proxies /api and /hubs to :5080
cd web && npm run build                                   # outputs to src/Sumire.Api/wwwroot
```

The app starts and serves the UI even when the database is unreachable; `/api/health` reports why.

## Folder map

```
src/Sumire.Api/
  Program.cs            CLI verb dispatch + web host wiring (calls Add<Area>/Map<Area>)
  Common/               WIB time, paths (SeedPaths), core services, /api/health
  Data/                 SumireDbContext, entities, Db helpers, Migrations/
  Market/               Yahoo client, quotes hub, IdxRules (ticks, ARA/ARB, lot, fees), MarketCalendar
  Analytics/            indicators, risk stats, GARCH, signal score, breadth, screener
  Forecasting/          baseline, Chronos-2, evaluator, `forecast --once`
  Research/             queue, runners, validator, scheduler, `research --once`, Mcp/
  Relations/            relations map, correlation clusters
  Alerts/               alerts, watchlists, settings, RSS news
  Seed/                 instruments.csv, relations.json, idx-rules.json, idx-holidays.json
src/Sumire.Api.Tests/   xUnit tests, Fixtures/<area>/
web/                    React SPA (function screens in web/src/functions/<CODE>.tsx)
docs/api/               endpoint docs per area
.agents/rules/          coding conventions
```

Each area owns `src/Sumire.Api/<Area>/<Area>Module.cs` with `Add<Area>(IServiceCollection, IConfiguration)` and `Map<Area>(IEndpointRouteBuilder)`. CLI verbs build the same services but never start the host, so `BackgroundService`s only run in web mode.

## Time and market rules

All market times are WIB (Asia/Jakarta). Use `TimeProvider` (injected) and `Common/Wib`, never `DateTime.Now`. `MarketCalendar` knows sessions (Friday has a longer break), weekends and IDX holidays; `IdxRules` knows tick sizes, dated ARA/ARB regimes, lot size and fees. Both read `Seed/idx-*.json`; entries marked `"verified": false` should be checked against IDX announcements.
