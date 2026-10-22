using System.Globalization;
using System.Text.Json;
using Sumire.Api.Analytics.Core;
using Sumire.Api.Common;
using Sumire.Api.Data;
using Sumire.Api.Market;

namespace Sumire.Api.Alerts;

/// <summary>
/// Alert types, their parameters and evaluation. Quote alerts are checked on live quotes during sessions,
/// bar alerts after the EOD bars land, news alerts against stored insights. Each alert fires at most once per WIB day.
/// </summary>
public static class AlertRules
{
    public const string PriceAbove = "price_above";       // { "price": 9000 }
    public const string PriceBelow = "price_below";       // { "price": 8500 }
    public const string PctChange = "pct_change";         // { "pct": 3 }  absolute % move vs previous close
    public const string MaCross = "ma_cross";             // { "fast": "price"|"sma20"|"sma50", "slow": "sma50"|"sma200", "direction": "above"|"below"|"any" }
    public const string RsiAbove = "rsi_above";           // { "value": 70 }
    public const string RsiBelow = "rsi_below";           // { "value": 30 }
    public const string New52wHigh = "new_52w_high";      // {}
    public const string NegativeInsight = "negative_insight"; // { "minConfidence": "high"|"med", "maxSentiment": -0.3 }; code null = every watchlist stock

    public static readonly string[] QuoteTypes = [PriceAbove, PriceBelow, PctChange];
    public static readonly string[] BarTypes = [MaCross, RsiAbove, RsiBelow, New52wHigh];
    public static readonly string[] AllTypes = [.. QuoteTypes, .. BarTypes, NegativeInsight];

    static readonly string[] MaNames = ["price", "sma20", "sma50", "sma200"];

    /// <summary>Returns an error message, or null when the alert is valid. Normalizes Params to compact JSON.</summary>
    public static string? Validate(Alert alert)
    {
        if (!AllTypes.Contains(alert.Type)) return $"Unknown type '{alert.Type}'. Use one of: {string.Join(", ", AllTypes)}.";
        if (alert.Code is null && alert.Type != NegativeInsight) return "code is required for this alert type.";

        JsonElement p;
        try { p = JsonDocument.Parse(string.IsNullOrWhiteSpace(alert.Params) ? "{}" : alert.Params).RootElement; }
        catch (JsonException) { return "params must be a JSON object."; }
        if (p.ValueKind != JsonValueKind.Object) return "params must be a JSON object.";

        switch (alert.Type)
        {
            case PriceAbove or PriceBelow when Num(p, "price") is not > 0:
                return "params.price must be a positive number.";
            case PctChange when Num(p, "pct") is not > 0:
                return "params.pct must be a positive number (percent).";
            case RsiAbove or RsiBelow when Num(p, "value") is not (> 0 and < 100):
                return "params.value must be between 0 and 100.";
            case MaCross:
                var fast = Str(p, "fast") ?? "price";
                var slow = Str(p, "slow") ?? "sma200";
                if (!MaNames.Contains(fast) || !MaNames.Contains(slow) || slow == "price" || fast == slow || Array.IndexOf(MaNames, fast) >= Array.IndexOf(MaNames, slow))
                    return "params.fast/slow must be price, sma20, sma50 or sma200 with fast shorter than slow.";
                if (Str(p, "direction") is { } d && d is not ("above" or "below" or "any"))
                    return "params.direction must be above, below or any.";
                break;
        }
        alert.Params = p.GetRawText();
        return null;
    }

    /// <summary>True unless the alert already fired today (WIB).</summary>
    public static bool CanFire(Alert alert, DateTimeOffset now) =>
        alert.Active && (alert.LastTriggeredAt is not { } last || Wib.ToWib(last).Date != Wib.ToWib(now).Date);

    /// <summary>Message when a quote alert's condition holds, else null.</summary>
    public static string? EvaluateQuote(Alert alert, Quote quote)
    {
        if (quote.Price is not { } price) return null;
        var p = Params(alert);
        switch (alert.Type)
        {
            case PriceAbove when Num(p, "price") is { } level && price >= (decimal)level:
                return $"Violet alert: {quote.Code} at {Fmt(price)}, above {Fmt((decimal)level)}";
            case PriceBelow when Num(p, "price") is { } level && price <= (decimal)level:
                return $"Violet alert: {quote.Code} at {Fmt(price)}, below {Fmt((decimal)level)}";
            case PctChange when Num(p, "pct") is { } pct && quote.ChangePct is { } chg && Math.Abs((double)chg) >= pct:
                return $"Violet alert: {quote.Code} {(chg >= 0 ? "up" : "down")} {Math.Abs(chg).ToString("0.##", CultureInfo.InvariantCulture)}% today at {Fmt(price)}";
            default:
                return null;
        }
    }

