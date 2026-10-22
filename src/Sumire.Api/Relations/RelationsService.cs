using Sumire.Api.Data;
using Sumire.Api.Market;

namespace Sumire.Api.Relations;

/// <summary>Today's move of one instrument. ChgPct is in percent; null when no quote is available.</summary>
public sealed record MoveDto(string Code, string? Name, decimal? Price, decimal? ChgPct);

public sealed record GroupDto(string Name, string? Note, IReadOnlyList<MoveDto> Peers);

public sealed record FactorDto(string Factor, string Kind, string Direction, string? Note, MoveDto? Proxy, string Source = "curated");

public sealed record StageDto(string Stage, bool Self, IReadOnlyList<MoveDto> Tickers);

public sealed record SupplyChainDto(string Name, IReadOnlyList<StageDto> Stages);

public sealed record CorrelatedDto(string Code, string? Name, double Rho, int N, decimal? ChgPct, string Source = "statistical");

public sealed record LeadLagDto(string Proxy, string? Name, double Rho, int N, double TStat, decimal? ChgPct, string Source = "statistical");

public sealed record StockRelations(
    string Code,
    string? Name,
    string? Sector,
    MoveDto? Quote,
    IReadOnlyList<GroupDto> Groups,
    IReadOnlyList<FactorDto> Factors,
    IReadOnlyList<SupplyChainDto> SupplyChain,
    IReadOnlyList<CorrelatedDto> Correlated,
    IReadOnlyList<LeadLagDto> LeadLag,
    DateOnly? StatsAsOf,
    string? MapUpdated);

public sealed record FactorEntryDto(string Direction, string? Note, IReadOnlyList<MoveDto> Tickers, IReadOnlyList<string> Sectors);

public sealed record FollowerDto(string Code, string? Name, double Rho, int N, decimal? ChgPct);

public sealed record FactorRelations(string Factor, string? Kind, MoveDto? Proxy, IReadOnlyList<FactorEntryDto> Entries,
    IReadOnlyList<FollowerDto> LeadLag, DateOnly? StatsAsOf);

