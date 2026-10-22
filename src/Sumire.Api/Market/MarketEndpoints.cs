using System.Data.Common;
using Sumire.Api.Common;
using Sumire.Api.Data;
using Sumire.Api.Jobs;

namespace Sumire.Api.Market;

public static class MarketEndpoints
{
    public sealed record InstrumentDto(string Code, string Name, string? Sector, string? Industry, string? Board, bool IsIndex,
        string[] IndexMemberships, string? GroupName, bool Core)
    {
        public static InstrumentDto From(Instrument i) => new(i.Code, i.Name, i.Sector, i.Industry, i.Board, i.IsIndex, i.IndexMemberships, i.GroupName, i.Core);
    }

    /// <summary>Time: unix seconds of the bar in WIB wall-clock (lightweight-charts shows it as-is); daily bars use 00:00 of the date.</summary>
    public sealed record ChartBar(long Time, string? Date, decimal Open, decimal High, decimal Low, decimal Close, long Volume);

    public sealed record ChartResponse(string Code, string Range, string Interval, string Source, decimal? PrevClose, IReadOnlyList<ChartBar> Bars);

    static readonly Dictionary<string, string> IntradayDefault = new() { ["1d"] = "1m", ["5d"] = "5m" };
    static readonly HashSet<string> IntradayIntervals = ["1m", "2m", "5m", "15m", "30m", "60m"];
    static readonly Dictionary<string, string> YahooRange = new()
    {
        ["1m"] = "1mo", ["3m"] = "3mo", ["6m"] = "6mo", ["ytd"] = "ytd", ["1y"] = "1y", ["3y"] = "5y",
    };

    public static IEndpointRouteBuilder MapMarketEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api").AddEndpointFilter(UnavailableFilter);

        api.MapGet("/instruments", (InstrumentCatalog catalog, string? q, int? limit) =>
            catalog.Search(q, limit ?? 20).Select(InstrumentDto.From));

        api.MapGet("/instruments/{code}", (InstrumentCatalog catalog, string code) =>
            catalog.Find(code) is { } i ? Results.Ok(InstrumentDto.From(i)) : Results.Problem(statusCode: 404, title: $"Unknown code {code.ToUpperInvariant()}"));

        api.MapGet("/quotes", async (MarketDataService market, string? codes, CancellationToken ct) =>
            await market.GetQuotesAsync((codes ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries), ct));

        api.MapGet("/chart/{code}", GetChartAsync);

        api.MapGet("/status", async (MarketDataService market, MarketCalendar calendar, MarketStatus status, QuoteSubscriptions subs,
            TimeProvider clock, CancellationToken ct) =>
        {
            DateOnly? lastBar = null;
            string? dbError = null;
            if (Db.IsConfigured)
            {
                try { lastBar = await market.LastBarDateAsync("IHSG", ct); }
                catch (Exception ex) when (!ct.IsCancellationRequested) { dbError = Db.ShortError(ex); }
            }
            else dbError = Db.NotConfiguredReason;

            var today = calendar.TodayWib;
            var session = calendar.CurrentSession;
            return new
            {
                time = Wib.Iso(clock.GetUtcNow()),
                session,
                isTradingDay = calendar.IsTradingDay(today),
                holiday = calendar.HolidayOn(today)?.Name,
                nextTradingDay = calendar.NextTradingDay(today),
                lastBarDate = lastBar,
                expectedBarDate = market.LastCompletedTradingDay(),
                lastQuoteTime = status.LastQuoteTime,
                db = dbError is null ? "ok" : "error",
                dbError,
                eod = new { running = status.EodRunning, lastRun = status.LastEodRun, lastResult = status.LastEodResult, lastError = status.LastEodError, nextRun = status.NextEodRun },
                quotes = new { connections = subs.ConnectionCount, polling = IntradayQuoteJob.ShouldPoll(session, subs.ConnectionCount), lastPoll = status.LastPoll is { } p ? Wib.ToWib(p) : (DateTimeOffset?)null },
            };
        });

