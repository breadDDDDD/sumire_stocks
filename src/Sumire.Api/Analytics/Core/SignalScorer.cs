using System.Globalization;

namespace Sumire.Api.Analytics.Core;

/// <summary>Component weights (any scale; they are renormalized over the components that have data).</summary>
public record SignalWeights(
    double Trend = 25, double Momentum = 20, double Volume = 10, double Volatility = 10,
    double Valuation = 15, double Forecast = 10, double News = 10)
{
    public static readonly SignalWeights Default = new();
}

public record ValuationInputs(double? Pe, double? SectorMedianPe, double? Pb, double? SectorMedianPb, double? DividendYield);

/// <summary>Forecast to score: the model that beats the baseline, else the baseline itself.</summary>
/// <param name="ProbUp">P(close above today's at the horizon), 0..1.</param>
/// <param name="P50Return">Median expected return as a fraction.</param>
/// <param name="HitRate">Track-record direction hit rate (0..1) if known; scales confidence.</param>
public record ForecastInputs(string Model, int Horizon, double ProbUp, double P50Return, double? HitRate = null);

/// <param name="Sentiment">-1..1.</param>
/// <param name="Confidence">0..1.</param>
public record NewsInputs(double Sentiment, double Confidence, double AgeDays);

public record SignalInputs
{
    public TechnicalsSnapshot? Technicals { get; init; }
    /// <summary>3- and 6-month return minus the index's, as fractions.</summary>
    public double? RelReturn3M { get; init; }
    public double? RelReturn6M { get; init; }
    /// <summary>GARCH volatility percentile, 0..100.</summary>
    public double? VolPercentile { get; init; }
    public ValuationInputs? Valuation { get; init; }
    public ForecastInputs? Forecast { get; init; }
    public IReadOnlyList<NewsInputs>? News { get; init; }
}

public enum SignalComponent { Trend, Momentum, Volume, Volatility, Valuation, Forecast, News }

/// <summary>Score is -100..100, or null when the component had no data (then it is skipped).</summary>
public record ComponentScore(SignalComponent Component, double? Score, double Weight, string Reason);

/// <param name="Coverage">Share of the total weight that had data, 0..1.</param>
public record SignalResult(double Total, string Label, IReadOnlyList<ComponentScore> Components, double Coverage);

/// <summary>Explainable signal score (plan §5.6).</summary>
public static class SignalScorer
{
    public const double NewsDecayDays = 5;

    public static SignalResult Score(SignalInputs input, SignalWeights? weights = null)
    {
        var w = weights ?? SignalWeights.Default;
        var t = input.Technicals;
        return Combine([
            Trend(t, w.Trend),
            Momentum(t, input.RelReturn3M, input.RelReturn6M, w.Momentum),
            Volume(t, w.Volume),
            Volatility(input.VolPercentile, w.Volatility),
            Valuation(input.Valuation, w.Valuation),
            Forecast(input.Forecast, w.Forecast),
            News(input.News, w.News),
        ]);
    }

    /// <summary>Weighted mean of the components that have a score; weights renormalized.</summary>
    public static SignalResult Combine(IReadOnlyList<ComponentScore> components)
    {
        double sum = 0, used = 0, all = 0;
        foreach (var c in components)
        {
            all += c.Weight;
            if (c.Score is not { } s || c.Weight <= 0) continue;
            sum += s * c.Weight;
            used += c.Weight;
        }
        var total = used > 0 ? Math.Round(sum / used, 1) : 0;
        return new SignalResult(total, Label(total), components, all > 0 ? used / all : 0);
    }

    public static string Label(double total) => total switch
    {
        >= 60 => "Strongly Bullish",
        >= 20 => "Bullish",
        > -20 => "Neutral",
        > -60 => "Bearish",
        _ => "Strongly Bearish",
    };

