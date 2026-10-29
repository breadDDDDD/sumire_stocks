using System.Text.Json;
using Sumire.Api.Analytics.Core;
using Sumire.Api.Analytics.Services;
using Sumire.Api.Tests.AnalyticsApi;

namespace Sumire.Api.Tests.Analytics;

public class AnalyticsEdgeCaseTests
{
    static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [Fact]
    public void High_and_low_52w_cover_one_calendar_year()
    {
        // A spike just over a year ago must fall outside the 52-week range.
        var closes = Enumerable.Range(0, 300).Select(i => 1000.0).ToArray();
        var bars = SyntheticBars.FromCloses(closes);
        var outside = bars.FindLastIndex(b => b.Date < bars[^1].Date.AddYears(-1));
        bars[outside] = bars[outside] with { High = 5000 };
        var inside = bars.FindIndex(b => b.Date > bars[^1].Date.AddYears(-1));
        bars[inside] = bars[inside] with { High = 2000 };

        var t = TechnicalsCalculator.Snapshot(bars)!;
        Assert.Equal(2000m, t.High52w);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(30)]
    [InlineData(300)]
    public void Flat_or_short_series_analyse_and_serialize_without_nan(int n)
    {
        var bars = TestUniverse.Flat(n);
        var a = UniverseBuilder.Analyze(TestUniverse.Stock("FLAT"), bars, TestUniverse.Flat(n, 7000), null, null, [], null, null,
            SignalWeights.Default);
        Assert.True(double.IsFinite(a.Score.Total));
        // System.Text.Json throws on NaN/Infinity, which would turn every screen into a 500.
        var json = JsonSerializer.Serialize(new { a.Technicals, a.Risk, a.Score }, Web);
        Assert.DoesNotContain("NaN", json);
    }

    [Fact]
    public void Sector_index_keeps_a_members_move_across_a_missing_bar()
    {
        var a = SyntheticBars.FromCloses([100, 100, 110, 121]);
        // B has no bar on day 2, so its +21% from day 1 to day 3 must still show up on day 3.
        var b = SyntheticBars.FromCloses([100, 100, 110, 121]).Where((_, i) => i != 2).ToList();
        var u = TestUniverse.Build(new()
        {
            ["AAAA"] = ("Energy", a),
            ["BBBB"] = ("Energy", b),
        });
        var levels = u.Sectors.Single().Levels;

        Assert.Equal(4, levels.Count);
        Assert.Equal(1100m, levels[2].Close); // only A moved +10%
        // Day 3: A +10%, B +21% (from day 1), equal weights.
        Assert.Equal(1100 * (1 + (0.10 + 0.21) / 2), (double)levels[3].Close, 3);
    }

    [Fact]
    public void Screener_rejects_filters_without_field_or_op()
    {
        var u = TestUniverse.Build(new() { ["AAAA"] = ("Energy", TestUniverse.Flat(60)) });
        var value = JsonDocument.Parse("1").RootElement;
        Assert.Throws<ScreenerException>(() => Screener.Run(u, new ScreenRequest(null, [new ScreenFilter(null!, "gt", value)], null, null, null)));
        Assert.Throws<ScreenerException>(() => Screener.Run(u, new ScreenRequest(null, [new ScreenFilter("rsi", null!, value)], null, null, null)));
        Assert.Throws<ScreenerException>(() => Screener.Run(u, new ScreenRequest(null, [new ScreenFilter("rsi", "gt", default)], null, null, null)));
    }
}
