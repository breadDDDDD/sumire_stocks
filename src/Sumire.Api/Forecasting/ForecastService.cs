using Microsoft.EntityFrameworkCore;
using Sumire.Api.Analytics.Core;
using Sumire.Api.Analytics.Services;
using Sumire.Api.Data;
using Sumire.Api.Market;

namespace Sumire.Api.Forecasting;

public sealed record ForecastHorizonDto(int Horizon, decimal P10, decimal P50, decimal P90, double ProbUp, double Sigma);

public sealed record ModelForecastDto(string Model, DateOnly AsOf, IReadOnlyList<ForecastHorizonDto> Horizons);

/// <summary>±1σ/±2σ price band around the last close for one horizon (no drift).</summary>
public sealed record ConeDto(int Horizon, double Sigma, decimal Minus2, decimal Minus1, decimal Plus1, decimal Plus2);

public sealed record GarchDto(double AnnualizedVol, double LongRunVol, double VolPercentile, double Persistence, bool Converged, IReadOnlyList<ConeDto> Cone);

public sealed record ScoreDto(double Total, string Label, double Coverage, IReadOnlyList<ComponentScore> Components)
{
    public static ScoreDto From(SignalResult r) => new(r.Total, r.Label, Math.Round(r.Coverage, 2), r.Components);
}

public sealed record TrackRecordDto(string Model, int Horizon, string Scope, int N, double DirAccuracy, double Mape, double Coverage80, bool? BeatsBaseline);

public sealed record StockForecastResponse(
    string Code, DateOnly AsOf, decimal Last, IReadOnlyList<ModelForecastDto> Models, GarchDto? Garch, ScoreDto Score,
    IReadOnlyList<TrackRecordDto> TrackRecord);

/// <summary>Baseline forecasts, walk-forward evaluation and the per-stock forecast view.</summary>
public sealed class ForecastService(SumireDbContext db, TimeProvider clock, ILogger<ForecastService> logger)
{
    public const int BacktestWeeks = 52;
    const int InsertChunk = 2000;

    public static IEnumerable<Forecast> Rows(string code, BaselineForecast f) => f.Horizons.Select(h => new Forecast
    {
        Code = code,
        AsOf = f.AsOf,
        Horizon = h.Horizon,
        Model = BaselineForecaster.ModelName,
        P10 = h.P10,
        P50 = h.P50,
        P90 = h.P90,
        ProbUp = Math.Round(h.ProbUp, 4),
        Sigma = Math.Round(h.Sigma, 6),
    });

    static IEnumerable<StockAnalytics> ForecastUniverse(Universe u) =>
        u.Ihsg is { } ihsg ? u.Stocks.Values.Append(ihsg) : u.Stocks.Values;

    /// <summary>Writes today's baseline forecasts for the core universe and IHSG (replacing any for the same day).</summary>
    public async Task<int> WriteBaselineAsync(Universe u, CancellationToken ct)
    {
        var rows = ForecastUniverse(u).Where(s => s.Baseline is not null).SelectMany(s => Rows(s.Code, s.Baseline!)).ToList();
        foreach (var date in rows.Select(r => r.AsOf).Distinct())
            await db.Forecasts.Where(f => f.Model == BaselineForecaster.ModelName && f.AsOf == date).ExecuteDeleteAsync(ct);
        await InsertAsync(rows, ct);
        logger.LogInformation("Baseline forecasts written: {Count} rows", rows.Count);
        return rows.Count;
    }

    /// <summary>
    /// One-time walk-forward backtest: when there are no baseline forecasts older than ~4 months, re-creates weekly
    /// baseline forecasts over the last 12 months from the bars known at each date, so the track record fills at once.
    /// </summary>
    public async Task<int> BacktestIfNeededAsync(Universe u, CancellationToken ct)
    {
        var cutoff = u.ExpectedDate.AddDays(-120);
        if (await db.Forecasts.AnyAsync(f => f.Model == BaselineForecaster.ModelName && f.AsOf <= cutoff, ct)) return 0;

        var stocks = ForecastUniverse(u).ToList();
        var all = new List<Forecast>[stocks.Count];
        Parallel.For(0, stocks.Count, new ParallelOptions { CancellationToken = ct }, i => all[i] = Backtest(stocks[i]));
        var rows = all.SelectMany(x => x).ToList();

        var dates = rows.Select(r => r.AsOf).Distinct().ToList();
        await db.Forecasts.Where(f => f.Model == BaselineForecaster.ModelName && dates.Contains(f.AsOf)).ExecuteDeleteAsync(ct);
        await InsertAsync(rows, ct);
        logger.LogInformation("Baseline backtest written: {Count} rows over {Dates} dates", rows.Count, dates.Count);
        return rows.Count;
    }

