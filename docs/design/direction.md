# Sumire v2 design direction: "Night Desk"

This is the implementable spec for the v2 frontend. It replaces the v1 "Stolen Terminal" comic direction completely. Inputs: [v2-references.md](v2-references.md) (visual research), [v2-motion.md](v2-motion.md) (motion system, normative), the board decision **V2 RULES**, and implementation-plan.md §7. Where this file and v2-motion.md overlap, v2-motion.md wins on motion details and this file wins on everything visual.

## 1. Concept

**Night Desk** is a terminal on a trading desk after the lights go down: black glass, amber phosphor, white numbers that only move when the market does. It is faithful to Bloomberg in structure (security bar, function codes, colored keys, dense black panes) and differs from it through craft, in three places:

1. **The security bar.** The top bar shows the loaded ticker in a solid amber field with black text, and the focused function's name on a crimson slab. It is the one loud object on screen and the place where the brand lives.
2. **Numbers that report.** Prices flash only the cell that ticked, aggregates tween, the IHSG hero rolls its digits, and charts draw in once. Nothing else moves.
3. **A strict color grammar.** Every hue has one job (section 3.2), so the screen stays quiet until something is abnormal. This is the "quiet and dark" cockpit principle.

Sumire appears only as the app icon: a favicon and a 16px mark in the top bar. There is no mascot, comic device or Persona texture anywhere.

What makes it not generic: true black panes on a slightly lighter canvas (not tinted near-black everywhere), Bloomberg's own grammar of amber values, white references and cyan group labels, condensed sentence-case labels next to uppercase mono codes, and motion that only appears when data changes.

## 2. Removal list (v1 to delete)

Everything below goes. A component that is removed is deleted with its CSS module and its exports.

| Remove | Where |
|---|---|
| Ransom-note `CutText`, `Starburst`, calling-card alert styling, "Violet alert" copy | components/ui, components/alerts, any function using them |
| Halftone / screentone (`.halftone`, `--tone-*`) | global.css, Empty, any module |
| Cut-paper `clip-path` slants, slanted panel headers | Panel, Pane, TopBar, Button, Badge, CommandLine, functions |
| Ribbon on the active pane (`.ribbon`) | Pane |
| Offset plates (`--plate*`, hard offset `box-shadow`) | everywhere |
| `DM Serif Display`, `Anton` (`--font-cut`, `--font-display`) | index.html, tokens, modules |
| `--ink`, `--paper` usages that draw paper or ink devices | everywhere |
| Corner mascot (`components/mascot` if present), the `m` shortcut, its HELP and overlay entries | App.tsx, ShortcutOverlay, functions/help/shortcuts.ts |
| Glasses spinner (the glasses now belong to the icon only) | components/ui/Spinner |
| "VIOLET" codename in the top bar | TopBar |
| `--ease-snap`, `slash-in`, `ribbon-drop`, `wipe-down`, `wipe-in`, shimmer | see v2-motion.md §12 |

Copy that used the codename becomes plain: "Alert: BBCA crossed MA200".

## 3. Tokens

### 3.1 Full token block (`web/src/theme/tokens.css`)

This is the complete target block. Area A writes it. The legacy aliases at the bottom keep unmigrated modules compiling, and the last area to finish deletes them (section 12).

