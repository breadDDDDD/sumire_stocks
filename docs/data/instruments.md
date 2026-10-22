# Instruments seed (`src/Sumire.Api/Seed/instruments.csv`)

The core universe the app tracks every night: every stock in **LQ45, IDX30, IDX80 and KOMPAS100**, a few large caps outside those indexes, and the index and macro symbols used by the status bar and the relations engine. Any other IDX stock still works on demand; it just isn't in this file.

**As of:** index review effective **3 Aug 2026** (LQ45, IDX30, IDX80 valid until 30 Oct 2026; KOMPAS100 until 29 Jan 2027). File compiled 22 Oct 2026.

**Contents:** 110 stocks + 12 index/macro rows. Every row has `core=true`.

## Columns

| Column | Notes |
|---|---|
| `code` | IDX ticker (4 uppercase letters) or an internal code for index/macro rows (`IHSG`, `USDIDR`, ...) |
| `yahoo_symbol` | `<CODE>.JK` for stocks; Yahoo symbol for the others |
| `name` | Official company name (current name after renames, e.g. ADRO = PT Alamtri Resources Indonesia Tbk, EXCL = PT XLSMART Telecom Sejahtera Tbk) |
| `sector`, `industry` | IDX-IC sector; industry is IDX-IC industry level, simplified. `Index` / `Macro` for non-stocks |
| `board` | `Utama` where known; **empty = not verified** (not "no board") |
| `is_index` | `true` for index and macro rows |
| `index_memberships` | Pipe-separated, in the order `LQ45\|IDX30\|IDX80\|KOMPAS100`; empty for the out-of-index extras |
| `group_name` | Conglomerate. State-owned companies (including MIND ID / Pertamina subsidiaries and INCO, where MIND ID is the largest holder) use `Danantara/BUMN` |
| `core` | Always `true` here |

## Sources

- LQ45 (45): Kontan/Momsmoney, "Susunan LQ45 periode 3 Agustus - 30 Oktober 2026". Changes: +INDY, +NCKL, -SMGR, -TOWR.
- IDX30 (30): Stockwatch.id, "Daftar terbaru penghuni indeks IDX30 periode Agustus-Oktober 2026". Change: +DEWA, -PTBA.
- IDX80 (80): Stockwatch.id, IDX80 table for Aug-Oct 2026 (BEI announcement Peng-00148/BEI.POP/07-2026). Changes: +BFIN, +LSIP, +NCKL, -INTP, -PANI, -SIDO.
- KOMPAS100: Kontan, "Sembilan saham tersisih, begini daftar saham hasil rebalancing indeks KOMPAS100". Changes: +BFIN, +BIPI, +BNBR, +COIN, +EMAS, +GGRM, +LSIP, +MINA, +RMKE; -BREN, -BTPS, -DSSA, -FILM, -HMSP, -INTP, -MTEL, -SIDO, -TCPI.
- Names, sectors, groups: company knowledge, cross-checked loosely; see uncertain rows below.

The membership lists are checked when the file is built: IDX30 is a subset of LQ45, LQ45 of IDX80, IDX80 of KOMPAS100.

## Uncertain rows

- **BRPT in KOMPAS100:** the Kontan article lists 99 codes. BRPT is in LQ45, IDX30 and IDX80 but missing from that list, so it is assumed to be the 100th.
- **Extras outside all four indexes:** BREN, BTPS, DSSA, FILM, HMSP, INTP, MTEL, SIDO, TCPI (dropped in the July 2026 review) and BYAN (large cap, low free float). They are kept as core because of their size; drop them if you only want index members.
- **Names to re-check:** COIN (PT Indokripto Koin Semesta Tbk), MINA (PT Sanurhasta Mitra Tbk), KPIG (PT MNC Tourism Indonesia Tbk, formerly MNC Land), INET, WIRG, SMIL, BUVA.
- **Sector/industry to re-check:** EMTK (Technology vs Media), ADRO (still Energy/Coal after the AADI spin-off), BIPI, BULL, SSIA, INET, WIFI, KPIG, TOBA, COIN, SMIL.
- **Group to re-check:** EXCL (Sinar Mas after the Smartfren merger), CUAN/PTRO (Prajogo), EMAS/MBMA/MDKA (`Merdeka`), DEWA/ENRG (Bakrie).
- **Board:** empty for newer or smaller listings whose board wasn't verified.
- **Yahoo symbol** `^JKLQ45` for the LQ45 index: check that Yahoo still serves it. Coal and nickel proxies are not included. Add them, with relations.json, once a working Yahoo symbol is confirmed.

## Refreshing

Do this after each IDX index review, which happens in late Jan, Apr, Jul and Oct and takes effect on the first trading day of the next month:

1. Get the new constituent lists from the BEI announcement "Evaluasi Indeks ... (Peng-xxxxx/BEI.POP/MM-YYYY)" on idx.co.id (News > Announcements), or from the Kontan/Stockwatch write-ups that reproduce the tables.
2. Names, sectors and boards: idx.co.id > Data Pasar > Data Saham > **Daftar Saham** (export to Excel). It has Kode, Nama Perusahaan, Tanggal Pencatatan, Saham and Papan Pencatatan (board). The sector comes from the IDX-IC classification on the company profile pages.
3. Update the rows: change `index_memberships`, add new codes and keep removed codes only if you still want them tracked. Keep the header and the column order unchanged. Quote any field that contains a comma.
4. Check the file: each row has 10 columns, codes are unique, and stock codes match `^[A-Z]{4}$`.
5. Update the as-of date and the sources in this document.
