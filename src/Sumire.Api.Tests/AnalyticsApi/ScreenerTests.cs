using System.Text.Json;
using Sumire.Api.Analytics.Services;
using Sumire.Api.Data;
using Sumire.Api.Tests.Analytics;

namespace Sumire.Api.Tests.AnalyticsApi;

public class ScreenerTests
{
    static readonly Universe U = TestUniverse.Build(
        new()
        {
            ["UPPP"] = ("Energy", SyntheticBars.Linear(300, 1000, 5)),
            ["DOWN"] = ("Energy", SyntheticBars.Linear(300, 5000, -10)),
            ["BANK"] = ("Financials", SyntheticBars.RandomWalk(300, seed: 3)),
        },
        ihsg: SyntheticBars.RandomWalk(300, seed: 9),
        fundamentals: new()
        {
            ["BANK"] = new Fundamental { Code = "BANK", Pe = 8, Pb = 1.1, DivYield = 0.06, MarketCap = 1_000_000 },
            ["UPPP"] = new Fundamental { Code = "UPPP", Pe = 30, Pb = 4, DivYield = 0.01 },
        });

    static ScreenFilter F(string field, string op, object value) => new(field, op, JsonSerializer.SerializeToElement(value));

    [Fact]
    public void Numeric_filters_and_sort()
    {
        var r = Screener.Run(U, new ScreenRequest(null, [F("rsi", "gt", 70)], "rsi", true, null));
        Assert.Equal(["UPPP"], r.Rows.Select(x => x.Code));

        var all = Screener.Run(U, new ScreenRequest(null, null, "chgPct", false, null));
        Assert.Equal(3, all.Count);
        Assert.Equal(all.Rows.Select(x => x.ChgPct).Order(), all.Rows.Select(x => x.ChgPct));
    }

    [Fact]
    public void Text_filters_are_case_insensitive()
    {
        var r = Screener.Run(U, new ScreenRequest(null, [F("sector", "eq", "financials")], null, null, null));
        Assert.Equal(["BANK"], r.Rows.Select(x => x.Code));
        var c = Screener.Run(U, new ScreenRequest(null, [F("sector", "contains", "ERG")], null, null, null));
        Assert.Equal(2, c.Count);
    }

    [Fact]
    public void Missing_values_never_match()
    {
        // DOWN has no fundamentals, so a PE filter excludes it whatever the bound.
        var r = Screener.Run(U, new ScreenRequest(null, [F("pe", "lt", 1000)], null, null, null));
        Assert.DoesNotContain(r.Rows, x => x.Code == "DOWN");
        Assert.Equal(2, r.Count);
    }

    [Fact]
    public void Presets_combine_with_extra_filters()
    {
        Assert.Equal(["BANK"], Screener.Run(U, new ScreenRequest("value", null, null, null, null)).Rows.Select(x => x.Code));
        Assert.Equal(["BANK"], Screener.Run(U, new ScreenRequest("dividend", null, null, null, null)).Rows.Select(x => x.Code));
        Assert.Equal(0, Screener.Run(U, new ScreenRequest("dividend", [F("sector", "eq", "Energy")], null, null, null)).Count);

        var top = Screener.Run(U, new ScreenRequest("top-score", null, null, null, null));
        Assert.Equal(top.Rows.Select(x => x.Score).OrderDescending(), top.Rows.Select(x => x.Score));
        Assert.Equal("DOWN", top.Rows[^1].Code);
    }

    [Fact]
    public void Every_preset_runs()
    {
        foreach (var p in Screener.Presets.Keys)
            Screener.Run(U, new ScreenRequest(p, null, null, null, null));
    }

    [Theory]
    [InlineData("nope", "gt", 1)]
    [InlineData("rsi", "between", 1)]
    [InlineData("sector", "gt", "x")]
    [InlineData("rsi", "gt", "x")]
    public void Invalid_filters_throw(string field, string op, object value) =>
        Assert.Throws<ScreenerException>(() => Screener.Run(U, new ScreenRequest(null, [F(field, op, value)], null, null, null)));

    [Fact]
    public void Unknown_preset_and_sort_throw()
    {
        Assert.Throws<ScreenerException>(() => Screener.Run(U, new ScreenRequest("nope", null, null, null, null)));
        Assert.Throws<ScreenerException>(() => Screener.Run(U, new ScreenRequest(null, null, "nope", null, null)));
    }

    [Fact]
    public void Limit_is_applied_after_counting()
    {
        var r = Screener.Run(U, new ScreenRequest(null, null, null, null, 1));
        Assert.Equal(3, r.Count);
        Assert.Single(r.Rows);
    }
}
