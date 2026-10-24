# V2 motion system

Scope: every animation in `web/`. Motion here reports data or confirms input. It is never decoration. If a motion does not answer "what changed?" or "did my keystroke land?", it is cut.

Hard limits (from V2 RULES):

- Animate `transform` and `opacity` only. Color changes are allowed as instant state changes. A color "fade" is an `opacity` animation on an overlay layer.
- Each step runs 80-240 ms. The boot sequence is the only multi-step choreography, and it totals under 900 ms.
- No ambient loops except the live indicator. The ticker tape is the one sanctioned continuous motion because it carries data (see section 5). The lead should confirm this exception.
- No new npm dependencies. Use CSS transitions, `@keyframes` defined inside the CSS module that uses them (modules hash keyframe names), and the Web Animations API (`el.animate`) for one-shot DOM effects.
- Every effect has a `prefers-reduced-motion` fallback (section 11).

## 1. Principles applied to a terminal

1. **Response first.** Feedback appears on `keydown` or `pointerdown`, never after a transition. Inputs, the focus ring and the active row change on the same frame as the event. Nothing waits for an animation to finish, and nothing blocks input while one runs.
2. **Enter animates, exit is instant (or 80 ms).** When a command runs, the old pane content disappears at once and the new content eases in. Users read the new state, not a goodbye.
3. **Interruptible by construction.** Anything that can be re-targeted while it moves (selection indicators, breadth bars, focus hairline) uses a CSS **transition**, because a transition starts from the current on-screen value. Fire-and-forget effects (tick flash, row enter, chart shutter) use **keyframes or WAAPI** and are restarted, not queued.
4. **Spatial consistency.** History back moves content in from the left and forward moves it in from the right. A menu opens from its trigger (`transform-origin` at the command line) and closes back into it.
5. **Phosphor, not glow.** The terminal flavour comes from timing: a fast attack and a long tail, like CRT phosphor decay. It never comes from blur, bloom or scanline overlays.
6. **Critically damped.** Nothing overshoots. Remove `--ease-snap` (the overshoot curve for comic stamps) along with the V1 look.

## 2. Tokens (`web/src/theme/tokens.css`, replacing the current motion block)

```css
:root {
  /* durations */
  --dur-1: 80ms;    /* exits, list close, hover/press acknowledgement */
  --dur-2: 120ms;   /* enters: menus, rows, pane content */
  --dur-3: 160ms;   /* moving indicators, FLIP reorders, focus hairline */
  --dur-4: 240ms;   /* ceiling: tick-flash decay, chart shutter, odometer */

  /* easings */
  --ease-out:   cubic-bezier(0.2, 0.8, 0.2, 1);   /* standard enter (current --ease) */
  --ease-in:    cubic-bezier(0.4, 0, 1, 1);       /* exits */
  --ease-move:  cubic-bezier(0.3, 0, 0, 1);       /* position changes; reads like a damping-1.0 spring, response ~0.2s */
  --ease-decay: cubic-bezier(0.05, 0.7, 0.1, 1);  /* phosphor: drops fast, long faint tail */
  --ease-wipe:  cubic-bezier(0.65, 0, 0.35, 1);   /* chart/sparkline shutter, symmetric */

  /* legacy aliases so existing modules keep compiling until they are migrated */
  --ease: var(--ease-out);
  --dur-fast: var(--dur-1);
  --dur: var(--dur-2);
  --dur-slow: var(--dur-4);

  /* flash tints (overlay layer, animated via opacity) */
  --flash-up: rgba(34, 197, 94, 0.32);
  --flash-down: rgba(244, 63, 94, 0.32);
}
```

Delete `--ease-snap`.

## 3. Event map

