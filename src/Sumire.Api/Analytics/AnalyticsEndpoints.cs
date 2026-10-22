using System.Data.Common;
using Sumire.Api.Analytics.Core;
using Sumire.Api.Analytics.Services;
using Sumire.Api.Common;
using Sumire.Api.Data;
using Sumire.Api.Forecasting;
using Sumire.Api.Market;

namespace Sumire.Api.Analytics;

public static class AnalyticsEndpoints
{
    public sealed record KeyStats(
        decimal Last, decimal? MarketCap, double? Pe, double? Pb, double? DivYield, double? Roe, decimal? Eps,
        decimal? High52w, decimal? Low52w, decimal? AvgValueTraded20, decimal LotValue, decimal TickSize,
        DateOnly NextTradingDay, decimal AraNext, decimal ArbNext, double? Beta);

    public sealed record ProfileResponse(
        string Code, string Name, string? Sector, string? Industry, string? Board, string? GroupName, string[] IndexMemberships,
        bool IsIndex, string? Description, string? Website, DateOnly AsOf, KeyStats KeyStats, DateTimeOffset? FundamentalsUpdatedAt);

    public sealed record ReturnsDto(double? D1, double? W1, double? M1, double? M3, double? Ytd, double? Y1);

    public sealed record DrawdownDto(double DepthPct, DateOnly Peak, DateOnly Trough);

    /// <summary>Returns and volatility in percent.</summary>
    public sealed record RiskDto(ReturnsDto Returns, double? Beta, double? Correlation, double? Sharpe, double? VolatilityPct, DrawdownDto? MaxDrawdown,
        double? Rel3MPct, double? Rel6MPct);

    public sealed record TechnicalsResponse(string Code, DateOnly AsOf, string Source, TechnicalsSnapshot Technicals, RiskDto Risk,
        IReadOnlyDictionary<string, object?>? Series);

    public sealed record RatiosDto(decimal? MarketCap, double? Pe, double? Pb, double? Roe, double? Der, double? Npm, decimal? Eps,
        double? DivYield, double? RevenueGrowth, double? EarningsGrowth);

    public sealed record FundamentalsResponse(string Code, string? Sector, DateTimeOffset? UpdatedAt, RatiosDto? Ratios, SectorMedians? SectorMedians,
        Statements? Statements, string? StatementsError);

    public sealed record Mover(string Code, string? Name, decimal? Price, decimal? ChangePct, decimal? Value);

    public sealed record HorizonDto(int Horizon, decimal P10, decimal P50, decimal P90, double ProbUp);

    public sealed record IhsgOutlook(DateOnly AsOf, decimal Last, IReadOnlyList<HorizonDto> Horizons, double? AnnualizedVol, double? VolPercentile);

    public sealed record OverviewResponse(
        DateOnly? AsOf, string Time, Quote? Ihsg, Quote? Lq45, Regime Regime, Breadth? Breadth,
        IReadOnlyList<Mover> Gainers, IReadOnlyList<Mover> Losers, IReadOnlyList<Mover> MostActive,
        IReadOnlyList<Quote> Macro, IhsgOutlook? IhsgForecast, string MoversSource);

    public sealed record RotationDto(double RsRatio, double RsMomentum, RotationQuadrant Quadrant, DateOnly Date);

    public sealed record SectorDto(string Name, string Weighting, double? D1, double? W1, double? M1, double? M3, double? RsM1, double? RsM3,
        RotationDto? Rotation, IReadOnlyList<RotationDto> Trail, IReadOnlyList<SectorMember> Members);

    public sealed record SectorsResponse(DateOnly? AsOf, IReadOnlyList<SectorDto> Sectors);

    const int MoverCount = 5;

    public static IEndpointRouteBuilder MapAnalyticsEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api").AddEndpointFilter(UnavailableFilter);

