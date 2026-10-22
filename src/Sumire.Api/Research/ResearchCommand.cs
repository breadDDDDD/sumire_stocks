using Sumire.Api.Data;

namespace Sumire.Api.Research;

/// <summary>`Sumire research --once`: queues the due batch, processes pending jobs and exits (run by Task Scheduler).</summary>
public static class ResearchCommand
{
    public static async Task<int> RunOnceAsync(IServiceProvider services, string[] args, CancellationToken ct)
    {
        if (!Db.IsConfigured)
        {
            Console.Error.WriteLine($"research --once: {Db.NotConfiguredReason}");
            return 1;
        }

        var queue = services.GetRequiredService<ResearchQueue>();
        var worker = services.GetRequiredService<ResearchWorker>();
        try
        {
            await queue.CleanupAsync(ct);
            var batch = await worker.RunDueBatchAsync(null, ct);
            Console.WriteLine(batch is null ? "research --once: no batch due" : $"research --once: queued batch {batch}");

            var done = await worker.RunPendingAsync(int.MaxValue, ct);
            var usage = await queue.TodayUsageAsync(ct);
            var options = services.GetRequiredService<ResearchOptions>();
            Console.WriteLine($"research --once: {done} job(s) processed; today {usage.Total}/{options.DailyCap}"
                + (worker.StoppedReason is { } r ? $"; stopped: {r}" : ""));
            return 0;
        }
        catch (OperationCanceledException)
        {
            return 130;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"research --once failed: {Db.ShortError(ex)}");
            return 1;
        }
    }
}
