using Sumire.Api.Analytics.Core;

namespace Sumire.Api.Analytics.Services;

/// <summary>
/// Own sector indexes: daily returns of the members weighted by today's market cap (equal weight when no caps
/// are known), chained into a level starting at 1000.
/// </summary>
public static class SectorIndexBuilder
{
    public const int Days = 260;
    static readonly HashSet<string> NotSectors = ["Index", "Macro"];

    public static IReadOnlyList<SectorIndex> Build(IEnumerable<StockAnalytics> stocks, IReadOnlyList<Bar>? benchmark)
    {
        return stocks
            .Where(s => s.Instrument.Sector is { } sec && !NotSectors.Contains(sec) && !s.Instrument.IsIndex)
            .GroupBy(s => s.Instrument.Sector!)
            .Select(g => BuildOne(g.Key, g.ToList(), benchmark))
            .OfType<SectorIndex>()
            .OrderBy(s => s.Name)
            .ToList();
    }

    public static SectorIndex? BuildOne(string name, IReadOnlyList<StockAnalytics> members, IReadOnlyList<Bar>? benchmark)
    {
        if (members.Count == 0) return null;
        var (weights, weighting) = Weights(members);

        var closes = members.Select(m => m.Bars.ToDictionary(b => b.Date, b => b.AdjClose)).ToList();
        var dates = (benchmark is { Count: > 0 } ? benchmark.Select(b => b.Date) : members.SelectMany(m => m.Bars.Select(b => b.Date)))
            .Distinct().Order().TakeLast(Days + 1).ToList();
        if (dates.Count < 2) return null;

        var levels = new List<Bar>(dates.Count);
        var level = 1000.0;
        Add(dates[0], level);
        for (var t = 1; t < dates.Count; t++)
        {
            double sum = 0, wsum = 0;
            for (var i = 0; i < members.Count; i++)
            {
                if (closes[i].TryGetValue(dates[t], out var c) && closes[i].TryGetValue(dates[t - 1], out var p) && p > 0)
                {
                    sum += weights[i] * (double)(c / p - 1);
                    wsum += weights[i];
                }
            }
            if (wsum > 0) level *= 1 + sum / wsum;
            Add(dates[t], level);
        }

        var r = RiskStats.Returns(levels);
        var trail = benchmark is null ? [] : RelativeStrength.Rotation(levels, benchmark, tail: 5);
        var total = weights.Sum();
        var memberRows = members
            .Select((m, i) => new SectorMember(m.Code, m.Instrument.Name, m.Fundamental?.MarketCap, Math.Round(weights[i] / total, 4), m.Last,
                m.Technicals.ChangePct is { } c ? Math.Round(c, 2) : null))
            .OrderByDescending(m => m.Weight)
            .ToList();

        return new SectorIndex(name, weighting, levels,
            Pct(r.D1), Pct(r.W1), Pct(r.M1), Pct(r.M3),
            benchmark is null ? null : Pct(RelativeStrength.RelativeReturn(levels, benchmark, 21)),
            benchmark is null ? null : Pct(RelativeStrength.RelativeReturn(levels, benchmark, 63)),
            trail.Count > 0 ? trail[^1] : null, trail, memberRows);

        void Add(DateOnly d, double v)
        {
            var dv = (decimal)Math.Round(v, 4);
            levels.Add(new Bar(d, dv, dv, dv, dv, dv, 0));
        }
    }

    /// <summary>Market-cap weights; members without a cap get the median cap. Equal weights when nobody has one.</summary>
    static (double[] Weights, string Weighting) Weights(IReadOnlyList<StockAnalytics> members)
    {
        var caps = members.Select(m => m.Fundamental?.MarketCap is { } c && c > 0 ? (double)c : (double?)null).ToArray();
        var known = caps.OfType<double>().Order().ToList();
        if (known.Count == 0) return (Enumerable.Repeat(1.0, members.Count).ToArray(), "equal");
        var median = known[known.Count / 2];
        return (caps.Select(c => c ?? median).ToArray(), "marketCap");
    }

    static double? Pct(double? fraction) => fraction is { } f ? Math.Round(f * 100, 2) : null;
}
