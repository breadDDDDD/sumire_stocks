using Microsoft.EntityFrameworkCore;
using Sumire.Api.Analytics.Services;
using Sumire.Api.Common;
using Sumire.Api.Market;
using Sumire.Api.Research;

namespace Sumire.Api.Data;

/// <summary>One way to free space. <paramref name="Table"/> is compacted afterwards so the database size really drops.</summary>
public sealed record RetentionStep(string Name, string Table, Func<SumireDbContext, TimeProvider, CancellationToken, Task<int>> Delete);

/// <summary>
/// Keeps the database under half the 500 MB free-tier quota. Over the limit it compacts first, then runs
/// <see cref="Steps"/> in order until the size is back under it. Normal retention lives with each area; this is the backstop.
/// </summary>
public sealed class StorageGuard(SumireDbContext db, TimeProvider clock, ILogger<StorageGuard> logger)
{
    public const string LimitEnvVar = "DB_STORAGE_LIMIT_MB";
    public const int DefaultLimitMb = 250;
    const long BytesPerMb = 1024 * 1024;
    const int CompactTimeoutSeconds = 300;

    // Every floor stays clear of what the code reads back: analytics and forecasts load 3 years of bars, the evaluator
    // scores 400 days of forecasts, and BacktestIfNeededAsync rewrites a year of baselines if none is older than 120 days.
    public const int BarFloorDays = AnalyticsStore.HistoryDays + 30;
    public const int ForecastFloorDays = 180;
    public const int InsightFloorDays = 30;
    public const int BriefFloorDays = 180;
    public const int JobFloorDays = 7;

    /// <summary>Least valuable data first. Watchlists, alerts, settings, instruments and latest-value tables are never touched.</summary>
    public static readonly IReadOnlyList<RetentionStep> Steps =
    [
        new("finished research jobs", "research_jobs", (db, clock, ct) =>
        {
            var cutoff = clock.GetUtcNow().AddDays(-JobFloorDays);
            return db.ResearchJobs.Where(j => j.Status != "pending" && j.Status != "running" && j.CreatedAt < cutoff).ExecuteDeleteAsync(ct);
        }),
        new("bars outside the history window", "daily_bars", (db, clock, ct) =>
        {
            var cutoff = Wib.Today(clock).AddDays(-BarFloorDays);
            return db.DailyBars.Where(b => b.Date < cutoff).ExecuteDeleteAsync(ct);
        }),
        OlderForecasts(400),
        new("old stock insights", "insights", (db, clock, ct) =>
        {
            var cutoff = clock.GetUtcNow().AddDays(-InsightFloorDays);
            return db.Insights.Where(i => !ResearchKinds.Briefs.Contains(i.Kind) && i.CreatedAt < cutoff).ExecuteDeleteAsync(ct);
        }),
        OlderForecasts(270),
        new("old briefs", "insights", (db, clock, ct) =>
        {
            var cutoff = clock.GetUtcNow().AddDays(-BriefFloorDays);
            return db.Insights.Where(i => ResearchKinds.Briefs.Contains(i.Kind) && i.CreatedAt < cutoff).ExecuteDeleteAsync(ct);
        }),
        OlderForecasts(ForecastFloorDays),
    ];

    static RetentionStep OlderForecasts(int days) => new($"forecasts older than {days} days", "forecasts", (db, clock, ct) =>
    {
        var cutoff = Wib.Today(clock).AddDays(-days);
        return db.Forecasts.Where(f => f.AsOf < cutoff).ExecuteDeleteAsync(ct);
    });

    /// <summary>The limit from <c>DB_STORAGE_LIMIT_MB</c>; 0 turns the guard off, empty or invalid means the default.</summary>
    public static long LimitBytes(string? raw) =>
        (int.TryParse(raw, out var mb) && mb >= 0 ? mb : DefaultLimitMb) * BytesPerMb;

    /// <summary>Never throws: a failed cleanup must not fail the job it runs after.</summary>
    public async Task TryEnforceAsync(CancellationToken ct)
    {
        var limit = LimitBytes(Environment.GetEnvironmentVariable(LimitEnvVar));
        if (limit == 0 || !Db.IsConfigured) return;

        var timeout = db.Database.GetCommandTimeout();
        db.Database.SetCommandTimeout(CompactTimeoutSeconds);
        try
        {
            var tables = db.Model.GetEntityTypes().Select(e => e.GetTableName()).OfType<string>().Distinct().ToList();
            await EnforceAsync(limit, tables, Steps, SizeAsync, s => s.Delete(db, clock, ct), CompactAsync, logger, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning("Storage check failed: {Error}", Db.ShortError(ex));
        }
        finally
        {
            db.Database.SetCommandTimeout(timeout);
        }
    }

    /// <summary>
    /// A plain DELETE leaves the files the same size, so each step is compacted before measuring again;
    /// otherwise every step would look like it freed nothing and the whole list would run.
    /// </summary>
    public static async Task<long> EnforceAsync(
        long limit,
        IReadOnlyList<string> tables,
        IReadOnlyList<RetentionStep> steps,
        Func<CancellationToken, Task<long>> measure,
        Func<RetentionStep, Task<int>> delete,
        Func<IEnumerable<string>, CancellationToken, Task> compact,
        ILogger logger,
        CancellationToken ct)
    {
        var size = await measure(ct);
        if (size <= limit) return size;

        logger.LogInformation("Database is {Size} MB, over the {Limit} MB limit; compacting", size / BytesPerMb, limit / BytesPerMb);
        await compact(tables, ct);
        size = await measure(ct);

        foreach (var step in steps)
        {
            if (size <= limit) break;
            var deleted = await delete(step);
            if (deleted == 0) continue;
            await compact([step.Table], ct);
            size = await measure(ct);
            logger.LogInformation("Deleted {Rows} rows of {Step}; database is now {Size} MB", deleted, step.Name, size / BytesPerMb);
        }

        if (size > limit)
            logger.LogWarning("Database is still {Size} MB after cleanup, over the {Limit} MB limit; everything left is within its retention floor",
                size / BytesPerMb, limit / BytesPerMb);
        return size;
    }

    async Task<long> SizeAsync(CancellationToken ct)
    {
        await db.Database.OpenConnectionAsync(ct);
        try
        {
            await using var command = db.Database.GetDbConnection().CreateCommand();
            command.CommandText = "SELECT pg_database_size(current_database())";
            return Convert.ToInt64(await command.ExecuteScalarAsync(ct));
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    // VACUUM FULL briefly locks the table, so this runs only when over the limit and only after the daily jobs.
    async Task CompactAsync(IEnumerable<string> tables, CancellationToken ct)
    {
        foreach (var table in tables)
        {
            var sql = $"VACUUM (FULL, ANALYZE) {SumireDbContext.Schema}.\"{table}\"";
            await db.Database.ExecuteSqlRawAsync(sql, ct);
        }
    }
}

public sealed class StorageGuardStep(StorageGuard guard) : IEodPipelineStep
{
    public int Order => EodOrder.StorageGuard;
    public string Name => "storage guard";

    public Task RunAsync(DateOnly tradingDate, CancellationToken ct) => guard.TryEnforceAsync(ct);
}