```css
:root {
  /* ── surfaces (neutral, not warm) ───────────────────────── */
  --canvas: #0c0c0c;        /* app background, pane gutters */
  --bg: #000000;            /* pane body, tables, inputs */
  --raise-1: #121212;       /* pane header strip, table header band, hover row */
  --raise-2: #1a1a1a;       /* buttons, popovers, suggestion list */
  --raise-3: #242424;       /* button hover, pressed keycap */
  --line: #222222;          /* hairlines between rows-groups, pane borders */
  --line-strong: #3a3a3a;   /* control borders, focused-control border */
  --line-faint: #161616;    /* optional rule every 5 rows */

  /* ── phosphor ─────────────────────────────────────────── */
  --amber: #ffa028;         /* labels, codes, primary values, chrome text */
  --amber-hi: #ffc266;      /* hover on amber, caret */
  --amber-dim: #b8741a;     /* secondary amber: axis text, units */
  --amber-field: #ffa028;   /* security bar field (text on it is #000) */

  /* ── text ─────────────────────────────────────────────── */
  --text: #f2f2f2;          /* values, reference numbers, headlines */
  --muted: #8c8c8c;         /* column headers, descriptions (6.6:1 on #000) */
  --faint: #6e6e6e;         /* footnotes, disabled (4.1:1 – non-essential only) */
  --on-fill: #000000;       /* text on amber / yellow fills */

  /* ── semantic hues (one job each, see 3.2) ────────────── */
  --crimson: #c8102e;       /* focus, active pane, brand */
  --crimson-hi: #e8203f;    /* focus text on black, error code */
  --crimson-slab: #8e0c22;  /* function title slab, white text on it (7.9:1) */
  --crimson-wash: #24060c;  /* keyboard-selected row / suggestion */
  --cyan: #4fa3e0;          /* navigable links, in-table group labels */
  --select: #1f5faf;        /* selected tab / chip / segment (white text) */
  --up: #22c55e;
  --down: #f43f5e;
  --flat: #8c8c8c;
  --warn: #ffa028;          /* = amber; caution reads as amber */

  /* ── Bloomberg keys ───────────────────────────────────── */
  --key-sector: #f5c400;    /* yellow: functions and markets, black legend */
  --key-sector-edge: #8a6d00;
  --key-go: #2e7d32;        /* GO / run / confirm, white legend */
  --key-go-edge: #14431a;
  --key-cancel: #c8102e;    /* cancel / Esc / stop, white legend */
  --key-cancel-edge: #6e0918;
  --key-plain: #2a2a2a;     /* every other key, #f2f2f2 legend */
  --key-plain-edge: #000000;

  /* ── charts ───────────────────────────────────────────── */
  --chart-top: #10151c;
  --chart-bottom: #07090c;
  --chart-grid: #1c232c;
  --chart-axis: #8c8c8c;

  /* ── type ─────────────────────────────────────────────── */
  --font-mono: 'JetBrains Mono', 'IBM Plex Mono', ui-monospace, 'Cascadia Mono', Consolas, monospace;
  --font-label: 'IBM Plex Sans Condensed', 'Arial Narrow', 'Roboto Condensed', sans-serif;
  --font-prose: 'IBM Plex Sans', 'Segoe UI', system-ui, sans-serif;

  --fs-2xs: 10px;   /* axis ticks, keycap legends, lamp labels */
  --fs-xs: 11px;    /* column headers, badges, pane-strip labels */
  --fs-sm: 12px;    /* table data (the default data size) */
  --fs-md: 13px;    /* command input, prose, buttons */
  --fs-lg: 15px;    /* pane hero line (DES name + price) */
  --fs-xl: 20px;    /* quote tile values, MKT secondary heroes */
  --fs-2xl: 28px;   /* IHSG hero only */

  --lh-data: 1.5;   /* 12px → 18px rows */
  --lh-prose: 1.55;

  --track-label: 0.01em;   /* condensed labels */
  --track-code: 0.04em;    /* uppercase mono codes */
  --track-hero: -0.01em;   /* 20px and up */

  /* ── space (4px base) ─────────────────────────────────── */
  --s-0: 2px;
  --s-1: 4px;
  --s-2: 8px;
  --s-3: 12px;
  --s-4: 16px;
  --s-5: 24px;
  --s-6: 32px;

  /* ── geometry ─────────────────────────────────────────── */
  --row-h: 18px;           /* dense rows everywhere */
  --row-h-comfy: 22px;     /* suggestion list, settings forms */
  --strip-h: 20px;         /* pane header strip, Panel header */
  --topbar-h: 32px;
  --tape-h: 24px;
  --status-h: 22px;
  --rail-w: 208px;
  --gutter: 2px;           /* gap between panes; shows --canvas */
  --radius: 0;
  --radius-key: 1px;       /* keycaps only */

  --focus-ring: 0 0 0 1px var(--crimson);
  --focus-ring-inset: inset 0 0 0 1px var(--crimson);

  /* ── motion (from v2-motion.md §2) ────────────────────── */
  --dur-1: 80ms;
  --dur-2: 120ms;
  --dur-3: 160ms;
  --dur-4: 240ms;
  --ease-out: cubic-bezier(0.2, 0.8, 0.2, 1);
  --ease-in: cubic-bezier(0.4, 0, 1, 1);
  --ease-move: cubic-bezier(0.3, 0, 0, 1);
  --ease-decay: cubic-bezier(0.05, 0.7, 0.1, 1);
  --ease-wipe: cubic-bezier(0.65, 0, 0.35, 1);
  --flash-up: rgba(34, 197, 94, 0.32);
  --flash-down: rgba(244, 63, 94, 0.32);

  /* ── legacy aliases (delete in the cleanup step, section 12) ── */
  --panel: var(--bg);
  --panel-2: var(--raise-2);
  --panel-hover: var(--raise-1);
  --border: var(--line);
  --border-strong: var(--line-strong);
  --amber-deep: var(--line-strong);
  --crimson-bright: var(--crimson-hi);
  --crimson-dim: var(--crimson-wash);
  --crimson-ink: var(--crimson-slab);
  --danger: var(--down);
  --ink: #000000;
  --paper: var(--text);
  --key-fn: var(--key-sector);
  --key-fn-edge: var(--key-sector-edge);
  --key-dark: var(--key-plain);
  --key-dark-edge: var(--key-plain-edge);
  --font-display: var(--font-label);
  --fs-d1: var(--fs-md);
  --fs-d2: var(--fs-xl);
  --fs-d3: var(--fs-2xl);
  --gap: var(--s-1);
  --plate: none;
  --plate-lg: none;
  --plate-crimson: none;
  --amber-glow: transparent;
  --ease: var(--ease-out);
  --dur-fast: var(--dur-1);
  --dur: var(--dur-2);
  --dur-slow: var(--dur-4);
}
```

`--font-cut`, `--tone-dot` and `--tone-size` are not aliased. Their few usages are removed outright with the v1 devices.

Approximate contrast on `#000`: amber ~10:1, text ~18:1, muted ~6:1, cyan ~7:1, up ~9:1, down ~5.5:1, crimson-hi ~4.6:1. White on `--select` is ~6.5:1, white on `--crimson-slab` is ≥ 7:1, and black on `--amber-field` is ~10:1. Area A re-checks these with a contrast tool, and any value under 4.5:1 for text is a bug.

### 3.2 Color grammar

| Hue | Means | Never used for |
|---|---|---|
| amber | labels, function codes, tickers, primary value on a screen, caution | errors, direction |
| white `--text` | live values, reference values, headlines | labels |
| muted grey | column headers, descriptions, units | values the user came for |
| cyan | something you can navigate to (links, related functions) and in-table group labels | status |
| select blue | the chosen tab, chip or segment within a screen | focus, links |
| crimson | where the keyboard is: focused pane, focused control, keyboard-selected row; the brand mark and title slab; errors (with a label) | price direction |
| up / down | price and change direction only, always with a sign or arrow | buttons, errors, status lamps (lamps use their own rule, 6.4) |

Quiet and dark: in a normal state a screen is amber, white and grey only. Color appears for direction, the focused pane, a selected tab, and abnormal states (stale, down, ARA/ARB, alert).

### 3.3 Type

Google Fonts link in `web/index.html` (replaces the current one):

```html
<link href="https://fonts.googleapis.com/css2?family=IBM+Plex+Sans+Condensed:wght@400;500;600&family=IBM+Plex+Sans:wght@400;500&family=JetBrains+Mono:wght@400;500;700&display=swap" rel="stylesheet" />
```

