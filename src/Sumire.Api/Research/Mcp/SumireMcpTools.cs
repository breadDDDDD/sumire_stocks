using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Sumire.Api.Common;
using Sumire.Api.Data;
using Sumire.Api.Research.FactPacket;
using Sumire.Api.Research.Prompts;

namespace Sumire.Api.Research.Mcp;

/// <summary>
/// The MCP tool handlers. Each returns a JSON string for the agent. Jobs are claimed with runner "mcp"; the first
/// submission with validator issues is rejected with the issues, a second one is stored with them as warnings.
/// </summary>
public sealed partial class SumireMcpTools(IMcpData data, IInsightNotifier notifier, ResearchOptions options, TimeProvider clock)
{
    public const string Runner = "mcp";
    const string RejectedMarker = "mcp: rejected";

    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = false };

    public async Task<string> ListPendingResearchAsync(int limit, CancellationToken ct)
    {
        var jobs = await data.OpenJobsAsync(Math.Clamp(limit, 1, 50), ct);
        return Serialize(new
        {
            count = jobs.Count,
            jobs = jobs.Select(j => new
            {
                id = j.Id,
                kind = j.Kind,
                subject = j.Subject,
                question = Question(j),
                priority = j.Priority,
                status = j.Status,
                createdAt = Wib.Iso(j.CreatedAt),
            }),
            next = jobs.Count > 0 ? "Call get_research_job(id) for the first job, research it, then submit_insight(job_id, json)." : "Nothing pending.",
        });
    }

    public async Task<string> GetResearchJobAsync(long id, CancellationToken ct)
    {
        var job = await data.FindJobAsync(id, ct);
        if (job is null) return Error($"Job {id} not found.");
        if (job.Kind == ResearchKinds.Batch) return Error($"Job {id} is a batch marker, not a research job.");
        if (job.Status is "done" or "failed" or "skipped") return Error($"Job {id} is already {job.Status}.");
        if (job.Status == "running" && job.Runner != Runner) return Error($"Job {id} is being run by {job.Runner}; pick another one.");

        // The web worker claims jobs with FOR UPDATE SKIP LOCKED; claim atomically too, so both never run the same job.
        if (!await data.TryClaimAsync(job.Id, clock.GetUtcNow(), ct))
            return Error($"Job {id} was just taken by another runner; pick another one.");

        var question = Question(job);
        var packet = StoredPacket(job);
        if (packet is null || job.Runner != Runner)
        {
            var request = new FactPacketRequest(job.Kind, job.Subject, question);
            packet = FactPacketBuilder.Trim(FactPackets.Build(request, await data.InputsAsync(request, ct)));
            if (question is not null) packet["question"] = question;
            job.Packet = packet.ToJsonString();
        }
        if (job.Status != "running" || job.Runner != Runner)
        {
            job.Status = "running";
            job.Runner = Runner;
            job.Model = null;
            job.Attempts++;
            job.Error = null;
        }
        job.StartedAt = clock.GetUtcNow();
        await data.SaveAsync(ct);

        return Serialize(new
        {
            id = job.Id,
            kind = job.Kind,
            subject = job.Subject,
            question,
            instructions = ResearchPrompts.Build(job.Kind, job.Subject, question, job.Packet!) + McpInstructions(job.Id),
            packet = JsonNode.Parse(job.Packet!),
            schema = JsonNode.Parse(InsightSchema.Text),
        });
    }

    public async Task<string> GetStockSnapshotAsync(string code, CancellationToken ct)
    {
        code = (code ?? "").Trim().ToUpperInvariant();
        if (!Ticker().IsMatch(code)) return Error("code must be an IDX ticker, e.g. BBCA.");
        var request = new FactPacketRequest(ResearchKinds.Stock, code, null);
        var inputs = await data.InputsAsync(request, ct);
        if (inputs.Stock is null) return Error($"No price history for {code}.");
        var packet = FactPackets.Build(request, inputs);
        packet.Remove("kind");
        packet.Remove("subject");
        return packet.ToJsonString();
    }

    public async Task<string> GetRelationsAsync(string code, CancellationToken ct)
    {
        code = (code ?? "").Trim().ToUpperInvariant();
        return await data.RelationsAsync(code, ct) is { } rel ? Serialize(rel) : Error($"Unknown code {code}.");
    }

    public async Task<string> GetMarketOverviewAsync(CancellationToken ct)
    {
        var request = new FactPacketRequest(ResearchKinds.BriefClose, "IHSG", null);
        var inputs = await data.InputsAsync(request, ct);
        if (inputs.Universe is null) return Error("Market data is unavailable right now.");
        var packet = FactPackets.Build(request, inputs);
        packet.Remove("kind");
        packet.Remove("subject");
        return packet.ToJsonString();
    }

    public async Task<string> SubmitInsightAsync(long jobId, string json, string? model, CancellationToken ct)
    {
        var job = await data.FindJobAsync(jobId, ct);
        if (job is null) return Error($"Job {jobId} not found.");
        if (job.Status is "done" or "failed" or "skipped") return Error($"Job {jobId} is already {job.Status}.");
        if (job.Runner != Runner || StoredPacket(job) is not { } packet)
            return Error($"Call get_research_job({jobId}) first: it claims the job and builds the fact packet the insight is checked against.");

        JsonNode? node;
        try { node = JsonNode.Parse(json); }
        catch (JsonException ex) { return Rejected($"The insight is not valid JSON: {ex.Message}", []); }
        var doc = InsightDocument.TryParse(node, out var parseError);
        if (doc is null) return Rejected($"The insight does not match the schema: {parseError}", []);

        var now = clock.GetUtcNow();
        var issues = InsightValidator.Validate(doc, packet, data.KnownTickers, now, options.SourceMaxAgeDays);
        var rejectedBefore = job.Error?.StartsWith(RejectedMarker, StringComparison.Ordinal) == true;
        if (issues.Count > 0 && !rejectedBefore)
        {
            job.Error = $"{RejectedMarker} once ({issues.Count} issue(s))";
            await data.SaveAsync(ct);
            return Rejected("The validator found issues. Fix them and submit the full corrected JSON once more; " +
                "a second submission is stored, with any remaining issues shown as warnings.", issues);
        }

        var insight = new Insight
        {
            Kind = job.Kind,
            Subject = job.Subject,
            CreatedAt = now,
            Runner = Runner,
            Model = Trim(model, 64),
            Sentiment = doc.Sentiment,
            Confidence = doc.Confidence,
            Body = doc.BodyJson(),
            Sources = doc.SourcesJson(),
            Warnings = issues.Count > 0 ? JsonSerializer.Serialize(issues.Select(i => new { i.Code, i.Message }), InsightDocument.Json) : null,
        };
        var insightId = await data.AddInsightAsync(insight, ct);
        job.InsightId = insightId;
        job.Model = insight.Model;
        job.Status = "done";
        job.Error = issues.Count > 0 ? $"{issues.Count} validator warning(s)" : null;
        job.FinishedAt = now;
        job.Packet = null;
        await data.SaveAsync(ct);

        // The UI only hears about it when the web app runs in this process; otherwise it shows up on the next read.
        try { await notifier.InsightReadyAsync(new InsightReady(job.Kind, job.Subject, insightId), ct); }
        catch (Exception ex) when (ex is not OperationCanceledException) { }

        return Serialize(new
        {
            accepted = true,
            insightId,
            warnings = issues.Select(i => i.Message),
            next = "Done. Call list_pending_research for the next job.",
        });
    }

    public async Task<string> QueueResearchAsync(string kind, string subject, string? question, CancellationToken ct)
    {
        kind = kind?.Trim().ToLowerInvariant() ?? "";
        if (ResearchKinds.BriefKind(kind) is { } brief) kind = brief;
        if (!ResearchKinds.IsValid(kind)) return Error($"kind must be one of {string.Join(", ", ResearchKinds.All)}.");
        var target = kind switch
        {
            ResearchKinds.Ask => question ?? subject,
            _ when ResearchKinds.IsBrief(kind) => "IHSG",
            _ => subject,
        };
        if (string.IsNullOrWhiteSpace(target)) return Error(kind == ResearchKinds.Ask ? "question is required." : "subject is required.");
        if (kind == ResearchKinds.Stock && !Ticker().IsMatch(target.Trim().ToUpperInvariant()))
            return Error("subject must be an IDX ticker, e.g. BBCA.");

        var result = await data.EnqueueAsync(new EnqueueRequest(kind, target, kind == ResearchKinds.Ask ? target.Trim() : question), ct);
        return Serialize(new { jobId = result.JobId, status = result.Status, insightId = result.InsightId });
    }

    static string McpInstructions(long id) => $"""

        HOW TO SUBMIT (MCP)
        - You may search the web; cite every source you use in sources[] with its real URL and publishedAt date.
        - When done, call submit_insight with job_id={id} and json = the insight object (matching the schema), nothing else.
        - If it comes back with issues, fix them and submit once more.
        """;

    static string? Question(ResearchJob job)
    {
        if (job.Kind == ResearchKinds.Ask) return job.Subject;
        if (job.Packet is not { } p) return null;
        try { return JsonNode.Parse(p)?["question"]?.GetValue<string>(); }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException) { return null; }
    }

    /// <summary>The fact packet built when the job was claimed (the queue stores only {question} before that).</summary>
    static JsonObject? StoredPacket(ResearchJob job)
    {
        if (job.Packet is not { } p) return null;
        try { return JsonNode.Parse(p) is JsonObject o && o.ContainsKey("asOf") ? o : null; }
        catch (JsonException) { return null; }
    }

    static string Rejected(string message, IReadOnlyList<InsightIssue> issues) => Serialize(new
    {
        accepted = false,
        message,
        issues = issues.Select(i => new { code = i.Code, message = i.Message }),
    });

    static string Error(string message) => Serialize(new { error = message });

    static string Serialize(object value) => JsonSerializer.Serialize(value, Json);

    static string? Trim(string? s, int max) => string.IsNullOrWhiteSpace(s) ? null : s.Trim() is { } t && t.Length > max ? t[..max] : s.Trim();

    [GeneratedRegex("^[A-Z0-9]{2,6}$")]
    private static partial Regex Ticker();
}
