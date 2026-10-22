using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Sumire.Api.Market;

namespace Sumire.Api.Tests.Market;

public class YahooClientTests
{
    /// <summary>Answers by URL; records every request.</summary>
    sealed class FakeHandler(Func<HttpRequestMessage, int, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<string> Urls { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Urls.Add(request.RequestUri!.ToString());
            return Task.FromResult(respond(request, Urls.Count(u => u.Contains("/v7/finance/quote"))));
        }
    }

    static HttpResponseMessage Json(string body, HttpStatusCode code = HttpStatusCode.OK) =>
        new(code) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };

    static YahooClient Client(FakeHandler handler) =>
        new(new HttpClient(handler), new YahooSession(), TimeProvider.System, NullLogger<YahooClient>.Instance);

    [Fact]
    public async Task Quotes_refresh_crumb_once_on_401()
    {
        var crumbs = 0;
        var handler = new FakeHandler((req, quoteCalls) =>
        {
            var url = req.RequestUri!.ToString();
            if (url.StartsWith("https://fc.yahoo.com")) return new HttpResponseMessage(HttpStatusCode.NotFound);
            if (url.Contains("getcrumb")) return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent($"crumb{++crumbs}") };
            return quoteCalls == 1
                ? Json(YahooParserTests.Fixture("quote-unauthorized.json"), HttpStatusCode.Unauthorized)
                : Json(YahooParserTests.Fixture("quote-bbca-jkse-idr.json"));
        });

        var quotes = await Client(handler).GetQuotesAsync(["BBCA.JK", "^JKSE", "IDR=X"]);

        Assert.Equal(3, quotes.Count);
        Assert.Equal(2, crumbs);
        Assert.Equal(2, handler.Urls.Count(u => u.StartsWith("https://fc.yahoo.com")));
        Assert.Contains(handler.Urls, u => u.Contains("/v7/finance/quote") && u.Contains("crumb=crumb2"));
    }

    [Fact]
    public async Task Quotes_give_up_after_second_401()
    {
        var handler = new FakeHandler((req, _) =>
        {
            var url = req.RequestUri!.ToString();
            if (url.Contains("getcrumb")) return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("abc") };
            if (url.StartsWith("https://fc.yahoo.com")) return new HttpResponseMessage(HttpStatusCode.NotFound);
            return Json(YahooParserTests.Fixture("quote-unauthorized.json"), HttpStatusCode.Unauthorized);
        });

        var ex = await Assert.ThrowsAsync<YahooException>(() => Client(handler).GetQuotesAsync(["BBCA.JK"]));
        Assert.Equal(401, ex.Status);
        Assert.Equal(2, handler.Urls.Count(u => u.Contains("/v7/finance/quote")));
    }

    [Fact]
    public async Task Crumb_is_reused_between_calls()
    {
        var handler = new FakeHandler((req, _) =>
        {
            var url = req.RequestUri!.ToString();
            if (url.Contains("getcrumb")) return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("abc") };
            if (url.StartsWith("https://fc.yahoo.com")) return new HttpResponseMessage(HttpStatusCode.NotFound);
            return Json(YahooParserTests.Fixture("quote-bbca-jkse-idr.json"));
        });
        var client = Client(handler);

        await client.GetQuotesAsync(["BBCA.JK"]);
        await client.GetQuotesAsync(["BBRI.JK"]);

        Assert.Single(handler.Urls, u => u.Contains("getcrumb"));
    }

    [Fact]
    public async Task Quotes_are_batched_by_50()
    {
        var handler = new FakeHandler((req, _) =>
        {
            var url = req.RequestUri!.ToString();
            if (url.Contains("getcrumb")) return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("abc") };
            if (url.StartsWith("https://fc.yahoo.com")) return new HttpResponseMessage(HttpStatusCode.NotFound);
            return Json("""{"quoteResponse":{"result":[],"error":null}}""");
        });

        await Client(handler).GetQuotesAsync(Enumerable.Range(0, 120).Select(i => $"S{i}.JK"));

        Assert.Equal(3, handler.Urls.Count(u => u.Contains("/v7/finance/quote")));
    }

    [Fact]
    public async Task Chart_uses_period_when_from_is_given()
    {
        var handler = new FakeHandler((_, _) => Json(YahooParserTests.Fixture("chart-bbca-1mo-1d.json")));
        var from = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        var chart = await Client(handler).GetChartAsync("BBCA.JK", "1d", from: from, to: from.AddDays(30));

        Assert.Equal(23, chart.Bars.Count);
        Assert.Contains($"period1={from.ToUnixTimeSeconds()}", handler.Urls[0]);
        Assert.DoesNotContain("range=", handler.Urls[0]);
    }

    [Fact]
    public async Task Chart_not_found_keeps_status()
    {
        var handler = new FakeHandler((_, _) => Json(YahooParserTests.Fixture("chart-notfound.json"), HttpStatusCode.NotFound));

        var ex = await Assert.ThrowsAsync<YahooException>(() => Client(handler).GetChartAsync("ZZZZ.JK", "1d", "1mo"));
        Assert.True(ex.IsNotFound);
    }
}