| Family | Role | Rules |
|---|---|---|
| JetBrains Mono | every number, ticker, function code, keycap legend, command input, table cells, news headlines in lists | `font-variant-numeric: tabular-nums slashed-zero` globally; codes and tickers uppercase with `--track-code`; weights 400 (data), 500 (emphasis), 700 (codes in strips and keycaps only) |
| IBM Plex Sans Condensed | chrome labels: pane names, column headers, buttons, badges, panel titles, status labels | **sentence case** (`Price chart`, `Last`, `Chg %`), 500 weight, `--track-label`; never all caps |
| IBM Plex Sans | reading text: insight paragraphs, DES business description, ASK answers, error messages longer than a line | 13px / 1.55, 400, `max-width: 72ch` |

The case rule is the typographic signature. Uppercase is reserved for things that are codes (BBCA, GP, IHSG, LQ45, WIB). Words people read are in sentence case in the condensed face. This mirrors Bloomberg's own screens ("Govt Benchmark", "Last Yield") and avoids the tracked-caps eyebrow look.

Numbers:
- Right-align every numeric column. Decimals within a column use one fixed count (prices: IDX prices are integers; percentages: 2 decimals; ratios: 2).
- Negative numbers use the real minus `−` (U+2212) through `formatNum`, and positive changes get an explicit `+`.
- Sizes 20px and up use `--track-hero`. The hero is the only place that uses 500 weight at large size, and no number is bold.

## 4. Surfaces and separators

- The **canvas** (`--canvas`) is what shows between panes: panes are separated by a `--gutter` (2px) of canvas, not by borders. This makes each pane read as a tile of black glass.
- **Panes** are `--bg` with a 1px `--line` border. The focused pane keeps the same border and gets the crimson top hairline (section 5).
- **Inside a pane**, separate by alignment and spacing first. Use a 1px `--line` rule only between sections (for example between the DES header and the stats grid), never between rows. An optional `--line-faint` rule every 5 rows is allowed in long numeric tables (EQS, W).
- **Header bands** (pane strip, Panel header, table header) are `--raise-1`. No gradients, except the chart background.
- **Popovers** (suggestions, menus, shortcut overlay) are `--raise-2` with a 1px `--line-strong` border and no shadow. The overlay backdrop is `rgba(0,0,0,.6)` with no blur.
- No `border-radius` anywhere except keycaps (`--radius-key`). No `box-shadow` except focus rings (which are outlines; see 9.4).

## 5. Shell

### 5.1 Layout

```
┌──────────────────────────────────────────────────────────────────────────────┐ 32px
│▣ Sumire │[BBCA ▾]│ bbca gp_                         │ Price chart ││▥│14:32:07 WIB│● Live│
├──────────┬───────────────────────────────────┬────────────────────────────────┤
│Watchlist │1 GP  BBCA  Price chart       ‹ ›  │2 INS  BBCA  Insight      ‹ ›   │ 20px strip
│ Core   ▾ │                                   │                                │
│BBCA  9,875 +0.51%                            │                                │
│BBRI  4,310 −1.14%                            │                                │
│▸ Banks (cyan group)                          │                                │
│          ├───────────────────────────────────┼────────────────────────────────┤
│          │3 MKT  Market                 ‹ ›  │4 N  Headlines            ‹ ›   │
│          │                                   │                                │
├──────────┴───────────────────────────────────┴────────────────────────────────┤
│ IHSG 7,312.40 +0.42% ▲ │ LQ45 951.2 −0.10% ▼ │ USDIDR 16,250 │ … (tape)        │ 24px
├──────────────────────────────────────────────────────────────────────────────┤
│ ■ Feed 14:31  ■ Hub  ■ DB  ■ EOD  │ Research 9/20 · runner · model           │ 22px
└──────────────────────────────────────────────────────────────────────────────┘
```

Everything is left-aligned. Numbers are right-aligned in their columns. Nothing in the shell is centered except the boot sequence.

### 5.2 Top bar: the security bar (`shell/TopBar.tsx`, `CommandLine.tsx`)

Height `--topbar-h` (32px), background `--bg`, 1px `--line` bottom border. Left to right:

1. **Brand.** A `SumireMark` at 16px plus the wordmark "Sumire" in Plex Sans Condensed 600, 13px, `--text`, with `--s-2` gap and `--s-3` horizontal padding. A 1px `--line` divider follows. The "VIOLET" codename is removed.
2. **Loaded security field.** When the focused pane has a ticker, a solid `--amber-field` block shows it: 24px tall, `--s-2` padding, JetBrains Mono 700 13px, `--on-fill`, `--track-code`, followed by a small `▾` glyph. When there is no ticker, the block is absent (no empty amber box). It is display-only and not focusable.
3. **Command input.** Flexible width, `--bg`, no border of its own, mono 13px, typed text in `--amber`, caret `--amber-hi` (native caret, no blinking block). Placeholder in `--faint`: `Ticker and function, e.g. BBRI GP`. When focused, the whole input region gets a 1px inset crimson outline (`--focus-ring-inset`).
4. **Function slab.** On the right of the input, a block with `--crimson-slab` background shows the focused pane's function name (`registry[code].name`, e.g. "Price chart") in Plex Sans Condensed 500 13px white, padded `--s-3`, 24px tall. This and the amber field are the two loud objects in the app.
5. **Layout button.** A 24×24 square button showing the 1/2/4 pane glyph in `--muted` and amber on hover. Its title and behavior are unchanged.
6. **Clock.** `14:32` in mono 13px `--text`, then `:07` in `--muted`, then `WIB` in Plex Sans Condensed 11px `--muted`.
7. **Session pill.** A 6px square lamp plus the label, in sentence case: `Live`, `Pre-open`, `Break`, `Pre-close`, `Post`, `Closed`. Lamp colors: `Live` up green with the live pulse (v2-motion §7); `Break`, `Pre-open`, `Pre-close` and `Post` amber; `Closed` `--faint`. There is no pill border, just the lamp and the text.

**Suggestion list** (CommandLine): a popover below the input, aligned to its left edge, min width 420px, `--raise-2`, 1px `--line-strong`. Rows are `--row-h-comfy` (22px) and form three columns:

| Column | Style |
|---|---|
| code / ticker | mono 700 12px `--amber`, 6ch, uppercase |
| name | Plex Sans Condensed 500 12px `--text` |
| hint (sector, example) | Plex Sans Condensed 400 11px `--muted`, right side, truncated |