| Event | What moves | How | Duration / easing |
| --- | --- | --- | --- |
| Quote tick, price changed | the changed cell only | tint overlay `opacity .32 -> 0`. Text turns `--up`/`--down` at once and drops back to its resting color when the flash ends | 240 ms `--ease-decay` |
| Quote tick, same price | nothing | (no flash: unchanged values must not blink) | - |
| Aggregate value changed (IHSG level, breadth counts, totals) | text | value tween via rAF (section 4.2) | 200 ms `--ease-out` |
| IHSG hero on MKT changed | changed digits only | odometer roll, `translateY` per digit column | 240 ms `--ease-move` |
| Command runs in pane | pane body | `opacity 0 -> 1`, `translateY(3px) -> 0` | 120 ms `--ease-out` |
| History back / forward | pane body | `translateX(-6px / +6px) -> 0` + opacity | 120 ms `--ease-out` |
| Pane gains focus | crimson top hairline | `scaleX(0) -> 1`, origin left (transition) | 160 ms `--ease-move` |
| Pane loses focus | hairline | `opacity -> 0` | 80 ms `--ease-in` |
| Layout change (1/2/4 panes) | panes | opacity crossfade only (grid moves are instant) | 120 ms |
| Command suggestions open | list | `opacity`, `translateY(-4px) -> 0`, origin top | 120 ms `--ease-out` |
| Suggestions close | list | reverse of open | 80 ms `--ease-in` |
| Active suggestion changes (arrows) | one shared highlight bar | `translateY(index * row-h)` (transition) | 80 ms `--ease-move`; none under key repeat |
| Command error | input box | `translateX` 0, -3, 3, -2, 0 (one nudge) | 180 ms linear |
| Data arrives after skeleton | rows | stagger: opacity + `translateY(2px)`, 12 ms per row, max 12 rows | 120 ms each, 240 ms total max |
| First chart data | chart canvas | shutter wipe left to right (section 6) | 240 ms `--ease-wipe` |
| Chart range/interval change | chart container | `opacity .45` while fetching, `-> 1` on data | 120 ms |
| Sparkline first data | svg | shutter wipe; area fill fades in after | 200 ms + 120 ms |
| Movers re-rank | moved rows | FLIP `translateY(old - new) -> 0` | 160 ms `--ease-move` |
| Breadth / bar values | bar fill | `scaleX(ratio)` transition, origin left | 240 ms `--ease-move` |
| Watchlist current row | crimson left marker | one shared marker, `translateY` transition | 160 ms `--ease-move` |
| Watchlist item added | new row | opacity + `translateX(-4px) -> 0` | 120 ms |
| Toast / alert | toast | `translateY(6px) -> 0` + opacity in, opacity out | 120 ms in / 80 ms out |
| Hub connected | live dot | pulse loop (the only ambient loop) | 2 s cycle, opacity 1 -> .35 |
| Hub reconnecting / down | live dot | static amber / static crimson, no loop | - |
| App start | boot sequence | section 9 | < 900 ms, once per session |

Focus rings and button press states are never animated. `:focus-visible` and `:active` apply on the same frame.

## 4. `components/ui/Num.tsx`

### 4.1 Tick flash (opt-in prop)

Add an optional `flash?: boolean` prop. The prop is additive, so existing call sites keep their current behaviour. Live surfaces pass it: the tape, the watchlist rail, quote and movers tables, and the DES header.

```tsx
// Num.tsx (sketch)
const prev = useRef(value)
const [tick, setTick] = useState<{ n: number; dir: 'up' | 'down' } | null>(null)
useEffect(() => {
  const p = prev.current
  prev.current = value
  if (!flash || p == null || value == null || p === value) return // no flash on mount or on equal values
  if (document.hidden) return
  setTick((t) => ({ n: (t?.n ?? 0) + 1, dir: value > p ? 'up' : 'down' }))
}, [value, flash])

return (
  <span className={[cls, s.num].join(' ')} data-tick={tick?.dir}>
    {mark}{formatNum(...)}
    {tick && <span key={tick.n} className={s.flash} data-dir={tick.dir} onAnimationEnd={() => setTick(null)} aria-hidden="true" />}
  </span>
)
```

```css
/* Num.module.css */
.num { position: relative; display: inline-block; }
.flash {
  position: absolute; inset: -1px -3px; pointer-events: none; z-index: -1;
  animation: tick-decay var(--dur-4) var(--ease-decay) forwards;
}
.flash[data-dir='up']   { background: var(--flash-up); }
.flash[data-dir='down'] { background: var(--flash-down); }
.num[data-tick='up']   { color: var(--up); }
.num[data-tick='down'] { color: var(--down); }
@keyframes tick-decay { from { opacity: 1; } to { opacity: 0; } }
```

- The flash direction is the direction of the tick, not of the day's change. A stock down 3% on the day that just ticked up flashes green. That is what traders read in a tick flash.
- `key={tick.n}` restarts the animation on rapid ticks without a reflow hack.
- The cell's parent needs `isolation: isolate` (so `z-index: -1` stays in the cell), or put the text in an inner span above the overlay instead.
- Throttle: only REST refetches and hub pushes change `value`, so no throttle is needed. A refetch that returns identical numbers does not flash.

### 4.2 Value tween (aggregates only)

**Never tween a stock price.** IDX prices sit on tick bands, and a tween shows prices that cannot exist, such as 1,237.4 on a 5-rupiah band. Prices snap and flash. Tween only continuous aggregates: index levels, percentages, breadth counts and portfolio totals. Ship this as a separate `useTweened(value, 200)` hook in `components/ui/`, called by the few screens that want it, rather than as a `Num` default.

