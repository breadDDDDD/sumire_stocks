using System.Diagnostics;
using System.Text;

namespace Sumire.Api.Research.Runners;

public sealed record RunnerCommand(string FileName, IReadOnlyList<string> Arguments, string? Stdin, string WorkingDirectory);

public sealed record ProcessOutput(int ExitCode, string Stdout, string Stderr, bool TimedOut, TimeSpan Duration = default);

/// <summary>Runs a CLI headless: stdin in, stdout/stderr captured, killed (whole tree) on timeout.</summary>
public static class ProcessRunner
{
    public static async Task<ProcessOutput> RunAsync(RunnerCommand command, TimeSpan timeout, CancellationToken ct)
    {
        var exe = ResolveExecutable(command.FileName)
            ?? throw new FileNotFoundException($"'{command.FileName}' was not found on PATH");

        var psi = new ProcessStartInfo
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = command.WorkingDirectory,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            StandardInputEncoding = new UTF8Encoding(false),
        };
        if (exe.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase) || exe.EndsWith(".bat", StringComparison.OrdinalIgnoreCase))
        {
            // npm shims (gemini.cmd) need cmd.exe, which re-parses the line: /s keeps a quoted path with spaces intact.
            psi.FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe";
            psi.Arguments = $"/d /s /c \"{CmdLine(exe, command.Arguments)}\"";
        }
        else
        {
            psi.FileName = exe;
            foreach (var arg in command.Arguments) psi.ArgumentList.Add(arg);
        }

        var watch = Stopwatch.StartNew();
        using var process = new Process { StartInfo = psi };
        process.Start();
        var stdout = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var stderr = process.StandardError.ReadToEndAsync(CancellationToken.None);

        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(timeout);
        // Not awaited before the wait: a CLI that never reads a large stdin would block the write past the timeout.
        // Killing the process breaks the pipe, which ends the write.
        var stdin = WriteStdinAsync(process, command.Stdin);
        var timedOut = false;
        try
        {
            await process.WaitForExitAsync(limit.Token);
        }
        catch (OperationCanceledException)
        {
            timedOut = !ct.IsCancellationRequested;
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            await process.WaitForExitAsync(CancellationToken.None);
            await stdin;
            ct.ThrowIfCancellationRequested();
        }
        await stdin;

        return new ProcessOutput(timedOut ? -1 : process.ExitCode, await stdout, await stderr, timedOut, watch.Elapsed);
    }

    /// <summary>
    /// Command line for a batch file run through cmd.exe. cmd.exe expands %VAR% and treats &amp; | &lt; &gt; ^ as operators
    /// even inside quotes it has lost track of, and cannot pass line breaks, so such arguments (e.g. a prompt with
    /// headline text) are refused rather than risk running something else; such runners must take the prompt on stdin.
    /// </summary>
    internal static string CmdLine(string exe, IEnumerable<string> arguments)
    {
        var parts = new List<string> { Quote(exe) };
        foreach (var arg in arguments)
        {
            if (arg.IndexOfAny(CmdUnsafe) >= 0)
                throw new ArgumentException($"'{Path.GetFileName(exe)}' is a batch file; an argument contains characters cmd.exe would interpret");
            parts.Add(Quote(arg));
        }
        return string.Join(' ', parts);
    }

    static readonly char[] CmdUnsafe = ['%', '&', '|', '<', '>', '^', '!', '\r', '\n'];

    /// <summary>Quotes one argument the way the C runtime splits a command line.</summary>
    static string Quote(string arg)
    {
        if (arg.Length > 0 && arg.IndexOfAny([' ', '\t', '"']) < 0) return arg;
        var sb = new StringBuilder("\"");
        var backslashes = 0;
        foreach (var c in arg)
        {
            if (c == '\\') { backslashes++; continue; }
            sb.Append('\\', c == '"' ? backslashes * 2 + 1 : backslashes).Append(c);
            backslashes = 0;
        }
        return sb.Append('\\', backslashes * 2).Append('"').ToString();
    }

    static async Task WriteStdinAsync(Process process, string? text)
    {
        try
        {
            if (text is not null) await process.StandardInput.WriteAsync(text);
            process.StandardInput.Close();
        }
        catch (IOException) { } // the process exited (or was killed) before reading all of stdin
    }

    /// <summary>Full path of a command on PATH (honouring PATHEXT on Windows), or null.</summary>
    public static string? ResolveExecutable(string name)
    {
        if (Path.IsPathRooted(name)) return File.Exists(name) ? name : null;
        var extensions = OperatingSystem.IsWindows()
            ? (Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE;.CMD;.BAT").Split(';', StringSplitOptions.RemoveEmptyEntries)
            : [""];
        if (Path.HasExtension(name)) extensions = ["", .. extensions];
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            foreach (var ext in extensions)
            {
                var candidate = Path.Combine(dir.Trim('"'), name + ext);
                if (File.Exists(candidate)) return candidate;
            }
        return null;
    }
}