The active row is shown by one shared highlight bar (v2-motion §7): `--crimson-wash` background plus a 2px crimson left edge. A footer row shows `Tab accept · ↑↓ move · Enter run · Esc close` with each key as a plain keycap (6.7). Errors appear as one line under the input inside the popover: `--crimson-hi` code `ERR` then the message in `--text`. The input runs the nudge animation.

### 5.3 Pane (`shell/Pane.tsx`)

**Strip** (`--strip-h` 20px, `--raise-1`, 1px `--line` bottom):

```
│1│ GP │ BBCA │ 1Y D       Price chart                         ‹  › │
 ^   ^    ^      ^          ^                                   ^
 |   |    |      args       name (label face, muted)            nav
 |   |    ticker (mono 500, --text)
 |   code (mono 700, --amber, keycap style when focused)
 index: 20×20 cell, mono 11px --muted on --bg; focused: --crimson fill, white digit
```

- Index cell: a square flush with the left edge. It is the only filled crimson block on a pane, and only when the pane is focused.
- Code: unfocused, plain amber mono 700. Focused, it renders as a `key-sector` keycap (yellow, black legend), which ties the keyboard to the screen.
- Focus hairline: a 2px crimson line on the top edge of the strip, `scaleX` 0 to 1 from the left (v2-motion §7). It replaces the ribbon.
- Nav buttons: 20×20, `‹ ›` in `--muted`, `--amber` on hover, `--faint` and `cursor: default` when disabled.
- **Body** is `--bg` with `overflow: auto` and thin scrollbars (6px thumb `--line-strong` on transparent track). Content enters with `pane-in` as in v2-motion §7.

**Related functions footer (optional, self-contained).** A pane may render a 20px footer listing 3-5 related functions as cyan links: `GP Price chart   FA Fundamentals   N Headlines`. The code is mono 700 `--text`, and the name is in the label face in `--cyan`. Clicking a link runs `<ticker> <code>` in the same pane. It has no number shortcuts, because digits already belong to the chart. Area B owns it, with the map in `shell/related.ts`.

### 5.4 Status bar (`shell/StatusBar.tsx`)

Height `--status-h` (22px), `--canvas` background, 1px `--line` top border, mono 10px.

Left: **lamps**. Each lamp is a 6×6 square plus a label in Plex Sans Condensed 500 11px `--muted`, with `--s-3` between lamps:

| Lamp | Green | Amber | Red (`--crimson-hi`) | Grey |
|---|---|---|---|---|
| `Feed` + age (`Feed 14:31`) | fresh | stale (FreshnessBadge rule) | very stale | no data |
| `Hub` | connected (pulse while session is Live) | reconnecting | disconnected | — |
| `DB` | ok | — | down (title has the error) | unknown |
| `EOD` | bars current | running or stale | last run failed | — |
| `API` | — | — | `/api/status` failed | — |

Lamp green is `--up`. This is the one place green means "healthy" rather than "up", and it is acceptable because a lamp is a square, never a number. The lamp label stays muted, and only the square carries color. The hover title keeps the current explanatory text.

Right: **research**: `ResearchUsage` (for example `Research 9/20`), then runner and model in `--muted`. The running indicator is a 6px amber square, static, with no spin.

### 5.5 Ticker tape (`components/tape/TickerTape.tsx`)

24px, `--bg`, 1px `--line` top and bottom. Each item:

```
IHSG 7,312.40 +0.42% ▲ │
```

- The code is mono 700 12px `--amber`. The price is mono 400 12px `--text` with `<Num flash>`. The change is mono 12px in up or down with sign and arrow. Inside an item the gap is `--s-2`. Items are separated by a 1px × 12px `--line` divider with `--s-3` on each side.
- Index and macro items (IHSG, LQ45, USDIDR, GOLD…) come first and use the same style. There is no special badge.
- Motion is constant-speed scroll at 48 px/s, pausing on hover or focus. Under reduced motion it becomes a static scrollable strip (v2-motion §5).
- The left and right edges have no gradient fade. The strip is clipped hard, like a hardware display.

### 5.6 Watchlist rail (`components/watchlist/WatchlistRail.tsx`)

Width `--rail-w` (208px), `--bg`, 1px `--line` right border.

- Header (`--strip-h`, `--raise-1`): "Watchlist" in the label face 500 12px `--amber`, followed by the list name in a native `select` styled flat (`--raise-2`, 1px `--line-strong`, 18px tall).
- Rows (`--row-h`, 18px, padding 0 `--s-2`) form three columns: code (mono 500 12px `--amber`, 6ch), last (`<Num flash>`, `--text`, right-aligned), and chg% (signed, colored, right-aligned, 7ch).
- Group labels: cyan label-face rows (`Banks`, `Coal`), 18px, with no box and a `--s-2` top margin.
- The current ticker is shown by the shared 2px crimson marker on the left (v2-motion §8). Hover is `--raise-1`. The keyboard-selected row is `--crimson-wash`.
- Empty: "No tickers yet. Type a ticker and press `w`." in muted text, with `w` as a plain keycap.

### 5.7 Boot sequence (`components/boot/Boot.tsx`)

Follow v2-motion §9 exactly. Visual layout, centered on `--bg`:

```
            ▣  Sumire                       (mark 32px + label 600 20px --text)

            Link   /hubs/quotes .......... OK
            Feed   Yahoo · 247 symbols ... OK
            IDX    Session 2 13:30–15:50 WIB
            DB     ....................... Offline
```

The lines are mono 12px. The key column is `--amber` (8ch), the dot leaders are `--faint`, and the result is `--up` for OK, `--amber` for Offline or Retry, and `--text` for information. It is skipped under reduced motion and on any key press. It runs once per session (`sessionStorage['sumire.boot']`).

## 6. UI kit (`components/ui/`)

Props and exports are unchanged unless stated.

### 6.1 Panel