```ts
export function useTweened(target: number | null | undefined, ms = 200) {
  const [shown, setShown] = useState(target)
  const from = useRef(target)
  useEffect(() => {
    if (target == null || from.current == null || reducedMotion()) { from.current = target; setShown(target); return }
    const a = from.current, t0 = performance.now()
    let raf = 0
    const step = (t: number) => {
      const k = Math.min(1, (t - t0) / ms), e = 1 - Math.pow(1 - k, 3) // ease-out cubic
      const v = a + (target - a) * e
      from.current = v; setShown(v)       // re-targets from the live value if interrupted
      if (k < 1) raf = requestAnimationFrame(step)
    }
    raf = requestAnimationFrame(step)
    return () => cancelAnimationFrame(raf)
  }, [target, ms])
  return shown
}
```

`tabular-nums slashed-zero` already keeps the width stable while digits change. Keep it.

### 4.3 Odometer (IHSG hero only)

Use it on the one hero number on MKT and nowhere else. Split the formatted string into characters. Each digit is a 1-em-tall window holding a 0-9 column, moved by `translateY(-d * 1em)` with a `--dur-4 --ease-move` transition. Only digits from the first changed position to the right move. Separators (`,` `.`) are static glyphs. Because a transition re-targets from the current position, a second tick mid-roll stays smooth. The roll direction follows the value: rising rolls up and falling rolls down, which is free because the target digit's offset decides it. Under reduced motion, set `transition: none`.

## 5. `components/tape/TickerTape.tsx`

Problems today: `animationDuration` comes from `shown.length * 4`, so the speed changes with the item count, and any change to the duration makes the loop jump.

- **Constant speed instead of constant duration.** Measure one `.group` width with a `ResizeObserver` and set `--tape-dur = width / 48px per second`. Write it as a CSS variable on the track only when the width changes by more than 2%. That stops new tape items from resetting the loop.
- Keyframe `translateX(0) -> translateX(-50%)` linear, defined inside `TickerTape.module.css`. Add `will-change: transform` on the track only.
- Pause with `animation-play-state: paused` on `:hover` and `:focus-within`. Resume from the same spot, with no jump.
- Ticks inside the tape use `<Num flash>`. The flash overlay moves with the item, which is fine because it is a child of the moving track.
- Hidden tab: pause the animation via a `document.visibilitychange` class to save frames.
- Reduced motion: no scroll. The tape becomes a static strip with `overflow-x: auto` and a hidden scrollbar. Flashes still show, because they do not cause motion.

## 6. `components/chart/useChart.ts` (lightweight-charts)

lightweight-charts has no built-in series reveal, and the canvas cannot be animated per point. Two tools are available:

1. **Shutter reveal on first data.** Add `revealChart(container: HTMLElement, rightInset: number)` in `components/chart/reveal.ts`. It appends an absolutely positioned `div` with `background: CHART_COLORS.bg` over the plot area (inset right by `chart.priceScale('right').width()`, so the axis stays visible). It runs `el.animate([{ transform: 'scaleX(1)' }, { transform: 'scaleX(0)' }], { duration: 240, easing: 'cubic-bezier(0.65,0,0.35,1)' })` with `transform-origin: right`, then removes the `div`. The series appears to draw from left to right. It is transform-only and needs no plugin. Screens call it once after the first `setData`, not on live `update()`. Expose a small `reveal()` from `useChart` so the screens don't each touch the DOM.
2. **Last-price pulse.** For line and area series, set `lastPriceAnimation: LastPriceAnimationMode.OnDataUpdate`, the library's own pulse on the last point that fires only when data updates. That gives live charts a heartbeat that is data-driven rather than ambient. Candlestick series have no equivalent and stay static.

Interval or range change: set the container to `opacity: .45` while the query is fetching, then `transition: opacity var(--dur-2)` back to 1 when data lands. Do not run the shutter again.

Reduced motion: skip the shutter, set `lastPriceAnimation: Disabled`, and keep the opacity dim.

The same shutter applies to `functions/mkt/Sparkline.tsx`. Wrap the svg in a `position: relative` span with a `::after` overlay that runs `scaleX(1 -> 0)` once on the first non-empty `values`. Fade the area-fill path from 0 to .08 after the wipe ends (`animation-delay: 200ms`).

## 7. Shell

### `shell/Pane.tsx`

