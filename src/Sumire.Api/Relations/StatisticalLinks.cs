using Sumire.Api.Analytics.Core;

namespace Sumire.Api.Relations;

public sealed record CorrelatedLink(string Code, double Rho, int N);

/// <summary>Stock return on day t vs the proxy's return on the previous common day.</summary>
public sealed record LeadLagLink(string Proxy, double Rho, int N, double TStat);

public sealed record StatisticalSnapshot(
    DateOnly AsOf,
    DateTimeOffset ComputedAt,
    Dictionary<string, List<CorrelatedLink>> Correlated,
    Dictionary<string, List<LeadLagLink>> LeadLag)
{
    public static readonly StatisticalSnapshot Empty = new(default, default, [], []);
}

/// <summary>Return correlations among stocks and a one-day lead-lag check against factor proxies.</summary>
public static class StatisticalLinks
{
    public const int CorrelationWindow = 60;
    public const int LeadLagWindow = 120;
    public const double MinRho = 0.6;
    public const int TopCorrelated = 5;
    public const int TopLeadLag = 3;
    /// <summary>|t| above this (about p &lt; 0.01 two-sided) and |rho| at least <see cref="MinLeadLagRho"/> to keep a lead-lag link.</summary>
    public const double MinTStat = 2.6;
    public const double MinLeadLagRho = 0.2;

    /// <summary>Global markets, currencies and commodities that trade before or overnight relative to IDX.</summary>
    public static readonly string[] ProxyCodes = ["SPX", "N225", "HSI", "STI", "SSE", "USDIDR", "US10Y", "WTI", "BRENT", "GOLD"];

    public static double Pearson(ReadOnlySpan<double> x, ReadOnlySpan<double> y)
    {
        var n = Math.Min(x.Length, y.Length);
        if (n < 3) return double.NaN;
        double mx = 0, my = 0;
        for (var i = 0; i < n; i++) { mx += x[i]; my += y[i]; }
        mx /= n; my /= n;
        double sxy = 0, sxx = 0, syy = 0;
        for (var i = 0; i < n; i++)
        {
            var dx = x[i] - mx;
            var dy = y[i] - my;
            sxy += dx * dy; sxx += dx * dx; syy += dy * dy;
        }
        return sxx <= 0 || syy <= 0 ? double.NaN : sxy / Math.Sqrt(sxx * syy);
    }

    public static double TStat(double rho, int n) =>
        n <= 2 ? 0 : Math.Abs(rho) >= 0.9999 ? 99 * Math.Sign(rho) : rho * Math.Sqrt((n - 2) / (1 - rho * rho));

    /// <summary>Log returns on the dates both series traded, keyed by the later date.</summary>
    public static (double[] A, double[] B) AlignedReturns(IReadOnlyList<Bar> a, IReadOnlyList<Bar> b, int window)
    {
        var pairs = SeriesMath.Align(a, b);
        var start = Math.Max(1, pairs.Count - window);
        var n = pairs.Count - start;
        if (n <= 0) return ([], []);
        var ra = new double[n];
        var rb = new double[n];
        for (var i = 0; i < n; i++)
        {
            var (_, a1, b1) = pairs[start + i];
            var (_, a0, b0) = pairs[start + i - 1];
            ra[i] = Math.Log(a1 / a0);
            rb[i] = Math.Log(b1 / b0);
        }
        return (ra, rb);
    }

    /// <summary>Correlation of the last <paramref name="window"/> common daily returns; null when there is too little overlap.</summary>
    public static (double Rho, int N)? Correlation(IReadOnlyList<Bar> a, IReadOnlyList<Bar> b, int window = CorrelationWindow)
    {
        var (ra, rb) = AlignedReturns(a, b, window);
        if (ra.Length < window * 2 / 3) return null;
        var rho = Pearson(ra, rb);
        return double.IsNaN(rho) ? null : (rho, ra.Length);
    }

    /// <summary>Correlation of the stock's return on day t with the proxy's return on day t-1 (common trading days).</summary>
    public static LeadLagLink? LeadLag(IReadOnlyList<Bar> stock, IReadOnlyList<Bar> proxy, string proxyCode, int window = LeadLagWindow)
    {
        var (rs, rp) = AlignedReturns(stock, proxy, window + 1);
        if (rs.Length < window * 2 / 3) return null;
        var n = rs.Length - 1;
        var rho = Pearson(rs.AsSpan(1), rp.AsSpan(0, n));
        if (double.IsNaN(rho)) return null;
        return new LeadLagLink(proxyCode, rho, n, TStat(rho, n));
    }

    /// <summary>For every stock, the top correlated other stocks with |rho| at or above <see cref="MinRho"/>.</summary>
    public static Dictionary<string, List<CorrelatedLink>> CorrelationClusters(IReadOnlyDictionary<string, IReadOnlyList<Bar>> stocks,
        int window = CorrelationWindow, double minRho = MinRho, int top = TopCorrelated)
    {
        var codes = stocks.Keys.Order(StringComparer.Ordinal).ToArray();
        var all = codes.ToDictionary(c => c, _ => new List<CorrelatedLink>());
        for (var i = 0; i < codes.Length; i++)
            for (var j = i + 1; j < codes.Length; j++)
            {
                if (Correlation(stocks[codes[i]], stocks[codes[j]], window) is not { } c || Math.Abs(c.Rho) < minRho) continue;
                all[codes[i]].Add(new CorrelatedLink(codes[j], Math.Round(c.Rho, 3), c.N));
                all[codes[j]].Add(new CorrelatedLink(codes[i], Math.Round(c.Rho, 3), c.N));
            }
        return all.Where(kv => kv.Value.Count > 0)
            .ToDictionary(kv => kv.Key, kv => kv.Value.OrderByDescending(l => Math.Abs(l.Rho)).ThenBy(l => l.Code).Take(top).ToList());
    }

    /// <summary>For every stock, the proxies whose previous-day move significantly predicts the stock's move.</summary>
    public static Dictionary<string, List<LeadLagLink>> LeadLags(IReadOnlyDictionary<string, IReadOnlyList<Bar>> stocks,
        IReadOnlyDictionary<string, IReadOnlyList<Bar>> proxies, int window = LeadLagWindow, int top = TopLeadLag)
    {
        var result = new Dictionary<string, List<LeadLagLink>>();
        foreach (var (code, bars) in stocks)
        {
            var links = proxies
                .Select(p => LeadLag(bars, p.Value, p.Key, window))
                .OfType<LeadLagLink>()
                .Where(l => Math.Abs(l.TStat) >= MinTStat && Math.Abs(l.Rho) >= MinLeadLagRho)
                .OrderByDescending(l => Math.Abs(l.Rho))
                .Take(top)
                .Select(l => l with { Rho = Math.Round(l.Rho, 3), TStat = Math.Round(l.TStat, 2) })
                .ToList();
            if (links.Count > 0) result[code] = links;
        }
        return result;
    }
}
