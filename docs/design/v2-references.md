# V2 visual references

Research notes for the v2 redesign. The target is a premium, faithful Bloomberg-terminal feel, without the comic or Persona look. Each section lists concrete details we can reuse.

Hex values marked `~` were sampled by eye from screenshots (JPEG, scaled). Treat them as starting points to tune, not as official specs. Sources that blocked fetching (bloomberg.com returns a bot check, godelterminal.com/docs returns 403) were viewed as screenshots or through search snippets instead.

---

## 1. Bloomberg Terminal (classic panel screens)

Sources:
- Markets Monitor / RMB Bond Suite screenshot, Bloomberg press image: https://assets.bbhub.io/company/sites/51/2019/07/RMB-Bond-Suite-Landing-Page.jpg
- "Precious Metals Prices and Rates" screen (screenshot reached through a Bing image search)
- Bloomberg UX, "Designing the Terminal for Color Accessibility" (2021): https://www.bloomberg.com/ux/ (bot-gated; read through search summary)
- Tech at Bloomberg, "How Bloomberg Terminal UX designers conceal complexity": https://www.bloomberg.com/company/stories/how-bloomberg-terminal-ux-designers-conceal-complexity
- Accessibility page: https://www.bloomberg.com/ux/accessibility/
- Font note: https://github.com/jx22/berg (Bloomberg Prop Unicode N, by Matthew Carter)

Observed structure, top to bottom:
1. **Command / security bar.** A solid amber-orange field (`~#F39C12` to `~#FB8B1E`) with black text holds the loaded security ("China"), followed by a `▾` dropdown. Next to it, dark-red slabs (`~#8B1A1A`; the red-pill "Browse", "98) Settings") hold actions in white text. The screen title ("Markets Monitor") is right-aligned in white on the same red bar. Our crimson `#C8102E` fits this role.
2. **Numbered tab strip.** Mid-grey tabs (`~#3C3C3C`) with a subtle top-to-bottom gradient hold white labels prefixed with a number and a parenthesis: `1) Foreign Exchange  2) RMB Overview  3) CNH`. The selected tab is a **blue fill** (`~#1F5FAF`) with white text. A second row holds sub-tabs. The numbers are real shortcuts: typing `6 <GO>` selects tab 6.
3. **Panes.** Pure black (`#000`). Each pane has a 16px header strip in dark grey (`~#262626`). It holds a small chart-type glyph at the left and an "expand / pop-out" glyph (an orange square with an arrow) at the right. Panes are separated by 2-3px black gutters, with a 1px grey edge (`~#3A3A3A`) and no rounded corners anywhere.
4. **Column headers.** These are white or light grey, right-aligned over numeric columns, and set on a slightly lighter band (`~#1A1A1A`).
5. **Group labels inside tables.** Group labels such as "Govt Benchmark", "CFETS Key Rates", "FX" and "Eq Indices" are **sky blue / cyan** text (`~#4FA3E0`) on black, left-aligned, and they replace a row. This is how Bloomberg groups without boxes.
6. **Values.** Row labels (tenors, tickers) are white. Primary values are **amber** (`~#F5A623`), and secondary or reference values are white. Negative changes are bright red (`~#FF3B3B`) with a leading `-`. Positive changes are white or green, depending on the screen. Numbers use a monospaced, tabular font, and decimals align vertically.
7. **Function links list.** Function links appear as `33) BTMM   Treasury Monitor`: the number is white, the 4-letter mnemonic is white, and the description is amber. This is the canonical "related functions" footer, and it is directly reusable as our "related functions" footer per screen.
8. **News rows.** Each row has a numbered index, an amber headline that is truncated, a source code in white (`BFW`, `BN`), and a time in `HH:MM`. A small `B` badge marks Bloomberg-original stories.
9. **Charts.** Charts sit on a **dark navy-grey gradient** (`~#1B2430` to `~#0E141B`), not pure black. They use dotted grid lines, a 1.5px white or green series line, and a right-side y-axis with the last value in a highlighted tag. Small "Track" and "Zoom" toggle buttons float top-left on the chart.
10. **Red header bar with numbered menu buttons.** Seen on PMP-style screens: `9) View ▾  2) News  3) Save as Default` as white text on red, with one blue-filled action ("4) Feedback").