    public static ComponentScore Trend(TechnicalsSnapshot? t, double weight)
    {
        const SignalComponent c = SignalComponent.Trend;
        if (t?.Sma50 is null) return Missing(c, weight, "Not enough history for moving averages");
        var close = (double)t.Close;

        double pos = 0, posW = 0;
        void Pos(double? ma, double w) { if (ma is { } m) { pos += Math.Sign(close - m) * w; posW += w; } }
        Pos(t.Sma20, 0.2);
        Pos(t.Sma50, 0.3);
        Pos(t.Sma200, 0.5);
        pos /= posW;

        var slopes = new List<double>();
        if (t.Sma50Slope is { } s50) slopes.Add(SeriesMath.Clamp(s50 / 3));
        if (t.Sma200Slope is { } s200) slopes.Add(SeriesMath.Clamp(s200 / 2));
        var slope = slopes.Count > 0 ? slopes.Average() : 0;

        var raw = 0.7 * pos + 0.3 * slope;
        // ADX says how much to trust the direction: weak trends are damped, strong ones amplified a bit.
        var strength = t.Adx is { } adx ? Math.Clamp(0.4 + adx / 40, 0.5, 1.2) : 1;
        var score = Pct(raw * strength);

        var anchor = t.Sma200 is { } m200 ? (close >= m200 ? "Above 200-day MA" : "Below 200-day MA")
            : close >= t.Sma50 ? "Above 50-day MA" : "Below 50-day MA";
        var second = t.Sma200 is not null ? (close >= t.Sma50 ? ", above 50-day" : ", below 50-day") : "";
        var adxText = t.Adx is { } a
            ? $", trend {(a >= 25 ? "strong" : a >= 20 ? "moderate" : "weak")} (ADX {F0(a)})"
            : "";
        return new ComponentScore(c, score, weight, anchor + second + adxText);
    }

    public static ComponentScore Momentum(TechnicalsSnapshot? t, double? rel3M, double? rel6M, double weight)
    {
        const SignalComponent c = SignalComponent.Momentum;
        var parts = new List<double>();
        var reasons = new List<string>();
        if (t?.Rsi is { } rsi)
        {
            parts.Add(SeriesMath.Clamp((rsi - 50) / 25));
            reasons.Add($"RSI {F0(rsi)}{(rsi >= 70 ? " (overbought)" : rsi <= 30 ? " (oversold)" : "")}");
        }
        if (t?.MacdHist is { } h && t.Close > 0)
        {
            parts.Add(SeriesMath.Clamp(h / (double)t.Close * 100));
            reasons.Add($"MACD histogram {(h >= 0 ? "positive" : "negative")}");
        }
        var rel = Avg(rel3M, rel6M);
        if (rel is { } r)
        {
            parts.Add(SeriesMath.Clamp(r / 0.15));
            var (v, span) = rel3M is { } r3 ? (r3, "3M") : (rel6M!.Value, "6M");
            reasons.Add($"{Signed(v * 100)}% vs IHSG over {span}");
        }
        return parts.Count == 0
            ? Missing(c, weight, "No momentum data")
            : new ComponentScore(c, Pct(parts.Average()), weight, string.Join(", ", reasons));
    }

    public static ComponentScore Volume(TechnicalsSnapshot? t, double weight)
    {
        const SignalComponent c = SignalComponent.Volume;
        if (t?.ObvSlope is not { } obv) return Missing(c, weight, "Not enough volume history");
        var raw = 0.6 * SeriesMath.Clamp(obv / 0.5);
        var reason = obv > 0.05 ? "OBV rising (accumulation)" : obv < -0.05 ? "OBV falling (distribution)" : "OBV flat";
        if (t.VolumeRatio is { } vr && vr >= 1.5 && t.Change is { } ch && ch != 0)
        {
            raw += 0.4 * Math.Sign(ch) * Math.Min((vr - 1) / 1.5, 1);
            reason += Inv($", volume {vr:0.0}x average on an {(ch > 0 ? "up" : "down")} day");
        }
        return new ComponentScore(c, Pct(raw), weight, reason);
    }

    public static ComponentScore Volatility(double? volPercentile, double weight)
    {
        const SignalComponent c = SignalComponent.Volatility;
        if (volPercentile is not { } p) return Missing(c, weight, "No GARCH volatility");
        var regime = p >= 80 ? "elevated" : p <= 20 ? "calm" : "normal";
        return new ComponentScore(c, Pct((50 - p) / 50), weight, $"GARCH vol at {Ordinal(p)} percentile ({regime})");
    }

