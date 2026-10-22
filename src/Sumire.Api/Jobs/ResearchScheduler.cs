using Sumire.Api.Data;
using Sumire.Api.Research;

namespace Sumire.Api.Jobs;

/// <summary>Wakes the scheduler early, e.g. after an on-command request (INS!, ASK).</summary>
public sealed class ResearchSignal
{
    readonly SemaphoreSlim _signal = new(0, 1);

    public void Wake()
    {
        if (_signal.CurrentCount == 0)
            try { _signal.Release(); } catch (SemaphoreFullException) { }
    }

    public async Task WaitAsync(TimeSpan timeout, CancellationToken ct)
    {
        try { await _signal.WaitAsync(timeout, ct); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }
    }
}

/// <summary>
/// Web-mode research loop: queues the due batch (07:45, 12:05 opt-in, 16:45, Sunday 19:00 WIB) and runs pending jobs one at a
/// time. `research --once` (Task Scheduler) does the same once; the batch lock row keeps them from both running a batch.
/// </summary>
public sealed class ResearchScheduler(IServiceScopeFactory scopes, ResearchSignal signal, ILogger<ResearchScheduler> logger) : BackgroundService
{
    static readonly TimeSpan Tick = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (!Db.IsConfigured)
        {
            logger.LogWarning("Research scheduler idle: {Reason}", Db.NotConfiguredReason);
            return;
        }

        await Task.Delay(TimeSpan.FromSeconds(10), ct); // let startup (migrations, first requests) settle
        var batchesRun = new HashSet<string>();
        var loaded = false;
        var lastCleanup = DateOnly.MinValue;
        string? lastReason = null;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var queue = scope.ServiceProvider.GetRequiredService<ResearchQueue>();
                var worker = scope.ServiceProvider.GetRequiredService<ResearchWorker>();
                var clock = scope.ServiceProvider.GetRequiredService<TimeProvider>();

                if (!loaded)
                {
                    batchesRun.UnionWith(await queue.RecentBatchesAsync(ct));
                    loaded = true;
                }
                var today = Common.Wib.Today(clock);
                if (today != lastCleanup)
                {
                    await queue.CleanupAsync(ct);
                    lastCleanup = today;
                }

                await worker.RunDueBatchAsync(batchesRun, ct);
                if (await queue.PendingCountAsync(ct) > 0)
                {
                    var n = await worker.RunPendingAsync(int.MaxValue, ct);
                    if (n > 0 || worker.StoppedReason != lastReason)
                        logger.LogInformation("Research: {Count} job(s) done{Reason}", n, worker.StoppedReason is { } r ? $"; stopped: {r}" : "");
                    lastReason = worker.StoppedReason;
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning("Research scheduler tick failed: {Error}", Db.ShortError(ex));
            }

            await signal.WaitAsync(Tick, ct);
        }
    }
}
