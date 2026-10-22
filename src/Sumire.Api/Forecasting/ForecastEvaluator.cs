using Sumire.Api.Data;

namespace Sumire.Api.Forecasting;

/// <summary>One forecast whose horizon has passed, with the close it was made from and the realized close.</summary>
public record ForecastOutcome(decimal BaseClose, decimal P10, decimal P50, decimal P90, double ProbUp, decimal Actual);

/// <param name="DirAccuracy">Share of forecasts that called the direction right, 0..1.</param>
/// <param name="Mape">Mean absolute % error of P50, as a fraction.</param>
/// <param name="Coverage80">Share of realized closes inside P10..P90 (ideal ≈ 0.8).</param>
public record ForecastScore(int N, double DirAccuracy, double Mape, double Coverage80);

public record ModelScore(string Model, int Horizon, ForecastScore Score);

/// <summary>Walk-forward scoring of past forecasts (plan §5.5).</summary>
public static class ForecastEvaluator
{
    public static ForecastScore? Evaluate(IEnumerable<ForecastOutcome> outcomes)
    {
        int n = 0, hits = 0, covered = 0;
        var ape = 0.0;
        foreach (var o in outcomes)
        {
            if (o.Actual <= 0 || o.BaseClose <= 0) continue;
            n++;
            var predictedUp = o.ProbUp != 0.5 ? o.ProbUp > 0.5 : o.P50 > o.BaseClose;
            if (predictedUp == o.Actual > o.BaseClose) hits++;
            if (o.Actual >= o.P10 && o.Actual <= o.P90) covered++;
            ape += (double)(Math.Abs(o.P50 - o.Actual) / o.Actual);
        }
        return n == 0 ? null : new ForecastScore(n, (double)hits / n, ape / n, (double)covered / n);
    }

    /// <summary>
    /// Matches stored forecasts of one instrument to its closes (oldest first): the base close is the bar on
    /// AsOf and the outcome is the bar <c>Horizon</c> trading days later. Forecasts not yet due are skipped.
    /// </summary>
    public static IEnumerable<(Forecast Forecast, ForecastOutcome Outcome)> Match(
        IEnumerable<Forecast> forecasts, IReadOnlyList<(DateOnly Date, decimal Close)> closes)
    {
        var index = new Dictionary<DateOnly, int>(closes.Count);
        for (var i = 0; i < closes.Count; i++) index[closes[i].Date] = i;

        foreach (var f in forecasts)
        {
            if (!index.TryGetValue(f.AsOf, out var i) || i + f.Horizon >= closes.Count) continue;
            yield return (f, new ForecastOutcome(closes[i].Close, f.P10, f.P50, f.P90, f.ProbUp, closes[i + f.Horizon].Close));
        }
    }

    /// <summary>Scores per (model, horizon).</summary>
    public static IReadOnlyList<ModelScore> ScoreByModel(IEnumerable<(Forecast Forecast, ForecastOutcome Outcome)> matched) =>
        matched
            .GroupBy(m => (m.Forecast.Model, m.Forecast.Horizon))
            .Select(g => (g.Key, Score: Evaluate(g.Select(m => m.Outcome))))
            .Where(x => x.Score is not null)
            .Select(x => new ModelScore(x.Key.Model, x.Key.Horizon, x.Score!))
            .OrderBy(s => s.Model).ThenBy(s => s.Horizon)
            .ToList();
}
