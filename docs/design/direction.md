# Design direction: "Stolen Terminal"

**Concept.** A Bloomberg terminal on warm phosphor black that a Phantom Thief has broken into overnight. The terminal itself stays strict: amber labels, white values, mono numbers, 20px rows and a 1px pane grid. The intruder leaves **crimson cut paper** on it: slanted tabs, a ribbon on the pane she is "watching", calling cards when an alert fires, and her own bust stuck in the corner.

**The single bold move is crimson cut paper.** Everything crimson has hard, slanted, hand-cut edges and a hard black offset plate. Nothing else on screen gets that treatment, and nothing else is decorated. Data never tilts, rotates, glows or uses display type.

Sources and reasons for every choice are in [references.md](references.md). This doc is the spec: values are final unless marked *optional*.

Rules that override everything below (from the board):
- Visual pass only. Don't change component props, exports, data fetching, keyboard behaviour or localStorage keys. Small markup changes inside a component (an extra `<span>` for a keycap, an extra SVG) are fine.
- No new npm packages. Fonts come only from the Google Fonts `<link>` in `web/index.html`.
- Crimson is brand only. Up `#22c55e` and down `#f43f5e` are the only direction colors.
- Every motion respects `prefers-reduced-motion`. Focus is always visible.

---

## 1. Tokens (`web/src/theme/tokens.css`)

Replace the whole `:root` block with this. Existing names are kept (with new values) so no component breaks. New names are marked `NEW`.

```css
:root {
  /* surfaces: warm ink, derived from amber, not cool blue-grey */
  --bg: #0a0908;
  --panel: #100e0b;
  --panel-2: #16130f;
  --panel-hover: #1f1a13;
  --border: #2a2218;
  --border-strong: #4a3a22;
  --ink: #0a0908;            /* NEW: outlines, plates, text on amber/paper */
  --paper: #f4efe6;          /* NEW: speech bubble, calling-card body text */

  /* phosphor */
  --amber: #ffb000;          /* labels, chrome text */
  --amber-hi: #ffd27a;       /* NEW: hover on amber things, cursor */
  --amber-dim: #b37c00;      /* table headers, secondary labels */
  --amber-deep: #7a4a00;     /* NEW: ledger rules, halftone dots, key edges */
  --amber-glow: rgba(255, 176, 0, 0.35); /* NEW: only the IHSG hero number */

  /* text */
  --text: #f4efe6;           /* values: warm white, same as paper */
  --muted: #9a8f7c;
  --faint: #5e5546;

  /* brand: crimson cut paper (never up/down) */
  --crimson: #c8102e;
  --crimson-bright: #e8203f;
  --crimson-ink: #7d0a1c;    /* NEW: shadow tone on crimson, ribbon fold */
  --crimson-dim: #3a0a12;    /* was #5c0a17: active-row wash on warm black */

  /* market */
  --up: #22c55e;
  --down: #f43f5e;
  --flat: #9a8f7c;
  --warn: #ffb000;
  --danger: #f43f5e;

  /* Bloomberg keycaps (NEW) */
  --key-fn: #f5c400;         /* yellow function/sector key, black legend */
  --key-fn-edge: #8a6d00;
  --key-go: #2e7d32;         /* GO key only; darker than --up so it never reads as "up" */
  --key-go-edge: #14431a;
  --key-cancel: var(--crimson);
  --key-cancel-edge: var(--crimson-ink);
  --key-dark: #1f1a13;
  --key-dark-edge: #000;

  /* screentone (NEW) */
  --tone-dot: var(--amber-deep);
  --tone-size: 6px;

  /* plates: hard offset, never blurred (NEW) */
  --plate: 3px 3px 0 0 #000;
  --plate-lg: 5px 5px 0 0 #000;
  --plate-crimson: 3px 3px 0 0 var(--crimson);

  --focus-ring: 0 0 0 2px var(--crimson);
  --focus-ring-inset: inset 0 0 0 2px var(--crimson); /* NEW */

  --font-mono: 'JetBrains Mono', 'IBM Plex Mono', ui-monospace, 'Cascadia Mono', Consolas, monospace;
  --font-display: 'Anton', 'Impact', 'Arial Narrow', sans-serif;
  --font-cut: 'DM Serif Display', 'Georgia', serif;   /* NEW: ransom letters only */

  --fs-xs: 11px;
  --fs-sm: 12px;
  --fs-md: 13px;
  --fs-lg: 15px;
  --fs-d1: 15px;   /* NEW: panel title tab */
  --fs-d2: 22px;   /* NEW: empty-state title, wordmark */
  --fs-d3: 34px;   /* NEW: hero numbers (IHSG on MKT), calling-card headline */

  --row-h: 20px;   /* NEW: every table / list row */
  --radius: 0;     /* was 2px: nothing is rounded */
  --radius-key: 1px; /* NEW: keycaps only */
  --gap: 6px;
  --topbar-h: 44px;
  --tape-h: 28px;
  --rail-w: 220px;

  /* motion (NEW except --ease) */
  --ease: cubic-bezier(0.2, 0.8, 0.2, 1);
  --ease-snap: cubic-bezier(0.3, 1.45, 0.5, 1); /* slight overshoot for stamps */
  --dur-fast: 90ms;
  --dur: 140ms;
  --dur-slow: 200ms; /* hard ceiling, nothing is slower */
}
```

### 1.1 Type

