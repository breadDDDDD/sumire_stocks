using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Sumire.Api.Common;
using Sumire.Api.Data;

namespace Sumire.Api.Research;

public sealed record EnqueueRequest(string Kind, string Subject, string? Question = null, bool Force = false, int Priority = ResearchPriority.OnCommand);

/// <summary>status: queued (new job), duplicate (already pending/running), cached (a fresh insight exists).</summary>
public sealed record EnqueueResult(long? JobId, string Status, long? InsightId = null);

/// <summary>The research queue over sumire.research_jobs, plus the pause/exhaustion state kept in sumire.settings.</summary>
public sealed class ResearchQueue(SumireDbContext db, TimeProvider clock, ResearchOptions options)
{
    public const string PausedKey = "research.paused";
    public const string ExhaustedKey = "research.exhausted";
    static readonly TimeSpan RunningTimeout = TimeSpan.FromMinutes(15);

    public static string NormalizeSubject(string kind, string subject) => kind switch
    {
        ResearchKinds.Stock => subject.Trim().ToUpperInvariant(),
        _ when ResearchKinds.IsBrief(kind) => "IHSG",
        ResearchKinds.Ask => subject.Trim() is { Length: > 200 } s ? s[..200] : subject.Trim(),
        _ => subject.Trim(),
    };

    public async Task<EnqueueResult> EnqueueAsync(EnqueueRequest request, CancellationToken ct)
    {
        var subject = NormalizeSubject(request.Kind, request.Subject);
        var now = clock.GetUtcNow();

        var open = await db.ResearchJobs
            .Where(j => j.Kind == request.Kind && j.Subject == subject && (j.Status == "pending" || j.Status == "running"))
            .OrderByDescending(j => j.Id).FirstOrDefaultAsync(ct);
        var latest = await db.Insights.AsNoTracking()
            .Where(i => i.Kind == request.Kind && i.Subject == subject)
            .OrderByDescending(i => i.CreatedAt).Select(i => new { i.Id, i.CreatedAt }).FirstOrDefaultAsync(ct);

        switch (ResearchBudget.Dedupe(request.Kind, request.Force, open is not null, latest?.CreatedAt, now, options))
        {
            case DedupeDecision.Duplicate:
                if (open!.Status == "pending" && request.Priority > open.Priority)
                {
                    open.Priority = request.Priority;
                    await db.SaveChangesAsync(ct);
                }
                return new(open.Id, "duplicate");
            case DedupeDecision.Cached:
                return new(null, "cached", latest!.Id);
        }

        var job = new ResearchJob
        {
            Kind = request.Kind,
            Subject = subject,
            Priority = request.Priority,
            Packet = request.Question is { Length: > 0 } q ? JsonSerializer.Serialize(new { question = q }) : null,
            CreatedAt = now,
        };
        db.ResearchJobs.Add(job);
        await db.SaveChangesAsync(ct);
        return new(job.Id, "queued");
    }

