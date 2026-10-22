using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Sumire.Api.Common;
using Sumire.Api.Data;

namespace Sumire.Api.Research;

public sealed record FactPacketRequest(string Kind, string Subject, string? Question);

/// <summary>Builds the compact JSON fact packet (2-4k tokens) a runner researches against.</summary>
public interface IFactPacketBuilder
{
    Task<JsonObject> BuildAsync(FactPacketRequest request, CancellationToken ct);
}

/// <summary>
/// Optional enrichment registered by other areas (quotes cache, RSS headlines, relations, indicators).
/// Contributors add sections; headline text must go under "headlines" (marked untrusted in the prompt).
/// </summary>
public interface IFactPacketContributor
{
    Task ContributeAsync(FactPacketRequest request, JsonObject packet, CancellationToken ct);
}

/// <summary>Price moves computed from daily bars (ascending by date). Percentages, rounded to 0.01.</summary>
public static class PriceMoves
{
    public static JsonObject? From(IReadOnlyList<DailyBar> bars)
    {
        if (bars.Count == 0) return null;
        var last = bars[^1];
        double? Chg(int back) => bars.Count > back && bars[^(back + 1)].Close > 0
            ? Math.Round((double)(last.Close / bars[^(back + 1)].Close - 1) * 100, 2)
            : null;
        var year = bars.Where(b => b.Date > last.Date.AddYears(-1)).ToList();
        var vol20 = bars.Count > 1 ? bars.TakeLast(21).SkipLast(1).Average(b => (double)b.Volume) : 0;

        var o = new JsonObject
        {
            ["date"] = last.Date.ToString("yyyy-MM-dd"),
            ["last"] = last.Close,
            ["chg1d"] = Chg(1),
            ["chg5d"] = Chg(5),
            ["chg20d"] = Chg(20),
            ["chg60d"] = Chg(60),
        };
        if (year.Count >= 120)
        {
            o["high52"] = year.Max(b => b.High);
            o["low52"] = year.Min(b => b.Low);
        }
        if (vol20 > 0) o["volVs20d"] = Math.Round(last.Volume / vol20, 2);
        foreach (var key in o.Where(kv => kv.Value is null).Select(kv => kv.Key).ToList()) o.Remove(key);
        return o;
    }
}

