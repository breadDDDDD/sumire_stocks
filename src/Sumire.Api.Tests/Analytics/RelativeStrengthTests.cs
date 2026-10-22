using Sumire.Api.Analytics.Core;

namespace Sumire.Api.Tests.Analytics;

public class RelativeStrengthTests
{
    [Theory]
    [InlineData(105, 102, RotationQuadrant.Leading)]
    [InlineData(105, 98, RotationQuadrant.Weakening)]
    [InlineData(95, 98, RotationQuadrant.Lagging)]
    [InlineData(95, 102, RotationQuadrant.Improving)]
    public void Classifies_quadrants(double ratio, double momentum, RotationQuadrant expected) =>
        Assert.Equal(expected, RelativeStrength.Classify(ratio, momentum));

    [Fact]
    public void Relative_return_is_difference_of_simple_returns()
    {
        var index = SyntheticBars.Linear(100, start: 1000, step: 0);
        var stock = SyntheticBars.Linear(100, start: 100, step: 1);
        var rel = RelativeStrength.RelativeReturn(stock, index, 20);
        Assert.Equal(199.0 / 179 - 1, rel!.Value, 9);
        Assert.Null(RelativeStrength.RelativeReturn(stock, index, 200));
    }

    [Fact]
    public void Accelerating_outperformer_is_leading()
    {
        var index = SyntheticBars.Linear(200, start: 1000, step: 0);
        var stock = SyntheticBars.FromCloses(Enumerable.Range(0, 200).Select(i => 100 * Math.Exp(0.0001 * i * i)));
        var p = RelativeStrength.Rotation(stock, index).Single();
        Assert.Equal(RotationQuadrant.Leading, p.Quadrant);
        Assert.True(p.RsRatio > 100 && p.RsMomentum > 100);
    }

    [Fact]
    public void Rebounding_underperformer_is_improving()
    {
        var index = SyntheticBars.Linear(200, start: 1000, step: 0);
        // Long slide, then a recent bounce that hasn't recovered the 60-day average yet.
        var closes = Enumerable.Range(0, 190).Select(i => 200.0 - 0.5 * i)
            .Concat(Enumerable.Range(1, 10).Select(i => 105.5 + 0.6 * i));
        var p = RelativeStrength.Rotation(SyntheticBars.FromCloses(closes), index).Single();
        Assert.Equal(RotationQuadrant.Improving, p.Quadrant);
    }

    [Fact]
    public void Rotation_returns_tail_and_needs_history()
    {
        var a = SyntheticBars.RandomWalk(200, seed: 1);
        var b = SyntheticBars.RandomWalk(200, seed: 2);
        Assert.Equal(5, RelativeStrength.Rotation(a, b, tail: 5).Count);
        Assert.Empty(RelativeStrength.Rotation(a.Take(50).ToList(), b));
    }
}
