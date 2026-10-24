using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Sumire.Api.Common;
using Sumire.Api.Data;
using Sumire.Api.Jobs;
using Sumire.Api.Market;
using Sumire.Api.Research.Runners;

namespace Sumire.Api.Research;

public static partial class ResearchModule
{
    public static IServiceCollection AddResearch(this IServiceCollection services, IConfiguration config)
    {
        services.AddSingleton(ResearchOptions.FromConfig(config));
        services.AddSingleton<IResearchRunner, AgyRunner>();
        services.AddSingleton<IResearchRunner, GeminiCliRunner>();
        services.AddSingleton<IResearchRunner, ClaudeCodeRunner>();
        services.AddSingleton<IInsightNotifier, SignalRInsightNotifier>();
        services.AddSingleton<ResearchSignal>();
        services.AddScoped<ResearchQueue>();
        services.AddScoped<IFactPacketBuilder, FactPacket.MarketFactPacketBuilder>();
        services.AddScoped<ResearchWorker>();
        services.AddHostedService<ResearchScheduler>();
        return services;
    }

    public record ResearchRequest(string? Kind, string? Subject, string? Question, bool? Force);

    public static IEndpointRouteBuilder MapResearch(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api").AddEndpointFilter(async (ctx, next) =>
            Db.IsConfigured ? await next(ctx) : Results.Problem(statusCode: 503, title: "Database not configured", detail: Db.NotConfiguredReason))
            .AddEndpointFilter(Analytics.AnalyticsEndpoints.UnavailableFilter);

        api.MapPost("/research", async (ResearchRequest body, ResearchQueue queue, ResearchSignal signal, CancellationToken ct) =>
        {
            var kind = body.Kind?.Trim().ToLowerInvariant() ?? "";
            if (ResearchKinds.BriefKind(kind) is { } brief) kind = brief;
            if (!ResearchKinds.IsValid(kind))
                return Results.Problem(statusCode: 400, title: $"kind must be one of {string.Join(", ", ResearchKinds.All)}");

            var subject = kind switch
            {
                ResearchKinds.Ask => body.Question ?? body.Subject,
                _ when ResearchKinds.IsBrief(kind) => "IHSG",
                _ => body.Subject,
            };
            if (string.IsNullOrWhiteSpace(subject))
                return Results.Problem(statusCode: 400, title: kind == ResearchKinds.Ask ? "question is required" : "subject is required");
            if (kind == ResearchKinds.Stock && !TickerRegex().IsMatch(subject.Trim()))
                return Results.Problem(statusCode: 400, title: "subject must be an IDX ticker, e.g. BBCA");

            var question = kind == ResearchKinds.Ask ? subject.Trim() : body.Question;
            var result = await queue.EnqueueAsync(new EnqueueRequest(kind, subject, question, body.Force ?? false), ct);
            if (result.Status != "cached") signal.Wake();
            return Results.Json(result, statusCode: result.Status == "queued" ? 202 : 200);
        });

        api.MapGet("/research", async (SumireDbContext db, ResearchQueue queue, ResearchOptions options, MarketCalendar calendar,
            IEnumerable<IResearchRunner> runners, TimeProvider clock, CancellationToken ct) =>
        {
            var now = clock.GetUtcNow();
            var usage = await queue.TodayUsageAsync(ct);
            var exhausted = await queue.ExhaustedAsync(ct);
            var installed = runners.ToDictionary(r => r.Name, r => ProcessRunner.ResolveExecutable(r.Executable) is not null);
            var jobs = await db.ResearchJobs.AsNoTracking().Where(j => j.Kind != ResearchKinds.Batch)
                .OrderByDescending(j => j.Id).Take(50).ToListAsync(ct);
            var pick = ResearchBudget.Pick(options, installed.Where(kv => kv.Value).Select(kv => kv.Key).ToHashSet(), usage, exhausted, now);

            return Results.Json(new
            {
                enabled = options.Enabled,
                paused = await queue.IsPausedAsync(ct),
                usage = new { today = usage.Total, dailyCap = options.DailyCap, batchCap = options.BatchCap },
                current = pick.Entry is { } e ? new { runner = e.Runner, model = e.Model } : null,
                blockedReason = pick.BlockedReason,
                runners = options.Runners.Select(r => new
                {
                    runner = r.Runner,
                    model = r.Model,
                    installed = installed.GetValueOrDefault(r.Runner),
                    used = usage.For(r),
                    cap = ResearchBudget.CapFor(options, r) is { } c ? new { pattern = c.Pattern, cap = c.Cap } : null,
                    exhaustedUntil = exhausted.TryGetValue(r.Key, out var until) ? Wib.Iso(until) : null,
                }),
                pending = await queue.PendingCountAsync(ct),
                jobs = jobs.Select(JobDto),
                nextBatches = ResearchSchedule.Upcoming(now, calendar, options.MiddayBatch)
                    .Select(b => new { kind = b.Kind.ToString().ToLowerInvariant(), at = Wib.Iso(b.At) }),
            });
        });

