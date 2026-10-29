using Sumire.Api.Market;

namespace Sumire.Api.Tests.Market;

public class YahooParserTests
{
    internal static string Fixture(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "market", name));

    [Fact]
    public void Daily_chart_parses_bars_in_wib()
    {
        var chart = YahooParser.ParseChart(Fixture("chart-bbca-1mo-1d.json"));

        Assert.Equal("BBCA.JK", chart.Symbol);
        Assert.Equal("PT Bank Central Asia Tbk", chart.Name);
        Assert.Equal("IDR", chart.Currency);
        Assert.Equal(TimeSpan.FromHours(7), chart.Offset);
        Assert.Equal(23, chart.Bars.Count);

        var first = chart.Bars[0];
        Assert.Equal(new DateOnly(2026, 9, 22), first.Date);
        Assert.Equal(9, first.Time.Hour);
        Assert.Equal((6225m, 6250m, 6175m, 6200m, 6200m, 93_211_200L), (first.Open, first.High, first.Low, first.Close, first.AdjClose, first.Volume));
        Assert.Equal(new DateOnly(2026, 10, 22), chart.Bars[^1].Date);
        Assert.Equal(5175m, chart.Bars[^1].Close);
        Assert.Equal(6200m, chart.PreviousClose); // chartPreviousClose fallback
        Assert.Empty(chart.Events);
    }

    [Fact]
    public void Intraday_chart_skips_null_padding()
    {
        var chart = YahooParser.ParseChart(Fixture("chart-bbca-1d-5m.json"));

        Assert.Equal(87 - 20, chart.Bars.Count);
        Assert.All(chart.Bars, b => Assert.True(b.Close > 0));
        Assert.Equal(5275m, chart.PreviousClose);
        Assert.Equal(new TimeOnly(9, 0), TimeOnly.FromDateTime(chart.Bars[0].Time.DateTime));
        Assert.True(chart.Bars.Zip(chart.Bars.Skip(1)).All(p => p.First.Time < p.Second.Time));
    }

    [Fact]
    public void Dividends_are_parsed_as_events()
    {
        var chart = YahooParser.ParseChart(Fixture("chart-bbri-2025-events.json"));

        Assert.Equal(236, chart.Bars.Count);
        Assert.Equal(2, chart.Events.Count);
        Assert.Equal(new CorporateAction(new DateOnly(2025, 4, 11), "dividend", 208.4m), chart.Events[0]);
        Assert.Equal(137m, chart.Events[1].Amount);
    }

    [Fact]
    public void Unknown_symbol_throws_not_found()
    {
        var ex = Assert.Throws<YahooException>(() => YahooParser.ParseChart(Fixture("chart-notfound.json")));
        Assert.True(ex.IsNotFound);
    }

    [Fact]
    public void Quotes_parse_with_local_offsets()
    {
        var quotes = YahooParser.ParseQuotes(Fixture("quote-bbca-jkse-idr.json"));

        Assert.Equal(["BBCA.JK", "^JKSE", "IDR=X"], quotes.Select(q => q.Symbol));
        var bbca = quotes[0];
        Assert.Equal(5175m, bbca.Price);
        Assert.Equal(5275m, bbca.PrevClose);
        Assert.Equal(-100m, bbca.Change);
        Assert.Equal(286_928_500L, bbca.Volume);
        Assert.Equal("POSTPOST", bbca.MarketState);
        Assert.Equal(TimeSpan.FromHours(7), bbca.Time!.Value.Offset);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1792660498), bbca.Time);
        Assert.Equal(5387.905m, quotes[1].Price);
    }

    [Fact]
    public void Unauthorized_quote_response_throws()
    {
        var ex = Assert.Throws<YahooException>(() => YahooParser.ParseQuotes(Fixture("quote-unauthorized.json")));
        Assert.Equal("Unauthorized", ex.Code);
    }

    [Theory]
    [InlineData("<html>Will be right back</html>")]
    [InlineData("""{"chart":{"result":[{"timestamp":[1],"indicators":{}}]}}""")]
    [InlineData("""{"unexpected":true}""")]
    public void Malformed_chart_becomes_a_yahoo_exception(string body) =>
        Assert.Equal("BadResponse", Assert.Throws<YahooException>(() => YahooParser.ParseChart(body)).Code);

    [Fact]
    public void Malformed_quotes_become_a_yahoo_exception() =>
        Assert.Throws<YahooException>(() => YahooParser.ParseQuotes("<html></html>"));

    [Fact]
    public void Short_or_missing_indicator_arrays_are_tolerated()
    {
        const string json = """
            {"chart":{"result":[{"meta":{"symbol":"X.JK","gmtoffset":25200},"timestamp":[1761098400,1761184800,1761271200],
             "indicators":{"quote":[{"close":[100,101,102],"open":[99,100]}]}}]}}
            """;
        var chart = YahooParser.ParseChart(json);
        Assert.Equal(3, chart.Bars.Count);
        Assert.Equal((102m, 102m, 0L), (chart.Bars[2].Close, chart.Bars[2].Open, chart.Bars[2].Volume));
    }
}
