# Shared UI kit

Import everything from `components/ui`:

```tsx
import { Panel, Table, Badge, Button, Spinner, Empty, ErrorState, FreshnessBadge, Num, Skeleton, formatNum } from '../components/ui'
```

## Writing a function screen

Each function lives in `web/src/functions/<CODE>.tsx` and default-exports a component taking `FunctionProps`
(`{ code, ticker?, args, force? }`, see `functions/types.ts`). Put helpers in `functions/<code-lowercase>/` and
feature components in `components/<feature>/`. Typical shape:

```tsx
export default function DES({ ticker }: FunctionProps) {
  const q = useQuery({ queryKey: ['profile', ticker], queryFn: ({ signal }) => api<Profile>(`/stocks/${ticker}/profile`, { signal }) })
  return (
    <Panel title={`${ticker} DES`} subtitle={q.data?.name} actions={<FreshnessBadge asOf={q.data?.asOf} />}>
      {q.isPending ? <Skeleton /> : q.isError ? <ErrorState error={q.error} onRetry={() => q.refetch()} /> : <Body data={q.data} />}
    </Panel>
  )
}
```

The pane already wraps your screen in `Suspense` (Skeleton fallback) and an error boundary; a `Panel` should be the
root so the screen fills the pane.

## Navigation

- `usePane().open('BBRI GP')` or `usePane().open({ code: 'DES', ticker: 'BBRI', args: [] })` opens something in the
  pane your component lives in (with history, so Alt+← goes back). Returns an error string or null.
- `useWorkspace().run(command, paneIndex?)` from outside panes (watchlist rail, tape) targets the focused pane.
- `useWorkspace().focusedTicker` is the ticker in the focused pane.
- `w` dispatches `window` event `watchlist:add` with `detail: { ticker }`.

## Keyboard

Global shortcuts listen on `window` in the bubble phase and skip events whose default was prevented. To own a key
(e.g. chart keys `1`–`6`, `i`, `f`, `v`) handle it on a focused element and call `e.preventDefault()`. Make the main
interactive area focusable with `tabIndex={0}`: Tab / Alt+1..4 focus the first `[tabindex="0"]` in the pane.

## Components

| Component | Props | Notes |
|---|---|---|
| `Panel` | `title, subtitle?, actions?, padded?, className?, bodyClassName?` | Slanted crimson header; body scrolls. |
| `Table<T>` | `columns: Column<T>[], rows, rowKey, onOpen?, initialSort?, autoFocus?, empty?` | Sticky header, click header to sort, `j/k/↑/↓/Home/End/PgUp/PgDn`, `Enter` → `onOpen(row)`. Column: `key, header, render?, sortValue?, sortable?, align?, width?`. Right-aligned columns sort descending first. |
| `Badge` | `tone?: default \| amber \| crimson \| up \| down \| muted \| solid, title?` | Small uppercase label. |
| `Button` | `variant?: default \| primary \| ghost, active?` + button attrs | |
| `Spinner` | `size?, label?, center?` | Sumire's round glasses. Prefer `Skeleton` for tables/lists. |
| `Skeleton` | `rows?, columns?` | Shimmer placeholder rows. |
| `Empty` | `title, message?, children?` (actions) | Halftone empty state. |
| `ErrorState` | `error?, onRetry?, title?` | Friendly text for `ApiError` (404, 5xx) and network errors. |
| `FreshnessBadge` | `asOf, staleAfterSec? = 1200, label? = 'Delayed'` | "Delayed · as of 14:32:05 WIB", red "Stale" past the threshold. |
| `Num` | `value, format?: price \| idr \| pct \| compact \| number, decimals?, signed?, colored?, colorBy?, arrow?` | Tabular figures; `colored` uses the value's sign, `colorBy` another value (e.g. color a price by its change). `pct` takes percent values (0.42 → 0.42%). |

`formatNum(value, format, { decimals, signed })` and `direction(value)` are the plain-string versions.

## Data

- `api<T>(path, init)` from `api/client.ts`: JSON fetch under `/api`, throws `ApiError { status, message, body }`.
  `body` in init is JSON-encoded. Query defaults: staleTime 30s, retry 1, no refetch on focus.
- `useQuotes(codes)` from `api/signalr.ts`: live quotes `{ [code]: { code, last, change, changePct, volume, time } }`
  from `/hubs/quotes` (Subscribe/Unsubscribe are ref-counted). `useHubEvent(name, handler)` for other events,
  `useHubStatus()` for the connection state. Never throws when the hub is down; it keeps retrying.

## Theme

Tokens in `theme/tokens.css`: `--bg --panel --panel-2 --panel-hover --border --border-strong --amber --text --muted
--faint --crimson --up --down --font-mono --font-display`. Global classes: `.up .down .flat .muted .value .display
.halftone`. Crimson is brand only; use `--up`/`--down` for market moves.
