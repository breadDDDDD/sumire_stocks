using Skender.Stock.Indicators;

namespace Sumire.Api.Analytics.Core;

public enum CrossKind { Golden, Death }

/// <summary>SMA50 crossing SMA200: golden = up through, death = down through.</summary>
public record MaCross(CrossKind Kind, DateOnly Date);

/// <summary>A support or resistance level built from clustered swing highs/lows.</summary>
public record PriceLevel(decimal Price, int Touches, DateOnly LastTouched);

public record Pivots(decimal P, decimal R1, decimal R2, decimal R3, decimal S1, decimal S2, decimal S3);

/// <summary>
/// Latest values of every indicator for one instrument. Indicators that need more history than
/// available are null. Prices are split/dividend adjusted, so they line up with the latest close.
/// </summary>
public record TechnicalsSnapshot
{
    public required DateOnly Date { get; init; }
    public required decimal Close { get; init; }
    public decimal? Change { get; init; }
    public double? ChangePct { get; init; }

    // Trend
    public double? Sma20 { get; init; }
    public double? Sma50 { get; init; }
    public double? Sma200 { get; init; }
    public double? Ema20 { get; init; }
    public double? Ema50 { get; init; }
    public double? Ema200 { get; init; }
    /// <summary>% change of the MA over the last 10 bars.</summary>
    public double? Sma20Slope { get; init; }
    public double? Sma50Slope { get; init; }
    public double? Sma200Slope { get; init; }
    public MaCross? LastGoldenCross { get; init; }
    public MaCross? LastDeathCross { get; init; }
    public double? Adx { get; init; }
    public double? PlusDi { get; init; }
    public double? MinusDi { get; init; }

    // Momentum
    public double? Rsi { get; init; }
    public double? Macd { get; init; }
    public double? MacdSignal { get; init; }
    public double? MacdHist { get; init; }
    public double? StochK { get; init; }
    public double? StochD { get; init; }
    /// <summary>20-day rate of change, %.</summary>
    public double? Roc20 { get; init; }

    // Volatility
    public double? BbUpper { get; init; }
    public double? BbMiddle { get; init; }
    public double? BbLower { get; init; }
    public double? BbPercentB { get; init; }
    public double? BbWidth { get; init; }
    public double? Atr { get; init; }
    /// <summary>ATR as % of close.</summary>
    public double? AtrPct { get; init; }
    /// <summary>Annualized historical volatility (fraction, 0.25 = 25%).</summary>
    public double? Hv20 { get; init; }
    public double? Hv60 { get; init; }

    // Volume
    public double? Obv { get; init; }
    /// <summary>OBV regression slope over 20 bars in units of average daily volume (≈ -1..1).</summary>
    public double? ObvSlope { get; init; }
    public long Volume { get; init; }
    public double? AvgVolume20 { get; init; }
    public double? VolumeRatio { get; init; }
    /// <summary>Close × volume in Rp (unadjusted).</summary>
    public decimal ValueTraded { get; init; }
    public decimal? AvgValueTraded20 { get; init; }

    // Levels
    public decimal? High52w { get; init; }
    public decimal? Low52w { get; init; }
    public double? PctFrom52wHigh { get; init; }
    public double? PctFrom52wLow { get; init; }
    public Pivots? Pivots { get; init; }
    /// <summary>Nearest levels below the close, closest first.</summary>
    public IReadOnlyList<PriceLevel> Support { get; init; } = [];
    /// <summary>Nearest levels above the close, closest first.</summary>
    public IReadOnlyList<PriceLevel> Resistance { get; init; } = [];
}

/// <summary>Per-bar overlay series for charts; arrays are aligned with <see cref="Dates"/>.</summary>
public record TechnicalSeries(
    IReadOnlyList<DateOnly> Dates,
    double?[] Sma20, double?[] Sma50, double?[] Sma200, double?[] Ema20,
    double?[] BbUpper, double?[] BbLower, double?[] Rsi, double?[] MacdHist);

public static class TechnicalsCalculator
{
    const int SlopeLookback = 10;
    const int SwingWindow = 5;
    const int LevelLookback = 250;
    const double LevelMergePct = 0.015;
    const int MaxLevels = 3;

