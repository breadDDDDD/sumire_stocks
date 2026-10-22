using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Npgsql;
using NpgsqlTypes;
using Sumire.Api.Common;
using Sumire.Api.Data;

namespace Sumire.Api.Market;

/// <summary>Daily bars (DB, fetched on demand), intraday bars and quotes (Yahoo, cached).</summary>
public sealed partial class MarketDataService(
    SumireDbContext db,
    IMarketDataProvider provider,
    InstrumentCatalog catalog,
    MarketCalendar calendar,
    MarketStatus status,
    IMemoryCache cache,
    ILogger<MarketDataService> logger)
{
    public const int HistoryYears = 3;
    static readonly TimeSpan LiveQuoteTtl = TimeSpan.FromSeconds(60);
    static readonly TimeSpan CloseQuoteTtl = TimeSpan.FromMinutes(10);
    static readonly TimeSpan EnsureTtl = TimeSpan.FromMinutes(10);
    static readonly TimeOnly EodReady = new(16, 15);

    /// <summary>The latest trading day whose daily bar is final (today after 16:15 WIB on a trading day).</summary>
    public DateOnly LastCompletedTradingDay()
    {
        var now = calendar.NowWib;
        var today = DateOnly.FromDateTime(now.DateTime);
        return calendar.IsTradingDay(today) && TimeOnly.FromDateTime(now.DateTime) >= EodReady ? today : calendar.PreviousTradingDay(today);
    }

    public Task<DateOnly?> LastBarDateAsync(string code, CancellationToken ct = default) =>
        db.DailyBars.Where(b => b.Code == code).MaxAsync(b => (DateOnly?)b.Date, ct);

    /// <summary>Fetches missing daily bars from Yahoo: 3 years when there is no history, else the last ~10 days.</summary>
    public async Task<int> SyncBarsAsync(Instrument instrument, CancellationToken ct = default)
    {
        var target = LastCompletedTradingDay();
        var last = await LastBarDateAsync(instrument.Code, ct);
        if (last >= target) return 0;

        var from = last is { } l ? l.AddDays(-10) : target.AddYears(-HistoryYears).AddDays(-7);
        var chart = await provider.GetChartAsync(instrument.YahooSymbol, "1d", from: new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero), ct: ct);
        // Drop today's still-forming bar.
        var bars = chart.Bars.Where(b => b.Date <= target).ToList();
        return await UpsertBarsAsync(instrument.Code, bars, ct);
    }

    /// <summary>One INSERT … ON CONFLICT per symbol using array parameters.</summary>
    public async Task<int> UpsertBarsAsync(string code, IReadOnlyList<PriceBar> bars, CancellationToken ct = default)
    {
        if (bars.Count == 0) return 0;
        // One bar per date (Yahoo occasionally repeats the last one).
        var unique = bars.GroupBy(b => b.Date).Select(g => g.Last()).ToList();
        const string sql = """
            INSERT INTO sumire.daily_bars (code, date, open, high, low, close, adj_close, volume)
            SELECT @code, d, o, h, l, c, a, v
            FROM unnest(@dates, @opens, @highs, @lows, @closes, @adjs, @vols) AS t(d, o, h, l, c, a, v)
            ON CONFLICT (code, date) DO UPDATE SET
                open = excluded.open, high = excluded.high, low = excluded.low, close = excluded.close,
                adj_close = excluded.adj_close, volume = excluded.volume
            """;
        return await db.Database.ExecuteSqlRawAsync(sql, [
            new NpgsqlParameter("code", code),
            new NpgsqlParameter("dates", NpgsqlDbType.Array | NpgsqlDbType.Date) { Value = unique.Select(b => b.Date).ToArray() },
            new NpgsqlParameter("opens", NpgsqlDbType.Array | NpgsqlDbType.Numeric) { Value = unique.Select(b => b.Open).ToArray() },
            new NpgsqlParameter("highs", NpgsqlDbType.Array | NpgsqlDbType.Numeric) { Value = unique.Select(b => b.High).ToArray() },
            new NpgsqlParameter("lows", NpgsqlDbType.Array | NpgsqlDbType.Numeric) { Value = unique.Select(b => b.Low).ToArray() },
            new NpgsqlParameter("closes", NpgsqlDbType.Array | NpgsqlDbType.Numeric) { Value = unique.Select(b => b.Close).ToArray() },
            new NpgsqlParameter("adjs", NpgsqlDbType.Array | NpgsqlDbType.Numeric) { Value = unique.Select(b => b.AdjClose).ToArray() },
            new NpgsqlParameter("vols", NpgsqlDbType.Array | NpgsqlDbType.Bigint) { Value = unique.Select(b => b.Volume).ToArray() },
        ], ct);
    }

    [GeneratedRegex("^[A-Z0-9]{4}$")]
    private static partial Regex IdxCodeRegex();

    public static bool IsIdxCode(string code) => IdxCodeRegex().IsMatch(code);

    /// <summary>
    /// Makes sure a stock has up-to-date daily bars, fetching 3 years for a non-core stock on first use.
    /// Unknown 4-letter codes are looked up on Yahoo as CODE.JK and added as non-core instruments.
    /// Returns null when the code does not exist.
    /// </summary>
    public async Task<Instrument?> EnsureBarsAsync(string code, CancellationToken ct = default)
    {
        code = code.Trim().ToUpperInvariant();
        var instrument = catalog.Find(code);
        if (instrument is null && !IdxCodeRegex().IsMatch(code)) return null;
        if (cache.TryGetValue($"ensure:{code}", out bool _) && instrument is not null) return instrument;

        if (instrument is null)
        {
            var symbol = code + ".JK";
            ChartData probe;
            try
            {
                probe = await provider.GetChartAsync(symbol, "1d", range: "5d", ct: ct);
            }
            catch (YahooException ex) when (ex.IsNotFound)
            {
                return null;
            }
            if (probe.Bars.Count == 0) return null;
            instrument = new Instrument { Code = code, YahooSymbol = symbol, Name = probe.Name ?? code, Core = false, Active = true };
            if (!await db.Instruments.AnyAsync(i => i.Code == code, ct))
            {
                db.Instruments.Add(instrument);
                await db.SaveChangesAsync(ct);
            }
            catalog.Add(instrument);
        }

        await SyncBarsAsync(instrument, ct);
        cache.Set($"ensure:{code}", true, EnsureTtl);
        return instrument;
    }

    public Task<List<DailyBar>> GetDailyBarsAsync(string code, DateOnly from, CancellationToken ct = default) =>
        db.DailyBars.AsNoTracking().Where(b => b.Code == code && b.Date >= from).OrderBy(b => b.Date).ToListAsync(ct);

    /// <summary>Yahoo chart cached 60 s (intraday, or daily when the DB is unavailable).</summary>
    public async Task<ChartData> GetYahooChartAsync(Instrument instrument, string interval, string range, CancellationToken ct = default)
    {
        var key = $"chart:{instrument.YahooSymbol}:{interval}:{range}";
        if (cache.TryGetValue(key, out ChartData? cached) && cached is not null) return cached;
        var chart = await provider.GetChartAsync(instrument.YahooSymbol, interval, range: range, ct: ct);
        cache.Set(key, chart, interval.EndsWith('m') ? TimeSpan.FromSeconds(60) : TimeSpan.FromMinutes(10));
        return chart;
    }

    /// <summary>
    /// Quotes for codes: cached first; then delayed Yahoo quotes while the market is open, or the last
    /// daily close from the DB when closed (each falls back to the other). Unknown codes are omitted.
    /// </summary>
    public async Task<IReadOnlyList<Quote>> GetQuotesAsync(IEnumerable<string> codes, CancellationToken ct = default)
    {
        var wanted = QuoteSubscriptions.Normalize(codes).Take(QuoteSubscriptions.MaxCodesPerConnection).ToList();
        var result = new Dictionary<string, Quote>();
        foreach (var code in wanted)
            if (cache.TryGetValue($"quote:{code}", out Quote? q) && q is not null) result[code] = q;

        var missing = wanted.Where(c => !result.ContainsKey(c) && catalog.Find(c) is not null).ToList();
        if (missing.Count > 0)
        {
            var open = calendar.IsOpenNow;
            var first = open ? (Func<List<string>, CancellationToken, Task<IReadOnlyList<Quote>>>)TryLiveAsync : TryCloseAsync;
            var second = open ? (Func<List<string>, CancellationToken, Task<IReadOnlyList<Quote>>>)TryCloseAsync : TryLiveAsync;
            foreach (var q in await first(missing, ct)) result[q.Code] = q;
            missing = missing.Where(c => !result.ContainsKey(c)).ToList();
            if (missing.Count > 0)
                foreach (var q in await second(missing, ct)) result[q.Code] = q;
        }
        return wanted.Where(result.ContainsKey).Select(c => result[c]).ToList();
    }

    async Task<IReadOnlyList<Quote>> TryLiveAsync(List<string> codes, CancellationToken ct)
    {
        try { return await RefreshLiveQuotesAsync(codes, ct); }
        catch (Exception ex) when (ex is HttpRequestException or YahooException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            logger.LogWarning("Live quotes unavailable: {Error}", ex.Message);
            return [];
        }
    }

    async Task<IReadOnlyList<Quote>> TryCloseAsync(List<string> codes, CancellationToken ct)
    {
        if (!Db.IsConfigured) return [];
        try { return await CloseQuotesAsync(codes, ct); }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning("Close quotes unavailable: {Error}", Db.ShortError(ex));
            return [];
        }
    }

    /// <summary>Fetches delayed quotes from Yahoo and caches them for 60 s.</summary>
    public async Task<IReadOnlyList<Quote>> RefreshLiveQuotesAsync(IEnumerable<string> codes, CancellationToken ct = default)
    {
        var instruments = codes.Select(catalog.Find).OfType<Instrument>().ToList();
        if (instruments.Count == 0) return [];
        var raw = await provider.GetQuotesAsync(instruments.Select(i => i.YahooSymbol), ct);
        var quotes = new List<Quote>();
        foreach (var y in raw)
        {
            if (catalog.FindBySymbol(y.Symbol) is not { } inst || y.Price is null) continue;
            var q = ToQuote(inst, y);
            cache.Set($"quote:{inst.Code}", q, LiveQuoteTtl);
            quotes.Add(q);
            if (q.Time is { } t && (status.LastQuoteTime is null || t > status.LastQuoteTime)) status.LastQuoteTime = t;
        }
        return quotes;
    }

    public static Quote ToQuote(Instrument inst, YahooQuote y)
    {
        var change = y.Change ?? (y.Price - y.PrevClose);
        var pct = y.ChangePct ?? (y.PrevClose is > 0 && change is not null ? change / y.PrevClose * 100 : null);
        return new Quote(inst.Code, inst.Name, y.Price, Round(change), Round(pct), y.PrevClose, y.Open, y.High, y.Low, y.Volume,
            y.Price is { } p && y.Volume is { } v && !inst.IsIndex ? p * v : null,
            y.Time is { } t ? Wib.ToWib(t) : null, "live");
    }

    /// <summary>Close-based quotes from the last two daily bars, cached 10 minutes.</summary>
    async Task<IReadOnlyList<Quote>> CloseQuotesAsync(List<string> codes, CancellationToken ct)
    {
        var since = calendar.TodayWib.AddDays(-20);
        var bars = await db.DailyBars.AsNoTracking()
            .Where(b => codes.Contains(b.Code) && b.Date >= since)
            .OrderByDescending(b => b.Date)
            .ToListAsync(ct);
        var quotes = new List<Quote>();
        foreach (var g in bars.GroupBy(b => b.Code))
        {
            var last2 = g.Take(2).ToList();
            if (catalog.Find(g.Key) is not { } inst) continue;
            var q = CloseQuote(inst, last2[0], last2.ElementAtOrDefault(1));
            cache.Set($"quote:{inst.Code}", q, CloseQuoteTtl);
            quotes.Add(q);
        }
        return quotes;
    }

    public static Quote CloseQuote(Instrument inst, DailyBar last, DailyBar? prev)
    {
        decimal? change = prev is null ? null : last.Close - prev.Close;
        decimal? pct = prev is { Close: > 0 } ? change / prev.Close * 100 : null;
        var time = new DateTimeOffset(last.Date.ToDateTime(new TimeOnly(16, 0)), TimeSpan.FromHours(7));
        return new Quote(inst.Code, inst.Name, last.Close, Round(change), Round(pct), prev?.Close, last.Open, last.High, last.Low, last.Volume,
            inst.IsIndex ? null : last.Close * last.Volume, time, "close");
    }

    static decimal? Round(decimal? d) => d is null ? null : Math.Round(d.Value, 4);
}
