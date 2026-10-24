using Sumire.Api.Forecasting;
using Sumire.Api.Forecasting.Chronos;
using Sumire.Api.Tests.Analytics;
using Xunit.Abstractions;

namespace Sumire.Api.Tests.Forecasting.Chronos;

/// <summary>Runs the real ONNX model when it is already in the local cache; passes without running otherwise.</summary>
public class ChronosGoldenTests(ITestOutputHelper output)
{
    [Fact]
    public void Real_model_forecasts_a_saved_series_sensibly()
    {
        var path = ForecastModelOptions.FromEnvironment() with { Repo = ForecastModelOptions.DefaultRepo, Revision = ForecastModelOptions.DefaultRevision, File = ForecastModelOptions.DefaultFile };
        if (!File.Exists(path.LocalPath))
        {
            output.WriteLine($"Skipped: model not cached at {path.LocalPath}");
            return;
        }

        var stock = SyntheticBars.RandomWalk(750, seed: 3, mu: 0.0005, sigma: 0.015);
        var ihsg = SyntheticBars.RandomWalk(750, seed: 4, mu: 0.0002, sigma: 0.01, start: 7000);
        using var model = new OnnxChronosModel(path.LocalPath);
        output.WriteLine(model.Describe());

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var rows = ChronosForecaster.Run(model, [new ChronosTarget("TEST", stock, [ihsg]), new ChronosTarget("IHSG", ihsg, [])]);
        output.WriteLine($"2 targets in {sw.ElapsedMilliseconds} ms");

        Assert.Equal(6, rows.Count);
        foreach (var r in rows)
        {
            var last = (r.Code == "TEST" ? stock : ihsg)[^1].Close;
            output.WriteLine($"{r.Code} h{r.Horizon}: {r.P10} / {r.P50} / {r.P90}  P(up) {r.ProbUp:0.00}");
            Assert.True(r.P10 < r.P50 && r.P50 < r.P90);
            Assert.InRange(r.P50, last * 0.8m, last * 1.2m);
            Assert.InRange(r.ProbUp, 0.05, 0.95);
        }
        var widths = rows.Where(r => r.Code == "TEST").OrderBy(r => r.Horizon).Select(r => r.P90 - r.P10).ToList();
        Assert.True(widths[0] < widths[2]);
    }
}
