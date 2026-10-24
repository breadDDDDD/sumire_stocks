using Microsoft.EntityFrameworkCore;
using Sumire.Api.Data;
using Sumire.Api.Market;
using Sumire.Api.Relations;
using Sumire.Api.Research.FactPacket;

namespace Sumire.Api.Research.Mcp;

/// <summary>What the MCP tools read and write. The real implementation is DB + analytics; tests use a fake.</summary>
public interface IMcpData
{
    Task<IReadOnlyList<ResearchJob>> OpenJobsAsync(int limit, CancellationToken ct);
    /// <summary>The job, tracked: changes are written by <see cref="SaveAsync"/>.</summary>
    Task<ResearchJob?> FindJobAsync(long id, CancellationToken ct);
    Task SaveAsync(CancellationToken ct);
    Task<long> AddInsightAsync(Insight insight, CancellationToken ct);
    Task<EnqueueResult> EnqueueAsync(EnqueueRequest request, CancellationToken ct);
    /// <summary>Packet inputs without headlines (snapshots and overview are quick reads, no RSS).</summary>
    Task<PacketInputs> InputsAsync(FactPacketRequest request, CancellationToken ct);
    Task<StockRelations?> RelationsAsync(string code, CancellationToken ct);
    ISet<string> KnownTickers { get; }
}

public sealed class McpData(
    SumireDbContext db,
    ResearchQueue queue,
    MarketFactPacketBuilder builder,
    RelationsService relations,
    InstrumentCatalog catalog) : IMcpData
{
    public async Task<IReadOnlyList<ResearchJob>> OpenJobsAsync(int limit, CancellationToken ct)
    {
        await queue.RecoverStaleAsync(ct);
        return await db.ResearchJobs.AsNoTracking()
            .Where(j => j.Kind != ResearchKinds.Batch && (j.Status == "pending" || (j.Status == "running" && j.Runner == SumireMcpTools.Runner)))
            .OrderByDescending(j => j.Priority).ThenBy(j => j.CreatedAt)
            .Take(limit).ToListAsync(ct);
    }

    public Task<ResearchJob?> FindJobAsync(long id, CancellationToken ct) => db.ResearchJobs.FirstOrDefaultAsync(j => j.Id == id, ct);

    public Task SaveAsync(CancellationToken ct) => db.SaveChangesAsync(ct);

    public async Task<long> AddInsightAsync(Insight insight, CancellationToken ct)
    {
        db.Insights.Add(insight);
        await db.SaveChangesAsync(ct);
        return insight.Id;
    }

    public Task<EnqueueResult> EnqueueAsync(EnqueueRequest request, CancellationToken ct) => queue.EnqueueAsync(request, ct);

    public Task<PacketInputs> InputsAsync(FactPacketRequest request, CancellationToken ct) => builder.GatherAsync(request, headlines: false, ct);

    public Task<StockRelations?> RelationsAsync(string code, CancellationToken ct) => relations.GetAsync(code, ct);

    public ISet<string> KnownTickers => catalog.All.Select(i => i.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);
}