| Face | Role | Where it is allowed |
|---|---|---|
| **JetBrains Mono** 400/500/700 | Everything: data, labels, body, inputs | Default `body`. Always `font-variant-numeric: tabular-nums slashed-zero`. |
| **Anton** 400 | Display: panel title tabs, empty/error titles, wordmark, calling-card headline, hero numbers' labels | Uppercase, `letter-spacing: 0.05em`, never under 15px, never for numbers that update. |
| **DM Serif Display** 400 + italic | Ransom-note accent letters only (`.cut` class, see 1.2) | At most 3 words per screen. Never in data or body. |

`web/index.html` font link becomes:
`https://fonts.googleapis.com/css2?family=Anton&family=DM+Serif+Display:ital@0;1&family=JetBrains+Mono:wght@400;500;700&display=swap`

Scale: 11 (meta) / 12 (secondary) / 13 (body + tables) / 15 (command input, lead text) / 22 / 34. Line heights are 20px for rows, 1.5 for prose (INS, BRIEF, N bodies) and 1 for display. Prose max width is 72ch.

Labels: uppercase amber with `letter-spacing: 0.04em` (no wider; Bloomberg labels are tight, not tracked out). Values: white. Never mix the two roles.

### 1.2 Ransom-note lettering (`.cut`)

Used for exactly three things: the `SUMIRE` wordmark, the `VIOLET` label on alerts, and the headline word on the calling card. It's a CSS class on a wrapper whose letters are each wrapped in a `<span>`. A tiny helper `web/src/components/ui/CutText.tsx` (`<CutText text="SUMIRE" />`) splits the text and applies a **deterministic** pattern by letter index `i % 6`:

| i % 6 | font | box | rotate | translateY |
|---|---|---|---|---|
| 0 | Anton | white `--paper` bg, `--ink` text | -3deg | 0 |
| 1 | DM Serif Display italic | `--ink` bg, `--paper` text | 2deg | 1px |
| 2 | Anton | `--crimson` bg, `--paper` text | -1deg | -1px |
| 3 | DM Serif Display | none (transparent, inherits color) | 3deg | 0 |
| 4 | Anton | `--ink` bg, `--amber` text | -2deg | 1px |
| 5 | DM Serif Display italic | `--paper` bg, `--crimson` text | 1deg | 0 |

Each letter has `padding: 0 2px`, `margin-right: 1px` and `line-height: 1.05`, plus `display: inline-block` and `font-size` from the parent. Serif letters get `font-size: 1.08em` so their x-height matches Anton. The whole word keeps `aria-label` = the plain text, and the letter spans get `aria-hidden`. No randomness, no hover effects.

### 1.3 Keycaps (global, `web/src/theme/global.css`)

Restyle `kbd` and add role classes. A keycap is a hard key with a darker 2px bottom edge, not a pill.

```css
kbd, .key {
  display: inline-block; min-width: 18px; height: 16px; padding: 0 4px;
  border: 0; border-bottom: 2px solid var(--key-dark-edge); border-radius: var(--radius-key);
  background: var(--key-dark); color: var(--text);
  font: 700 10px/16px var(--font-mono); letter-spacing: 0.04em; text-transform: uppercase;
  text-align: center; vertical-align: 1px;
}
.key-fn     { background: var(--key-fn);     border-bottom-color: var(--key-fn-edge);     color: var(--ink); }
.key-go     { background: var(--key-go);     border-bottom-color: var(--key-go-edge);     color: #fff; }
.key-cancel { background: var(--key-cancel); border-bottom-color: var(--key-cancel-edge); color: #fff; }
```

Usage: `<kbd className="key-go">GO</kbd>` for Enter, `<kbd className="key-cancel">ESC</kbd>` for Esc, `<kbd className="key-fn">GP</kbd>` for function codes in hints/HELP/rail, and plain `<kbd>` for every other key (`/`, `m`, `Alt`, `1`-`4`).

### 1.4 Screentone and starburst (global utilities)

Replace `.halftone` in `global.css`:

```css
.halftone { position: relative; isolation: isolate; }
.halftone::before {
  content: ''; position: absolute; inset: 0; z-index: -1; pointer-events: none;
  background-image: radial-gradient(circle, var(--tone-dot) 1px, transparent 1.3px);
  background-size: var(--tone-size) var(--tone-size);
  -webkit-mask-image: linear-gradient(225deg, #000 0%, transparent 55%);
          mask-image: linear-gradient(225deg, #000 0%, transparent 55%);
  opacity: 0.55;
}
```

The tone fades from the **top-right** corner only, like a hand-cut screentone sheet. Don't make it symmetric and don't put it behind numbers.

The starburst is a 24-point irregular star (P5 "impact" burst). It's one shared SVG path used in empty states and on the calling card. `viewBox="0 0 60 60"`, polygon points:

```
30.0,1.0 33.4,17.4 43.5,6.6 40.6,19.4 56.0,15.0 41.6,26.9 56.0,30.0 43.5,33.6 54.2,44.0 37.8,37.8 45.0,56.0 33.9,44.5 30.0,57.0 26.6,42.6 15.5,55.1 21.5,38.5 7.5,43.0 16.5,33.6 2.0,30.0 15.5,26.1 4.0,15.0 20.8,20.8 16.5,6.6 26.9,18.4
```

Put it in `web/src/components/ui/Starburst.tsx` (`<Starburst size={64} className=... />`, `fill="currentColor"`, `aria-hidden`).

### 1.5 Global resets that change

