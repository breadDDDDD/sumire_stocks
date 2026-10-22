using MathNet.Numerics.Distributions;

namespace Sumire.Api.Forecasting;

public static class ForecastMath
{
    public static readonly int[] Horizons = [5, 20, 60];

    /// <summary>Standard normal quantiles for the P10 and P90 bands.</summary>
    public static readonly double Z10 = Normal.InvCDF(0, 1, 0.10);
    public static readonly double Z90 = Normal.InvCDF(0, 1, 0.90);

    public static double NormalCdf(double z) => Normal.CDF(0, 1, z);

    public static double NormalQuantile(double p) => Normal.InvCDF(0, 1, p);

    /// <summary>Price quantile when log(S_h / S_0) ~ N(mean, sigma²).</summary>
    public static decimal LognormalQuantile(decimal price, double mean, double sigma, double z) =>
        Math.Round(price * (decimal)Math.Exp(mean + z * sigma), 2);

    /// <summary>P(S_h > S_0) when log(S_h / S_0) ~ N(mean, sigma²).</summary>
    public static double ProbUp(double mean, double sigma) =>
        sigma > 0 ? NormalCdf(mean / sigma) : mean > 0 ? 1 : mean < 0 ? 0 : 0.5;
}
