using Sumire.Api.Analytics.Core;

namespace Sumire.Api.Tests.Analytics;

public class RiskStatsTests
{
    [Fact]
    public void Beta_and_correlation_of_series_vs_itself_are_one()
    {
        var bars = SyntheticBars.RandomWalk(400, seed: 1);
        var (beta, corr) = RiskStats.BetaCorrelation(bars, bars);
        Assert.Equal(1, beta!.Value, 9);
        Assert.Equal(1, corr!.Value, 9);
    }

    [Fact]
    public void Beta_of_leveraged_series_is_the_leverage()
    {
        var index = SyntheticBars.RandomWalk(300, seed: 2);
        // Daily log returns doubled -> price = start * (index/start)^2
        var stock = index.Select(b =>
        {
            var c = (decimal)(1000 * Math.Pow((double)b.AdjClose / 1000, 2));
            return b with { Close = c, AdjClose = c };
        }).ToList();
        var (beta, corr) = RiskStats.BetaCorrelation(stock, index);
        Assert.Equal(2, beta!.Value, 6);
        Assert.Equal(1, corr!.Value, 6);
    }

    [Fact]
    public void Beta_aligns_by_date_and_needs_enough_overlap()
    {
        var a = SyntheticBars.RandomWalk(100, seed: 3);
        var b = a.Where((_, i) => i % 2 == 0).ToList();
        var (beta, _) = RiskStats.BetaCorrelation(a, b);
        Assert.Equal(1, beta!.Value, 9);
        Assert.Equal((null, null), RiskStats.BetaCorrelation(a.Take(10).ToList(), a));
    }

    [Fact]
    public void Period_returns_use_trading_day_offsets_and_year_start()
    {
        var bars = SyntheticBars.Linear(300, start: 100, step: 1);
        var r = RiskStats.Returns(bars);
        var last = 399.0;
        Assert.Equal(last / 398 - 1, r.D1!.Value, 9);
        Assert.Equal(last / 394 - 1, r.W1!.Value, 9);
        Assert.Equal(last / 378 - 1, r.M1!.Value, 9);
        Assert.Equal(last / 336 - 1, r.M3!.Value, 9);
        var yearAgo = bars.Last(b => b.Date <= bars[^1].Date.AddYears(-1));
        Assert.Equal(last / (double)yearAgo.AdjClose - 1, r.Y1!.Value, 9);

        var lastPrevYear = bars.Last(b => b.Date.Year < bars[^1].Date.Year);
        Assert.Equal(last / (double)lastPrevYear.AdjClose - 1, r.Ytd!.Value, 9);

        var shortR = RiskStats.Returns(bars.Take(3).ToList());
        Assert.Null(shortR.W1);
        Assert.Null(shortR.Ytd);
    }

    [Fact]
    public void One_year_return_is_calendar_based_and_needs_a_full_year()
    {
        // 242 weekday bars cover less than a calendar year; 300 cover more.
        Assert.Null(RiskStats.Returns(SyntheticBars.Linear(242)).Y1);

        var bars = SyntheticBars.Linear(300, start: 100, step: 1);
        var target = bars[^1].Date.AddYears(-1);
        var i = SeriesMath.YearAgoIndex(bars);
        Assert.True(bars[i].Date <= target && bars[i + 1].Date > target);
    }

    [Fact]
    public void Sharpe_uses_simple_returns()
    {
        // +10% / -9.09% alternating: the log mean is zero, the simple mean is positive.
        var closes = Enumerable.Range(0, 101).Select(i => i % 2 == 0 ? 1000.0 : 1100.0);
        var sharpe = RiskStats.Sharpe(SyntheticBars.FromCloses(closes), riskFree: 0);
        Assert.True(sharpe > 0.5);
    }

    [Fact]
    public void Max_drawdown_finds_peak_and_trough()
    {
        var bars = SyntheticBars.FromCloses([100, 120, 90, 110, 60, 130, 125]);
        var dd = RiskStats.MaxDrawdown(bars)!;
        Assert.Equal(0.5, dd.Depth, 9);
        Assert.Equal(bars[1].Date, dd.Peak);
        Assert.Equal(bars[4].Date, dd.Trough);
    }

    [Fact]
    public void Sharpe_sign_follows_excess_return()
    {
        var up = SyntheticBars.RandomWalk(300, seed: 5, mu: 0.003, sigma: 0.01);
        var down = SyntheticBars.RandomWalk(300, seed: 5, mu: -0.003, sigma: 0.01);
        Assert.True(RiskStats.Sharpe(up) > 1);
        Assert.True(RiskStats.Sharpe(down) < -1);
        Assert.Null(RiskStats.Sharpe(up.Take(10).ToList()));
    }

    [Fact]
    public void Compute_fills_everything_with_a_benchmark()
    {
        var bars = SyntheticBars.RandomWalk(300, seed: 6);
        var s = RiskStats.Compute(bars, bars);
        Assert.NotNull(s.Beta);
        Assert.NotNull(s.Sharpe);
        Assert.NotNull(s.MaxDrawdown);
        Assert.InRange(s.Volatility!.Value, 0.2, 0.45); // sigma 2%/day ≈ 32% a year
    }
}
