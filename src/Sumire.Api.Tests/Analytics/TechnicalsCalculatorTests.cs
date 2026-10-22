using Skender.Stock.Indicators;
using Sumire.Api.Analytics.Core;

namespace Sumire.Api.Tests.Analytics;

public class TechnicalsCalculatorTests
{
    [Fact]
    public void Monotonic_up_series_is_overbought_and_trending()
    {
        var bars = SyntheticBars.Linear(300);
        var t = TechnicalsCalculator.Snapshot(bars)!;

        Assert.True(t.Rsi > 99, $"RSI {t.Rsi}");
        Assert.True(t.Sma20 > t.Sma50 && t.Sma50 > t.Sma200);
        Assert.True(t.Sma50Slope > 0 && t.Sma200Slope > 0);
        Assert.True(t.Adx > 25);
        Assert.True(t.MacdHist is not null);
        Assert.True(t.Roc20 > 0);
        Assert.True(t.ObvSlope > 0.5);
        Assert.Equal(t.Close, bars[^1].Close);
        Assert.Equal(Math.Round(bars[^1].High, 2), t.High52w);
        Assert.Null(t.LastDeathCross);
        Assert.Empty(t.Resistance); // nothing overhead in a straight line up
    }

    [Fact]
    public void Sma_matches_hand_calculation()
    {
        var bars = SyntheticBars.Linear(250, start: 100, step: 1);
        var t = TechnicalsCalculator.Snapshot(bars)!;
        // Last 20 closes are 330..349 -> mean 339.5
        Assert.Equal(339.5, t.Sma20!.Value, 6);
        Assert.Equal(349 - 99.5, t.Sma200!.Value, 6);
    }

    [Fact]
    public void Constant_series_has_zero_volatility()
    {
        var bars = SyntheticBars.FromCloses(Enumerable.Repeat(500.0, 100));
        var t = TechnicalsCalculator.Snapshot(bars)!;
        Assert.Equal(0, t.Hv20!.Value, 9);
        Assert.Equal(0, t.Hv60!.Value, 9);
        Assert.Equal(1, t.VolumeRatio!.Value, 9);
        Assert.Equal(500m * 1_000_000, t.ValueTraded);
    }

    [Fact]
    public void Short_history_returns_nulls_instead_of_throwing()
    {
        var bars = SyntheticBars.Linear(15);
        var t = TechnicalsCalculator.Snapshot(bars)!;
        Assert.Null(t.Sma20);
        Assert.Null(t.Sma200);
        Assert.Null(t.Hv20);
        Assert.Null(t.LastGoldenCross);
        Assert.NotNull(t.Pivots);
        Assert.Null(TechnicalsCalculator.Snapshot([]));
    }

    [Fact]
    public void Detects_golden_and_death_cross_dates()
    {
        // Down 300 bars, then up 300: SMA50 crosses above SMA200 on the way up.
        var closes = Enumerable.Range(0, 300).Select(i => 2000.0 - 3 * i)
            .Concat(Enumerable.Range(1, 300).Select(i => 1103.0 + 3 * i));
        var bars = SyntheticBars.FromCloses(closes);
        var t = TechnicalsCalculator.Snapshot(bars)!;

        Assert.NotNull(t.LastGoldenCross);
        Assert.Equal(CrossKind.Golden, t.LastGoldenCross!.Kind);
        Assert.True(t.LastGoldenCross.Date > bars[300].Date);

        var series = TechnicalsCalculator.Series(bars);
        var i = bars.FindIndex(b => b.Date == t.LastGoldenCross.Date);
        Assert.True(series.Sma50[i] > series.Sma200[i]);
        Assert.True(series.Sma50[i - 1] <= series.Sma200[i - 1]);
    }

    [Fact]
    public void Classic_pivots()
    {
        var p = TechnicalsCalculator.ClassicPivots(new Quote { High = 110, Low = 90, Close = 100 });
        Assert.Equal(100m, p.P);
        Assert.Equal(110m, p.R1);
        Assert.Equal(90m, p.S1);
        Assert.Equal(120m, p.R2);
        Assert.Equal(80m, p.S2);
        Assert.Equal(130m, p.R3);
        Assert.Equal(70m, p.S3);
    }

    [Fact]
    public void Swing_levels_surround_the_close()
    {
        // Oscillating series: repeated peaks near 1100 and troughs near 900, ending at 1000.
        var closes = Enumerable.Range(0, 300).Select(i => 1000 + 100 * Math.Sin(i * 2 * Math.PI / 40));
        var bars = SyntheticBars.FromCloses(closes);
        var t = TechnicalsCalculator.Snapshot(bars)!;

        Assert.NotEmpty(t.Support);
        Assert.NotEmpty(t.Resistance);
        Assert.All(t.Support, l => Assert.True(l.Price < t.Close));
        Assert.All(t.Resistance, l => Assert.True(l.Price > t.Close));
        Assert.True(t.Resistance[0].Touches >= 3);
    }

    [Fact]
    public void Series_arrays_align_with_bars()
    {
        var bars = SyntheticBars.Linear(260);
        var s = TechnicalsCalculator.Series(bars);
        Assert.Equal(bars.Count, s.Dates.Count);
        Assert.Equal(bars.Count, s.Sma200.Length);
        Assert.Null(s.Sma200[198]);
        Assert.NotNull(s.Sma200[199]);
    }
}
