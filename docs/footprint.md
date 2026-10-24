# Footprint check (plan §2.1)

Measured 2026-10-24 (Saturday, market closed) on the dev PC (Windows 11, .NET 9.0.311 SDK). The build is the single-file self-contained Release publish (`Sumire.exe`). The database was unreachable (the direct Supabase host is IPv6-only), so the analytics snapshot came from the Yahoo fallback. No browser tab was connected.

| Target | Measured | Status |
|---|---|---|
| Startup under 3 s | **2.6 s** from process start to the first `200` on `/api/health` (2.58 s and 2.64 s over two runs) | met |
| Idle CPU ~0% | **0.03%** of total CPU (156 ms of CPU time in 30 s, on all cores) after warm-up | met |
| Idle RAM under ~150 MB | **~295 MB working set / ~175 MB private** about 60 s after start, once the startup snapshot is built | **over** |
| Frontend under 400 KB gzipped | **94 KB** initial load (index JS 89 KB + CSS 5 KB, gzip -9), **220 KB** for every lazy chunk combined. The largest lazy chunk is GP (lightweight-charts), 61 KB | met |
| EOD pipeline under 2 min | not measurable without the DB. The Yahoo-fallback snapshot (110 core stocks + IHSG/LQ45, 3 years of bars) builds in **23 s** | n/a |

Publish output: `Sumire.exe` is 57 MB (self-contained, compressed), with `wwwroot/`, `Seed/` and `Research/insight.schema.json` next to it.

## How it was measured

```powershell
dotnet publish src/Sumire.Api -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o publish
# startup: Start-Process publish\Sumire.exe, then poll http://localhost:5080/api/health every 100 ms
# RAM: (Get-Process -Id $pid).WorkingSet64 / PrivateMemorySize64 about 60 s after start
# CPU: TotalProcessorTime delta over 30 s / (30 s x logical cores)
# bundle: gzip -9 of web build output (src/Sumire.Api/wwwroot/assets)
```

## Memory notes

- The memory is mostly the startup warm-up. `AnalyticsStore` builds the day's snapshot (3 years of daily bars, indicators, GARCH and scores for the core universe), and the Yahoo fallback parses a lot of JSON to do it. The GC does not shrink the heap again while the process is idle.
- GC A/B on the same build, 55 s after start: server GC (the ASP.NET default, which uses DATAS dynamic heap sizing in .NET 9) came out at 294 MB working set / 175 MB private. Workstation GC came out at 324 / 199 MB. So the default stays. `DOTNET_GCConserveMemory=7` made no difference.
- Easy wins applied: EF Core query/command, `HttpClient` and Polly logs lowered to Warning/Critical, so a down DB or Yahoo traffic no longer floods the console. The ONNX runtime only loads inside `forecast --once`, never in web mode.
- Follow-up to reach the target: after the snapshot build finishes, `AnalyticsStore` should drop intermediate data (raw Yahoo payloads, per-stock temporaries). It should then do one compacting `GC.Collect(2, GCCollectionMode.Aggressive)`, once a day, so the idle process returns memory to the OS. Measure again with the DB reachable, since DB bars avoid the Yahoo JSON.
