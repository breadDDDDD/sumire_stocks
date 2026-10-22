using Sumire.Api.Analytics.Core;
using Sumire.Api.Analytics.Services;
using Sumire.Api.Data;
using Sumire.Api.Tests.Analytics;

namespace Sumire.Api.Tests.AnalyticsApi;

static class TestUniverse
{
    public static Instrument Stock(string code, string sector = "Energy") =>
        new() { Code = code, YahooSymbol = code + ".JK", Name = code + " Tbk", Sector = sector, Core = true };

    public static Instrument Ihsg() => new() { Code = "IHSG", YahooSymbol = "^JKSE", Name = "IHSG", Sector = "Index", IsIndex = true, Core = true };

    public static Universe Build(Dictionary<string, (string Sector, List<Bar> Bars)> stocks, List<Bar>? ihsg = null,
        Dictionary<string, Fundamental>? fundamentals = null)
    {
        var bars = stocks.ToDictionary(s => s.Key, s => s.Value.Bars);
        if (ihsg is not null) bars["IHSG"] = ihsg;
        return UniverseBuilder.Build(new UniverseInput
        {
            ExpectedDate = new DateOnly(2026, 10, 22),
            Now = DateTimeOffset.UtcNow,
            Source = "test",
            Stocks = stocks.Select(s => Stock(s.Key, s.Value.Sector)).ToList(),
            Bars = bars,
            IhsgInstrument = ihsg is null ? null : Ihsg(),
            Fundamentals = fundamentals ?? [],
        });
    }

    public static List<Bar> Flat(int n, double price = 1000) => SyntheticBars.FromCloses(Enumerable.Repeat(price, n));
}