    public static TechnicalsSnapshot? Snapshot(IReadOnlyList<Bar> bars)
    {
        if (bars.Count == 0) return null;
        var q = ToQuotes(bars);
        var n = q.Length;
        var last = bars[^1];
        var close = (double)q[^1].Close;

        var sma20 = q.GetSma(20).Select(r => r.Sma).ToArray();
        var sma50 = q.GetSma(50).Select(r => r.Sma).ToArray();
        var sma200 = q.GetSma(200).Select(r => r.Sma).ToArray();
        var adx = q.GetAdx(14).Last();
        var macd = q.GetMacd(12, 26, 9).Last();
        var stoch = q.GetStoch(14, 3, 3).Last();
        var bb = q.GetBollingerBands(20, 2).Last();
        var atr = q.GetAtr(14).Last();
        var obv = q.GetObv().Select(r => r.Obv).ToArray();
        var returns = SeriesMath.LogReturns(bars);

        double? avgVol20 = n >= 20 ? bars.Skip(n - 20).Average(b => (double)b.Volume) : null;
        decimal? avgValue20 = n >= 20 ? bars.Skip(n - 20).Average(b => b.Close * b.Volume) : null;

        var year = q.AsSpan(Math.Max(0, n - SeriesMath.TradingDaysPerYear));
        decimal? hi52 = n >= 2 ? Max(year, x => x.High) : null;
        decimal? lo52 = n >= 2 ? Min(year, x => x.Low) : null;

        var (golden, death) = LastCrosses(bars, sma50, sma200);
        var (support, resistance) = Levels(bars, q, q[^1].Close);

        return new TechnicalsSnapshot
        {
            Date = last.Date,
            Close = last.Close,
            Change = n >= 2 ? last.Close - bars[^2].Close : null,
            ChangePct = n >= 2 && bars[^2].Close > 0 ? (double)((last.Close - bars[^2].Close) / bars[^2].Close) * 100 : null,

            Sma20 = sma20[^1],
            Sma50 = sma50[^1],
            Sma200 = sma200[^1],
            Ema20 = q.GetEma(20).Last().Ema,
            Ema50 = q.GetEma(50).Last().Ema,
            Ema200 = q.GetEma(200).Last().Ema,
            Sma20Slope = SlopePct(sma20),
            Sma50Slope = SlopePct(sma50),
            Sma200Slope = SlopePct(sma200),
            LastGoldenCross = golden,
            LastDeathCross = death,
            Adx = adx.Adx,
            PlusDi = adx.Pdi,
            MinusDi = adx.Mdi,

            Rsi = q.GetRsi(14).Last().Rsi,
            Macd = macd.Macd,
            MacdSignal = macd.Signal,
            MacdHist = macd.Histogram,
            StochK = stoch.Oscillator,
            StochD = stoch.Signal,
            Roc20 = q.GetRoc(20).Last().Roc,

            BbUpper = bb.UpperBand,
            BbMiddle = bb.Sma,
            BbLower = bb.LowerBand,
            BbPercentB = bb.PercentB,
            BbWidth = bb.Width,
            Atr = atr.Atr,
            AtrPct = atr.Atrp,
            Hv20 = HistVol(returns, 20),
            Hv60 = HistVol(returns, 60),

            Obv = obv[^1],
            ObvSlope = n >= 20 && avgVol20 > 0 ? SeriesMath.Slope(obv.AsSpan(n - 20)) / avgVol20 : null,
            Volume = last.Volume,
            AvgVolume20 = avgVol20,
            VolumeRatio = avgVol20 > 0 ? last.Volume / avgVol20 : null,
            ValueTraded = last.Close * last.Volume,
            AvgValueTraded20 = avgValue20,

            High52w = hi52,
            Low52w = lo52,
            PctFrom52wHigh = hi52 > 0 ? (close / (double)hi52.Value - 1) * 100 : null,
            PctFrom52wLow = lo52 > 0 ? (close / (double)lo52.Value - 1) * 100 : null,
            Pivots = n >= 1 ? ClassicPivots(q[^1]) : null,
            Support = support,
            Resistance = resistance,
        };
    }

    public static TechnicalSeries Series(IReadOnlyList<Bar> bars)
    {
        var q = ToQuotes(bars);
        var bb = q.GetBollingerBands(20, 2).ToArray();
        return new TechnicalSeries(
            bars.Select(b => b.Date).ToArray(),
            q.GetSma(20).Select(r => r.Sma).ToArray(),
            q.GetSma(50).Select(r => r.Sma).ToArray(),
            q.GetSma(200).Select(r => r.Sma).ToArray(),
            q.GetEma(20).Select(r => r.Ema).ToArray(),
            bb.Select(r => r.UpperBand).ToArray(),
            bb.Select(r => r.LowerBand).ToArray(),
            q.GetRsi(14).Select(r => r.Rsi).ToArray(),
            q.GetMacd(12, 26, 9).Select(r => r.Histogram).ToArray());
    }

