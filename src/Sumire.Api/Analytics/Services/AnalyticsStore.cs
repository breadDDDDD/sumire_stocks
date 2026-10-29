using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Sumire.Api.Analytics.Core;
using Sumire.Api.Data;
using Sumire.Api.Forecasting;
using Sumire.Api.Market;

namespace Sumire.Api.Analytics.Services;

/// <summary>
/// Holds the analytics snapshot of the core universe for the current trading day. Built once per trading day
/// (first request, startup warm-up or the EOD step) from DB bars, or from Yahoo when the DB is unavailable.
/// Stocks outside the core universe are analysed on demand and cached.
/// </summary>
public sealed class AnalyticsStore(
    IServiceScopeFactory scopes,
    InstrumentCatalog catalog,
    MarketCalendar calendar,
    IMemoryCache cache,
    TimeProvider clock,
    ILogger<AnalyticsStore> logger)
{
    public const int HistoryDays = 3 * 366;
    static readonly TimeSpan FailureBackoff = TimeSpan.FromSeconds(30);
    static readonly TimeOnly EodReady = new(16, 15);

    readonly object _lock = new();
    Universe? _current;
    Task<Universe>? _building;
    (DateTimeOffset At, Exception Error)? _failure;

    public Universe? Current => _current;

    /// <summary>Same rule as MarketDataService: today after 16:15 WIB on a trading day, else the previous trading day.</summary>
    public DateOnly ExpectedDate()
    {
        var now = calendar.NowWib;
        var today = DateOnly.FromDateTime(now.DateTime);
        return calendar.IsTradingDay(today) && TimeOnly.FromDateTime(now.DateTime) >= EodReady ? today : calendar.PreviousTradingDay(today);
    }

    public Task<Universe> GetAsync(CancellationToken ct = default)
    {
        var expected = ExpectedDate();
        if (_current is { } u && u.ExpectedDate == expected) return Task.FromResult(u);
        lock (_lock)
        {
            if (_failure is { } f && clock.GetUtcNow() - f.At < FailureBackoff) return Task.FromException<Universe>(f.Error);
            _building ??= BuildAsync();
            return _building.WaitAsync(ct);
        }
    }

    /// <summary>Forces a rebuild (EOD step) and waits for it.</summary>
    public Task<Universe> RebuildAsync(CancellationToken ct = default)
    {
        lock (_lock)
        {
            _failure = null;
            var previous = _building;
            _building = (previous ?? Task.CompletedTask).ContinueWith(_ => BuildAsync(), TaskScheduler.Default).Unwrap();
            return _building.WaitAsync(ct);
        }
    }

    /// <summary>Recomputes signal scores with freshly stored metrics, model forecasts and insights.</summary>
    public async Task<Universe> RescoreAsync(CancellationToken ct = default)
    {
        var u = await GetAsync(ct);
        using var scope = scopes.CreateScope();
        var stored = await LoadStoredAsync(scope.ServiceProvider.GetRequiredService<SumireDbContext>(), ct);
        var rescored = UniverseBuilder.Rescore(u, stored.Metrics, stored.ModelForecasts, stored.News);
        Set(rescored);
        return rescored;
    }

    /// <summary>Replaces the snapshot (e.g. after rescoring).</summary>
    public void Set(Universe u)
    {
        lock (_lock) _current = u;
    }

    async Task<Universe> BuildAsync()
    {
        try
        {
            var u = await LoadAndBuildAsync(CancellationToken.None);
            lock (_lock) { _current = u; _building = null; _failure = null; }
            logger.LogInformation("Analytics snapshot for {Date} built from {Source}: {Count} stocks in {Ms} ms",
                u.ExpectedDate, u.Source, u.Stocks.Count, (int)(clock.GetUtcNow() - u.BuiltAt).TotalMilliseconds);
            // The build churns through bars and Yahoo JSON once a day; give that memory back instead of idling on it.
            GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);
            return u;
        }
        catch (Exception ex)
        {
            lock (_lock) { _building = null; _failure = (clock.GetUtcNow(), ex); }
            logger.LogWarning("Analytics snapshot failed: {Error}", Db.ShortError(ex));
            throw;
        }
    }

    async Task<Universe> LoadAndBuildAsync(CancellationToken ct)
    {
        var started = clock.GetUtcNow();
        var expected = ExpectedDate();
        var stocks = catalog.Core.Where(i => !i.IsIndex).OrderBy(i => i.Code).ToList();
        var ihsg = catalog.Find("IHSG");
        var lq45 = catalog.Find("LQ45");
        var all = stocks.Concat(new[] { ihsg, lq45 }.OfType<Instrument>()).ToList();

        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SumireDbContext>();
        var extras = await LoadStoredAsync(db, ct);
        var (bars, source) = await LoadBarsAsync(scope.ServiceProvider, db, all, expected, ct);

        var input = new UniverseInput
        {
            ExpectedDate = expected,
            Now = started,
            Source = source,
            Stocks = stocks,
            Bars = bars,
            IhsgInstrument = ihsg,
            Lq45Instrument = lq45,
            Fundamentals = extras.Fundamentals,
            Metrics = extras.Metrics,
            ModelForecasts = extras.ModelForecasts,
            News = extras.News,
        };
        return await Task.Run(() => UniverseBuilder.Build(input), ct);
    }

    public sealed record Stored(
        Dictionary<string, Fundamental> Fundamentals,
        List<ForecastMetric> Metrics,
        Dictionary<string, List<Forecast>> ModelForecasts,
        Dictionary<string, List<NewsInputs>> News);

    /// <summary>Fundamentals, metrics, latest model forecasts and recent insights; empty when the DB is down.</summary>
    public async Task<Stored> LoadStoredAsync(SumireDbContext db, CancellationToken ct)
    {
        var empty = new Stored([], [], [], []);
        if (!Db.IsConfigured) return empty;
        try
        {
            var fundamentals = await db.Fundamentals.AsNoTracking().ToDictionaryAsync(f => f.Code, ct);
            var metrics = await db.ForecastMetrics.AsNoTracking().ToListAsync(ct);

            var since = calendar.TodayWib.AddDays(-10);
            var recent = await db.Forecasts.AsNoTracking()
                .Where(f => f.Model != BaselineForecaster.ModelName && f.AsOf >= since)
                .ToListAsync(ct);
            var modelForecasts = recent.GroupBy(f => f.Code).ToDictionary(g => g.Key,
                g => g.GroupBy(f => f.Model).SelectMany(m => m.Where(f => f.AsOf == m.Max(x => x.AsOf))).ToList());

            var newsSince = clock.GetUtcNow().AddDays(-SignalScorer.NewsDecayDays).ToUniversalTime();
            var insights = await db.Insights.AsNoTracking()
                .Where(i => i.Kind == "stock" && i.CreatedAt >= newsSince)
                .Select(i => new { i.Subject, i.Sentiment, i.Confidence, i.CreatedAt })
                .ToListAsync(ct);
            var now = clock.GetUtcNow();
            var news = insights.GroupBy(i => i.Subject).ToDictionary(g => g.Key,
                g => g.Select(i => new NewsInputs(i.Sentiment, ConfidenceValue(i.Confidence), (now - i.CreatedAt).TotalDays)).ToList());

            return new Stored(fundamentals, metrics, modelForecasts, news);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning("Stored analytics inputs unavailable: {Error}", Db.ShortError(ex));
            return empty;
        }
    }

    public static double ConfidenceValue(string confidence) => confidence switch
    {
        "high" => 1.0,
        "med" => 0.67,
        _ => 0.33,
    };

    async Task<(Dictionary<string, List<Bar>> Bars, string Source)> LoadBarsAsync(IServiceProvider sp, SumireDbContext db,
        List<Instrument> instruments, DateOnly expected, CancellationToken ct)
    {
        var from = expected.AddDays(-HistoryDays);
        if (Db.IsConfigured)
        {
            try
            {
                var codes = instruments.Select(i => i.Code).ToList();
                var rows = await db.DailyBars.AsNoTracking()
                    .Where(b => codes.Contains(b.Code) && b.Date >= from && b.Date <= expected)
                    .OrderBy(b => b.Code).ThenBy(b => b.Date)
                    .ToListAsync(ct);
                if (rows.Count > 0)
                    return (rows.GroupBy(r => r.Code).ToDictionary(g => g.Key, g => g.Select(ToBar).ToList()), "db");
                logger.LogInformation("No daily bars in the database yet; using Yahoo for the analytics snapshot");
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                logger.LogWarning("Daily bars unavailable from the database, using Yahoo: {Error}", Db.ShortError(ex));
            }
        }

        var yahoo = sp.GetRequiredService<IMarketDataProvider>();
        var result = new System.Collections.Concurrent.ConcurrentDictionary<string, List<Bar>>();
        var failed = 0;
        await Parallel.ForEachAsync(instruments, new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = ct }, async (inst, c) =>
        {
            try { result[inst.Code] = await YahooBarsAsync(yahoo, inst, from, expected, c); }
            catch (Exception ex) when (ex is HttpRequestException or YahooException or TaskCanceledException && !c.IsCancellationRequested)
            {
                Interlocked.Increment(ref failed);
            }
        });
        if (result.IsEmpty) throw new HttpRequestException("No bars from the database or Yahoo");
        if (failed > 0) logger.LogWarning("Yahoo bars failed for {Failed} instruments", failed);
        return (new Dictionary<string, List<Bar>>(result), "yahoo");
    }

    /// <summary>Daily bars for one instrument straight from Yahoo (finished sessions only).</summary>
    public static async Task<List<Bar>> YahooBarsAsync(IMarketDataProvider yahoo, Instrument inst, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var chart = await yahoo.GetChartAsync(inst.YahooSymbol, "1d",
            from: new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero), ct: ct);
        return chart.Bars.Where(b => b.Date <= to && b.Close > 0)
            .GroupBy(b => b.Date).Select(g => g.Last())
            .Select(b => new Bar(b.Date, b.Open, b.High, b.Low, b.Close, b.AdjClose > 0 ? b.AdjClose : b.Close, b.Volume))
            .ToList();
    }

    public static Bar ToBar(DailyBar b) => new(b.Date, b.Open, b.High, b.Low, b.Close, b.AdjClose > 0 ? b.AdjClose : b.Close, b.Volume);

    /// <summary>
    /// Analytics for any instrument: from the snapshot for core stocks and IHSG/LQ45, otherwise computed on demand
    /// (bars fetched and stored on first use) and cached for the trading day. Null for unknown codes.
    /// </summary>
    public async Task<StockAnalytics?> GetStockAsync(string code, CancellationToken ct = default)
    {
        code = code.Trim().ToUpperInvariant();
        var u = await GetAsync(ct);
        if (u.Find(code) is { } hit) return hit;

        var key = $"analytics:{code}:{u.ExpectedDate}";
        if (cache.TryGetValue(key, out StockAnalytics? cached)) return cached;

        using var scope = scopes.CreateScope();
        var sp = scope.ServiceProvider;
        var market = sp.GetRequiredService<MarketDataService>();
        var from = u.ExpectedDate.AddDays(-HistoryDays);

        List<Bar>? bars = null;
        var inst = catalog.Find(code);
        if (Db.IsConfigured)
        {
            try
            {
                inst = await market.EnsureBarsAsync(code, ct);
                if (inst is null) return null;
                bars = (await market.GetDailyBarsAsync(inst.Code, from, ct)).Where(b => b.Date <= u.ExpectedDate).Select(ToBar).ToList();
            }
            catch (Exception ex) when (!ct.IsCancellationRequested && ex is not (YahooException or HttpRequestException))
            {
                logger.LogDebug("DB bars for {Code} unavailable: {Error}", code, Db.ShortError(ex));
            }
        }
        if (bars is not { Count: > 0 })
        {
            inst ??= MarketDataService.IsIdxCode(code) ? new Instrument { Code = code, YahooSymbol = code + ".JK", Name = code } : null;
            if (inst is null) return null;
            try { bars = await YahooBarsAsync(sp.GetRequiredService<IMarketDataProvider>(), inst, from, u.ExpectedDate, ct); }
            catch (YahooException ex) when (ex.IsNotFound) { return null; }
        }
        if (bars.Count == 0 || inst is null) return null;

        Fundamental? fundamental = null;
        if (!inst.IsIndex)
            fundamental = await sp.GetRequiredService<FundamentalsService>().GetAsync(inst.Code, ct);
        var medians = inst.Sector is { } s ? u.SectorMedians.GetValueOrDefault(s) : null;
        var stored = await LoadStoredForAsync(sp.GetRequiredService<SumireDbContext>(), inst.Code, ct);
        var a = UniverseBuilder.Analyze(inst, bars, u.Ihsg?.Bars, fundamental, medians, u.Metrics, stored.Forecasts, stored.News, SignalWeights.Default);
        cache.Set(key, a, TimeSpan.FromHours(6));
        return a;
    }

    async Task<(List<Forecast> Forecasts, List<NewsInputs> News)> LoadStoredForAsync(SumireDbContext db, string code, CancellationToken ct)
    {
        if (!Db.IsConfigured) return ([], []);
        try
        {
            var since = calendar.TodayWib.AddDays(-10);
            var forecasts = await db.Forecasts.AsNoTracking()
                .Where(f => f.Code == code && f.Model != BaselineForecaster.ModelName && f.AsOf >= since).ToListAsync(ct);
            var newsSince = clock.GetUtcNow().AddDays(-SignalScorer.NewsDecayDays).ToUniversalTime();
            var now = clock.GetUtcNow();
            var news = (await db.Insights.AsNoTracking()
                    .Where(i => i.Kind == "stock" && i.Subject == code && i.CreatedAt >= newsSince)
                    .Select(i => new { i.Sentiment, i.Confidence, i.CreatedAt }).ToListAsync(ct))
                .Select(i => new NewsInputs(i.Sentiment, ConfidenceValue(i.Confidence), (now - i.CreatedAt).TotalDays)).ToList();
            var latest = forecasts.GroupBy(f => f.Model).SelectMany(g => g.Where(f => f.AsOf == g.Max(x => x.AsOf))).ToList();
            return (latest, news);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogDebug("Stored inputs for {Code} unavailable: {Error}", code, Db.ShortError(ex));
            return ([], []);
        }
    }
}
