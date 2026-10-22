# Relations map (`Seed/relations.json`)

Hand-curated links between IDX tickers that the `REL` screen, the fact packet builder (§6.3) and Chronos-2 covariates use. It is small on purpose: only links that actually move prices together.

## Shape

```json
{
  "updated": "YYYY-MM-DD",
  "groups":       [{ "name", "tickers": [], "note", "asOf" }],
  "commodities":  [{ "factor", "proxySymbol", "tickers": [], "direction", "note" }],
  "macro":        [{ "factor", "tickers": [], "direction", "note" }],
  "supplyChains": [{ "name", "steps": [{ "stage", "tickers": [] }] }]
}
```

- `groups`: conglomerate / controlling-shareholder groups. A ticker may appear in more than one group when control is shared (e.g. `BUMI` in Salim and Bakrie, `ADRO` in Adaro and Saratoga). `asOf` is the month the ownership was last checked.
- `commodities.factor`: `COAL`, `NICKEL`, `CPO`, `GOLD`, `OIL`, `COPPER`, `TIN`, `GAS`. `proxySymbol` is an instrument `code` from `instruments.csv` (`GOLD`, `BRENT`, `WTI`) or `null` when Yahoo has no usable series. A factor may have several entries when some names are hurt instead of helped (see the two `OIL` entries).
- `macro.factor`: `BI_RATE`, `USDIDR`, `US10Y`, `CHINA`, `DOMESTIC_CONSUMPTION`. Entries use `tickers`; the shape also allows `sectors` (IDX-IC sector names as in `instruments.csv`) instead.
- `direction`: `positive` means the ticker tends to rise when the factor rises; `negative` means it tends to fall.
- `supplyChains.steps` are ordered upstream to downstream.

## Editing

1. Use only real, listed IDX codes (the code without `.JK`). Codes that are not in `instruments.csv` are ignored by the app and flagged by insight validation.
2. Keep notes short (one or two sentences): they are shown in `REL` and sent to the research agent.
3. When ownership changes (spin-offs, mergers, delistings), update the entry, its `asOf`, and the top-level `updated` date.
4. Check the file is valid JSON:
   `node -e "JSON.parse(require('fs').readFileSync('src/Sumire.Api/Seed/relations.json'))"`

Review the groups at least twice a year; IDX ownership moves quickly.

## Notable changes reflected (checked Oct 2026)

- **Adaro**: Adaro Energy was renamed AlamTri Resources (`ADRO`, metallurgical coal, minerals, green energy); the thermal coal business was spun off as Adaro Andalan (`AADI`, listed 5 Dec 2024). Coal exposure now sits mainly in `AADI`.
- **Prajogo Pangestu**: six listed names: `BRPT` (parent), `TPIA`, `BREN`, `CDIA` (TPIA's infrastructure arm, IPO Jul 2025), `CUAN` and its subsidiary `PTRO`.
- **Danantara**: Series B shares of `BMRI`, `BBRI`, `BBNI`, `BBTN`, `TLKM`, `SMGR`, `JSMR` and others moved to Danantara's operating holding (PT BKI) in March 2025; the state keeps control via the Series A Dwiwarna share. Split into "state banks", "SOEs" and "MIND ID (state mining)".
- **BUMI**: jointly controlled by Bakrie and Salim (via Mach Energy) since the 2025 rights issue.
- **XLSmart**: Smartfren merged into XL Axiata in 2025; `EXCL` is kept under Sinar Mas as a shared-control link.
- **Haji Isam / Happy Hapsoro**: added as groups because their names (`JARR`, `PGUN`; `RAJA`, `RATU`, `BUVA`, `MINA`) moved together sharply in 2025.

## Sources

- Databoks (Katadata): Prajogo Pangestu's issuers' stock growth since early 2025 — https://databoks.katadata.co.id/en/market/statistics/6953be0575391/prajogo-pangestus-issuers-stock-growth-since-early-2025-cdia-soars-554
- IDNFinancials: CDIA debut, Prajogo shares rally — https://www.idnfinancials.com/news/55727/cdia-to-make-its-debut-tomorrow-prajogo-pangestus-shares-rally
- IDNFinancials: Danantara operational holding receives 52% of BMRI shares — https://www.idnfinancials.com/news/53384/danantara-operational-holding-receives-52-of-bmri-shares
- IDNFinancials: Gov't transfers 52.09% of TLKM to Danantara's operational holding — https://idnfinancials.com/news/53366/govt-transfers-52-09-of-tlkm-to-danantaras-operational-holding
- Bisnis.com: Danantara secures dividends from BBRI, BMRI, TLKM and other SOEs — https://premium.bisnis.com/read/20250325/664/1864563/danantara-secures-trillions-of-rupiah-from-bbri-bmri-tlkm-and-other-soes
- Wikipedia: AlamTri Resources — https://en.wikipedia.org/wiki/AlamTri_Resources
- Bisnis.com: AADI and ADRO synergy after the spin-off — https://market.bisnis.com/read/20241205/192/1821837/bos-adaro-andalan-aadi-buka-bukaan-sinergi-adro-usai-spin-off
- Kontan: Bakrie to share control of BUMI with Salim — https://insight.kontan.co.id/news/pasca-seperempat-abad-bakrie-kini-akan-berbagi-kendali-di-bumi-dengan-grup-salim
- Bisnis.com: BUMI shares held by the Salim group — https://premium.bisnis.com/read/20250615/664/1885099/a-closer-look-at-bumi-shares-held-by-the-salim-group-blackrock
- IDNFinancials: Haji Isam and Happy Hapsoro shares top weekly gainers — https://www.idnfinancials.com/news/56349/shares-of-haji-isam-and-happy-hapsoro-surge-top-weekly-gainers
- CNBC Indonesia: Haji Isam stocks rally — https://www.cnbcindonesia.com/market/20251209092058-17-692377/saham-haji-isam-kompak-melesat-ada-apa
- Company annual reports and IDX shareholder disclosures for the long-standing groups (Djarum, Salim, Astra, Sinar Mas, Lippo, MNC, Emtek, CT Corp, Saratoga, Indika, Medco, Triputra).
