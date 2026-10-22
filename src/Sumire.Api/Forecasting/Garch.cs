using MathNet.Numerics.LinearAlgebra;
using MathNet.Numerics.Optimization;
using Sumire.Api.Analytics.Core;

namespace Sumire.Api.Forecasting;

/// <summary>
/// Fitted GARCH(1,1) on daily log returns: σ²ₜ₊₁ = ω + α·εₜ² + β·σ²ₜ. Variances are daily, in log-return units.
/// </summary>
public record GarchFit(
    double Mu, double Omega, double Alpha, double Beta,
    double NextVariance, double LogLikelihood, bool Converged,
    double[] ConditionalVol)
{
    public double Persistence => Alpha + Beta;

    public double LongRunVariance => Persistence < 1 ? Omega / (1 - Persistence) : NextVariance;

    /// <summary>Expected variance of the return k days ahead (k = 1 is tomorrow).</summary>
    public double VarianceAt(int k) =>
        LongRunVariance + Math.Pow(Persistence, k - 1) * (NextVariance - LongRunVariance);

    /// <summary>Variance of the summed log return over the next h days.</summary>
    public double CumulativeVariance(int h)
    {
        var s = 0.0;
        for (var k = 1; k <= h; k++) s += VarianceAt(k);
        return s;
    }

    /// <summary>σ of the h-day log return (the ±1σ cone half-width in log terms).</summary>
    public double SigmaOver(int h) => Math.Sqrt(CumulativeVariance(h));

    public double AnnualizedVol => Math.Sqrt(NextVariance * SeriesMath.TradingDaysPerYear);

    public double LongRunAnnualizedVol => Math.Sqrt(LongRunVariance * SeriesMath.TradingDaysPerYear);

    /// <summary>Where tomorrow's σ sits among the fitted history of σ, 0..100.</summary>
    public double VolPercentile => SeriesMath.PercentileRank(ConditionalVol, Math.Sqrt(NextVariance));
}

public static class Garch
{
    public const int MinReturns = 100;
    const double Scale = 100; // fit on % returns for numerical stability
    const double MaxPersistence = 0.999;

    public static GarchFit? Fit(IReadOnlyList<Bar> bars, int window = 2 * SeriesMath.TradingDaysPerYear)
    {
        var r = SeriesMath.LogReturns(bars);
        return Fit(r.AsSpan(Math.Max(0, r.Length - window)).ToArray());
    }

    /// <summary>Maximum-likelihood fit (Gaussian). Returns null with fewer than <see cref="MinReturns"/> returns.</summary>
    public static GarchFit? Fit(IReadOnlyList<double> logReturns)
    {
        var n = logReturns.Count;
        if (n < MinReturns) return null;

        var mu = 0.0;
        foreach (var x in logReturns) mu += x;
        mu /= n;
        var e = new double[n];
        for (var i = 0; i < n; i++) e[i] = (logReturns[i] - mu) * Scale;
        var sampleVar = 0.0;
        foreach (var x in e) sampleVar += x * x;
        sampleVar /= n;
        if (!(sampleVar > 0)) return null;

        var objective = ObjectiveFunction.Value(p => NegLogLik(e, sampleVar, p));
        var start = Vector<double>.Build.Dense([Math.Log(sampleVar * 0.02), Logit(0.98 / MaxPersistence), Logit(0.08 / 0.98)]);

        Vector<double> best = start;
        var converged = false;
        try
        {
            best = new NelderMeadSimplex(1e-9, 2000).FindMinimum(objective, start).MinimizingPoint;
            converged = true;
        }
        catch (Exception ex) when (ex is MaximumIterationsException or OptimizationException)
        {
            // Keep the starting point (a typical equity GARCH) rather than failing the whole pipeline.
        }

        var (omega, alpha, beta) = Params(best);
        var vols = new double[n];
        var s2 = sampleVar;
        var ll = 0.0;
        for (var t = 0; t < n; t++)
        {
            vols[t] = Math.Sqrt(s2) / Scale;
            ll += Math.Log(2 * Math.PI) + Math.Log(s2) + e[t] * e[t] / s2;
            s2 = omega + alpha * e[t] * e[t] + beta * s2;
        }
        var s2Scale = Scale * Scale;
        // Back to unscaled returns: each density is Scale times larger.
        var logLik = -0.5 * ll + n * Math.Log(Scale);
        return new GarchFit(mu, omega / s2Scale, alpha, beta, s2 / s2Scale, logLik, converged, vols);
    }

    static double NegLogLik(double[] e, double sampleVar, Vector<double> p)
    {
        var (omega, alpha, beta) = Params(p);
        var s2 = sampleVar;
        var ll = 0.0;
        foreach (var x in e)
        {
            if (!(s2 > 1e-12)) return 1e12;
            ll += Math.Log(s2) + x * x / s2;
            s2 = omega + alpha * x * x + beta * s2;
        }
        return double.IsFinite(ll) ? 0.5 * ll : 1e12;
    }

    /// <summary>Unconstrained vector to ω &gt; 0, α, β ≥ 0, α + β &lt; 1.</summary>
    static (double Omega, double Alpha, double Beta) Params(Vector<double> p)
    {
        var omega = Math.Exp(Math.Clamp(p[0], -30, 10));
        var persistence = MaxPersistence * Logistic(p[1]);
        var alpha = persistence * Logistic(p[2]);
        return (omega, alpha, persistence - alpha);
    }

    static double Logistic(double x) => 1 / (1 + Math.Exp(-x));

    static double Logit(double p) => Math.Log(p / (1 - p));
}
