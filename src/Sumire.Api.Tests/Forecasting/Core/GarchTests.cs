using Sumire.Api.Forecasting;
using Sumire.Api.Tests.Analytics;

namespace Sumire.Api.Tests.Forecasting.Core;

public class GarchTests
{
    /// <summary>Simulates GARCH(1,1) log returns with Gaussian shocks.</summary>
    public static double[] Simulate(int n, double omega, double alpha, double beta, int seed, double mu = 0)
    {
        var rng = new Random(seed);
        var r = new double[n];
        var s2 = omega / (1 - alpha - beta);
        for (var t = 0; t < n; t++)
        {
            var e = Math.Sqrt(s2) * SyntheticBars.Gaussian(rng);
            r[t] = mu + e;
            s2 = omega + alpha * e * e + beta * s2;
        }
        return r;
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    public void Recovers_known_parameters(int seed)
    {
        const double omega = 1e-5, alpha = 0.10, beta = 0.85;
        var fit = Garch.Fit(Simulate(4000, omega, alpha, beta, seed, mu: 0.0005))!;

        Assert.True(fit.Converged);
        Assert.InRange(fit.Alpha, alpha - 0.04, alpha + 0.04);
        Assert.InRange(fit.Beta, beta - 0.06, beta + 0.06);
        Assert.InRange(fit.LongRunVariance, 1.5e-4, 2.7e-4); // true 2e-4
        Assert.InRange(fit.Mu, 0.0, 0.001);
        Assert.True(double.IsFinite(fit.LogLikelihood));
    }

    [Fact]
    public void Multi_day_variance_reverts_to_long_run()
    {
        var fit = new GarchFit(0, Omega: 2e-6, Alpha: 0.1, Beta: 0.88, NextVariance: 4e-4, LogLikelihood: 0, Converged: true, ConditionalVol: [0.01, 0.02]);
        Assert.Equal(1e-4, fit.LongRunVariance, 12);
        Assert.Equal(4e-4, fit.VarianceAt(1), 12);
        Assert.Equal(1e-4 + 0.98 * 3e-4, fit.VarianceAt(2), 12);
        Assert.True(fit.VarianceAt(500) < 1.01e-4);
        Assert.Equal(fit.VarianceAt(1) + fit.VarianceAt(2), fit.CumulativeVariance(2), 12);
        Assert.Equal(Math.Sqrt(fit.CumulativeVariance(20)), fit.SigmaOver(20), 12);
        Assert.Equal(75, fit.VolPercentile); // σ 0.02 ties the max: (1 below + ½ tie) / 2
    }

    [Fact]
    public void Vol_percentile_is_high_right_after_a_shock()
    {
        var r = Simulate(600, 1e-5, 0.1, 0.85, seed: 3);
        r[^1] = -0.12; // crash day
        var fit = Garch.Fit(r)!;
        Assert.True(fit.VolPercentile > 95, $"percentile {fit.VolPercentile}");
    }

    [Fact]
    public void Too_few_returns_gives_null()
    {
        Assert.Null(Garch.Fit(new double[50]));
        Assert.Null(Garch.Fit(new double[200])); // zero variance
    }

    [Fact]
    public void Fits_from_bars_using_the_last_two_years()
    {
        var bars = SyntheticBars.RandomWalk(800, seed: 4, sigma: 0.015);
        var fit = Garch.Fit(bars)!;
        Assert.Equal(504, fit.ConditionalVol.Length);
        Assert.InRange(fit.AnnualizedVol, 0.15, 0.35);
        Assert.True(fit.Persistence < 1);
    }
}
