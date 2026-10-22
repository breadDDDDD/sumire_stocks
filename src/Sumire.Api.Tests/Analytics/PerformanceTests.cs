using System.Diagnostics;
using Sumire.Api.Analytics.Core;
using Sumire.Api.Forecasting;

namespace Sumire.Api.Tests.Analytics;

public class PerformanceTests
{
    [Fact]
    public void Full_snapshot_and_garch_for_750_bars_is_fast()
    {
        var bars = SyntheticBars.RandomWalk(750, seed: 42);
        var bench = SyntheticBars.RandomWalk(750, seed: 43);
        Run(); // warm up JIT

        var best = TimeSpan.MaxValue;
        for (var i = 0; i < 5; i++)
        {
            var sw = Stopwatch.StartNew();
            Run();
            best = sw.Elapsed < best ? sw.Elapsed : best;
        }
        // Target is < 50 ms; the bound is loose so slow CI machines don't flake.
        Assert.True(best < TimeSpan.FromMilliseconds(150), $"took {best.TotalMilliseconds:0} ms");

        void Run()
        {
            var t = TechnicalsCalculator.Snapshot(bars);
            RiskStats.Compute(bars, bench);
            var g = Garch.Fit(bars);
            BaselineForecaster.Forecast(bars, g);
            SignalScorer.Score(new SignalInputs { Technicals = t, VolPercentile = g?.VolPercentile });
        }
    }
}
