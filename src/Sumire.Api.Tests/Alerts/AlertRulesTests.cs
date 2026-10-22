using Sumire.Api.Alerts;
using Sumire.Api.Analytics.Core;
using Sumire.Api.Data;
using Sumire.Api.Jobs;
using Sumire.Api.Market;

namespace Sumire.Api.Tests.Alerts;

public class AlertRulesTests
{
    static Alert A(string type, string? code = "BBCA", string p = "{}") => new() { Code = code, Type = type, Params = p };

    static Quote Q(decimal price, decimal? pct = null) =>
        new("BBCA", "Bank Central Asia", price, null, pct, null, null, null, null, null, null, null, "live");

    static DateTimeOffset Wib(int d, int h) => new(2026, 10, d, h, 0, 0, TimeSpan.FromHours(7));

    [Theory]
    [InlineData("price_above", """{"price":9000}""", null)]
    [InlineData("price_above", """{"price":-1}""", "price")]
    [InlineData("pct_change", """{}""", "pct")]
    [InlineData("rsi_below", """{"value":30}""", null)]
    [InlineData("rsi_below", """{"value":130}""", "value")]
    [InlineData("ma_cross", """{"fast":"sma50","slow":"sma200"}""", null)]
    [InlineData("ma_cross", """{"fast":"sma200","slow":"sma50"}""", "fast")]
    [InlineData("ma_cross", """{"direction":"sideways"}""", "direction")]
    [InlineData("new_52w_high", """{}""", null)]
    [InlineData("bogus", """{}""", "Unknown type")]
    [InlineData("price_above", """[1]""", "object")]
    public void Validate_checks_type_and_params(string type, string p, string? errorContains)
    {
        var error = AlertRules.Validate(A(type, p: p));
        if (errorContains is null) Assert.Null(error);
        else Assert.Contains(errorContains, error);
    }

    [Fact]
    public void Code_is_required_except_for_negative_insight()
    {
        Assert.NotNull(AlertRules.Validate(A(AlertRules.PriceAbove, code: null, p: """{"price":1}""")));
        Assert.Null(AlertRules.Validate(A(AlertRules.NegativeInsight, code: null)));
    }

    [Fact]
    public void Price_alerts_fire_at_or_through_the_level()
    {
        var above = A(AlertRules.PriceAbove, p: """{"price":9000}""");
        Assert.Null(AlertRules.EvaluateQuote(above, Q(8975)));
        Assert.Equal("Violet alert: BBCA at 9,025, above 9,000", AlertRules.EvaluateQuote(above, Q(9025)));

        var below = A(AlertRules.PriceBelow, p: """{"price":8500}""");
        Assert.NotNull(AlertRules.EvaluateQuote(below, Q(8500)));
        Assert.Null(AlertRules.EvaluateQuote(below, Q(8525)));
    }

    [Fact]
    public void Pct_change_uses_absolute_move()
    {
        var alert = A(AlertRules.PctChange, p: """{"pct":3}""");
        Assert.Contains("down 3.4%", AlertRules.EvaluateQuote(alert, Q(9000, -3.4m)));
        Assert.Contains("up 3%", AlertRules.EvaluateQuote(alert, Q(9000, 3m)));
        Assert.Null(AlertRules.EvaluateQuote(alert, Q(9000, 2.9m)));
    }

    [Fact]
    public void Cooldown_is_once_per_wib_day()
    {
        var alert = A(AlertRules.PriceAbove);
        Assert.True(AlertRules.CanFire(alert, Wib(22, 10)));

        alert.LastTriggeredAt = Wib(22, 9).ToUniversalTime();
        Assert.False(AlertRules.CanFire(alert, Wib(22, 15)));
        // 23:30 UTC on the 22nd is already the 23rd in WIB.
        Assert.True(AlertRules.CanFire(alert, new DateTimeOffset(2026, 10, 22, 23, 30, 0, TimeSpan.Zero)));

        alert.Active = false;
        Assert.False(AlertRules.CanFire(alert, Wib(25, 10)));
    }

    static List<Bar> Bars(IEnumerable<decimal> closes)
    {
        var day = new DateOnly(2025, 1, 1);
        return closes.Select((c, i) => new Bar(day.AddDays(i), c, c + 1, c - 1, c, c, 1000)).ToList();
    }

