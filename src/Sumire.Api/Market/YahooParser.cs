using System.Text.Json;

namespace Sumire.Api.Market;

/// <summary>Parses Yahoo Finance chart / quote / quoteSummary JSON.</summary>
public static class YahooParser
{
    public static ChartData ParseChart(string json) => Guard(() => ParseChartCore(json));

    static ChartData ParseChartCore(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var chart = doc.RootElement.GetProperty("chart");
        ThrowIfError(chart);
        if (!chart.TryGetProperty("result", out var results) || results.ValueKind != JsonValueKind.Array || results.GetArrayLength() == 0)
            throw new YahooException("Not Found", "No chart data");

        var r = results[0];
        var meta = r.GetProperty("meta");
        var offset = TimeSpan.FromSeconds(Int(meta, "gmtoffset") ?? 0);
        var symbol = Str(meta, "symbol") ?? "";

        var bars = new List<PriceBar>();
        if (r.TryGetProperty("timestamp", out var ts) && ts.ValueKind == JsonValueKind.Array)
        {
            var indicators = r.GetProperty("indicators");
            var quotes = indicators.GetProperty("quote");
            var q = quotes.GetArrayLength() > 0 ? quotes[0] : default;
            JsonElement? adj = indicators.TryGetProperty("adjclose", out var a) && a.GetArrayLength() > 0
                ? a[0].GetProperty("adjclose") : null;

            for (var i = 0; i < ts.GetArrayLength(); i++)
            {
                var c = Dec(At(q, "close", i));
                if (c is null || ts[i].ValueKind != JsonValueKind.Number) continue; // Yahoo pads missing intervals with nulls
                var o = Dec(At(q, "open", i)) ?? c.Value;
                var h = Dec(At(q, "high", i)) ?? Math.Max(o, c.Value);
                var l = Dec(At(q, "low", i)) ?? Math.Min(o, c.Value);
                var adjClose = adj is { } ad && i < ad.GetArrayLength() ? Dec(ad[i]) ?? c.Value : c.Value;
                var vol = At(q, "volume", i) is { ValueKind: JsonValueKind.Number } v ? (long)v.GetDouble() : 0;
                var time = DateTimeOffset.FromUnixTimeSeconds(ts[i].GetInt64()).ToOffset(offset);
                bars.Add(new PriceBar(time, o, h, l, c.Value, adjClose, vol));
            }
        }

        var events = new List<CorporateAction>();
        if (r.TryGetProperty("events", out var ev))
        {
            if (ev.TryGetProperty("dividends", out var divs))
                foreach (var d in divs.EnumerateObject())
                    events.Add(new CorporateAction(LocalDate(d.Value.GetProperty("date").GetInt64(), offset), "dividend", Dec(d.Value.GetProperty("amount")) ?? 0));
            if (ev.TryGetProperty("splits", out var splits))
                foreach (var s in splits.EnumerateObject())
                {
                    var num = Dec(s.Value.GetProperty("numerator")) ?? 1;
                    var den = Dec(s.Value.GetProperty("denominator")) ?? 1;
                    events.Add(new CorporateAction(LocalDate(s.Value.GetProperty("date").GetInt64(), offset), "split", den == 0 ? 1 : num / den, Str(s.Value, "splitRatio")));
                }
            events.Sort((x, y) => x.Date.CompareTo(y.Date));
        }

        return new ChartData(
            symbol,
            Str(meta, "longName") ?? Str(meta, "shortName"),
            Str(meta, "currency"),
            offset,
            Dec(meta, "regularMarketPrice"),
            Dec(meta, "previousClose") ?? Dec(meta, "chartPreviousClose"),
            Int(meta, "regularMarketTime") is { } t ? DateTimeOffset.FromUnixTimeSeconds(t).ToOffset(offset) : null,
            bars,
            events);
    }

    public static List<YahooQuote> ParseQuotes(string json) => Guard(() => ParseQuotesCore(json));

    static List<YahooQuote> ParseQuotesCore(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("finance", out var finance)) ThrowIfError(finance);
        var resp = doc.RootElement.GetProperty("quoteResponse");
        ThrowIfError(resp);