Font and type:
- The original Terminal font was a **9x19 monospaced bitmap** that was hand-copied in the late 1990s and later converted to TrueType. Matthew Carter drew a proportional successor, *Bloomberg Prop Unicode*, around 2007. When the Terminal changed fonts, users pushed back hard. The takeaway is that type is the product: we should pick one mono face and tune it, not swap faces per screen.
- The rows are dense, about 16-17px at a 12-13px font size, with no zebra striping on most screens. Row separation comes from alignment, not lines.

Color accessibility:
- Bloomberg estimates about 20,000 users with color vision deficiency (CVD) and ships alternate schemes for deuteranopia and protanomaly (`PDFU COLORS <GO>`). The lesson for us is to **never encode up/down by color alone**. Always show a sign (`+`/`-`) or an arrow, and consider an optional CVD palette (for example blue up, orange down).

Keyboard:
- Bloomberg's yellow sector keys (Equity, Govt, Corp, Index, Curncy, Comdty) select the market. The green key is `<GO>` (action), and the red key is Cancel/Esc (stop). Tickers are typed as `BBRI IJ Equity <GO>`, then a function is typed (`GP <GO>`). Function mnemonics are 2-4 uppercase letters.

## 2. Godel Terminal (modern browser terminal)

Sources: https://godelterminal.com (landing-page product screenshot, 2026), https://godelterminal.com/docs (command reference; 403 to fetch, summary from search).

- Background is near-black (`~#0A0A0A`). Each window is a pane with a **1px grey border** (`~#2A2A2A`) and a **one-line 14px title bar**: the function name, a link glyph, and the ticker ("Description 🔗 PLTR US"), with tiny window controls at the right.
- A thin **green accent rule** (`~#1FBF75`) sits at the top of the workspace, and the landing page uses a green-on-black theme. Bloomberg's amber is *not* used, which is how Godel differentiates itself.
- Quote Monitor has **filter chips** as tabs ("Technology / Growth / Macro / ETFs / Futures"), with the active one as a filled green chip. Columns are `Ticker Last Bid Ask Chg% Volume Latency`. The **Latency column** (for example "10ms") makes data freshness visible, which is a nice, honest detail.
- Numeric cells are colored text, and on update the **whole cell gets a short red or green background flash**.
- The security description pane has a summary line in mono at the top (`PLTR US ▲ $135.48 -0.34 -0.25% Vol 20.4M ... At 14:01:04`), with the stats in a right-hand "SNAPSHOT" column. Section labels are small caps in grey, values are white, and the column is right-aligned.
- Single-quote **tiles**: big ticker, white price, and a colored change line beneath with both % and absolute change (`-0.07% -0.50`).
- A rating table shows the target range as a **colored filled cell** (green or red background with white text) rather than colored text.
- The command input is focused with the backtick key. Double `Esc` closes a window, `Tab`/`Shift+Tab` cycles windows, `F1` opens HELP, and `Cmd+Z` reopens the last closed window. Shift+arrows moves a window and Option+arrows resizes it.
- The docs list commands as **pills** that open their documentation.
- The landing page shows a **radial command wheel**: a ring of capability names in mono with a glowing node orbiting it. It's too decorative for the app, but a good boot-screen idea.

## 3. Hyperliquid (pro trading screen)

Source: https://app.hyperliquid.xyz/trade (live, viewed 2026-10-24).

