using Sumire.Api.Analytics.Core;

namespace Sumire.Api.Tests.Analytics;

public class SignalScorerTests
{
    [Theory]
    [InlineData(80, "Strongly Bullish")]
    [InlineData(60, "Strongly Bullish")]
    [InlineData(30, "Bullish")]
    [InlineData(0, "Neutral")]
    [InlineData(-20, "Bearish")]
    [InlineData(-59.9, "Bearish")]
    [InlineData(-60, "Strongly Bearish")]
    public void Labels(double total, string label) => Assert.Equal(label, SignalScorer.Label(total));

    [Fact]
    public void Missing_components_are_skipped_and_weights_renormalized()
    {
        var r = SignalScorer.Combine([
            new(SignalComponent.Trend, 80, 25, ""),
            new(SignalComponent.Momentum, 20, 25, ""),
            new(SignalComponent.Valuation, null, 50, ""),
        ]);
        Assert.Equal(50, r.Total);
        Assert.Equal(0.5, r.Coverage, 9);
        Assert.Equal("Bullish", r.Label);
    }

    [Fact]
    public void No_data_scores_neutral_zero()
    {
        var r = SignalScorer.Score(new SignalInputs());
        Assert.Equal(0, r.Total);
        Assert.Equal("Neutral", r.Label);
        Assert.Equal(0, r.Coverage);
        Assert.Equal(7, r.Components.Count);
        Assert.All(r.Components, c => Assert.False(string.IsNullOrEmpty(c.Reason)));
    }

    [Fact]
    public void Strong_uptrend_scores_bullish_with_readable_reasons()
    {
        var t = TechnicalsCalculator.Snapshot(SyntheticBars.Linear(300))!;
        var r = SignalScorer.Score(new SignalInputs { Technicals = t, RelReturn3M = 0.2, VolPercentile = 30 });

        Assert.True(r.Total >= 60, $"total {r.Total}");
        Assert.Equal("Strongly Bullish", r.Label);
        var trend = r.Components.Single(c => c.Component == SignalComponent.Trend);
        Assert.Equal(100, trend.Score);
        Assert.StartsWith("Above 200-day MA, above 50-day, trend strong (ADX ", trend.Reason);
        Assert.Contains("+20.0% vs IHSG over 3M", r.Components.Single(c => c.Component == SignalComponent.Momentum).Reason);
    }

    [Fact]
    public void All_scores_stay_in_range()
    {
        var t = TechnicalsCalculator.Snapshot(SyntheticBars.Linear(300, start: 5000, step: -15))!;
        var r = SignalScorer.Score(new SignalInputs
        {
            Technicals = t,
            RelReturn3M = -5,
            RelReturn6M = -5,
            VolPercentile = 100,
            Valuation = new ValuationInputs(-3, 10, 50, 1, 0),
            Forecast = new ForecastInputs("baseline", 20, 0, -1, 0.9),
            News = [new NewsInputs(-5, 5, 0)],
        });
        Assert.All(r.Components, c => Assert.InRange(c.Score!.Value, -100, 100));
        Assert.Equal("Strongly Bearish", r.Label);
        Assert.Equal(1, r.Coverage, 9);
    }

    [Fact]
    public void Volatility_high_percentile_counts_against()
    {
        Assert.Equal(-60, SignalScorer.Volatility(80, 10).Score);
        Assert.Equal(60, SignalScorer.Volatility(20, 10).Score);
        Assert.Equal("GARCH vol at 82nd percentile (elevated)", SignalScorer.Volatility(82, 10).Reason);
    }

    [Fact]
    public void Valuation_cheap_vs_sector_is_positive()
    {
        var c = SignalScorer.Valuation(new ValuationInputs(8, 16, 1, 2, null), 15);
        Assert.Equal(100, c.Score);
        Assert.Equal("PE 8.0 vs sector 16.0, PB 1.0 vs sector 2.0", c.Reason);
    }

    [Fact]
    public void Forecast_is_damped_by_a_weak_track_record()
    {
        var full = SignalScorer.Forecast(new ForecastInputs("baseline", 20, 0.7, 0.05), 10).Score;
        var weak = SignalScorer.Forecast(new ForecastInputs("baseline", 20, 0.7, 0.05, HitRate: 0.5), 10).Score;
        Assert.Equal(100, full);
        Assert.Equal(50, weak);
    }

    [Fact]
    public void News_decays_to_nothing_after_five_days()
    {
        Assert.Null(SignalScorer.News([new NewsInputs(1, 1, 6)], 10).Score);
        var fresh = SignalScorer.News([new NewsInputs(1, 0.8, 0)], 10);
        Assert.Equal(80, fresh.Score);
        Assert.Equal("1 recent insight, positive tone", fresh.Reason);
        // A fresh negative outweighs an older positive.
        var mixed = SignalScorer.News([new NewsInputs(1, 1, 4), new NewsInputs(-1, 1, 0)], 10);
        Assert.True(mixed.Score < 0);
    }

    [Fact]
    public void Custom_weights_change_the_mix()
    {
        var input = new SignalInputs { VolPercentile = 0, Valuation = new ValuationInputs(30, 15, null, null, null) };
        Assert.Equal(-20, SignalScorer.Score(input).Total); // (+100 × 10 - 100 × 15) / 25
        var r = SignalScorer.Score(input, new SignalWeights(Volatility: 30, Valuation: 10));
        Assert.Equal(50, r.Total);
    }
}
