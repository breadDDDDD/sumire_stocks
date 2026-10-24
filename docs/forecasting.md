# Forecasting: Chronos-2 via ONNX

How the foundation-model forecasts are made, where the model comes from and how to run them (implementation-plan.md §5.4).
The baseline (random walk + GARCH) is always computed in-process by the EOD pipeline; Chronos-2 runs as a separate
batch job, normally in GitHub Actions, and its rows land in `sumire.forecasts` with `model = 'chronos-2'`.

## Model

| | |
|---|---|
| Model | Amazon **Chronos-2** (~120M parameters), unofficial fp32 ONNX export `sktime/chronos2-onnx` |
| File | `chronos2-ctx2048-h64.onnx` (481,610,807 bytes, opset 20) |
| Pinned revision | `84282421a54142dc689c0a71bc9876285b60bdae` |
| SHA-256 | `f06aba77b0445061b78639008074d71f45af8c0edf16c9dbe4c9ea513c422c0e` |
| Licence | Apache-2.0 |

Why this export: it is a single-pass graph with a documented contract and a published checksum, and its author reports
it matches the official `Chronos2Pipeline` within 0.0001 % of the forecast spread. `kashif/chronos-2-onnx` is a
transformers.js encoder/decoder split that would need its own pre/post-processing, so it is not used. `chronos-bolt-small`
remains a possible light fallback but needs no extra code path today.

Contract (from the repo's `manifest.json`):

- input `context` float32 `[batch, 2048]`: raw values, `NaN` = missing, short series left-padded with `NaN` (the model
  scales each row itself);
- input `group_ids` int64 `[batch]`: rows sharing an id are forecast jointly (multivariate), distinct ids are independent;
- output `quantiles` float32 `[batch, 64, 21]` at levels 0.01, 0.05, 0.1 … 0.9, 0.95, 0.99 (median = index 10).

How Sumire uses it (`Forecasting/ChronosForecaster.cs`):

- Each core stock is one group of three rows: the stock's adjusted close, **IHSG** and **USD/IDR**, aligned on the
  stock's trading days (`NaN` where a covariate has no bar). IHSG itself is grouped with USD/IDR. Up to ~3 years of
  history from `daily_bars` (the model accepts 2,048 days).
- From the stock row, step `h−1` for h = 5, 20, 60: P10 = level 0.1, P50 = 0.5, P90 = 0.9 (rescaled from adjusted to
  traded prices), P(up) = 1 − F(last close) interpolated across the 21 levels, σ = ln(P90/P10) / 2.563.
- Batches of 8 groups; ~110 stocks + IHSG is ~14 forward passes.
- The signal score only uses Chronos-2 once its 20-day track record (≥ 30 matured forecasts) beats the baseline on
  direction hit rate without a worse MAPE (`docs/api/analytics.md`).

## Where the weights live

The Hugging Face Hub is the bucket. `forecast --once` downloads the pinned file once, verifies the SHA-256 while
streaming, and caches it:

- locally: `%LOCALAPPDATA%\Sumire\models\sktime--chronos2-onnx\<revision>\chronos2-ctx2048-h64.onnx`
- in CI: `$FORECAST_MODEL_CACHE` (restored with `actions/cache`, keyed by repo + revision)

A cached or downloaded file with a different checksum is deleted and refused, and a revision that is not a full
40-character commit hash (e.g. `main`) is refused. Configuration (`.env` / Action env):

| Variable | Default |
|---|---|
| `FORECAST_MODE` | `actions` (`off` skips the run; `local` is the same run started on this PC) |
| `FORECAST_MODEL_REPO` | `sktime/chronos2-onnx` |
| `FORECAST_MODEL_REVISION` | `84282421a54142dc689c0a71bc9876285b60bdae` |
| `FORECAST_MODEL_FILE` | `chronos2-ctx2048-h64.onnx` |
| `FORECAST_MODEL_SHA256` | `f06aba77…c422c0e` |
| `FORECAST_MODEL_CACHE` | `%LOCALAPPDATA%\Sumire\models` |
| `HF_TOKEN` | unset (only for a private mirror) |
| `FORECAST_LOCAL_FALLBACK` | unset (`true` enables the 18:00 WIB local fallback) |

### Mirroring into your own private HF repo (optional, recommended)

So an upstream change or deletion cannot break the app:

1. Create a private model repo on huggingface.co (e.g. `you/chronos2-onnx-mirror`).
2. Upload the same file (`huggingface-cli upload you/chronos2-onnx-mirror chronos2-ctx2048-h64.onnx`, or the web UI).
3. Copy the commit hash of that upload from the repo's "Files and versions" history.
4. Set `FORECAST_MODEL_REPO=you/chronos2-onnx-mirror`, `FORECAST_MODEL_REVISION=<that hash>`, keep the same
   `FORECAST_MODEL_SHA256`, and create a **read-only** access token as `HF_TOKEN`.

## Running it

```powershell
dotnet run --project src/Sumire.Api -- forecast --once                     # core universe + IHSG
dotnet run --project src/Sumire.Api -- forecast --once --codes BBCA,BBRI    # a few stocks
dotnet run --project src/Sumire.Api -- forecast --once --skip-holidays      # no-op on non-trading days
```

Exit code 0 on success (or nothing to do), 1 on failure (no DB, no bars, download/checksum error). Rows for the same
`as_of`/model/code are replaced. Peak memory is roughly 1–1.5 GB for 1–3 minutes, then the process exits.
ONNX Runtime is referenced only by this verb; the web server never loads it.

### Local fallback

With `FORECAST_LOCAL_FALLBACK=true`, the web app checks at 18:00 WIB on trading days (or at startup if later) whether
today's `chronos-2` rows exist. If not (the Action failed or GitHub was down) it starts `Sumire forecast --once` as a
child process, then rescores the signal scores once it succeeds.

## GitHub Actions

`.github/workflows/forecast.yml` runs at 10:00 UTC (17:00 WIB) Monday–Friday, skipping IDX holidays, plus a manual
"Run workflow" button. It sets up .NET 9, restores the cached model, builds and runs `forecast --once` with a 20-minute
timeout. A private repo gets 2,000 free minutes a month; a run takes ~5–10 minutes.

### Adding the secrets

GitHub → your repo → **Settings → Secrets and variables → Actions → New repository secret**:

- `SUPABASE_CONNECTION_STRING`: the **pooler** connection string (port 6543), ideally for the restricted role below.
- `HF_TOKEN`: only if you mirror into a private HF repo.

Secrets are never in the repo or the logs.

### A restricted Postgres role for the Action

Run once in the Supabase SQL editor (pick your own password), then build the Action's connection string with this
user. It can read bars and instruments and write forecasts, nothing else.

```sql
create role sumire_forecaster with login password 'choose-a-long-random-password';
grant usage on schema sumire to sumire_forecaster;
grant select on sumire.daily_bars, sumire.instruments to sumire_forecaster;
grant select, insert, update, delete on sumire.forecasts to sumire_forecaster;
```

`delete` is needed because a re-run replaces the day's rows; `select` on `forecasts` because the delete filters on it.
With the Supabase pooler the user name becomes `sumire_forecaster.<project-ref>`.
