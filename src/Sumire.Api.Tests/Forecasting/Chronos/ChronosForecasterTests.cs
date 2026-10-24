using Sumire.Api.Analytics.Core;
using Sumire.Api.Forecasting;
using Sumire.Api.Tests.Analytics;

namespace Sumire.Api.Tests.Forecasting.Chronos;

public class ChronosForecasterTests
{
    const int Q = 21;

    [Fact]
    public void Short_series_are_left_padded_with_nan()
    {
        var bars = SyntheticBars.Linear(10, start: 100, step: 1);
        var rows = ChronosForecaster.BuildRows(bars, [], contextLength: 16);
        var row = Assert.Single(rows);
        Assert.Equal(16, row.Length);
        Assert.All(row.Take(6), v => Assert.True(float.IsNaN(v)));
        Assert.Equal(100f, row[6]);
        Assert.Equal(109f, row[15]);
    }

    [Fact]
    public void Long_series_keep_the_most_recent_context()
    {
        var bars = SyntheticBars.Linear(3000, start: 1, step: 1);
        var row = ChronosForecaster.BuildRows(bars, [])[0];
        Assert.Equal(ChronosForecaster.ContextLength, row.Length);
        Assert.Equal(3000f - 2047, row[0]);
        Assert.Equal(3000f, row[^1]);
    }

    [Fact]
    public void Covariates_align_on_target_dates_and_mask_gaps()
    {
        var target = SyntheticBars.Linear(5, start: 10, step: 1);
        // Covariate misses the 3rd date and has an extra date the target does not trade on.
        var cov = target.Where((_, i) => i != 2).Select(b => b with { AdjClose = b.AdjClose * 100 }).ToList();
        cov.Insert(0, cov[0] with { Date = cov[0].Date.AddDays(-3) });

        var rows = ChronosForecaster.BuildRows(target, [cov], contextLength: 5);
        Assert.Equal(2, rows.Length);
        Assert.Equal([10f, 11f, 12f, 13f, 14f], rows[0]);
        Assert.Equal(1000f, rows[1][0]);
        Assert.True(float.IsNaN(rows[1][2]));
        Assert.Equal(1400f, rows[1][4]);
    }

    /// <summary>Quantiles of a lognormal-ish forecast centred on <paramref name="median"/>, widening with the step.</summary>
    static float[] FakeQuantiles(double median, double spreadPerStep = 0.01)
    {
        var q = new float[ChronosForecaster.PredictionLength * Q];
        for (var s = 0; s < ChronosForecaster.PredictionLength; s++)
        {
            var sigma = spreadPerStep * Math.Sqrt(s + 1);
            for (var l = 0; l < Q; l++)
                q[s * Q + l] = (float)(median * Math.Exp(sigma * ForecastMath.NormalQuantile(ChronosForecaster.Levels[l])));
        }
        return q;
    }

    [Fact]
    public void Maps_quantiles_to_bands_and_prob_up()
    {
        var q = FakeQuantiles(100);
        var h = ChronosForecaster.Map(q, 100m, [5, 20, 60]);

        Assert.Equal([5, 20, 60], h.Select(x => x.Horizon));
        Assert.All(h, x => Assert.True(x.P10 < x.P50 && x.P50 < x.P90));
        Assert.All(h, x => Assert.Equal(0.5, x.ProbUp, 3));
        Assert.Equal(100m, h[0].P50);
        // σ recovered from the P10–P90 band: 0.01 × √20
        Assert.Equal(0.01 * Math.Sqrt(20), h[1].Sigma, 3);
        Assert.True(h[2].P90 - h[2].P10 > h[0].P90 - h[0].P10);

        var cheap = ChronosForecaster.Map(q, 95m, [20])[0];
        Assert.True(cheap.ProbUp > 0.8);
        Assert.True(cheap.Mean > 0);
    }

    [Fact]
    public void Map_rescales_adjusted_values_and_rejects_bad_horizons()
    {
        var h = ChronosForecaster.Map(FakeQuantiles(50), 100m, [5], adjToClose: 2m)[0];
        Assert.Equal(100m, h.P50);
        Assert.Throws<ArgumentOutOfRangeException>(() => ChronosForecaster.Map(FakeQuantiles(50), 100m, [65]));
    }

    [Fact]
    public void Cdf_interpolates_between_levels()
    {
        var values = ChronosForecaster.Levels.Select(l => l * 100).ToArray();
        Assert.Equal(0.5, ChronosForecaster.Cdf(values, 50), 9);
        Assert.Equal(0.525, ChronosForecaster.Cdf(values, 52.5), 9);
        Assert.Equal(0.01, ChronosForecaster.Cdf(values, -5));
        Assert.Equal(0.99, ChronosForecaster.Cdf(values, 500));
    }

    sealed class FakeModel : IChronosModel
    {
        public List<(int Rows, long[] Groups)> Calls { get; } = [];

        // Each row's median = its last observed value × (1 + 1% × its index within its group).
        public float[] Predict(float[] context, long[] groupIds, int rows)
        {
            Calls.Add((rows, groupIds));
            var output = new float[rows * ChronosForecaster.PredictionLength * Q];
            for (var r = 0; r < rows; r++)
            {
                var inGroup = groupIds.Take(r).Count(g => g == groupIds[r]);
                var last = context[(r + 1) * ChronosForecaster.ContextLength - 1];
                FakeQuantiles(last * (1 + 0.01 * inGroup)).CopyTo(output, r * ChronosForecaster.PredictionLength * Q);
            }
            return output;
        }
    }

    [Fact]
    public void Runs_targets_with_covariates_in_shared_groups()
    {
        var ihsg = SyntheticBars.RandomWalk(300, seed: 1);
        var usd = SyntheticBars.RandomWalk(300, seed: 2, start: 16000);
        var targets = Enumerable.Range(0, 3)
            .Select(i => new ChronosTarget($"S{i}", SyntheticBars.RandomWalk(300, seed: 10 + i), [ihsg, usd]))
            .Append(new ChronosTarget("IHSG", ihsg, [usd]))
            .ToList();
        var model = new FakeModel();

        var rows = ChronosForecaster.Run(model, targets, batchSize: 2);

        Assert.Equal(2, model.Calls.Count);
        Assert.Equal(6, model.Calls[0].Rows);
        Assert.Equal([0L, 0, 0, 1, 1, 1], model.Calls[0].Groups);
        Assert.Equal([0L, 0, 0, 1, 1], model.Calls[1].Groups);
        Assert.Equal(12, rows.Count);
        Assert.All(rows, r => Assert.Equal("chronos-2", r.Model));
        // The target is row 0 of its group, so its median equals its last close (not a covariate's).
        Assert.All(rows, r => Assert.InRange(r.P50 - targets.Single(t => t.Code == r.Code).Bars[^1].Close, -0.05m, 0.05m));
        Assert.Equal(targets[0].Bars[^1].Date, rows[0].AsOf);
    }
}