- `::selection` stays crimson/white.
- `:focus-visible` uses `outline: 2px solid var(--crimson); outline-offset: 1px;` instead of box-shadow, so it survives `clip-path` and `overflow: hidden`. Inside tables and lists use `--focus-ring-inset`.
- Scrollbar thumb: `var(--border-strong)` 8px with a 2px `--bg` border (via `border: 2px solid var(--bg)` on the thumb) so it reads as a thin rail. Square, no radius.
- `body`: add `font-variant-numeric: tabular-nums slashed-zero;`.
- Delete every `border-radius` value other than `0`, `--radius-key` and `50%` on things that really are circles (glasses lenses, chart markers). Status dots become squares.

---

## 2. Shell

```
┌[SUMIRE]◣VIOLET──[▸│ BBCA GP_                     <GO>]────────── 14:32:07 WIB ■ LIVE  ▦ ┐  44px
├──────────────┬─────────────────────────────────┬────────────────────────────────────────┤
│◤WATCHLIST◢   │[1◣ GP  BBCA  Price chart      ‹ › ]│[2◣ INS BBCA  Insight     ▼ribbon ‹ ›]│  22px strip
│ BBCA 9,875 +│                                  │                                        │
│ ...          │  (1px grid lines between panes,   no gaps, no card shadows)               │
├──────────────┴─────────────────────────────────┴────────────────────────────────────────┤
│[IHSG 7,312 +0.42%]│ LQ45 … │ USDIDR … │ COAL … (scrolling)                     [Sumire]  │  28px
└──────────────────────────────────────────────────────────────────────────────────────────┘
```

### 2.1 Top bar (`shell/TopBar.module.css`, `TopBar.tsx`)
- `.bar`: `background: var(--bg)` (delete the gradient), `border-bottom: 1px solid var(--border-strong)`.
- `.brand`: keep the crimson plate with `clip-path: polygon(0 0, 100% 0, calc(100% - 16px) 100%, 0 100%)`. Add `filter: drop-shadow(3px 0 0 #000)` so the slanted edge has a hard black plate. Padding is `0 28px 0 12px`.
- `.wordmark`: replace the plain text with `<CutText text="SUMIRE" />` at `font-size: 20px`. Remove `skewX`. The cut letters supply the energy.
- `.codename` `VIOLET`: keep a white tag with crimson text rotated -6deg. Change it to `font-family: var(--font-display); font-size: 10px; letter-spacing: 0.08em; padding: 0 4px; box-shadow: 2px 2px 0 #000;`.
- `.clock`: white 13px 500, seconds in `--muted`, `WIB` in `--amber-dim`. No glow.
- `.pill` (session) becomes a **status block**, not a pill: `border-radius: 0; border: 0; padding: 0 6px; height: 18px; line-height: 18px;`. LIVE: `background: var(--up); color: var(--ink)`. BREAK/PRE: `background: var(--amber); color: var(--ink)`. CLOSED: `background: transparent; color: var(--muted); box-shadow: inset 0 0 0 1px var(--border-strong)`. `.dot` is a 6px **square** (`border-radius: 0`) in `--ink`. **Delete the pulse animation**: no ambient loops.
- `.layoutBtn`: square cells in `--amber-dim`, hover border `--crimson`, radius 0.

### 2.2 Command line (`shell/CommandLine.module.css`, `CommandLine.tsx`)
- `.box`: `height: 30px; background: var(--bg); border: 1px solid var(--border-strong);`. On `:focus-within`: `border-color: var(--crimson); box-shadow: var(--plate-crimson);` (hard 3px crimson offset). **Delete the 18px glow.**
- `.prompt`: crimson plate with a `▸` glyph, `clip-path` 8px right cut, unchanged otherwise.
- `.input`: amber 15px uppercase; `caret-color: var(--amber-hi)`. Placeholder in `--faint` sentence case: `Type a ticker or function, e.g. BBCA GP`.
- `.hint` (right end): show `<kbd className="key-go">GO</kbd>` permanently, and `<kbd className="key-cancel">ESC</kbd>` only while the list is open. The text hint becomes these keycaps plus nothing else.
- `.list` (suggestions): `border: 1px solid var(--crimson); box-shadow: var(--plate-lg); background: var(--panel);`. Opens with `wipe-down` (section 4).
- `.section` header: `--amber-dim` 10px uppercase, with a 1px `--amber-deep` rule after the text (`display:flex; gap:6px; ::after flex:1 height:1px`).
- `.item`: `height: var(--row-h); padding: 0 10px; align-items: center`. Label = function or ticker in amber 700, detail white, meta `--muted`.
- `.itemActive` = **the off-palette selection rule**: `background: var(--crimson) !important; box-shadow: none;`, label `color: var(--amber)`, detail `color: var(--paper)`, meta `color: rgba(244, 239, 230, 0.7)`. Add `clip-path: polygon(0 0, 100% 0, calc(100% - 8px) 100%, 0 100%)` so the selected row is a cut strip.
- `.footer`: keycaps (`<kbd>↑</kbd><kbd>↓</kbd>`, `<kbd className="key-go">GO</kbd>`, `<kbd className="key-cancel">ESC</kbd>`) with `--faint` words.
- `.error`: border `--down`, `.errorText` `--down`.