- Remove the ribbon (V1). The focused pane gets a 2 px crimson hairline along the top edge of the strip: a `::before` with `transform: scaleX(0)` and `transform-origin: left`, which becomes `scaleX(1)` on `.focused`, as a `--dur-3 --ease-move` transition. It is a transition, so quickly moving focus between panes re-targets without stutter. The strip background swaps instantly.
- Body enter: wrap the rendered function in an element keyed on `formatEntry(entry)`. On change, React remounts it and the `pane-in` keyframe (opacity plus `translateY(3px)`, `--dur-2`) plays. For history, pass `data-dir="back|fwd"` from the nav action and use `translateX(-6px)` or `translateX(6px)` instead. No exit animation.
- Lazy chunk loading (`Suspense` fallback): show the fallback only after 150 ms (`animation: appear 0s 150ms both` with opacity 0 to 1), so fast loads never flash a spinner.

### `shell/CommandLine.tsx`

- Suggestion list: `transform-origin: top left`. Open runs `opacity 0 -> 1`, `translateY(-4px) -> 0` over `--dur-2 --ease-out`. Close is the reverse over `--dur-1 --ease-in`. To animate the close, keep the list mounted for 80 ms after `showList` turns false, using a `data-state="open|closing"` attribute and `onAnimationEnd` to unmount. Keyboard input is never blocked during the close.
- Active item: replace the per-item background swap with one absolutely positioned highlight `div` that moves by `translateY(activeIndex * var(--row-h))` with an 80 ms `--ease-move` transition. When arrow keys arrive less than 90 ms apart (key repeat), set `data-fast` on the list, which sets `transition: none`, so the bar tracks the key exactly instead of lagging behind.
- Error: one `nudge` keyframe on the box (`translateX` 0, -3px, 3px, -2px, 0 over 180 ms linear), restarted by keying on an error counter. The red border and error text appear on the same frame. Reduced motion keeps the color and drops the nudge.
- Caret: keep the native caret. Do not add a blinking block cursor, which would be an ambient loop.

### `shell/App.tsx` / StatusBar

- Toasts: `translateY(6px) -> 0` plus opacity, 120 ms in and 80 ms opacity-only out.
- The live indicator in the status bar is the only loop: a 6 px dot running opacity `1 -> .35 -> 1` on a 2 s cycle with `ease-in-out`, and only while the hub is connected and the market session is open. Outside the session, the dot is static. Reduced motion keeps it static.

## 8. `components/watchlist/WatchlistRail.tsx` and `functions/mkt`

Watchlist rail:

- Price and percent cells use `<Num flash>`.
- Current-ticker marker: one shared 2 px crimson bar positioned at the `.current` row's `offsetTop` (measured in a layout effect), moved with a `transform: translateY` transition over `--dur-3 --ease-move`. Changing the focused ticker slides the marker instead of blinking the row.
- A new item enters with `rail-in` (opacity plus `translateX(-4px)`) over 120 ms. A removed item disappears immediately.

MKT:

- IHSG hero: odometer (4.3) plus a tick flash on the change line.
- Macro strip and movers: `<Num flash>`.
- Movers re-rank (`MoversTable.tsx`): FLIP. Before the commit, record each row's `getBoundingClientRect().top` by code. In `useLayoutEffect`, apply `transform: translateY(old - new)` to each moved row, force one frame, then transition to `translateY(0)` over `--dur-3 --ease-move`. A row new to the list fades in instead. Skip the FLIP if more than half the rows moved (a full reshuffle reads as noise), and skip it under reduced motion.
- Breadth or advance/decline bars: `transform: scaleX(r)`, `transform-origin: left`, transition `--dur-4 --ease-move`. Never animate `width`.
- Skeleton to data: the skeleton is static (remove any shimmer loop) and appears only after 150 ms. When data arrives, rows run `row-in` with `animation-delay: calc(var(--i) * 12ms)` for the first 12 rows. Later rows have no delay.

## 9. Boot sequence (once per session, under 900 ms, skippable)

Purpose: confirm what the terminal connected to. It is a status readout, not a splash screen. It is drawn as an overlay above the already-rendered shell, so the app is never delayed: the shell mounts and fetches underneath, and the command line already has focus.

Gate: `sessionStorage['sumire.boot'] === '1'` skips it. This is a new sessionStorage key, and the existing localStorage keys are untouched. Wrap the access in try/catch. Reduced motion skips the boot entirely.

Timeline (`components/boot/Boot.tsx` plus a module with its own keyframes):