- Header: `--strip-h`, `--raise-1`, `--s-2` horizontal padding, flex row. The title is Plex Sans Condensed 600 12px `--amber`, in sentence case. The subtitle is the label face 400 11px `--muted`, `--s-2` after the title. Actions are pushed right, as small buttons or segment controls.
- There is no slant, ribbon or underline. The panel has a 1px `--line` border only when it is nested in a pane body next to other panels (class `.boxed`); the default is borderless, with the header band doing the separation.
- Body: `padded` means `--s-2 --s-3`.

### 6.2 Table

- Header row: `--row-h`, `--raise-1`, label face 500 11px `--muted`, sentence case. Numeric columns are right-aligned. The sortable column header shows `▲`/`▼` in `--amber`, and the sorted column's header text becomes `--text`.
- Body rows: `--row-h` (18px), mono 12px, padding 0 `--s-2`. There is no zebra striping and no row borders. Hover is `--raise-1`. The keyboard-selected row is `--crimson-wash` plus a 2px crimson inset on the left (`box-shadow: inset 2px 0 0 var(--crimson)`). Selection never changes text colors.
- Cell defaults: ticker or code cells are `--amber` 500, values are `--text`, changes are signed and colored, units and secondary data are `--muted`.
- Group rows: a full-width cell with cyan label-face 500 12px text and a `--s-2` top gap. These replace boxed sub-tables.
- **Depth bars (optional per column)**: for volume, value, score or strength columns, draw a bar behind the number with `background: linear-gradient(to left, <tint> var(--pct), transparent var(--pct))` where the tint is `rgba(255,160,40,.12)` (amber) by default, or up or down at `.14` for signed values. The bar grows from the right edge. Expose it as `Column.bar?: (row) => { pct: number; tone?: 'amber'|'up'|'down' }`, an additive prop.
- Row enter stagger and FLIP reorder per v2-motion §8.

### 6.3 Num

Unchanged visually: tabular mono with sign and color for `direction`. It gains the `flash` prop, `useTweened` and `Odometer` (v2-motion §4). Area B owns these files.

### 6.4 Badge

Height 16px, padding 0 `--s-1`, label face 500 11px, sentence case, 0 radius, 1px border. Tones:

| Tone | Text | Border | Fill |
|---|---|---|---|
| default | `--text` | `--line-strong` | none |
| amber | `--amber` | `--amber-dim` | none |
| crimson | `--crimson-hi` | `--crimson` | none |
| up / down | `--up` / `--down` | same at 50% | none |
| muted | `--muted` | `--line` | none |
| solid | `--on-fill` | none | `--amber` |

Badges never pulse. A badge that carries a direction must also carry a sign or arrow in its text.

**Filled state cells** (ARA, ARB, hit target, suspended) are a cell style rather than a badge: a full-cell fill with text at AA contrast. ARA is `--up` fill with black text, ARB is `--down` fill with black text, and suspended is `--raise-3` fill with `--text`. Expose `.cell-ara`, `.cell-arb` and `.cell-halt` as globals in global.css (area A).

### 6.5 Button

| Variant | Rest | Hover | Active / pressed |
|---|---|---|---|
| default | `--raise-2` fill, 1px `--line-strong`, `--text` | `--raise-3` | `transform: translateY(1px)` |
| primary | `--amber` fill, `--on-fill` text, no border | `--amber-hi` | translateY(1px) |
| ghost | transparent, `--amber` text | `--raise-1` fill | translateY(1px) |
| `active` prop | `--select` fill, white text, 1px `--select` border | same | — |

All variants are 22px tall, padded 0 `--s-2`, label face 500 12px, sentence case. Disabled is `--faint` text with no hover and `cursor: default`. Focus-visible is a 1px crimson outline with 1px offset. The press state applies on `:active` with no transition (v2-motion §3). A button with a key binding shows its keycap after the label: `Refresh  [r]`.

**Segmented control** (timeframes, presets): adjacent default buttons with -1px overlap, where the selected one uses `active`. This is how GP's `1D 1W 1M 3M 1Y 5Y` and EQS presets look.

### 6.6 Empty, ErrorState, Spinner, Skeleton

These are terminal messages, not illustrations. They are left-aligned at the top-left of the pane body, padded `--s-3`, with a maximum width of 60ch.

```
No forecast for BBCA yet                       ← label face 600 13px --amber
Forecasts run after the daily close (16:15 WIB). Queue one now or check back.
                                               ← prose 13px --muted
[INS!] Queue research   [Esc] Back             ← keycaps + label-face actions
```

- **Empty**: as above. There is no halftone, slash or icon. `children` render as the action row, using Buttons or keycap plus text.
- **ErrorState** (`tone="error"`): a 2px crimson left border on the block. The first line shows `ERR 503` in mono 700 `--crimson-hi`, then the title in `--text`. The message is in prose `--text` (full contrast, `role="alert"`). Retry is a primary Button. The copy stays the plain, specific messages already in `describe()`.
- **Spinner**: the glasses are removed. It is an indeterminate 2px × 48px `--amber` bar moving across a 120px `--line` track (transform `translateX`, 900ms linear, iterating only while mounted), followed by the label (default "Loading") in the label face `--muted`. The `size` prop scales the track width (`size * 3px`). Under reduced motion it is a static bar at 30% plus the label. This is the one allowed loop besides the live lamp, and it exists only while something is loading.
- **Skeleton**: static `--raise-1` bars at row height with the widths of the real columns. It has no shimmer and appears after 150ms (v2-motion §8).

### 6.7 Keycaps (`kbd`, global classes)

16px tall, min width 16px, padding 0 `--s-1`, mono 700 10px, `--radius-key`, and a 2px bottom edge in the `-edge` color (the only "depth" in the UI).

| Class | Fill | Legend | Used for |
|---|---|---|---|
| `kbd` (plain) | `--key-plain` | `--text` | all ordinary keys: `/`, `Tab`, `j`, `Alt` |
| `.key-fn` / `.key-sector` | `--key-sector` | black | function codes (GP, DES, MKT) and the focused pane code |
| `.key-go` | `--key-go` | white | Enter / run / confirm |
| `.key-cancel` | `--key-cancel` | white | Esc / cancel / stop |

When a shortcut fires (for example `Esc` in HELP or the command footer), the matching visible keycap may invert for 80ms (fill becomes `--text`, legend becomes the fill color). This is press feedback, not decoration. Optional: area C does it in CheatSheet, and area B in the command footer.