### 2.3 Pane strip and active marker (`shell/Pane.module.css`, `Pane.tsx`)
- `.pane`: `border: 0; background: var(--panel);`. The 1px grid lines come from the parent grid (2.5). Delete `box-shadow` on focus.
- `.focused`: `outline: 1px solid var(--crimson); outline-offset: -1px; z-index: 1;`.
- `.strip` (22px): `background: var(--bg); border-bottom: 1px solid var(--border);`.
  - `.num`: the pane-number tab. Unfocused: `--panel-2` / `--muted`. Focused: `--crimson` / `#fff`, plus `filter: drop-shadow(2px 0 0 #000)`. Keep the 6px right `clip-path` cut.
  - Split `.cmd` into a **code keycap + ticker** (small markup change in `Pane.tsx`: render `entry.code` in `<kbd className="key-fn">` and the rest of `formatEntry(entry)` as text). Ticker in white 700. The function name (`registry[...].name`) stays `--muted`.
  - Unfocused panes: the code keycap uses plain `kbd` (dark key) instead of `key-fn`, so only the focused pane shows yellow. That's a second cue besides the ribbon.
  - `.navBtn`: `‹ ›` glyphs in `--amber-dim`, hover `--amber-hi` on `--panel-hover`.
- `.ribbon` (active-pane marker): replace it with a two-tail ribbon hanging from the strip's bottom edge:
  - `position: absolute; top: 14px; right: 56px; width: 12px; height: 20px; background: var(--crimson);`
  - `clip-path: polygon(0 0, 100% 0, 100% 100%, 50% 74%, 0 100%);`
  - Add an `::before` knot: `top: -3px; left: -2px; width: 16px; height: 6px; background: var(--crimson-ink);`
  - `filter: drop-shadow(1px 2px 0 #000);`
  - It enters with `ribbon-drop` (section 4) when focus changes.

### 2.4 Status bar (`shell/StatusBar.module.css`)
- Segments are separated by `border-right: 1px solid var(--border)` with `padding: 0 8px`. Labels in amber-dim uppercase 11px and values white.
- The research runner text stays `--muted`. When research is running, prefix it with a 6px crimson square (static, no pulse).
- Buttons inside use the dark-key button (3.4) at 18px height.

### 2.5 App frame (`shell/App.module.css`)
- `.grid`: `gap: 1px; padding: 0; background: var(--border);` so panes sit on hairlines like one instrument.
- `.rail`: `border-right: 1px solid var(--border-strong); background: var(--panel);`.
- `.bottom`: `border-top: 1px solid var(--border-strong); background: var(--bg)`.
- `.overlay`: `background: rgba(10, 9, 8, 0.82);` and **delete `backdrop-filter`**.
- `.dialog`: `border: 1px solid var(--crimson); box-shadow: var(--plate-lg);` and animation `wipe-in` (section 4).
- `.toast`: `border: 0; border-left: 4px solid var(--crimson); background: var(--panel-2); box-shadow: var(--plate);`. Animation `slash-in`.

### 2.6 Ticker tape (`components/tape/TickerTape.module.css`)
- The first item (IHSG) is **inverted**: `background: var(--amber); color: var(--ink); padding: 0 8px; font-weight: 700;`, with its % change still in up/down color on a `--ink` 1px-padded chip, so green/red stay legible. Break the symmetry once.
- `.sep`: `border-left: 1px solid var(--amber-deep)`.
- Code amber, price white, change up/down. Gap 18px. The scroll stays (functional) and pauses on hover/focus. Reduced motion means a static row (already done).

### 2.7 Watchlist rail (`components/watchlist/WatchlistRail.module.css`)
- `.head`: the title becomes a small slanted crimson tab like the panel title (Anton 13px, `clip-path` 8px cut, `padding: 2px 14px 2px 8px`). The select is square, `--panel-2`, with a 1px `--border-strong` border.
- `.row`: `height: var(--row-h); padding: 0 8px; align-items: center; border-bottom: 0;`. Every 5th row (`li:nth-child(5n) .row`) gets `border-bottom: 1px solid var(--amber-deep)` (ledger rule).
- `.current`: `box-shadow: inset 3px 0 0 var(--crimson); background: var(--crimson-dim);` with the code in `--amber-hi`.
- `.note`: `border: 0; border-left: 3px solid var(--crimson); box-shadow: var(--plate);`.

---

## 3. UI kit (`components/ui/`)

### 3.1 Panel (`Panel.module.css`)
- `.panel`: `border: 0; background: var(--panel);`. Panes already draw the frame.
- `.header`: `min-height: 26px; background: var(--bg); border-bottom: 1px solid var(--amber-deep);`. **Delete the crimson gradient.**
- `.title`: crimson tab, Anton `var(--fs-d1)`, `padding: 2px 22px 2px 10px`, `clip-path: polygon(0 0, 100% 0, calc(100% - 12px) 100%, 0 100%)`, `filter: drop-shadow(3px 0 0 #000)`. Text `--paper`.
- `.subtitle`: `--muted` 12px. `.actions` right-aligned, 6px gap.
- `.padded`: `padding: 8px 10px`.

### 3.2 Table (`Table.module.css`)
- `.table th`: `height: var(--row-h); padding: 0 8px; background: var(--bg); color: var(--amber-dim); font-size: 11px; font-weight: 500; letter-spacing: 0.04em; border-bottom: 1px solid var(--amber-deep);`. `.sorted`: `color: var(--amber)`, with the sort arrow as `▲`/`▼` in 9px.
- `.table td`: `height: var(--row-h); padding: 0 8px; border-bottom: 0;`. **No zebra striping.** Ledger rule: `tbody tr:nth-child(5n) td { border-bottom: 1px solid var(--amber-deep); }`, plus a faint `1px solid var(--border)` on every other row's bottom only if the screen needs it (default: no).
- Number cells (`.right`): `font-variant-numeric: tabular-nums slashed-zero;`.
- `.row:hover td`: `background: var(--panel-hover)`.
- `.active td`: `background: var(--crimson-dim) !important;`. The first cell keeps `box-shadow: inset 3px 0 0 var(--crimson)`.
- `.wrap:focus-visible`: `outline: none; box-shadow: var(--focus-ring-inset);`.