    /// <summary>Claims the highest-priority pending job (FOR UPDATE SKIP LOCKED, so the web app and Task Scheduler never share one).</summary>
    public async Task<ResearchJob?> ClaimNextAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var claimed = await db.ResearchJobs.FromSqlInterpolated($"""
            UPDATE sumire.research_jobs SET status = 'running', started_at = {now}, attempts = attempts + 1
            WHERE id = (
                SELECT id FROM sumire.research_jobs
                WHERE status = 'pending' AND kind <> 'batch'
                ORDER BY priority DESC, created_at
                LIMIT 1 FOR UPDATE SKIP LOCKED)
            RETURNING *
            """).AsNoTracking().ToListAsync(ct);
        if (claimed.Count == 0) return null;
        return await db.ResearchJobs.FirstAsync(j => j.Id == claimed[0].Id, ct);
    }

    /// <summary>Jobs stuck in 'running' (a crashed process) go back to pending.</summary>
    public Task<int> RecoverStaleAsync(CancellationToken ct)
    {
        var cutoff = clock.GetUtcNow() - RunningTimeout;
        return db.ResearchJobs.Where(j => j.Status == "running" && j.StartedAt < cutoff)
            .ExecuteUpdateAsync(s => s.SetProperty(j => j.Status, "pending"), ct);
    }

    public async Task<int> PendingCountAsync(CancellationToken ct) =>
        await db.ResearchJobs.CountAsync(j => j.Status == "pending" && j.Kind != ResearchKinds.Batch, ct);

    /// <summary>Jobs finished today (WIB) per runner:model; quota failures are not counted.</summary>
    public async Task<ResearchUsage> TodayUsageAsync(CancellationToken ct)
    {
        var start = StartOfWibDay(clock.GetUtcNow());
        var rows = await db.ResearchJobs.AsNoTracking()
            .Where(j => j.Kind != ResearchKinds.Batch && j.Runner != null && j.StartedAt >= start && (j.Status == "done" || j.Status == "failed"))
            .GroupBy(j => new { j.Runner, j.Model })
            .Select(g => new { g.Key.Runner, g.Key.Model, N = g.Count() })
            .ToListAsync(ct);
        return new(rows.ToDictionary(r => $"{r.Runner}:{r.Model}", r => r.N));
    }

    public static DateTimeOffset StartOfWibDay(DateTimeOffset now)
    {
        var wib = Wib.ToWib(now);
        return new DateTimeOffset(wib.Date, wib.Offset).ToUniversalTime();
    }

    // Batch lock rows -----------------------------------------------------------------------------------------

    /// <summary>Inserts the lock row for a batch; false when another process already ran (or is running) it.</summary>
    public async Task<bool> TryClaimBatchAsync(string batchId, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(7316041)", ct);
        var inserted = await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO sumire.research_jobs (kind, subject, priority, status, attempts, created_at, started_at, finished_at)
            SELECT 'batch', {batchId}, 0, 'done', 0, {now}, {now}, {now}
            WHERE NOT EXISTS (SELECT 1 FROM sumire.research_jobs WHERE kind = 'batch' AND subject = {batchId})
            """, ct);
        await tx.CommitAsync(ct);
        return inserted == 1;
    }

    public async Task<HashSet<string>> RecentBatchesAsync(CancellationToken ct)
    {
        var since = clock.GetUtcNow().AddDays(-4);
        return (await db.ResearchJobs.AsNoTracking()
            .Where(j => j.Kind == ResearchKinds.Batch && j.CreatedAt >= since)
            .Select(j => j.Subject).ToListAsync(ct)).ToHashSet();
    }

    // Settings: pause and exhaustion ---------------------------------------------------------------------------

    public async Task<bool> IsPausedAsync(CancellationToken ct)
    {
        if (!options.Enabled) return true;
        var value = await GetSettingAsync(PausedKey, ct);
        return value is not null && value.Trim() == "true";
    }

    public Task SetPausedAsync(bool paused, CancellationToken ct) => SetSettingAsync(PausedKey, paused ? "true" : "false", ct);

    public async Task<Dictionary<string, DateTimeOffset>> ExhaustedAsync(CancellationToken ct)
    {
        var json = await GetSettingAsync(ExhaustedKey, ct);
        if (json is null) return [];
        try
        {
            var map = JsonSerializer.Deserialize<Dictionary<string, DateTimeOffset>>(json) ?? [];
            return ResearchBudget.Active(map, clock.GetUtcNow());
        }
        catch (JsonException) { return []; }
    }

    public async Task MarkExhaustedAsync(RunnerEntry entry, DateTimeOffset until, CancellationToken ct)
    {
        var map = await ExhaustedAsync(ct);
        map[entry.Key] = until;
        await SetSettingAsync(ExhaustedKey, JsonSerializer.Serialize(map), ct);
    }

    async Task<string?> GetSettingAsync(string key, CancellationToken ct) =>
        await db.Settings.AsNoTracking().Where(s => s.Key == key).Select(s => s.Value).FirstOrDefaultAsync(ct);

    async Task SetSettingAsync(string key, string jsonValue, CancellationToken ct)
    {
        var row = await db.Settings.FirstOrDefaultAsync(s => s.Key == key, ct);
        if (row is null) db.Settings.Add(new Setting { Key = key, Value = jsonValue });
        else row.Value = jsonValue;
        await db.SaveChangesAsync(ct);
    }

    // Retention -----------------------------------------------------------------------------------------------

    /// <summary>Jobs kept 30 days; stock/sector/ask insights 90 days, briefs 1 year; pending jobs older than a day are skipped.</summary>
    public async Task CleanupAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        await db.ResearchJobs.Where(j => j.Status == "pending" && j.CreatedAt < now.AddDays(-1))
            .ExecuteUpdateAsync(s => s.SetProperty(j => j.Status, "skipped").SetProperty(j => j.Error, "expired before it could run")
                .SetProperty(j => j.Packet, (string?)null).SetProperty(j => j.FinishedAt, now), ct);
        await db.ResearchJobs.Where(j => j.CreatedAt < now.AddDays(-30)).ExecuteDeleteAsync(ct);
        string[] briefs = [ResearchKinds.BriefOpen, ResearchKinds.BriefClose, ResearchKinds.BriefWeek];
        await db.Insights.Where(i => !briefs.Contains(i.Kind) && i.CreatedAt < now.AddDays(-90)).ExecuteDeleteAsync(ct);
        await db.Insights.Where(i => briefs.Contains(i.Kind) && i.CreatedAt < now.AddYears(-1)).ExecuteDeleteAsync(ct);
    }
}