### 6.8 FreshnessBadge

The same thresholds are kept. Copy: `Delayed 14:32:05` (muted), `Stale 14:12:05` (amber), `Stale 13:40:00` (crimson), `No data yet` (muted). The time is mono and the word is in the label face.

## 7. Alerts, insight, research

### 7.1 Alert toasts (`components/alerts/AlertToaster.tsx`)

Toasts are stacked bottom-right above the status bar, 320px wide, `--raise-2`, with a 1px `--line-strong` border and a 2px left bar colored by kind: alert rule hit `--amber`, ARA/ARB `--up`/`--down`, research done `--cyan`, error `--crimson`.

```
Alert · BBCA crossed MA200                 14:32
9,875  +1.02% ▲   MA200 9,790                     ← mono 12px
[Enter] Open GP   [Esc] Dismiss
```

The first line uses the label face 600 12px with the kind word in `--amber`. Motion follows v2-motion §3 (120ms in, 80ms out). There is no calling-card shape, stamp or violet. A toast auto-dismisses after 8s unless it is hovered or focused.

### 7.2 Insight (`components/insight/*`, `functions/ins/InsightView.tsx`)

- The headline is prose 500 15px `--text`. The meta line holds the ticker (amber mono), the generated time, the runner, and a FreshnessBadge.
- The summary is prose 13px `--text` with a `max-width` of 72ch.
- **Driver chain** rows (`factor → ticker`) are mono 12px: the factor in `--cyan`, a `→` in `--faint`, the ticker as a `TickerChip`, then the live move with `<Num flash>`. These are 18px rows forming one table, not cards.
- `TickerChip`: mono 500 12px `--amber`, 1px `--line-strong` border, 16px tall, and `--raise-1` on hover. Click or Enter opens DES.
- Source chips: label face 11px `--muted` showing `Publisher · 23 Oct`, with a 1px `--line` border. They are links, so they turn `--cyan` on hover.
- `SentimentMeter`: a 120×6px track in `--line`, with a single 2px vertical needle in `--text` at the score position, and the endpoints labeled `Bearish` / `Bullish` in 10px `--muted`. The score number sits to the right, signed and colored. There are no gradients.

### 7.3 Research (`components/research/*`)

`ResearchUsage` is mono 11px `9/20` with a 40×4px usage bar (fill `--amber`, or `--crimson` at 90% or more of the cap) using `scaleX`. The job list in RQ is a standard Table with status in the label face: `Queued` muted, `Running` amber with a static square, `Done` `--text`, `Failed` `--crimson-hi`.

## 8. Function screens (`web/src/functions/`)

Shared rules: every screen opens with a **hero line**: the ticker in amber mono 500 15px, the name in the label face 500 15px `--text`, then the last price with `<Num flash>` and the change. After that come dense sections with Panel headers. Sections are separated by `--s-3` and a 1px `--line` rule. There are no cards inside panes.

| Screen | Specifics |
|---|---|
| MKT | IHSG hero 28px with `Odometer` plus a tweened change line. Breadth bar: one 6px track, green advancing `scaleX` from the left, red declining from the right, unchanged as a gap, counts above it in mono. Movers tables with FLIP. Macro strip as a dense 2-column table, not tiles. Sparkline 1px `--text` line, shutter reveal, no fill under 40px height. |
| DES | Hero line, then a two-column grid: the left holds the description (prose) and the profile; the right holds a "Snapshot" label/value table (label face `--muted` left, mono `--text` right, 18px rows, group labels cyan). |
| GP | The chart fills the pane. A toolbar row above it holds the segmented timeframes, the indicator menu button and `f`/`v` toggles as small buttons with keycaps. The legend is overlaid top-left on the chart in mono 11px: `BBCA  O 9,850 H 9,900 L 9,825 C 9,875`. The forecast cone uses an amber line plus a `rgba(255,160,40,.10)` band, never crimson. |
| FA | Valuation table: company vs sector median, with the deviation as a depth bar (up or down tint). |
| FCST | Ranges table, signal-score breakdown as depth bars, and the track record as a mono hit-rate table. |
| INS | Section 7.2. `INS!` shows a queued state as an Empty with the job id and `[RQ] Open queue`. |
| REL | Graph nodes: 1px `--line-strong` square nodes with amber mono labels. The focused ticker node is filled `--amber` with black text. Edges are 1px `--line-strong`, positive relations `--up` at 60%, negative `--down` at 60%. The list view is a Table with cyan group rows per relation type. |
| N | Headline list: time (mono `--muted` 5ch), source code (mono `--text` 4ch), headline (mono 12px `--amber`, truncated), tickers (TickerChips). Unread rows are `--text`; read rows are `--amber-dim`. |
| W | Table with group rows, depth bars on volume, and the `--line-faint` rule every 5 rows. |
| EQS | Presets as a segmented control. Filter form fields are 22px, `--bg`, 1px `--line-strong`, mono, crimson outline on focus. The results table follows W. |
| SECT | Treemap tiles with 1px `--bg` gaps (no borders). Fill: a diverging scale from `--down` (at −3% or below) through `#1a1a1a` (0) to `--up` (at +3% or above), in 7 steps. The label is mono with black text on fills at ≥ 50% lightness and `--text` otherwise. The rotation chart uses 1px lines, the current point as a 4px square, and quadrant labels in the label face `--muted`. |
| COMP | Up to 4 series in fixed order: amber `#ffa028`, cyan `#4fa3e0`, white `#f2f2f2`, then grey `#8c8c8c` dashed. No purple. The legend is mono, with each series color shown as an 8×2px swatch. |
| ALRT | Rules table plus a create form in the same style as EQS. Triggered state is a filled `cell-ara`/`cell-arb`-style cell, or an amber badge. |
| ASK, BRIEF | Prose screens: label-face section headers, prose 13px at 72ch, numbers inline in mono, tickers as TickerChips. |
| RQ | Section 7.3. |
| HELP | A two-column cheat sheet. Function codes are `.key-fn` keycaps with the name in the label face. Shortcut rows have keycaps on the left and the action in the label face `--text`. The `m` mascot entry is removed. |

