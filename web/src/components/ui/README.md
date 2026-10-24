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

Visual rules live in `docs/design/direction.md` §6: black panes, `--raise-1` header bands, sentence-case labels in the
condensed face, uppercase mono codes, no radius or shadows. Motion follows `docs/design/v2-motion.md`.

| Component | Props | Notes |
|---|---|---|
| `Panel` | `title, subtitle?, actions?, padded?, boxed?, className?, bodyClassName?` | 20px `--raise-1` header with an amber sentence-case title; `boxed` adds a 1px border for nested panels; body scrolls. |
| `Table<T>` | `columns: Column<T>[], rows, rowKey, onOpen?, initialSort?, autoFocus?, empty?` | 18px mono rows, sticky header, click header to sort, `j/k/↑/↓/Home/End/PgUp/PgDn`, `Enter` → `onOpen(row)`. Column: `key, header, render?, sortValue?, sortable?, align?, width?, bar?`. `bar(row) → { pct 0–100, tone?: amber \| up \| down }` draws a depth bar from the right edge. The first rows ease in once when data arrives. Right-aligned columns sort descending first. |
| `Badge` | `tone?: default \| amber \| crimson \| up \| down \| muted \| solid, title?` | 16px outlined label, sentence case. Never pulses; a direction badge carries a sign or arrow. |
| `Button` | `variant?: default \| primary \| ghost, active?` + button attrs | 22px. `primary` is amber, `ghost` amber text, `active` the selected segment (select blue). Adjacent buttons overlap 1px, so a row of them reads as a segmented control. Put a `<kbd>` after the label for its shortcut. |
| `Spinner` | `size?, label?, center?` | Indeterminate amber bar on a `size × 3`px track plus a label (default "Loading"). Prefer `Skeleton` for tables/lists. |
| `Skeleton` | `rows?, columns?` | Static row-height bars, shown after 150ms so fast loads never flash. |
| `Empty` | `title, message?, children?` (actions), `tone?: 'error'` | Terminal message at the top-left: amber title, prose message, action row; `error` adds a crimson left rule and announces itself. |
| `ErrorState` | `error?, onRetry?, title?` | `ERR 503` code + title, friendly text for `ApiError` (404, 5xx) and network errors, primary Retry. |
| `FreshnessBadge` | `asOf, staleAfterSec? = 1200, label? = 'Delayed'` | "Delayed 14:32:05" (WIB) muted while fresh, amber "Stale" past the threshold, crimson at 3x. |
| `Num` | `value, format?: price \| idr \| pct \| compact \| number, decimals?, signed?, colored?, colorBy?, arrow?, flash?` | Tabular figures; `colored` uses the value's sign, `colorBy` another value (e.g. color a price by its change). `flash` tints the cell for one tick on a live change. `pct` takes percent values (0.42 → 0.42%). |

`formatNum(value, format, { decimals, signed })` and `direction(value)` are the plain-string versions.

## Data

- `api<T>(path, init)` from `api/client.ts`: JSON fetch under `/api`, throws `ApiError { status, message, body }`.
  `body` in init is JSON-encoded. Query defaults: staleTime 30s, retry 1, no refetch on focus.
- `useQuotes(codes)` from `api/signalr.ts`: live quotes `{ [code]: { code, name, price, change, changePct, prevClose, open, high, low, volume, value, time, source } }` (the `/api/quotes` shape)
  from `/hubs/quotes` (Subscribe/Unsubscribe are ref-counted). `useHubEvent(name, handler)` for other events,
  `useHubStatus()` for the connection state. Never throws when the hub is down; it keeps retrying.

## Theme

Tokens in `theme/tokens.css` (direction.md §3.1): surfaces `--canvas --bg --raise-1/2/3 --line --line-strong`, text
`--amber --text --muted --faint`, hues `--crimson --cyan --select --up --down`, fonts `--font-mono --font-label
--font-prose`, sizes `--fs-2xs … --fs-2xl`, space `--s-0 … --s-6`, motion `--dur-1 … --dur-4 --ease-*`. Global
classes: `.up .down .flat .muted .value .cyan .label .prose .display .cell-ara .cell-arb .cell-halt`, keycaps `kbd`,
`.key-fn`, `.key-go`, `.key-cancel`. Each hue has one job: crimson marks focus and the brand, up/down mark direction
only, cyan marks something you can navigate to.
