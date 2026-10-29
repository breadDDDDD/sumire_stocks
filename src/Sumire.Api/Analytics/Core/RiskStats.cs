namespace Sumire.Api.Analytics.Core;

/// <summary>Simple returns as fractions (0.05 = +5%); null when history is too short.</summary>
public record PeriodReturns(double? D1, double? W1, double? M1, double? M3, double? Ytd, double? Y1);

public record Drawdown(double Depth, DateOnly Peak, DateOnly Trough);

public record RiskSnapshot(
    PeriodReturns Returns,
    double? Beta,
    double? Correlation,
    double? Sharpe,
    double? Volatility,
    Drawdown? MaxDrawdown);

/// <summary>Return and risk statistics on AdjClose.</summary>
public static class RiskStats
{
    public const double DefaultRiskFree = 0.05;
    public const int DefaultWindow = SeriesMath.TradingDaysPerYear;

    public static RiskSnapshot Compute(IReadOnlyList<Bar> bars, IReadOnlyList<Bar>? benchmark = null,
        double riskFree = DefaultRiskFree, int window = DefaultWindow)
    {
        var (beta, corr) = benchmark is null ? (null, null) : BetaCorrelation(bars, benchmark, window);
        var returns = SeriesMath.LogReturns(bars);
        var recent = returns.AsSpan(Math.Max(0, returns.Length - window));
        return new RiskSnapshot(
            Returns(bars),
            beta,
            corr,
            Sharpe(bars, riskFree, window),
            recent.Length >= 20 ? SeriesMath.StdDev(recent) * Math.Sqrt(SeriesMath.TradingDaysPerYear) : null,
            MaxDrawdown(bars, window));
    }

    public static PeriodReturns Returns(IReadOnlyList<Bar> bars)
    {
        if (bars.Count == 0) return new PeriodReturns(null, null, null, null, null, null);
        var last = bars[^1];
        double? Back(int days) => bars.Count > days ? Simple(bars[^(days + 1)].AdjClose, last.AdjClose) : null;

        double? ytd = null;
        for (var i = bars.Count - 1; i >= 0; i--)
        {
            if (bars[i].Date.Year < last.Date.Year)
            {
                ytd = Simple(bars[i].AdjClose, last.AdjClose);
                break;
            }
        }

        var yearAgo = SeriesMath.YearAgoIndex(bars);
        double? y1 = yearAgo >= 0 ? Simple(bars[yearAgo].AdjClose, last.AdjClose) : null;

        return new PeriodReturns(Back(1), Back(5), Back(21), Back(63), ytd, y1);
    }

    /// <summary>Simple return between the last close and the close <paramref name="days"/> bars earlier.</summary>
    public static double? ReturnOver(IReadOnlyList<Bar> bars, int days) =>
        bars.Count > days ? Simple(bars[^(days + 1)].AdjClose, bars[^1].AdjClose) : null;

    /// <summary>Beta and correlation of daily log returns, aligned by date, over the last <paramref name="window"/> returns.</summary>
    public static (double? Beta, double? Correlation) BetaCorrelation(IReadOnlyList<Bar> stock, IReadOnlyList<Bar> benchmark,
        int window = DefaultWindow, int minObservations = 20)
    {
        var aligned = SeriesMath.Align(stock, benchmark);
        var start = Math.Max(1, aligned.Count - window);
        var n = aligned.Count - start;
        if (n < minObservations) return (null, null);

        double sa = 0, sb = 0, saa = 0, sbb = 0, sab = 0;
        for (var i = start; i < aligned.Count; i++)
        {
            var a = Math.Log(aligned[i].A / aligned[i - 1].A);
            var b = Math.Log(aligned[i].B / aligned[i - 1].B);
            sa += a; sb += b; saa += a * a; sbb += b * b; sab += a * b;
        }
        var cov = sab - sa * sb / n;
        var va = saa - sa * sa / n;
        var vb = sbb - sb * sb / n;
        if (vb <= 0) return (null, null);
        var beta = cov / vb;
        double? corr = va > 0 ? cov / Math.Sqrt(va * vb) : null;
        return (beta, corr);
    }

    /// <summary>
    /// Annualized Sharpe ratio of daily simple returns over the window, against an annual risk-free rate. Simple, not
    /// log, returns: the log mean is lower by about half the variance, which would bias volatile stocks downwards.
    /// </summary>
    public static double? Sharpe(IReadOnlyList<Bar> bars, double riskFree = DefaultRiskFree, int window = DefaultWindow)
    {
        var r = SeriesMath.SimpleReturns(bars);
        var span = r.AsSpan(Math.Max(0, r.Length - window));
        if (span.Length < 20) return null;
        var sd = SeriesMath.StdDev(span);
        if (!(sd > 0)) return null;
        var excess = SeriesMath.Mean(span) - (Math.Pow(1 + riskFree, 1.0 / SeriesMath.TradingDaysPerYear) - 1);
        return excess / sd * Math.Sqrt(SeriesMath.TradingDaysPerYear);
    }

    /// <summary>Largest peak-to-trough fall as a positive fraction (0.3 = -30%), over the last window bars (null = all).</summary>
    public static Drawdown? MaxDrawdown(IReadOnlyList<Bar> bars, int? window = null)
    {
        var start = window is { } w ? Math.Max(0, bars.Count - w - 1) : 0;
        if (bars.Count - start < 2) return null;
        var peak = bars[start];
        Drawdown best = new(0, peak.Date, peak.Date);
        for (var i = start + 1; i < bars.Count; i++)
        {
            var b = bars[i];
            if (b.AdjClose > peak.AdjClose) { peak = b; continue; }
            if (peak.AdjClose <= 0) continue;
            var dd = (double)(1 - b.AdjClose / peak.AdjClose);
            if (dd > best.Depth) best = new Drawdown(dd, peak.Date, b.Date);
        }
        return best;
    }

    static double? Simple(decimal from, decimal to) => from > 0 ? (double)(to / from - 1) : null;
}
