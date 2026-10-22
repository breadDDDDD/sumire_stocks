using Microsoft.Extensions.Caching.Memory;

namespace Sumire.Api.Market.Rss;

/// <summary>One RSS source. <see cref="LinkContains"/> filters site-wide feeds down to market articles.</summary>
public sealed record RssFeed(string Publisher, string Url, string? LinkContains = null);

/// <summary>
/// Free market headlines from Indonesian RSS feeds, fetched only when requested and cached 15 minutes.
/// Feeds come from the "News:Feeds" config section (array of { publisher, url, linkContains }), else <see cref="DefaultFeeds"/>.
/// </summary>
public sealed class RssNewsClient(HttpClient http, IMemoryCache cache, InstrumentCatalog catalog, IConfiguration config, ILogger<RssNewsClient> logger)
{
    public static readonly RssFeed[] DefaultFeeds =
    [
        new("CNBC Indonesia", "https://www.cnbcindonesia.com/market/rss"),
        new("Kontan", "https://investasi.kontan.co.id/rss"),
        new("Bisnis", "https://rss.bisnis.com/", "market.bisnis.com"),
        new("IDX Channel", "https://www.idxchannel.com/rss/market-news"),
    ];

    static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(15);
    static readonly SemaphoreSlim FetchLock = new(1, 1);
    const string CacheKey = "news:all";

    public IReadOnlyList<RssFeed> Feeds
    {
        get
        {
            var configured = config.GetSection("News:Feeds").Get<RssFeed[]>();
            return configured is { Length: > 0 } ? configured : DefaultFeeds;
        }
    }

    /// <summary>All headlines, newest first, optionally only those linked to <paramref name="code"/>.</summary>
    public async Task<IReadOnlyList<Headline>> GetHeadlinesAsync(string? code = null, int limit = 30, CancellationToken ct = default)
    {
        var all = await GetAllAsync(ct);
        IEnumerable<Headline> q = all;
        if (!string.IsNullOrWhiteSpace(code))
        {
            var c = code.Trim().ToUpperInvariant();
            q = q.Where(h => h.Tickers.Contains(c));
        }
        return q.Take(Math.Clamp(limit, 1, 200)).ToList();
    }

    async Task<IReadOnlyList<Headline>> GetAllAsync(CancellationToken ct)
    {
        if (cache.TryGetValue(CacheKey, out IReadOnlyList<Headline>? cached) && cached is not null) return cached;
        await FetchLock.WaitAsync(ct);
        try
        {
            if (cache.TryGetValue(CacheKey, out cached) && cached is not null) return cached;
            var matcher = new TickerMatcher(catalog.All);
            var results = await Task.WhenAll(Feeds.Select(f => FetchFeedAsync(f, ct)));
            var merged = Merge(results.SelectMany(r => r).Select(matcher.Tag));
            // A total failure is cached briefly so a dead network is not hammered.
            cache.Set(CacheKey, merged, merged.Count > 0 ? CacheFor : TimeSpan.FromMinutes(1));
            return merged;
        }
        finally
        {
            FetchLock.Release();
        }
    }

    /// <summary>Newest first, one entry per URL.</summary>
    public static IReadOnlyList<Headline> Merge(IEnumerable<Headline> headlines) =>
        headlines.GroupBy(h => h.Url.TrimEnd('/'), StringComparer.OrdinalIgnoreCase).Select(g => g.First())
            .OrderByDescending(h => h.PublishedAt ?? DateTimeOffset.MinValue)
            .ToList();

    async Task<List<Headline>> FetchFeedAsync(RssFeed feed, CancellationToken ct)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            var xml = await http.GetStringAsync(feed.Url, timeout.Token);
            return RssParser.Parse(xml, feed.Publisher, feed.LinkContains);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning("RSS feed {Publisher} failed: {Error}", feed.Publisher, ex.Message);
            return [];
        }
    }
}