### 3.3 Badge (`Badge.module.css`)
- `border-radius: 0; height: 16px; line-height: 14px; padding: 0 5px; font-size: 10px; font-weight: 700; letter-spacing: 0.04em;`.
- `default`: `--text` on `--border-strong` outline. `amber`: amber outline. `up`/`down`: colored outline and text. `muted`: `--muted` on `--border`.
- `solid`: `--ink` on `--amber`.
- `crimson`: `--paper` on `--crimson`, **cut corner** `clip-path: polygon(0 0, 100% 0, calc(100% - 5px) 100%, 0 100%); padding-right: 8px;`. It's the only badge with a cut, because it is brand.

### 3.4 Button (`Button.module.css`): Bloomberg keys
- Base (`.button`, dark key): `height: 22px; padding: 0 10px; border: 0; border-bottom: 2px solid #000; border-radius: var(--radius-key); background: var(--key-dark); color: var(--text); font-size: 11px; font-weight: 700; letter-spacing: 0.04em; text-transform: uppercase;`. Hover: `background: var(--panel-hover); color: var(--amber-hi);`. `:active`: `transform: translateY(1px); border-bottom-width: 1px;`.
- `.primary` = **yellow function key**: `background: var(--key-fn); color: var(--ink); border-bottom-color: var(--key-fn-edge);`. Hover: `background: #ffd21f`. This replaces the crimson primary: crimson is cut paper, not a button.
- `.ghost`: text only `--muted`, hover `--amber`, with an underline offset of 3px and no box.
- `.active` (toggle on): `background: var(--amber); color: var(--ink); border-bottom-color: var(--amber-deep);`.
- `:disabled`: `opacity: 0.4`, no transform.
- Destructive actions (delete alert or watchlist), if a component wants them: add `className="key-cancel"` styles via a local class (`background: var(--key-cancel)`). No new variant prop.

### 3.5 Empty state (`Empty.tsx`, `Empty.module.css`)
**Left-aligned and asymmetric** (the current centered stack is the templated look).

```
┌───────────────────────────────────────────── ·:·:· (tone, top-right fade)
│  ✸ (64px starburst, crimson, rotated -8deg, sits behind/left of title)
│     NO ALERTS YET            ← Anton 22px --paper, offset 18px right of burst center
│     ▬▬▬▬ (48×5 crimson slash, skewX(-24deg))
│     I'll watch BBCA and tell you when it crosses a line you set.   ← --muted 12px, 46ch
│     [NEW ALERT] (yellow key)
```

- `.empty`: `align-items: flex-start; justify-content: center; text-align: left; padding: 20px 24px 20px 28px; gap: 6px;`, with `halftone` (new top-right fade) kept.
- Add `<Starburst size={64} className={s.burst} />` before the title: `.burst { position: absolute; margin: -22px 0 0 -18px; color: var(--crimson); transform: rotate(-8deg); opacity: 0.9; z-index: -1; }`. The title sits on top of it (the title wrapper gets `position: relative`).
- `.title`: Anton `var(--fs-d2)`, `--paper`, letter-spacing 0.05em, `text-shadow: 2px 2px 0 #000` (hard, no blur).
- `.slash` stays (48x5 crimson, skew -24deg), placed **under** the title.
- Copy is in Sumire's voice and names real things (section 7).

### 3.6 Error state (`ErrorState.tsx`)
The same layout as Empty, but the starburst is **outlined, not filled**: `fill: none; stroke: var(--down); stroke-width: 1.5`. The title is `--paper` and the message `--text` (errors must be read). Retry is a **yellow key** (`variant="primary"`). Keep `describe()` but rewrite its strings (section 7). Pass the outlined variant via a small `tone="error"` prop on the internal `Empty` call only if `Empty` needs it. Empty's public props must not change for other callers, so make it optional.

### 3.7 Spinner (`Spinner.module.css`): glasses
- The frames stay round (real circles). `stroke: var(--crimson); stroke-width: 2.4; stroke-linecap: square;`. The bridge gets `stroke-linejoin: miter`.
- Add the temples: a short line from each outer lens edge (`M2 9 l-3 -2` and `M46 9 l3 -2`).
- The glint is a white dash sweeping round each lens at 1.1s linear (keep the existing logic).
- Label: `--muted` 12px, sentence case, e.g. `Loading BBCA chart` (callers already pass labels).
- **Reduced motion**: no sweep. A static white glint arc at the top-left of each lens (`stroke-dasharray: 8 49; stroke-dashoffset: -6`) and the label text are shown. Implement it with an explicit `@media (prefers-reduced-motion: reduce) { .shine { animation: none; stroke-dashoffset: -6; } }`, because the global 0.01ms rule makes it flicker.

### 3.8 Skeleton (`Skeleton.module.css`)
- **Delete the shimmer** (a looping AI-ish tell). Bars are static: `height: 10px; background: var(--panel-2); border-radius: 0;`.
- Rows mimic the ledger: `.row` height 20px with a 1px `--border` bottom. Every 5th row gets an `--amber-deep` rule.
- The first bar of the first row is 28px wide in `--amber-deep` (a "label" stub). The others vary width as now.
- One blinking amber block cursor (`8x12px`, `--amber`, `steps(1)` 1s) is shown after the last bar. It's the one allowed loop, and it's hidden under reduced motion.

### 3.9 FreshnessBadge / Num
- FreshnessBadge: uses Badge. Fresh = `muted`, stale = `amber`, very stale = `down` outline. No color invention.
- Num: no change except ensuring `slashed-zero`.