    /// <summary>Classic floor pivots from one bar's high, low and close.</summary>
    public static Pivots ClassicPivots(IQuote bar)
    {
        decimal h = bar.High, l = bar.Low, c = bar.Close;
        var p = (h + l + c) / 3;
        return new Pivots(
            R(p), R(2 * p - l), R(p + (h - l)), R(h + 2 * (p - l)),
            R(2 * p - h), R(p - (h - l)), R(l - 2 * (h - p)));

        static decimal R(decimal v) => Math.Round(v, 2);
    }

    static Quote[] ToQuotes(IReadOnlyList<Bar> bars)
    {
        var q = new Quote[bars.Count];
        for (var i = 0; i < bars.Count; i++)
        {
            var b = bars[i];
            var f = b.Close > 0 && b.AdjClose > 0 ? b.AdjClose / b.Close : 1m;
            q[i] = new Quote
            {
                Date = b.Date.ToDateTime(TimeOnly.MinValue),
                Open = b.Open * f,
                High = b.High * f,
                Low = b.Low * f,
                Close = b.Close * f,
                Volume = b.Volume,
            };
        }
        return q;
    }

    static double? SlopePct(double?[] ma)
    {
        if (ma.Length <= SlopeLookback) return null;
        var now = ma[^1];
        var then = ma[^(SlopeLookback + 1)];
        return now is { } a && then is { } b && b != 0 ? (a / b - 1) * 100 : null;
    }

    static double? HistVol(double[] returns, int window) =>
        returns.Length >= window
            ? SeriesMath.StdDev(returns.AsSpan(returns.Length - window)) * Math.Sqrt(SeriesMath.TradingDaysPerYear)
            : null;

    static (MaCross? Golden, MaCross? Death) LastCrosses(IReadOnlyList<Bar> bars, double?[] fast, double?[] slow)
    {
        MaCross? golden = null, death = null;
        for (var i = bars.Count - 1; i > 0 && (golden is null || death is null); i--)
        {
            if (fast[i] is not { } f || slow[i] is not { } s || fast[i - 1] is not { } pf || slow[i - 1] is not { } ps)
                break;
            if (golden is null && pf <= ps && f > s) golden = new MaCross(CrossKind.Golden, bars[i].Date);
            if (death is null && pf >= ps && f < s) death = new MaCross(CrossKind.Death, bars[i].Date);
        }
        return (golden, death);
    }

    /// <summary>Swing highs/lows (fractals), merged within 1.5%, split around the close.</summary>
    static (List<PriceLevel> Support, List<PriceLevel> Resistance) Levels(IReadOnlyList<Bar> bars, Quote[] q, decimal close)
    {
        var start = Math.Max(SwingWindow, q.Length - LevelLookback);
        var points = new List<(decimal Price, DateOnly Date)>();
        for (var i = start; i < q.Length - SwingWindow; i++)
        {
            bool isHigh = true, isLow = true;
            for (var k = i - SwingWindow; k <= i + SwingWindow && (isHigh || isLow); k++)
            {
                if (k == i) continue;
                if (q[k].High >= q[i].High) isHigh = false;
                if (q[k].Low <= q[i].Low) isLow = false;
            }
            if (isHigh) points.Add((q[i].High, bars[i].Date));
            if (isLow) points.Add((q[i].Low, bars[i].Date));
        }

        var clusters = new List<(decimal Sum, int Count, DateOnly Last)>();
        foreach (var (price, date) in points.OrderBy(p => p.Price))
        {
            if (clusters.Count > 0)
            {
                var c = clusters[^1];
                var mean = c.Sum / c.Count;
                if (mean > 0 && (double)((price - mean) / mean) <= LevelMergePct)
                {
                    clusters[^1] = (c.Sum + price, c.Count + 1, date > c.Last ? date : c.Last);
                    continue;
                }
            }
            clusters.Add((price, 1, date));
        }

        var levels = clusters.Select(c => new PriceLevel(Math.Round(c.Sum / c.Count, 2), c.Count, c.Last)).ToList();
        var support = levels.Where(l => l.Price < close).OrderByDescending(l => l.Price).Take(MaxLevels).ToList();
        var resistance = levels.Where(l => l.Price > close).OrderBy(l => l.Price).Take(MaxLevels).ToList();
        return (support, resistance);
    }

    static decimal Max(ReadOnlySpan<Quote> q, Func<Quote, decimal> f)
    {
        var m = decimal.MinValue;
        foreach (var x in q) m = Math.Max(m, f(x));
        return Math.Round(m, 2);
    }

    static decimal Min(ReadOnlySpan<Quote> q, Func<Quote, decimal> f)
    {
        var m = decimal.MaxValue;
        foreach (var x in q) m = Math.Min(m, f(x));
        return Math.Round(m, 2);
    }
}
