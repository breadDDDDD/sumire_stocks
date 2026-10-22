using System.Text.Json.Nodes;

namespace Sumire.Api.Research.Runners;

/// <summary>
/// Antigravity CLI (agy 1.4.x). `-p` takes the prompt as its value (it does not read the prompt from stdin), so the
/// prompt goes in the argument; packets are kept small enough for the Windows command-line limit.
/// stdout: {"status":"SUCCESS|ERROR","response":...,"error":"...","structured_output"?:...}; quota → exit 3 + RESOURCE_EXHAUSTED.
/// </summary>
public sealed class AgyRunner : IResearchRunner
{
    /// <summary>Prompt + packet limit, well inside the 32k Windows command line.</summary>
    public const int MaxPromptChars = 24_000;

    public string Name => "agy";
    public string Executable => "agy";

    public RunnerCommand BuildCommand(RunRequest r)
    {
        if (r.Prompt.Length > MaxPromptChars)
            throw new ArgumentException($"agy prompt is {r.Prompt.Length} chars; the limit is {MaxPromptChars}");
        var args = new List<string>();
        if (r.Model != "") args.AddRange(["--model", r.Model]);
        args.AddRange([
            "--output-format", "json",
            "--json-schema", r.SchemaPath,
            "--disable-slash-commands",
            "--print-timeout", $"{Math.Max(30, (int)r.Timeout.TotalSeconds - 15)}s",
            "-p", r.Prompt,
        ]);
        return new RunnerCommand(Executable, args, Stdin: null, r.Workspace);
    }

    public RunnerResult Parse(ProcessOutput output, DateTimeOffset now) =>
        RunnerOutput.Parse(output, now, root =>
        {
            var error = root["error"] is JsonValue v && v.TryGetValue<string>(out var s) && s.Trim() != "" ? s : null;
            var failed = root["status"] is JsonValue st && st.TryGetValue<string>(out var status)
                && status.Equals("ERROR", StringComparison.OrdinalIgnoreCase);
            return failed ? error ?? "agy reported an error" : error;
        });
}

/// <summary>Gemini CLI: `gemini -p ... --output-format json`; stdout {"response": "...", "error": {message}}. Prompt on stdin.</summary>
public sealed class GeminiCliRunner : IResearchRunner
{
    public string Name => "gemini-cli";
    public string Executable => "gemini";

    public RunnerCommand BuildCommand(RunRequest r)
    {
        var args = new List<string>();
        if (r.Model != "") args.AddRange(["--model", r.Model]);
        // Gemini CLI appends stdin to the -p text. It has no --json-schema flag; the prompt carries the schema.
        args.AddRange(["--output-format", "json", "-p", "Follow the research instructions given on stdin and reply with the JSON object only."]);
        return new RunnerCommand(Executable, args, r.Prompt + "\n\nINSIGHT JSON SCHEMA\n" + InsightSchema.Minified, r.Workspace);
    }

    public RunnerResult Parse(ProcessOutput output, DateTimeOffset now) =>
        RunnerOutput.Parse(output, now, root => root["error"] switch
        {
            JsonObject e => RunnerOutput.Text(e["message"]) ?? e.ToJsonString(),
            JsonValue v => RunnerOutput.Text(v),
            _ => null,
        });
}

/// <summary>
/// Claude Code headless: `claude -p --output-format json --json-schema '{...}'`, prompt on stdin, tools limited to web search/fetch.
/// stdout {"type":"result","is_error":bool,"result":"...","structured_output":{...}}.
/// </summary>
public sealed class ClaudeCodeRunner : IResearchRunner
{
    public string Name => "claude-code";
    public string Executable => "claude";

    public RunnerCommand BuildCommand(RunRequest r)
    {
        var args = new List<string> { "-p", "--output-format", "json", "--json-schema", InsightSchema.Minified };
        if (r.Model != "") args.AddRange(["--model", r.Model]);
        args.AddRange(["--tools", "WebSearch,WebFetch", "--allowedTools", "WebSearch,WebFetch", "--strict-mcp-config"]);
        return new RunnerCommand(Executable, args, r.Prompt, r.Workspace);
    }

    public RunnerResult Parse(ProcessOutput output, DateTimeOffset now) =>
        RunnerOutput.Parse(output, now, root =>
            root["is_error"] is JsonValue v && v.TryGetValue<bool>(out var isError) && isError
                ? RunnerOutput.Text(root["result"]) ?? RunnerOutput.Text(root["subtype"]) ?? "claude reported an error"
                : null);
}
