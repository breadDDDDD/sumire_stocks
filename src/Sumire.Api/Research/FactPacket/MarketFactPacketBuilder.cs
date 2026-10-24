using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Sumire.Api.Analytics.Services;
using Sumire.Api.Data;
using Sumire.Api.Market;
using Sumire.Api.Market.Rss;
using Sumire.Api.Relations;

namespace Sumire.Api.Research.FactPacket;

/// <summary>
/// The fact packet builder used by the research worker and the MCP server. Gathers the analytics snapshot (DB or Yahoo),
/// relations, macro quotes, the watchlist and RSS headlines, each optional, then composes with <see cref="FactPackets"/>
/// and trims to <see cref="FactPacketBuilder.MaxChars"/>.
/// </summary>
public sealed partial class MarketFactPacketBuilder(
    AnalyticsStore store,
    RelationsService relations,
    MarketDataService market,
    RssNewsClient news,
    InstrumentCatalog catalog,
    SumireDbContext db,
    TimeProvider clock,
    IEnumerable<IFactPacketContributor> contributors,
    ILogger<MarketFactPacketBuilder> logger) : IFactPacketBuilder
{
    public async Task<JsonObject> BuildAsync(FactPacketRequest request, CancellationToken ct)
    {
        var inputs = await GatherAsync(request, headlines: true, ct);
        var packet = FactPackets.Build(request, inputs);
        if (inputs.Universe is null && inputs.Stock is null)
            packet["note"] = "Price data unavailable right now; rely on cited sources.";

        foreach (var contributor in contributors)
        {
            try { await contributor.ContributeAsync(request, packet, ct); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Fact packet contributor {Contributor} failed", contributor.GetType().Name);
            }
        }
        return FactPacketBuilder.Trim(packet);
    }

    /// <summary>Loads the packet inputs; each source that fails is left out. Without headlines (MCP snapshots) no RSS is fetched.</summary>
    public async Task<PacketInputs> GatherAsync(FactPacketRequest request, bool headlines, CancellationToken ct)
    {
        var universe = await TryAsync("analytics snapshot", () => store.GetAsync(ct), ct);
        var inputs = new PacketInputs
        {
            Now = clock.GetUtcNow(),
            Universe = universe,
            Macro = await TryAsync("macro quotes", () => market.GetQuotesAsync(MacroCodes(), ct), ct) ?? [],
        };

        switch (request.Kind)
        {
            case ResearchKinds.Stock:
                inputs = inputs with
                {
                    Stock = await TryAsync("stock analytics", () => store.GetStockAsync(request.Subject, ct), ct),
                    Relations = await TryAsync("relations", () => relations.GetAsync(request.Subject, ct), ct),
                    Headlines = await NewsAsync(request.Subject),
                };
                break;
            case ResearchKinds.Sector:
                var members = catalog.Core.Where(i => !i.IsIndex && string.Equals(i.Sector, request.Subject, StringComparison.OrdinalIgnoreCase)).ToList();
                inputs = inputs with
                {
                    SectorFactors = members.SelectMany(i => relations.Map.FactorsFor(i.Code, i.Sector)).ToList(),
                    Headlines = !headlines ? [] : await SectorHeadlinesAsync(members.Select(i => i.Code).ToHashSet(), ct),
                };
                break;
            case ResearchKinds.Ask:
                var mentioned = MentionedTickers(request.Question ?? request.Subject, catalog);
                inputs = inputs with
                {
                    Mentioned = mentioned,
                    Stock = mentioned.Count > 0 && universe?.Stocks.ContainsKey(mentioned[0]) != true
                        ? await TryAsync("stock analytics", () => store.GetStockAsync(mentioned[0], ct), ct)
                        : null,
                    Headlines = await NewsAsync(mentioned.FirstOrDefault()),
                };
                break;
            default:
                inputs = inputs with
                {
                    Watchlist = await WatchlistAsync(ct),
                    Headlines = await NewsAsync(null),
                };
                break;
        }

        return inputs;

        async Task<IReadOnlyList<Headline>> NewsAsync(string? code) => !headlines ? []
            : await TryAsync("headlines", () => news.GetHeadlinesAsync(code, FactPackets.MaxHeadlines, ct), ct) ?? [];
    }

    IEnumerable<string> MacroCodes() => catalog.Tape.Where(i => i.Code is not ("IHSG" or "LQ45")).Select(i => i.Code);

    async Task<IReadOnlyList<Headline>> SectorHeadlinesAsync(HashSet<string> members, CancellationToken ct)
    {
        var all = await TryAsync("headlines", () => news.GetHeadlinesAsync(null, 60, ct), ct) ?? [];
        return all.Where(h => h.Tickers.Any(members.Contains)).Take(FactPackets.MaxHeadlines).ToList();
    }

    async Task<IReadOnlyList<string>> WatchlistAsync(CancellationToken ct)
    {
        if (!Db.IsConfigured) return [];
        return await TryAsync("watchlist", async () => (IReadOnlyList<string>)(await db.WatchlistItems.AsNoTracking()
            .OrderBy(i => i.WatchlistId).ThenBy(i => i.Sort).Select(i => i.Code).ToListAsync(ct)).Distinct().ToList(), ct) ?? [];
    }

    /// <summary>Tickers named in free text: upper- or lower-case codes in the catalog, and company names.</summary>
    public static IReadOnlyList<string> MentionedTickers(string text, InstrumentCatalog catalog)
    {
        var found = new List<string>();
        foreach (Match m in Word().Matches(text))
        {
            var code = m.Value.ToUpperInvariant();
            // Lower-case words only count when they look deliberate (4 letters, not a common English word).
            if (m.Value != code && CommonWords.Contains(code)) continue;
            if (catalog.Find(code) is { IsIndex: false } && !found.Contains(code)) found.Add(code);
        }
        foreach (var code in new TickerMatcher(catalog.All).Match(text))
            if (!found.Contains(code)) found.Add(code);
        return found;
    }

    static readonly HashSet<string> CommonWords = ["WHAT", "WHEN", "WITH", "THIS", "THAT", "FROM", "WILL", "HAVE", "WHY", "GOOD", "BEST", "WEEK", "NEXT", "LAST", "OVER", "INTO", "MORE", "SOME", "THEM", "BANK", "GOTO", "HOLD", "SELL", "BUYS", "RATE"];

    async Task<T?> TryAsync<T>(string what, Func<Task<T>> load, CancellationToken ct)
    {
        try { return await load(); }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning("Fact packet without {What}: {Error}", what, Db.ShortError(ex));
            return default;
        }
    }

    [GeneratedRegex(@"\b[A-Za-z]{4}\b")]
    private static partial Regex Word();
}
