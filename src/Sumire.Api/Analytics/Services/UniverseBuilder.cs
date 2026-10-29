using System.Collections.Concurrent;
using Sumire.Api.Analytics.Core;
using Sumire.Api.Data;
using Sumire.Api.Forecasting;

namespace Sumire.Api.Analytics.Services;

/// <summary>Pure computation of the daily analytics snapshot from bars and stored data.</summary>
public static class UniverseBuilder
{
    /// <summary>Horizon whose forecast feeds the signal score.</summary>
    public const int ScoreHorizon = 20;
    /// <summary>Matured forecasts a model needs before it can replace the baseline in the score.</summary>
    public const int MinTrackRecord = 30;

    public static Universe Build(UniverseInput input)
    {
        var ihsgBars = input.IhsgInstrument is { } ih ? input.Bars.GetValueOrDefault(ih.Code) : null;
        var medians = Medians(input.Stocks, input.Fundamentals);

        var stocks = new ConcurrentDictionary<string, StockAnalytics>();
        Parallel.ForEach(input.Stocks, inst =>
        {
            if (input.Bars.GetValueOrDefault(inst.Code) is not { Count: > 0 } bars) return;
            var a = Analyze(inst, bars, ihsgBars, input.Fundamentals.GetValueOrDefault(inst.Code),
                inst.Sector is { } s ? medians.GetValueOrDefault(s) : null,
                input.Metrics, input.ModelForecasts.GetValueOrDefault(inst.Code), input.News.GetValueOrDefault(inst.Code), input.Weights);
            stocks[inst.Code] = a;
        });

        StockAnalytics? Index(Instrument? inst) =>
            inst is not null && input.Bars.GetValueOrDefault(inst.Code) is { Count: > 0 } b
                ? Analyze(inst, b, ihsgBars, null, null, input.Metrics, input.ModelForecasts.GetValueOrDefault(inst.Code), null, input.Weights)
                : null;

        var ihsg = Index(input.IhsgInstrument);
        var breadth = BreadthOf(stocks.Values);
        return new Universe
        {
            ExpectedDate = input.ExpectedDate,
            AsOf = ihsg?.AsOf ?? (stocks.IsEmpty ? null : stocks.Values.Max(s => s.AsOf)),
            BuiltAt = input.Now,
            Source = input.Source,
            Stocks = new Dictionary<string, StockAnalytics>(stocks),
            Ihsg = ihsg,
            Lq45 = Index(input.Lq45Instrument),
            Breadth = breadth,
            Regime = MarketRegime.Classify(ihsg?.Technicals, breadth, ihsg?.Garch?.VolPercentile),
            Sectors = SectorIndexBuilder.Build(stocks.Values, ihsgBars),
            SectorMedians = medians,
            Metrics = input.Metrics,
        };
    }

    public static StockAnalytics Analyze(Instrument inst, IReadOnlyList<Bar> bars, IReadOnlyList<Bar>? benchmark,
        Fundamental? fundamental, SectorMedians? medians, IReadOnlyList<ForecastMetric> metrics,
        IReadOnlyList<Forecast>? modelForecasts, IReadOnlyList<NewsInputs>? news, SignalWeights weights)
    {
        var t = TechnicalsCalculator.Snapshot(bars)!;
        var garch = Garch.Fit(bars);
        var baseline = BaselineForecaster.Forecast(bars, garch);
        var a = new StockAnalytics
        {
            Instrument = inst,
            Bars = bars,
            Technicals = t,
            Risk = RiskStats.Compute(bars, benchmark),
            Garch = garch,
            Baseline = baseline,
            Fundamental = fundamental,
            Rel3M = benchmark is null ? null : RelativeStrength.RelativeReturn(bars, benchmark, 63),
            Rel6M = benchmark is null ? null : RelativeStrength.RelativeReturn(bars, benchmark, 126),
            GoldenCrossDays = DaysSince(bars, t.LastGoldenCross?.Date),
            DeathCrossDays = DaysSince(bars, t.LastDeathCross?.Date),
            Score = new SignalResult(0, SignalScorer.Label(0), [], 0),
        };
        return a with { Score = ScoreOf(a, medians, metrics, modelForecasts, news, weights) };
    }

    /// <summary>Recomputes signal scores only (e.g. after forecast metrics or insights changed).</summary>
    public static Universe Rescore(Universe u, IReadOnlyList<ForecastMetric> metrics,
        IReadOnlyDictionary<string, List<Forecast>> modelForecasts, IReadOnlyDictionary<string, List<NewsInputs>> news,
        SignalWeights? weights = null)
    {
        var w = weights ?? SignalWeights.Default;
        var stocks = u.Stocks.Values.ToDictionary(s => s.Code, s => s with
        {
            Score = ScoreOf(s, s.Instrument.Sector is { } sec ? u.SectorMedians.GetValueOrDefault(sec) : null,
                metrics, modelForecasts.GetValueOrDefault(s.Code), news.GetValueOrDefault(s.Code), w),
        });
        return u with { Stocks = stocks, Metrics = metrics };
    }

