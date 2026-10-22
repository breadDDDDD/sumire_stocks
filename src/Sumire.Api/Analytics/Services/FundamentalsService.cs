using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Sumire.Api.Data;
using Sumire.Api.Market;

namespace Sumire.Api.Analytics.Services;

public sealed record StatementPeriod(string EndDate, IReadOnlyDictionary<string, double> Items);

public sealed record Statements(
    IReadOnlyList<StatementPeriod> IncomeAnnual,
    IReadOnlyList<StatementPeriod> IncomeQuarterly,
    IReadOnlyList<StatementPeriod> BalanceSheet,
    IReadOnlyList<StatementPeriod> Cashflow);

public sealed record CompanyProfile(string? Description, string? Website, string? YahooSector, string? YahooIndustry, int? Employees);

/// <summary>Key ratios (stored weekly in <c>fundamentals</c>), statements and company profile from Yahoo quoteSummary.</summary>
public sealed class FundamentalsService(
    SumireDbContext db,
    IMarketDataProvider yahoo,
    InstrumentCatalog catalog,
    IMemoryCache cache,
    TimeProvider clock,
    ILogger<FundamentalsService> logger)
{
    public static readonly TimeSpan MaxAge = TimeSpan.FromDays(7);
    static readonly TimeSpan CacheTtl = TimeSpan.FromHours(24);
    public static readonly string[] RatioModules = ["financialData", "defaultKeyStatistics", "summaryDetail", "price"];
    static readonly string[] StatementModules = ["incomeStatementHistory", "incomeStatementHistoryQuarterly", "balanceSheetHistory", "cashflowStatementHistory"];

    /// <summary>Stored ratios, refreshed from Yahoo when missing or older than 7 days. Works without the DB.</summary>
    public async Task<Fundamental?> GetAsync(string code, CancellationToken ct = default)
    {
        if (cache.TryGetValue($"fund:{code}", out Fundamental? cached)) return cached;
        Fundamental? row = null;
        var dbOk = Db.IsConfigured;
        if (dbOk)
        {
            try { row = await db.Fundamentals.AsNoTracking().FirstOrDefaultAsync(f => f.Code == code, ct); }
            catch (Exception ex) when (!ct.IsCancellationRequested) { dbOk = false; logger.LogDebug("Fundamentals DB read failed: {Error}", Db.ShortError(ex)); }
        }
        if (row is null || clock.GetUtcNow() - row.UpdatedAt > MaxAge)
        {
            try { row = await RefreshAsync(code, save: dbOk, ct) ?? row; }
            catch (Exception ex) when (ex is HttpRequestException or YahooException or TaskCanceledException && !ct.IsCancellationRequested)
            {
                logger.LogWarning("Fundamentals for {Code} unavailable: {Error}", code, ex.Message);
            }
        }
        cache.Set($"fund:{code}", row, row is null ? TimeSpan.FromMinutes(10) : TimeSpan.FromHours(6));
        return row;
    }

    /// <summary>Fetches ratios from Yahoo and (optionally) upserts the row. Null for unknown codes.</summary>
    public async Task<Fundamental?> RefreshAsync(string code, bool save = true, CancellationToken ct = default)
    {
        if (catalog.Find(code) is not { IsIndex: false } inst) return null;
        var summary = await yahoo.GetQuoteSummaryAsync(inst.YahooSymbol, RatioModules, ct);
        var f = Parse(code, summary, clock.GetUtcNow());
        if (save)
        {
            try
            {
                var existing = await db.Fundamentals.FirstOrDefaultAsync(x => x.Code == code, ct);
                if (existing is null) db.Fundamentals.Add(f);
                else db.Entry(existing).CurrentValues.SetValues(f);
                await db.SaveChangesAsync(ct);
                db.ChangeTracker.Clear();
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                logger.LogWarning("Saving fundamentals for {Code} failed: {Error}", code, Db.ShortError(ex));
            }
        }
        cache.Set($"fund:{code}", f, TimeSpan.FromHours(6));
        return f;
    }

    public async Task<Statements?> GetStatementsAsync(string code, CancellationToken ct = default)
    {
        if (catalog.Find(code) is not { IsIndex: false } inst) return null;
        return await cache.GetOrCreateAsync($"statements:{code}", async e =>
        {
            e.AbsoluteExpirationRelativeToNow = CacheTtl;
            return ParseStatements(await yahoo.GetQuoteSummaryAsync(inst.YahooSymbol, StatementModules, ct));
        });
    }

    /// <summary>Business summary and website; null when Yahoo has nothing or is down (never throws).</summary>
    public async Task<CompanyProfile?> GetProfileAsync(string code, CancellationToken ct = default)
    {
        if (catalog.Find(code) is not { IsIndex: false } inst) return null;
        if (cache.TryGetValue($"profile:{code}", out CompanyProfile? p)) return p;
        try
        {
            var s = await yahoo.GetQuoteSummaryAsync(inst.YahooSymbol, ["assetProfile"], ct);
            p = s.TryGetProperty("assetProfile", out var a)
                ? new CompanyProfile(Str(a, "longBusinessSummary"), Str(a, "website"), Str(a, "sector"), Str(a, "industry"),
                    YahooParser.Raw(a, "fullTimeEmployees") is { } emp ? (int)emp : null)
                : null;
            cache.Set($"profile:{code}", p, CacheTtl);
            return p;
        }
        catch (Exception ex) when (ex is HttpRequestException or YahooException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            logger.LogWarning("Profile for {Code} unavailable: {Error}", code, ex.Message);
            cache.Set($"profile:{code}", (CompanyProfile?)null, TimeSpan.FromMinutes(10));
            return null;
        }
    }

    /// <summary>Maps quoteSummary financialData/defaultKeyStatistics/summaryDetail/price to a row.</summary>
    public static Fundamental Parse(string code, JsonElement s, DateTimeOffset now)
    {
        var fin = Module(s, "financialData");
        var stats = Module(s, "defaultKeyStatistics");
        var detail = Module(s, "summaryDetail");
        var price = Module(s, "price");
        double? R(JsonElement? m, string name) => m is { } e ? YahooParser.Raw(e, name) : null;

        var cap = R(price, "marketCap") ?? R(detail, "marketCap");
        var eps = R(stats, "trailingEps");
        return new Fundamental
        {
            Code = code,
            UpdatedAt = now.ToUniversalTime(),
            MarketCap = cap is { } c ? (decimal)c : null,
            Pe = R(detail, "trailingPE"),
            Pb = R(stats, "priceToBook"),
            Roe = R(fin, "returnOnEquity"),
            // Yahoo reports debt/equity in percent.
            Der = R(fin, "debtToEquity") is { } d ? d / 100 : null,
            Npm = R(fin, "profitMargins") ?? R(stats, "profitMargins"),
            Eps = eps is { } e ? Math.Round((decimal)e, 4) : null,
            DivYield = R(detail, "dividendYield") ?? R(detail, "trailingAnnualDividendYield"),
            RevenueGrowth = R(fin, "revenueGrowth"),
            EarningsGrowth = R(fin, "earningsGrowth"),
        };
    }

    public static Statements ParseStatements(JsonElement s) => new(
        Periods(s, "incomeStatementHistory"),
        Periods(s, "incomeStatementHistoryQuarterly"),
        Periods(s, "balanceSheetHistory"),
        Periods(s, "cashflowStatementHistory"));

    /// <summary>Each module wraps one array of periods; keeps the non-zero numeric items.</summary>
    static List<StatementPeriod> Periods(JsonElement s, string module)
    {
        var result = new List<StatementPeriod>();
        if (Module(s, module) is not { } m) return result;
        foreach (var prop in m.EnumerateObject())
        {
            if (prop.Value.ValueKind != JsonValueKind.Array) continue;
            foreach (var period in prop.Value.EnumerateArray())
            {
                var end = period.TryGetProperty("endDate", out var ed) && ed.TryGetProperty("fmt", out var fmt) ? fmt.GetString() : null;
                if (end is null) continue;
                var items = new Dictionary<string, double>();
                foreach (var item in period.EnumerateObject())
                    if (item.Name is not ("endDate" or "maxAge") && YahooParser.Raw(period, item.Name) is { } v && v != 0)
                        items[item.Name] = v;
                result.Add(new StatementPeriod(end, items));
            }
        }
        return result;
    }

    static JsonElement? Module(JsonElement s, string name) =>
        s.TryGetProperty(name, out var m) && m.ValueKind == JsonValueKind.Object ? m : null;

    static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
