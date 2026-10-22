using System.Net;
using System.Text.Json;

namespace Sumire.Api.Market;

/// <summary>Market data source. Yahoo today; a paid IDX feed can implement this later.</summary>
public interface IMarketDataProvider
{
    /// <summary>Bars for a range ("1d", "5d", "1mo", "1y"…) or, when <paramref name="from"/> is set, for a period.</summary>
    Task<ChartData> GetChartAsync(string symbol, string interval, string? range = null, DateTimeOffset? from = null, DateTimeOffset? to = null, CancellationToken ct = default);

    Task<IReadOnlyList<YahooQuote>> GetQuotesAsync(IEnumerable<string> symbols, CancellationToken ct = default);

    Task<JsonElement> GetQuoteSummaryAsync(string symbol, IEnumerable<string> modules, CancellationToken ct = default);
}

/// <summary>
/// Yahoo Finance over HTTP: chart (no auth), quote and quoteSummary (cookie + crumb).
/// Throttled to ~5 requests/second across all callers.
/// </summary>
public sealed class YahooClient(HttpClient http, YahooSession session, TimeProvider clock, ILogger<YahooClient> logger) : IMarketDataProvider
{
    public const string UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0 Safari/537.36";
    const string Base = "https://query1.finance.yahoo.com";
    const int QuoteBatch = 50;
    static readonly TimeSpan MinGap = TimeSpan.FromMilliseconds(200);

    public async Task<ChartData> GetChartAsync(string symbol, string interval, string? range = null, DateTimeOffset? from = null, DateTimeOffset? to = null, CancellationToken ct = default)
    {
        var url = $"{Base}/v8/finance/chart/{Uri.EscapeDataString(symbol)}?interval={interval}&events=div%2Csplits&includeAdjustedClose=true";
        url += from is { } f
            ? $"&period1={f.ToUnixTimeSeconds()}&period2={(to ?? clock.GetUtcNow()).ToUnixTimeSeconds()}"
            : $"&range={range ?? "1mo"}";

        using var resp = await SendAsync(url, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode && !body.TrimStart().StartsWith('{'))
            throw new YahooException(resp.StatusCode.ToString(), $"Chart request failed ({(int)resp.StatusCode})", (int)resp.StatusCode);
        try
        {
            return YahooParser.ParseChart(body);
        }
        catch (YahooException ex) when (ex.Status is null)
        {
            throw new YahooException(ex.Code, ex.Message, (int)resp.StatusCode);
        }
    }

    public async Task<IReadOnlyList<YahooQuote>> GetQuotesAsync(IEnumerable<string> symbols, CancellationToken ct = default)
    {
        var all = new List<YahooQuote>();
        foreach (var batch in symbols.Distinct().Chunk(QuoteBatch))
        {
            var body = await GetWithCrumbAsync(c => $"{Base}/v7/finance/quote?symbols={Uri.EscapeDataString(string.Join(',', batch))}&crumb={Uri.EscapeDataString(c)}", ct);
            all.AddRange(YahooParser.ParseQuotes(body));
        }
        return all;
    }

    public async Task<JsonElement> GetQuoteSummaryAsync(string symbol, IEnumerable<string> modules, CancellationToken ct = default)
    {
        var body = await GetWithCrumbAsync(c => $"{Base}/v10/finance/quoteSummary/{Uri.EscapeDataString(symbol)}?modules={string.Join(',', modules)}&crumb={Uri.EscapeDataString(c)}", ct);
        return YahooParser.ParseQuoteSummary(body);
    }

    /// <summary>Runs a crumb-protected request; on 401/403 refreshes the cookie + crumb once and retries.</summary>
    async Task<string> GetWithCrumbAsync(Func<string, string> url, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            var crumb = await GetCrumbAsync(attempt > 0, ct);
            using var resp = await SendAsync(url(crumb), ct);
            if (resp.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden && attempt == 0)
            {
                logger.LogInformation("Yahoo crumb rejected, refreshing");
                continue;
            }
            var body = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
                throw new YahooException(resp.StatusCode.ToString(), $"Yahoo request failed ({(int)resp.StatusCode})", (int)resp.StatusCode);
            return body;
        }
    }

    async Task<string> GetCrumbAsync(bool refresh, CancellationToken ct)
    {
        if (!refresh && session.Crumb is { } cached) return cached;
        await session.CrumbLock.WaitAsync(ct);
        try
        {
            if (!refresh && session.Crumb is { } again) return again;
            // fc.yahoo.com answers 404 but sets the A3 cookie the crumb is bound to.
            using (await SendAsync("https://fc.yahoo.com", ct)) { }
            using var resp = await SendAsync($"{Base}/v1/test/getcrumb", ct);
            var crumb = (await resp.Content.ReadAsStringAsync(ct)).Trim();
            if (!resp.IsSuccessStatusCode || crumb.Length == 0 || crumb.StartsWith('<') || crumb.StartsWith('{'))
                throw new YahooException("Unauthorized", $"Could not get a Yahoo crumb ({(int)resp.StatusCode})", (int)resp.StatusCode);
            return session.Crumb = crumb;
        }
        finally
        {
            session.CrumbLock.Release();
        }
    }

    async Task<HttpResponseMessage> SendAsync(string url, CancellationToken ct)
    {
        await session.Gate.WaitAsync(ct);
        try
        {
            var wait = session.LastRequest + MinGap - clock.GetUtcNow();
            if (wait > TimeSpan.Zero) await Task.Delay(wait, clock, ct);
            session.LastRequest = clock.GetUtcNow();
        }
        finally
        {
            session.Gate.Release();
        }
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        return await http.SendAsync(req, HttpCompletionOption.ResponseContentRead, ct);
    }
}

/// <summary>Process-wide Yahoo state shared by every typed-client instance: cookies, crumb and throttle.</summary>
public sealed class YahooSession
{
    public CookieContainer Cookies { get; } = new();
    internal SemaphoreSlim Gate { get; } = new(1, 1);
    internal SemaphoreSlim CrumbLock { get; } = new(1, 1);
    internal DateTimeOffset LastRequest { get; set; }
    internal string? Crumb { get; set; }
}
