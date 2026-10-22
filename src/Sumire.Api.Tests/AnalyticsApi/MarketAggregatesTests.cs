using Sumire.Api.Analytics.Core;
using Sumire.Api.Analytics.Services;
using Sumire.Api.Data;
using Sumire.Api.Tests.Analytics;

namespace Sumire.Api.Tests.AnalyticsApi;

public class MarketAggregatesTests
{
    static TechnicalsSnapshot Ihsg(decimal close, double sma50, double sma200) =>
        new() { Date = new DateOnly(2026, 10, 22), Close = close, Sma50 = sma50, Sma200 = sma200 };

    static Breadth BreadthAt(double pctAbove50) => new(new DateOnly(2026, 10, 22), 100, 50, 40, 10, pctAbove50, 50, 0, 0);

    [Fact]
    public void Regime_risk_on_when_trend_breadth_and_vol_agree()
    {
        var r = MarketRegime.Classify(Ihsg(7000, 6800, 6500), BreadthAt(70), 20);
        Assert.Equal(Regime.RiskOn, r.Label);
        Assert.Equal(4, r.Score);
        Assert.Contains("IHSG above 200-day MA", r.Reasons);
    }

    [Fact]
    public void Regime_risk_off_and_neutral()
    {
        var off = MarketRegime.Classify(Ihsg(6000, 6500, 6800), BreadthAt(25), 90);
        Assert.Equal(Regime.RiskOff, off.Label);
        Assert.Contains("Volatility elevated (GARCH 90th percentile)", off.Reasons);

        var mixed = MarketRegime.Classify(Ihsg(7000, 7200, 6500), BreadthAt(50), 50);
        Assert.Equal(Regime.Neutral, mixed.Label);
        Assert.Equal(0, mixed.Score);

        Assert.Equal(Regime.Neutral, MarketRegime.Classify(null, null, null).Label);
    }

    [Fact]
    public void Breadth_counts_advancers_and_mas()
    {
        var u = TestUniverse.Build(new()
        {
            ["AAAA"] = ("Energy", SyntheticBars.Linear(300, 1000, 5)),
            ["BBBB"] = ("Energy", SyntheticBars.Linear(300, 5000, -10)),
            ["CCCC"] = ("Energy", TestUniverse.Flat(300)),
        });
        var b = u.Breadth!;
        Assert.Equal((3, 1, 1, 1), (b.Total, b.Advancers, b.Decliners, b.Unchanged));
        Assert.Equal(33.3, b.PctAboveSma200);
        Assert.Equal(1, b.NewHighs);
        Assert.Equal(1, b.NewLows);
    }

    [Fact]
    public void Equal_weighted_sector_index_averages_member_returns()
    {
        // One member flat, one up 10% on the last day -> index up 5%.
        var flat = TestUniverse.Flat(30);
        var jump = TestUniverse.Flat(30).Select((b, i) => i == 29 ? b with { Close = 1100, AdjClose = 1100 } : b).ToList();
        var u = TestUniverse.Build(new() { ["FLAT"] = ("Energy", flat), ["JUMP"] = ("Energy", jump) });

        var s = u.Sectors.Single();
        Assert.Equal("equal", s.Weighting);
        Assert.Equal(5, s.D1!.Value, 6);
        Assert.Equal(1050m, s.Levels[^1].Close);
        Assert.Equal(0.5, s.Members[0].Weight);
    }

    [Fact]
    public void Cap_weighted_sector_index_uses_market_caps()
    {
        var flat = TestUniverse.Flat(30);
        var jump = TestUniverse.Flat(30).Select((b, i) => i == 29 ? b with { Close = 1100, AdjClose = 1100 } : b).ToList();
        var u = TestUniverse.Build(
            new() { ["FLAT"] = ("Energy", flat), ["JUMP"] = ("Energy", jump), ["IDX1"] = ("Index", flat) },
            fundamentals: new()
            {
                ["FLAT"] = new Fundamental { Code = "FLAT", MarketCap = 300 },
                ["JUMP"] = new Fundamental { Code = "JUMP", MarketCap = 100 },
            });

        var s = Assert.Single(u.Sectors); // "Index" is not a sector
        Assert.Equal("marketCap", s.Weighting);
        Assert.Equal(2.5, s.D1!.Value, 6);
        Assert.Equal("FLAT", s.Members[0].Code);
        Assert.Equal(0.75, s.Members[0].Weight);
    }

    [Fact]
    public void Sector_rotation_needs_a_benchmark()
    {
        var ihsg = SyntheticBars.RandomWalk(300, seed: 1);
        var u = TestUniverse.Build(new()
        {
            ["AAAA"] = ("Energy", SyntheticBars.RandomWalk(300, seed: 2)),
            ["BBBB"] = ("Financials", SyntheticBars.RandomWalk(300, seed: 3)),
        }, ihsg);
        Assert.Equal(2, u.Sectors.Count);
        Assert.All(u.Sectors, s =>
        {
            Assert.NotNull(s.Rotation);
            Assert.Equal(5, s.Trail.Count);
            Assert.NotNull(s.RsM3);
        });
        Assert.NotNull(u.Ihsg);
        Assert.Equal(u.Ihsg!.AsOf, u.AsOf);
    }

    [Fact]
    public void Sector_medians_ignore_negative_pe()
    {
        var m = UniverseBuilder.Medians(
            [TestUniverse.Stock("A"), TestUniverse.Stock("B"), TestUniverse.Stock("C")],
            new Dictionary<string, Fundamental>
            {
                ["A"] = new() { Code = "A", Pe = 10, Pb = 1 },
                ["B"] = new() { Code = "B", Pe = -5, Pb = 2 },
                ["C"] = new() { Code = "C", Pe = 20, Pb = 3 },
            });
        Assert.Equal(15, m["Energy"].Pe);
        Assert.Equal(2, m["Energy"].Pb);
        Assert.Equal(3, m["Energy"].N);
    }

    [Fact]
    public void Model_replaces_baseline_in_score_only_with_a_better_track_record()
    {
        var a = TestUniverse.Build(new() { ["AAAA"] = ("Energy", SyntheticBars.RandomWalk(300, seed: 4)) }).Stocks["AAAA"];
        var chronos = new Forecast { Code = "AAAA", AsOf = a.AsOf, Horizon = 20, Model = "chronos-2", P10 = 1, P50 = a.Last * 1.1m, P90 = 2, ProbUp = 0.8 };
        ForecastMetric M(string model, double dir, double mape, int n = 100) =>
            new() { Model = model, Horizon = 20, Scope = "all", DirAccuracy = dir, Mape = mape, N = n };

        Assert.Equal("baseline", UniverseBuilder.ChooseForecast(a, [M("baseline", 0.52, 0.05), M("chronos-2", 0.50, 0.04)], [chronos])!.Model);
        Assert.Equal("baseline", UniverseBuilder.ChooseForecast(a, [M("baseline", 0.52, 0.05), M("chronos-2", 0.60, 0.04, n: 10)], [chronos])!.Model);
        var pick = UniverseBuilder.ChooseForecast(a, [M("baseline", 0.52, 0.05), M("chronos-2", 0.58, 0.04)], [chronos])!;
        Assert.Equal("chronos-2", pick.Model);
        Assert.Equal(0.1, pick.P50Return, 6);
        Assert.Equal(0.58, pick.HitRate);
    }
}
