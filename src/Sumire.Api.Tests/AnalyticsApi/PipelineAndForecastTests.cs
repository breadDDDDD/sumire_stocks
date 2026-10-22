using System.Text.Json;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sumire.Api.Analytics;
using Sumire.Api.Analytics.Services;
using Sumire.Api.Data;
using Sumire.Api.Forecasting;
using Sumire.Api.Market;
using Sumire.Api.Tests.Analytics;

namespace Sumire.Api.Tests.AnalyticsApi;

public class PipelineAndForecastTests
{
    static string Fixture(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "analytics", name));

    [Fact]
    public void Eod_steps_are_registered_in_pipeline_order()
    {
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder().Build();
        services.AddAnalytics(config).AddForecasting(config);

        var types = services.Where(d => d.ServiceType == typeof(IEodPipelineStep)).Select(d => d.ImplementationType).ToList();
        Assert.Equal(4, types.Count);

        // Dependencies are not needed to read Order/Name.
        IEodPipelineStep[] steps =
        [
            new SignalScoreStep(null!, null!, null!),
            new ForecastEvaluationStep(null!, null!),
            new AnalyticsSnapshotStep(null!),
            new BaselineForecastStep(null!, null!),
        ];
        Assert.All(steps, s => Assert.Contains(s.GetType(), types));
        Assert.Equal(["analytics snapshot", "baseline forecasts", "forecast evaluation", "signal scores"],
            steps.OrderBy(s => s.Order).Select(s => s.Name));
    }

    [Fact]
    public void Parses_quote_summary_ratios()
    {
        var json = YahooParser.ParseQuoteSummary(Fixture("quotesummary-bbca.json"));
        var f = FundamentalsService.Parse("BBCA", json, DateTimeOffset.UnixEpoch);

        Assert.Equal(635706095435776m, f.MarketCap);
        Assert.Equal(10.963983, f.Pe!.Value, 6);
        Assert.Equal(2.3506572, f.Pb!.Value, 6);
        Assert.Equal(0.21818, f.Roe!.Value, 6);
        Assert.Equal(0.0722, f.DivYield!.Value, 6);
        Assert.Equal(472m, f.Eps);
        Assert.Null(f.Der); // banks have no debtToEquity on Yahoo
        Assert.Equal(0.53118, f.Npm!.Value, 6);
    }

    [Fact]
    public void Parses_statements_keeping_non_zero_items()
    {
        var s = FundamentalsService.ParseStatements(YahooParser.ParseQuoteSummary(Fixture("quotesummary-bbca-statements.json")));
        Assert.Equal(4, s.IncomeAnnual.Count);
        Assert.Equal("2025-12-31", s.IncomeAnnual[0].EndDate);
        Assert.Equal(111861388000000, s.IncomeAnnual[0].Items["totalRevenue"]);
        Assert.DoesNotContain("costOfRevenue", s.IncomeAnnual[0].Items.Keys);
        Assert.Equal(4, s.IncomeQuarterly.Count);
        Assert.Equal(4, s.Cashflow.Count);
    }

    [Fact]
    public void Backtest_makes_weekly_forecasts_from_past_bars_only()
    {
        var u = TestUniverse.Build(new() { ["AAAA"] = ("Energy", SyntheticBars.RandomWalk(400, seed: 5)) });
        var s = u.Stocks["AAAA"];
        var rows = ForecastService.Backtest(s, weeks: 10);

        var dates = rows.Select(r => r.AsOf).Distinct().Order().ToList();
        Assert.Equal(10, dates.Count);
        Assert.All(rows, r => Assert.Equal("baseline", r.Model));
        Assert.Equal(s.Bars[^6].Date, dates[^1]);
        Assert.Equal(5, s.Bars.ToList().FindIndex(b => b.Date == dates[^1]) - s.Bars.ToList().FindIndex(b => b.Date == dates[^2]));
        var first = rows.First(r => r.AsOf == dates[0] && r.Horizon == 5);
        var expected = BaselineForecaster.Forecast(s.Bars.Where(b => b.Date <= dates[0]).ToList())!.Horizons[0];
        Assert.Equal(expected.P50, first.P50);
    }

    [Fact]
    public void Metrics_split_by_scope()
    {
        var d = new DateOnly(2026, 1, 5);
        (Forecast, ForecastOutcome) M(string code, string model, decimal actual) =>
            (new Forecast { Code = code, AsOf = d, Horizon = 5, Model = model, P10 = 90, P50 = 105, P90 = 110, ProbUp = 0.7 },
             new ForecastOutcome(100, 90, 105, 110, 0.7, actual));

        var metrics = ForecastService.Metrics(
            [M("AAAA", "baseline", 104), M("BBBB", "baseline", 95), M("IHSG", "baseline", 120), M("AAAA", "chronos-2", 104)],
            code => code == "AAAA" ? "Energy" : "Financials", DateTimeOffset.UnixEpoch);

        var all = metrics.Single(m => m.Scope == "all" && m.Model == "baseline");
        Assert.Equal(2, all.N);
        Assert.Equal(0.5, all.DirAccuracy);
        var ihsg = metrics.Single(m => m.Scope == "IHSG");
        Assert.Equal((1, 1.0, 0.0), (ihsg.N, ihsg.DirAccuracy, ihsg.Coverage80));
        Assert.Contains(metrics, m => m.Scope == "Energy" && m.Model == "chronos-2");
        Assert.DoesNotContain(metrics, m => m.Scope == "Financials" && m.Model == "chronos-2");
    }

    [Fact]
    public void Baseline_rows_and_signal_rows_map_cleanly()
    {
        var u = TestUniverse.Build(new() { ["AAAA"] = ("Energy", SyntheticBars.RandomWalk(300, seed: 6)) });
        var s = u.Stocks["AAAA"];
        var rows = ForecastService.Rows(s.Code, s.Baseline!).ToList();
        Assert.Equal([5, 20, 60], rows.Select(r => r.Horizon));

        var score = SignalScoreStep.ToRow(s);
        Assert.Equal(s.Score.Total, score.Total);
        var reasons = JsonSerializer.Deserialize<Dictionary<string, string>>(score.Reasons!)!;
        Assert.Contains("trend", reasons.Keys);
        Assert.Contains("news", reasons.Keys);
    }

    [Fact]
    public void Responses_serialize_without_nan_even_for_degenerate_series()
    {
        var options = new JsonOptions().SerializerOptions;
        options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        var u = TestUniverse.Build(new()
        {
            ["FLAT"] = ("Energy", TestUniverse.Flat(300)),
            ["TINY"] = ("Energy", SyntheticBars.Linear(3)),
        }, TestUniverse.Flat(300));

        foreach (var s in u.Stocks.Values)
        {
            JsonSerializer.Serialize(s.Technicals, options);
            JsonSerializer.Serialize(ForecastService.TrackRecord(s, []), options);
            JsonSerializer.Serialize(ScoreDto.From(s.Score), options);
            JsonSerializer.Serialize(AnalyticsEndpoints.Series(s, string.Join(',', AnalyticsEndpoints.SeriesNames)), options);
        }
        JsonSerializer.Serialize(u.Sectors, options);
        JsonSerializer.Serialize(u.Breadth, options);
    }
}
