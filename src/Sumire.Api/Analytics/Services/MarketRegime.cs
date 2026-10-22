using System.Globalization;
using Sumire.Api.Analytics.Core;

namespace Sumire.Api.Analytics.Services;

/// <summary>Risk-on / Neutral / Risk-off from IHSG trend, breadth and GARCH volatility (plan §5.7).</summary>
public static class MarketRegime
{
    public static Regime Classify(TechnicalsSnapshot? ihsg, Breadth? breadth, double? volPercentile)
    {
        var score = 0;
        var reasons = new List<string>();
        var close = ihsg is null ? (double?)null : (double)ihsg.Close;

        void Ma(double? ma, string name)
        {
            if (ma is not { } m || close is not { } c) return;
            var above = c >= m;
            score += above ? 1 : -1;
            reasons.Add($"IHSG {(above ? "above" : "below")} {name}");
        }
        Ma(ihsg?.Sma200, "200-day MA");
        Ma(ihsg?.Sma50, "50-day MA");

        if (breadth is { Total: > 0 } b)
        {
            var pct = b.PctAboveSma50.ToString("0", CultureInfo.InvariantCulture);
            if (b.PctAboveSma50 >= 60) { score++; reasons.Add($"Breadth healthy: {pct}% above 50-day MA"); }
            else if (b.PctAboveSma50 <= 40) { score--; reasons.Add($"Breadth weak: {pct}% above 50-day MA"); }
            else reasons.Add($"Breadth mixed: {pct}% above 50-day MA");
        }

        if (volPercentile is { } v)
        {
            var p = v.ToString("0", CultureInfo.InvariantCulture);
            if (v >= 80) { score--; reasons.Add($"Volatility elevated (GARCH {p}th percentile)"); }
            else if (v <= 30) { score++; reasons.Add($"Volatility calm (GARCH {p}th percentile)"); }
        }

        var label = score >= 2 ? Regime.RiskOn : score <= -2 ? Regime.RiskOff : Regime.Neutral;
        if (reasons.Count == 0) reasons.Add("Not enough data");
        return new Regime(label, score, reasons);
    }
}
