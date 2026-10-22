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
            // npm shims (gemini.cmd) need cmd.exe; arguments for these runners are simple flags.
            psi.FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe";
            psi.ArgumentList.Add("/d");
            psi.ArgumentList.Add("/c");
            psi.ArgumentList.Add(exe);
        }
        else
        {
            psi.FileName = exe;
        }
        foreach (var arg in command.Arguments) psi.ArgumentList.Add(arg);

        var watch = Stopwatch.StartNew();
        using var process = new Process { StartInfo = psi };
        process.Start();
        var stdout = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var stderr = process.StandardError.ReadToEndAsync(CancellationToken.None);
        try
        {
            if (command.Stdin is not null) await process.StandardInput.WriteAsync(command.Stdin);
            process.StandardInput.Close();
        }
        catch (IOException) { } // the process exited before reading stdin

        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(timeout);
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
            ct.ThrowIfCancellationRequested();
        }

        return new ProcessOutput(timedOut ? -1 : process.ExitCode, await stdout, await stderr, timedOut, watch.Elapsed);
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