/// <summary>
/// Curated relations plus statistical links, with today's moves along each link. Used by the REL screen
/// and by research fact packets.
/// </summary>
public sealed class RelationsService(
    RelationsMapProvider maps,
    RelationsStatsStore stats,
    InstrumentCatalog catalog,
    MarketDataService market,
    IServiceProvider services,
    ILogger<RelationsService> logger)
{
    static readonly SemaphoreSlim ComputeGate = new(1, 1);
    static DateTimeOffset _lastComputeAttempt = DateTimeOffset.MinValue;

    public RelationsMap Map => maps.Current;

    public StatisticalSnapshot Stats => stats.Snapshot;

    /// <summary>Relations of a stock; null when the code is neither in the catalog nor in the relations map.</summary>
    public async Task<StockRelations?> GetAsync(string code, CancellationToken ct = default)
    {
        code = code.Trim().ToUpperInvariant();
        var map = maps.Current;
        var instrument = catalog.Find(code);
        if (instrument is null && !map.Mentions(code)) return null;

        await EnsureStatsAsync(ct);
        var snap = stats.Snapshot;

        var groups = map.GroupsFor(code).ToList();
        var factors = map.FactorsFor(code, instrument?.Sector).ToList();
        var chains = map.SupplyChainsFor(code).ToList();
        var correlated = snap.Correlated.GetValueOrDefault(code) ?? [];
        var leadLag = snap.LeadLag.GetValueOrDefault(code) ?? [];

        var wanted = new[] { code }
            .Concat(groups.SelectMany(g => g.Tickers))
            .Concat(factors.Select(f => f.ProxyCode).OfType<string>())
            .Concat(correlated.Select(c => c.Code))
            .Concat(leadLag.Select(l => l.Proxy))
            .Concat(chains.SelectMany(c => c.Steps).SelectMany(s => s.Tickers));
        var moves = await MovesAsync(wanted, ct);

        return new StockRelations(
            code,
            instrument?.Name,
            instrument?.Sector,
            moves.GetValueOrDefault(code),
            groups.Select(g => new GroupDto(g.Name, g.Note, g.Tickers.Where(t => t != code).Select(t => Move(moves, t)).ToList())).ToList(),
            factors.Select(f => new FactorDto(f.Factor, f.Kind, f.Direction, f.Note, f.ProxyCode is null ? null : Move(moves, f.ProxyCode))).ToList(),
            chains.Select(c => new SupplyChainDto(c.Name,
                c.Steps.Select(s => new StageDto(s.Stage, s.Tickers.Contains(code), s.Tickers.Select(t => Move(moves, t)).ToList())).ToList())).ToList(),
            correlated.Select(c => new CorrelatedDto(c.Code, catalog.Find(c.Code)?.Name, c.Rho, c.N, moves.GetValueOrDefault(c.Code)?.ChgPct)).ToList(),
            leadLag.Select(l => new LeadLagDto(l.Proxy, catalog.Find(l.Proxy)?.Name, l.Rho, l.N, l.TStat, moves.GetValueOrDefault(l.Proxy)?.ChgPct)).ToList(),
            snap.AsOf == default ? null : snap.AsOf,
            map.Updated);
    }

    /// <summary>
    /// Tickers linked to a factor (curated entries) and stocks whose moves follow its proxy (statistical).
    /// <paramref name="factor"/> is a factor name (COAL, BI_RATE) or a proxy code (GOLD, SPX). Null when unknown.
    /// </summary>
    public async Task<FactorRelations?> GetFactorAsync(string factor, CancellationToken ct = default)
    {
        factor = factor.Trim().ToUpperInvariant();
        var entries = maps.Current.Factor(factor);
        var proxy = entries.Select(e => e.Link.ProxyCode).FirstOrDefault(p => p is not null)
            ?? (StatisticalLinks.ProxyCodes.Contains(factor) ? factor : null);

        await EnsureStatsAsync(ct);
        var snap = stats.Snapshot;
        var followers = proxy is null ? [] : snap.LeadLag
            .SelectMany(kv => kv.Value.Where(l => l.Proxy == proxy).Select(l => (Code: kv.Key, Link: l)))
            .OrderByDescending(x => Math.Abs(x.Link.Rho))
            .ToList();
        if (entries.Count == 0 && followers.Count == 0 && proxy is null) return null;

        var wanted = entries.SelectMany(e => e.Tickers).Concat(followers.Select(f => f.Code));
        if (proxy is not null) wanted = wanted.Append(proxy);
        var moves = await MovesAsync(wanted, ct);

        return new FactorRelations(
            factor,
            entries.Count > 0 ? entries[0].Link.Kind : null,
            proxy is null ? null : Move(moves, proxy),
            entries.Select(e => new FactorEntryDto(e.Link.Direction, e.Link.Note, e.Tickers.Select(t => Move(moves, t)).ToList(), e.Sectors)).ToList(),
            followers.Select(f => new FollowerDto(f.Code, catalog.Find(f.Code)?.Name, f.Link.Rho, f.Link.N, moves.GetValueOrDefault(f.Code)?.ChgPct)).ToList(),
            snap.AsOf == default ? null : snap.AsOf);
    }

    /// <summary>Computes the statistical links once when none exist yet (first run before any EOD pipeline).</summary>
    async Task EnsureStatsAsync(CancellationToken ct)
    {
        if (stats.HasData || !Db.IsConfigured || DateTimeOffset.UtcNow - _lastComputeAttempt < TimeSpan.FromMinutes(10)) return;
        await ComputeGate.WaitAsync(ct);
        try
        {
            if (stats.HasData || DateTimeOffset.UtcNow - _lastComputeAttempt < TimeSpan.FromMinutes(10)) return;
            _lastComputeAttempt = DateTimeOffset.UtcNow;
            await services.GetRequiredService<RelationsStatsStep>().RunAsync(market.LastCompletedTradingDay(), ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning("Relations stats unavailable: {Error}", Db.ShortError(ex));
        }
        finally
        {
            ComputeGate.Release();
        }
    }

    async Task<Dictionary<string, MoveDto>> MovesAsync(IEnumerable<string> codes, CancellationToken ct)
    {
        var list = codes.Distinct().Where(c => catalog.Find(c) is not null).Take(QuoteSubscriptions.MaxCodesPerConnection).ToList();
        var quotes = list.Count == 0 ? [] : await market.GetQuotesAsync(list, ct);
        return quotes.ToDictionary(q => q.Code, q => new MoveDto(q.Code, q.Name, q.Price, q.ChangePct is { } p ? Math.Round(p, 2) : null));
    }

    MoveDto Move(Dictionary<string, MoveDto> moves, string code) =>
        moves.GetValueOrDefault(code) ?? new MoveDto(code, catalog.Find(code)?.Name, null, null);
}
