using System.Diagnostics;
using Sumire.Api.Common;
using Sumire.Api.Data;
using Sumire.Api.Market;

namespace Sumire.Api.Jobs;

/// <summary>
/// End-of-day pipeline: at 16:30 WIB on trading days (and once at startup if bars are behind) it imports the
/// instrument seed, syncs daily bars for the core universe (3-year backfill where history is missing) and
/// then runs every registered <see cref="IEodPipelineStep"/>.
/// </summary>
public sealed class EodPipelineJob(
    IServiceScopeFactory scopes,
    InstrumentCatalog catalog,
    MarketCalendar calendar,
    MarketStatus status,
    TimeProvider clock,
    ILogger<EodPipelineJob> logger) : BackgroundService
{
    public static readonly TimeOnly RunAt = new(16, 30);
    static readonly TimeSpan RetryDelay = TimeSpan.FromMinutes(15);
    const int MaxBehindRetries = 3;
    static readonly SemaphoreSlim RunLock = new(1, 1);

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (!Db.IsConfigured)
        {
            logger.LogWarning("EOD pipeline disabled: {Reason}", Db.NotConfiguredReason);
            return;
        }

        // Let the web server finish starting before doing network work.
        await Task.Delay(TimeSpan.FromSeconds(3), clock, ct);

        var caughtUp = await TryCatchUpAsync(ct);
        var behindRetries = 0;
        while (!ct.IsCancellationRequested)
        {
            // Yahoo sometimes publishes the day's bar late: a run that leaves IHSG behind is retried a few times.
            if (caughtUp && behindRetries < MaxBehindRetries && await IsBehindAsync(ct))
            {
                behindRetries++;
                caughtUp = false;
                logger.LogInformation("Bars still behind after the EOD run; retry {Retry} of {Max} in {Delay}",
                    behindRetries, MaxBehindRetries, RetryDelay);
            }

            var next = caughtUp ? NextRun(calendar.NowWib) : clock.GetUtcNow() + RetryDelay;
            status.NextEodRun = Wib.ToWib(next);
            var wait = next - clock.GetUtcNow();
            if (wait > TimeSpan.Zero) await Task.Delay(wait, clock, ct);
            if (caughtUp)
            {
                behindRetries = 0;
                caughtUp = await TryRunAsync(ct);
            }
            else caughtUp = await TryCatchUpAsync(ct);
        }
    }

    async Task<bool> IsBehindAsync(CancellationToken ct)
    {
        try
        {
            using var scope = scopes.CreateScope();
            var market = scope.ServiceProvider.GetRequiredService<MarketDataService>();
            var ihsg = catalog.Find("IHSG")?.Code ?? catalog.Core.First().Code;
            return await market.LastBarDateAsync(ihsg, ct) < market.LastCompletedTradingDay();
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogDebug("EOD freshness check failed: {Error}", Db.ShortError(ex));
            return false;
        }
    }

    /// <summary>Next 16:30 WIB on a trading day strictly after <paramref name="nowWib"/>.</summary>
    public DateTimeOffset NextRun(DateTimeOffset nowWib)
    {
        var day = DateOnly.FromDateTime(nowWib.DateTime);
        if (!calendar.IsTradingDay(day) || TimeOnly.FromDateTime(nowWib.DateTime) >= RunAt) day = calendar.NextTradingDay(day);
        return new DateTimeOffset(day.ToDateTime(RunAt), TimeSpan.FromHours(7));
    }

    async Task<bool> TryCatchUpAsync(CancellationToken ct)
    {
        try
        {
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<SumireDbContext>();
            await catalog.ImportSeedAsync(db, ct);

            var market = scope.ServiceProvider.GetRequiredService<MarketDataService>();
            var ihsg = catalog.Find("IHSG")?.Code ?? catalog.Core.First().Code;
            var last = await market.LastBarDateAsync(ihsg, ct);
            if (last >= market.LastCompletedTradingDay()) return true;
            logger.LogInformation("Bars are behind (last {Last}); running EOD catch-up", last?.ToString("yyyy-MM-dd") ?? "none");
            return await TryRunAsync(ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            status.LastEodError = Db.ShortError(ex);
            logger.LogWarning("EOD catch-up failed, retrying later: {Error}", status.LastEodError);
            return false;
        }
    }

    async Task<bool> TryRunAsync(CancellationToken ct)
    {
        try
        {
            await RunOnceAsync(ct);
            return true;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            status.LastEodError = Db.ShortError(ex);
            logger.LogError("EOD pipeline failed: {Error}", status.LastEodError);
            return false;
        }
    }

    /// <summary>Runs the whole pipeline once. Safe to call concurrently (later callers wait).</summary>
    public async Task RunOnceAsync(CancellationToken ct)
    {
        await RunLock.WaitAsync(ct);
        status.EodRunning = true;
        var sw = Stopwatch.StartNew();
        try
        {
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<SumireDbContext>();
            var market = scope.ServiceProvider.GetRequiredService<MarketDataService>();
            await catalog.ImportSeedAsync(db, ct);

            int symbols = 0, rows = 0, failed = 0;
            foreach (var instrument in catalog.Core.OrderBy(i => i.Code))
            {
                try
                {
                    rows += await market.SyncBarsAsync(instrument, ct);
                    symbols++;
                }
                catch (Exception ex) when (ex is HttpRequestException or YahooException or TaskCanceledException && !ct.IsCancellationRequested)
                {
                    failed++;
                    logger.LogWarning("Bars for {Code} failed: {Error}", instrument.Code, ex.Message);
                }
            }

            var tradingDate = market.LastCompletedTradingDay();
            foreach (var step in scope.ServiceProvider.GetServices<IEodPipelineStep>().OrderBy(s => s.Order))
            {
                try
                {
                    await step.RunAsync(tradingDate, ct);
                }
                catch (Exception ex) when (!ct.IsCancellationRequested)
                {
                    logger.LogError(ex, "EOD step {Step} failed", step.Name);
                }
            }

            status.LastEodRun = Wib.ToWib(clock.GetUtcNow());
            status.LastEodResult = $"{symbols} symbols, {rows} rows upserted, {failed} failed, {sw.Elapsed.TotalSeconds:0}s";
            status.LastEodError = null;
            logger.LogInformation("EOD pipeline done: {Result}", status.LastEodResult);
        }
        finally
        {
            status.EodRunning = false;
            RunLock.Release();
        }
    }
}