    public static ComponentScore Valuation(ValuationInputs? v, double weight)
    {
        const SignalComponent c = SignalComponent.Valuation;
        var parts = new List<double>();
        var reasons = new List<string>();
        if (v?.Pe is { } pe)
        {
            if (pe <= 0) { parts.Add(-0.5); reasons.Add("Loss-making (negative PE)"); }
            else if (v.SectorMedianPe is { } mpe && mpe > 0)
            {
                parts.Add(SeriesMath.Clamp((1 - pe / mpe) / 0.5));
                reasons.Add(Inv($"PE {pe:0.0} vs sector {mpe:0.0}"));
            }
        }
        if (v?.Pb is { } pb && pb > 0 && v.SectorMedianPb is { } mpb && mpb > 0)
        {
            parts.Add(SeriesMath.Clamp((1 - pb / mpb) / 0.5));
            reasons.Add(Inv($"PB {pb:0.0} vs sector {mpb:0.0}"));
        }
        if (v?.DividendYield is { } dy)
        {
            parts.Add(Math.Clamp((dy - 0.02) / 0.04, -0.5, 1));
            reasons.Add(Inv($"yield {dy * 100:0.0}%"));
        }
        return parts.Count == 0
            ? Missing(c, weight, "No valuation data")
            : new ComponentScore(c, Pct(parts.Average()), weight, string.Join(", ", reasons));
    }

    public static ComponentScore Forecast(ForecastInputs? f, double weight)
    {
        const SignalComponent c = SignalComponent.Forecast;
        if (f is null) return Missing(c, weight, "No forecast");
        var raw = 0.6 * SeriesMath.Clamp((f.ProbUp - 0.5) / 0.2) + 0.4 * SeriesMath.Clamp(f.P50Return / 0.05);
        var trust = f.HitRate is { } hr ? Math.Clamp(0.5 + (hr - 0.5) * 5, 0.25, 1) : 1;
        var reason = Inv($"{f.Model} {f.Horizon}d: P(up) {f.ProbUp * 100:0}%, median {Signed(f.P50Return * 100)}%")
            + (f.HitRate is { } h ? Inv($", hit rate {h * 100:0}%") : "");
        return new ComponentScore(c, Pct(raw * trust), weight, reason);
    }

    /// <summary>Sentiment × confidence, each item fading linearly to zero over <see cref="NewsDecayDays"/>.</summary>
    public static ComponentScore News(IReadOnlyList<NewsInputs>? news, double weight)
    {
        const SignalComponent c = SignalComponent.News;
        double sum = 0, decaySum = 0;
        var n = 0;
        foreach (var item in news ?? [])
        {
            var d = 1 - Math.Max(0, item.AgeDays) / NewsDecayDays;
            if (d <= 0) continue;
            sum += Math.Clamp(item.Sentiment, -1, 1) * Math.Clamp(item.Confidence, 0, 1) * d;
            decaySum += d;
            n++;
        }
        if (n == 0) return Missing(c, weight, "No recent insights");
        var score = Pct(sum / decaySum);
        var tone = score >= 20 ? "positive" : score <= -20 ? "negative" : "mixed";
        return new ComponentScore(c, score, weight, $"{n} recent insight{(n == 1 ? "" : "s")}, {tone} tone");
    }

    static ComponentScore Missing(SignalComponent c, double weight, string reason) => new(c, null, weight, reason);

    static double Pct(double raw) => Math.Round(SeriesMath.Clamp(raw) * 100, 1);

    static double? Avg(double? a, double? b) => (a, b) switch
    {
        ({ } x, { } y) => (x + y) / 2,
        ({ } x, null) => x,
        (null, { } y) => y,
        _ => null,
    };

    static string Inv(FormattableString s) => FormattableString.Invariant(s);

    static string F0(double v) => v.ToString("0", CultureInfo.InvariantCulture);

    static string Signed(double v) => (v >= 0 ? "+" : "") + v.ToString("0.0", CultureInfo.InvariantCulture);

    static string Ordinal(double p)
    {
        var n = (int)Math.Round(p);
        var suffix = (n % 100) is 11 or 12 or 13 ? "th" : (n % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" };
        return n + suffix;
    }
}
