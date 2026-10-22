using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Sumire.Api.Research.Runners;

public sealed record RunRequest(string Model, string Prompt, string Workspace, string SchemaPath, TimeSpan Timeout);

public enum RunnerOutcome { Success, QuotaExhausted, Timeout, Failed }

public sealed record RunnerResult(RunnerOutcome Outcome, JsonNode? Json = null, string? Error = null, DateTimeOffset? ResetAt = null)
{
    public static RunnerResult Ok(JsonNode json) => new(RunnerOutcome.Success, json);
    public static RunnerResult Fail(string error) => new(RunnerOutcome.Failed, Error: error);
}

/// <summary>One agent CLI: builds its command line and parses its stdout. Adding a CLI is one class.</summary>
public interface IResearchRunner
{
    /// <summary>Name used in RESEARCH_RUNNERS (agy, gemini-cli, claude-code).</summary>
    string Name { get; }
    /// <summary>Executable looked up on PATH.</summary>
    string Executable { get; }
    RunnerCommand BuildCommand(RunRequest request);
    RunnerResult Parse(ProcessOutput output, DateTimeOffset now);
}

/// <summary>Shared stdout parsing and quota detection for the CLI runners.</summary>
public static partial class RunnerOutput
{
    /// <summary>Common parse: timeout → quota → wrapper/error → insight JSON.</summary>
    public static RunnerResult Parse(ProcessOutput output, DateTimeOffset now, Func<JsonObject, string?> wrapperError)
    {
        if (output.TimedOut) return new RunnerResult(RunnerOutcome.Timeout, Error: "timed out; the process was killed");

        var root = TryParseObject(output.Stdout.Trim());
        var error = root is not null ? wrapperError(root) : null;
        var errorText = $"{error}\n{output.Stderr}";

        if ((output.ExitCode != 0 || error is not null) && IsQuotaError(errorText))
            return new RunnerResult(RunnerOutcome.QuotaExhausted, Error: OneLine(error ?? output.Stderr), ResetAt: ParseReset(errorText, now));
        if (error is not null) return RunnerResult.Fail(OneLine(error));

        var insight = root is not null ? Unwrap(root) : ExtractObject(output.Stdout);
        if (insight is not null) return RunnerResult.Ok(insight);
        if (output.ExitCode != 0)
            return RunnerResult.Fail($"exit code {output.ExitCode}: {OneLine(output.Stderr is { Length: > 0 } e ? e : output.Stdout)}");
        return RunnerResult.Fail("no JSON object in the output");
    }

    /// <summary>The insight object inside a CLI wrapper: structured_output, result or response (object or JSON string).</summary>
    public static JsonObject? Unwrap(JsonObject root)
    {
        if (root.ContainsKey("summary")) return root;
        foreach (var key in new[] { "structured_output", "structuredOutput", "result", "response", "output" })
        {
            switch (root[key])
            {
                case JsonObject obj:
                    return Unwrap(obj);
                case JsonValue value when value.TryGetValue<string>(out var text) && ExtractObject(text) is { } parsed:
                    return parsed;
            }
        }
        return null;
    }

    /// <summary>First JSON object in free text (handles ```json fences and chatter around it).</summary>
    public static JsonObject? ExtractObject(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var fence = FenceRegex().Match(text);
        if (fence.Success && TryParseObject(fence.Groups[1].Value) is { } fenced) return Unwrap(fenced) ?? fenced;
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start < 0 || end <= start) return null;
        var obj = TryParseObject(text[start..(end + 1)]);
        return obj is null ? null : Unwrap(obj) ?? obj;
    }

    public static JsonObject? TryParseObject(string text)
    {
        try { return JsonNode.Parse(text) as JsonObject; }
        catch (JsonException) { return null; }
    }

    /// <summary>Non-empty string value of a node, else null.</summary>
    public static string? Text(JsonNode? node) =>
        node is JsonValue v && v.TryGetValue<string>(out var s) && s.Trim() != "" ? s : null;

    public static bool IsQuotaError(string text) => QuotaRegex().IsMatch(text);

    /// <summary>"Resets in 65h3m22s" → now + duration. Null when the CLI does not say.</summary>
    public static DateTimeOffset? ParseReset(string text, DateTimeOffset now)
    {
        var m = ResetRegex().Match(text);
        if (!m.Success) return null;
        int Part(string g) => m.Groups[g].Success ? int.Parse(m.Groups[g].Value, CultureInfo.InvariantCulture) : 0;
        var span = new TimeSpan(Part("d"), Part("h"), Part("m"), Part("s"));
        return span > TimeSpan.Zero ? now + span : null;
    }

    public static string OneLine(string text)
    {
        var line = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? "";
        return line.Length > 300 ? line[..300] + "…" : line;
    }

    [GeneratedRegex(@"quota|rate[ -]?limit|\b429\b|credits?\b|exhausted|usage limit|hit your limit|too many requests", RegexOptions.IgnoreCase)]
    private static partial Regex QuotaRegex();

    [GeneratedRegex(@"resets?\s+in\s+(?:(?<d>\d+)d)?\s*(?:(?<h>\d+)h)?\s*(?:(?<m>\d+)m(?!s))?\s*(?:(?<s>\d+)s)?", RegexOptions.IgnoreCase)]
    private static partial Regex ResetRegex();

    [GeneratedRegex(@"```(?:json)?\s*(\{.*?\})\s*```", RegexOptions.Singleline)]
    private static partial Regex FenceRegex();
}
