using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Sumire.Api.Data;
using Sumire.Api.Market;
using Sumire.Api.Research.Prompts;
using Sumire.Api.Research.Runners;

namespace Sumire.Api.Research;

/// <summary>Runs queued research jobs one at a time: packet → runner (with fallback) → validator (one retry) → insight.</summary>
public sealed class ResearchWorker(
    SumireDbContext db,
    ResearchQueue queue,
    IFactPacketBuilder packets,
    IEnumerable<IResearchRunner> runners,
    IInsightNotifier notifier,
    ResearchOptions options,
    MarketCalendar calendar,
    TimeProvider clock,
    ILogger<ResearchWorker> logger)
{
    readonly Dictionary<string, IResearchRunner> _runners = runners.ToDictionary(r => r.Name, StringComparer.OrdinalIgnoreCase);
    HashSet<string>? _knownTickers;

    /// <summary>Why the last run stopped early (paused, caps, all runners exhausted); null when the queue simply ran dry.</summary>
    public string? StoppedReason { get; private set; }

    /// <summary>Queues and claims the due scheduled batch, if any. Returns the batch id when this process ran it.</summary>
    public async Task<string?> RunDueBatchAsync(ISet<string>? knownRun, CancellationToken ct)
    {
        var alreadyRun = knownRun ?? await queue.RecentBatchesAsync(ct);
        var due = ResearchSchedule.Due(clock.GetUtcNow(), calendar, options.MiddayBatch, alreadyRun);
        if (due is null) return null;
        knownRun?.Add(due.Id);
        if (!options.Enabled || !await queue.TryClaimBatchAsync(due.Id, ct)) return null;

        var context = await BatchPlanner.LoadContextAsync(db, clock, ct);
        var plan = BatchPlanner.Plan(due.Kind, context, clock.GetUtcNow(), options.BatchCap);
        foreach (var request in plan) await queue.EnqueueAsync(request, ct);
        logger.LogInformation("Research batch {Batch}: queued {Count} job(s)", due.Id, plan.Count);
        return due.Id;
    }

    /// <summary>Processes pending jobs until the queue is empty, a cap is hit, everything is exhausted or research is paused.</summary>
    public async Task<int> RunPendingAsync(int maxJobs, CancellationToken ct)
    {
        StoppedReason = null;
        await queue.RecoverStaleAsync(ct);
        var processed = 0;
        while (processed < maxJobs && !ct.IsCancellationRequested)
        {
            if (await queue.IsPausedAsync(ct)) { StoppedReason = "paused"; break; }
            var pick = await PickAsync(null, ct);
            if (pick.Entry is null) { StoppedReason = pick.BlockedReason; break; }
            var job = await queue.ClaimNextAsync(ct);
            if (job is null) break;
            try
            {
                if (!await RunJobAsync(job, ct)) break;
            }
            catch (Exception ex)
            {
                // Without this the job would sit in 'running' until stale recovery picks it up.
                await ReleaseAsync(job, ex, ct.IsCancellationRequested);
                throw;
            }
            processed++;
        }
        return processed;
    }

    async Task<RunnerPick> PickAsync(ISet<string>? skip, CancellationToken ct) =>
        ResearchBudget.Pick(options, _runners.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase),
            await queue.TodayUsageAsync(ct), await queue.ExhaustedAsync(ct), clock.GetUtcNow(), skip);

    /// <summary>Runs one claimed job. Returns false when processing should stop (no runner left).</summary>
    async Task<bool> RunJobAsync(ResearchJob job, CancellationToken ct)
    {
        var question = job.Packet is { } p && JsonNode.Parse(p) is JsonObject o ? (string?)o["question"] : null;
        var packet = await packets.BuildAsync(new FactPacketRequest(job.Kind, job.Subject, question), ct);
        var packetJson = packet.ToJsonString();
        job.Packet = packetJson;
        await db.SaveChangesAsync(ct);

        var workspace = options.ResolveWorkspace();
        Directory.CreateDirectory(workspace);
        var schemaPath = Path.Combine(workspace, InsightSchema.FileName);
        await File.WriteAllTextAsync(schemaPath, InsightSchema.Text, ct);

        var tried = new HashSet<string>();
        List<string>? feedback = null;
        var retried = false;
        while (true)
        {
            var pick = await PickAsync(tried, ct);
            if (pick.Entry is not { } entry)
            {
                // Nothing can run it now: back to the queue, without counting this attempt.
                job.Status = "pending";
                job.Attempts = Math.Max(0, job.Attempts - 1);
                job.Error = pick.BlockedReason;
                job.StartedAt = null;
                await db.SaveChangesAsync(ct);
                StoppedReason = pick.BlockedReason;
                return false;
            }

            var runner = _runners[entry.Runner];
            job.Runner = entry.Runner;
            job.Model = entry.Model;
            var prompt = ResearchPrompts.Build(job.Kind, job.Subject, question, packetJson, feedback);

            ProcessOutput output;
            try
            {
                var command = runner.BuildCommand(new RunRequest(entry.Model, prompt, workspace, schemaPath, options.Timeout));
                output = await ProcessRunner.RunAsync(command, options.Timeout, ct);
            }
            catch (Exception ex) when (ex is FileNotFoundException or ArgumentException or System.ComponentModel.Win32Exception)
            {
                logger.LogWarning("Research runner {Entry} is not available: {Error}", entry, ex.Message);
                tried.Add(entry.Key);
                continue;
            }

            var now = clock.GetUtcNow();
            var result = runner.Parse(output, now);
            logger.LogInformation("Research job {Id} ({Kind} {Subject}) via {Entry}: {Outcome} in {Seconds:0}s",
                job.Id, job.Kind, job.Subject, entry, result.Outcome, output.Duration.TotalSeconds);

            switch (result.Outcome)
            {
                case RunnerOutcome.QuotaExhausted:
                    await queue.MarkExhaustedAsync(entry, ResearchBudget.ExhaustedUntil(now, result.ResetAt), ct);
                    tried.Add(entry.Key);
                    continue;

                case RunnerOutcome.Timeout:
                    await FinishAsync(job, job.Attempts < 2 ? "pending" : "failed", $"timed out after {options.Timeout.TotalMinutes:0} min", ct);
                    return true;

                case RunnerOutcome.Failed:
                    if (!retried)
                    {
                        retried = true;
                        feedback = [$"Your previous reply could not be used ({result.Error}). Reply with one JSON object matching the schema."];
                        continue;
                    }
                    await FinishAsync(job, "failed", result.Error, ct);
                    return true;
            }

            var doc = InsightDocument.TryParse(result.Json, out var parseError);
            if (doc is null)
            {
                if (!retried)
                {
                    retried = true;
                    feedback = [$"Your previous reply did not match the insight schema ({parseError})."];
                    continue;
                }
                await FinishAsync(job, "failed", parseError, ct);
                return true;
            }

            var issues = InsightValidator.Validate(doc, packet, await KnownTickersAsync(ct), now, options.SourceMaxAgeDays);
            if (issues.Count > 0 && !retried)
            {
                retried = true;
                feedback = issues.Select(i => i.Message).ToList();
                continue;
            }

            var insight = new Insight
            {
                Kind = job.Kind,
                Subject = job.Subject,
                CreatedAt = now,
                Runner = entry.Runner,
                Model = entry.Model,
                Sentiment = doc.Sentiment,
                Confidence = doc.Confidence,
                Body = doc.BodyJson(),
                Sources = doc.SourcesJson(),
                Warnings = issues.Count > 0 ? JsonSerializer.Serialize(issues.Select(i => new { i.Code, i.Message }), InsightDocument.Json) : null,
            };
            db.Insights.Add(insight);
            await db.SaveChangesAsync(ct);
            job.InsightId = insight.Id;
            await FinishAsync(job, "done", issues.Count > 0 ? $"{issues.Count} validator warning(s)" : null, ct);
            await notifier.InsightReadyAsync(new InsightReady(job.Kind, job.Subject, insight.Id), ct);
            return true;
        }
    }

    /// <summary>Shutdown puts the job back without counting the attempt; a crash requeues it once, then fails it.</summary>
    async Task ReleaseAsync(ResearchJob job, Exception ex, bool cancelled)
    {
        try
        {
            if (cancelled)
            {
                job.Status = "pending";
                job.Attempts = Math.Max(0, job.Attempts - 1);
                job.StartedAt = null;
            }
            else if (job.Attempts < 2)
            {
                job.Status = "pending";
                job.Error = Db.ShortError(ex);
            }
            else
            {
                job.Status = "failed";
                job.Error = Db.ShortError(ex);
                job.FinishedAt = clock.GetUtcNow();
                job.Packet = null;
            }
            await db.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception saveError) when (saveError is not OutOfMemoryException)
        {
            logger.LogWarning("Could not release research job {Id}: {Error}", job.Id, Db.ShortError(saveError));
        }
    }

    async Task FinishAsync(ResearchJob job, string status, string? error, CancellationToken ct)
    {
        job.Status = status;
        job.Error = error;
        if (status != "pending")
        {
            job.FinishedAt = clock.GetUtcNow();
            job.Packet = null;
        }
        await db.SaveChangesAsync(ct);
    }

    async Task<HashSet<string>> KnownTickersAsync(CancellationToken ct) =>
        _knownTickers ??= (await db.Instruments.AsNoTracking().Select(i => i.Code).ToListAsync(ct)).ToHashSet(StringComparer.OrdinalIgnoreCase);
}
