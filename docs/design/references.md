# Design references

Input for the visual pass on `web/`. Each entry gives the source, the idea we can take from it, and where it goes in Sumire Platform. The baseline tokens are in `web/src/theme/tokens.css` (`--bg #0b0b0f`, `--amber #ffb000`, `--crimson #c8102e`, `--up #22c55e`, `--down #f43f5e`) and in implementation-plan.md §7.4/§7.5. When a source gave no exact values, the values below are our own choices and are marked *(ours)*.

## 1. Bloomberg Terminal

### 1.1 Black and amber is the brand
- Source: Bloomberg design leads, interview at https://digitalcontentnext.org/?p=14856 and the Tech at Bloomberg posts https://www.bloomberg.com/company/stories/how-bloomberg-terminal-ux-designers-conceal-complexity (403 to fetch, quoted via search): "amber is our base font color", and the black/amber scheme is something they "protect as part of our brand".
- Unofficial palettes: https://madegooddesigns.com/?p=99813 gives screen black `#0B0B0B`, amber `#FFA028`, deep amber `#7A4A00` and cream `#FFF4DE`. https://www.webdesignhot.com/design.md/bloomberg/ gives orange `#fa7900`, flash amber `#ffb800` / `#ffe066`, and a dark surface stack of `#0d0d0d` / `#1a1a1a` / `#262626` / `#333333`.
- Idea: amber is for **labels and chrome**. Values are white, and only direction gets color. Our `--amber #ffb000` sits between Bloomberg amber (`#FFA028`) and P3 phosphor (`#ffb700`, see 3.2). Keep it.
- Mapping: add `--amber-deep: #7a4a00` *(from madegood)* for hairline rules, inactive tab text and halftone dots on black, so we stop using grey `#26262f` for every border. Grey borders on black are one of the things that make the UI look templated.

### 1.2 Colored function keys
- Source: https://en.wikipedia.org/wiki/Bloomberg_Terminal, university guides (https://guides.lib.byu.edu/bloomberg/KeyFunctions, https://www.lib.sfu.ca/system/files/32883/bloomberg_getting_started_booklet.pdf). Esc is red and named **CANCEL**, Enter is green and named **GO**, and the yellow keys along the top are **market sectors** (GOVT, CORP, EQUITY, INDEX) used as suffixes: `MSFT <EQUITY> GO`. The keys were renamed from F10 to names and colors so users don't have to memorize them.
- Idea: render keyboard hints as physical **keycaps** with a role color, not as grey `<kbd>` pills.
  - Action key (GO / Enter): bg `#1f8a3a`, text `#000` *(ours)*
  - Cancel (Esc): bg `#c8102e` (crimson, brand), text `#fff`
  - Sector/function key: bg `#ffb000`, text `#000`, 1px bottom border `#7a4a00` for a slight 3D edge
  - Size: 16px high, 4px side padding, `--fs-xs` uppercase, radius 1px, no shadow.
- Mapping: command line hints ("BBCA DES <GO>"), the HELP screen, empty states ("Press `/` to search"), and the function-code chips in the rail (MKT, DES, GP ... in yellow sector-key style).

