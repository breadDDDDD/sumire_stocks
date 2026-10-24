# Sumire mascot

The bottom-right bust-up (`web/src/components/mascot/`) reacts to the market:

| Mood | When |
|---|---|
| `surprised` | an alert fires (hub `alert` event), for 12 s |
| `smiling` | a new insight or brief arrives (`insightReady`), or IHSG is up 1% or more |
| `determined` | a research job is running |
| `worried` | IHSG is down 1% or more |
| `neutral` | otherwise |

Outfit: `auto` (gym clothes during the IDX session and lunch break, school uniform otherwise), or fixed `uniform` / `gym`. Hover the portrait and click the small label to cycle; the choice is kept in localStorage. `m` hides or shows her. The speech bubble shows the first sentence of the latest brief, or the latest alert or insight message; click it to dismiss. Animations stop under `prefers-reduced-motion`.

## Portrait files

No game art ships with the repo. Without images the app draws an original SVG placeholder (crimson hair, round glasses, blazer or track jacket). To use real portraits, put PNGs in `web/public/assets/sumire/` (gitignored, so they never get committed) and rebuild the web app (`npm run build` inside `web\`). Files are served from `/assets/sumire/`.

Expected names (`<outfit>-<mood>.png`, 10 files; any missing one falls back to the placeholder):

```
uniform-neutral.png   uniform-smiling.png   uniform-worried.png   uniform-determined.png   uniform-surprised.png
gym-neutral.png       gym-smiling.png       gym-worried.png       gym-determined.png       gym-surprised.png
```

Square images work best (shown at 120×120, `object-fit: contain`, anchored to the bottom), with a transparent background. Crop the bust-ups to head and shoulders.

## Where to find images

Persona 5 Royal's bust-up portraits show Sumire in the Shujin Academy uniform and in her gym outfit (track jacket). Places to look:

- Megami Tensei Wiki (Fandom), Sumire Yoshizawa page, Gallery tab: https://megamitensei.fandom.com/wiki/Sumire_Yoshizawa
- Megaten Wiki pages for her (Kasumi / Violet), with portrait galleries: https://megatenwiki.com/wiki/Kasumi and https://megatenwiki.com/wiki/Violet
- The Spriters Resource, Persona 5 Royal section (look under "Portraits" / "Bustups"): https://www.spriters-resource.com/ (search "Persona 5 Royal")
- Official Atlus art: the Persona 5 Royal official site and art book, and licensed prints such as https://www.cookandbecker.com/en/artwork/3192/show-time-persona-5-atlus.html

Map the game's expressions to the five moods: calm → neutral, smile → smiling, troubled → worried, serious/fired-up → determined, shocked → surprised.

## Copyright

The character and her artwork belong to Atlus / SEGA. Use them only for your own personal, local copy of the app. Don't commit them, publish them or redistribute a build that contains them. That is why `web/public/assets/sumire/` is in `.gitignore`. Note that `npm run build` copies the folder into `src/Sumire.Api/wwwroot/` (also gitignored), so a published build would include them: remove them before sharing a build.