/// <summary>Default packet: instrument facts, price moves (stock, peers, group, IHSG, macro), sector moves, signal score, fundamentals.</summary>
public sealed class FactPacketBuilder(SumireDbContext db, TimeProvider clock, IEnumerable<IFactPacketContributor> contributors, ILogger<FactPacketBuilder> logger)
    : IFactPacketBuilder
{
    public const int MaxChars = 14_000;
    const int MaxPeers = 8;

    public async Task<JsonObject> BuildAsync(FactPacketRequest request, CancellationToken ct)
    {
        var packet = new JsonObject
        {
            ["asOf"] = Wib.Iso(clock.GetUtcNow()),
            ["kind"] = request.Kind,
            ["subject"] = request.Subject,
        };
        if (request.Question is { Length: > 0 } q) packet["question"] = q;
        var moves = new JsonObject();
        packet["moves"] = moves;

        if (Db.IsConfigured)
        {
            try { await AddDbFactsAsync(request, packet, moves, ct); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning("Fact packet without DB facts: {Error}", Db.ShortError(ex));
                packet["note"] = "Price data unavailable; rely on cited sources.";
            }
        }

        foreach (var contributor in contributors)
        {
            try { await contributor.ContributeAsync(request, packet, ct); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Fact packet contributor {Contributor} failed", contributor.GetType().Name);
            }
        }
        return Trim(packet);
    }

    async Task AddDbFactsAsync(FactPacketRequest request, JsonObject packet, JsonObject moves, CancellationToken ct)
    {
        var instruments = await db.Instruments.AsNoTracking().Where(i => i.Active).ToListAsync(ct);
        var byCode = instruments.ToDictionary(i => i.Code, StringComparer.OrdinalIgnoreCase);
        var macro = instruments.Where(i => i.IsIndex).Select(i => i.Code).ToList();
        var codes = new List<string>(macro);
        Instrument? stock = null;

        switch (request.Kind)
        {
            case ResearchKinds.Stock when byCode.TryGetValue(request.Subject, out stock):
                packet["instrument"] = new JsonObject
                {
                    ["code"] = stock.Code,
                    ["name"] = stock.Name,
                    ["sector"] = stock.Sector,
                    ["industry"] = stock.Industry,
                    ["group"] = stock.GroupName,
                    ["indexes"] = new JsonArray(stock.IndexMemberships.Select(x => (JsonNode?)x).ToArray()),
                };
                codes.Add(stock.Code);
                codes.AddRange(instruments.Where(i => !i.IsIndex && i.Code != stock.Code
                        && ((stock.GroupName != null && i.GroupName == stock.GroupName) || (i.Core && i.Industry == stock.Industry)))
                    .Take(MaxPeers).Select(i => i.Code));
                break;
            case ResearchKinds.Sector:
                codes.AddRange(instruments.Where(i => !i.IsIndex && i.Core && string.Equals(i.Sector, request.Subject, StringComparison.OrdinalIgnoreCase))
                    .Select(i => i.Code));
                break;
        }

        var since = Wib.Today(clock).AddDays(-(stock is null ? 45 : 380));
        var core = instruments.Where(i => i.Core && !i.IsIndex).Select(i => i.Code).ToList();
        var wanted = codes.Concat(core).Distinct().ToList();
        var bars = (await db.DailyBars.AsNoTracking()
                .Where(b => wanted.Contains(b.Code) && b.Date >= since)
                .ToListAsync(ct))
            .GroupBy(b => b.Code)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<DailyBar>)g.OrderBy(b => b.Date).ToList(), StringComparer.OrdinalIgnoreCase);

        foreach (var code in codes.Distinct())
            if (bars.TryGetValue(code, out var series) && PriceMoves.From(series) is { } m)
                moves[code] = m;

        // Sector averages and the day's biggest core movers give briefs and sector research their context.
        var coreMoves = core.Where(bars.ContainsKey)
            .Select(c => (Code: c, Moves: PriceMoves.From(bars[c])))
            .Where(x => x.Moves?["chg1d"] is not null)
            .Select(x => (x.Code, Chg: (double)x.Moves!["chg1d"]!, x.Moves))
            .ToList();
        if (coreMoves.Count > 0)
        {
            packet["sectors"] = new JsonArray(coreMoves
                .GroupBy(x => byCode[x.Code].Sector ?? "Other")
                .Select(g => new { Sector = g.Key, Avg = Math.Round(g.Average(x => x.Chg), 2), N = g.Count() })
                .OrderByDescending(s => s.Avg)
                .Select(s => (JsonNode?)new JsonObject { ["sector"] = s.Sector, ["avgChg1d"] = s.Avg, ["stocks"] = s.N })
                .ToArray());
            if (request.Kind != ResearchKinds.Stock)
            {
                var movers = coreMoves.OrderByDescending(x => Math.Abs(x.Chg)).Take(10).ToList();
                foreach (var mv in movers) moves[mv.Code] ??= mv.Moves!.DeepClone();
                packet["topMovers"] = new JsonArray(movers.Select(x => (JsonNode?)x.Code).ToArray());
            }
        }

        if (stock is not null)
        {
            if (await db.SignalScores.AsNoTracking().FirstOrDefaultAsync(s => s.Code == stock.Code, ct) is { } score)
                packet["signalScore"] = new JsonObject
                {
                    ["asOf"] = score.AsOf.ToString("yyyy-MM-dd"),
                    ["total"] = Math.Round(score.Total, 1),
                    ["reasons"] = score.Reasons is { } r ? JsonNode.Parse(r) : null,
                };
            if (await db.Fundamentals.AsNoTracking().FirstOrDefaultAsync(f => f.Code == stock.Code, ct) is { } f)
                packet["fundamentals"] = new JsonObject
                {
                    ["pe"] = Round(f.Pe), ["pb"] = Round(f.Pb), ["roe"] = Round(f.Roe), ["der"] = Round(f.Der),
                    ["divYield"] = Round(f.DivYield), ["marketCap"] = f.MarketCap,
                };
        }
    }

    static double? Round(double? v) => v is { } d && double.IsFinite(d) ? Math.Round(d, 3) : null;

    /// <summary>Keeps the packet under MaxChars by dropping the least important parts first.</summary>
    public static JsonObject Trim(JsonObject packet, int maxChars = MaxChars)
    {
        bool Fits() => packet.ToJsonString().Length <= maxChars;
        if (packet["headlines"] is JsonArray headlines)
        {
            while (headlines.Count > 0 && !Fits()) headlines.RemoveAt(headlines.Count - 1);
            if (headlines.Count == 0) packet.Remove("headlines");
        }
        foreach (var key in new[] { "fundamentals", "sectors", "topMovers" })
        {
            if (Fits()) break;
            packet.Remove(key);
        }
        if (packet["moves"] is JsonObject moves)
        {
            var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { packet["subject"]?.ToString() ?? "", "IHSG" };
            foreach (var code in moves.Select(kv => kv.Key).Where(k => !keep.Contains(k)).Reverse().ToList())
            {
                if (Fits()) break;
                moves.Remove(code);
            }
        }
        return packet;
    }
}
