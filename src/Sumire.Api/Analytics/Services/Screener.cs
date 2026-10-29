using System.Text.Json;

namespace Sumire.Api.Analytics.Services;

/// <summary>One condition, e.g. { "field": "rsi", "op": "lt", "value": 30 } or { "field": "sector", "op": "eq", "value": "Energy" }.</summary>
public sealed record ScreenFilter(string Field, string Op, JsonElement Value);

public sealed record ScreenRequest(string? Preset, IReadOnlyList<ScreenFilter>? Filters, string? Sort, bool? Desc, int? Limit);

public sealed record ScreenRow(
    string Code, string Name, string? Sector, decimal Last, double? ChgPct, double Score, string Label,
    double? Rsi, double? Pe, double? Pb, double? DivYield, double? VolRatio);

public sealed record ScreenResult(string? Preset, DateOnly? AsOf, int Count, IReadOnlyList<ScreenRow> Rows);

public sealed record ScreenPreset(string Name, string Description, IReadOnlyList<(string Field, string Op, double Value)> Filters, string Sort, bool Desc, int Limit = 100);

public sealed class ScreenerException(string message) : Exception(message);

/// <summary>Filters the cached universe snapshot; no I/O, so it answers in milliseconds.</summary>
public static class Screener
{
    public const int MaxLimit = 500;

    /// <summary>Numeric fields. Units: *Pct and ret* in percent; pe/pb plain; divYield/roe fractions; marketCap/valueTraded in Rp.</summary>
    public static readonly IReadOnlyDictionary<string, Func<StockAnalytics, double?>> NumericFields =
        new Dictionary<string, Func<StockAnalytics, double?>>(StringComparer.OrdinalIgnoreCase)
        {
            ["score"] = s => s.Score.Coverage > 0 ? s.Score.Total : null,
            ["last"] = s => (double)s.Last,
            ["chgPct"] = s => s.Technicals.ChangePct,
            ["rsi"] = s => s.Technicals.Rsi,
            ["adx"] = s => s.Technicals.Adx,
            ["stochK"] = s => s.Technicals.StochK,
            ["macdHist"] = s => s.Technicals.MacdHist,
            ["volRatio"] = s => s.Technicals.VolumeRatio,
            ["valueTraded"] = s => (double?)s.Technicals.AvgValueTraded20,
            ["atrPct"] = s => s.Technicals.AtrPct,
            ["hv20"] = s => s.Technicals.Hv20 * 100,
            ["distSma50"] = s => Dist(s, s.Technicals.Sma50),
            ["distSma200"] = s => Dist(s, s.Technicals.Sma200),
            ["pctFrom52wHigh"] = s => s.Technicals.PctFrom52wHigh,
            ["pctFrom52wLow"] = s => s.Technicals.PctFrom52wLow,
            ["goldenCrossDays"] = s => s.GoldenCrossDays,
            ["deathCrossDays"] = s => s.DeathCrossDays,
            ["ret1w"] = s => s.Risk.Returns.W1 * 100,
            ["ret1m"] = s => s.Risk.Returns.M1 * 100,
            ["ret3m"] = s => s.Risk.Returns.M3 * 100,
            ["ret1y"] = s => s.Risk.Returns.Y1 * 100,
            ["rel3m"] = s => s.Rel3M * 100,
            ["beta"] = s => s.Risk.Beta,
            ["volPercentile"] = s => s.Garch?.VolPercentile,
            ["pe"] = s => s.Fundamental?.Pe,
            ["pb"] = s => s.Fundamental?.Pb,
            ["roe"] = s => s.Fundamental?.Roe,
            ["der"] = s => s.Fundamental?.Der,
            ["divYield"] = s => s.Fundamental?.DivYield,
            ["marketCap"] = s => (double?)s.Fundamental?.MarketCap,
        };

    public static readonly IReadOnlyDictionary<string, Func<StockAnalytics, string?>> TextFields =
        new Dictionary<string, Func<StockAnalytics, string?>>(StringComparer.OrdinalIgnoreCase)
        {
            ["sector"] = s => s.Instrument.Sector,
            ["industry"] = s => s.Instrument.Industry,
            ["group"] = s => s.Instrument.GroupName,
            ["board"] = s => s.Instrument.Board,
            ["label"] = s => s.Score.Label,
            ["index"] = s => string.Join('|', s.Instrument.IndexMemberships),
        };

