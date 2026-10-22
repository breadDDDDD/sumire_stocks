namespace Sumire.Api.Analytics.Core;

public enum RotationQuadrant { Leading, Weakening, Lagging, Improving }

/// <summary>RS-Ratio and RS-Momentum are centred on 100 (RRG style).</summary>
public record RotationPoint(DateOnly Date, double RsRatio, double RsMomentum, RotationQuadrant Quadrant);

/// <summary>Relative strength of a stock (or sector index) against a benchmark, aligned by date.</summary>
public static class RelativeStrength
{
    /// <summary>Stock return minus benchmark return over the last <paramref name="days"/> common dates.</summary>
    public static double? RelativeReturn(IReadOnlyList<Bar> stock, IReadOnlyList<Bar> benchmark, int days)
    {
        var a = SeriesMath.Align(stock, benchmark);
        if (a.Count <= days) return null;
        var from = a[^(days + 1)];
        var to = a[^1];
        return (to.A / from.A - 1) - (to.B / from.B - 1);
    }

    /// <summary>Price ratio stock/benchmark per common date.</summary>
    public static List<(DateOnly Date, double Ratio)> RatioSeries(IReadOnlyList<Bar> stock, IReadOnlyList<Bar> benchmark) =>
        SeriesMath.Align(stock, benchmark).Select(p => (p.Date, p.A / p.B)).ToList();

    public static RotationQuadrant Classify(double rsRatio, double rsMomentum) => (rsRatio >= 100, rsMomentum >= 100) switch
    {
        (true, true) => RotationQuadrant.Leading,
        (true, false) => RotationQuadrant.Weakening,
        (false, false) => RotationQuadrant.Lagging,
        (false, true) => RotationQuadrant.Improving,
    };

    /// <summary>
    /// RS-Ratio = 100 × ratio / its <paramref name="lookback"/>-day mean; RS-Momentum = 100 × RS-Ratio / RS-Ratio
    /// <paramref name="momentumLag"/> days earlier. Returns the trail of the last <paramref name="tail"/> points.
    /// </summary>
    public static IReadOnlyList<RotationPoint> Rotation(IReadOnlyList<Bar> stock, IReadOnlyList<Bar> benchmark,
        int lookback = 60, int momentumLag = 10, int tail = 1)
    {
        var ratio = RatioSeries(stock, benchmark);
        if (ratio.Count < lookback + momentumLag) return [];

        var rs = new double[ratio.Count];
        var sum = 0.0;
        for (var i = 0; i < ratio.Count; i++)
        {
            sum += ratio[i].Ratio;
            if (i >= lookback) sum -= ratio[i - lookback].Ratio;
            rs[i] = i >= lookback - 1 ? 100 * ratio[i].Ratio / (sum / lookback) : double.NaN;
        }

        var points = new List<RotationPoint>();
        for (var i = Math.Max(lookback - 1 + momentumLag, ratio.Count - tail); i < ratio.Count; i++)
        {
            var mom = 100 * rs[i] / rs[i - momentumLag];
            points.Add(new RotationPoint(ratio[i].Date, rs[i], mom, Classify(rs[i], mom)));
        }
        return points;
    }
}
