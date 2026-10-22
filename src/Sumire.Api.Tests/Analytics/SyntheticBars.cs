using Sumire.Api.Analytics.Core;

namespace Sumire.Api.Tests.Analytics;

/// <summary>Deterministic synthetic price series for indicator and model tests.</summary>
public static class SyntheticBars
{
    public static readonly DateOnly Start = new(2023, 1, 2);

    /// <summary>Weekdays from <see cref="Start"/>.</summary>
    public static IEnumerable<DateOnly> TradingDates()
    {
        for (var d = Start; ; d = d.AddDays(1))
            if (d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
                yield return d;
    }

    public static List<Bar> FromCloses(IEnumerable<double> closes, long volume = 1_000_000) =>
        closes.Zip(TradingDates(), (c, d) =>
        {
            var close = (decimal)Math.Round(c, 4);
            return new Bar(d, close, close * 1.01m, close * 0.99m, close, close, volume);
        }).ToList();

    public static List<Bar> Linear(int n, double start = 1000, double step = 5) =>
        FromCloses(Enumerable.Range(0, n).Select(i => start + step * i));

    /// <summary>Geometric random walk with constant daily drift and volatility.</summary>
    public static List<Bar> RandomWalk(int n, int seed, double mu = 0.0003, double sigma = 0.02, double start = 1000)
    {
        var rng = new Random(seed);
        var closes = new double[n];
        var p = start;
        for (var i = 0; i < n; i++)
        {
            if (i > 0) p *= Math.Exp(mu + sigma * Gaussian(rng));
            closes[i] = p;
        }
        return FromCloses(closes);
    }

    public static double Gaussian(Random rng)
    {
        var u1 = 1.0 - rng.NextDouble();
        var u2 = rng.NextDouble();
        return Math.Sqrt(-2 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2);
    }
}