    [Fact]
    public void Price_crossing_above_sma50_fires_once_on_the_cross_bar()
    {
        var closes = Enumerable.Repeat(100m, 60).Concat(Enumerable.Range(0, 5).Select(i => 99m - i)).ToList(); // drift below MA
        var alert = A(AlertRules.MaCross, p: """{"fast":"price","slow":"sma50","direction":"above"}""");

        Assert.Null(AlertRules.EvaluateBars(alert, Bars(closes)));
        closes.Add(105m);
        Assert.Equal("Violet alert: BBCA price crossed above MA50", AlertRules.EvaluateBars(alert, Bars(closes)));
        closes.Add(106m);
        Assert.Null(AlertRules.EvaluateBars(alert, Bars(closes))); // already above: no new cross
    }

    [Fact]
    public void Direction_filter_ignores_the_other_cross()
    {
        var closes = Enumerable.Repeat(100m, 60).Append(90m).ToList();
        Assert.Null(AlertRules.EvaluateBars(A(AlertRules.MaCross, p: """{"fast":"price","slow":"sma50","direction":"above"}"""), Bars(closes)));
        Assert.Contains("crossed below", AlertRules.EvaluateBars(A(AlertRules.MaCross, p: """{"fast":"price","slow":"sma50"}"""), Bars(closes)));
    }

    [Fact]
    public void Rsi_alerts_use_latest_rsi()
    {
        var falling = Bars(Enumerable.Range(0, 40).Select(i => 200m - i * 3));
        Assert.NotNull(AlertRules.EvaluateBars(A(AlertRules.RsiBelow, p: """{"value":30}"""), falling));
        Assert.Null(AlertRules.EvaluateBars(A(AlertRules.RsiAbove, p: """{"value":70}"""), falling));
    }

    [Fact]
    public void New_52w_high_needs_a_higher_high_than_the_prior_year()
    {
        var closes = Enumerable.Range(0, 100).Select(i => 100m + (i % 10)).ToList();
        var alert = A(AlertRules.New52wHigh);
        Assert.Null(AlertRules.EvaluateBars(alert, Bars(closes.Append(105m))));
        Assert.Contains("52-week high", AlertRules.EvaluateBars(alert, Bars(closes.Append(120m))));
    }

    [Fact]
    public void Negative_insight_respects_confidence_and_sentiment()
    {
        var alert = A(AlertRules.NegativeInsight, code: null);
        Insight I(double s, string c) => new() { Kind = "stock", Subject = "ADRO", Sentiment = s, Confidence = c, Body = "{}", Sources = "[]" };

        Assert.NotNull(AlertRules.EvaluateInsight(alert, I(-0.6, "high")));
        Assert.Null(AlertRules.EvaluateInsight(alert, I(-0.6, "med")));
        Assert.Null(AlertRules.EvaluateInsight(alert, I(-0.1, "high")));
        Assert.NotNull(AlertRules.EvaluateInsight(A(AlertRules.NegativeInsight, p: """{"minConfidence":"med"}"""), I(-0.5, "med")));
    }

    [Theory]
    [InlineData(MarketSession.Session1, 1, true, true, true)]
    [InlineData(MarketSession.Closed, 1, true, false, true)]
    [InlineData(MarketSession.Session1, 0, true, false, false)]
    [InlineData(MarketSession.Session1, 1, false, false, false)]
    public void Job_only_checks_when_a_tab_is_open(MarketSession s, int connections, bool db, bool quotes, bool insights) =>
        Assert.Equal((quotes, insights), AlertJob.WhatToCheck(s, connections, db));

    [Fact]
    public void Reorder_puts_given_codes_first()
    {
        var items = new List<WatchlistItem>
        {
            new() { Code = "BBCA", Sort = 0 }, new() { Code = "BBRI", Sort = 1 }, new() { Code = "TLKM", Sort = 2 },
        };
        AlertsEndpoints.Reorder(items, ["tlkm", "BBCA"]);
        Assert.Equal(["TLKM", "BBCA", "BBRI"], items.OrderBy(i => i.Sort).Select(i => i.Code));
    }
}
