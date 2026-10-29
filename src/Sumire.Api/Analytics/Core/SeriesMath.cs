namespace Sumire.Api.Analytics.Core;

/// <summary>Small numeric helpers shared by the analytics and forecasting code.</summary>
public static class SeriesMath
{
    /// <summary>IDX sessions per year (weekdays minus ~15-20 bursa holidays), used to annualize daily statistics.</summary>
    public const int TradingDaysPerYear = 242;

    /// <summary>
    /// Index of the last bar dated on or before one calendar year before the last bar, or -1 when the history does
    /// not reach back that far. Calendar based, so it doesn't depend on how many sessions a year had.
    /// </summary>
    public static int YearAgoIndex(IReadOnlyList<Bar> bars)
    {
        if (bars.Count < 2) return -1;
        var target = bars[^1].Date.AddYears(-1);
        if (bars[0].Date > target) return -1;
        for (var i = bars.Count - 2; i >= 0; i--)
            if (bars[i].Date <= target) return i;
        return -1;
    }

    /// <summary>Daily log returns of AdjClose; result[i] is the return into bars[i + 1].</summary>
    public static double[] LogReturns(IReadOnlyList<Bar> bars)
    {
        if (bars.Count < 2) return [];
        var r = new double[bars.Count - 1];
        for (var i = 1; i < bars.Count; i++)
            r[i - 1] = LogReturn(bars[i - 1].AdjClose, bars[i].AdjClose);
        return r;
    }

    /// <summary>Daily simple returns of AdjClose; result[i] is the return into bars[i + 1].</summary>
    public static double[] SimpleReturns(IReadOnlyList<Bar> bars)
    {
        if (bars.Count < 2) return [];
        var r = new double[bars.Count - 1];
        for (var i = 1; i < bars.Count; i++)
            r[i - 1] = bars[i - 1].AdjClose > 0 && bars[i].AdjClose > 0 ? (double)(bars[i].AdjClose / bars[i - 1].AdjClose) - 1 : 0;
        return r;
    }

    public static double LogReturn(decimal from, decimal to) =>
        from > 0 && to > 0 ? Math.Log((double)to / (double)from) : 0;

    public static double Mean(ReadOnlySpan<double> x)
    {
        if (x.IsEmpty) return double.NaN;
        var s = 0.0;
        foreach (var v in x) s += v;
        return s / x.Length;
    }

    /// <summary>Sample standard deviation (n - 1).</summary>
    public static double StdDev(ReadOnlySpan<double> x)
    {
        if (x.Length < 2) return double.NaN;
        var m = Mean(x);
        var s = 0.0;
        foreach (var v in x) s += (v - m) * (v - m);
        return Math.Sqrt(s / (x.Length - 1));
    }

    /// <summary>Least-squares slope of y against 0..n-1.</summary>
    public static double Slope(ReadOnlySpan<double> y)
    {
        var n = y.Length;
        if (n < 2) return double.NaN;
        var xm = (n - 1) / 2.0;
        var ym = Mean(y);
        double num = 0, den = 0;
        for (var i = 0; i < n; i++)
        {
            num += (i - xm) * (y[i] - ym);
            den += (i - xm) * (i - xm);
        }
        return num / den;
    }

    /// <summary>Share of values strictly below <paramref name="value"/> plus half the ties, 0..100.</summary>
    public static double PercentileRank(ReadOnlySpan<double> values, double value)
    {
        if (values.IsEmpty) return double.NaN;
        double below = 0, equal = 0;
        foreach (var v in values)
        {
            if (v < value) below++;
            else if (v == value) equal++;
        }
        return 100.0 * (below + equal / 2) / values.Length;
    }

    public static double Clamp(double v, double limit = 1) => Math.Clamp(v, -limit, limit);

    /// <summary>Pairs of AdjClose for dates present in both series, oldest first.</summary>
    public static List<(DateOnly Date, double A, double B)> Align(IReadOnlyList<Bar> a, IReadOnlyList<Bar> b)
    {
        var map = new Dictionary<DateOnly, decimal>(b.Count);
        foreach (var bar in b) map[bar.Date] = bar.AdjClose;
        var result = new List<(DateOnly, double, double)>(Math.Min(a.Count, b.Count));
        foreach (var bar in a)
            if (map.TryGetValue(bar.Date, out var other) && bar.AdjClose > 0 && other > 0)
                result.Add((bar.Date, (double)bar.AdjClose, (double)other));
        return result;
    }
}