        api.MapGet("/stocks/{code}/profile", GetProfileAsync);
        api.MapGet("/stocks/{code}/technicals", GetTechnicalsAsync);
        api.MapGet("/stocks/{code}/fundamentals", GetFundamentalsAsync);
        api.MapGet("/market/overview", GetOverviewAsync);
        api.MapGet("/market/sectors", async (AnalyticsStore store, CancellationToken ct) =>
        {
            var u = await store.GetAsync(ct);
            return new SectorsResponse(u.AsOf, u.Sectors.Select(ToDto).ToList());
        });
        api.MapGet("/screener/presets", () => new
        {
            presets = Screener.Presets.Values.Select(p => new { p.Name, p.Description }),
            numericFields = Screener.NumericFields.Keys,
            textFields = Screener.TextFields.Keys,
        });
        api.MapPost("/screener", async (AnalyticsStore store, ScreenRequest req, CancellationToken ct) =>
        {
            var u = await store.GetAsync(ct);
            try { return Results.Ok(Screener.Run(u, req)); }
            catch (ScreenerException ex) { return Results.Problem(statusCode: 400, title: "Invalid screen", detail: ex.Message); }
        });
        return app;
    }

    static async Task<IResult> GetProfileAsync(AnalyticsStore store, FundamentalsService fundamentals, IdxRules rules, MarketCalendar calendar,
        string code, CancellationToken ct)
    {
        if (await store.GetStockAsync(code, ct) is not { } s) return UnknownCode(code);
        var i = s.Instrument;
        var f = i.IsIndex ? null : s.Fundamental ?? await fundamentals.GetAsync(i.Code, ct);
        var profile = i.IsIndex ? null : await fundamentals.GetProfileAsync(i.Code, ct);
        var next = calendar.NextTradingDay(s.AsOf);
        var (ara, arb) = rules.AraArb(s.Last, next);
        var t = s.Technicals;
        return Results.Ok(new ProfileResponse(i.Code, i.Name, i.Sector, i.Industry, i.Board, i.GroupName, i.IndexMemberships, i.IsIndex,
            profile?.Description, profile?.Website, s.AsOf,
            new KeyStats(s.Last, f?.MarketCap, f?.Pe, f?.Pb, f?.DivYield, f?.Roe, f?.Eps, t.High52w, t.Low52w, t.AvgValueTraded20,
                rules.LotValue(s.Last), rules.TickSize(s.Last), next, ara, arb, R(s.Risk.Beta, 3)),
            f?.UpdatedAt is { } u ? Wib.ToWib(u) : null));
    }

    static async Task<IResult> GetTechnicalsAsync(AnalyticsStore store, string code, string? series, CancellationToken ct)
    {
        if (await store.GetStockAsync(code, ct) is not { } s) return UnknownCode(code);
        var u = await store.GetAsync(ct);
        var source = u.Find(s.Code) is not null ? u.Source : "on-demand";
        return Results.Ok(new TechnicalsResponse(s.Code, s.AsOf, source, s.Technicals, Risk(s), Series(s, series)));
    }

    static RiskDto Risk(StockAnalytics s)
    {
        var r = s.Risk;
        return new RiskDto(
            new ReturnsDto(P(r.Returns.D1), P(r.Returns.W1), P(r.Returns.M1), P(r.Returns.M3), P(r.Returns.Ytd), P(r.Returns.Y1)),
            R(r.Beta, 3), R(r.Correlation, 3), R(r.Sharpe, 2), P(r.Volatility),
            r.MaxDrawdown is { } d ? new DrawdownDto(Math.Round(d.Depth * 100, 2), d.Peak, d.Trough) : null,
            P(s.Rel3M), P(s.Rel6M));
    }

    public static readonly string[] SeriesNames = ["sma20", "sma50", "sma200", "ema20", "bb", "rsi", "macd", "vol"];

    /// <summary>Chart overlays aligned with <c>dates</c>; null when not requested.</summary>
    public static Dictionary<string, object?>? Series(StockAnalytics s, string? names)
    {
        if (string.IsNullOrWhiteSpace(names)) return null;
        var wanted = names.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(n => n.ToLowerInvariant()).ToHashSet();
        var t = TechnicalsCalculator.Series(s.Bars);
        var result = new Dictionary<string, object?> { ["dates"] = t.Dates.Select(d => d.ToString("yyyy-MM-dd")).ToList() };
        static double?[] Rd(double?[] x) => x.Select(v => v is { } d && double.IsFinite(d) ? Math.Round(d, 4) : (double?)null).ToArray();
        if (wanted.Contains("sma20")) result["sma20"] = Rd(t.Sma20);
        if (wanted.Contains("sma50")) result["sma50"] = Rd(t.Sma50);
        if (wanted.Contains("sma200")) result["sma200"] = Rd(t.Sma200);
        if (wanted.Contains("ema20")) result["ema20"] = Rd(t.Ema20);
        if (wanted.Contains("bb")) { result["bbUpper"] = Rd(t.BbUpper); result["bbLower"] = Rd(t.BbLower); }
        if (wanted.Contains("rsi")) result["rsi"] = Rd(t.Rsi);
        if (wanted.Contains("macd")) result["macdHist"] = Rd(t.MacdHist);
        if (wanted.Contains("vol"))
        {
            result["volume"] = s.Bars.Select(b => b.Volume).ToArray();
            result["volumeSma20"] = s.Bars.Select((_, i) => i >= 19 ? Math.Round(s.Bars.Skip(i - 19).Take(20).Average(b => (double)b.Volume)) : (double?)null).ToArray();
        }
        return result;
    }

    static async Task<IResult> GetFundamentalsAsync(AnalyticsStore store, FundamentalsService fundamentals, InstrumentCatalog catalog,
        string code, CancellationToken ct)
    {
        code = code.Trim().ToUpperInvariant();
        var inst = catalog.Find(code);
        if (inst is null) return UnknownCode(code);
        if (inst.IsIndex) return Results.Problem(statusCode: 404, title: $"{code} is an index and has no fundamentals");

        var u = await store.GetAsync(ct);
        var f = await fundamentals.GetAsync(code, ct);
        Statements? statements = null;
        string? error = null;
        try { statements = await fundamentals.GetStatementsAsync(code, ct); }
        catch (Exception ex) when (ex is HttpRequestException or YahooException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            error = "Statements unavailable from Yahoo right now";
        }
        var ratios = f is null ? null : new RatiosDto(f.MarketCap, f.Pe, f.Pb, f.Roe, f.Der, f.Npm, f.Eps, f.DivYield, f.RevenueGrowth, f.EarningsGrowth);
        return Results.Ok(new FundamentalsResponse(code, inst.Sector, f?.UpdatedAt is { } t ? Wib.ToWib(t) : null, ratios,
            inst.Sector is { } sec ? u.SectorMedians.GetValueOrDefault(sec) : null, statements, error));
    }

    static async Task<IResult> GetOverviewAsync(AnalyticsStore store, MarketDataService market, InstrumentCatalog catalog, TimeProvider clock,
        ILoggerFactory loggers, CancellationToken ct)
    {
        var u = await store.GetAsync(ct);
        var macroCodes = catalog.Tape.Where(i => i.Code is not ("IHSG" or "LQ45")).OrderBy(i => i.Sector).ThenBy(i => i.Code).Select(i => i.Code).ToList();
        var stockCodes = u.Stocks.Keys.ToList();

        IReadOnlyList<Quote> quotes = [];
        try { quotes = await market.GetQuotesAsync(stockCodes.Concat(macroCodes).Append("IHSG").Append("LQ45"), ct); }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            loggers.CreateLogger("Analytics").LogWarning("Overview quotes unavailable: {Error}", Db.ShortError(ex));
        }
        var byCode = quotes.ToDictionary(q => q.Code);

        var stockQuotes = stockCodes.Select(c => byCode.GetValueOrDefault(c)).OfType<Quote>().Where(q => q.ChangePct is not null).ToList();
        var moversSource = "quotes";
        List<Mover> movers;
        if (stockQuotes.Count > 0)
            movers = stockQuotes.Select(q => new Mover(q.Code, q.Name, q.Price, q.ChangePct is { } p ? Math.Round(p, 2) : null, q.Value)).ToList();
        else
        {
            moversSource = "snapshot";
            movers = u.Stocks.Values.Select(s => new Mover(s.Code, s.Instrument.Name, s.Last,
                s.Technicals.ChangePct is { } p ? Math.Round((decimal)p, 2) : null, s.Technicals.ValueTraded)).ToList();
        }

        IhsgOutlook? outlook = u.Ihsg?.Baseline is { } b
            ? new IhsgOutlook(b.AsOf, b.LastClose, b.Horizons.Select(h => new HorizonDto(h.Horizon, h.P10, h.P50, h.P90, Math.Round(h.ProbUp, 4))).ToList(),
                R(u.Ihsg.Garch?.AnnualizedVol, 4), R(u.Ihsg.Garch?.VolPercentile, 1))
            : null;

        return Results.Ok(new OverviewResponse(
            u.AsOf, Wib.Iso(clock.GetUtcNow()),
            byCode.GetValueOrDefault("IHSG"), byCode.GetValueOrDefault("LQ45"),
            u.Regime, u.Breadth,
            movers.Where(m => m.ChangePct > 0).OrderByDescending(m => m.ChangePct).Take(MoverCount).ToList(),
            movers.Where(m => m.ChangePct < 0).OrderBy(m => m.ChangePct).Take(MoverCount).ToList(),
            movers.Where(m => m.Value is not null).OrderByDescending(m => m.Value).Take(MoverCount).ToList(),
            macroCodes.Select(c => byCode.GetValueOrDefault(c)).OfType<Quote>().ToList(),
            outlook, moversSource));
    }

    static SectorDto ToDto(SectorIndex s) => new(s.Name, s.Weighting, s.D1, s.W1, s.M1, s.M3, s.RsM1, s.RsM3,
        s.Rotation is { } r ? Rot(r) : null, s.Trail.Select(Rot).ToList(), s.Members);

    static RotationDto Rot(RotationPoint p) => new(Math.Round(p.RsRatio, 2), Math.Round(p.RsMomentum, 2), p.Quadrant, p.Date);

    static double? P(double? fraction) => fraction is { } f && double.IsFinite(f) ? Math.Round(f * 100, 2) : null;

    static double? R(double? v, int digits) => v is { } x && double.IsFinite(x) ? Math.Round(x, digits) : null;

    public static IResult UnknownCode(string code) => Results.Problem(statusCode: 404, title: $"Unknown code {code.Trim().ToUpperInvariant()}");

    static bool IsDbError(Exception ex) =>
        ex is DbException or TimeoutException or System.Net.Sockets.SocketException || ex is InvalidOperationException && ex.Source?.StartsWith("Npgsql") == true
        || ex.InnerException is { } inner && IsDbError(inner);

    /// <summary>DB or Yahoo outages become 503 ProblemDetails instead of 500s.</summary>
    public static async ValueTask<object?> UnavailableFilter(EndpointFilterInvocationContext ctx, EndpointFilterDelegate next)
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