| t (ms) | Element | Motion |
| --- | --- | --- |
| 0 | full-screen `--bg` overlay | already opaque, no fade-in |
| 0-160 | Sumire brand mark plus `SUMIRE` in amber mono, letter-spaced | CRT power-on: `scaleY(0.02) -> 1` with `scaleX(1.04 -> 1)`, origin center, `--ease-out`. Played once, this is the single phosphor reference. |
| 160-520 | 4 status lines, staggered 80 ms, each enters with opacity plus `translateX(-4px)` over 120 ms | `LINK   /hubs/quotes ........ OK` (or `RETRY`) / `FEED   YAHOO  247 SYM ....... OK` / `IDX    SESI 2  13:30-15:50 WIB` (from MarketCalendar data, or `CLOSED`) / `DB     ........ OK` (or `OFFLINE`, amber, not an error) |
| 520-700 | the result column resolves | the real result replaces `...` without animation as each check returns. A check still pending at 700 ms shows `...` and the boot proceeds anyway. |
| 700-860 | overlay | `opacity 1 -> 0` over 160 ms `--ease-in`. The panes underneath run `pane-in`, staggered 30 ms per pane. |
| 860 | remove overlay, set `sessionStorage['sumire.boot']='1'` | - |

The status results come from already-running queries (`/api/health`, the hub connection state, quotes count). The boot reads them and never starts its own blocking fetch.

Skip: any `keydown`, `pointerdown` or `wheel` jumps straight to the 700 ms fade-out step, so it finishes 160 ms later. The keydown is not swallowed: the command line already has focus, so the first typed character lands in it.

Accessibility: the overlay is `aria-hidden="true"`. The shell is the accessible tree from frame 0.

## 10. Performance budget

- One live tick changes one `Num`. Memoize table rows by code (`React.memo` on the row with the quote as its prop) so a hub push re-renders only the rows that changed.
- The flash overlay is a composited opacity layer. Expect about 100 simultaneous flashes on a busy update with no layout cost.
- No flash on first mount or after a tab becomes visible again: the `document.hidden` guard plus the `p == null` guard cover this. A tab that wakes up after 10 minutes must not light up every cell at once.
- `will-change` only on the tape track and on the chart shutter while it runs. Nowhere else.

## 11. Reduced motion

Put one global block in `theme/global.css`, plus per-module overrides where needed:

```css
@media (prefers-reduced-motion: reduce) {
  *, *::before, *::after {
    animation-duration: 1ms !important;
    animation-iteration-count: 1 !important;
    transition-duration: 1ms !important;
  }
}
```

Explicit exceptions under reduced motion, because they cause no motion and they carry meaning:

- **Tick flash stays** (opacity only). Modules opt it back in with `animation-duration: var(--dur-4) !important` inside their own reduced-motion block.
- **Pane enter, toast and suggestions** become an 80 ms opacity crossfade with no translate.
- **Tape** is a static scrollable strip. **Odometer, tween, FLIP, shutter and nudge** snap instantly. **Live dot** is static. **Boot** is skipped.

The JavaScript-driven effects (tween, FLIP, shutter, boot) check `window.matchMedia('(prefers-reduced-motion: reduce)').matches` through one shared `reducedMotion()` helper in `web/src/lib/motion.ts`.

## 12. Removal list (V1 motion to delete)

`slash-in`, `ribbon-drop`, `wipe-down`, `wipe-in`, the `--ease-snap` overshoot, any Starburst or calling-card keyframes, skeleton shimmer, and the mascot animations. Each surviving animation defines its keyframes inside its own `.module.css`, which fixes the silently dead global-keyframe references noted on the board.

## 13. Implementation order

1. Tokens and the reduced-motion block (`tokens.css`, `global.css`, `lib/motion.ts`).
2. `Num` flash, then switch the tape, rail and MKT to `<Num flash>`. This is the biggest perceived-quality win.
3. Pane hairline, pane enter and the command-line list and highlight bar.
4. Chart and sparkline shutter, plus `lastPriceAnimation`.
5. Tape constant speed, movers FLIP, the IHSG odometer and breadth bars.
6. Boot sequence.

## Sources

- Apple, *Designing Fluid Interfaces* (WWDC 2018), via the local `apple-design` notes: interruptibility, presentation-value retargeting, critically damped defaults, spatial consistency, reduced-motion equivalents.
- Financial UI patterns notes (mdskills.ai, "financial-ui-patterns"): tinted tick flash in CSS on the changed cell only, never color alone (keep the arrow and sign), isolate updating cells. https://www.mdskills.ai/skills/financial-ui-patterns
- lightweight-charts discussion #1392: no native series draw-in; new points render immediately, so a reveal must happen outside the library or in a plugin. https://github.com/tradingview/lightweight-charts/discussions/1392