    /// <summary>Message when a bar alert's condition holds on the latest bar (oldest-first list), else null.</summary>
    public static string? EvaluateBars(Alert alert, IReadOnlyList<Bar> bars)
    {
        if (bars.Count < 2) return null;
        var code = alert.Code;
        var p = Params(alert);
        switch (alert.Type)
        {
            case RsiAbove or RsiBelow:
            {
                if (TechnicalsCalculator.Snapshot(bars)?.Rsi is not { } rsi || Num(p, "value") is not { } level) return null;
                var hit = alert.Type == RsiAbove ? rsi >= level : rsi <= level;
                return hit ? $"Violet alert: {code} RSI {rsi.ToString("0.0", CultureInfo.InvariantCulture)} {(alert.Type == RsiAbove ? "above" : "below")} {level.ToString("0.#", CultureInfo.InvariantCulture)}" : null;
            }
            case New52wHigh:
            {
                var last = bars[^1];
                var prior = bars.Skip(Math.Max(0, bars.Count - 253)).Take(Math.Min(252, bars.Count - 1)).ToList();
                if (prior.Count < 20) return null;
                var prevHigh = prior.Max(b => b.High);
                return last.High > prevHigh ? $"Violet alert: {code} hit a new 52-week high at {Fmt(last.High)}" : null;
            }
            case MaCross:
            {
                var fast = Str(p, "fast") ?? "price";
                var slow = Str(p, "slow") ?? "sma200";
                var direction = Str(p, "direction") ?? "any";
                var series = TechnicalsCalculator.Series(bars);
                double? F(int i) => Line(series, bars, fast, i);
                double? S(int i) => Line(series, bars, slow, i);
                int n = bars.Count - 1;
                if (F(n) is not { } f1 || S(n) is not { } s1 || F(n - 1) is not { } f0 || S(n - 1) is not { } s0) return null;
                var crossedUp = f0 <= s0 && f1 > s1;
                var crossedDown = f0 >= s0 && f1 < s1;
                if (!(crossedUp && direction is "above" or "any") && !(crossedDown && direction is "below" or "any")) return null;
                return $"Violet alert: {code} {Label(fast)} crossed {(crossedUp ? "above" : "below")} {Label(slow)}";
            }
            default:
                return null;
        }
    }

    /// <summary>Message when the insight is negative enough for a negative_insight alert, else null.</summary>
    public static string? EvaluateInsight(Alert alert, Insight insight)
    {
        var p = Params(alert);
        var minConfidence = Str(p, "minConfidence") ?? "high";
        var maxSentiment = Num(p, "maxSentiment") ?? -0.3;
        if (insight.Sentiment > maxSentiment || Rank(insight.Confidence) < Rank(minConfidence)) return null;
        return $"Violet alert: negative {insight.Confidence}-confidence insight on {insight.Subject} (sentiment {insight.Sentiment.ToString("0.00", CultureInfo.InvariantCulture)})";
    }

    static int Rank(string confidence) => confidence switch { "high" => 3, "med" or "medium" => 2, "low" => 1, _ => 0 };

    static double? Line(TechnicalSeries s, IReadOnlyList<Bar> bars, string name, int i) => name switch
    {
        "price" => (double)bars[i].AdjClose,
        "sma20" => At(s.Sma20, i),
        "sma50" => At(s.Sma50, i),
        "sma200" => At(s.Sma200, i),
        _ => null,
    };

    static double? At(IReadOnlyList<double?> values, int i) => i >= 0 && i < values.Count ? values[i] : null;

    static string Label(string name) => name == "price" ? "price" : name.ToUpperInvariant().Replace("SMA", "MA");

    static JsonElement Params(Alert alert)
    {
        try { return JsonDocument.Parse(string.IsNullOrWhiteSpace(alert.Params) ? "{}" : alert.Params).RootElement; }
        catch (JsonException) { return JsonDocument.Parse("{}").RootElement; }
    }

    static double? Num(JsonElement p, string name) =>
        p.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;

    static string? Str(JsonElement p, string name) =>
        p.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    static string Fmt(decimal d) => d.ToString(d >= 1000 ? "#,0" : "#,0.##", CultureInfo.InvariantCulture);
}