---

## 4. Motion (`global.css` keyframes)

At most 200ms, always directional (things enter from where they come from), no idle loops (only exceptions: ticker tape scroll, spinner, skeleton cursor).

```css
@keyframes slash-in {   /* toasts, alerts, mascot bubble */
  from { transform: translateX(-10px) skewX(-8deg); opacity: 0; }
}
@keyframes wipe-in {    /* dialogs, overlays: left-to-right cut */
  from { clip-path: inset(0 100% 0 0); }
  to   { clip-path: inset(0 0 0 0); }
}
@keyframes wipe-down {  /* command suggestions */
  from { clip-path: inset(0 0 100% 0); }
  to   { clip-path: inset(0 0 0 0); }
}
@keyframes stamp {      /* VIOLET label on a new alert */
  from { transform: scale(1.25) rotate(-6deg); opacity: 0; }
}
@keyframes ribbon-drop {
  from { transform: translateY(-8px); }
}
```

| Element | Animation |
|---|---|
| Dialog (`.dialog`) | `wipe-in var(--dur-slow) var(--ease)` |
| Command list | `wipe-down var(--dur-fast) var(--ease)` |
| Toast, alert card, mascot bubble | `slash-in var(--dur) var(--ease)` |
| VIOLET label inside a new alert | `stamp var(--dur) var(--ease-snap) 60ms both` |
| Pane ribbon on focus | `ribbon-drop var(--dur) var(--ease-snap)` |
| Hover, color changes | `transition: background-color var(--dur-fast), color var(--dur-fast)` only |

Delete: the mascot `bob` loop, the session `pulse`, the skeleton `shimmer`, `pop` scale-ups (replaced by the above).

Reduced motion (`@media (prefers-reduced-motion: reduce)`): keep the existing global 0.01ms rule, and additionally set `animation: none` on the ribbon, the spinner glint (static, see 3.7), the skeleton cursor and the ticker track. Elements must appear in their final state, never stuck at `from`.

---

## 5. Mascot (`components/mascot/Portrait.tsx` placeholder, `Mascot.module.css`)

Replace the `Placeholder` SVG with a **new hand-built bust**. It's still an original drawing (no traced game art), and the PNG override from `web/public/assets/sumire/` keeps working unchanged.

