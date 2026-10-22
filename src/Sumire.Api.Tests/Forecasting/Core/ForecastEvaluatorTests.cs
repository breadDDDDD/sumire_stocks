using Sumire.Api.Data;
using Sumire.Api.Forecasting;

namespace Sumire.Api.Tests.Forecasting.Core;

public class ForecastEvaluatorTests
{
    [Fact]
    public void Scores_a_hand_made_example()
    {
        ForecastOutcome[] outcomes =
        [
            new(100, 90, 105, 120, 0.7, 110),  // up called, up realized, inside, APE 5/110
            new(100, 95, 98, 102, 0.3, 104),   // down called, up realized, outside, APE 6/104
            new(100, 80, 100, 115, 0.5, 90),   // tie on P(up), P50 == base -> down called; down realized, inside, APE 10/90
            new(100, 85, 102, 110, 0.6, 100),  // up called, flat realized (not up) -> miss, inside, APE 2/100
        ];
        var s = ForecastEvaluator.Evaluate(outcomes)!;

        Assert.Equal(4, s.N);
        Assert.Equal(0.5, s.DirAccuracy, 9);
        Assert.Equal(0.75, s.Coverage80, 9);
        Assert.Equal((5.0 / 110 + 6.0 / 104 + 10.0 / 90 + 2.0 / 100) / 4, s.Mape, 9);
    }

    [Fact]
    public void Empty_gives_null()
    {
        Assert.Null(ForecastEvaluator.Evaluate([]));
    }

    [Fact]
    public void Matches_forecasts_to_the_close_horizon_trading_days_later()
    {
        var d0 = new DateOnly(2026, 10, 1);
        var closes = Enumerable.Range(0, 30).Select(i => (d0.AddDays(i), 100m + i)).ToList();
        Forecast F(int day, int h, string model) => new()
        {
            Code = "BBCA", AsOf = d0.AddDays(day), Horizon = h, Model = model, P10 = 90, P50 = 101, P90 = 130, ProbUp = 0.6,
        };

        var matched = ForecastEvaluator.Match([F(0, 5, "baseline"), F(2, 5, "baseline"), F(0, 20, "chronos-2"), F(20, 20, "baseline")], closes).ToList();

        Assert.Equal(3, matched.Count); // the last one is not due yet
        Assert.Equal(100m, matched[0].Outcome.BaseClose);
        Assert.Equal(105m, matched[0].Outcome.Actual);
        Assert.Equal(107m, matched[1].Outcome.Actual);

        var scores = ForecastEvaluator.ScoreByModel(matched);
        Assert.Equal(["baseline/5", "chronos-2/20"], scores.Select(s => $"{s.Model}/{s.Horizon}"));
        Assert.Equal(2, scores[0].Score.N);
        Assert.Equal(1, scores[0].Score.DirAccuracy);
    }
}