### 1.3 Monospace grid and the 9x19 font
- Source: Tech at Bloomberg (https://www.bloomberg.com/company/?p=28086, via search): the original terminal font was a **9x19 monospaced** bitmap that was hand-copied pixel by pixel in the late 1990s and converted to TrueType around 2007. Matthew Carter later designed Bloomberg Prop Unicode (https://github.com/jx22/berg).
- Idea: build tables on a character grid. Row height = 1 line, 18-20px for 12-13px mono. Columns are right-aligned for numbers with `font-variant-numeric: tabular-nums slashed-zero`, and labels are left-aligned and uppercase amber.
- Mapping: `Table` rows at `height: 20px`, `line-height: 20px`, cell padding `0 6px`. No zebra striping. Use a 1px `--amber-deep` rule every 5 rows instead, like ledger paper *(ours)*.

### 1.4 Density and hidden complexity
- Source: same Bloomberg post, where the UX framework manager says the "tight, information-dense screen" makes hierarchy hard, and the CTO's line "conceal it from the user". Core77 (https://www.core77.com/posts/24893/Moneymaking-Multi-Monitor-Mayhem-and-Why-Some-Prefer-Interface-Design-That-Sucks): power users treat the clunky black/orange look as a badge, so consistency matters more than polish.
- Idea: show hierarchy through **type and color roles**, not whitespace: amber uppercase labels, white values, green/red deltas and crimson only for brand/active. Keep pane padding at 6px (`--gap`).
- Mapping: don't add padding during the redesign. Any new decoration must fit in the existing header or empty-state areas.

### 1.5 Title bars
- Bloomberg panels have a thin title strip: function code, security, then menu numbers ("1) Actions 2) Settings"). (From product screenshots and the guides above. There is no published spec.)
- Idea: panel header = `[CODE]` keycap + title in condensed display font + right-aligned freshness / menu items numbered `1)` `2)` in amber.
- Mapping: `Panel` header, with the P5 slant applied (see 2.2).

## 2. Persona 5 / P5 Royal

### 2.1 Palette: crimson, black, white, plus one off-palette highlight
- Source: art lead interview (Famitsu, summarized at https://segabits.com/?p=54918): the main color is a **crimson red**, and black and white text was tested against it. Pause menu analysis at https://mechanicsofmagic.com/2022/04/23/visual-design-of-games-persona-5/: the **selected item grows and turns a contrasting blue**, which stands out because it is the only off-palette color on screen.
- Idea: one off-palette "selection" accent that only appears on the focused item. For us, the off-palette color is **amber** on a crimson field (amber is already ours). Don't add blue.
- Mapping: the active rail item and the command palette selection get bg `#c8102e`, text `#ffb000` and weight 700, are scaled 1.04 on the x axis with `transform-origin: left`, and are rotated -1deg. No other element uses crimson as a fill.

### 2.2 Slanted, cut-paper shapes
- Source: https://inverse.com/gaming/persona-5-tactica-user-interface describes a "cut-out, do-it-yourself look", "multiple moving axes and dynamic baselines". https://www.siliconera.com/what-makes-the-ui-and-menus-in-persona-games-so-good/ notes that every menu and submenu shares one consistent vibe.
- Idea: use `clip-path` polygons, not rounded rectangles. Our angles *(ours)*:
  - Panel header tab: `clip-path: polygon(0 0, 100% 0, calc(100% - 10px) 100%, 0 100%)`, a 10px slant on the right only (asymmetric).
  - Active tab: parallelogram, `skewX(-12deg)` on the background pseudo-element with the text left unskewed.
  - Alert toast: an irregular 5-point polygon `polygon(0 4px, 100% 0, 98% 100%, 2% calc(100% - 3px))`, so it looks hand-cut.
  - Offset "shadow" plate: a second solid black or crimson copy shifted `4px 4px`, never a blurred box-shadow.
- Mapping: Panel header, tabs, Violet alerts, the BRIEF title card.

### 2.3 Ransom-note typography, used sparingly
- Source: Robin Rendle (https://robinrendle.com/adventures/video-games-and-wiggletech/): a mix of upper and lower case, sans, slab and serif, stretched at giant sizes or squished small, "set with wood type and printed incorrectly", like a punk gig poster. Ransom-note basics: https://kittl.com/article/ransom-note-letter-designs.
- Counterpoint (Inverse, above): P5's type puts style over information. Tactica moved to a rational sans for readability, keeping stable baselines for text that has to be read.
- Idea: ransom-note treatment **only for display words of 1 to 3 words** (screen titles, the "VIOLET" alert label, the login/splash wordmark, empty-state headlines). Never for data or body text.
  - Per-letter spans alternate between three faces already in reach via Google Fonts *(ours)*: Anton (condensed sans), Bebas Neue, and a serif such as "Playfair Display" 900 or "DM Serif Display".
  - Each letter gets a fixed (not random per render) rotation from a set of {-4, -2, 0, 2, 3}deg, a vertical offset from 0 to 2px, and alternating white-on-black / black-on-white / black-on-crimson boxes with 2px padding.
  - Use a deterministic pattern seeded by the string, so the same title always looks the same.
- Mapping: a `RansomText` component for `SUMIRE` in the top bar, the `VIOLET` alert label, and the screen title in big headers. Data stays in JetBrains Mono.

### 2.4 Calling Card
- Source: the calling card in the game is a red and black card with cut-out lettering and the Phantom Thieves top-hat logo (https://twinfinite.net/?p=271303). The *P5/P5R Official Design Works* book (https://thevideogamelibrary.org/book/persona-5-official-design-works) is the authoritative art reference.
- Idea: a "calling card" layout for **high-severity, rare events** only: a crimson card, an inner black 2px border inset by 4px, ransom-text headline, body in mono white, and a ticker in an amber keycap. Rotated -2deg and slid in from the right in 180ms *(ours)*.
- Mapping: Violet alert when an alert triggers (MA200 cross, ARA/ARB hit), and the INS insight "headline" card. Not for routine toasts.

### 2.5 Menu motion
- Source: Persona director Hashino (https://theouterhaven.net/2024/10/persona-director-notes-that-beloved-in-game-feature-is-actually-annoying-to-make): each menu has its own design and program, which is expensive. The feel is snappy, overshooting and directional.
- Idea: only two motions, both under 200ms *(ours)*:
  - "Slash in": translateX(-8px) skewX(-8deg) to 0 over 140ms, `cubic-bezier(0.2, 0.8, 0.2, 1)` (our existing `--ease`).
  - "Stamp": scale 1.15 to 1 and opacity 0 to 1 over 120ms for a newly triggered alert label.
- All motion off under `prefers-reduced-motion: reduce`. No looping animations except the glasses spinner, which reduced motion swaps for a static glyph plus "Loading".

### 2.6 Halftone and screentone
- Source: P5's comic/print look (the cut-out DIY description in the Inverse article) and manga screentone in general.
- Idea: CSS-only halftone, no images *(ours)*:
  - `background-image: radial-gradient(circle, #7a4a00 1px, transparent 1.2px); background-size: 6px 6px;` at about 35% opacity on black.
  - A diagonal fade: mask the dots with `linear-gradient(135deg, #000 0%, transparent 60%)` so the tone falls off from one corner (asymmetric, like a hand-applied screentone sheet).
- Mapping: empty states, behind the mascot bust, in the panel header's right-hand gap. Never behind numbers.

## 3. Other terminals and retro displays

### 3.1 Teletext / Ceefax
- Source: https://en.wikipedia.org/wiki/Teletext and the BBC BASIC teletext manual (https://www.bbcbasic.co.uk/tccgen/manual/tcgen2.html): a 24x40 grid (Ceefax 32 columns), 6 colors + black + white, control codes take a cell, **sixel** mosaic graphics (2x3 blocks per cell), and double-height text. Typography history: https://www.typeroom.eu/article/teletext.
- Idea: (a) **page number header** like Ceefax "P100": each function screen shows its code and a 3-digit page in the top-right corner (`MKT 100`, `DES 210`), in amber on black and uppercase. (b) Sixel-style **block bars** (U+2580 to U+259F block characters) for tiny inline breadth bars and volume bars in tables. They render in the mono font with no SVG.
- Mapping: Panel header page code. MKT breadth row (adv/dec as `████▌░░░`). SECT heat values.

### 3.2 CRT amber phosphor
- Source: https://en.wikipedia.org/wiki/Monochrome_monitor; the 602nm P3 amber phosphor converts to about `#ffb700` (https://publish.obsidian.md/xybre/permalink/e397c38e-aa6f-45eb-831f-f02a3836abc4). DOSBox Staging has an "amber" CRT profile (https://www.dosbox-staging.org/0.83/manual/graphics/rendering/colour-and-image-adjustments/).
- Idea: our `#ffb000` is already phosphor amber. Use at most **one** subtle CRT cue: a 1px scanline overlay `repeating-linear-gradient(0deg, rgba(0,0,0,.18) 0 1px, transparent 1px 3px)` on the top bar only, or `text-shadow: 0 0 2px rgba(255,176,0,.35)` on the big IHSG number. Not both, and not everywhere.
- Mapping: the IHSG headline value on MKT and the top-bar clock.

### 3.3 Modern trading UIs (Koyfin, OpenBB, generic)
- Source: trading dashboard review https://adminlte.io/blog/trading-dashboard-design-examples/: interfaces are "denser than anything a usability guide would approve", almost always dark, and "colour is reserved, since red and green mean direction". A design-system listing (https://open-design.ai/systems/trading-terminal/) uses a `#050505` base, 1px grid lines and monochrome highlights. OpenBB widgets as containers: https://stg.openbb.co/blog/a-step-by-step-guide-to-building-custom-crypto-applications-with-openbb.
- Idea: confirm the rule **green/red mean direction only**. Use a 1px grid between panes (`gap: 1px` on a `--amber-deep` or `#1a1a1a` background) instead of gaps with card borders, so the layout reads as one instrument, not floating cards.
- Mapping: the app shell grid and multi-pane screens (MKT, DES, GP).

## 4. What makes UI look "AI generated"

- Source: https://21st.dev/blog/website-not-look-ai-generated: tells are purple-to-blue gradient heroes with soft glow, three equal feature cards with matching icons, the default palette and fonts, symmetrical equal-cell grids with no hierarchy, no specific numbers or names, and generic copy that works for any product. Fixes: a deliberate palette from one reasoned color, typefaces chosen on purpose, **"break the symmetry once"**, show real data, a named voice, and adapting borrowed components with your own tokens.
- https://dev.to/lyd_lyd_d4869af25d33ce0eb/why-do-ai-generated-websites-all-look-the-same-k2j and https://managed-code.com/blog-post/why-ai-websites-look-the-same: models produce the statistically average design, plus glassmorphism, identical card layouts, uniform spacing and the same animations. Busy-looking elements "that look like they're doing work but aren't".
- https://shuffle.dev/blog/?p=2275: vague prompts lead to sameness, and the fix is specific references and constraints.
- How this applies to us:
  - The palette has a reason: amber = Bloomberg phosphor, crimson = Sumire's hair and the Shujin uniform. Neutrals are derived from amber (`#7a4a00`, warm blacks such as `#0b0a08`) rather than cool blue-greys (`#121218`, `#17171f`, `#26262f` are slightly blue, which reads as a default dark theme). Consider shifting the panel tokens warm *(ours)*: `--panel #110f0c`, `--panel-2 #16130f`, `--border #2a2218`.
  - Uniform `border-radius` on every box is a tell. Use 0 radius everywhere. 1px only on keycaps.
  - Spacing should not be uniform. Keep 6px inside panes and 1px between panes, and give the one hero element (the IHSG number, the mascot) generous space.
  - Copy has a voice: Sumire's speech bubble and empty states speak as her ("No alerts yet. I'll keep watching BBCA for you.") and name real tickers and times in WIB.
  - Use one off-axis element per screen (the slanted header, a rotated stamp), not tilting everything.

## 5. Hand-made mascot design

- Sources: silhouette and palette guidance at https://madegooddesigns.com/character-design-basics/ and https://madegooddesigns.com/mascot-logo-design/; https://www.toonsmag.com/how-to-design-a-cartoon-mascot-for-your-brand/; https://adobe.com/creativecloud/design/discover/mascot-logo-design.html.
  - **Silhouette test**: fill the character solid black. It must still read. Exaggerate the distinctive features so the outline carries the identity.
  - **Tight palette**: one dominant color, one or two secondaries and one accent. 3 to 4 colors reproduce reliably.
  - **Consistent line weight**, vector, plus a **stripped-down head or silhouette variant** for small sizes (favicon).
- Character facts (https://en.wikipedia.org/wiki/Kasumi_Yoshizawa, https://giantbomb.com/wiki/Characters/Kasumi_Yoshizawa, https://samurai-gamers.com/persona-5/kasumi-yoshizawa-character-profile/, https://megatenwiki.com/wiki/Violet): designed by Shigenori Soejima, who used a photo of a ponytail as a design seed. She is a rhythmic gymnast. "Sumire" means violet (*Viola mandshurica*), hence the codename **Violet**. Thief outfit by Azusa Shimada: a black mask, black strapless leotard and jacket, with a rapier and a lever-action gun. The red hair, red-framed round glasses and hair ribbon used in our plan come from the in-game art. The sources above don't state them, so check against the Official Design Works book before treating exact hues as canon.
- How this maps to our original mascot (not a traced game asset):
  - **Silhouette carriers**: (1) a long high ponytail swinging to one side (asymmetric, the Soejima seed), (2) **round glasses** as two clear circles, (3) a ribbon bow at the ponytail tie. Those three must survive the black-fill test at 48px.
  - **Palette (4 + ink)** *(ours)*: hair crimson `#c8102e` with a shadow of `#7d0a1c`, skin `#f6dcc8` with a shadow of `#e2b9a0`, uniform black `#141414` with a white collar `#f2f2f5`, accent amber `#ffb000` (only on the ribbon edge or a glasses glint, tying her to the terminal). Ink lines `#0b0b0f`.
  - **Line**: one stroke weight (2px at the 120px render) with `stroke-linejoin: miter`. Corners are sharp, not round, to match the P5 cut-paper language. Shadows are flat cel shapes (one tone each), not gradients.
  - **Expressions** (§7.5) are done with eyebrows, mouth and glasses glint only, swapped as SVG groups, so the silhouette never changes.
  - **Frame**: an asymmetric slanted crimson plate behind the bust (10deg cut), halftone in one corner, and the speech bubble as a cut-paper rectangle with a hard 3px offset black plate and a sharp triangular tail. No rounded bubble.
  - **Small variant**: a head with glasses and ponytail only, 2 colors (crimson + ink), for the favicon and collapsed state.

## 6. Shortlist of motifs, ranked by fit

1. **Warm-black surfaces + amber-derived neutrals** (`#0b0a08`, `#110f0c`, `#2a2218`, `#7a4a00` rules). Removes the default cool dark theme tell at zero UX cost. (1.1, 4)
2. **Role-colored keycaps** (GO green, CANCEL crimson, function yellow) for every keyboard hint. Pure Bloomberg and reinforces the keyboard-first UX. (1.2)
3. **Asymmetric slanted panel header** via `clip-path`, 10px right cut, with a `[CODE]` keycap and numbered `1) 2)` menu items. (1.5, 2.2)
4. **Off-palette selection**: crimson fill + amber text + -1deg rotation + 1.04 x-scale on the focused rail/palette item only. (2.1)
5. **Violet alert as Calling Card**: a crimson cut card, ransom-text "VIOLET", hard offset plate, stamp-in at 120ms. (2.3, 2.4)
6. **1px pane grid** in place of card gaps and borders, so the layout reads as one instrument. (3.3)
7. **Ledger-style tables**: 20px rows, tabular slashed zero, a `#7a4a00` rule every 5 rows, no zebra stripes and no radius. (1.3)
8. **Hand-built SVG mascot** with a ponytail, round glasses and ribbon silhouette, 4-color palette plus ink, miter joins, a slanted plate and a cut-paper speech bubble. (5)
9. **Deterministic ransom-note wordmark** `SUMIRE` in the top bar (Anton / Bebas / serif, rotations of -4 to 3deg, alternating boxes). (2.3)
10. **Corner-fading halftone** (6px dot grid, `#7a4a00`, diagonal mask) in empty states and behind the mascot. (2.6)
11. **Teletext page codes** (`MKT 100`) in panel headers and **block-character bars** for breadth and volume. (3.1)
12. **Ribbon on the active tab**: a small crimson notched ribbon tail (`clip-path` with a V notch) hanging 6px below the tab. (plan §7.4, 2.2)
13. **Glasses spinner**: two circles and a bridge, with the lens glint rotating. Static under reduced motion. (plan §7.4)
14. **Single CRT cue**: a faint amber glow on the IHSG hero number only. (3.2)
15. **Sumire's voice in microcopy** for empty and error states, with real tickers and WIB times. (4)

## 7. Avoid

- Purple, violet or blue gradients (even though her codename is Violet: "Violet" is a word, not a color here), plus gradient text and gradient buttons.
- Glassmorphism: `backdrop-filter: blur`, translucent white panels, frosted overlays.
- Rounded cards (`border-radius` >= 6px), pill buttons, soft multi-layer `box-shadow`. Use hard offset plates instead.
- Generic glow (`box-shadow: 0 0 20px` in the accent color) on hover or focus everywhere. Focus is a crisp 2px crimson ring or outline.
- Cool blue-grey neutrals (`#121218`, `#1e293b`-style slate) as the whole surface system.
- Equal three-card rows with an icon, a title and a sentence each, and symmetric equal-cell dashboards with no hero element.
- Emoji or stock icon sets as decoration. Use text labels, keycaps and block characters.
- Ransom-note or rotated type in data, tables, numbers or body text. Readability wins (Tactica lesson).
- Crimson for price direction. Up `#22c55e` and down `#f43f5e` only.
- Looping or ambient animation (pulsing dots, shimmer skeletons on everything). Keep motion under 200ms, directional and off under reduced motion.
- Random-per-render rotation or jitter, which looks like a bug. Make every "hand-made" offset deterministic.
- A symmetrical, centered, big-eyed chibi mascot or a generic anime avatar. Keep her off-center, with a ponytail swinging to one side and sharp cel shading.
- Tracing or embedding copyrighted game art in the repo. The mascot is an original SVG interpretation, and optional official images stay in the gitignored `web/public/assets/sumire/` as the plan says.
