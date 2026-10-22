using System.Diagnostics;

namespace Sumire.Api.Research;

/// <summary>`Sumire install-schedule` / `uninstall-schedule`: Windows Task Scheduler entries for `research --once`.</summary>
public static class ScheduleInstaller
{
    public sealed record ScheduledTask(string Name, DayOfWeek[] Days, TimeOnly WibTime, string Description);

    static readonly DayOfWeek[] Weekdays = [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday];

    /// <summary>Holidays are skipped by the app itself (research --once finds no batch due).</summary>
    public static readonly ScheduledTask[] Tasks =
    [
        new("SumireResearchMorning", Weekdays, ResearchSchedule.MorningAt, "morning brief"),
        new("SumireEod", Weekdays, new TimeOnly(16, 35), "post-close catch-up of pending research"),
        new("SumireResearchClose", Weekdays, ResearchSchedule.CloseAt, "closing recap and sector/watchlist insights"),
        new("SumireResearchWeekend", [DayOfWeek.Sunday], ResearchSchedule.WeekendAt, "week-ahead brief"),
    ];

    public static async Task<int> InstallAsync(IServiceProvider services, string[] args, CancellationToken ct)
    {
        if (!OperatingSystem.IsWindows())
        {
            Console.Error.WriteLine("install-schedule: Windows Task Scheduler is only available on Windows; use cron with `Sumire research --once`.");
            return 1;
        }
        var exe = ExePath();
        var offset = TimeZoneInfo.Local.GetUtcOffset(DateTime.UtcNow);
        var failures = 0;
        foreach (var task in Tasks)
        {
            var (time, days) = ToLocal(task.WibTime, task.Days, offset);
            var code = await SchtasksAsync(CreateArgs(task, exe, time, days), ct);
            if (code == 0)
                Console.WriteLine($"created {task.Name,-22} {string.Join(",", days.Select(Abbrev))} {time:HH:mm} local ({task.WibTime:HH:mm} WIB): {task.Description}");
            else failures++;
        }
        Console.WriteLine($"Each task runs: \"{exe}\" research --once");
        if (offset != TimeSpan.FromHours(7))
            Console.WriteLine($"Note: this PC is on UTC{offset:+hh\\:mm;-hh\\:mm}; times were converted from WIB.");
        Console.WriteLine("Tasks run while you are logged on. Remove them with: Sumire uninstall-schedule");
        return failures == 0 ? 0 : 1;
    }

    public static async Task<int> UninstallAsync(IServiceProvider services, string[] args, CancellationToken ct)
    {
        if (!OperatingSystem.IsWindows())
        {
            Console.Error.WriteLine("uninstall-schedule: only available on Windows.");
            return 1;
        }
        foreach (var task in Tasks)
        {
            var code = await SchtasksAsync(["/Delete", "/TN", task.Name, "/F"], ct, quiet: true);
            Console.WriteLine(code == 0 ? $"removed {task.Name}" : $"{task.Name} was not installed");
        }
        return 0;
    }

    public static List<string> CreateArgs(ScheduledTask task, string exe, TimeOnly localTime, DayOfWeek[] localDays) =>
    [
        "/Create", "/F",
        "/TN", task.Name,
        "/TR", $"\"{exe}\" research --once",
        "/SC", "WEEKLY",
        "/D", string.Join(",", localDays.Select(Abbrev)),
        "/ST", localTime.ToString("HH:mm"),
        "/RL", "LIMITED",
    ];

    /// <summary>WIB wall time → this PC's local time, shifting the weekdays when it crosses midnight.</summary>
    public static (TimeOnly Time, DayOfWeek[] Days) ToLocal(TimeOnly wib, DayOfWeek[] days, TimeSpan localOffset)
    {
        var minutes = wib.Hour * 60 + wib.Minute + (int)(localOffset - TimeSpan.FromHours(7)).TotalMinutes;
        var dayShift = (int)Math.Floor(minutes / 1440.0);
        minutes -= dayShift * 1440;
        var shifted = days.Select(d => (DayOfWeek)(((int)d + dayShift % 7 + 7) % 7)).ToArray();
        return (new TimeOnly(minutes / 60, minutes % 60), shifted);
    }

    static string Abbrev(DayOfWeek d) => d.ToString()[..3].ToUpperInvariant();

    /// <summary>The apphost exe (Sumire.exe), also when started through `dotnet run`.</summary>
    static string ExePath()
    {
        var path = Environment.ProcessPath ?? "";
        if (Path.GetFileNameWithoutExtension(path).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            path = Path.Combine(AppContext.BaseDirectory, "Sumire.exe");
        return path;
    }

    static async Task<int> SchtasksAsync(IEnumerable<string> args, CancellationToken ct, bool quiet = false)
    {
        var psi = new ProcessStartInfo("schtasks.exe")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEndAsync(ct);
        var stderr = p.StandardError.ReadToEndAsync(ct);
        await p.WaitForExitAsync(ct);
        if (p.ExitCode != 0 && !quiet) Console.Error.WriteLine((await stderr).Trim() is { Length: > 0 } e ? e : (await stdout).Trim());
        return p.ExitCode;
    }
}
