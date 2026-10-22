using Sumire.Api.Common;
using Sumire.Api.Research;
using Sumire.Api.Research.Runners;

namespace Sumire.Api.Tests.Research;

static class ResearchFixtures
{
    static readonly string Dir = Path.Combine(
        AppPaths.FindUpward(AppContext.BaseDirectory, d => File.Exists(Path.Combine(d, "Sumire.Api.Tests.csproj")))!,
        "Fixtures", "Research");

    public static string Read(string name) => File.ReadAllText(Path.Combine(Dir, name));

    public static ProcessOutput Output(string stdoutFile, int exitCode = 0, string? stderrFile = null) =>
        new(exitCode, Read(stdoutFile), stderrFile is null ? "" : Read(stderrFile), TimedOut: false);
}

public class RunnerTests
{
    static readonly DateTimeOffset Now = new(2026, 10, 22, 16, 50, 0, TimeSpan.FromHours(7));
    static readonly RunRequest Request = new("gemini-3.8-flash-medium", "PROMPT", @"C:\ws", @"C:\ws\insight.schema.json", TimeSpan.FromMinutes(4));

    [Fact]
    public void Agy_command_passes_prompt_as_p_value_with_schema_and_no_skip_permissions()
    {
        var cmd = new AgyRunner().BuildCommand(Request);

        Assert.Equal("agy", cmd.FileName);
        Assert.Equal(@"C:\ws", cmd.WorkingDirectory);
        Assert.Null(cmd.Stdin);
        var args = cmd.Arguments.ToList();
        Assert.Equal("gemini-3.8-flash-medium", args[args.IndexOf("--model") + 1]);
        Assert.Equal("json", args[args.IndexOf("--output-format") + 1]);
        Assert.Equal(@"C:\ws\insight.schema.json", args[args.IndexOf("--json-schema") + 1]);
        Assert.Equal("PROMPT", args[args.IndexOf("-p") + 1]);
        Assert.Equal("-p", args[^2]);
        Assert.DoesNotContain("--dangerously-skip-permissions", args);
    }

    [Fact]
    public void Agy_rejects_prompts_over_the_command_line_budget()
    {
        var big = Request with { Prompt = new string('x', AgyRunner.MaxPromptChars + 1) };
        Assert.Throws<ArgumentException>(() => new AgyRunner().BuildCommand(big));
    }

    [Fact]
    public void Claude_command_uses_stdin_inline_schema_and_web_tools_only()
    {
        var cmd = new ClaudeCodeRunner().BuildCommand(Request with { Model = "sonnet" });

        Assert.Equal("claude", cmd.FileName);
        Assert.Equal("PROMPT", cmd.Stdin);
        var args = cmd.Arguments.ToList();
        Assert.Equal("-p", args[0]);
        Assert.Equal("sonnet", args[args.IndexOf("--model") + 1]);
        Assert.StartsWith("{", args[args.IndexOf("--json-schema") + 1]);
        Assert.Equal("WebSearch,WebFetch", args[args.IndexOf("--allowedTools") + 1]);
        Assert.DoesNotContain("--dangerously-skip-permissions", args);
    }

    [Fact]
    public void Gemini_command_sends_prompt_and_schema_on_stdin()
    {
        var cmd = new GeminiCliRunner().BuildCommand(Request);
        Assert.Equal("gemini", cmd.FileName);
        Assert.Contains("PROMPT", cmd.Stdin);
        Assert.Contains("whatWouldChangeThisView", cmd.Stdin);
        Assert.Contains("-p", cmd.Arguments);
    }

    [Theory]
    [InlineData("agy", "agy-success.stdout.json")]       // response string with a ```json fence
    [InlineData("agy", "agy-structured.stdout.json")]    // structured_output object
    [InlineData("claude-code", "claude-success.stdout.json")]
    [InlineData("claude-code", "claude-wrapped.stdout.json")] // result text with chatter around the JSON
    [InlineData("gemini-cli", "gemini-success.stdout.json")]
    public void Parses_success_outputs_into_an_insight(string runner, string fixture)
    {
        var result = Runner(runner).Parse(ResearchFixtures.Output(fixture), Now);

        Assert.Equal(RunnerOutcome.Success, result.Outcome);
        var doc = InsightDocument.TryParse(result.Json, out var error);
        Assert.Null(error);
        Assert.Equal("med", doc!.Confidence);
        Assert.Equal("s1", Assert.Single(doc.Sources).Id);
    }