        api.MapPost("/research/pause", async (ResearchQueue queue, CancellationToken ct) =>
        {
            await queue.SetPausedAsync(true, ct);
            return Results.Json(new { paused = true });
        });
        api.MapPost("/research/resume", async (ResearchQueue queue, ResearchOptions options, ResearchSignal signal, CancellationToken ct) =>
        {
            await queue.SetPausedAsync(false, ct);
            signal.Wake();
            return Results.Json(new { paused = !options.Enabled });
        });

        api.MapGet("/stocks/{code}/insight", async (string code, SumireDbContext db, TimeProvider clock, CancellationToken ct) =>
        {
            code = code.Trim().ToUpperInvariant();
            var insight = await db.Insights.AsNoTracking().Where(i => i.Kind == ResearchKinds.Stock && i.Subject == code)
                .OrderByDescending(i => i.CreatedAt).FirstOrDefaultAsync(ct);
            if (insight is not null) return Results.Json(InsightDto(insight, clock.GetUtcNow()));
            var lastJob = await db.ResearchJobs.AsNoTracking().Where(j => j.Kind == ResearchKinds.Stock && j.Subject == code)
                .OrderByDescending(j => j.Id).FirstOrDefaultAsync(ct);
            return Results.Json(new { title = $"No insight for {code} yet", status = 404, lastJob = lastJob is null ? null : JobDto(lastJob) }, statusCode: 404);
        });

        api.MapGet("/briefs/latest", async (string? kind, SumireDbContext db, TimeProvider clock, CancellationToken ct) =>
        {
            string[] kinds = kind is null ? [ResearchKinds.BriefOpen, ResearchKinds.BriefClose, ResearchKinds.BriefWeek]
                : ResearchKinds.BriefKind(kind) is { } k ? [k] : [];
            if (kinds.Length == 0) return Results.Problem(statusCode: 400, title: "kind must be open, close or week");
            var insight = await db.Insights.AsNoTracking().Where(i => kinds.Contains(i.Kind))
                .OrderByDescending(i => i.CreatedAt).FirstOrDefaultAsync(ct);
            return insight is null
                ? Results.Problem(statusCode: 404, title: "No brief yet")
                : Results.Json(InsightDto(insight, clock.GetUtcNow()));
        });

        api.MapGet("/insights/{id:long}", async (long id, SumireDbContext db, TimeProvider clock, CancellationToken ct) =>
            await db.Insights.AsNoTracking().FirstOrDefaultAsync(i => i.Id == id, ct) is { } insight
                ? Results.Json(InsightDto(insight, clock.GetUtcNow()))
                : Results.Problem(statusCode: 404, title: "Insight not found"));

        return app;
    }

    public static object JobDto(ResearchJob j) => new
    {
        id = j.Id,
        kind = j.Kind,
        subject = j.Subject,
        priority = j.Priority,
        status = j.Status,
        runner = j.Runner,
        model = j.Model,
        attempts = j.Attempts,
        error = j.Error,
        createdAt = Wib.Iso(j.CreatedAt),
        startedAt = j.StartedAt is { } s ? Wib.Iso(s) : null,
        finishedAt = j.FinishedAt is { } f ? Wib.Iso(f) : null,
        insightId = j.InsightId,
    };

    /// <summary>Insight row flattened: metadata + the body fields + sources + warnings.</summary>
    public static JsonObject InsightDto(Insight i, DateTimeOffset now)
    {
        var dto = new JsonObject
        {
            ["id"] = i.Id,
            ["kind"] = i.Kind,
            ["subject"] = i.Subject,
            ["createdAt"] = Wib.Iso(i.CreatedAt),
            ["ageMinutes"] = (int)Math.Max(0, (now - i.CreatedAt).TotalMinutes),
            ["runner"] = i.Runner,
            ["model"] = i.Model,
            ["sentiment"] = i.Sentiment,
            ["confidence"] = i.Confidence,
        };
        if (JsonNode.Parse(i.Body) is JsonObject body)
            foreach (var (key, value) in body.ToList())
            {
                body.Remove(key);
                dto[key] = value;
            }
        dto["sources"] = JsonNode.Parse(i.Sources);
        dto["warnings"] = i.Warnings is { } w ? JsonNode.Parse(w) : new JsonArray();
        return dto;
    }

    [GeneratedRegex("^[A-Za-z0-9]{2,6}$")]
    private static partial Regex TickerRegex();
}