- The background is a desaturated teal-black (`~#0F1A1F`), with pane separators 1-2px darker. There is a single accent (mint `~#50D2C1`), used on the active tab underline, the primary button and positive funding.
- **Order book rows are about 20px**. Price is colored (red `~#ED7088` for asks, green `~#1FA67D` for bids), and size and total are white and right-aligned. **Depth is drawn as a translucent bar behind the row**, growing from the right, with about 15% opacity of the side color. The cumulative total is a second, lighter bar. This pattern fits our breadth, volume and screener columns.
- The spread row is centered on a slightly lighter band: `Spread 0.002 0.003%`.
- The header stat strip has small grey labels with **dotted underlines (meaning a tooltip is available)** above white values (`Mark 75.605 · Oracle 75.644 · 24h Change -2.508 / -3.21%`). The funding countdown ticks every second.
- The status bar has an `● Online` pill bottom-left, with a green dot and a thin green border. This is a cheap, honest connection indicator.
- The title-bar `<title>` carries the live price ("75.596 | HYPE | Hyperliquid"). We can do the same with the IHSG level.

## 4. Refinitiv Eikon / LSEG Workspace, Koyfin, OpenBB, TradingView (from prior knowledge, not re-captured)

- **LSEG Workspace:** a dark slate (`~#1E2328`) rather than black, with a blue accent. Apps are tiled with thin tab headers, and the "app search" box at the top is the main entry point. Lesson: one global command box is the main way to get anywhere.
- **Koyfin:** dark navy (`~#131722`-ish). Dashboards are made of widgets with compact 24px headers, and a **heat-colored % column** goes from red to green on a continuous scale with white text. Its typography is less dense than Bloomberg's but very clean.
- **OpenBB Terminal/Workspace:** OpenBB's terminal identity is **orange on black** (`~#FFA500`) with mono everywhere, and its widgets have a small header showing the source and "last updated". The lesson is that source and freshness metadata belong in every pane header.
- **TradingView pro layouts:** charts carry a **last-price label pinned on the right axis**, colored by the last bar's direction, with a dashed horizontal line across the chart. Crosshair labels sit on both axes in solid filled boxes. Panes resize with 1px splitters that highlight on hover.

## 5. High-craft instrument UIs (non-finance)