    [Fact]
    public void Real_agy_quota_error_is_detected_with_reset_time()
    {
        var result = new AgyRunner().Parse(ResearchFixtures.Output("agy-quota.stdout.json", 3, "agy-quota.stderr.txt"), Now);

        Assert.Equal(RunnerOutcome.QuotaExhausted, result.Outcome);
        Assert.Contains("quota", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(Now + new TimeSpan(65, 3, 22), result.ResetAt);
    }

    [Fact]
    public void Claude_usage_limit_is_quota_exhaustion()
    {
        var result = new ClaudeCodeRunner().Parse(ResearchFixtures.Output("claude-limit.stdout.json", 1), Now);
        Assert.Equal(RunnerOutcome.QuotaExhausted, result.Outcome);
        Assert.Equal(Now + new TimeSpan(2, 30, 0), result.ResetAt);
    }

    [Fact]
    public void Gemini_429_is_quota_exhaustion_without_reset_time()
    {
        var result = new GeminiCliRunner().Parse(ResearchFixtures.Output("gemini-quota.stdout.json", 1), Now);
        Assert.Equal(RunnerOutcome.QuotaExhausted, result.Outcome);
        Assert.Null(result.ResetAt);
        Assert.Equal(new DateTimeOffset(2026, 10, 23, 0, 0, 0, TimeSpan.FromHours(7)), ResearchBudget.ExhaustedUntil(Now, result.ResetAt));
    }

    [Fact]
    public void Malformed_output_fails()
    {
        var result = new AgyRunner().Parse(ResearchFixtures.Output("malformed.stdout.txt"), Now);
        Assert.Equal(RunnerOutcome.Failed, result.Outcome);
    }

    [Fact]
    public void Non_json_crash_reports_exit_code_and_stderr()
    {
        var result = new AgyRunner().Parse(new ProcessOutput(2, "", "flag needs an argument: -p\nUsage of agy.exe:", false), Now);
        Assert.Equal(RunnerOutcome.Failed, result.Outcome);
        Assert.Equal("exit code 2: flag needs an argument: -p", result.Error);
    }

    [Fact]
    public void Timeout_wins_over_partial_output()
    {
        var result = new ClaudeCodeRunner().Parse(new ProcessOutput(-1, "{\"partial\":", "", TimedOut: true), Now);
        Assert.Equal(RunnerOutcome.Timeout, result.Outcome);
    }

    [Fact]
    public void Non_quota_wrapper_error_is_a_plain_failure()
    {
        var result = new AgyRunner().Parse(new ProcessOutput(1, "{\"status\":\"ERROR\",\"error\":\"model not found\"}", "", false), Now);
        Assert.Equal(RunnerOutcome.Failed, result.Outcome);
        Assert.Equal("model not found", result.Error);
    }

    [Fact]
    public async Task Process_runner_kills_on_timeout()
    {
        if (!OperatingSystem.IsWindows()) return;
        var cmd = new RunnerCommand("powershell.exe", ["-NoProfile", "-Command", "Start-Sleep -Seconds 20"], null, Path.GetTempPath());
        var output = await ProcessRunner.RunAsync(cmd, TimeSpan.FromMilliseconds(500), CancellationToken.None);
        Assert.True(output.TimedOut);
        Assert.True(output.Duration < TimeSpan.FromSeconds(15));
    }

    [Fact]
    public async Task Process_runner_feeds_stdin_and_captures_stdout()
    {
        if (!OperatingSystem.IsWindows()) return;
        var cmd = new RunnerCommand("findstr.exe", ["x"], "abc\nxyz\n", Path.GetTempPath());
        var output = await ProcessRunner.RunAsync(cmd, TimeSpan.FromSeconds(20), CancellationToken.None);
        Assert.Equal(0, output.ExitCode);
        Assert.Equal("xyz", output.Stdout.Trim());
    }

    static IResearchRunner Runner(string name) => name switch
    {
        "agy" => new AgyRunner(),
        "claude-code" => new ClaudeCodeRunner(),
        _ => new GeminiCliRunner(),
    };
}
