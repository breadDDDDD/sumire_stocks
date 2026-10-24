using Sumire.Api.Analytics.Core;
using Sumire.Api.Data;

namespace Sumire.Api.Forecasting;

/// <summary>
/// The Chronos-2 ONNX contract: <c>context</c> float32 [rows, 2048] (NaN = missing, left-padded) and
/// <c>group_ids</c> int64 [rows] (rows sharing an id are forecast jointly) in, <c>quantiles</c> float32
/// [rows, 64, 21] out. Implemented by the ONNX Runtime session, faked in tests.
/// </summary>
public interface IChronosModel
{
    /// <summary>Returns rows × 64 × 21 quantile values, row-major.</summary>
    float[] Predict(float[] context, long[] groupIds, int rows);
}

/// <summary>One series to forecast plus the covariates forecast jointly with it (e.g. IHSG, USD/IDR).</summary>
public sealed record ChronosTarget(string Code, IReadOnlyList<Bar> Bars, IReadOnlyList<IReadOnlyList<Bar>> Covariates);

/// <summary>Builds model inputs from bars and maps the quantile output to P10/P50/P90/P(up) (plan §5.4).</summary>
public static class ChronosForecaster
{
    public const string ModelName = "chronos-2";
    public const int ContextLength = 2048;
    public const int PredictionLength = 64;
    public static readonly double[] Levels = [0.01, 0.05, 0.1, 0.15, 0.2, 0.25, 0.3, 0.35, 0.4, 0.45, 0.5, 0.55, 0.6, 0.65, 0.7, 0.75, 0.8, 0.85, 0.9, 0.95, 0.99];
    const int I10 = 2, I50 = 10, I90 = 18;
    static readonly int Q = Levels.Length;

    /// <summary>
    /// Rows for one target: the target's AdjClose, then each covariate aligned on the target's trading days
    /// (NaN where the covariate has no bar). Truncated to the last <paramref name="contextLength"/> days and
    /// left-padded with NaN.
    /// </summary>
    public static float[][] BuildRows(IReadOnlyList<Bar> target, IReadOnlyList<IReadOnlyList<Bar>> covariates, int contextLength = ContextLength)
    {
        var start = Math.Max(0, target.Count - contextLength);
        var n = target.Count - start;
        var pad = contextLength - n;

        var rows = new float[1 + covariates.Count][];
        rows[0] = Padded(contextLength);
        for (var i = 0; i < n; i++) rows[0][pad + i] = (float)target[start + i].AdjClose;

        for (var c = 0; c < covariates.Count; c++)
        {
            var byDate = new Dictionary<DateOnly, decimal>(covariates[c].Count);
            foreach (var b in covariates[c]) byDate[b.Date] = b.AdjClose;
            var row = rows[c + 1] = Padded(contextLength);
            for (var i = 0; i < n; i++)
                if (byDate.TryGetValue(target[start + i].Date, out var v) && v > 0) row[pad + i] = (float)v;
        }
        return rows;
    }

    static float[] Padded(int length)
    {
        var a = new float[length];
        Array.Fill(a, float.NaN);
        return a;
    }

    /// <summary>
    /// Maps one row's [64 × 21] quantiles to forecasts. Values are in AdjClose units and are rescaled to the
    /// traded close via <paramref name="adjToClose"/>.
    /// </summary>
    public static List<HorizonForecast> Map(ReadOnlySpan<float> quantiles, decimal lastClose, IReadOnlyList<int> horizons, decimal adjToClose = 1m)
    {
        var result = new List<HorizonForecast>(horizons.Count);
        var last = (double)lastClose;
        foreach (var h in horizons)
        {
            if (h < 1 || h > PredictionLength) throw new ArgumentOutOfRangeException(nameof(horizons), $"Horizon {h} outside 1..{PredictionLength}");
            var step = quantiles.Slice((h - 1) * Q, Q).ToArray().Select(v => (double)v * (double)adjToClose).ToArray();
            Array.Sort(step); // guard against tiny quantile crossings
            var p10 = step[I10];
            var p50 = step[I50];
            var p90 = step[I90];
            var sigma = p10 > 0 && p90 > 0 ? Math.Log(p90 / p10) / (ForecastMath.Z90 - ForecastMath.Z10) : 0;
            var mean = p50 > 0 && last > 0 ? Math.Log(p50 / last) : 0;
            result.Add(new HorizonForecast(h, R(p10), R(p50), R(p90), Math.Round(1 - Cdf(step, last), 4), mean, sigma));
        }
        return result;

        static decimal R(double v) => Math.Round((decimal)v, 2);
    }

    /// <summary>P(X ≤ x) by linear interpolation between quantile levels; clamped to the outer levels.</summary>
    public static double Cdf(double[] values, double x)
    {
        if (x <= values[0]) return Levels[0];
        if (x >= values[^1]) return Levels[^1];
        for (var i = 1; i < values.Length; i++)
        {
            if (x > values[i]) continue;
            var span = values[i] - values[i - 1];
            var t = span > 0 ? (x - values[i - 1]) / span : 1;
            return Levels[i - 1] + t * (Levels[i] - Levels[i - 1]);
        }
        return Levels[^1];
    }

    /// <summary>Runs targets in batches (each target + its covariates share a group id) and returns forecast rows.</summary>
    public static List<Forecast> Run(IChronosModel model, IReadOnlyList<ChronosTarget> targets, int batchSize = 8,
        IReadOnlyList<int>? horizons = null)
    {
        var hs = horizons ?? ForecastMath.Horizons;
        var forecasts = new List<Forecast>();
        foreach (var batch in targets.Where(t => t.Bars.Count >= 2).Chunk(batchSize))
        {
            var rows = new List<float[]>();
            var groups = new List<long>();
            var targetRow = new int[batch.Length];
            for (var g = 0; g < batch.Length; g++)
            {
                targetRow[g] = rows.Count;
                foreach (var r in BuildRows(batch[g].Bars, batch[g].Covariates))
                {
                    rows.Add(r);
                    groups.Add(g);
                }
            }

            var context = new float[rows.Count * ContextLength];
            for (var r = 0; r < rows.Count; r++) rows[r].CopyTo(context, r * ContextLength);
            var output = model.Predict(context, groups.ToArray(), rows.Count);

            for (var g = 0; g < batch.Length; g++)
            {
                var t = batch[g];
                var lastBar = t.Bars[^1];
                var adjToClose = lastBar.AdjClose > 0 ? lastBar.Close / lastBar.AdjClose : 1m;
                var q = output.AsSpan(targetRow[g] * PredictionLength * Q, PredictionLength * Q);
                foreach (var h in Map(q, lastBar.Close, hs, adjToClose))
                    forecasts.Add(new Forecast
                    {
                        Code = t.Code, AsOf = lastBar.Date, Horizon = h.Horizon, Model = ModelName,
                        P10 = h.P10, P50 = h.P50, P90 = h.P90, ProbUp = h.ProbUp, Sigma = Math.Round(h.Sigma, 6),
                    });
            }
        }
        return forecasts;
    }
}