**Aviation glass cockpit (Boeing/Airbus PFD/ND)**. Sources: https://pmflight.co.uk/a320-primary-flight-display/, https://flyawaysimulation.com/ask/answers/boeing-737-pfd-navigation-display-symbols/, https://thepointsguy.com/guide/a-look-inside-the-boeing-787-dreamliner-flight-deck/
- Color is a **semantic grammar, not decoration**. Magenta means commanded or target, green means active or engaged, white means scales, labels and current state, cyan means selectable or pilot-selected, amber means caution, and red means warning. Translated to Sumire: crimson means focus or selection, amber means labels and caution, white means live values, cyan means interactive links (as Bloomberg's blue group labels do), and green/red mean direction only.
- **Rolling digit drums**: altitude and speed tapes scroll digits vertically, and only the changing digits move. This is a very high-craft way to animate price changes (`transform: translateY` on per-digit columns, about 120ms).
- **Boxed values when something changes state**: a mode annunciator gets a white box for about 10s after a mode change. Our equivalent is a 1px box that outlines a value for a short time after it changes, instead of glowing.
- "Quiet and dark" (Boeing) / "lights out" (Airbus): in normal state nothing is highlighted, and the display only lights up what is abnormal. Lesson: only stale data, alerts and the focused pane get emphasis.

**F1 timing / telemetry graphics (broadcast timing tower, team pit-wall screens)**
- The timing tower uses **purple for overall best, green for personal best and yellow for slower**, which conveys a lot of meaning in very little space. A sector-colored cell (filled background with black text) beats colored text for scan speed.
- Gaps are right-aligned with a `+` sign and fixed decimals (`+1.234`), and the leader row shows `Interval` or `Leader`.
- Position changes animate as **row slides** (translateY about 200ms) when the order changes. This is directly usable for MOST/TOP-style movers lists.

**NASA mission control (MOCR / modern FCR consoles)**
- These are rows of **identical consoles with strict label/value grids**, and every value has a fixed-width slot, so nothing reflows. Consoles have discrete, physical-looking status lights with labels. Lesson: our status bar can use a row of small labeled lamps (`FEED`, `DB`, `HUB`, `RSCH`) that are green, amber or red, each with a 2-3 letter label.

**Teenage Engineering (OP-1 field, TP-7, website)**. Sources: https://www.thisispaper.com/mag/op-1-field-teenage-engineering, https://read.cv/jdsimcoe/inside-design-at-teenage-engineering
- **Four-color hardware/screen coupling**: blue, green, white and orange encoders light up the matching on-screen element. This maps directly onto Bloomberg's colored keys. Our function-key chips (the yellow sector key, the green GO, the red cancel) should use the *same* colors that their on-screen targets use.
- The marketing site uses bitmap/pixel fonts at thin weight on black, with generous negative space around a tight grid. Small uppercase labels with wide tracking sit next to large numerals.

**Linear (precision UI)**. Sources: https://open-design.ai/plugins/design-system-linear-app/ (third-party token analysis)
- It uses a near-achromatic palette with one accent, used only for active state, CTAs and brand. It has many tuned weights (Inter 510 as the "medium"), and display sizes get negative tracking. Lesson for us: **tune weights and tracking per size**. Labels get small caps with `letter-spacing: 0.06em`, and big numbers get `-0.01em`. Use `font-variant-numeric: tabular-nums slashed-zero` everywhere.

---

## Concrete spec suggestions for Sumire v2

These suggestions are derived from the references above.

| Token / rule | Value | Why |
|---|---|---|
| `--bg` | `#000000` (panes) / `#0A0A0A` (gutters, app bg) | Bloomberg panes are pure black; gutters a hair lighter make panes read as tiles |
| pane border | 1px `#2A2A2A`, focused pane 1px `--crimson` + crimson header underline | Godel border, Bloomberg red as brand |
| pane header | 18px tall, `#1A1A1A`, 11px uppercase mono, tracking 0.06em, amber title, white ticker, right side: source + freshness + pop-out glyph | Bloomberg/Godel/OpenBB |
| row height | 18px (dense) / 22px (comfortable); font 12px mono; no zebra; 1px `#141414` rule every 5 rows optional | Bloomberg density, "rule every N rows" from printed price tables |
| group label rows | cyan-blue `#4FA3E0` text, no box | Bloomberg in-table grouping |
| primary value | amber `#FFB000`; reference values white `#F2F2F2` | Bloomberg value colors |
| change | `+`/`-` always, up `#22C55E`, down `#F43F5E`, flat `#8A8A8A` | CVD-safe with sign |
| command bar | amber field `#FFB000` with black text for the loaded security, crimson slab for screen title | Bloomberg security bar |
| numbered actions | `1)` `2)` prefixes on tabs and related-function links, typing the number activates | Bloomberg tab/links convention |
| chart bg | subtle vertical gradient `#11161D` to `#07090C`, dotted grid `#1F2730`, last-price tag on right axis | Bloomberg/TradingView charts |
| tick flash | full-cell background 20% up/down color, fade 600ms; value text stays white | Godel / Bloomberg |
| status lamps | 6px square lamps + 2-3 letter labels in footer | NASA consoles, Hyperliquid Online pill |

---

## Ranked ideas: premium and unique, not comic and not generic

1. **Bloomberg-faithful security bar.** The loaded ticker sits in a solid amber field with black mono text (`BBRI IJ ▾`), and the function title sits right-aligned on a crimson slab. It's the most recognizable terminal chrome, and the crimson slab doubles as our brand moment.
2. **Numbered everything.** Tabs, related functions and screener presets get a `1)` `2)` prefix that works as a keyboard shortcut (type `3` then Enter). Every screen ends with a Bloomberg-style "Function links" footer (`12) FA  Financial analysis`), with a white code and an amber description.
3. **Rolling-digit price changes** (aviation altitude tape). On a quote update, only the digits that changed roll vertically in their fixed slot, in 120-160ms, using `transform` only. Pair this with a 1px up/down colored underline that fades over 600ms. This is very distinctive and it isn't glow.
4. **Whole-cell tick flash.** A cell's background flashes at 20% of the up/down color and decays over 600ms, while the text stays white, so it can always be read. This is honest, data-driven motion, and it uses opacity on a pseudo-element.
5. **Cyan group-label rows instead of boxes.** Group tables like Bloomberg does, with cyan-blue (`#4FA3E0`) label rows inside one table (Banks / Telco / Coal), rather than nested cards. This cuts chrome and adds a second, functional accent hue.
6. **Pane header as an instrument strip.** Each header carries the function code, the ticker, the data source code (`YF`, `DB`), a live freshness age (`4s`, which turns amber when stale and red when dead) and a pop-out glyph. Freshness becomes a first-class visual, as Godel's latency column and OpenBB's "last updated" do.
7. **Depth bars behind rows** (Hyperliquid). For volume, breadth, screener score and relation strength, draw a translucent bar (about 12% opacity) growing from the right edge behind the numbers instead of a separate sparkline column. It keeps density and adds meaning.
8. **Semantic color grammar, documented**, borrowed from cockpit displays. Crimson means focus or selection, amber means labels and caution, white means live values, cyan means navigable links and groups, and green/red mean direction only. Everything else is grey. Write it into tokens.css comments so screens stay consistent.
9. **"Quiet and dark" emphasis.** In the normal state the screen is calm and monochrome amber/white. Only abnormal states light up: stale feed, ARA/ARB hit, an alert or the focused pane. ARA/ARB hits get a filled cell (white text on a crimson or green fill) like the F1 purple/green sector cells.
10. **Boot sequence** (once per session, skippable with any key, and skipped entirely under reduced motion). The Sumire mark comes first, followed by mono lines that type out real checks (`FEED .... OK 182ms`, `DB .... DOWN`, `HUB .... OK`, `CAL .... IDX OPEN 09:00-12:00 WIB`), and then the panes wipe in left to right with a 30ms stagger. It runs in 600-900ms total and reports real health, not fake loading.
11. **Status-bar lamps** (NASA consoles). The footer holds square 6px lamps with 2-3 letter labels (`FEED` `DB` `HUB` `RSCH` `MKT OPEN`), followed by a WIB clock with seconds and the session countdown (`BREAK IN 00:41:12`). Only the live lamp pulses.
12. **Colored function keys that match their targets** (Teenage Engineering coupling). Key chips in HELP and in the command line hint use yellow for sector/market and green for `GO`, and red for `CANCEL`. When the user presses one, the matching chip briefly inverts, as physical keycap feedback.
13. **Chart craft.** Charts draw in left to right over 240ms on load (clip-path inset, which is effectively transform-only), sit on a navy-black vertical gradient behind them with a dotted grid, show a last-price tag on the right axis colored by direction with a dashed price line, and show the crosshair value in filled boxes on both axes.
14. **Row reorder slides** (F1 timing tower). In movers, MOST and the watchlist sorted by change %, rows slide to their new positions (FLIP technique, about 200ms) when the ranking changes, instead of jumping.
15. **Typographic precision.** Use one mono face for data (JetBrains Mono or IBM Plex Mono via Google Fonts) with `tabular-nums slashed-zero`. Labels are 10-11px uppercase with 0.06em tracking, and hero numbers are 28-32px with -0.01em tracking. Decimals always align, and negative numbers keep the sign column. The live IHSG level is also shown in `document.title`.

## Avoid list

- Comic or Persona remnants: ransom letters, starbursts, calling cards, halftone/screentone, cut-paper slants, offset "plate" shadows and display serifs.
- Rounded cards (`border-radius` > 2px), soft drop shadows, glassmorphism and backdrop blur.
- Purple/blue gradients, neon glow on text, ambient looping animations (except the live lamp), and parallax.
- Up/down shown by color alone. Always add a sign or arrow.
- Colored text on colored backgrounds with low contrast. Filled cells must use white or black text at AA contrast or better.
- Proportional figures in any numeric column, centered numbers, and mixed decimal counts within one column.
- Zebra striping combined with grid lines. Pick at most one; preferably neither, and use alignment instead.
- Emoji or generic icon-font icons. Use text codes (`GP`, `DES`) and a few hand-drawn 1px glyphs.
- Swapping fonts between screens (the lesson from Bloomberg's font backlash). Use one data face and one optional UI label face.
- Fake loading theatrics. Motion must reflect real data or real state, and run 80-240ms, except tick decays.
