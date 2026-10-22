using Sumire.Api.Analytics.Core;

namespace Sumire.Api.Forecasting;

/// <param name="Mean">Expected h-day log return (drift × h).</param>
/// <param name="Sigma">σ of the h-day log return.</param>
public record HorizonForecast(int Horizon, decimal P10, decimal P50, decimal P90, double ProbUp, double Mean, double Sigma);

public record BaselineForecast(DateOnly AsOf, decimal LastClose, string VolSource, IReadOnlyList<HorizonForecast> Horizons);

/// <summary>
/// Random walk with drift plus the GARCH cone (plan §5.3): log(S_h/S_0) ~ N(h·μ, Σ σ²ₖ). The benchmark every
/// other model must beat.
/// </summary>
public static class BaselineForecaster
{
    public const string ModelName = "baseline";
    public const int DriftWindow = SeriesMath.TradingDaysPerYear;
    const int MinBars = 21;

    /// <param name="garch">A fit to reuse; fitted here when null. Falls back to the recent sample variance.</param>
    public static BaselineForecast? Forecast(IReadOnlyList<Bar> bars, GarchFit? garch = null, IReadOnlyList<int>? horizons = null)
    {
        if (bars.Count < MinBars || bars[^1].Close <= 0) return null;
        var returns = SeriesMath.LogReturns(bars);
        var recent = returns.AsSpan(Math.Max(0, returns.Length - DriftWindow));
        var mu = SeriesMath.Mean(recent);

        garch ??= Garch.Fit(bars);
        var sampleVar = Math.Pow(SeriesMath.StdDev(recent), 2);
        Func<int, double> variance = garch is null ? h => h * sampleVar : garch.CumulativeVariance;

        var last = bars[^1].Close;
        var result = new List<HorizonForecast>();
        foreach (var h in horizons ?? ForecastMath.Horizons)
        {
            var mean = mu * h;
            var sigma = Math.Sqrt(variance(h));
            result.Add(new HorizonForecast(h,
                ForecastMath.LognormalQuantile(last, mean, sigma, ForecastMath.Z10),
                ForecastMath.LognormalQuantile(last, mean, sigma, 0),
                ForecastMath.LognormalQuantile(last, mean, sigma, ForecastMath.Z90),
                ForecastMath.ProbUp(mean, sigma),
                mean, sigma));
        }
        return new BaselineForecast(bars[^1].Date, last, garch is null ? "sample" : "garch", result);
    }
}