### 8.1 Chart colors (`components/chart/useChart.ts`)

```ts
export const CHART_COLORS = {
  bgTop: '#10151c',
  bgBottom: '#07090c',
  bg: '#07090c',           // shutter overlay + fallback
  grid: '#1c232c',
  border: '#222222',
  text: '#8c8c8c',
  amber: '#ffa028',
  line: '#f2f2f2',         // single-series line
  crimson: '#c8102e',
  up: '#22c55e',
  down: '#f43f5e',
  upAlpha: 'rgba(34, 197, 94, 0.35)',
  downAlpha: 'rgba(244, 63, 94, 0.35)',
}
```

- Background: `{ type: ColorType.VerticalGradient, topColor: bgTop, bottomColor: bgBottom }`.
- Grid: `style: LineStyle.Dotted` on both axes, `grid` color.
- Crosshair: dashed lines in `--muted` (`#8c8c8c`). The label background is `#242424` with `--text` labels, not crimson.
- Price scale: border `#222`. The last-value label is colored by direction (the library default for candlesticks; set it for line series by comparing the last value to the previous close).
- Candles: up body `up`, down body `down`, wicks the same color, and no borders.
- Volume: `upAlpha`/`downAlpha` histogram on its own scale, 20% of the height.
- Font: JetBrains Mono 11px.
- Reveal: `revealChart` shutter and `lastPriceAnimation` follow v2-motion §6.

## 9. Global CSS (`web/src/theme/global.css`)

1. `body`: `--canvas` background, `--amber` color, `--font-mono`, `--fs-sm`, `line-height: var(--lh-data)`, `tabular-nums slashed-zero`. `overflow: hidden` is kept.
2. Utility classes are kept with their names: `.muted`, `.value` (`--text`), `.up`, `.down`, `.display` (now the label face 600 15px, sentence case), plus the new `.label` (label face 500 11px `--muted`), `.prose` (prose 13px/1.55, 72ch), `.cyan`, `.cell-ara`, `.cell-arb` and `.cell-halt`.
3. `kbd`, `.key-fn`, `.key-sector`, `.key-go` and `.key-cancel` follow 6.7.
4. Focus: `:focus-visible { outline: 1px solid var(--crimson); outline-offset: 1px; }`. Inside dense tables and lists, use the inset variant `outline-offset: -1px`.
5. Scrollbars: `scrollbar-width: thin; scrollbar-color: var(--line-strong) transparent;` plus a WebKit equivalent with a 6px thumb.
6. `::selection` is `--select` with white text.
7. The reduced-motion block from v2-motion §11 applies.
8. `.halftone` and all v1 utilities are deleted.

## 10. The Sumire app icon

The icon is a geometric bust on a black tile: a crimson hair disc with a ponytail sweeping right, a pale face under the fringe, black round glasses, and a small amber hair tie. It was rendered at 16, 24, 32 and 64px on dark and white backgrounds. At 16px it still reads as "red-haired girl with round glasses". An earlier "crimson tile with two amber lenses" draft was rejected because at small sizes it read as a creature face, not a person.

`web/public/favicon.svg` (the same paths are in `components/brand/SumireMark.tsx`):

```svg
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 32 32">
  <rect width="32" height="32" fill="#000"/>
  <path d="M24 7c6 3 8 11 6 20-2-4-4-6-7-7z" fill="#8E0C22"/>
  <circle cx="15" cy="15" r="12.5" fill="#C8102E"/>
  <path d="M6 25V17c3-1 5-3 6-6 2 3 7 5 12 5v9c0 4-4 7-9 7s-9-3-9-7z" fill="#F2D3BE"/>
  <rect x="25" y="5" width="4.5" height="3" fill="#FFA028" transform="rotate(40 27 6.5)"/>
  <circle cx="10.5" cy="21" r="3.6" fill="none" stroke="#000" stroke-width="2"/>
  <circle cx="19.5" cy="21" r="3.6" fill="none" stroke="#000" stroke-width="2"/>
  <path d="M14 20.5h2" stroke="#000" stroke-width="1.6"/>
</svg>
```

- `SumireMark({ size = 16, label })` renders the same SVG inline. Without `label` it is `aria-hidden`; with `label` it is `role="img"`. Use it in the top bar (16px) and the boot sequence (32px) only.
- `index.html`: `<meta name="theme-color" content="#000000">`, and the title is `Sumire`. Area B may set `document.title` to `IHSG 7,312 +0.42% · Sumire` while the market is live.
- Skin `#F2D3BE` is the only non-token color in the app, and it appears only inside the icon.

## 11. Motion mapped to components

Durations and easings come from section 3.1. The behavior is specified in v2-motion.md.

| Component | Effect | Owner |
|---|---|---|
| `ui/Num` | `flash` tick overlay, `useTweened`, `Odometer` | B |
| `tape/TickerTape` | constant-speed scroll, pause, `<Num flash>` | B |
| `watchlist/WatchlistRail` | shared crimson marker slide, row enter, `<Num flash>` | B |
| `shell/Pane` | focus hairline, `pane-in`, back/fwd direction, delayed Suspense fallback | B |
| `shell/CommandLine` | list open/close, shared highlight bar, error nudge | B |
| `shell/StatusBar`, `TopBar` | live lamp pulse (only loop) | B |
| `boot/Boot` | boot sequence | B |
| `alerts/AlertToaster` | toast in/out | C |
| `ui/Table` | row enter stagger (prop-free: on first data) | C |
| `ui/Spinner`, `ui/Skeleton` | indeterminate bar; delayed static skeleton | C |
| `research/ResearchUsage` | usage bar `scaleX` transition | C |
| `chart/useChart` + `chart/reveal.ts` | shutter reveal, `lastPriceAnimation`, refetch dim | D |
| `mkt/*` | odometer usage, breadth `scaleX`, movers FLIP, sparkline shutter | D |
| `lib/motion.ts` | `reducedMotion()` helper | A |

