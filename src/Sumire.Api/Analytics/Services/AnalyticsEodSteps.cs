using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Sumire.Api.Data;
using Sumire.Api.Market;

namespace Sumire.Api.Analytics.Services;

/// <summary>EOD step order used by analytics and forecasting (other areas can slot in between).</summary>
public static class EodOrder
{
    public const int Snapshot = 100;
    public const int BaselineForecasts = 200;
    public const int ForecastEvaluation = 300;
    public const int SignalScores = 400;
    public const int StorageGuard = 1000;
}

/// <summary>Rebuilds the analytics snapshot (indicators, GARCH, baseline, breadth, sectors) from the new bars.</summary>
public sealed class AnalyticsSnapshotStep(AnalyticsStore store) : IEodPipelineStep
{
    public int Order => EodOrder.Snapshot;
    public string Name => "analytics snapshot";

    public Task RunAsync(DateOnly tradingDate, CancellationToken ct) => store.RebuildAsync(ct);
}

/// <summary>Rescores with the latest track record and writes <c>signal_scores</c> for the core universe.</summary>
public sealed class SignalScoreStep(AnalyticsStore store, SumireDbContext db, ILogger<SignalScoreStep> logger) : IEodPipelineStep
{
    public int Order => EodOrder.SignalScores;
    public string Name => "signal scores";

    public async Task RunAsync(DateOnly tradingDate, CancellationToken ct)
    {
        var u = await store.RescoreAsync(ct);
        var rows = u.Stocks.Values.Select(ToRow).ToList();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.SignalScores.ExecuteDeleteAsync(ct);
        db.SignalScores.AddRange(rows);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        db.ChangeTracker.Clear();
        logger.LogInformation("Signal scores written for {Count} stocks", rows.Count);
    }

    public static SignalScore ToRow(StockAnalytics s)
    {
        double? C(Core.SignalComponent c) => s.Score.Components.FirstOrDefault(x => x.Component == c)?.Score;
        var reasons = s.Score.Components.ToDictionary(c => JsonNamingPolicy.CamelCase.ConvertName(c.Component.ToString()), c => c.Reason);
        return new SignalScore
        {
            Code = s.Code,
            AsOf = s.AsOf,
            Total = s.Score.Total,
            Trend = C(Core.SignalComponent.Trend),
            Momentum = C(Core.SignalComponent.Momentum),
            Volume = C(Core.SignalComponent.Volume),
            Volatility = C(Core.SignalComponent.Volatility),
            Valuation = C(Core.SignalComponent.Valuation),
            Forecast = C(Core.SignalComponent.Forecast),
            News = C(Core.SignalComponent.News),
            Reasons = JsonSerializer.Serialize(reasons),
        };
    }
}
