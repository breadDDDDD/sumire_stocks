using Sumire.Api.Forecasting;
using Sumire.Api.Tests.Analytics;

namespace Sumire.Api.Tests.Forecasting.Core;

public class BaselineForecasterTests
{
    [Fact]
    public void Quantiles_are_monotonic_and_widen_with_horizon()
    {
        var bars = SyntheticBars.RandomWalk(600, seed: 11);
        var f = BaselineForecaster.Forecast(bars)!;

        Assert.Equal("garch", f.VolSource);
        Assert.Equal([5, 20, 60], f.Horizons.Select(h => h.Horizon));
        foreach (var h in f.Horizons)
        {
            Assert.True(h.P10 < h.P50 && h.P50 < h.P90, $"h={h.Horizon}");
            Assert.InRange(h.ProbUp, 0, 1);
        }
        var widths = f.Horizons.Select(h => h.P90 - h.P10).ToList();
        Assert.True(widths[0] < widths[1] && widths[1] < widths[2]);
    }

    [Fact]
    public void Positive_drift_means_prob_up_above_half()
    {
        var up = BaselineForecaster.Forecast(SyntheticBars.Linear(300, start: 1000, step: 2)
            .Select((b, i) => b with { Close = b.Close + (i % 2) * 15, AdjClose = b.AdjClose + (i % 2) * 15 }).ToList())!;
        Assert.All(up.Horizons, h => Assert.True(h.ProbUp > 0.5));
        Assert.All(up.Horizons, h => Assert.True(h.P50 > up.LastClose));
    }

    [Fact]
    public void Short_history_uses_sample_variance_and_tiny_history_gives_null()
    {
        var f = BaselineForecaster.Forecast(SyntheticBars.RandomWalk(60, seed: 1))!;
        Assert.Equal("sample", f.VolSource);
        Assert.Null(BaselineForecaster.Forecast(SyntheticBars.RandomWalk(10, seed: 1)));
    }

    [Fact]
    public void Band_covers_about_80_percent_on_a_random_walk()
    {
        const int paths = 300, history = 300, horizon = 20;
        var inside = 0;
        for (var seed = 0; seed < paths; seed++)
        {
            var bars = SyntheticBars.RandomWalk(history + horizon, seed + 1000, mu: 0, sigma: 0.02);
            var f = BaselineForecaster.Forecast(bars.Take(history).ToList(), horizons: [horizon])!;
            var h = f.Horizons[0];
            var actual = bars[^1].Close;
            if (actual >= h.P10 && actual <= h.P90) inside++;
        }
        Assert.InRange((double)inside / paths, 0.72, 0.88);
    }
}