    public static readonly IReadOnlyDictionary<string, ScreenPreset> Presets = new ScreenPreset[]
    {
        new("oversold-uptrend", "RSI under 35 while still above the 200-day MA", [("rsi", "lt", 35), ("distSma200", "gt", 0)], "rsi", false),
        new("breakout-52w", "Within 1% of the 52-week high on 1.5x+ volume", [("pctFrom52wHigh", "gte", -1), ("volRatio", "gte", 1.5)], "volRatio", true),
        new("golden-cross", "50-day MA crossed above the 200-day in the last 10 sessions", [("goldenCrossDays", "lte", 10)], "goldenCrossDays", false),
        new("value", "PE 12 or less and PB 1.5 or less", [("pe", "gt", 0), ("pe", "lte", 12), ("pb", "lte", 1.5)], "pe", false),
        new("dividend", "Dividend yield of 4% or more", [("divYield", "gte", 0.04)], "divYield", true),
        new("volume-surge", "Volume at least twice the 20-day average", [("volRatio", "gte", 2)], "volRatio", true),
        new("top-score", "Highest signal scores", [("score", "gt", -1000)], "score", true, 30),
    }.ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);

    public static ScreenResult Run(Universe u, ScreenRequest req)
    {
        var conditions = new List<Func<StockAnalytics, bool>>();
        string sort = "score";
        var desc = true;
        var limit = 100;

        if (!string.IsNullOrWhiteSpace(req.Preset))
        {
            if (!Presets.TryGetValue(req.Preset, out var p)) throw new ScreenerException($"Unknown preset '{req.Preset}'");
            conditions.AddRange(p.Filters.Select(f => Numeric(f.Field, f.Op, f.Value)));
            (sort, desc, limit) = (p.Sort, p.Desc, p.Limit);
        }
        foreach (var f in req.Filters ?? []) conditions.Add(Compile(f));

        sort = req.Sort ?? sort;
        if (!NumericFields.TryGetValue(sort, out var sortKey)) throw new ScreenerException($"Unknown sort field '{sort}'");
        desc = req.Desc ?? desc;
        limit = Math.Clamp(req.Limit ?? limit, 1, MaxLimit);

        var matches = u.Stocks.Values.Where(s => conditions.All(c => c(s))).ToList();
        var ordered = desc
            ? matches.OrderBy(s => sortKey(s) is null).ThenByDescending(s => sortKey(s))
            : matches.OrderBy(s => sortKey(s) is null).ThenBy(s => sortKey(s));
        var rows = ordered.ThenBy(s => s.Code).Take(limit).Select(Row).ToList();
        return new ScreenResult(req.Preset, u.AsOf, matches.Count, rows);
    }

    public static ScreenRow Row(StockAnalytics s) => new(
        s.Code, s.Instrument.Name, s.Instrument.Sector, s.Last,
        s.Technicals.ChangePct is { } c ? Math.Round(c, 2) : null,
        s.Score.Total, s.Score.Label,
        Round(s.Technicals.Rsi, 1), Round(s.Fundamental?.Pe, 2), Round(s.Fundamental?.Pb, 2),
        Round(s.Fundamental?.DivYield, 4), Round(s.Technicals.VolumeRatio, 2));

    public static Func<StockAnalytics, bool> Compile(ScreenFilter f)
    {
        if (f is null || string.IsNullOrWhiteSpace(f.Field) || string.IsNullOrWhiteSpace(f.Op))
            throw new ScreenerException("Each filter needs 'field', 'op' and 'value'");
        if (TextFields.TryGetValue(f.Field, out var text))
        {
            if (f.Value.ValueKind != JsonValueKind.String) throw new ScreenerException($"'{f.Field}' needs a string value");
            var v = f.Value.GetString()!;
            return f.Op.ToLowerInvariant() switch
            {
                "eq" => s => string.Equals(text(s), v, StringComparison.OrdinalIgnoreCase),
                "neq" => s => !string.Equals(text(s), v, StringComparison.OrdinalIgnoreCase),
                "contains" => s => text(s)?.Contains(v, StringComparison.OrdinalIgnoreCase) == true,
                _ => throw new ScreenerException($"Operator '{f.Op}' is not valid for '{f.Field}' (use eq, neq, contains)"),
            };
        }
        if (f.Value.ValueKind != JsonValueKind.Number) throw new ScreenerException($"'{f.Field}' needs a number value");
        return Numeric(f.Field, f.Op, f.Value.GetDouble());
    }

    static Func<StockAnalytics, bool> Numeric(string field, string op, double value)
    {
        if (!NumericFields.TryGetValue(field, out var get)) throw new ScreenerException($"Unknown field '{field}'");
        Func<double, bool> test = op.ToLowerInvariant() switch
        {
            "gt" => x => x > value,
            "gte" => x => x >= value,
            "lt" => x => x < value,
            "lte" => x => x <= value,
            "eq" => x => Math.Abs(x - value) < 1e-9,
            "neq" => x => Math.Abs(x - value) >= 1e-9,
            _ => throw new ScreenerException($"Unknown operator '{op}' (use gt, gte, lt, lte, eq, neq)"),
        };
        // Missing data never matches.
        return s => get(s) is { } x && double.IsFinite(x) && test(x);
    }

    static double? Dist(StockAnalytics s, double? ma) => ma is > 0 ? ((double)s.Last / ma.Value - 1) * 100 : null;

    static double? Round(double? v, int digits) => v is { } x && double.IsFinite(x) ? Math.Round(x, digits) : null;
}