    public static SignalResult ScoreOf(StockAnalytics a, SectorMedians? medians, IReadOnlyList<ForecastMetric> metrics,
        IReadOnlyList<Forecast>? modelForecasts, IReadOnlyList<NewsInputs>? news, SignalWeights weights)
    {
        var f = a.Fundamental;
        return SignalScorer.Score(new SignalInputs
        {
            Technicals = a.Technicals,
            RelReturn3M = a.Rel3M,
            RelReturn6M = a.Rel6M,
            VolPercentile = a.Garch?.VolPercentile,
            Valuation = f is null ? null : new ValuationInputs(f.Pe, medians?.Pe, f.Pb, medians?.Pb, f.DivYield),
            Forecast = ChooseForecast(a, metrics, modelForecasts),
            News = news,
        }, weights);
    }

    /// <summary>
    /// The forecast for the score: a model's 20-day forecast when its track record (scope "all") beats the
    /// baseline on direction without a worse MAPE, otherwise the baseline. Hit rate scales its weight.
    /// </summary>
    public static ForecastInputs? ChooseForecast(StockAnalytics a, IReadOnlyList<ForecastMetric> metrics, IReadOnlyList<Forecast>? modelForecasts)
    {
        var baseMetric = metrics.FirstOrDefault(m => m.Model == BaselineForecaster.ModelName && m.Horizon == ScoreHorizon && m.Scope == "all");
        var last = a.Last;
        if (last <= 0) return null;

        var best = (modelForecasts ?? [])
            .Where(f => f.Horizon == ScoreHorizon && f.Model != BaselineForecaster.ModelName)
            .Select(f => (f, m: metrics.FirstOrDefault(m => m.Model == f.Model && m.Horizon == ScoreHorizon && m.Scope == "all")))
            .Where(x => x.m is not null && BeatsBaseline(x.m, baseMetric))
            .OrderByDescending(x => x.m!.DirAccuracy)
            .FirstOrDefault();
        if (best.f is { } bf)
            return new ForecastInputs(bf.Model, ScoreHorizon, bf.ProbUp, (double)(bf.P50 / last - 1), best.m!.DirAccuracy);

        var h = a.Baseline?.Horizons.FirstOrDefault(x => x.Horizon == ScoreHorizon);
        return h is null ? null
            : new ForecastInputs(BaselineForecaster.ModelName, ScoreHorizon, h.ProbUp, (double)(h.P50 / last - 1), baseMetric?.DirAccuracy);
    }

    public static bool BeatsBaseline(ForecastMetric model, ForecastMetric? baseline) =>
        model.N >= MinTrackRecord && (baseline is null
            ? model.DirAccuracy > 0.5
            : model.DirAccuracy > baseline.DirAccuracy && model.Mape <= baseline.Mape);

    public static Breadth? BreadthOf(IEnumerable<StockAnalytics> stocks)
    {
        var list = stocks.Where(s => s.Bars.Count >= 2).ToList();
        if (list.Count == 0) return null;
        var date = list.Max(s => s.AsOf);
        var today = list.Where(s => s.AsOf == date).ToList();

        int adv = 0, dec = 0, unch = 0, highs = 0, lows = 0;
        foreach (var s in today)
        {
            var c = s.Bars[^1].Close.CompareTo(s.Bars[^2].Close);
            if (c > 0) adv++; else if (c < 0) dec++; else unch++;

            if (s.Bars.Count < 200) continue;
            var last = s.Bars[^1].AdjClose;
            decimal hi = decimal.MinValue, lo = decimal.MaxValue;
            for (var i = SeriesMath.YearAgoIndex(s.Bars) + 1; i < s.Bars.Count - 1; i++)
            {
                hi = Math.Max(hi, s.Bars[i].AdjClose);
                lo = Math.Min(lo, s.Bars[i].AdjClose);
            }
            if (last > hi) highs++;
            if (last < lo) lows++;
        }

        static double Pct(IEnumerable<StockAnalytics> xs, Func<TechnicalsSnapshot, double?> ma)
        {
            var with = xs.Where(x => ma(x.Technicals) is not null).ToList();
            return with.Count == 0 ? 0 : Math.Round(100.0 * with.Count(x => (double)x.Technicals.Close > ma(x.Technicals)) / with.Count, 1);
        }

        return new Breadth(date, today.Count, adv, dec, unch, Pct(today, t => t.Sma50), Pct(today, t => t.Sma200), highs, lows);
    }

    public static Dictionary<string, SectorMedians> Medians(IEnumerable<Instrument> stocks, IReadOnlyDictionary<string, Fundamental> fundamentals) =>
        stocks
            .Where(s => s.Sector is not null && fundamentals.ContainsKey(s.Code))
            .GroupBy(s => s.Sector!)
            .ToDictionary(g => g.Key, g =>
            {
                var f = g.Select(s => fundamentals[s.Code]).ToList();
                return new SectorMedians(
                    Median(f.Select(x => x.Pe).Where(v => v > 0)),
                    Median(f.Select(x => x.Pb).Where(v => v > 0)),
                    Median(f.Select(x => x.Roe)),
                    Median(f.Select(x => x.DivYield)),
                    f.Count);
            });

    public static double? Median(IEnumerable<double?> values)
    {
        var v = values.OfType<double>().Where(double.IsFinite).Order().ToList();
        if (v.Count == 0) return null;
        return v.Count % 2 == 1 ? v[v.Count / 2] : (v[v.Count / 2 - 1] + v[v.Count / 2]) / 2;
    }

    static int? DaysSince(IReadOnlyList<Bar> bars, DateOnly? date)
    {
        if (date is not { } d) return null;
        for (var i = bars.Count - 1; i >= 0; i--)
            if (bars[i].Date == d) return bars.Count - 1 - i;
        return null;
    }
}
