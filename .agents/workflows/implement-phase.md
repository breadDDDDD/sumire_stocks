---
description: Implement one phase of implementation-plan.md (e.g. /implement-phase 6)
---

# /implement-phase N

1. Read `AGENTS.md`, `.agents/rules/conventions.md` and, in `implementation-plan.md`, §9 phase N (its scope and its **Done when** list) plus every section it refers to.
2. Read the code and `docs/api/*.md` the phase builds on. Don't duplicate what exists; extend it.
3. Make a short plan (files to add or change, endpoints, tests). Keep to the plan's stack and conventions: no extra layers, no new frameworks.
4. Implement in small steps. After each step, build (`dotnet build`, and `npm run build` in `web/` for frontend work) and fix errors before moving on.
5. Add tests for the logic (xUnit, no DB or network, fixtures under `src/Sumire.Api.Tests/Fixtures/`). Run `dotnet test` (and `npm run lint --if-present` in `web/`) until green.
6. Document every new HTTP endpoint in `docs/api/<area>.md` (method, path, query, JSON example).
7. Walk through the phase's **Done when** list and check each item for real (run the app where needed). Report anything not met.
8. Commit in small Conventional Commits (`feat(scope): ...`, `test(scope): ...`, `docs: ...`) under the existing git identity, with no AI attribution.