    /// <summary>Weekly baseline forecasts over the last <paramref name="weeks"/> weeks using only bars up to each date.</summary>
    public static List<Forecast> Backtest(StockAnalytics s, int weeks = BacktestWeeks)
    {
        var rows = new List<Forecast>();
        var n = s.Bars.Count;
        for (var end = n - 1 - 5; end >= Math.Max(0, n - 1 - weeks * 5); end -= 5)
        {
            var history = s.Bars.Take(end + 1).ToList();
            if (BaselineForecaster.Forecast(history) is { } f) rows.AddRange(Rows(s.Code, f));
        }
        return rows;
    }

    /// <summary>Scores every matured forecast of the last ~13 months and replaces forecast_metrics (except scope "news").</summary>
    public async Task<List<ForecastMetric>> EvaluateAsync(Universe u, CancellationToken ct)
    {
        var since = u.ExpectedDate.AddDays(-400);
        var forecasts = await db.Forecasts.AsNoTracking().Where(f => f.AsOf >= since).ToListAsync(ct);
        var matched = forecasts.GroupBy(f => f.Code).SelectMany(g =>
            u.Find(g.Key) is { } s
                ? ForecastEvaluator.Match(g, s.Bars.Select(b => (b.Date, b.Close)).ToList())
                : []).ToList();

        var metrics = Metrics(matched, code => u.Find(code)?.Instrument.Sector, clock.GetUtcNow());
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.ForecastMetrics.Where(m => m.Scope != "news").ExecuteDeleteAsync(ct);
        db.ForecastMetrics.AddRange(metrics);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        db.ChangeTracker.Clear();
        logger.LogInformation("Forecast metrics: {Count} rows from {Matched} matured forecasts", metrics.Count, matched.Count);
        return metrics;
    }

    /// <summary>Metrics per model and horizon for scopes "all" (stocks), "IHSG" and each sector.</summary>
    public static List<ForecastMetric> Metrics(IEnumerable<(Forecast Forecast, ForecastOutcome Outcome)> matched, Func<string, string?> sectorOf, DateTimeOffset now)
    {
        var list = matched.ToList();
        var result = new List<ForecastMetric>();
        void Add(string scope, IEnumerable<(Forecast, ForecastOutcome)> items)
        {
            foreach (var s in ForecastEvaluator.ScoreByModel(items))
                result.Add(new ForecastMetric
                {
                    Model = s.Model, Horizon = s.Horizon, Scope = scope, N = s.Score.N,
                    DirAccuracy = Math.Round(s.Score.DirAccuracy, 4), Mape = Math.Round(s.Score.Mape, 4),
                    Coverage80 = Math.Round(s.Score.Coverage80, 4), UpdatedAt = now.ToUniversalTime(),
                });
        }
        Add("all", list.Where(m => m.Forecast.Code != "IHSG"));
        Add("IHSG", list.Where(m => m.Forecast.Code == "IHSG"));
        foreach (var g in list.Where(m => m.Forecast.Code != "IHSG").GroupBy(m => sectorOf(m.Forecast.Code)).Where(g => g.Key is not null))
            Add(g.Key!, g);
        return result;
    }

    async Task InsertAsync(List<Forecast> rows, CancellationToken ct)
    {
        foreach (var chunk in rows.Chunk(InsertChunk))
        {
            db.Forecasts.AddRange(chunk);
            await db.SaveChangesAsync(ct);
            db.ChangeTracker.Clear();
        }
    }

