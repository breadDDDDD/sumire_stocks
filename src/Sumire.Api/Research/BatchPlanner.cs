using Microsoft.EntityFrameworkCore;
using Sumire.Api.Data;

namespace Sumire.Api.Research;

/// <summary>Inputs for planning a batch: today's % moves, watchlist codes, sector averages and insight ages.</summary>
public sealed record BatchContext(
    IReadOnlyCollection<string> Watchlist,
    IReadOnlyDictionary<string, double> Moves,
    IReadOnlyDictionary<string, double> SectorMoves,
    IReadOnlyDictionary<string, DateTimeOffset> LatestStockInsight,
    ISet<string> CoreCodes);

/// <summary>Which jobs each batch queues (plan §6.6). Pure; the result is ordered by priority and trimmed to the batch cap.</summary>
public static class BatchPlanner
{
    public const double BigMovePct = 3.0;
    static readonly TimeSpan StaleAfter = TimeSpan.FromHours(20);

    public static List<EnqueueRequest> Plan(BatchKind kind, BatchContext ctx, DateTimeOffset now, int batchCap)
    {
        var jobs = new List<EnqueueRequest>();
        switch (kind)
        {
            case BatchKind.Morning:
                jobs.Add(Brief(ResearchKinds.BriefOpen));
                // Watchlist names that moved hard yesterday: overnight follow-up news is likely.
                jobs.AddRange(ctx.Watchlist.Where(c => Math.Abs(ctx.Moves.GetValueOrDefault(c)) >= BigMovePct)
                    .Select(c => Stock(c, ResearchPriority.Watchlist)));
                break;
            case BatchKind.Midday:
                jobs.AddRange(ctx.Watchlist.Where(c => Math.Abs(ctx.Moves.GetValueOrDefault(c)) >= BigMovePct)
                    .OrderByDescending(c => Math.Abs(ctx.Moves[c]))
                    .Select(c => Stock(c, ResearchPriority.Watchlist)));
                break;
            case BatchKind.Close:
                jobs.Add(Brief(ResearchKinds.BriefClose));
                var sectors = ctx.SectorMoves.Where(s => s.Key is not ("Macro" or "Index")).OrderByDescending(s => s.Value).Select(s => s.Key).ToList();
                jobs.AddRange(sectors.Take(2).Concat(sectors.TakeLast(2)).Distinct()
                    .Select(s => new EnqueueRequest(ResearchKinds.Sector, s, Priority: ResearchPriority.Sector)));
                jobs.AddRange(ctx.Watchlist
                    .Where(c => !ctx.LatestStockInsight.TryGetValue(c, out var at) || now - at > StaleAfter)
                    .Select(c => Stock(c, ResearchPriority.Watchlist)));
                jobs.AddRange(ctx.Moves.Where(m => ctx.CoreCodes.Contains(m.Key))
                    .OrderByDescending(m => Math.Abs(m.Value))
                    .Select(m => Stock(m.Key, ResearchPriority.Mover)));
                break;
            case BatchKind.Weekend:
                jobs.Add(Brief(ResearchKinds.BriefWeek));
                break;
        }

        return jobs.DistinctBy(j => (j.Kind, ResearchQueue.NormalizeSubject(j.Kind, j.Subject)))
            .OrderByDescending(j => j.Priority)
            .Take(batchCap)
            .ToList();
    }

    static EnqueueRequest Brief(string kind) => new(kind, "IHSG", Priority: ResearchPriority.Brief);
    static EnqueueRequest Stock(string code, int priority) => new(ResearchKinds.Stock, code, Priority: priority);

    /// <summary>Loads the batch context from the DB: 1-day % change from the last two daily bars of each core/watchlist stock.</summary>
    public static async Task<BatchContext> LoadContextAsync(SumireDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var watchlist = await db.WatchlistItems.AsNoTracking().Select(i => i.Code).Distinct().ToListAsync(ct);
        var instruments = await db.Instruments.AsNoTracking().Where(i => i.Active && !i.IsIndex)
            .Select(i => new { i.Code, i.Sector, i.Core }).ToListAsync(ct);
        var core = instruments.Where(i => i.Core).Select(i => i.Code).ToHashSet();
        var codes = core.Concat(watchlist).ToHashSet();

        var since = Common.Wib.Today(clock).AddDays(-10);
        var bars = await db.DailyBars.AsNoTracking().Where(b => codes.Contains(b.Code) && b.Date >= since)
            .Select(b => new { b.Code, b.Date, b.Close }).ToListAsync(ct);
        var moves = bars.GroupBy(b => b.Code)
            .Select(g => g.OrderByDescending(b => b.Date).Take(2).ToList())
            .Where(l => l.Count == 2 && l[1].Close > 0)
            .ToDictionary(l => l[0].Code, l => Math.Round((double)(l[0].Close / l[1].Close - 1) * 100, 2));

        var sectorOf = instruments.ToDictionary(i => i.Code, i => i.Sector ?? "Other");
        var sectorMoves = moves.Where(m => core.Contains(m.Key))
            .GroupBy(m => sectorOf.GetValueOrDefault(m.Key, "Other"))
            .Where(g => g.Count() >= 3)
            .ToDictionary(g => g.Key, g => Math.Round(g.Average(m => m.Value), 2));

        var latest = await db.Insights.AsNoTracking().Where(i => i.Kind == ResearchKinds.Stock && watchlist.Contains(i.Subject))
            .GroupBy(i => i.Subject).Select(g => new { Code = g.Key, At = g.Max(i => i.CreatedAt) }).ToListAsync(ct);

        return new BatchContext(watchlist, moves, sectorMoves, latest.ToDictionary(x => x.Code, x => x.At), core);
    }
}