Every `.module.css` that animates defines its own `@keyframes` (CSS modules hash keyframe names).

## 12. Implementer checklists

The areas can run in parallel because every area codes against the token names in 3.1. Area A lands first if possible, and the aliases keep everything building in the meantime. Each area verifies with `cd web && npm run build && npm run lint`. No commits (the lead commits), no new npm dependencies, no AI or tool names in code. Do not change props, exports, data fetching, keyboard behavior (except removing `m`) or localStorage keys.

### A. Theme and icon

Files: `web/src/theme/tokens.css`, `web/src/theme/global.css`, `web/index.html`, `web/public/favicon.svg`, `web/src/lib/motion.ts` (new), `web/src/components/brand/SumireMark.tsx` (new).

- [ ] Replace tokens.css with section 3.1 exactly, including the legacy aliases.
- [ ] Replace the Google Fonts link (section 3.3) and set theme-color to `#000000`.
- [ ] global.css per section 9: body, utilities (keep names, add `.label .prose .cyan .cell-ara .cell-arb .cell-halt`), keycaps (6.7, with `.key-sector` as an alias of `.key-fn`), focus, scrollbars, `::selection`, and the reduced-motion block. Delete `.halftone` and the other v1 utilities.
- [ ] `lib/motion.ts`: `export const reducedMotion = () => typeof window !== 'undefined' && window.matchMedia('(prefers-reduced-motion: reduce)').matches`.
- [ ] Favicon and `SumireMark` per section 10. Check them at 16px.
- [ ] Grep that `--font-cut` and `--tone-*` have no remaining users after C and D finish (report any to the lead).

### B. Shell, tape, watchlist, motion hooks

Files: `web/src/shell/**`, `web/src/components/tape/**`, `web/src/components/watchlist/**`, `web/src/components/boot/**` (new), `web/src/components/ui/Num.tsx` (+ `Num.module.css`), `web/src/components/ui/useTweened.ts` (new), `web/src/components/ui/Odometer.tsx` (+ css, new), `web/src/components/ui/index.ts` (add exports only).

- [ ] TopBar: brand mark and wordmark, amber loaded-security field, crimson function slab, layout button, clock, and session lamp (5.2). Remove "VIOLET".
- [ ] CommandLine: input styling, three-column suggestion list, shared highlight bar, keycap footer, error line plus nudge (5.2, v2-motion §7).
- [ ] Pane: strip anatomy, crimson index cell, keycap code when focused, focus hairline, `pane-in`, back/fwd direction, delayed Suspense fallback (5.3). Delete the ribbon. Add the optional related-functions footer (`shell/related.ts`).
- [ ] App: 2px pane gutters on `--canvas`, toast styling, and removal of the `m` handler and any mascot mount.
- [ ] ShortcutOverlay: `--raise-2` popover, keycaps, no `m`.
- [ ] StatusBar: lamps table (5.4) and the research segment.
- [ ] TickerTape (5.5) and WatchlistRail (5.6) with motion per v2-motion §5 and §8.
- [ ] Num `flash` prop, `useTweened`, `Odometer` (v2-motion §4). Export the new ones from `ui/index.ts`.
- [ ] Boot sequence (5.7, v2-motion §9).
- [ ] Check: no `clip-path`, no `box-shadow` except focus and the 2px row inset, and no `border-radius` in shell modules.

### C. UI kit, alerts, insight, research

Files: `web/src/components/ui/**` except the B-owned files above, `web/src/components/alerts/**`, `web/src/components/insight/**`, `web/src/components/research/**`.

- [ ] Panel (6.1), Table (6.2 including the optional `bar` column prop and the row-enter stagger), Badge (6.4), Button (6.5, including the segmented-control pattern via `active`), Empty, ErrorState, Spinner and Skeleton (6.6), FreshnessBadge copy (6.8).
- [ ] Remove the glasses Spinner, the halftone and slash in Empty, and any `CutText` or `Starburst` component and export.
- [ ] AlertToaster (7.1): remove the calling card and "Violet". Define its keyframes inside the module.
- [ ] Insight components (7.2) and research components (7.3).
- [ ] Update `components/ui/README.md` to describe the v2 kit in a few lines.

### D. Function screens and chart colors

Files: `web/src/functions/**`, `web/src/components/chart/**` (`useChart.ts`, new `reveal.ts`).

- [ ] `CHART_COLORS` and chart options per 8.1. `revealChart` plus `reveal()` on `useChart`, `lastPriceAnimation`, refetch dim (v2-motion §6).
- [ ] Every screen per section 8: hero line, Panel sections, no cards, sentence-case labels, cyan group rows, depth bars where listed.
- [ ] MKT: Odometer (from B; if B hasn't landed yet, use `Num` and leave a TODO for the lead), breadth bar, movers FLIP, sparkline shutter.
- [ ] HELP: remove the `m` entry in `help/shortcuts.ts`. Function codes as `.key-fn` keycaps.
- [ ] SECT diverging scale and COMP series colors as specified (no purple).
- [ ] Remove all v1 devices in function modules: clip-path slants, plates, `--font-display` headline styling, halftone, `--tone-*`, `--ease-snap`, and global keyframe references.

### Cleanup (last, lead or area A)

- [ ] Remove the legacy alias block from tokens.css once `grep -rn "var(--panel\b\|--panel-2\|--border\b\|--ink\|--paper\|--plate\|--font-display\|--key-fn\b\|--dur-fast" web/src` is empty, renaming the remaining usages to the v2 names.

## 13. Acceptance

A screenshot of the 4-pane layout passes when:

1. The only filled crimson objects are the brand mark, the function slab, the focused pane's index cell and focus hairline, and the keyboard-selected row wash.
2. The only filled amber object is the loaded-security field (plus primary buttons, if visible).
3. Every number is mono, right-aligned and tabular, and every change has a sign.
4. Labels are sentence case in the condensed face, and codes are uppercase mono. No tracked all-caps labels appear.
5. No rounded corners, soft shadows, gradients (except the chart background), halftone, slants, serif display type or mascot.
6. On a quote push, only the changed cells flash, and nothing else on screen moves except the tape and the live lamp.