        var list = new List<YahooQuote>();
        foreach (var q in resp.GetProperty("result").EnumerateArray())
        {
            var offset = TimeSpan.FromMilliseconds(Int(q, "gmtOffSetMilliseconds") ?? 0);
            list.Add(new YahooQuote(
                Symbol: Str(q, "symbol") ?? "",
                Name: Str(q, "longName") ?? Str(q, "shortName"),
                Price: Dec(q, "regularMarketPrice"),
                Change: Dec(q, "regularMarketChange"),
                ChangePct: Dec(q, "regularMarketChangePercent"),
                PrevClose: Dec(q, "regularMarketPreviousClose"),
                Open: Dec(q, "regularMarketOpen"),
                High: Dec(q, "regularMarketDayHigh"),
                Low: Dec(q, "regularMarketDayLow"),
                Volume: Int(q, "regularMarketVolume"),
                Time: Int(q, "regularMarketTime") is { } t ? DateTimeOffset.FromUnixTimeSeconds(t).ToOffset(offset) : null,
                MarketState: Str(q, "marketState"),
                FiftyTwoWeekHigh: Dec(q, "fiftyTwoWeekHigh"),
                FiftyTwoWeekLow: Dec(q, "fiftyTwoWeekLow"),
                MarketCap: Dec(q, "marketCap")));
        }
        return list;
    }

    /// <summary>Returns quoteSummary.result[0] (cloned) for the caller to read modules from.</summary>
    public static JsonElement ParseQuoteSummary(string json) => Guard(() => ParseQuoteSummaryCore(json));

    static JsonElement ParseQuoteSummaryCore(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("finance", out var finance)) ThrowIfError(finance);
        var qs = doc.RootElement.GetProperty("quoteSummary");
        ThrowIfError(qs);
        var result = qs.GetProperty("result");
        if (result.ValueKind != JsonValueKind.Array || result.GetArrayLength() == 0)
            throw new YahooException("Not Found", "No quoteSummary data");
        return result[0].Clone();
    }

    /// <summary>Yahoo's {"raw": 1.23, "fmt": "1.23"} wrapper or a plain number.</summary>
    public static double? Raw(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var v)) return null;
        if (v.ValueKind == JsonValueKind.Object && v.TryGetProperty("raw", out var raw)) v = raw;
        return v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;
    }

    /// <summary>An HTML error page or a reshaped payload becomes a YahooException, which callers already treat as an outage.</summary>
    static T Guard<T>(Func<T> parse)
    {
        try { return parse(); }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or IndexOutOfRangeException or FormatException)
        {
            throw new YahooException("BadResponse", $"Unexpected Yahoo response: {ex.Message}");
        }
    }

    /// <summary>Element i of an indicator array, or an undefined element when the array is missing or short.</summary>
    static JsonElement At(JsonElement quote, string name, int i) =>
        quote.ValueKind == JsonValueKind.Object && quote.TryGetProperty(name, out var arr) && arr.ValueKind == JsonValueKind.Array && i < arr.GetArrayLength()
            ? arr[i] : default;

    static void ThrowIfError(JsonElement e)
    {
        if (e.TryGetProperty("error", out var err) && err.ValueKind == JsonValueKind.Object)
            throw new YahooException(Str(err, "code") ?? "Error", Str(err, "description") ?? "Yahoo error");
    }

    static DateOnly LocalDate(long unix, TimeSpan offset) =>
        DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeSeconds(unix).ToOffset(offset).DateTime);

    static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    static long? Int(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? (long)v.GetDouble() : null;

    static decimal? Dec(JsonElement e, string name) => e.TryGetProperty(name, out var v) ? Dec(v) : null;

    /// <summary>Doubles carry float noise (6149.99999); 4 decimals is plenty for IDR prices, FX and yields.</summary>
    static decimal? Dec(JsonElement v)
    {
        if (v.ValueKind != JsonValueKind.Number) return null;
        var d = v.GetDouble();
        if (double.IsNaN(d) || double.IsInfinity(d) || Math.Abs(d) > 7.9e27) return null;
        return Math.Round((decimal)d, 4);
    }
}