        return app;
    }

    static async Task<IResult> GetChartAsync(MarketDataService market, InstrumentCatalog catalog, MarketCalendar calendar,
        string code, string? range, string? interval, CancellationToken ct)
    {
        code = code.Trim().ToUpperInvariant();
        range = (range ?? "6m").ToLowerInvariant();

        if (IntradayDefault.TryGetValue(range, out var defInterval))
        {
            interval = interval is not null && IntradayIntervals.Contains(interval) ? interval : defInterval;
            var instrument = Resolve(catalog, code);
            if (instrument is null) return UnknownCode(code);
            var chart = await market.GetYahooChartAsync(instrument, interval, range, ct);
            return Results.Ok(new ChartResponse(code, range, interval, "yahoo", chart.PreviousClose, chart.Bars.Select(ToChartBarIntraday).ToList()));
        }

        if (!YahooRange.ContainsKey(range))
            return Results.Problem(statusCode: 400, title: "Invalid range", detail: "Use 1d, 5d, 1m, 3m, 6m, ytd, 1y or 3y.");
        interval = interval == "1wk" ? "1wk" : "1d";
        var today = calendar.TodayWib;
        var from = range switch
        {
            "1m" => today.AddMonths(-1),
            "3m" => today.AddMonths(-3),
            "6m" => today.AddMonths(-6),
            "ytd" => new DateOnly(today.Year, 1, 1),
            "1y" => today.AddYears(-1),
            _ => today.AddYears(-3),
        };

        List<ChartBar> bars;
        string source;
        if (Db.IsConfigured && await TryDbBarsAsync(market, code, from, ct) is { } dbBars)
        {
            if (dbBars.Bars is null) return UnknownCode(code);
            bars = dbBars.Bars.Select(b => ToChartBar(b.Date, b.Open, b.High, b.Low, b.Close, b.Volume)).ToList();
            source = "db";
        }
        else
        {
            // DB unavailable: serve straight from Yahoo so the chart still works.
            var instrument = Resolve(catalog, code);
            if (instrument is null) return UnknownCode(code);
            var chart = await market.GetYahooChartAsync(instrument, "1d", YahooRange[range], ct);
            bars = chart.Bars.Where(b => b.Date >= from).Select(b => ToChartBar(b.Date, b.Open, b.High, b.Low, b.Close, b.Volume)).ToList();
            source = "yahoo";
        }

        if (interval == "1wk") bars = Weekly(bars);
        return Results.Ok(new ChartResponse(code, range, interval, source, null, bars));
    }

    sealed record DbBars(List<DailyBar>? Bars);

    /// <summary>Null when the DB failed; DbBars(null) when the code does not exist.</summary>
    static async Task<DbBars?> TryDbBarsAsync(MarketDataService market, string code, DateOnly from, CancellationToken ct)
    {
        try
        {
            var instrument = await market.EnsureBarsAsync(code, ct);
            return new DbBars(instrument is null ? null : await market.GetDailyBarsAsync(instrument.Code, from, ct));
        }
        catch (Exception ex) when (IsDbError(ex) && !ct.IsCancellationRequested)
        {
            return null;
        }
    }

    /// <summary>Catalog entry, or CODE.JK for an unknown IDX-style code (Yahoo decides whether it exists).</summary>
    static Instrument? Resolve(InstrumentCatalog catalog, string code) =>
        catalog.Find(code) ?? (MarketDataService.IsIdxCode(code) ? new Instrument { Code = code, YahooSymbol = code + ".JK", Name = code } : null);

    static IResult UnknownCode(string code) => Results.Problem(statusCode: 404, title: $"Unknown code {code}");

    static ChartBar ToChartBar(DateOnly date, decimal o, decimal h, decimal l, decimal c, long v) =>
        new(new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).ToUnixTimeSeconds(), date.ToString("yyyy-MM-dd"), o, h, l, c, v);

    static ChartBar ToChartBarIntraday(PriceBar b)
    {
        var wib = Wib.ToWib(b.Time);
        return new(new DateTimeOffset(wib.DateTime, TimeSpan.Zero).ToUnixTimeSeconds(), null, b.Open, b.High, b.Low, b.Close, b.Volume);
    }

    public static List<ChartBar> Weekly(List<ChartBar> daily) =>
        daily.GroupBy(b =>
            {
                var d = DateOnly.Parse(b.Date!);
                return d.AddDays(-(((int)d.DayOfWeek + 6) % 7)); // Monday of the week
            })
            .Select(g => new ChartBar(g.First().Time, g.First().Date, g.First().Open, g.Max(b => b.High), g.Min(b => b.Low), g.Last().Close, g.Sum(b => b.Volume)))
            .ToList();

    static bool IsDbError(Exception ex) =>
        ex is DbException or TimeoutException or System.Net.Sockets.SocketException || ex is InvalidOperationException && ex.Source?.StartsWith("Npgsql") == true
        || ex.InnerException is { } inner && IsDbError(inner);

    /// <summary>DB or Yahoo outages become 503 ProblemDetails instead of 500s.</summary>
    static async ValueTask<object?> UnavailableFilter(EndpointFilterInvocationContext ctx, EndpointFilterDelegate next)
    {
        try
        {
            return await next(ctx);
        }
        catch (Exception ex) when (!ctx.HttpContext.RequestAborted.IsCancellationRequested
            && (IsDbError(ex) || ex is HttpRequestException or YahooException or TaskCanceledException))
        {
            if (ex is YahooException { IsNotFound: true })
                return Results.Problem(statusCode: 404, title: "Not found on Yahoo Finance");
            var what = IsDbError(ex) ? "Database" : "Market data";
            return Results.Problem(statusCode: 503, title: $"{what} unavailable", detail: IsDbError(ex) ? Db.ShortError(ex) : ex.Message);
        }
    }
}