### 5.1 Art direction
- **View**: 3/4, facing **left** toward the screen (she sits bottom-right and looks at your panes). Head tilted 4deg clockwise. The face centre is at about x=54 in a 120x120 viewBox, not centered, so she leans into the frame.
- **Silhouette carriers** (must read as a solid black fill at 48px): (1) long hair falling past the shoulders with a **low ponytail** swinging out to the right edge (x 92-112, y 70-118), (2) **round glasses**, (3) the **ribbon bow** at the ponytail tie (x~94, y~66) with two sharp tails.
- **Line**: ink `#0a0908`. The outer silhouette is 2.25 stroke and inner lines (hair strands, collar, jaw) are 1.25. `stroke-linejoin: miter; stroke-linecap: square`. No round caps anywhere except the glasses.
- **Shape language**: hair is built from **sharp, faceted locks**: bangs are 4 pointed locks swept to her left (viewer's right) with tips at y 50-58. Side locks end in points. No blobby curves: use `L` and short `Q` segments, with each lock tip a hard point.
- **Cel shading**: exactly 2 tones per material (base + one shadow). Shadows are hard-edged shapes (no gradients, no opacity blends), cast from top-left. That's under the bangs, the left jaw edge, under the chin and inside the ponytail.
- **Palette** (only these):

| Material | Base | Shadow |
|---|---|---|
| Hair | `#c8102e` | `#7d0a1c` |
| Hair highlight (1 sharp sliver on the crown) | `#e8203f` | none |
| Skin | `#f6dcc8` | `#e2b9a0` |
| Eyes | `#2b1418` iris, `#f4efe6` 1 square catchlight | none |
| Glasses | frame `#c8102e`, lens fill `rgba(244,239,230,0.18)`, 1 white glint wedge | none |
| Ribbon | `#c8102e` with an `#ffb000` 1px edge on one tail (terminal tie-in) | `#7d0a1c` |
| Uniform blazer | `#1c1a1d` | `#0a0908` |
| Shirt / collar | `#f4efe6` | `#cfc6b8` |
| Neck ribbon (uniform) | `#c8102e` | `#7d0a1c` |
| Gym jacket | `#1c1a1d` with a `#c8102e` zip stripe and `#f4efe6` 1.5px piping on the collar | `#0a0908` |

- **Backing**: behind the bust, a crimson cut-paper plate: `polygon(14,30 112,18 118,120 4,120)` (a 10deg slanted top edge), with a black copy offset `4,4` behind it. A halftone patch (6px dots `--amber-deep`) is clipped to the plate's top-right triangle only. The bust overlaps the plate's top edge (hair breaks the frame) so the backing doesn't read as a tidy avatar circle.
- **Drop shadow**: `.figure { filter: drop-shadow(4px 4px 0 #000); }`, which replaces the blurred one.

### 5.2 Layer order (back to front)
1. Black offset plate, 2. crimson plate + halftone, 3. ponytail + ribbon, 4. back hair mass, 5. body/outfit, 6. neck (skin shadow under the chin), 7. face, 8. ears (only the left ear is visible), 9. eyes, 10. glasses, 11. brows, 12. mouth, 13. bangs + side locks (over the glasses' top edge), 14. mood extras.

Group the mood-dependent parts (`<g data-part="brows|eyes|mouth|extra">`) so that **only these change between moods**. The silhouette and hair never move.

### 5.3 Moods (`neutral | smiling | worried | determined | surprised`)

| Mood | Brows | Eyes | Mouth | Extra |
|---|---|---|---|---|
| neutral | flat, slight inner lift | open ovals, catchlight top-left | short flat line, 1.25 ink, slightly off-centre left | none |
| smiling | raised and arched | closed upward arcs (`^`), 2px ink | open smile: wedge shape, `#8a2a32` fill, ink outline | 2 short ink hatch lines on each cheek (blush as **hatching**, not pink ovals) |
| worried | inner ends raised steeply | ovals, smaller pupils, catchlight lower | small wavy line | one angular sweat drop (a teardrop made of 3 straight segments, `#f4efe6` fill, ink outline) at the right temple |
| determined | angled down toward the centre, thick (2.5) | narrowed: top lid flattened by a straight ink line | firm line with a downturned left corner | a white glint wedge across the left lens (a 3-point polygon), the glasses "flash" (P5 look) |
| surprised | high, short | wide circles, small pupils | small open "o" (ink ring, `#8a2a32` fill) | 3 short ink "shock" lines radiating above the head at -30deg, 0deg and 30deg |

### 5.4 Outfits
- **uniform** (Shujin-like): dark blazer with sharp notched lapels as 2 triangles, a white collar point on each side, and a crimson neck ribbon tied as a flat bow with two pointed tails.
- **gym**: a dark track jacket zipped to the collarbone, a high collar with white piping, a crimson vertical zip stripe, and the shoulder line slightly raised (sporty). No ribbon at the neck. The hair ribbon stays.
- An auto-switch rule already exists in `Mascot.tsx`. Don't touch it.

### 5.5 Speech bubble (`Mascot.module.css .bubble`)
A **jagged P5 callout**, not a rounded bubble:
- `background: var(--paper); color: var(--ink); border: 0; padding: 8px 12px 9px 10px; font-size: 12px; line-height: 1.4; max-width: 260px;`
- `clip-path: polygon(0 6%, 100% 0, 97% 82%, 100% 100%, 88% 88%, 4% 96%);`, so the bottom-right spike (100% 100%) is the tail pointing down-right at her.
- Hard plate: since clip-path cuts box-shadow, wrap the bubble's background in a `::before` copy at `transform: translate(4px, 4px)` in `--ink`, or apply `filter: drop-shadow(4px 4px 0 #000)` to the button (preferred: one line).
- A crimson 3px rule along the left edge inside (`box-shadow: inset 3px 0 0 var(--crimson)`).
- `transform: rotate(-1.5deg)`, entering with `slash-in`.
- **Alert variant** (when the bubble shows an alert): `background: var(--crimson); color: var(--paper);`, with the first word replaced by `<CutText text="VIOLET" />` at 13px. That's the only other place ransom letters appear.
- `.outfit` toggle button: a dark keycap (`--key-dark`, radius-key), visible on hover/focus as now.
- **Delete** the `bob` animation.

### 5.6 Small variant (favicon / future collapsed state)
Head only in 2 colors (crimson hair silhouette with ponytail + ink glasses circles) on transparent. *Optional*: replace `web/public/favicon.svg` with it if one exists. Otherwise skip.

---

## 6. Violet alerts: the calling card (`components/alerts/AlertToaster.module.css`)

Alerts currently use purple `#8b5cf6` / `#a78bfa`, which is an AI-default tell. **Remove all purple.** "Violet" is her codename, not a color.

```
   ┌─────────────────────────────────────┐▌  (hard black plate 4px)
   │ ✸ V I O L E T   (cut letters)  14:32│   ← crimson card, slight -1deg tilt
   │ ┌─────────────────────────────────┐ │   ← inner 1px --ink border inset 4px
   │ │ [BBCA] crossed MA200 at 9,875   │ │   ← mono 12px --paper, ticker in yellow key
   │ └─────────────────────────────────┘ │
   └─────────────────────────────────────┘
```

- `.toast`: `background: var(--crimson); border: 0; box-shadow: none; filter: drop-shadow(4px 4px 0 #000); clip-path: polygon(0 4px, 100% 0, 99% 100%, 1% calc(100% - 3px)); transform: rotate(-1deg);` with `slash-in` entry.
- Inner frame: `.body { margin: 4px; outline: 1px solid rgba(10,9,8,0.6); outline-offset: -1px; padding: 6px 9px; }`.
- `.brand` (the "Violet" label): `<CutText text="VIOLET" />` at 13px with `stamp` entry. The time is right-aligned, `--paper` at 0.75 opacity, mono 11px.
- Message: mono 12px `--paper`, with the ticker rendered as `<kbd className="key-fn">BBCA</kbd>` (if the message is a plain string, leave it as text, but don't parse it with regexes).
- Dismiss button: `ESC`-style crimson-ink dark keycap in the top right.
- A small 18px `Starburst` in `--paper` before the label.

---

## 7. Copy (Sumire's voice, sentence case, specific)

Empty and error messages speak as her, briefly, naming the real thing and the next action. Examples to use where the strings currently are generic:

| Where | Title | Message |
|---|---|---|
| Alerts empty | No alerts yet | I'll watch a price or MA cross for you. Add one with New alert. |
| Watchlist empty | Empty watchlist | Type a ticker and press <GO>, then `W` to add it here. |
| News empty | No news for BBCA | Nothing in the feeds since 07:00 WIB. |
| Insight empty | No notes on BBCA yet | Queue research with `BBCA ASK` <GO>. |
| Error: server down | Server not reachable | The Sumire server at localhost:5080 isn't answering. Start it, then retry. |
| Error: 404 | Not found | There's no data for this ticker yet. |
| Error: 5xx | Server error (503) | {message} |
| Spinner labels | | `Loading BBCA chart`, `Reading the news` |

Implementers: change only the strings, not the logic. Use real keycaps (`<kbd>`) where a key is named. Errors don't apologize.

---

## 8. Per-area checklist

Each implementer owns one row. Tokens (section 1) land first. Everything else reads tokens and must not hardcode hex values except inside the mascot SVG.

### A. Theme + fonts (`web/index.html`, `web/src/theme/tokens.css`, `web/src/theme/global.css`)
- [ ] Replace the tokens (section 1), font link (1.1), keycaps (1.3), halftone (1.4), focus/scrollbar/selection (1.5) and keyframes (section 4).
- [ ] Add `components/ui/CutText.tsx` + `CutText.module.css` (1.2) and `components/ui/Starburst.tsx` (1.4). Export both from `components/ui/index.ts`.

### B. Shell (`web/src/shell/*.module.css`, small markup in `TopBar.tsx`, `CommandLine.tsx`, `Pane.tsx`, `ShortcutOverlay.tsx`)
- [ ] TopBar (2.1): no gradient, CutText wordmark, square status block, no pulse.
- [ ] CommandLine (2.2): no glow, crimson plate on focus, keycap hints, cut selected row.
- [ ] Pane (2.3): key-fn code chip on the focused pane, new ribbon, outline focus.
- [ ] App (2.5): 1px grid, no backdrop blur, plate shadows, new animations.
- [ ] StatusBar (2.4).
- [ ] ShortcutOverlay / HELP cheat sheet: every key shown as `kbd` with the role classes (GO/ESC/function codes).

### C. UI kit + alerts + mascot (`web/src/components/ui/*`, `components/alerts/*`, `components/mascot/*`)
- [ ] Panel, Table, Badge, Button, Empty, ErrorState, Spinner, Skeleton (section 3).
- [ ] AlertToaster calling card (section 6). Zero purple left (`grep -ri "8b5cf6\|a78bfa\|violet" *.css` returns nothing color-related).
- [ ] New Portrait placeholder SVG (5.1-5.4), bubble (5.5), figure drop shadow, no bob.

### D. Feature components + screens (`components/insight|research|tape|watchlist/*`, `web/src/functions/**/*.module.css`)
- [ ] TickerTape (2.6), WatchlistRail (2.7).
- [ ] InsightCard: the summary left rule stays crimson 4px. Source chips become Badge-style (radius 0, 16px). The `.warnings` block becomes `border-left: 3px solid var(--warn); background: transparent;`. The headline of a brief uses Anton `--fs-d2` `--paper`. TickerChip: radius 0, amber code, white price.
- [ ] Research: progress/usage bars are square, the fill is `--amber` and the track is `--panel-2`. No gradients.
- [ ] Every `functions/**/*.module.css`:
  - remove `border-radius` (except real circles), `linear-gradient`/`radial-gradient` used as decoration, blurred `box-shadow` (use `var(--plate)` or nothing), and `backdrop-filter`;
  - replace hardcoded colors with tokens (cool greys go to `--panel*`/`--border*`, purple/blue to `--amber` or `--crimson` by role);
  - set table-like lists to `--row-h` rows and ledger rules;
  - section headings inside screens: amber-dim 11px uppercase with a trailing 1px `--amber-deep` rule (the same as the command-list section header), **not** another crimson tab (only the Panel title is a crimson tab);
  - MKT: the IHSG hero value at `--fs-d3` mono 700 white with `text-shadow: 0 0 2px var(--amber-glow)`. That's the only glow in the app. Breadth bars may use block characters (`█▌░`) in up/down colors;
  - charts (GP/COMP/FCST/SECT): grid lines `--border`, axis text `--amber-dim`, crosshair `--amber`, series colors unchanged for up/down. Forecast bands are `--amber` at 0.12 alpha (no blue);
  - EQS form inputs: square, `--bg` background, `--border-strong` border, focus `outline: 2px solid var(--crimson)`.

### Verification (every implementer)
- `cd web && npm run build` and `npm run lint` pass. Don't start the API server.
- `grep -rnE "border-radius: *[1-9]|backdrop-filter|#8b5cf6|#a78bfa|shimmer|rgba\(0, ?0, ?0, ?0\.[0-9]+\) *[0-9]" web/src --include=*.css` returns nothing (the only exception is `--radius-key` and 50% circles).
- Keyboard: Tab through the top bar, command line and a pane. A crimson focus outline is visible everywhere, including on clipped elements.
- Reduced motion (DevTools > Rendering > emulate): nothing animates except instant state changes. The spinner shows a static glint and a label.

---

## 9. Self-review against the brief

- *Is any of this the default dark dashboard?* The cool blue-grey surfaces, rounded 2px boxes, gradient headers, glow focus and purple alerts all were. Each is replaced by a warm phosphor ink, a hard plate, or crimson cut paper.
- *Is crimson overused?* Crimson only appears on: wordmark plate, panel title tabs, pane number (focused), ribbon, selected command row, active-row bar, calling cards and the mascot. Buttons moved to the yellow Bloomberg key so crimson stays rare.
- *Density kept?* Rows are 20px, header strips 22-26px, padding 6-10px, panes on 1px lines. Nothing grew.
- *One memorable thing?* Crimson cut paper with hard black plates. The ransom letters are limited to 3 words app-wide so they stay a surprise.