    /// <summary>The forecast view for one stock. Stored model forecasts are added when the DB is reachable.</summary>
    public async Task<StockForecastResponse> GetAsync(StockAnalytics s, Universe u, CancellationToken ct)
    {
        var models = new List<ModelForecastDto>();
        if (s.Baseline is { } b)
            models.Add(new ModelForecastDto(BaselineForecaster.ModelName, b.AsOf,
                b.Horizons.Select(h => new ForecastHorizonDto(h.Horizon, h.P10, h.P50, h.P90, Math.Round(h.ProbUp, 4), Math.Round(h.Sigma, 6))).ToList()));

        if (Db.IsConfigured)
        {
            try
            {
                var since = s.AsOf.AddDays(-10);
                var stored = await db.Forecasts.AsNoTracking()
                    .Where(f => f.Code == s.Code && f.Model != BaselineForecaster.ModelName && f.AsOf >= since).ToListAsync(ct);
                foreach (var g in stored.GroupBy(f => f.Model))
                {
                    var asOf = g.Max(f => f.AsOf);
                    models.Add(new ModelForecastDto(g.Key, asOf, g.Where(f => f.AsOf == asOf).OrderBy(f => f.Horizon)
                        .Select(f => new ForecastHorizonDto(f.Horizon, f.P10, f.P50, f.P90, f.ProbUp, f.Sigma)).ToList()));
                }
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                logger.LogDebug("Stored forecasts unavailable: {Error}", Db.ShortError(ex));
            }
        }

        return new StockForecastResponse(s.Code, s.AsOf, s.Last, models, Garch(s), ScoreDto.From(s.Score), TrackRecord(s, u.Metrics));
    }

    public static GarchDto? Garch(StockAnalytics s)
    {
        if (s.Garch is not { } g) return null;
        var last = s.Last;
        var cone = ForecastMath.Horizons.Select(h =>
        {
            var sigma = g.SigmaOver(h);
            decimal P(double k) => Math.Round(last * (decimal)Math.Exp(k * sigma), 2);
            return new ConeDto(h, Math.Round(sigma, 6), P(-2), P(-1), P(1), P(2));
        }).ToList();
        return new GarchDto(Math.Round(g.AnnualizedVol, 4), Math.Round(g.LongRunAnnualizedVol, 4), Math.Round(g.VolPercentile, 1),
            Math.Round(g.Persistence, 4), g.Converged, cone);
    }

    public static List<TrackRecordDto> TrackRecord(StockAnalytics s, IReadOnlyList<ForecastMetric> metrics)
    {
        var scopes = new List<string> { s.Code == "IHSG" ? "IHSG" : "all" };
        if (s.Code != "IHSG" && s.Instrument.Sector is { } sec) scopes.Add(sec);
        return metrics.Where(m => scopes.Contains(m.Scope))
            .OrderBy(m => scopes.IndexOf(m.Scope)).ThenBy(m => m.Model != BaselineForecaster.ModelName).ThenBy(m => m.Model).ThenBy(m => m.Horizon)
            .Select(m =>
            {
                var b = metrics.FirstOrDefault(x => x.Model == BaselineForecaster.ModelName && x.Horizon == m.Horizon && x.Scope == m.Scope);
                bool? beats = m.Model == BaselineForecaster.ModelName ? null : UniverseBuilder.BeatsBaseline(m, b);
                return new TrackRecordDto(m.Model, m.Horizon, m.Scope, m.N, m.DirAccuracy, m.Mape, m.Coverage80, beats);
            })
            .ToList();
    }
}

public sealed class BaselineForecastStep(AnalyticsStore store, ForecastService forecasts) : IEodPipelineStep
{
    public int Order => EodOrder.BaselineForecasts;
    public string Name => "baseline forecasts";

    public async Task RunAsync(DateOnly tradingDate, CancellationToken ct) =>
        await forecasts.WriteBaselineAsync(await store.GetAsync(ct), ct);
}

public sealed class ForecastEvaluationStep(AnalyticsStore store, ForecastService forecasts) : IEodPipelineStep
{
    public int Order => EodOrder.ForecastEvaluation;
    public string Name => "forecast evaluation";

    public async Task RunAsync(DateOnly tradingDate, CancellationToken ct)
    {
        var u = await store.GetAsync(ct);
        await forecasts.BacktestIfNeededAsync(u, ct);
        await forecasts.EvaluateAsync(u, ct);
    }
}
