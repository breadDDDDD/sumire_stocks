using System.Diagnostics;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Sumire.Api.Analytics.Core;
using Sumire.Api.Analytics.Services;
using Sumire.Api.Data;
using Sumire.Api.Forecasting.Chronos;
using Sumire.Api.Market;

namespace Sumire.Api.Forecasting;

/// <summary>
/// `Sumire forecast --once [--codes BBCA,BBRI] [--skip-holidays]`: runs Chronos-2 on the core universe + IHSG (with IHSG and USD/IDR
/// as joint covariates), upserts <c>forecasts</c> rows with model "chronos-2" and exits (0 ok, 1 failed).
/// FORECAST_MODE=off skips the run.
/// </summary>
public static class ForecastCommand
{
    static readonly string[] CovariateCodes = ["IHSG", "USDIDR"];
    const int BatchSize = 8;
    const int HistoryDays = 3 * 366;

    public static async Task<int> RunOnceAsync(IServiceProvider services, string[] args, CancellationToken ct)
    {
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("Sumire.Forecast");
        var mode = (Environment.GetEnvironmentVariable("FORECAST_MODE") is { Length: > 0 } m ? m : "actions").Trim().ToLowerInvariant();
        if (mode == "off")
        {
            logger.LogInformation("FORECAST_MODE=off: nothing to do");
            return 0;
        }
        if (args.Contains("--skip-holidays") && services.GetRequiredService<MarketCalendar>() is var cal && !cal.IsTradingDay(cal.TodayWib))
        {
            logger.LogInformation("{Date} is not an IDX trading day: nothing to do", cal.TodayWib);
            return 0;
        }
        if (!Db.IsConfigured)
        {
            logger.LogError("Cannot forecast: {Reason}", Db.NotConfiguredReason);
            return 1;
        }

        var total = Stopwatch.StartNew();
        try
        {
            var catalog = services.GetRequiredService<InstrumentCatalog>();
            var db = services.GetRequiredService<SumireDbContext>();
            var codes = ParseCodes(args) ?? catalog.Core.Where(i => !i.IsIndex).Select(i => i.Code).Append("IHSG").ToList();

            var sw = Stopwatch.StartNew();
            var since = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-HistoryDays);
            var wanted = codes.Concat(CovariateCodes).Distinct().ToList();
            var rows = await db.DailyBars.AsNoTracking()
                .Where(b => wanted.Contains(b.Code) && b.Date >= since)
                .OrderBy(b => b.Code).ThenBy(b => b.Date)
                .ToListAsync(ct);
            var bars = rows.GroupBy(r => r.Code).ToDictionary(g => g.Key, g => g.Select(AnalyticsStore.ToBar).ToList());
            var targets = Targets(codes, bars);
            logger.LogInformation("Loaded bars for {Count} series in {Ms} ms ({Mode} mode)", bars.Count, sw.ElapsedMilliseconds, mode);
            if (targets.Count == 0)
            {
                logger.LogError("No bars in the database for the requested codes");
                return 1;
            }

            sw.Restart();
            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
            var path = await new ForecastModelStore(ForecastModelOptions.FromEnvironment(), http, logger).EnsureAsync(ct);
            logger.LogInformation("Model ready in {Ms} ms", sw.ElapsedMilliseconds);

            sw.Restart();
            var forecasts = Predict(path, targets);
            logger.LogInformation("Forecast {Targets} series in {Ms} ms", targets.Count, sw.ElapsedMilliseconds);

            await SaveAsync(db, forecasts, ct);
            logger.LogInformation("Wrote {Rows} {Model} rows; total {Seconds:0.0}s", forecasts.Count, ChronosForecaster.ModelName, total.Elapsed.TotalSeconds);
            return 0;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogError("Forecast run failed: {Error}", ex is ForecastModelException ? ex.Message : Db.ShortError(ex));
            return 1;
        }
    }

    /// <summary>Kept out of line so ONNX Runtime is only loaded when a forecast actually runs.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    static List<Forecast> Predict(string modelPath, IReadOnlyList<ChronosTarget> targets)
    {
        using var model = new OnnxChronosModel(modelPath);
        return ChronosForecaster.Run(model, targets, BatchSize);
    }

    /// <summary>Each stock is forecast jointly with IHSG and USD/IDR; IHSG with USD/IDR.</summary>
    public static List<ChronosTarget> Targets(IEnumerable<string> codes, IReadOnlyDictionary<string, List<Bar>> bars) =>
        codes.Where(c => bars.GetValueOrDefault(c) is { Count: >= 2 })
            .Select(c => new ChronosTarget(c, bars[c],
                CovariateCodes.Where(cv => cv != c && bars.ContainsKey(cv)).Select(cv => (IReadOnlyList<Bar>)bars[cv]).ToList()))
            .ToList();

    static List<string>? ParseCodes(string[] args)
    {
        var i = Array.IndexOf(args, "--codes");
        return i >= 0 && i + 1 < args.Length
            ? args[i + 1].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(c => c.ToUpperInvariant()).ToList()
            : null;
    }

    static async Task SaveAsync(SumireDbContext db, List<Forecast> forecasts, CancellationToken ct)
    {
        var codes = forecasts.Select(f => f.Code).Distinct().ToList();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        foreach (var asOf in forecasts.Select(f => f.AsOf).Distinct())
            await db.Forecasts.Where(f => f.Model == ChronosForecaster.ModelName && f.AsOf == asOf && codes.Contains(f.Code)).ExecuteDeleteAsync(ct);
        db.Forecasts.AddRange(forecasts);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }
}
